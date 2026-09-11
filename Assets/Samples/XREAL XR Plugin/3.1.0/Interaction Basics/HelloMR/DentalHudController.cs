using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Unity.XR.XREAL;

namespace Unity.XR.XREAL.Samples
{
    public sealed class DentalHudController : MonoBehaviour
    {
        const float CanvasScale = 0.001f;
        const float RingRadiusMm = 56f;
        const float LateralDisplayMaxMm = 2f;
        const float CrosshairDegGain = 4f;
        const float CrosshairDegCap = 28f;
        const float BarWidthMm = 220f;
        const float BarHeightMm = 10f;

        static readonly Color Plate = new Color(11f / 255f, 15f / 255f, 20f / 255f, 0.62f);
        static readonly Color TextPrimary = new Color(0.969f, 0.969f, 0.949f, 1f);
        static readonly Color TextMuted = new Color(0.604f, 0.639f, 0.698f, 1f);
        static readonly Color Green = new Color(0.239f, 0.863f, 0.592f, 1f);
        static readonly Color Amber = new Color(0.961f, 0.773f, 0.094f, 1f);
        static readonly Color Red = new Color(1f, 0.302f, 0.302f, 1f);
        static readonly Color Gray = new Color(0.604f, 0.639f, 0.698f, 1f);
        static readonly Color FrameCyan = new Color(0.361f, 0.882f, 1f, 0.35f);

        [SerializeField]
        float m_PlaneDistanceMeters = 1.80f;

        [SerializeField]
        Vector2 m_CanvasMillimeters = new Vector2(1000f, 560f);

        [SerializeField]
        float m_PlannedDepthMm = 10f;

        [SerializeField]
        bool m_HudVisible = true;

        [SerializeField]
        bool m_WidgetFrameVisible = true;

        [SerializeField]
        DentalToleranceSettings m_Tolerance = DentalToleranceSettings.Default;

        static DentalHudController s_Instance;

        readonly DentalNavigationBand m_Band = new DentalNavigationBand();

        Transform m_HudRoot;
        Canvas m_Canvas;
        HudViews m_Views;
        Sprite m_CircleSprite;
        Sprite m_RingSprite;
        Sprite m_SquareRingSprite;
        Sprite m_WhiteSprite;
        TMP_FontAsset m_Font;
        Camera m_Camera;
        DentalHudEvaluation m_LastEvaluation;

        public static DentalHudController Instance => s_Instance;

        public bool HudVisible => m_HudVisible;

        public bool WidgetFrameVisible => m_WidgetFrameVisible;

        public DentalHudEvaluation LastEvaluation => m_LastEvaluation;

        public static DentalHudController EnsureInstance()
        {
            if (s_Instance != null)
                return s_Instance;

            var existing = FindObjectOfType<DentalHudController>();
            if (existing != null)
            {
                s_Instance = existing;
                return s_Instance;
            }

            var obj = new GameObject("Dental HUD");
            s_Instance = obj.AddComponent<DentalHudController>();
            return s_Instance;
        }

        sealed class HudViews
        {
            public CanvasGroup RootGroup;
            public GameObject AlarmRoot;
            public GameObject WidgetFrameRoot;
            public Image LinkDot;
            public TMP_Text LinkLabel;
            public TMP_Text DatasetLabel;
            public TMP_Text AgeLabel;
            public TMP_Text AlarmText;
            public Image DepthPlate;
            public TMP_Text DepthTitle;
            public TMP_Text DepthValue;
            public TMP_Text DepthUnit;
            public RectTransform BarFill;
            public Image BarFillImage;
            public Image RingOuter;
            public Image RingMid;
            public Image RingInner;
            public Image MarkerImage;
            public Image CrosshairH;
            public Image CrosshairV;
            public RectTransform Crosshair;
            public RectTransform Marker;
            public TMP_Text ChannelStatus;
            public TMP_Text LateralRead;
            public TMP_Text AngleRead;
            public TMP_Text AngleValue;
            public TMP_Text AngleUnit;
            public RectTransform AngleNeedle;
            public Image AngleNeedleImage;
        }

