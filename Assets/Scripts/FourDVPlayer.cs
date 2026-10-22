using System.Collections;
using System.Collections.Generic;
using System.IO;
using GaussianSplat4D.Runtime;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;

// Plays 4DV.ai volumetric clips one at a time. Each clip is a child GameObject with a GaussianSplat4DSource;
// only the active one renders. Clips whose frame data isn't on disk are skipped, so a release can ship its
// clips in more than one zip.
//
//   Space pause/play · ← → previous/next clip · ↑ ↓ speed · R restart · L loop mode · Enter next clip
//
// Next to each clip's frame data FourDVImport may have saved audio.mp3 (played in sync) and view.json
// (4DV's suggested camera, applied to FourDVOrbit when the clip is selected).
public class FourDVPlayer : MonoBehaviour
{
    public enum LoopMode { LoopClip, PlayAll, Once }

    public GaussianSplat4DSource[] clips;
    public FourDVOrbit orbit;
    public float playbackSpeed = 1f;
    public LoopMode loop = LoopMode.LoopClip;
    public string startClip = "dance_sample.c";

    public static readonly float[] Speeds = { 0.25f, 0.5f, 1f, 1.5f, 2f };

    readonly List<GaussianSplat4DSource> m_Available = new List<GaussianSplat4DSource>();
    int m_Index;
    float m_Time;
    bool m_Paused;
    AudioSource m_Audio;
    readonly Dictionary<string, AudioClip> m_AudioCache = new Dictionary<string, AudioClip>();

    public IReadOnlyList<GaussianSplat4DSource> Available => m_Available;
    public int Index => m_Index;
    public float Time => m_Time;
    public float Duration => Current != null ? Mathf.Max(Current.GSplat4DAsset.totalDuration, 0.001f) : 1f;
    public bool Paused => m_Paused;
    public GaussianSplat4DSource Current => m_Available.Count > 0 ? m_Available[m_Index] : null;

    // "Kat_Miche_10s_150w" -> "Kat Miche", "badminton_10s" -> "Badminton": drop 4DV's duration/size/rig tags.
    public static string DisplayName(GaussianSplat4DSource s)
    {
        var words = new List<string>();
        foreach (var t in s.GSplat4DAsset.name.Replace('.', '_').Split('_'))
        {
            if (t.Length == 0 || t == "c" || t == "mvc" || t == "high") continue;
            if (System.Text.RegularExpressions.Regex.IsMatch(t, @"^\d+[sw]$")) continue;
            words.Add(char.ToUpperInvariant(t[0]) + t.Substring(1));
        }
        return words.Count > 0 ? string.Join(" ", words) : s.GSplat4DAsset.name;
    }

    void Awake()
    {
        m_Audio = gameObject.AddComponent<AudioSource>();
        m_Audio.playOnAwake = false;
        m_Audio.loop = false;
    }

    void Start()
    {
        foreach (var c in clips)
        {
            if (c == null) continue;
            c.gameObject.SetActive(false);
            if (Directory.Exists(DataDir(c))) m_Available.Add(c);
            else Debug.Log($"[4DV] Skipping {c.GSplat4DAsset.name}: no frame data at {DataDir(c)}");
        }
        ConfigureRenderer();
        if (s_Debug) LogRenderSettings();

        // `-4dv-clip N` (0-based) opens on that clip instead of startClip.
        var args = System.Environment.GetCommandLineArgs();
        int a = System.Array.IndexOf(args, "-4dv-clip");
        if (a >= 0 && a + 1 < args.Length && int.TryParse(args[a + 1], out int n)) Select(n);
        else Select(Mathf.Max(0, m_Available.FindIndex(c => c.GSplat4DAsset.name == startClip)));
    }

