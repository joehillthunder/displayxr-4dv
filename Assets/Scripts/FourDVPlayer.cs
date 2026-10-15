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
        Select(Mathf.Max(0, m_Available.FindIndex(c => c.GSplat4DAsset.name == startClip)));
    }

    // byteFolderPath is the editor path (Assets/StreamingAssets/GaussianAssets/<clip>); map it to the player's.
    static string DataDir(GaussianSplat4DSource s)
    {
        var p = s.GSplat4DAsset.byteFolderPath.Replace('\\', '/');
        const string marker = "StreamingAssets/";
        int i = p.IndexOf(marker, System.StringComparison.Ordinal);
        return i >= 0 ? Path.Combine(Application.streamingAssetsPath, p.Substring(i + marker.Length)) : p;
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
    }

    // ---- controls (keyboard and FourDVPanel) ----

    public void Select(int index)
    {
        if (m_Available.Count == 0) return;
        if (Current != null) Current.gameObject.SetActive(false);
        m_Index = ((index % m_Available.Count) + m_Available.Count) % m_Available.Count;
        Current.gameObject.SetActive(true);
        m_Time = 0f;
        Debug.Log($"[4DV] Playing {Current.GSplat4DAsset.name} ({m_Index + 1}/{m_Available.Count})");

        m_Audio.Stop();
        m_Audio.clip = null;
        StopAllCoroutines();
        StartCoroutine(LoadAudio(Current));
        ApplyView(Current);
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
            using var req = UnityWebRequestMultimedia.GetAudioClip("file:///" + path.Replace('\\', '/'), AudioType.MPEG);
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