        void Awake()
        {
            if (s_Instance != null && s_Instance != this)
            {
                Destroy(this);
                return;
            }

            s_Instance = this;
            DentalNavigationState.EnsureInstance();
            m_Font = TMP_Settings.defaultFontAsset;
            if (m_Font == null)
                Debug.LogError("DentalHudController: TMP default font is missing.");

            BuildSprites();
            BuildTree();
            SetHudVisible(m_HudVisible);
        }

        void Start()
        {
            StartCoroutine(AttachWhenCameraReady());
        }

        void Update()
        {
            if (m_Views == null)
                return;

            AttachToCameraIfNeeded();

            var state = DentalNavigationState.Instance;
            if (state == null)
                return;

            var snap = state.Capture(Time.realtimeSinceStartup);
            m_LastEvaluation = m_Band.Evaluate(snap, m_Tolerance);
            state.SetEvaluation(m_LastEvaluation);

            ApplyStatusCapsule(snap, m_LastEvaluation);
            ApplyAlarmBar(m_LastEvaluation);
            ApplyWidgetFrame();
            ApplyDepthCard(snap, m_LastEvaluation);
            ApplyBullseyeCard(snap, m_LastEvaluation);
            ApplyAngleCard(snap, m_LastEvaluation);
        }

        IEnumerator AttachWhenCameraReady()
        {
            for (var i = 0; i < 120 && m_Camera == null; i++)
            {
                m_Camera = XREALUtility.MainCamera != null ? XREALUtility.MainCamera : Camera.main;
                if (m_Camera != null)
                    break;

                yield return null;
            }

            AttachToCameraIfNeeded();
        }

        void AttachToCameraIfNeeded()
        {
            var camera = XREALUtility.MainCamera != null ? XREALUtility.MainCamera : Camera.main;
            if (camera == null || m_HudRoot == null)
                return;

            if (m_Camera == camera && m_HudRoot.parent == camera.transform)
                return;

            m_Camera = camera;
            m_HudRoot.SetParent(camera.transform, false);
            m_HudRoot.localPosition = new Vector3(0f, 0f, m_PlaneDistanceMeters);
            m_HudRoot.localRotation = Quaternion.identity;
            m_HudRoot.localScale = Vector3.one;
            if (m_Canvas != null)
                m_Canvas.worldCamera = camera;
        }

        void OnDestroy()
        {
            if (s_Instance == this)
                s_Instance = null;

            DestroySprite(m_CircleSprite);
            DestroySprite(m_RingSprite);
            DestroySprite(m_SquareRingSprite);
            DestroySprite(m_WhiteSprite);
        }

        public void SetHudVisible(bool visible)
        {
            m_HudVisible = visible;
            if (m_Views == null || m_Views.RootGroup == null)
                return;

            m_Views.RootGroup.alpha = visible ? 1f : 0f;
            m_Views.RootGroup.interactable = false;
            m_Views.RootGroup.blocksRaycasts = false;
        }

        public void SetWidgetFrameVisible(bool visible)
        {
            m_WidgetFrameVisible = visible;
            ApplyWidgetFrame();
        }