    static readonly bool s_Debug = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-4dv-debug") >= 0;
    // The renderer feature's splat pool (4,194,304 in the v1.0.0 build) is the most splats it draws in a
    // frame, and Badminton, Corridor, Horse Walk, Neemo and Unitree have 4.8-6.5M per segment: the rest
    // were dropped. Grow it to the largest clip (power of two; 64 bytes a splat). The manager's opacity cut
    // of 0.04 also dropped the faint splats a 4D clip fades through; 0.01 keeps them
    // (`-4dv-opacity-cut x` overrides).
    void ConfigureRenderer()
    {
        int need = 0;
        foreach (var c in clips) if (c != null) need = Mathf.Max(need, c.GSplat4DAsset.maxSplatCount);
        int pool = Mathf.NextPowerOfTwo(Mathf.Max(need, 1 << 20));

        const System.Reflection.BindingFlags all = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
        if (rp?.GetType().GetField("m_RendererDataList", all)?.GetValue(rp) is System.Array list)
            foreach (var rd in list)
                if (rd is UnityEngine.Rendering.Universal.ScriptableRendererData data)
                    foreach (var feat in data.rendererFeatures)
                    {
                        // GaussianSplat4DFeature is internal to the plugin, so this goes through reflection.
                        var t = feat?.GetType();
                        var size = t?.GetField("splatPoolSize", all);
                        if (t?.Name != "GaussianSplat4DFeature" || size == null) continue;
                        int current = (int)size.GetValue(feat);
                        // 16-bit blending target: thousands of faint splats stack per pixel, and 8 bits band them.
                        var hdr = t.GetField("overrideNativeFramebufferFormat", all);
                        bool wantHdr = ArgFloat("-4dv-hdr", 1f) > 0f;
                        if (current >= pool && (hdr == null || (bool)hdr.GetValue(feat) == wantHdr)) continue;
                        Debug.Log($"[4DV] Splat pool {current:N0} -> {pool:N0} (largest clip {need:N0} splats), 16-bit blend {wantHdr}");
                        t.GetMethod("ReleaseRuntimeResources", all, null, System.Type.EmptyTypes, null)?.Invoke(feat, null);
                        size.SetValue(feat, Mathf.Max(current, pool));
                        hdr?.SetValue(feat, wantHdr);
                        feat.Create();
                    }

        // The compute shader also drops any splat narrower (2 sigma) than SplatCullTanThreshold x its distance.
        // 4DV's 0.001 (0.057 degrees) suits a 100-degree headset; this panel's views span about 20 degrees in
        // 1200 rows, so it threw away everything under about 3.4 pixels: hair, fur, edges, thin props. Half a
        // pixel here is 0.00015 (`-4dv-size-cut x` overrides).
        var mgr = FindAnyObjectByType<GaussianSplat4DManager>();
        if (mgr != null)
        {
            mgr.SplatOpacityThreshold = ArgFloat("-4dv-opacity-cut", 0.01f);
            mgr.SplatCullTanThreshold = ArgFloat("-4dv-size-cut", 0.00015f);
        }
    }

