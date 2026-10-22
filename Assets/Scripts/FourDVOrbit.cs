using System;
using DisplayXR;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// Turntable controls for a display-centric DisplayXR rig:
//
//   Orbit Pivot (this, at the subject's centre)   yaw/pitch = orbit, position = pan
//     Main Camera (Camera + DisplayXRDisplay)      3D: on the pivot (display plane through the subject)
//                                                  2D: backed off along -Z to frame the same height
//
// Mouse:  left-drag orbit · right/middle-drag pan · wheel zoom
// Keys:   T auto-turn · Home reset view · [ ] 3D strength · V 2D/3D · F11 fullscreen · I screenshot · Esc quit
//
// With the DisplayXR provider running, the woven image is shown in a separate window that Unity's Input
// System doesn't get clicks from, so buttons come from the provider's window (displayxr_get_overlay_pointer)
// and motion from Raw Input deltas, the same approach as DisplayXR's DefaultInputController sample.
//
// Android (Nubia Pad 3D II / RedMagic 3D): DisplayXR's Android runtime is an OpenXR runtime, reached through
// Unity's OpenXR plug-in (the DisplayXR Unity package has no Android plug-in):
//
//   Orbit Pivot (this)
//     XR Space (xrSpace)                           on the pivot, scaled to scene units per metre: the panel
//       XR Camera (xrCamera)                       the runtime's tracked head pose, metres in front of it
//
// Touch:  one finger orbit · pinch zoom · two-finger drag pan · double tap reset view
public class FourDVOrbit : MonoBehaviour
{
    public DisplayXRDisplay display;
    public Camera fallbackCamera;
    public Transform xrSpace;               // Android (OpenXR): the panel's frame
    public Camera xrCamera;
    // Where DisplayXR's Android runtime puts the panel centre in its tracking space (its rig origin, a standing
    // eye height; it logs "Rig source ... armed at (0.000, 1.600, 0.000)"), and the panel's size (its
    // display_info: 0.2600 x 0.1630 m on the Nubia Pad 3D II; Android's reported 320 dpi would say 0.127 m).
    public Vector3 xrPanelOrigin = new Vector3(0f, 1.6f, 0f);
    public Vector2 xrPanelSize = new Vector2(0.26f, 0.163f);

    public static readonly Color Background = new Color(0.02f, 0.03f, 0.05f);

    [Header("View")]
    public float displayHeight = 2.0f;      // metres of scene shown top-to-bottom; smaller = zoomed in
    public float minHeight = 0.4f, maxHeight = 8f;
    public float viewDistanceRatio = 1.4f;  // 2D camera distance / display height (a comfortable desk view)

    [Header("Input")]
    public float orbitSpeed = 0.25f;        // degrees per pixel
    public float touchOrbitSpeed = 0.15f;   // degrees per pixel of finger travel
    public float zoomStep = 0.12f;          // fraction per wheel notch
    public float autoTurnSpeed = 12f;       // degrees per second

    public const float MaxStrength = 3f;
    public float Strength { get; set; } = 1f;   // times DepthFit = DisplayXRDisplay.ipdFactor: 0 = flat, 3 = exaggerated
    public bool Is3D { get; private set; } = true;
    public bool AutoTurn { get; set; }
    // Off: natural depth for every clip (Strength alone), even where the panel shows double edges.
    public bool FitDepth { get; set; } = true;

    // AR (FourDVAR): the screen turns about the viewer's eyes by ArTurn, the tablet's own turn since the clip
    // was placed, so the clip stays put in the room while the camera image behind it moves with the tablet.
    public bool ArMode { get; set; }
    public Quaternion ArTurn { get; set; } = Quaternion.identity;
    public event Action ViewReset;