        void BuildTree()
        {
            m_HudRoot = new GameObject("DentalHudRoot").transform;
            m_HudRoot.SetParent(transform, false);

            var canvasGo = new GameObject("DentalHudCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            canvasGo.transform.SetParent(m_HudRoot, false);
            var canvasRt = canvasGo.GetComponent<RectTransform>();
            StretchIdentity(canvasRt);
            canvasRt.sizeDelta = m_CanvasMillimeters;
            canvasRt.localScale = Vector3.one * CanvasScale;

            m_Canvas = canvasGo.GetComponent<Canvas>();
            m_Canvas.renderMode = RenderMode.WorldSpace;
            m_Canvas.sortingOrder = 20;
            m_Canvas.additionalShaderChannels =
                AdditionalCanvasShaderChannels.TexCoord1
                | AdditionalCanvasShaderChannels.Normal
                | AdditionalCanvasShaderChannels.Tangent;

            m_Views = new HudViews { RootGroup = canvasGo.GetComponent<CanvasGroup>() };
            m_Views.RootGroup.interactable = false;
            m_Views.RootGroup.blocksRaycasts = false;

            BuildStatusCapsule(canvasRt);
            BuildAlarmBar(canvasRt);
            BuildWidgetFrame(canvasRt);
            BuildDepthCard(canvasRt);
            BuildBullseyeCard(canvasRt);
            BuildAngleCard(canvasRt);
        }

        void BuildStatusCapsule(RectTransform canvas)
        {
            var card = CreateCard(canvas, "StatusCapsule", new Vector2(0f, 230f), new Vector2(240f, 36f), Plate);
            m_Views.LinkDot = CreateImage(card, "LinkDot", new Vector2(-104f, 0f), new Vector2(14f, 14f), m_CircleSprite, Gray);
            m_Views.LinkLabel = CreateTmp(card, "LinkLabel", new Vector2(-48f, 0f), new Vector2(90f, 32f),
                18f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, TextPrimary);
            m_Views.DatasetLabel = CreateTmp(card, "DatasetLabel", new Vector2(42f, 0f), new Vector2(80f, 32f),
                16f, FontStyles.Normal, TextAlignmentOptions.Center, TextMuted);
            m_Views.AgeLabel = CreateTmp(card, "AgeLabel", new Vector2(102f, 0f), new Vector2(50f, 32f),
                14f, FontStyles.Normal, TextAlignmentOptions.MidlineRight, TextMuted);
            m_Views.LinkLabel.text = "未连接";
            m_Views.DatasetLabel.text = "—";
            m_Views.AgeLabel.text = string.Empty;
        }

        void BuildAlarmBar(RectTransform canvas)
        {
            var card = CreateCard(canvas, "AlarmBar", new Vector2(0f, 188f), new Vector2(440f, 40f),
                new Color(Red.r, Red.g, Red.b, 0.85f));
            m_Views.AlarmRoot = card.gameObject;
            m_Views.AlarmText = CreateTmp(card, "AlarmText", Vector2.zero, new Vector2(416f, 36f),
                22f, FontStyles.Bold, TextAlignmentOptions.Center, TextPrimary);
            m_Views.AlarmRoot.SetActive(false);
        }

        void BuildWidgetFrame(RectTransform canvas)
        {
            var card = CreateUiObject(canvas, "WidgetFrame");
            card.anchoredPosition = new Vector2(320f, -24f);
            card.sizeDelta = new Vector2(168f, 168f);
            var image = card.gameObject.AddComponent<Image>();
            image.sprite = m_SquareRingSprite;
            image.color = FrameCyan;
            image.raycastTarget = false;
            m_Views.WidgetFrameRoot = card.gameObject;
        }

        void BuildDepthCard(RectTransform canvas)
        {
            var card = CreateCard(canvas, "DepthCard", new Vector2(-320f, -200f), new Vector2(280f, 160f), Plate);
            m_Views.DepthPlate = card.GetComponent<Image>();
            m_Views.DepthTitle = CreateTmp(card, "Title", new Vector2(0f, 58f), new Vector2(240f, 28f),
                20f, FontStyles.Normal, TextAlignmentOptions.Center, TextMuted);
            m_Views.DepthValue = CreateTmp(card, "Value", new Vector2(-18f, 10f), new Vector2(180f, 72f),
                64f, FontStyles.Bold, TextAlignmentOptions.Center, TextPrimary);
            m_Views.DepthUnit = CreateTmp(card, "Unit", new Vector2(96f, 2f), new Vector2(56f, 36f),
                22f, FontStyles.Normal, TextAlignmentOptions.MidlineLeft, TextMuted);
            m_Views.DepthTitle.text = "剩余深度";
            m_Views.DepthUnit.text = "mm";

            var track = CreateImage(card, "BarTrack", new Vector2(0f, -48f), new Vector2(BarWidthMm, BarHeightMm),
                m_WhiteSprite, new Color(0.165f, 0.192f, 0.235f, 1f));
            var trackRt = track.rectTransform;

            m_Views.BarFillImage = CreateImage(trackRt, "BarFill", Vector2.zero, new Vector2(0f, BarHeightMm),
                m_WhiteSprite, TextPrimary);
            m_Views.BarFill = m_Views.BarFillImage.rectTransform;
            m_Views.BarFill.anchorMin = new Vector2(0f, 0.5f);
            m_Views.BarFill.anchorMax = new Vector2(0f, 0.5f);
            m_Views.BarFill.pivot = new Vector2(0f, 0.5f);
            m_Views.BarFill.anchoredPosition = Vector2.zero;

            CreateImage(trackRt, "TargetTick", new Vector2(BarWidthMm * 0.5f, 0f),
                new Vector2(3f, 16f), m_WhiteSprite, TextPrimary);
        }

        void BuildBullseyeCard(RectTransform canvas)
        {
            var card = CreateCard(canvas, "BullseyeCard", new Vector2(0f, -200f), new Vector2(320f, 160f), Plate);
            CreateTmp(card, "Title", new Vector2(-90f, 58f), new Vector2(120f, 24f),
                18f, FontStyles.Normal, TextAlignmentOptions.MidlineLeft, TextMuted).text = "通道";
            m_Views.ChannelStatus = CreateTmp(card, "Status", new Vector2(80f, 58f), new Vector2(140f, 24f),
                18f, FontStyles.Bold, TextAlignmentOptions.MidlineRight, TextPrimary);

            var origin = CreateUiObject(card, "BullseyeOrigin");
            origin.anchoredPosition = new Vector2(-70f, -8f);
            origin.sizeDelta = Vector2.zero;

            m_Views.RingOuter = CreateImage(origin, "RingOuter", Vector2.zero, new Vector2(120f, 120f), m_RingSprite, Gray);
            m_Views.RingMid = CreateImage(origin, "RingMid", Vector2.zero, new Vector2(80f, 80f), m_RingSprite, Gray);
            m_Views.RingInner = CreateImage(origin, "RingInner", Vector2.zero, new Vector2(48f, 48f), m_RingSprite, Gray);

            m_Views.Crosshair = CreateUiObject(origin, "Crosshair");
            m_Views.Crosshair.sizeDelta = Vector2.zero;
            m_Views.CrosshairH = CreateImage(m_Views.Crosshair, "HBar", Vector2.zero, new Vector2(90f, 2f), m_WhiteSprite, TextPrimary);
            m_Views.CrosshairV = CreateImage(m_Views.Crosshair, "VBar", Vector2.zero, new Vector2(2f, 90f), m_WhiteSprite, TextPrimary);

            m_Views.MarkerImage = CreateImage(origin, "Marker", Vector2.zero, new Vector2(12f, 12f), m_CircleSprite, Green);
            m_Views.Marker = m_Views.MarkerImage.rectTransform;

            m_Views.LateralRead = CreateTmp(card, "LateralRead", new Vector2(90f, 12f), new Vector2(130f, 36f),
                28f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, TextPrimary);
            m_Views.AngleRead = CreateTmp(card, "AngleRead", new Vector2(90f, -28f), new Vector2(130f, 36f),
                22f, FontStyles.Normal, TextAlignmentOptions.MidlineLeft, TextMuted);
        }

        void BuildAngleCard(RectTransform canvas)
        {
            var card = CreateCard(canvas, "AngleCard", new Vector2(320f, -200f), new Vector2(280f, 160f), Plate);
            CreateTmp(card, "Title", new Vector2(0f, 58f), new Vector2(240f, 28f),
                20f, FontStyles.Normal, TextAlignmentOptions.Center, TextMuted).text = "轴向偏差";
            m_Views.AngleValue = CreateTmp(card, "Value", new Vector2(0f, 8f), new Vector2(240f, 72f),
                64f, FontStyles.Bold, TextAlignmentOptions.Center, TextPrimary);
            m_Views.AngleUnit = CreateTmp(card, "Unit", new Vector2(0f, -36f), new Vector2(80f, 28f),
                22f, FontStyles.Normal, TextAlignmentOptions.Center, TextMuted);
            m_Views.AngleUnit.text = "°";

            var arc = CreateUiObject(card, "Arc");
            arc.anchoredPosition = new Vector2(0f, -58f);
            CreateImage(arc, "ArcTrack", Vector2.zero, new Vector2(160f, 4f), m_WhiteSprite,
                new Color(0.165f, 0.192f, 0.235f, 1f));
            m_Views.AngleNeedleImage = CreateImage(arc, "Needle", Vector2.zero, new Vector2(4f, 28f),
                m_WhiteSprite, TextPrimary);
            m_Views.AngleNeedle = m_Views.AngleNeedleImage.rectTransform;
            m_Views.AngleNeedle.pivot = new Vector2(0.5f, 0f);
            m_Views.AngleNeedle.anchoredPosition = Vector2.zero;
        }

        void ApplyStatusCapsule(DentalNavigationSnapshot snap, DentalHudEvaluation eval)
        {
            string label;
            Color dot;
            if (snap.Link == DentalLinkState.Lost || snap.Link == DentalLinkState.Idle)
            {
                label = "未连接";
                dot = Red;
            }
            else if (snap.Link == DentalLinkState.Connecting)
            {
                label = "连接中";
                dot = Amber;
            }
            else if (eval.DashNumbers || snap.IsStale || eval.Overall == DentalMetricGrade.Stale)
            {
                label = "数据中断";
                dot = Gray;
            }
            else
            {
                label = "已连接";
                dot = Green;
            }

            m_Views.LinkLabel.text = label;
            m_Views.LinkDot.color = dot;
            m_Views.DatasetLabel.text = Truncate(snap.DatasetId, 10);
            m_Views.AgeLabel.text = eval.ShowAge ? $"{Mathf.RoundToInt(snap.AgeSeconds * 1000f)}ms" : string.Empty;
        }

        void ApplyAlarmBar(DentalHudEvaluation eval)
        {
            var show = eval.ShowAlarm && !string.IsNullOrEmpty(eval.AlarmText);
            if (m_Views.AlarmRoot.activeSelf != show)
                m_Views.AlarmRoot.SetActive(show);
            if (show)
                m_Views.AlarmText.text = eval.AlarmText;
        }

        void ApplyWidgetFrame()
        {
            if (m_Views.WidgetFrameRoot == null)
                return;

            if (m_Views.WidgetFrameRoot.activeSelf != m_WidgetFrameVisible)
                m_Views.WidgetFrameRoot.SetActive(m_WidgetFrameVisible);
        }

        void ApplyDepthCard(DentalNavigationSnapshot snap, DentalHudEvaluation eval)
        {
            var overshoot = snap.HasMetadata && !eval.DashNumbers && snap.DepthMm < 0f;
            m_Views.DepthTitle.text = overshoot ? "超过目标" : "剩余深度";
            m_Views.DepthUnit.text = eval.DashNumbers ? string.Empty : "mm";
            m_Views.DepthValue.text = eval.DashNumbers ? "—" : FormatFixed(Mathf.Abs(snap.DepthMm));
            m_Views.DepthValue.color = ColorForGrade(eval.Depth, false);
            m_Views.DepthPlate.color = eval.Depth == DentalMetricGrade.Red
                ? new Color(Red.r, Red.g, Red.b, 0.55f)
                : Plate;

            var planned = Mathf.Max(0.01f, m_PlannedDepthMm);
            var t = eval.DashNumbers ? 0f : Mathf.Clamp01(Mathf.Abs(snap.DepthMm) / planned);
            if (overshoot)
                t = 1f;
            m_Views.BarFill.sizeDelta = new Vector2(BarWidthMm * t, BarHeightMm);
            m_Views.BarFillImage.color = ColorForGrade(eval.Depth, false);
        }

        void ApplyBullseyeCard(DentalNavigationSnapshot snap, DentalHudEvaluation eval)
        {
            var ring = ColorForGrade(eval.Lateral, true);
            if (eval.DashNumbers || eval.Overall == DentalMetricGrade.Stale)
                ring.a = 0.35f;

            m_Views.RingOuter.color = ring;
            m_Views.RingMid.color = ring;
            m_Views.RingInner.color = ring;

            var cross = eval.DashNumbers ? Gray : ColorForGrade(eval.Angle, true);
            m_Views.CrosshairH.color = cross;
            m_Views.CrosshairV.color = cross;

            var visualAngle = Mathf.Clamp(snap.AngleDeg * CrosshairDegGain, -CrosshairDegCap, CrosshairDegCap);
            m_Views.Crosshair.localEulerAngles = eval.DashNumbers
                ? Vector3.zero
                : new Vector3(0f, 0f, visualAngle);

            var mmToPx = RingRadiusMm / LateralDisplayMaxMm;
            var y = eval.DashNumbers ? 0f : -Mathf.Min(snap.LateralMm, LateralDisplayMaxMm) * mmToPx;
            m_Views.Marker.anchoredPosition = new Vector2(0f, y);
            m_Views.Marker.gameObject.SetActive(!eval.DashNumbers);
            m_Views.MarkerImage.sprite = eval.Lateral == DentalMetricGrade.Red ? m_WhiteSprite : m_CircleSprite;
            m_Views.MarkerImage.color = ColorForGrade(eval.Lateral, true);

            m_Views.ChannelStatus.text = OverallPhrase(eval.Overall);
            m_Views.ChannelStatus.color = ColorForGrade(eval.Overall, true);
            m_Views.LateralRead.text = eval.DashNumbers ? "—" : $"{FormatFixed(snap.LateralMm)} mm";
            m_Views.AngleRead.text = eval.DashNumbers ? "—" : $"{FormatFixed(snap.AngleDeg)}°";
            m_Views.LateralRead.color = ColorForGrade(eval.Lateral, true);
            m_Views.AngleRead.color = ColorForGrade(eval.Angle, true);
        }

        void ApplyAngleCard(DentalNavigationSnapshot snap, DentalHudEvaluation eval)
        {
            m_Views.AngleValue.text = eval.DashNumbers ? "—" : FormatFixed(snap.AngleDeg);
            m_Views.AngleUnit.text = eval.DashNumbers ? string.Empty : "°";
            m_Views.AngleValue.color = ColorForGrade(eval.Angle, true);
            var needle = Mathf.Clamp(snap.AngleDeg * CrosshairDegGain, -CrosshairDegCap, CrosshairDegCap);
            m_Views.AngleNeedle.localEulerAngles = eval.DashNumbers ? Vector3.zero : new Vector3(0f, 0f, needle);
            m_Views.AngleNeedleImage.color = ColorForGrade(eval.Angle, true);
        }

        RectTransform CreateCard(RectTransform parent, string name, Vector2 pos, Vector2 size, Color plate)
        {
            var rt = CreateUiObject(parent, name);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            var image = rt.gameObject.AddComponent<Image>();
            image.sprite = m_WhiteSprite;
            image.color = plate;
            image.raycastTarget = false;
            return rt;
        }

        Image CreateImage(RectTransform parent, string name, Vector2 pos, Vector2 size, Sprite sprite, Color color)
        {
            var rt = CreateUiObject(parent, name);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            var image = rt.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            image.preserveAspect = true;
            return image;
        }

        TMP_Text CreateTmp(
            RectTransform parent,
            string name,
            Vector2 pos,
            Vector2 size,
            float fontSize,
            FontStyles style,
            TextAlignmentOptions align,
            Color color)
        {
            var rt = CreateUiObject(parent, name);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.font = m_Font;
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.alignment = align;
            tmp.color = color;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.raycastTarget = false;
            if (m_Font != null)
            {
                tmp.outlineWidth = 0.2f;
                tmp.outlineColor = Color.black;
                if (tmp.fontMaterial != null)
                    tmp.fontMaterial.EnableKeyword("OUTLINE_ON");
            }
            return tmp;
        }

        RectTransform CreateUiObject(RectTransform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            StretchIdentity(rt);
            return rt;
        }

        static void StretchIdentity(RectTransform rt)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition3D = Vector3.zero;
            rt.localRotation = Quaternion.identity;
            rt.localScale = Vector3.one;
        }