    static float ArgFloat(string name, float fallback)
    {
        var args = System.Environment.GetCommandLineArgs();
        int a = System.Array.IndexOf(args, name);
        return a >= 0 && a + 1 < args.Length &&
               float.TryParse(args[a + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : fallback;
    }

    // Dump the 4DV plugin's quality settings (renderer feature, manager, each clip) to Player.log.
    void LogRenderSettings()
    {
        const System.Reflection.BindingFlags all = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        string Dump(object o)
        {
            var sb = new System.Text.StringBuilder(o.GetType().Name);
            foreach (var f in o.GetType().GetFields(all))
                if (f.FieldType.IsPrimitive || f.FieldType.IsEnum) sb.Append($" {f.Name}={f.GetValue(o)}");
            return sb.ToString();
        }
        var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
        var list = rp?.GetType().GetField("m_RendererDataList", all)?.GetValue(rp) as System.Array;
        if (list != null)
            foreach (var rd in list)
                if (rd is UnityEngine.Rendering.Universal.ScriptableRendererData data)
                    foreach (var feat in data.rendererFeatures)
                        if (feat != null) Debug.Log($"[4DV-DBG] feature {Dump(feat)}");
        var mgr = FindAnyObjectByType<GaussianSplat4DManager>();
        if (mgr != null) Debug.Log($"[4DV-DBG] manager {Dump(mgr)}");
        foreach (var c in clips)
            if (c != null) Debug.Log($"[4DV-DBG] clip {c.GSplat4DAsset.name}: {Dump(c)} | {Dump(c.GSplat4DAsset)}");
    }

    // byteFolderPath is the editor path (Assets/StreamingAssets/GaussianAssets/<clip>); map it to the player's.
    // On Android 4DV's plugin reads from persistentDataPath instead (StreamingAssets is inside the APK, and
    // its FirstRunInstaller copies files out of it); this app's clips are pushed there over USB
    // (Tools/push-android-clips.ps1), so the APK stays small.
    static string DataDir(GaussianSplat4DSource s)
    {
        var p = s.GSplat4DAsset.byteFolderPath.Replace('\\', '/');
        const string marker = "StreamingAssets/";
        int i = p.IndexOf(marker, System.StringComparison.Ordinal);
        string root = Application.platform == RuntimePlatform.Android ? Application.persistentDataPath : Application.streamingAssetsPath;
        return i >= 0 ? Path.Combine(root, p.Substring(i + marker.Length)) : p;
    }

    // Tablets: if a clip can't hold 27 fps, draw every other splat (4DV's DensityLOD, which grows the splats
    // to cover), then every fourth. Only ever coarser within a clip, so it can't oscillate; a new clip starts
    // at full density.
    float m_FrameAvg = 1f / 60f, m_DensityHold;

    void AdaptDensity()
    {
        var src = Current;
        if (!Application.isMobilePlatform || src == null) return;
        m_FrameAvg = Mathf.Lerp(m_FrameAvg, UnityEngine.Time.unscaledDeltaTime, 0.05f);
        if (UnityEngine.Time.unscaledTime < m_DensityHold || m_FrameAvg < 1f / 27f || src.DensityLOD >= 2) return;
        src.DensityLOD++;
        m_DensityHold = UnityEngine.Time.unscaledTime + 3f;
        Debug.Log($"[4DV] {src.GSplat4DAsset.name}: {1f / m_FrameAvg:0} fps, density 1/{1 << src.DensityLOD}");
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.spaceKey.wasPressedThisFrame) TogglePause();
            if (kb.rightArrowKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame) Next();
            if (kb.leftArrowKey.wasPressedThisFrame) Previous();
            if (kb.upArrowKey.wasPressedThisFrame) StepSpeed(+1);
            if (kb.downArrowKey.wasPressedThisFrame) StepSpeed(-1);
            if (kb.rKey.wasPressedThisFrame) Seek(0f);
            if (kb.lKey.wasPressedThisFrame) CycleLoop();
        }

        var src = Current;
        if (src == null) return;
        float duration = Duration;
        if (!m_Paused) m_Time += playbackSpeed * UnityEngine.Time.deltaTime;
        if (m_Time >= duration)
        {
            switch (loop)
            {
                case LoopMode.LoopClip: m_Time %= duration; RestartAudio(); break;
                case LoopMode.PlayAll: Select(m_Index + 1); return;
                case LoopMode.Once: m_Time = duration - 0.001f; SetPaused(true); break;
            }
        }
        src.currentProgressTime = m_Time;
        SyncAudio();
        AdaptDensity();
    }

    // ---- controls (keyboard and FourDVPanel) ----

    public void Select(int index)
    {
        if (m_Available.Count == 0) return;
        if (Current != null) Current.gameObject.SetActive(false);
        m_Index = ((index % m_Available.Count) + m_Available.Count) % m_Available.Count;
        Current.gameObject.SetActive(true);
        Current.DensityLOD = 0;
        m_DensityHold = UnityEngine.Time.unscaledTime + 3f;   // loading the first segment stalls a few frames
        m_FrameAvg = 1f / 60f;
        m_Time = 0f;
        Debug.Log($"[4DV] Playing {Current.GSplat4DAsset.name} ({m_Index + 1}/{m_Available.Count})");

        m_Audio.Stop();
        m_Audio.clip = null;
        StopAllCoroutines();
        StartCoroutine(LoadAudio(Current));
        ApplyView(Current);
        if (orbit != null) orbit.SetDepthSamples(DepthSamples(Current));
    }

