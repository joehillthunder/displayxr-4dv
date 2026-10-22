using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

// AR view for the Android 3D tablets (Nubia Pad 3D II, RedMagic 3D): the rear camera's live image behind the clip, and the tablet's gyroscope
// keeping the clip fixed in the room as the tablet turns. Three degrees of freedom (turning, not walking
// around): ARCore doesn't list the Nubia Pad 3D II or RedMagic 3D Explorer, and Unity's ARCore plug-in only
// renders with Vulkan (which 4DV's sort needs) from AR Foundation 6.1 on Unity 6.
//
// The camera image is a quad parented to the screen, backgroundDepth display-heights behind it, so both eyes
// see it at one depth behind the clip. Face tracking keeps the front camera; this opens the rear one.
// Double tap (FourDVOrbit.ResetView) re-places the clip in front of the tablet.
public class FourDVAR : MonoBehaviour
{
    public FourDVOrbit orbit;
    public Material backgroundMaterial;      // URP Unlit; referenced here so the build keeps the shader
    public float backgroundDepth = 3f;       // behind the screen, in display heights

    public bool Active { get; private set; }
    public bool Starting { get; private set; }
    public string Status { get; private set; } = "";

    WebCamTexture m_Cam;
    GameObject m_Quad;
    Material m_Mat;
    Quaternion m_Ref;
    Vector3 m_Gravity, m_GravityRef;
    bool m_HaveRef;

    void Awake()
    {
        if (orbit != null) orbit.ViewReset += Recenter;
    }

    public void Toggle()
    {
        if (Starting) return;
        if (Active) End();
        else StartCoroutine(Begin());
    }

    public void Recenter() => m_HaveRef = false;

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
        Debug.Log($"[4DV] AR: {name} {m_Cam.width}x{m_Cam.height}, rotation {m_Cam.videoRotationAngle}");

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
        m_HaveRef = false;
        Active = true;
        Starting = false;
        Status = "";
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
        UpdateTurn();
        PlaceImage();
    }

    // The tablet's turn since the clip was placed, in the screen's frame. Android reports the attitude
    // right-handed (x right, y up, z out of the screen in the panel's natural landscape orientation);
    // negating z and w makes it Unity's left-handed frame, z into the screen.
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
            // Tilt only: gravity in the tablet's frame (z flipped to Unity's), low-pass filtered. When the tablet
            // turns by R, gravity seen from it turns by R's inverse; turning about gravity itself can't be seen.
            var a = Accelerometer.current.acceleration.ReadValue();
            var g = new Vector3(a.x, a.y, -a.z);
            if (g.sqrMagnitude < 1e-4f) return;
            m_Gravity = m_HaveRef ? Vector3.Lerp(m_Gravity, g.normalized, 0.15f) : g.normalized;
            if (!m_HaveRef) { m_GravityRef = m_Gravity; m_HaveRef = true; }
            turn = Quaternion.Inverse(Quaternion.FromToRotation(m_GravityRef, m_Gravity));
        }
        else return;
        // Upside-down landscape: the screen's axes are the sensor's turned half a turn about z.
        if (Screen.orientation == ScreenOrientation.LandscapeRight)
        {
            var flip = Quaternion.Euler(0f, 0f, 180f);
            turn = flip * turn * Quaternion.Inverse(flip);
        }
        orbit.ArTurn = turn;
    }

    // Cover the window as seen from the nominal eye, with margin for head movement.
    void PlaceImage()
    {
        var screen = orbit.ScreenTransform;
        if (screen == null || m_Cam.width <= 16) return;
        if (m_Quad.transform.parent != screen) m_Quad.transform.SetParent(screen, false);

        orbit.GetViewGeometry(out _, out _, out float viewer, out float spm);
        float h = orbit.displayHeight;
        float w = h * Screen.width / Mathf.Max(Screen.height, 1);
        float dist = viewer * spm, behind = backgroundDepth * h;
        float grow = (dist + behind) / Mathf.Max(dist, 1e-3f) * 1.15f;

        int angle = m_Cam.videoRotationAngle;
        bool sideways = angle % 180 != 0;
        float aspect = sideways ? (float)m_Cam.height / m_Cam.width : (float)m_Cam.width / m_Cam.height;
        float qh = h * grow, qw = qh * aspect;
        if (qw < w * grow) { qw = w * grow; qh = qw / aspect; }

        // The screen's frame may be scaled (Android's XR space is in metres): place the quad in its units.
        float k = 1f / Mathf.Max(screen.lossyScale.x, 1e-6f);
        var t = m_Quad.transform;
        t.localPosition = new Vector3(0f, 0f, behind * k);
        t.localRotation = Quaternion.Euler(0f, 0f, -angle);
        t.localScale = (sideways ? new Vector3(qh, qw, 1f) : new Vector3(qw, qh, 1f)) * k;
        bool flipY = m_Cam.videoVerticallyMirrored;
        m_Mat.mainTextureScale = new Vector2(1f, flipY ? -1f : 1f);
        m_Mat.mainTextureOffset = new Vector2(0f, flipY ? 1f : 0f);
    }
}
