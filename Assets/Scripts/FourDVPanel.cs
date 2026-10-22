// Sidebar for the 4DV viewer: clip list, transport, loop mode, speed, 3D on/off and 3D strength. H hides it.
//
// In 3D the panel is a DisplayXR window-space layer (DisplayXRWindowSpaceUI): composited by the runtime on
// top of the weave so text stays sharp. That layer carries no input, so pointer events are synthesized from
// the cursor over the woven window, the same way as DisplayXR's DisplayXRWsuiMouseRouter sample
// (Apache-2.0, DisplayXR/displayxr-unity-samples). In 2D (no DisplayXR runtime) the same canvas is a normal
// screen-space overlay driven by an EventSystem.

using System.Collections.Generic;
using DisplayXR;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

public class FourDVPanel : MonoBehaviour
{
    public FourDVPlayer player;
    public FourDVOrbit orbit;

    // fractional window placement (top-left origin), same units as DisplayXRWindowSpaceUI
    const float kX = 0.01f, kY = 0.02f, kW = 0.22f, kH = 0.96f;
    const int kRtW = 720, kRtH = 1800;

    static readonly Color kText = new Color(0.92f, 0.93f, 0.95f);
    static readonly Color kMuted = new Color(0.62f, 0.66f, 0.74f);
    static readonly Color kAccent = new Color(0.13f, 0.90f, 0.91f);     // 4DV cyan
    static readonly Color kButton = new Color(0.16f, 0.19f, 0.25f, 0.95f);
    static readonly Color kPanel = new Color(0.03f, 0.05f, 0.08f, 0.72f);

    Font m_Font;
    GameObject m_CanvasGO;
    Canvas m_Canvas;
    DisplayXRWindowSpaceUI m_Wsui;
    GraphicRaycaster m_Raycaster;
    // `-4dv-kiosk` opens with the sidebar hidden (H still shows it).
    bool m_Visible = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-4dv-kiosk") < 0;

    readonly List<(Button button, Text label)> m_ClipRows = new List<(Button, Text)>();
    Text m_Now, m_TimeText, m_PlayLabel, m_LoopLabel, m_SpeedLabel, m_FitLabel, m_3DLabel, m_StrengthValue;
    Slider m_Progress, m_Strength;
    bool m_SettingProgress, m_ScrubWasPaused, m_Scrubbing;

    // pointer synthesis (3D)
    PointerEventData m_Pointer;
    GameObject m_PressTarget;
    bool m_LeftDown;
    Vector2 m_LastPos;

    System.Collections.IEnumerator Start()
    {
        m_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        yield return null;   // FourDVPlayer.Start fills the clip list first
        Build();
    }

    // ---------------------------------------------------------------- build