    // Stereo depth budget (same rule as the web player): the largest screen disparity, as a fraction of the
    // panel's width, that the 5th- and 95th-percentile depths in view may reach at Strength 1. Past about 1%
    // the lenticular weave shows double edges (crosstalk). Scenes that surround the performer (Badminton,
    // Horse Walk, Rocket) reach 5-9x that at natural depth, so their depth is scaled down to fit, but never
    // below MinDepthShare.
    const float DisparityBudget = 0.012f;
    const float MinDepthShare = 0.25f;
    const float EyeSeparation = 0.063f;      // metres, at ipdFactor 1
    const int MaxDepthSamples = 8192;
    // 4DV's compute shader only culls splats behind the camera, and captures keep stray splats around the
    // capture rig, a few metres in front of the performer: where the display-centric rig puts the viewer's
    // eyes. Anything there gets huge crossed disparity, so the near plane sits this far (metres at the panel)
    // in front of the display; the 5th-95th percentile content of every clip stays within about 14 cm.
    // `-4dv-popout m` overrides it.
    static readonly float s_MaxPopOut = Arg("-4dv-popout", 1, 0.2f);
    float m_DefaultNear = -1f;
    public float DepthFit { get; private set; } = 1f;
    Vector3[] m_DepthSamples;
    float m_DepthTarget = 1f, m_NextDepthCheck;
    readonly System.Collections.Generic.List<float> m_Depths = new System.Collections.Generic.List<float>();

    Vector3 m_DefaultPos, m_HomePos;
    float m_DefaultHeight, m_DefaultYaw, m_DefaultPitch;
    float m_HomeHeight, m_HomeYaw, m_HomePitch;
    float m_Yaw, m_Pitch;
    int m_ButtonsCurr;
    bool m_Dragging;

    void Awake()
    {
        // Windowed weaving: open at 70% of the display rather than a fixed pixel size, which is tiny on
        // high-DPI 3D panels. F11 switches to fullscreen.
        if (Screen.fullScreenMode == FullScreenMode.Windowed)
        {
            var res = Screen.currentResolution;
            Screen.SetResolution(Mathf.RoundToInt(res.width * 0.7f), Mathf.RoundToInt(res.height * 0.7f), FullScreenMode.Windowed);
        }

        // Overrides for framing a clip without rebuilding:
        //   -4dv-pivot x y z   -4dv-yaw deg   -4dv-pitch deg   -4dv-height metres
        var p = transform.position;
        m_DefaultPos = new Vector3(Arg("-4dv-pivot", 1, p.x), Arg("-4dv-pivot", 2, p.y), Arg("-4dv-pivot", 3, p.z));
        m_DefaultYaw = Arg("-4dv-yaw", 1, transform.eulerAngles.y);
        m_DefaultPitch = Arg("-4dv-pitch", 1, 0f);
        m_DefaultHeight = Arg("-4dv-height", 1, displayHeight);
        UseDefaultHome();
        ScheduleScreenshotFromArgs();
#if UNITY_ANDROID && !UNITY_EDITOR
        // The runtime's face tracker opens the front camera from this process (DisplayXR's client template).
        if (xrSpace != null && !UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Camera);
#endif
        DisplayXRProvider.EyeTrackingStateChanged += (tracked, mode) =>
            Debug.Log($"[4DV] Eye tracking {(tracked ? "ON" : "OFF")} (mode {mode})");
    }

    // Every 5 s in Player.log: whether DisplayXR reports the viewer tracked, and the eye positions it gives
    // (metres, display space). Head tracking can then be checked from a normal launch.
    float m_NextEyeLog = 5f;
    void LogEyes()
    {
        if (UnityEngine.Time.unscaledTime < m_NextEyeLog || !ProviderActive()) return;
        m_NextEyeLog = UnityEngine.Time.unscaledTime + 5f;
        if (s_Debug)
            foreach (var mi in DisplayXRProvider.Modes)
                Debug.Log($"[4DV-DBG] mode {mi.modeIndex} '{mi.name}' views {mi.viewCount} tiles {mi.tileColumns}x{mi.tileRows} view {mi.viewWidthPx}x{mi.viewHeightPx} scale {mi.viewScaleX}x{mi.viewScaleY} hw3D {mi.hardwareDisplay3D} active {mi.isActive} requestable {mi.isRequestable}");
        bool got = DisplayXRProvider.TryGetViewerEyes(out var l, out var r);
        Debug.Log($"[4DV] Eyes tracked={DisplayXRProvider.IsEyeTracked} got={got} L={l.ToString("F3")} R={r.ToString("F3")}");
    }

