using System.Collections;
using System.Collections.Generic;
using GaussianSplat4D.Runtime;
using UnityEngine;
using UnityEngine.InputSystem;

// AR view for the Android 3D tablets (Nubia Pad 3D II, RedMagic 3D): the performer, cropped out of the capture
// stage, stands on a table or the floor seen through the rear camera.
//
// No ARCore (it doesn't list these tablets, and its Unity plug-in needs Unity 6 for Vulkan), so surfaces aren't
// detected. Instead the gyroscope gives the tablet's orientation against gravity (3 degrees of freedom: turning,
// not walking around), and the surface is a horizontal plane SurfaceDrop metres below the rear camera:
//
//   tap        place the figure where the tap's ray from the camera meets the plane
//   drag       move it along the plane
//   pinch      scale it (figurine to life size)
//   two-finger vertical drag   raise / lower the plane until the feet sit on the real surface
//   double tap re-place it in front, at the screen centre
//
// DisplayXR renders through a camera rig matching the rear camera's field of view (DisplayXRAndroidFeature),
// so the figure lines up with the camera image, which is a quad far behind it. Convergence sits on the figure,
// so it appears at screen depth with the room behind. Face tracking keeps the front camera; this opens the
// rear one. Without DisplayXR's view rig (Windows, older runtimes) AR keeps its earlier gyro-only behaviour.
public class FourDVAR : MonoBehaviour
{
    public FourDVOrbit orbit;
    public Material backgroundMaterial;      // URP Unlit; referenced here so the build keeps the shader
    public ComputeShader cropShader;         // FourDVCrop.compute: keeps a cylinder around the performer
    public float cropRadius = 0.7f;          // metres around the performer (in the capture)
    public float backgroundDistance = 6f;    // metres: the camera image, well behind the figure

    public bool Active { get; private set; }
    public bool Starting { get; private set; }
    public string Status { get; private set; } = "";

    public float FigureScale { get; private set; } = 0.2f;   // 1 = life size
    public float SurfaceDrop { get; private set; } = 0.35f;  // metres from the rear camera down to the surface

    FourDVPlayer m_Player;
    WebCamTexture m_Cam;
    GameObject m_Quad;
    Material m_Mat;
    Quaternion m_Ref;
    bool m_HaveRef;
    Vector3 m_Gravity, m_GravityRef;
    float m_VFov = 45f * Mathf.Deg2Rad, m_HFov = 70f * Mathf.Deg2Rad;

    // world placement
    Transform m_Clips;
    Vector3 m_ClipsPos, m_ClipsScale;
    Quaternion m_ClipsRot;
    Vector3 m_Spot;          // on the surface, camera-relative world frame (camera at the origin)
    bool m_Placed;
    GaussianSplat4DSource m_Cropped;

    void Awake()
    {
        if (orbit != null) orbit.ViewReset += Recenter;
        m_Player = FindAnyObjectByType<FourDVPlayer>();
    }

    bool CameraRigMode => DisplayXRAndroidFeature.ViewRigActive && orbit != null && orbit.xrSpace != null;

    public void Toggle()
    {
        if (Starting) return;
        if (Active) End();
        else StartCoroutine(Begin());
    }

    public void Recenter()
    {
        m_HaveRef = false;
        m_Placed = false;
    }

    IEnumerator Begin()
    {
        Starting = true;
        Status = "Starting camera...";
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
        {
            bool? granted = null;
            var cb = new UnityEngine.Android.PermissionCallbacks();
            cb.PermissionGranted += _ => granted = true;
            cb.PermissionDenied += _ => granted = false;
            cb.PermissionDeniedAndDontAskAgain += _ => granted = false;
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Camera, cb);
            while (granted == null) yield return null;
            if (granted == false) { Fail("Camera permission denied"); yield break; }
        }
#endif
        string name = null;
        foreach (var d in WebCamTexture.devices)
            if (!d.isFrontFacing) { name = d.name; break; }
        if (name == null) { Fail("No rear camera"); yield break; }

        m_Cam = new WebCamTexture(name, 1920, 1080, 30);
        m_Cam.Play();
        float until = Time.unscaledTime + 5f;
        while (m_Cam.width <= 16 && Time.unscaledTime < until) yield return null;
        if (m_Cam.width <= 16) { Fail("Rear camera didn't start"); yield break; }
        ReadCameraFov(name, (float)m_Cam.width / m_Cam.height);
        Debug.Log($"[4DV] AR: {name} {m_Cam.width}x{m_Cam.height}, rotation {m_Cam.videoRotationAngle}, fov {m_HFov * Mathf.Rad2Deg:0}x{m_VFov * Mathf.Rad2Deg:0} deg");

