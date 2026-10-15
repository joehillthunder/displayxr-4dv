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
public class FourDVOrbit : MonoBehaviour
{
    public DisplayXRDisplay display;
    public Camera fallbackCamera;

    [Header("View")]
    public float displayHeight = 2.0f;      // metres of scene shown top-to-bottom; smaller = zoomed in
    public float minHeight = 0.4f, maxHeight = 8f;
    public float viewDistanceRatio = 1.4f;  // 2D camera distance / display height (a comfortable desk view)

    [Header("Input")]
    public float orbitSpeed = 0.25f;        // degrees per pixel
    public float zoomStep = 0.12f;          // fraction per wheel notch
    public float autoTurnSpeed = 12f;       // degrees per second

    public const float MaxStrength = 2f;
    public float Strength { get; set; } = 1f;   // DisplayXRDisplay.ipdFactor: 0 = flat, 1 = natural, 2 = exaggerated
    public bool Is3D { get; private set; } = true;
    public bool AutoTurn { get; set; }

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
    }

    // ---- 3D controls (keyboard and FourDVPanel) ----

    public void Toggle3D() => Set3D(!Is3D);

    public void Set3D(bool on)
    {
        Is3D = on;
        if (ProviderActive()) DisplayXRProvider.RequestDisplayMode(on);
    }

    public void StepStrength(float delta) => Strength = Mathf.Clamp(Strength + delta, 0f, MaxStrength);

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
        // the woven window's close button / Alt+F4 arrive as a native request
        try { if (DisplayXRNative.displayxr_consume_overlay_close_request() != 0) Application.Quit(); }
        catch (EntryPointNotFoundException) { }
        catch (DllNotFoundException) { }

        Apply();

        if (s_Debug && Time.frameCount % 120 == 0 && fallbackCamera != null)
        {
            var c = fallbackCamera.transform;
            Debug.Log($"[4DV-DBG] pivot {transform.position} yaw {m_Yaw:0} pitch {m_Pitch:0} h {displayHeight:0.00} " +
                      $"display {display?.transform.position} cam {c.position} local {c.localPosition} fov {fallbackCamera.fieldOfView:0.0} " +
                      $"btn {m_ButtonsCurr} drag {m_Dragging} overUi {PointerOverUi()}");
        }
    }

    static readonly bool s_Debug = Array.IndexOf(Environment.GetCommandLineArgs(), "-4dv-debug") >= 0;

    void Apply()
    {
        transform.rotation = Quaternion.Euler(m_Pitch, m_Yaw, 0f);
        if (display == null) return;
        display.virtualDisplayHeight = displayHeight;
        display.ipdFactor = Strength;

        // The camera and DisplayXRDisplay share one GameObject (DisplayXR's supported layout).
        // 3D: that object sits on the pivot, so the virtual display plane passes through the performer.
        // 2D (no DisplayXR runtime): back it away so a plain camera frames the same displayHeight. DisplayXR
        // does this once at startup; redoing it every frame keeps zoom working.
        var t = display.transform;
        t.localRotation = Quaternion.identity;
        if (ProviderActive() || fallbackCamera == null)
        {
            t.localPosition = Vector3.zero;
            return;
        }
        fallbackCamera.fieldOfView = 2f * Mathf.Atan(0.5f / viewDistanceRatio) * Mathf.Rad2Deg;
        t.localPosition = display.TryGetVirtualDisplayPullback(fallbackCamera, out float d, out _)
            ? new Vector3(0f, 0f, -d) : Vector3.zero;
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
        if (ProviderActive())
        {
            try { DisplayXRNative.displayxr_get_overlay_pointer(out _, out _, out b); }
            catch (EntryPointNotFoundException) { }
        }
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