    void Build()
    {
        m_CanvasGO = new GameObject("4DV Panel", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        m_CanvasGO.SetActive(false);
        m_CanvasGO.transform.SetParent(transform, false);
        m_CanvasGO.layer = LayerMask.NameToLayer("UI");
        m_Canvas = m_CanvasGO.GetComponent<Canvas>();
        m_Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        m_Raycaster = m_CanvasGO.GetComponent<GraphicRaycaster>();

        // The root is sized as the sidebar. In 2D it's anchored to the left of the screen;
        // in 3D DisplayXRWindowSpaceUI renders the canvas into its own texture.
        var root = UI("Sidebar", m_CanvasGO.transform);
        var rootRT = root.GetComponent<RectTransform>();
        rootRT.anchorMin = rootRT.anchorMax = rootRT.pivot = new Vector2(0, 1);
        rootRT.sizeDelta = new Vector2(kRtW, kRtH);
        root.AddComponent<Image>().color = kPanel;
        var v = root.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(40, 40, 36, 36);
        v.spacing = 16;
        v.childAlignment = TextAnchor.UpperLeft;
        v.childControlWidth = true; v.childControlHeight = true;
        v.childForceExpandWidth = true; v.childForceExpandHeight = false;

        Height(Label(root.transform, "4DV Volumetric", 56, FontStyle.Bold, kAccent).gameObject, 76);
        Height(Label(root.transform, "CLIPS", 30, FontStyle.Bold, kMuted).gameObject, 40);

        // clip list: two columns so a full library fits at a readable size
        var list = UI("Clips", root.transform);
        var grid = list.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2((kRtW - 80 - 12) / 2f, 62);
        grid.spacing = new Vector2(12, 8);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 2;
        int rows = (player.Available.Count + 1) / 2;
        Height(list, rows * 62 + Mathf.Max(rows - 1, 0) * 8);
        for (int i = 0; i < player.Available.Count; i++)
        {
            int idx = i;
            m_ClipRows.Add(MakeButton(list.transform, FourDVPlayer.DisplayName(player.Available[i]), 29,
                                      () => player.Select(idx), 62, TextAnchor.MiddleLeft));
        }

        Spacer(root.transform, 6);

        // now playing + progress
        m_Now = Label(root.transform, "", 42, FontStyle.Bold, kText);
        Height(m_Now.gameObject, 56);
        m_Progress = MakeSlider(root.transform, 0f, 1f, 0f, OnProgressChanged, scrub: true);
        m_TimeText = Label(root.transform, "", 32, FontStyle.Normal, kMuted);
        Height(m_TimeText.gameObject, 42);

        // transport
        var row = Row(root.transform, 100);
        MakeButton(row, "Prev", 40, player.Previous, 100);
        m_PlayLabel = MakeButton(row, "Pause", 40, player.TogglePause, 100).label;
        MakeButton(row, "Next", 40, player.Next, 100);

        var row2 = Row(root.transform, 88);
        m_LoopLabel = MakeButton(row2, "", 34, player.CycleLoop, 88).label;
        m_SpeedLabel = MakeButton(row2, "", 34, player.CycleSpeed, 88).label;
        m_FitLabel = MakeButton(row2, "", 34, () => orbit.FitDepth = !orbit.FitDepth, 88).label;

        Spacer(root.transform, 6);
        Height(Label(root.transform, "3D", 30, FontStyle.Bold, kMuted).gameObject, 40);

        var row3 = Row(root.transform, 88);
        m_3DLabel = MakeButton(row3, "", 34, orbit.Toggle3D, 88).label;
        MakeButton(row3, "Turntable", 34, () => orbit.AutoTurn = !orbit.AutoTurn, 88);
        MakeButton(row3, "Reset view", 34, orbit.ResetView, 88);

        var strengthRow = Row(root.transform, 46);
        Label(strengthRow, "3D strength", 36, FontStyle.Normal, kText);
        m_StrengthValue = Label(strengthRow, "", 36, FontStyle.Bold, kAccent);
        m_StrengthValue.alignment = TextAnchor.MiddleRight;
        m_Strength = MakeSlider(root.transform, 0f, FourDVOrbit.MaxStrength, orbit.Strength, s => orbit.Strength = s);

        var hint = Label(root.transform, Application.isMobilePlatform
                ? "Drag orbit · Pinch zoom · Two-finger drag pan\nDouble tap reset view · Menu hides this panel"
                : "Drag orbit · Right-drag pan · Wheel zoom\nSpace pause · ← → clip · L loop · [ ] strength\nV 2D/3D · F11 fullscreen · H hide panel",
            30, FontStyle.Normal, kMuted);
        hint.horizontalOverflow = HorizontalWrapMode.Wrap;
        Height(hint.gameObject, 150);

        PlaceFor2D();
        m_CanvasGO.SetActive(m_Visible);
        if (Application.isMobilePlatform) BuildToolbar();
    }

    // Tablets have no keyboard: a toolbar in the top-right corner, always on screen, with depth, AR and the
    // sidebar toggle. With the sidebar hidden the clip fills the screen.
    Text m_MenuLabel, m_ArLabel, m_ToolFitLabel, m_Status;
    FourDVAR m_Ar;

    void BuildToolbar()
    {
        m_Ar = FindAnyObjectByType<FourDVAR>();
        var go = new GameObject("4DV Toolbar", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.transform.SetParent(transform, false);
        go.layer = LayerMask.NameToLayer("UI");
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(kRtW / kW, kRtH / kH);
        scaler.matchWidthOrHeight = 1f;

        var bar = UI("Bar", go.transform);
        var rt = bar.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1, 1);
        rt.anchoredPosition = new Vector2(-24, -24);
        var hl = bar.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = 12; hl.childControlWidth = true; hl.childControlHeight = true;
        hl.childForceExpandWidth = false; hl.childForceExpandHeight = true;
        void Tool(string text, System.Action onClick, out Text label)
        {
            var (b, l) = MakeButton(bar.transform, text, 36, onClick, 88);
            var le = b.GetComponent<LayoutElement>();
            le.preferredWidth = le.minWidth = 170;
            label = l;
        }
        Tool("Depth -", () => orbit.StepStrength(-0.25f), out _);
        Tool("Depth +", () => orbit.StepStrength(+0.25f), out _);
        Tool("", () => orbit.FitDepth = !orbit.FitDepth, out m_ToolFitLabel);
        if (m_Ar != null) Tool("AR", m_Ar.Toggle, out m_ArLabel);
        Tool("", ToggleVisible, out m_MenuLabel);
        int count = m_Ar != null ? 5 : 4;
        rt.sizeDelta = new Vector2(count * 170 + (count - 1) * 12, 88);

        m_Status = Label(go.transform, "", 34, FontStyle.Bold, kText);
        m_Status.alignment = TextAnchor.UpperRight;
        var srt = m_Status.GetComponent<RectTransform>();
        srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(1, 1);
        srt.anchoredPosition = new Vector2(-24, -124);
        srt.sizeDelta = new Vector2(900, 50);
    }

    void RefreshToolbar()
    {
        if (m_MenuLabel == null) return;
        m_MenuLabel.text = m_Visible ? "Hide" : "Menu";
        m_ToolFitLabel.text = orbit.FitDepth ? "Fit: on" : "Fit: off";
        string depth = $"Depth {orbit.Strength * orbit.DepthFit:0.00}x";
        if (m_Ar != null)
        {
            m_ArLabel.text = m_Ar.Active ? "AR: on" : "AR";
            if (m_Ar.Starting || m_Ar.Status.Length > 0) depth = m_Ar.Status;
        }
        m_Status.text = depth;
    }

    void ToggleVisible()
    {
        m_Visible = !m_Visible;
        m_CanvasGO.SetActive(m_Visible);
        if (!m_Visible) DisplayXRWindowSpaceUI.IsCursorOverInteractive = false;
    }

    // ---------------------------------------------------------------- per frame

    void Update()
    {
        if (m_CanvasGO == null) return;
        var kb = Keyboard.current;
        if (kb != null && kb.hKey.wasPressedThisFrame) ToggleVisible();
        RefreshToolbar();

        // switch to the window-space layer once DisplayXR is driving the display
        if (m_Wsui == null && FourDVOrbit.ProviderActive()) AttachWindowSpaceLayer();
        if (m_Wsui == null) Ensure2DEventSystem();

        Refresh();
        if (m_Wsui != null && m_Visible) RoutePointer();
    }

    void Refresh()
    {
        for (int i = 0; i < m_ClipRows.Count; i++)
        {
            bool on = i == player.Index;
            m_ClipRows[i].button.targetGraphic.color = on ? new Color(kAccent.r, kAccent.g, kAccent.b, 0.30f) : kButton;
            m_ClipRows[i].label.color = on ? Color.white : kText;
        }
        if (player.Current != null)
            m_Now.text = $"{player.Index + 1}/{player.Available.Count}  {FourDVPlayer.DisplayName(player.Current)}";
        m_TimeText.text = $"{player.Time:0.0} / {player.Duration:0.0} s";
        if (!m_Scrubbing)
        {
            m_SettingProgress = true;
            m_Progress.value = player.Time / player.Duration;
            m_SettingProgress = false;
        }
        m_PlayLabel.text = player.Paused ? "Play" : "Pause";
        m_LoopLabel.text = player.loop switch
        {
            FourDVPlayer.LoopMode.LoopClip => "Loop: clip",
            FourDVPlayer.LoopMode.PlayAll => "Loop: all",
            _ => "Play once",
        };
        m_SpeedLabel.text = $"Speed {player.playbackSpeed:0.##}x";
        m_FitLabel.text = orbit.FitDepth ? "Fit: on" : "Fit: off";
        m_3DLabel.text = orbit.Is3D ? "3D: on" : "3D: off";
        if (!Mathf.Approximately(m_Strength.value, orbit.Strength)) m_Strength.SetValueWithoutNotify(orbit.Strength);
        m_StrengthValue.text = orbit.DepthFit < 0.995f ? $"{orbit.Strength:0.00}x · fit {orbit.DepthFit * 100f:0}%" : $"{orbit.Strength:0.00}x";
    }

    void OnProgressChanged(float f)
    {
        if (m_SettingProgress) return;
        player.Seek(f * player.Duration);
    }

    // ---------------------------------------------------------------- 2D / 3D hosting

    void PlaceFor2D()
    {
        // scale the sidebar to the same fraction of the window it occupies in 3D
        var scaler = m_CanvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(kRtW / kW, kRtH / kH);
        scaler.matchWidthOrHeight = 1f;
        var rt = m_CanvasGO.transform.GetChild(0).GetComponent<RectTransform>();
        rt.anchoredPosition = new Vector2(kX * kRtW / kW, -kY * kRtH / kH);
    }

    void AttachWindowSpaceLayer()
    {
        m_CanvasGO.SetActive(false);
        var es = EventSystem.current;
        if (es != null)
        {
            // pointer events are synthesized below; a live input module would double-handle them
            var module = es.GetComponent<BaseInputModule>();
            if (module != null) Destroy(module);
        }
        var scaler = m_CanvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.scaleFactor = 1f;
        var rt = m_CanvasGO.transform.GetChild(0).GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;

        m_Wsui = m_CanvasGO.AddComponent<DisplayXRWindowSpaceUI>();
        m_Wsui.positionX = kX; m_Wsui.positionY = kY; m_Wsui.width = kW; m_Wsui.height = kH;
        m_Wsui.disparity = 0f;
        m_Wsui.resolution = new Vector2Int(kRtW, kRtH);
        // the layer's camera flips Y, which GraphicRaycaster would otherwise treat as back-facing
        m_Raycaster.ignoreReversedGraphics = false;
        m_CanvasGO.SetActive(m_Visible);

        if (EventSystem.current == null) new GameObject("EventSystem", typeof(EventSystem));
        m_Pointer = new PointerEventData(EventSystem.current);
    }

    void Ensure2DEventSystem()
    {
        if (EventSystem.current != null) return;
        new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
    }

    // ---------------------------------------------------------------- pointer routing (3D)

    void RoutePointer()
    {
        if (!TryCursorFraction(out var frac) ||
            frac.x < kX || frac.x > kX + kW || frac.y < kY || frac.y > kY + kH)
        {
            DisplayXRWindowSpaceUI.IsCursorOverInteractive = m_PressTarget != null;
            if (!LeftDown()) Release();
            else if (m_PressTarget != null) Drag(m_LastPos);
            return;
        }
        var pos = new Vector2((frac.x - kX) / kW * kRtW, (frac.y - kY) / kH * kRtH);
        DisplayXRWindowSpaceUI.IsCursorOverInteractive = true;

        m_Pointer.Reset();
        m_Pointer.position = pos;
        m_Pointer.delta = pos - m_LastPos;
        m_Pointer.button = PointerEventData.InputButton.Left;
        var hits = new List<RaycastResult>();
        m_Raycaster.Raycast(m_Pointer, hits);
        var hovered = hits.Count > 0 ? hits[0].gameObject : null;
        m_Pointer.pointerCurrentRaycast = hits.Count > 0 ? hits[0] : default;

        bool down = LeftDown();
        if (!m_LeftDown && down && hovered != null)
        {
            m_Pointer.pointerPressRaycast = m_Pointer.pointerCurrentRaycast;
            m_Pointer.pressPosition = pos;
            m_PressTarget = ExecuteEvents.ExecuteHierarchy(hovered, m_Pointer, ExecuteEvents.pointerDownHandler)
                            ?? ExecuteEvents.GetEventHandler<IPointerClickHandler>(hovered);
            ExecuteEvents.Execute(m_PressTarget, m_Pointer, ExecuteEvents.beginDragHandler);
        }
        else if (m_LeftDown && down && m_PressTarget != null) Drag(pos);
        else if (m_LeftDown && !down)
        {
            var click = ExecuteEvents.GetEventHandler<IPointerClickHandler>(m_PressTarget);
            bool over = hovered != null && ExecuteEvents.GetEventHandler<IPointerClickHandler>(hovered) == click;
            ExecuteEvents.Execute(m_PressTarget, m_Pointer, ExecuteEvents.endDragHandler);
            ExecuteEvents.Execute(m_PressTarget, m_Pointer, ExecuteEvents.pointerUpHandler);
            if (click != null && over) ExecuteEvents.Execute(click, m_Pointer, ExecuteEvents.pointerClickHandler);
            m_PressTarget = null;
        }
        m_LeftDown = down;
        m_LastPos = pos;
    }

    void Drag(Vector2 pos)
    {
        m_Pointer.position = pos;
        ExecuteEvents.Execute(m_PressTarget, m_Pointer, ExecuteEvents.dragHandler);
    }

    void Release()
    {
        if (m_LeftDown && m_PressTarget != null)
        {
            ExecuteEvents.Execute(m_PressTarget, m_Pointer, ExecuteEvents.endDragHandler);
            ExecuteEvents.Execute(m_PressTarget, m_Pointer, ExecuteEvents.pointerUpHandler);
        }
        m_PressTarget = null;
        m_LeftDown = false;
    }

    // Cursor over the woven window, as a fraction of it (top-left origin).
    static bool TryCursorFraction(out Vector2 frac)
    {
        frac = default;
        try
        {
            DisplayXRNative.displayxr_get_overlay_pointer(out int cx, out int cy, out _);
            DisplayXRNative.displayxr_get_overlay_size(out int w, out int h);
            if (w > 0 && h > 0 && cx >= 0 && cy >= 0 && cx < w && cy < h)
            {
                frac = new Vector2((float)cx / w, (float)cy / h);
                return true;
            }
        }
        catch (System.EntryPointNotFoundException) { }
        var m = Mouse.current;
        if (m == null || Screen.width <= 0 || Screen.height <= 0) return false;
        var p = m.position.ReadValue();
        if (p.x < 0 || p.y < 0 || p.x >= Screen.width || p.y >= Screen.height) return false;
        frac = new Vector2(p.x / Screen.width, 1f - p.y / Screen.height);
        return true;
    }

    static bool LeftDown()
    {
        int b = 0;
        try { DisplayXRNative.displayxr_get_overlay_pointer(out _, out _, out b); }
        catch (System.EntryPointNotFoundException) { }
        return (b & 1) != 0 || (Mouse.current != null && Mouse.current.leftButton.isPressed);
    }

    // ---------------------------------------------------------------- UI helpers

    GameObject UI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.layer = LayerMask.NameToLayer("UI");
        return go;
    }