    static float Arg(string name, int offset, float fallback)
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + offset < args.Length &&
               float.TryParse(args[i + offset], System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }

    // ---- home view (set per clip by FourDVPlayer) ----

    public void UseDefaultHome() => SetHome(m_DefaultPos, m_DefaultYaw, m_DefaultPitch, m_DefaultHeight);

    // From a clip's suggested camera: orbit around its target, facing from its position, framing what its
    // field of view showed at that distance.
    public void SetHomeFromCamera(Vector3 camPos, Vector3 target, float fovDeg)
    {
        var dir = target - camPos;
        float dist = dir.magnitude;
        if (dist < 0.01f) { UseDefaultHome(); return; }
        float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        float pitch = -Mathf.Asin(Mathf.Clamp(dir.y / dist, -1f, 1f)) * Mathf.Rad2Deg;
        float height = 2f * dist * Mathf.Tan(Mathf.Clamp(fovDeg, 10f, 120f) * 0.5f * Mathf.Deg2Rad);
        SetHome(target, yaw, Mathf.Clamp(pitch, -80f, 80f), Mathf.Clamp(height, minHeight, maxHeight));
    }

    public void SetHome(Vector3 pos, float yaw, float pitch, float height)
    {
        m_HomePos = pos; m_HomeYaw = yaw; m_HomePitch = pitch; m_HomeHeight = height;
        ResetView();
    }

    public void ResetView()
    {
        transform.position = m_HomePos;
        m_Yaw = m_HomeYaw;
        m_Pitch = m_HomePitch;
        displayHeight = m_HomeHeight;
        Apply();
        ViewReset?.Invoke();
    }

    // ---- 3D controls (keyboard and FourDVPanel) ----

    public void Toggle3D() => Set3D(!Is3D);

    public void Set3D(bool on)
    {
        Is3D = on;
        if (ProviderActive()) DisplayXRProvider.RequestDisplayMode(on);
    }

    public void StepStrength(float delta) => Strength = Mathf.Clamp(Strength + delta, 0f, MaxStrength);

    // World-space points spread through the clip (FourDVPlayer passes its chunk centres); null = no budget.
    public void SetDepthSamples(Vector3[] worldPoints)
    {
        m_DepthSamples = worldPoints;
        m_NextDepthCheck = 0f;
        if (s_Debug && worldPoints != null && worldPoints.Length > 0)
        {
            // Median and 10th/90th percentiles per axis: where the content is, for 4dv-views.json overrides.
            string Axis(int a)
            {
                var v = new float[worldPoints.Length];
                for (int i = 0; i < v.Length; i++) v[i] = worldPoints[i][a];
                Array.Sort(v);
                return $"{v[v.Length / 10]:0.00}/{v[v.Length / 2]:0.00}/{v[v.Length * 9 / 10]:0.00}";
            }
            Debug.Log($"[4DV-DBG] content p10/median/p90 x {Axis(0)} y {Axis(1)} z {Axis(2)}");
        }
        DepthFit = m_DepthTarget = FitDepth ? MeasureDepthFit() : 1f;
    }

    // How much of the scene's natural depth fits the budget from the current view, re-measured four times a
    // second (the view changes while orbiting) and eased so the depth never jumps.
    void UpdateDepthFit()
    {
        if (UnityEngine.Time.unscaledTime >= m_NextDepthCheck)
        {
            m_NextDepthCheck = UnityEngine.Time.unscaledTime + 0.25f;
            m_DepthTarget = FitDepth ? MeasureDepthFit() : 1f;
        }
        DepthFit = Mathf.MoveTowards(DepthFit, m_DepthTarget, UnityEngine.Time.unscaledDeltaTime);
    }