    // Centres of the clip's 256-splat chunks in world space, for FourDVOrbit's stereo depth budget. The
    // first segment's <clip>_<n>_chunk.bytes holds 28 floats per chunk, starting with min xyz, max xyz; the
    // plugin negates z when it unpacks positions (GaussianSplat4D.hlsl, UnpackChunk), so this does too.
    readonly Dictionary<string, Vector3[]> m_DepthCache = new Dictionary<string, Vector3[]>();

    Vector3[] DepthSamples(GaussianSplat4DSource src)
    {
        string name = src.GSplat4DAsset.name;
        if (!m_DepthCache.TryGetValue(name, out var local))
        {
            local = null;
            try
            {
                string dir = DataDir(src);
                for (int seg = 0; seg < 4 && local == null; seg++)
                {
                    string path = Path.Combine(dir, $"{name}_{seg}_chunk.bytes");
                    if (!File.Exists(path)) continue;
                    var bytes = File.ReadAllBytes(path);
                    if (bytes.Length == 0 || bytes.Length % 112 != 0) break;
                    var f = new float[bytes.Length / 4];
                    System.Buffer.BlockCopy(bytes, 0, f, 0, bytes.Length);
                    local = new Vector3[f.Length / 28];
                    for (int c = 0; c < local.Length; c++)
                    {
                        int b = c * 28;
                        local[c] = new Vector3(f[b] + f[b + 3], f[b + 1] + f[b + 4], -(f[b + 2] + f[b + 5])) * 0.5f;
                    }
                }
            }
            catch (System.Exception e) { Debug.LogWarning($"[4DV] {name}: no depth samples ({e.Message})"); }
            m_DepthCache[name] = local;
        }
        if (local == null) return null;
        var m = src.transform.localToWorldMatrix;
        var world = new Vector3[local.Length];
        for (int i = 0; i < local.Length; i++) world[i] = m.MultiplyPoint3x4(local[i]);
        return world;
    }

    public void Next() => Select(m_Index + 1);
    public void Previous() => Select(m_Index - 1);
    public void TogglePause() => SetPaused(!m_Paused);

    public void SetPaused(bool paused)
    {
        m_Paused = paused;
        if (paused) m_Audio.Pause();
        else if (m_Audio.clip != null) { m_Audio.time = Mathf.Min(m_Time, m_Audio.clip.length - 0.01f); m_Audio.Play(); }
    }

    public void Seek(float t)
    {
        m_Time = Mathf.Clamp(t, 0f, Duration - 0.001f);
        if (m_Audio.clip != null) m_Audio.time = Mathf.Min(m_Time, m_Audio.clip.length - 0.01f);
    }

    public void CycleLoop() => loop = (LoopMode)(((int)loop + 1) % 3);

    public void StepSpeed(int dir)
    {
        int i = System.Array.IndexOf(Speeds, playbackSpeed);
        if (i < 0) i = 2;
        playbackSpeed = Speeds[Mathf.Clamp(i + dir, 0, Speeds.Length - 1)];
    }

    public void CycleSpeed()
    {
        int i = System.Array.IndexOf(Speeds, playbackSpeed);
        playbackSpeed = Speeds[(i + 1) % Speeds.Length];
    }

    // ---- audio ----