    Text Label(Transform parent, string text, int size, FontStyle style, Color color)
    {
        var t = UI("Text", parent).AddComponent<Text>();
        t.font = m_Font; t.fontSize = size; t.fontStyle = style; t.color = color; t.text = text;
        t.alignment = TextAnchor.MiddleLeft;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    static void Height(GameObject go, float h)
    {
        var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        le.preferredHeight = h; le.minHeight = h;
    }

    void Spacer(Transform parent, float h) => Height(UI("Spacer", parent), h);

    Transform Row(Transform parent, float h)
    {
        var row = UI("Row", parent);
        Height(row, h);
        var hl = row.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = 12; hl.childControlWidth = true; hl.childControlHeight = true;
        hl.childForceExpandWidth = true; hl.childForceExpandHeight = true;
        return row.transform;
    }

    (Button button, Text label) MakeButton(Transform parent, string text, int size, System.Action onClick, float h,
                                           TextAnchor align = TextAnchor.MiddleCenter)
    {
        var go = UI("Button", parent);
        Height(go, h);
        var img = go.AddComponent<Image>();
        img.color = kButton;
        var b = go.AddComponent<Button>();
        b.targetGraphic = img;
        var colors = b.colors;
        colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
        colors.fadeDuration = 0.06f;
        b.colors = colors;
        b.onClick.AddListener(() => onClick());
        var t = Label(go.transform, text, size, FontStyle.Normal, kText);
        t.alignment = align;
        var rt = t.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(align == TextAnchor.MiddleLeft ? 24 : 0, 0); rt.offsetMax = Vector2.zero;
        return (b, t);
    }

    Slider MakeSlider(Transform parent, float min, float max, float value, System.Action<float> onChanged, bool scrub = false)
    {
        var go = UI("Slider", parent);
        Height(go, 56);
        var bg = UI("Track", go.transform);
        var bgRT = bg.GetComponent<RectTransform>();
        bgRT.anchorMin = new Vector2(0, 0.5f); bgRT.anchorMax = new Vector2(1, 0.5f); bgRT.sizeDelta = new Vector2(0, 12);
        bg.AddComponent<Image>().color = new Color(0.22f, 0.25f, 0.32f);

        var fillArea = UI("Fill Area", go.transform).GetComponent<RectTransform>();
        fillArea.anchorMin = new Vector2(0, 0.5f); fillArea.anchorMax = new Vector2(1, 0.5f);
        fillArea.offsetMin = new Vector2(0, -6); fillArea.offsetMax = new Vector2(0, 6);
        var fill = UI("Fill", fillArea).GetComponent<RectTransform>();
        fill.anchorMin = Vector2.zero; fill.anchorMax = Vector2.one; fill.offsetMin = fill.offsetMax = Vector2.zero;
        fill.gameObject.AddComponent<Image>().color = kAccent;

        var handleArea = UI("Handle Area", go.transform).GetComponent<RectTransform>();
        handleArea.anchorMin = new Vector2(0, 0.5f); handleArea.anchorMax = new Vector2(1, 0.5f);
        handleArea.sizeDelta = new Vector2(-36, 36);
        var handle = UI("Handle", handleArea).GetComponent<RectTransform>();
        handle.sizeDelta = new Vector2(36, 0);
        var handleImg = handle.gameObject.AddComponent<Image>();
        handleImg.color = Color.white;

        var s = go.AddComponent<Slider>();
        s.fillRect = fill; s.handleRect = handle; s.targetGraphic = handleImg;
        s.minValue = min; s.maxValue = max; s.value = value;
        s.onValueChanged.AddListener(x => onChanged(x));

        // pause while scrubbing the progress bar so the clip doesn't fight the drag
        var trigger = go.AddComponent<EventTrigger>();
        void On(EventTriggerType type, System.Action a)
        {
            var e = new EventTrigger.Entry { eventID = type };
            e.callback.AddListener(_ => a());
            trigger.triggers.Add(e);
        }
        if (scrub)
        {
            On(EventTriggerType.PointerDown, () => { m_Scrubbing = true; m_ScrubWasPaused = player.Paused; player.SetPaused(true); });
            On(EventTriggerType.PointerUp, () => { m_Scrubbing = false; player.SetPaused(m_ScrubWasPaused); });
        }
        return s;
    }
}