    // Physical size of the woven area (the window, scaled from the panel) and the nominal viewing distance.
    void PanelGeometry(out float panelW, out float physW, out float physH, out float viewer)
    {
        panelW = 0.3444f; float panelH = 0.2153f; viewer = 0.6f;
        physW = panelW; physH = panelH;
        if (xrSpace != null)
        {
            // The viewing distance is the tracked head's, from the panel (metres).
            physW = panelW = xrPanelSize.x;
            physH = xrPanelSize.y;
            float z = xrCamera != null ? Mathf.Abs(xrCamera.transform.localPosition.z - xrPanelOrigin.z) : 0f;
            viewer = z > 0.2f && z < 1.5f ? z : 0.45f;
            return;
        }
        if (DisplayXRProvider.TryGetDisplayInfo(out var info) && info.widthM > 0f && info.heightM > 0f)
        {
            panelW = info.widthM; panelH = info.heightM;
            if (info.nominalZ > 0.1f) viewer = info.nominalZ;
            physW = info.pixelWidth > 0 ? panelW * Mathf.Min(1f, (float)Screen.width / info.pixelWidth) : panelW;
            physH = info.pixelHeight > 0 ? panelH * Mathf.Min(1f, (float)Screen.height / info.pixelHeight) : panelH;
        }
    }

    // In 3D, clip anything more than s_MaxPopOut in front of the panel (the near plane is measured from the eye,
    // in scene units); in 2D, put the camera's own near plane back.
    void ApplyNearClip(Camera cam, bool stereo)
    {
        if (cam == null) return;
        if (m_DefaultNear < 0f) m_DefaultNear = cam.nearClipPlane;
        if (!stereo) { cam.nearClipPlane = m_DefaultNear; return; }
        PanelGeometry(out _, out _, out float physH, out float viewer);
        float scenePerMetre = Mathf.Max(displayHeight, 1e-3f) / physH;
        cam.nearClipPlane = Mathf.Max(m_DefaultNear, (viewer - s_MaxPopOut) * scenePerMetre);
    }

    // 4DV's plugin culls and depth-sorts splats in the space of camera.worldToCameraMatrix (it never reads the
    // XR per-eye matrices) and drops anything with view z >= 0.01, i.e. behind that camera. In this rig the
    // Camera object sits ON the display plane, through the middle of the performer, so everything in front
    // of the screen was culled: the near half of every clip (Kat Miche showed one arm). Point that matrix
    // from the nominal viewer instead (viewer metres in front of the panel, 0.1 m above its centre). XR
    // rendering keeps its own per-eye matrices and DisplayXR places the display from the transform, so only
    // the plugin's culling and sorting move.
    //
    // The plugin's compute shader also blurs every splat to at least 0.0006 x its distance from this point
    // (about 2 px on this panel, sized for headsets). The final image comes from the real per-eye
    // projection, so measuring from a point nearer the screen shrinks that floor: `-4dv-splat-view f` puts it
    // at f x the viewing distance (default 0.5, about 1 px; 1 = the viewer, 4DV's own blur).
    static readonly float s_SplatViewDistance = Mathf.Clamp(Arg("-4dv-splat-view", 1, 0.5f), 0.3f, 1f);