        void BuildSprites()
        {
            m_WhiteSprite = CreateSolidSprite(8);
            m_CircleSprite = CreateRadialSprite(64, -1f);
            m_RingSprite = CreateRadialSprite(64, 0.78f);
            m_SquareRingSprite = CreateSquareRingSprite(64, 4);
        }

        static Sprite CreateSolidSprite(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[size * size];
            for (var i = 0; i < pixels.Length; i++)
                pixels[i] = Color.white;
            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        }

        static Sprite CreateRadialSprite(int size, float inner01)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[size * size];
            var cx = (size - 1) * 0.5f;
            var outer = cx - 1.25f;
            var inner = inner01 < 0f ? -8f : outer * inner01;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var d = Vector2.Distance(new Vector2(x, y), new Vector2(cx, cx));
                    var aOuter = Mathf.Clamp01(outer + 1.25f - d);
                    var aInner = inner01 < 0f ? 0f : Mathf.Clamp01(inner + 1.25f - d);
                    var a = Mathf.Clamp01(aOuter - aInner);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        }

        static Sprite CreateSquareRingSprite(int size, int thickness)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
            };
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var edge = x < thickness || y < thickness || x >= size - thickness || y >= size - thickness;
                    pixels[y * size + x] = edge ? Color.white : Color.clear;
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        }

        static void DestroySprite(Sprite sprite)
        {
            if (sprite == null)
                return;
            if (sprite.texture != null)
                Destroy(sprite.texture);
            Destroy(sprite);
        }

        static Color ColorForGrade(DentalMetricGrade grade, bool allowGreen)
        {
            switch (grade)
            {
                case DentalMetricGrade.Green:
                    return allowGreen ? Green : TextPrimary;
                case DentalMetricGrade.Amber:
                    return Amber;
                case DentalMetricGrade.Red:
                    return Red;
                case DentalMetricGrade.Stale:
                case DentalMetricGrade.Unavailable:
                    return Gray;
                default:
                    return TextPrimary;
            }
        }

        static string OverallPhrase(DentalMetricGrade overall)
        {
            switch (overall)
            {
                case DentalMetricGrade.Green:
                    return "在容差";
                case DentalMetricGrade.Amber:
                    return "接近";
                case DentalMetricGrade.Red:
                    return "超差";
                case DentalMetricGrade.Stale:
                    return "数据中断";
                default:
                    return "无数据";
            }
        }

        static string FormatFixed(float value)
        {
            return value.ToString("0.0");
        }

        static string Truncate(string text, int max)
        {
            if (string.IsNullOrEmpty(text) || text == "-" || text == "—")
                return "—";
            return text.Length <= max ? text : text.Substring(0, max - 1) + "…";
        }
    }
}
