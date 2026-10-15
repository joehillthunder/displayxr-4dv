using System;
using DisplayXR;
using UnityEngine;
using UnityEngine.InputSystem;

// Turntable controls for a display-centric DisplayXR rig:
//
//   Orbit Pivot (this, at the subject's centre)   yaw/pitch = orbit, position = pan
//     Virtual Display (DisplayXRDisplay)           its plane passes through the pivot
//       Main Camera                                 placed for the 2D fallback; XR overwrites its pose
//
// Mouse:  left-drag orbit · right/middle-drag pan · wheel zoom
// Keys:   T auto-turn · Home reset view · [ ] depth · V 2D/3D · F11 fullscreen · I screenshot · Esc quit
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

    Vector3 m_HomePos;
    float m_HomeHeight, m_Yaw, m_Pitch, m_Depth = 1f;
    bool m_AutoTurn, m_3D = true;
    int m_ButtonsPrev, m_ButtonsCurr;

    void Start()
    {
        // Windowed weaving: open at 70% of the display rather than a fixed pixel size, which is tiny on
        // high-DPI 3D panels. F11 switches to fullscreen.
        if (Screen.fullScreenMode == FullScreenMode.Windowed)
        {
            var res = Screen.currentResolution;
            Screen.SetResolution(Mathf.RoundToInt(res.width * 0.7f), Mathf.RoundToInt(res.height * 0.7f), FullScreenMode.Windowed);
        }

        // Overrides for framing a new clip without rebuilding:
        //   -4dv-pivot x y z   -4dv-yaw deg   -4dv-pitch deg   -4dv-height metres
        var args = Environment.GetCommandLineArgs();
        float Arg(string name, int offset, float fallback)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + offset < args.Length &&
                   float.TryParse(args[i + offset], System.Globalization.NumberStyles.Float,
                       System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }
        var p = transform.position;
        transform.position = new Vector3(Arg("-4dv-pivot", 1, p.x), Arg("-4dv-pivot", 2, p.y), Arg("-4dv-pivot", 3, p.z));
        m_Yaw = Arg("-4dv-yaw", 1, transform.eulerAngles.y);
        m_Pitch = Arg("-4dv-pitch", 1, 0f);
        displayHeight = Arg("-4dv-height", 1, displayHeight);

        m_HomePos = transform.position;
        m_HomeHeight = displayHeight;
        m_HomeYaw = m_Yaw;
        m_HomePitch = m_Pitch;
        Apply();
        ScheduleScreenshotFromArgs();
    }

    void Update()
    {
        var kb = Keyboard.current;
        PollButtons();

        // orbit / pan
        Vector2 d = Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
        if (Held(0))
        {
            m_Yaw += d.x * orbitSpeed;
            m_Pitch = Mathf.Clamp(m_Pitch - d.y * orbitSpeed, -80f, 80f);
            m_AutoTurn = false;
        }
        else if (Held(1) || Held(2))
        {
            float perPixel = displayHeight / Mathf.Max(Screen.height, 1);
            transform.position += transform.rotation * new Vector3(-d.x, -d.y, 0f) * perPixel;
        }
        if (m_AutoTurn) m_Yaw += autoTurnSpeed * Time.deltaTime;

        // zoom
        float wheel = Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f;
        if (Mathf.Abs(wheel) >= 10f) wheel /= 120f;   // player reports WHEEL_DELTA units
        if (Mathf.Abs(wheel) > 0.001f)
            displayHeight = Mathf.Clamp(displayHeight * Mathf.Pow(1f - zoomStep, wheel), minHeight, maxHeight);

        if (kb != null)
        {
            if (kb.tKey.wasPressedThisFrame) m_AutoTurn = !m_AutoTurn;
            if (kb.homeKey.wasPressedThisFrame) ResetView();
            if (kb.leftBracketKey.wasPressedThisFrame) m_Depth = Mathf.Max(0f, m_Depth - 0.25f);
            if (kb.rightBracketKey.wasPressedThisFrame) m_Depth = Mathf.Min(2f, m_Depth + 0.25f);
            if (kb.vKey.wasPressedThisFrame && ProviderActive())
            {
                m_3D = !m_3D;
                DisplayXRProvider.RequestDisplayMode(m_3D);
            }
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
    }

    float m_HomeYaw, m_HomePitch;

    void ResetView()
    {
        m_Yaw = m_HomeYaw;
        m_Pitch = m_HomePitch;
        m_Depth = 1f;
        displayHeight = m_HomeHeight;
        transform.position = m_HomePos;
        m_AutoTurn = false;
    }

    void Apply()
    {
        transform.rotation = Quaternion.Euler(m_Pitch, m_Yaw, 0f);
        if (display != null)
        {
            display.virtualDisplayHeight = displayHeight;
            display.ipdFactor = m_Depth;
        }
        if (fallbackCamera != null)
        {
            // 2D: frame the same displayHeight from a fixed distance ratio
            fallbackCamera.transform.localPosition = new Vector3(0f, 0f, -displayHeight * viewDistanceRatio);
            fallbackCamera.transform.localRotation = Quaternion.identity;
            fallbackCamera.fieldOfView = 2f * Mathf.Atan(0.5f / viewDistanceRatio) * Mathf.Rad2Deg;
        }
    }

    static bool ProviderActive()
    {
        try { return DisplayXRProviderDriver.IsActive; } catch { return false; }
    }

    void PollButtons()
    {
        m_ButtonsPrev = m_ButtonsCurr;
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

    // Test hook: `"4DV Viewer.exe" -4dv-shot C:\path\shot.png` saves one frame after 8 s, then quits.
    void ScheduleScreenshotFromArgs()
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "-4dv-shot");
        if (i < 0 || i + 1 >= args.Length) return;
        StartCoroutine(Shot(args[i + 1]));
    }

    System.Collections.IEnumerator Shot(string path)
    {
        yield return new WaitForSeconds(8f);
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(path);
        yield return new WaitForSeconds(1f);
        Application.Quit();
    }
}