    IEnumerator LoadAudio(GaussianSplat4DSource src)
    {
        var name = src.GSplat4DAsset.name;
        if (!m_AudioCache.TryGetValue(name, out var clip))
        {
            var path = Path.Combine(DataDir(src), "audio.mp3");
            if (!File.Exists(path)) yield break;
            using var req = UnityWebRequestMultimedia.GetAudioClip(new System.Uri(path).AbsoluteUri, AudioType.MPEG);
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success) { Debug.LogWarning($"[4DV] {name} audio: {req.error}"); yield break; }
            clip = DownloadHandlerAudioClip.GetContent(req);
            m_AudioCache[name] = clip;
        }
        if (Current != src) yield break;
        m_Audio.clip = clip;
        if (!m_Paused) { m_Audio.time = Mathf.Min(m_Time, clip.length - 0.01f); m_Audio.Play(); }
    }

    void RestartAudio()
    {
        if (m_Audio.clip == null || m_Paused) return;
        m_Audio.time = Mathf.Min(m_Time, m_Audio.clip.length - 0.01f);
        if (!m_Audio.isPlaying) m_Audio.Play();
    }

    void SyncAudio()
    {
        if (m_Audio.clip == null) return;
        m_Audio.pitch = playbackSpeed;
        if (m_Paused) return;
        if (m_Time >= m_Audio.clip.length) { if (m_Audio.isPlaying) m_Audio.Stop(); return; }
        if (!m_Audio.isPlaying) { m_Audio.time = m_Time; m_Audio.Play(); }
        else if (Mathf.Abs(m_Audio.time - m_Time) > 0.15f) m_Audio.time = m_Time;
    }

    // ---- per-clip view from 4DV's settings.json ----

    [System.Serializable] class ViewCamera { public float fov = 50f; public float[] position; public float[] target; }
    [System.Serializable] class ViewSettings { public ViewCamera camera; }
    [System.Serializable] class ViewOverride { public string clip; public float[] pivot; public float yaw, pitch, height; }
    [System.Serializable] class ViewOverrides { public ViewOverride[] views; }

    static Dictionary<string, ViewOverride> s_Overrides;

    static ViewOverride FindOverride(string clip)
    {
        if (s_Overrides == null)
        {
            s_Overrides = new Dictionary<string, ViewOverride>();
            var text = Resources.Load<TextAsset>("4dv-views");
            var parsed = text != null ? JsonUtility.FromJson<ViewOverrides>(text.text) : null;
            if (parsed?.views != null)
                foreach (var v in parsed.views) s_Overrides[v.clip] = v;
            // StreamingAssets/4dv-views.json (same format) wins, so an installed copy can be re-framed
            // without a rebuild.
            var path = Path.Combine(Application.streamingAssetsPath, "4dv-views.json");
            try
            {
                var local = File.Exists(path) ? JsonUtility.FromJson<ViewOverrides>(File.ReadAllText(path)) : null;
                if (local?.views != null)
                    foreach (var v in local.views) s_Overrides[v.clip] = v;
            }
            catch (System.Exception e) { Debug.LogWarning($"[4DV] {path}: {e.Message}"); }
        }
        return s_Overrides.TryGetValue(clip, out var o) ? o : null;
    }

    // Priority: -4dv-pivot on the command line (for finding values) > Resources/4dv-views.json > the clip's
    // view.json (4DV's suggested camera) > FourDVOrbit's default.
    void ApplyView(GaussianSplat4DSource src)
    {
        if (orbit == null) return;
        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-4dv-pivot") >= 0) { orbit.UseDefaultHome(); return; }
        var o = FindOverride(src.GSplat4DAsset.name);
        if (o?.pivot != null && o.pivot.Length >= 3)
        {
            orbit.SetHome(new Vector3(o.pivot[0], o.pivot[1], o.pivot[2]), o.yaw, o.pitch, o.height);
            return;
        }
        var path = Path.Combine(DataDir(src), "view.json");
        if (!File.Exists(path)) { orbit.UseDefaultHome(); return; }
        ViewSettings v = null;
        try { v = JsonUtility.FromJson<ViewSettings>(File.ReadAllText(path)); } catch { }
        var c = v?.camera;
        if (c?.position == null || c.target == null || c.position.Length < 3 || c.target.Length < 3) { orbit.UseDefaultHome(); return; }
        orbit.SetHomeFromCamera(ToUnity(c.position), ToUnity(c.target), c.fov);
    }

    // 4DV's web viewer (PlayCanvas) is right-handed; Unity is left-handed. Mirror X.
    static Vector3 ToUnity(float[] p) => new Vector3(-p[0], p[1], p[2]);
}