        m_Quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        m_Quad.name = "AR Camera Image";
        Destroy(m_Quad.GetComponent<Collider>());
        m_Mat = new Material(backgroundMaterial);
        m_Mat.mainTexture = m_Cam;
        var r = m_Quad.GetComponent<MeshRenderer>();
        r.sharedMaterial = m_Mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;

        if (AttitudeSensor.current != null) InputSystem.EnableDevice(AttitudeSensor.current);
        else if (Accelerometer.current != null) InputSystem.EnableDevice(Accelerometer.current);
        Debug.Log($"[4DV] AR: tracking with {(AttitudeSensor.current != null ? "the attitude sensor (gyroscope)" : Accelerometer.current != null ? "the accelerometer (tilt only)" : "nothing: no motion sensor")}");

        if (CameraRigMode && m_Player != null)
        {
            m_Clips = m_Player.transform;
            m_ClipsPos = m_Clips.position;
            m_ClipsRot = m_Clips.rotation;
            m_ClipsScale = m_Clips.localScale;
            DisplayXRAndroidFeature.CameraRig = true;
        }
        m_HaveRef = false;
        m_Placed = false;
        Active = true;
        Starting = false;
        Status = "Tap the table or floor to place";
        if (orbit != null) orbit.ArMode = true;
    }

    void Fail(string message)
    {
        Debug.LogWarning("[4DV] AR: " + message);
        End();
        Status = message;
    }

    void End()
    {
        StopAllCoroutines();
        Active = Starting = false;
        Status = "";
        DisplayXRAndroidFeature.CameraRig = false;
        if (m_Clips != null)
        {
            m_Clips.SetPositionAndRotation(m_ClipsPos, m_ClipsRot);
            m_Clips.localScale = m_ClipsScale;
            m_Clips = null;
        }
        SetCrop(null);
        if (orbit != null) { orbit.ArMode = false; orbit.ArTurn = Quaternion.identity; }
        if (m_Cam != null) { m_Cam.Stop(); Destroy(m_Cam); m_Cam = null; }
        if (m_Quad != null) { Destroy(m_Quad); m_Quad = null; }
        if (m_Mat != null) { Destroy(m_Mat); m_Mat = null; }
        if (AttitudeSensor.current != null && AttitudeSensor.current.enabled) InputSystem.DisableDevice(AttitudeSensor.current);
        if (Accelerometer.current != null && Accelerometer.current.enabled) InputSystem.DisableDevice(Accelerometer.current);
    }

    void OnApplicationPause(bool paused)
    {
        if (!Active || m_Cam == null) return;
        if (paused) m_Cam.Pause();
        else m_Cam.Play();
    }

    void OnDestroy() => End();

    void LateUpdate()
    {
        if (!Active || orbit == null) return;
        if (CameraRigMode && m_Clips != null) UpdateTabletop();
        else
        {
            UpdateTurn();
            PlaceImageOnScreen();
        }
    }

    // ---------------------------------------------------------------- tabletop (DisplayXR camera rig)

    void UpdateTabletop()
    {
        // The XR space is the rear camera: at the world origin, turned like the tablet against gravity.
        var cam = orbit.xrSpace;
        cam.SetPositionAndRotation(Vector3.zero, DeviceRotation());
        cam.localScale = Vector3.one;
        HandleTouch(cam);
        if (!m_Placed) PlaceAt(new Vector2(Screen.width * 0.5f, Screen.height * 0.4f), cam);

        // The figure: feet on the surface at the spot, upright, facing the camera.
        var toCam = -m_Spot;
        toCam.y = 0f;
        var facing = toCam.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(-toCam.normalized, Vector3.up) : Quaternion.identity;
        var src = m_Player != null ? m_Player.Current : null;
        Vector3 centre = src != null ? CropCentre(src) : Vector3.zero;
        m_Clips.localScale = Vector3.one * FigureScale;
        m_Clips.rotation = facing;
        m_Clips.position = m_Spot - facing * (new Vector3(centre.x, 0f, centre.z) * FigureScale);
        SetCrop(src);

        float dist = Mathf.Max(m_Spot.magnitude, 0.1f);
        DisplayXRAndroidFeature.VerticalFov = m_VFov;
        DisplayXRAndroidFeature.ConvergenceDiopters = 1f / dist;
        DisplayXRAndroidFeature.IpdFactor = orbit.Is3D ? orbit.Strength : 0f;
        Status = $"Size {FigureScale * 100f:0}% · surface {SurfaceDrop * 100f:0} cm below";

        // Camera image: a quad filling the rear camera's field of view, far behind the figure.
        float h = 2f * backgroundDistance * Mathf.Tan(m_VFov * 0.5f);
        float w = 2f * backgroundDistance * Mathf.Tan(m_HFov * 0.5f);
        var t = m_Quad.transform;
        if (t.parent != cam) t.SetParent(cam, false);
        int angle = m_Cam.videoRotationAngle;
        bool sideways = angle % 180 != 0;
        t.localPosition = new Vector3(0f, 0f, backgroundDistance);
        t.localRotation = Quaternion.Euler(0f, 0f, -angle);
        t.localScale = sideways ? new Vector3(h, w, 1f) : new Vector3(w, h, 1f);
        bool flipY = m_Cam.videoVerticallyMirrored;
        m_Mat.mainTextureScale = new Vector2(1f, flipY ? -1f : 1f);
        m_Mat.mainTextureOffset = new Vector2(0f, flipY ? 1f : 0f);
    }

    // Tablet orientation in a y-up world: Android's rotation vector is device-to-earth (x east, y north, z up)
    // in a right-handed frame; negating z and w makes it left-handed (device z into the screen, i.e. along the
    // rear camera), and the 90-degree turn about x maps earth-z-up to Unity's y-up.
    Quaternion DeviceRotation()
    {
        var sensor = AttitudeSensor.current;
        if (sensor != null && sensor.enabled)
        {
            var q = sensor.attitude.ReadValue();
            var r = Quaternion.Euler(90f, 0f, 0f) * new Quaternion(q.x, q.y, -q.z, -q.w);
            if (Screen.orientation == ScreenOrientation.LandscapeRight) r *= Quaternion.Euler(0f, 0f, 180f);
            return r;
        }
        // Accelerometer only: tilt from gravity, no heading.
        if (Accelerometer.current != null && Accelerometer.current.enabled)
        {
            var a = Accelerometer.current.acceleration.ReadValue();
            var g = new Vector3(a.x, a.y, -a.z);
            if (g.sqrMagnitude > 1e-4f) m_Gravity = Vector3.Lerp(m_Gravity.sqrMagnitude > 0f ? m_Gravity : g.normalized, g.normalized, 0.15f);
            // device-frame "up" is -gravity (the accelerometer reads the reaction to gravity: +up)
            return Quaternion.FromToRotation(m_Gravity.normalized, Vector3.up);
        }
        return Quaternion.identity;
    }

    // The point on the surface (a horizontal plane SurfaceDrop below the camera) under a screen position.
    bool RayToSurface(Vector2 screenPos, Transform cam, out Vector3 hit)
    {
        float nx = screenPos.x / Mathf.Max(Screen.width, 1) * 2f - 1f;
        float ny = screenPos.y / Mathf.Max(Screen.height, 1) * 2f - 1f;
        float tanV = Mathf.Tan(m_VFov * 0.5f);
        float tanH = tanV * Screen.width / Mathf.Max(Screen.height, 1);
        var dir = cam.rotation * new Vector3(nx * tanH, ny * tanV, 1f);
        hit = Vector3.zero;
        if (dir.y > -0.02f) return false;              // looking at or above the horizon
        float k = -SurfaceDrop / dir.y;
        if (k > 8f) return false;
        hit = dir * k;
        return true;
    }

    void PlaceAt(Vector2 screenPos, Transform cam)
    {
        if (RayToSurface(screenPos, cam, out var hit)) { m_Spot = hit; m_Placed = true; }
        else if (!m_Placed)
        {
            // Pointing at or above the horizon: put it 1 m ahead, on the surface.
            var f = cam.forward; f.y = 0f;
            m_Spot = (f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward) * 1f + Vector3.down * SurfaceDrop;
        }
    }

    // ---------------------------------------------------------------- touch

    int m_PrevTouches;
    bool m_OnUi;
    float m_PinchDist, m_PinchY;

    void HandleTouch(Transform cam)
    {
        var ts = Touchscreen.current;
        if (ts == null) return;
        int n = 0;
        Vector2 p0 = default, p1 = default;
        foreach (var t in ts.touches)
        {
            if (!t.isInProgress) continue;
            if (n == 0) p0 = t.position.ReadValue();
            else if (n == 1) p1 = t.position.ReadValue();
            n++;
        }
        if (ts.primaryTouch.tap.wasPressedThisFrame && ts.primaryTouch.tapCount.ReadValue() >= 2 && !m_OnUi)
            m_Placed = false;                              // double tap: back to the screen centre
        if (n == 0) { m_PrevTouches = 0; m_OnUi = false; return; }
        if (m_PrevTouches == 0) m_OnUi = FourDVPanel.HitsUi(p0);
        if (m_OnUi) { m_PrevTouches = n; return; }

        if (n == 1) PlaceAt(p0, cam);                     // tap or drag: the figure follows the finger
        else
        {
            float dist = (p0 - p1).magnitude;
            float y = (p0.y + p1.y) * 0.5f;
            if (m_PrevTouches >= 2 && m_PinchDist > 1f)
            {
                FigureScale = Mathf.Clamp(FigureScale * dist / m_PinchDist, 0.03f, 1.2f);
                // Two fingers up/down: the surface down/up (the screen's height is half a metre of drop).
                SurfaceDrop = Mathf.Clamp(SurfaceDrop - (y - m_PinchY) / Mathf.Max(Screen.height, 1) * 0.5f, 0.05f, 2.5f);
            }
            m_PinchDist = dist;
            m_PinchY = y;
        }
        m_PrevTouches = n;
    }

    // ---------------------------------------------------------------- crop

    // The performer's centre in the clip's local space: the median of its chunk centres near the capture's
    // middle (FourDVPlayer's depth samples), on the floor (y = 0).
    readonly Dictionary<GaussianSplat4DSource, Vector3> m_Centres = new Dictionary<GaussianSplat4DSource, Vector3>();

    Vector3 CropCentre(GaussianSplat4DSource src)
    {
        if (m_Centres.TryGetValue(src, out var c)) return c;
        c = Vector3.zero;
        var pts = m_Player != null ? m_Player.LocalSamples(src) : null;
        if (pts != null && pts.Length > 0)
        {
            var xs = new List<float>();
            var zs = new List<float>();
            foreach (var p in pts)
                if (p.x * p.x + p.z * p.z < 1.5f * 1.5f && p.y > 0.2f) { xs.Add(p.x); zs.Add(p.z); }
            if (xs.Count > 8) { xs.Sort(); zs.Sort(); c = new Vector3(xs[xs.Count / 2], 0f, zs[zs.Count / 2]); }
        }
        m_Centres[src] = c;
        return c;
    }

    void SetCrop(GaussianSplat4DSource src)
    {
        if (m_Cropped != null && m_Cropped != src)
        {
            m_Cropped.GSFXShader = null;
            m_Cropped.FXParameters.Clear();
            m_Cropped = null;
        }
        if (src == null || cropShader == null || src == m_Cropped) return;
        var c = CropCentre(src);
        src.FXParameters.Clear();
        src.FXParameters.Add(new GSFXParameter { parameterName = "_CropCylinder", type = GSFXParameterType.Vector4, vector4Value = new Vector4(c.x, c.z, cropRadius, 0f) });
        src.FXParameters.Add(new GSFXParameter { parameterName = "_CropHeight", type = GSFXParameterType.Vector4, vector4Value = new Vector4(-0.05f, 2.6f, 0f, 0f) });
        src.GSFXShader = cropShader;
        m_Cropped = src;
        Debug.Log($"[4DV] AR: cropping {src.GSplat4DAsset.name} to {cropRadius} m around ({c.x:0.00}, {c.z:0.00})");
    }

    // ---------------------------------------------------------------- gyro-only fallback (no view rig)

    void UpdateTurn()
    {
        Quaternion turn;
        var sensor = AttitudeSensor.current;
        if (sensor != null && sensor.enabled)
        {
            var q = sensor.attitude.ReadValue();
            var u = new Quaternion(q.x, q.y, -q.z, -q.w);
            if (!m_HaveRef) { m_Ref = u; m_HaveRef = true; }
            turn = Quaternion.Inverse(m_Ref) * u;
        }
        else if (Accelerometer.current != null && Accelerometer.current.enabled)
        {
            var a = Accelerometer.current.acceleration.ReadValue();
            var g = new Vector3(a.x, a.y, -a.z);
            if (g.sqrMagnitude < 1e-4f) return;
            m_Gravity = m_HaveRef ? Vector3.Lerp(m_Gravity, g.normalized, 0.15f) : g.normalized;
            if (!m_HaveRef) { m_GravityRef = m_Gravity; m_HaveRef = true; }
            turn = Quaternion.Inverse(Quaternion.FromToRotation(m_GravityRef, m_Gravity));
        }
        else return;
        if (Screen.orientation == ScreenOrientation.LandscapeRight)
        {
            var flip = Quaternion.Euler(0f, 0f, 180f);
            turn = flip * turn * Quaternion.Inverse(flip);
        }
        orbit.ArTurn = turn;
    }

    void PlaceImageOnScreen()
    {
        var screen = orbit.ScreenTransform;
        if (screen == null || m_Cam.width <= 16) return;
        if (m_Quad.transform.parent != screen) m_Quad.transform.SetParent(screen, false);
        orbit.GetViewGeometry(out _, out _, out float viewer, out float spm);
        float h = orbit.displayHeight;
        float w = h * Screen.width / Mathf.Max(Screen.height, 1);
        float dist = viewer * spm, behind = 3f * h;
        float grow = (dist + behind) / Mathf.Max(dist, 1e-3f) * 1.8f;
        int angle = m_Cam.videoRotationAngle;
        bool sideways = angle % 180 != 0;
        float aspect = sideways ? (float)m_Cam.height / m_Cam.width : (float)m_Cam.width / m_Cam.height;
        float qh = h * grow, qw = qh * aspect;
        if (qw < w * grow) { qw = w * grow; qh = qw / aspect; }
        float k = 1f / Mathf.Max(screen.lossyScale.x, 1e-6f);
        var t = m_Quad.transform;
        t.localPosition = new Vector3(0f, 0f, behind * k);
        t.localRotation = Quaternion.Euler(0f, 0f, -angle);
        t.localScale = (sideways ? new Vector3(qh, qw, 1f) : new Vector3(qw, qh, 1f)) * k;
        bool flipY = m_Cam.videoVerticallyMirrored;
        m_Mat.mainTextureScale = new Vector2(1f, flipY ? -1f : 1f);
        m_Mat.mainTextureOffset = new Vector2(0f, flipY ? 1f : 0f);
    }

    // ---------------------------------------------------------------- camera field of view

    // The rear camera's field of view at the stream's aspect, from Camera2 (focal length and sensor size).
    // A 16:9 stream from a 4:3 sensor keeps the full width and crops the height. Falls back to 70 degrees wide.
    void ReadCameraFov(string deviceName, float aspect)
    {
        float hfov = 70f * Mathf.Deg2Rad;
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            string id = deviceName.Replace("Camera ", "").Trim();
            var activity = new AndroidJavaClass("com.unity3d.player.UnityPlayer").GetStatic<AndroidJavaObject>("currentActivity");
            var manager = activity.Call<AndroidJavaObject>("getSystemService", "camera");
            var chars = manager.Call<AndroidJavaObject>("getCameraCharacteristics", id);
            var keys = new AndroidJavaClass("android.hardware.camera2.CameraCharacteristics");
            var focal = chars.Call<AndroidJavaObject>("get", keys.GetStatic<AndroidJavaObject>("LENS_INFO_AVAILABLE_FOCAL_LENGTHS"));
            var size = chars.Call<AndroidJavaObject>("get", keys.GetStatic<AndroidJavaObject>("SENSOR_INFO_PHYSICAL_SIZE"));
            float[] f = AndroidJNIHelper.ConvertFromJNIArray<float[]>(focal.GetRawObject());
            float sw = size.Call<float>("getWidth"), sh = size.Call<float>("getHeight");
            if (f != null && f.Length > 0 && f[0] > 0f && sw > 0f)
            {
                float sensorAspect = sw / Mathf.Max(sh, 1e-3f);
                float usedWidth = aspect >= sensorAspect ? sw : sh * aspect;
                hfov = 2f * Mathf.Atan(usedWidth / (2f * f[0]));
                Debug.Log($"[4DV] AR: camera {id} focal {f[0]:0.00} mm, sensor {sw:0.00}x{sh:0.00} mm");
            }
        }
        catch (System.Exception e) { Debug.LogWarning($"[4DV] AR: camera field of view unknown ({e.Message}); assuming 70 degrees"); }
#endif
        m_HFov = hfov;
        m_VFov = 2f * Mathf.Atan(Mathf.Tan(hfov * 0.5f) / Mathf.Max(aspect, 0.1f));
    }
}