    void ApplySplatViewpoint(Camera cam, bool stereo)
    {
        if (cam == null) return;
        if (!stereo) { cam.ResetWorldToCameraMatrix(); return; }
        PanelGeometry(out _, out _, out float physH, out float viewer);
        float scenePerMetre = Mathf.Max(displayHeight, 1e-3f) / physH;
        var t = cam.transform;
        Vector3 eye = t.position - t.forward * (viewer * s_SplatViewDistance * scenePerMetre) + t.up * (0.1f * scenePerMetre);
        cam.worldToCameraMatrix = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) *
                                  Matrix4x4.TRS(eye, t.rotation, Vector3.one).inverse;
    }

    float MeasureDepthFit()
    {
        var pts = m_DepthSamples;
        if (pts == null || pts.Length == 0) return 1f;

        PanelGeometry(out float panelW, out float physW, out float physH, out float viewer);
        float k = physH / Mathf.Max(displayHeight, 1e-3f);   // scene metres -> metres at the panel

        // Depth of each point that's in view, in metres behind (+) or in front of (-) the panel.
        Vector3 o = transform.position, f = transform.forward, r = transform.right, u = transform.up;
        m_Depths.Clear();
        int step = Mathf.Max(1, pts.Length / MaxDepthSamples);
        for (int i = 0; i < pts.Length; i += step)
        {
            var q = pts[i] - o;
            float z = Vector3.Dot(q, f) * k;
            if (z < -s_MaxPopOut) continue;                     // in front of the near plane (not drawn)
            float s = viewer / (viewer + z);
            if (Mathf.Abs(Vector3.Dot(q, r) * k * s) > physW * 0.5f || Mathf.Abs(Vector3.Dot(q, u) * k * s) > physH * 0.5f)
                continue;                                     // outside the window
            m_Depths.Add(z);
        }
        if (m_Depths.Count < 16) return 1f;
        m_Depths.Sort();
        float near = m_Depths[(int)(m_Depths.Count * 0.05f)];
        float far = m_Depths[Mathf.Min(m_Depths.Count - 1, (int)(m_Depths.Count * 0.95f))];

        // Screen disparity at ipdFactor 1 for a point z metres behind the panel: e·z / (viewer + z).
        float disparity = Mathf.Max(EyeSeparation * Mathf.Max(far, 0f) / (viewer + Mathf.Max(far, 0f)),
                                    EyeSeparation * Mathf.Max(-near, 0f) / (viewer + Mathf.Min(near, 0f)));
        float fit = disparity > 1e-5f ? Mathf.Clamp(DisparityBudget * panelW / disparity, MinDepthShare, 1f) : 1f;
        if (s_Debug) Debug.Log($"[4DV-DBG] {m_Depths.Count * 100 / ((pts.Length + step - 1) / step)}% of chunks in view, depth {near * 100f:0.0}..{far * 100f:0.0} cm, disparity {disparity * 1000f:0.0} mm, fit {fit:0.00}");
        return fit;
    }

    void Update()
    {
        var kb = Keyboard.current;
        PollButtons();
        bool overUi = PointerOverUi();

        // orbit / pan: a drag that starts over the panel belongs to the panel
        if (m_ButtonsCurr == 0) m_Dragging = false;
        else if (!m_Dragging && !overUi) m_Dragging = true;

        Vector2 d = Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
        if (m_Dragging && Held(0))
        {
            m_Yaw += d.x * orbitSpeed;
            m_Pitch = Mathf.Clamp(m_Pitch - d.y * orbitSpeed, -80f, 80f);
            AutoTurn = false;
        }
        else if (m_Dragging && (Held(1) || Held(2)))
        {
            float perPixel = displayHeight / Mathf.Max(Screen.height, 1);
            transform.position += transform.rotation * new Vector3(-d.x, -d.y, 0f) * perPixel;
        }
        if (Application.isMobilePlatform) UpdateTouch();
        if (AutoTurn) m_Yaw += autoTurnSpeed * Time.deltaTime;

        // zoom
        float wheel = Mouse.current != null && !overUi ? Mouse.current.scroll.ReadValue().y : 0f;
        if (Mathf.Abs(wheel) >= 10f) wheel /= 120f;   // player reports WHEEL_DELTA units
        if (Mathf.Abs(wheel) > 0.001f)
            displayHeight = Mathf.Clamp(displayHeight * Mathf.Pow(1f - zoomStep, wheel), minHeight, maxHeight);

        if (kb != null)
        {
            if (kb.tKey.wasPressedThisFrame) AutoTurn = !AutoTurn;
            if (kb.homeKey.wasPressedThisFrame) ResetView();
            if (kb.leftBracketKey.wasPressedThisFrame) StepStrength(-0.25f);
            if (kb.rightBracketKey.wasPressedThisFrame) StepStrength(+0.25f);
            if (kb.vKey.wasPressedThisFrame) Toggle3D();
            if (kb.f11Key.wasPressedThisFrame)
            {
                bool full = Screen.fullScreenMode != FullScreenMode.Windowed;
                Screen.fullScreenMode = full ? FullScreenMode.Windowed : FullScreenMode.FullScreenWindow;
            }
            if (kb.iKey.wasPressedThisFrame) DisplayXRScreenshot.Capture();
            if (kb.escapeKey.wasPressedThisFrame) Application.Quit();
        }
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN || UNITY_STANDALONE_LINUX
        // the woven window's close button / Alt+F4 arrive as a native request
        try { if (DisplayXRNative.displayxr_consume_overlay_close_request() != 0) Application.Quit(); }
        catch (EntryPointNotFoundException) { }
        catch (DllNotFoundException) { }
#endif

        Apply();
        LogEyes();

        if (s_Debug && Time.frameCount % 120 == 0 && fallbackCamera != null)
        {
            var c = fallbackCamera.transform;
            Debug.Log($"[4DV-DBG] pivot {transform.position} yaw {m_Yaw:0} pitch {m_Pitch:0} h {displayHeight:0.00} " +
                      $"display {display?.transform.position} cam {c.position} local {c.localPosition} fov {fallbackCamera.fieldOfView:0.0} " +
                      $"btn {m_ButtonsCurr} drag {m_Dragging} overUi {PointerOverUi()}");
        }
    }

    static readonly bool s_Debug = Array.IndexOf(Environment.GetCommandLineArgs(), "-4dv-debug") >= 0;

    // ---- touch (tablet) ----

    int m_PrevTouches;
    bool m_TouchOnUi;
    Vector2 m_PinchMid;
    float m_PinchDist;

    // One finger orbits; two fingers pinch to zoom and drag to pan; a double tap resets the view. A gesture
    // that starts on the panel belongs to the panel.
    void UpdateTouch()
    {
        var ts = Touchscreen.current;
        if (ts == null) return;
        int n = 0;
        Vector2 p0 = default, p1 = default, d0 = default;
        foreach (var t in ts.touches)
        {
            if (!t.isInProgress) continue;
            if (n == 0) { p0 = t.position.ReadValue(); d0 = t.delta.ReadValue(); }
            else if (n == 1) p1 = t.position.ReadValue();
            n++;
        }
        if (n == 0) { m_PrevTouches = 0; m_TouchOnUi = false; return; }
        if (m_PrevTouches == 0) m_TouchOnUi = TouchOverUi(p0);
        if (m_TouchOnUi) { m_PrevTouches = n; return; }

        if (ts.primaryTouch.tap.wasPressedThisFrame && ts.primaryTouch.tapCount.ReadValue() >= 2) ResetView();
        if (n == 1 && m_PrevTouches == 1)
        {
            m_Yaw += d0.x * touchOrbitSpeed;
            m_Pitch = Mathf.Clamp(m_Pitch - d0.y * touchOrbitSpeed, -80f, 80f);
            if (d0.sqrMagnitude > 0f) AutoTurn = false;
        }
        else if (n >= 2)
        {
            Vector2 mid = (p0 + p1) * 0.5f;
            float dist = (p0 - p1).magnitude;
            if (m_PrevTouches >= 2 && m_PinchDist > 1f)
            {
                displayHeight = Mathf.Clamp(displayHeight * m_PinchDist / Mathf.Max(dist, 1f), minHeight, maxHeight);
                Vector2 dm = mid - m_PinchMid;
                float perPixel = displayHeight / Mathf.Max(Screen.height, 1);
                transform.position += transform.rotation * new Vector3(-dm.x, -dm.y, 0f) * perPixel;
            }
            m_PinchMid = mid;
            m_PinchDist = dist;
        }
        m_PrevTouches = n;
    }

    static bool TouchOverUi(Vector2 screenPos)
    {
        var es = EventSystem.current;
        if (es == null) return false;
        var hits = new System.Collections.Generic.List<RaycastResult>();
        es.RaycastAll(new PointerEventData(es) { position = screenPos }, hits);
        return hits.Count > 0;
    }

    void Apply()
    {
        transform.rotation = Quaternion.Euler(m_Pitch, m_Yaw, 0f);
        if (xrSpace != null) { ApplyOpenXR(); return; }
        if (display == null) return;
        display.virtualDisplayHeight = displayHeight;
        UpdateDepthFit();
        display.ipdFactor = Strength * DepthFit;

        // The camera and DisplayXRDisplay share one GameObject (DisplayXR's supported layout).
        // 3D: that object sits on the pivot, so the virtual display plane passes through the performer.
        // 2D (no DisplayXR runtime): back it away so a plain camera frames the same displayHeight. DisplayXR
        // does this once at startup; redoing it every frame keeps zoom working.
        var t = display.transform;
        t.localRotation = Quaternion.identity;
        ApplyNearClip(fallbackCamera, ProviderActive());
        ApplySplatViewpoint(fallbackCamera, ProviderActive());
        if (ProviderActive() || fallbackCamera == null)
        {
            t.localPosition = Vector3.zero;
            return;
        }
        fallbackCamera.fieldOfView = 2f * Mathf.Atan(0.5f / viewDistanceRatio) * Mathf.Rad2Deg;
        t.localPosition = display.TryGetVirtualDisplayPullback(fallbackCamera, out float d, out _)
            ? new Vector3(0f, 0f, -d) : Vector3.zero;
    }

    // DisplayXR's Android runtime (an OpenXR runtime) computes the views. With XR_DXR_view_rig the app declares
    // a display rig (DisplayXRAndroidFeature): the virtual display at the XR space's origin, displayHeight tall,
    // eye spread Strength x DepthFit, and the runtime returns eye poses and off-axis projections in scene units,
    // as DisplayXR does on Windows. The XR space sits on the pivot, so the display plane passes through the
    // performer. Unity renders each eye from those poses; the camera object only matters to 4DV's plugin, which
    // culls and sorts from it, so it's put at the tracked head, scaled into the rig.
    //
    // Without the extension (an older runtime) the XR space is scaled to scene units per metre instead, and
    // the runtime's physical eyes are used as they are (no zoom-dependent depth, no strength).
    void ApplyOpenXR()
    {
        GetViewGeometry(out _, out _, out float viewer, out float spm);
        UpdateDepthFit();
        var t = xrSpace;
        bool rig = DisplayXRAndroidFeature.ViewRigActive;
        t.localScale = rig ? Vector3.one : Vector3.one * spm;
        t.localRotation = Quaternion.identity;
        var origin = Vector3.zero;
        if (ArMode)
        {
            // Turn the panel about the nominal eye point, not the pivot.
            var eye = new Vector3(0f, 0f, -viewer * spm);
            t.localRotation = ArTurn;
            origin = eye - ArTurn * eye;
        }
        t.localPosition = rig ? origin : origin - t.localRotation * (xrPanelOrigin * spm);
        DisplayXRAndroidFeature.Height = displayHeight;
        DisplayXRAndroidFeature.IpdFactor = Is3D ? Mathf.Min(Strength * DepthFit, 1f) : 0f;
        if (xrCamera == null) return;

        var head = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.CenterEye);
        Vector3 headPos = xrPanelOrigin + new Vector3(0f, 0f, -0.45f);
        if (head.isValid) head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.centerEyePosition, out headPos);
        var c = xrCamera.transform;
        c.localRotation = Quaternion.identity;
        c.localPosition = rig ? (headPos - xrPanelOrigin) * spm : headPos;

        // Clip anything more than s_MaxPopOut in front of the panel: see ApplyNearClip.
        var panel = ScreenTransform;
        float toPanel = Vector3.Dot(panel.position - c.position, panel.forward);
        xrCamera.nearClipPlane = Mathf.Max(0.01f, toPanel - s_MaxPopOut * spm);
        xrCamera.farClipPlane = 1000f;
        if (s_Debug && Time.frameCount % 120 == 0)
            Debug.Log($"[4DV-DBG] rig {rig} head {headPos.ToString("F3")} m, camera {c.localPosition.ToString("F2")}, height {displayHeight:0.00}, ipd {DisplayXRAndroidFeature.IpdFactor:0.00}, near {xrCamera.nearClipPlane:0.00}");
    }

    Transform m_XrPanel;

    // The virtual screen (what the woven image shows): the XR space on Android, DisplayXR's display elsewhere.
    public Transform ScreenTransform
    {
        get
        {
            if (xrSpace != null)
            {
                // The panel centre inside the XR space (the AR camera image hangs off it).
                if (m_XrPanel == null)
                {
                    m_XrPanel = new GameObject("XR Panel").transform;
                    m_XrPanel.SetParent(xrSpace, false);
                }
                m_XrPanel.localPosition = DisplayXRAndroidFeature.ViewRigActive ? Vector3.zero : xrPanelOrigin;
                return m_XrPanel;
            }
            return display != null ? display.transform : null;
        }
    }

    // Physical size of the woven area (metres), the viewing distance (metres) and scene units per metre at
    // the screen.
    public void GetViewGeometry(out float physW, out float physH, out float viewer, out float scenePerMetre)
    {
        PanelGeometry(out _, out physW, out physH, out viewer);
        scenePerMetre = Mathf.Max(displayHeight, 1e-3f) / physH;
    }

    public static bool ProviderActive()
    {
        try { return DisplayXRProviderDriver.IsActive; } catch { return false; }
    }

    static bool PointerOverUi()
    {
        if (DisplayXRWindowSpaceUI.IsCursorOverInteractive) return true;   // set by FourDVPanel in 3D
        var es = EventSystem.current;
        return es != null && es.IsPointerOverGameObject();                 // 2D fallback canvas
    }

    void PollButtons()
    {
        int b = 0;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN   // DisplayXR's overlay window exists only on Windows
        if (ProviderActive())
        {
            try { DisplayXRNative.displayxr_get_overlay_pointer(out _, out _, out b); }
            catch (EntryPointNotFoundException) { }
        }
#endif
        var m = Mouse.current;
        if (m != null)
        {
            if (m.leftButton.isPressed) b |= 1;
            if (m.rightButton.isPressed) b |= 2;
            if (m.middleButton.isPressed) b |= 4;
        }
        m_ButtonsCurr = b;
    }

    bool Held(int button) => (m_ButtonsCurr & (1 << button)) != 0;

    // Test hook: `"4DV Viewer.exe" -4dv-shot C:\path\shot.png [-4dv-clip N]` saves one frame after 8 s, then quits.
    void ScheduleScreenshotFromArgs()
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "-4dv-shot");
        if (i < 0 || i + 1 >= args.Length) return;
        StartCoroutine(Shot(args[i + 1]));
    }

    System.Collections.IEnumerator Shot(string path)
    {
        int clip = (int)Arg("-4dv-clip", 1, -1);
        if (clip >= 0)
        {
            yield return null;
            var player = FindAnyObjectByType<FourDVPlayer>();
            if (player != null) player.Select(clip);
        }
        yield return new WaitForSeconds(8f);
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(path);
        yield return new WaitForSeconds(1f);
        Application.Quit();
    }
}
