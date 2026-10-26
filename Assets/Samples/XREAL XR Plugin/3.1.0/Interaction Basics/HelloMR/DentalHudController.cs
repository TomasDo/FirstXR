using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Unity.XR.XREAL;

namespace Unity.XR.XREAL.Samples
{
    /// <summary>
    /// Intra-operative view that can either follow the head or remain fixed at its current world
    /// pose. The CT plane and target retain their buccal/lingual/mesial/distal orientation in both
    /// modes.
    /// </summary>
    public sealed class DentalHudController : MonoBehaviour
    {
        const float CanvasScale = 0.001f;
        const float PositionRingRadiusMm = 78f;
        const float AngleMarkerRadiusMm = 92f;
        const float DepthTrackHeightMm = 236f;

        static readonly Color Plate = new Color(11f / 255f, 15f / 255f, 20f / 255f, 0.72f);
        static readonly Color TextPrimary = new Color(0.969f, 0.969f, 0.949f, 1f);
        static readonly Color TextMuted = new Color(0.604f, 0.639f, 0.698f, 1f);
        static readonly Color Green = new Color(0.239f, 0.863f, 0.592f, 1f);
        static readonly Color Amber = new Color(0.961f, 0.773f, 0.094f, 1f);
        static readonly Color Red = new Color(1f, 0.302f, 0.302f, 1f);
        static readonly Color Gray = new Color(0.604f, 0.639f, 0.698f, 1f);
        static readonly Color CtEmpty = new Color(0.035f, 0.043f, 0.055f, 1f);

        [SerializeField]
        Vector3 m_DefaultLocalPositionMeters = new Vector3(0f, -0.13f, 1.80f);

        [SerializeField]
        Vector2 m_CanvasMillimeters = new Vector2(1000f, 560f);

        [SerializeField]
        bool m_HudVisible = true;

        [SerializeField]
        bool m_WidgetFrameVisible;

        static DentalHudController s_Instance;

        readonly DentalNavigationBand m_Band = new DentalNavigationBand();

        Transform m_HudRoot;
        Canvas m_Canvas;
        HudViews m_Views;
        Sprite m_CircleSprite;
        Sprite m_RingSprite;
        Sprite m_WhiteSprite;
        Sprite m_TriangleSprite;
        TMP_FontAsset m_Font;
        Camera m_Camera;
        DentalHudEvaluation m_LastEvaluation;
        IDentalCtSliceSource m_CtSource;
        Texture m_LastCtTexture;
        DentalNavigationSnapshot m_RenderedSnapshot;
        DentalHudEvaluation m_RenderedEvaluation;
        bool m_HasRenderedSnapshot;
        bool m_HasRenderedCtStatus;
        bool m_RenderedCtHasVolume;
        bool m_RenderedCtPatientPlane;
        int m_RenderedCtIndex;
        int m_RenderedCtCount;
        float m_RenderedCtOffset;
        string m_RenderedCtMessage;

        public static DentalHudController Instance => s_Instance;
        public bool HudVisible => m_HudVisible;
        public bool WidgetFrameVisible => m_WidgetFrameVisible;
        public DentalHudEvaluation LastEvaluation => m_LastEvaluation;

        public static DentalHudController EnsureInstance()
        {
            if (s_Instance != null)
                return s_Instance;

            var existing = FindFirstObjectByType<DentalHudController>();
            if (existing != null)
            {
                s_Instance = existing;
                return existing;
            }

            var obj = new GameObject("Dental HUD");
            s_Instance = obj.AddComponent<DentalHudController>();
            return s_Instance;
        }

        sealed class HudViews
        {
            public CanvasGroup RootGroup;
            public Image LinkDot;
            public TMP_Text LinkLabel;
            public TMP_Text ContextLabel;
            public TMP_Text AgeLabel;
            public GameObject AlarmRoot;
            public Image AlarmPlate;
            public TMP_Text AlarmText;

            public RawImage CtImage;
            public TMP_Text CtStatus;
            public TMP_Text ChannelStatus;
            public Image PositionOuterRing;
            public Image PositionMidRing;
            public Image PositionInnerRing;
            public Image AngleRing;
            public RectTransform PositionMarker;
            public Image PositionMarkerImage;
            public RectTransform AngleDirectionMarker;
            public Image AngleDirectionImage;
            public TMP_Text LateralRead;
            public TMP_Text AngleRead;

            public Image DepthPlate;
            public TMP_Text DepthTitle;
            public TMP_Text DepthValue;
            public TMP_Text DepthUnit;
            public TMP_Text CurrentDepth;
            public TMP_Text TargetDepth;
            public TMP_Text DepthOutOfRange;
            public RectTransform DepthTrack;
            public RectTransform DepthFill;
            public Image DepthFillImage;
            public RectTransform DepthCurrentMarker;
            public Image DepthCurrentMarkerImage;
            public RectTransform DepthTargetTick;
            public TMP_Text DepthScaleTop;
            public TMP_Text DepthScaleTarget;
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
            m_CtSource = DentalCtVolumeService.EnsureInstance();
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

            ApplyAnchorAndLayout();

            var state = DentalNavigationState.Instance;
            if (state == null)
                return;

            var snap = state.Capture(Time.realtimeSinceStartup);
            m_LastEvaluation = m_Band.Evaluate(snap);
            state.SetEvaluation(m_LastEvaluation);

            // Evaluation still runs every frame, including while hidden, so aging and safety
            // alarms remain immediate. Only unchanged visual work is skipped.
            if (!m_HudVisible)
                return;

            ApplyStatus(snap, m_LastEvaluation);
            ApplyAlarm(m_LastEvaluation);
            ApplyCtAndTarget(snap, m_LastEvaluation);
            if (!m_HasRenderedSnapshot || !SameDepthPresentation(m_RenderedSnapshot, snap,
                    m_RenderedEvaluation, m_LastEvaluation))
                ApplyDepth(snap, m_LastEvaluation);
            m_RenderedSnapshot = snap;
            m_RenderedEvaluation = m_LastEvaluation;
            m_HasRenderedSnapshot = true;
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

            ApplyAnchorAndLayout();
        }

        void ApplyAnchorAndLayout()
        {
            var camera = XREALUtility.MainCamera != null ? XREALUtility.MainCamera : Camera.main;
            if (camera == null || m_HudRoot == null)
                return;

            var layout = DentalDisplayLayoutController.Instance;
            var followsHead = layout == null
                || layout.ContentAnchorMode == DentalContentAnchorMode.FollowHead;
            m_Camera = camera;
            if (m_Canvas != null && m_Canvas.worldCamera != camera)
                m_Canvas.worldCamera = camera;

            if (followsHead)
            {
                if (m_HudRoot.parent != camera.transform)
                    m_HudRoot.SetParent(camera.transform, false);
                var position = layout != null ? layout.HudLocalPositionMeters : m_DefaultLocalPositionMeters;
                if (m_HudRoot.localPosition != position)
                    m_HudRoot.localPosition = position;
                if (m_HudRoot.localRotation != Quaternion.identity)
                    m_HudRoot.localRotation = Quaternion.identity;
                if (m_HudRoot.localScale != Vector3.one)
                    m_HudRoot.localScale = Vector3.one;
            }
            else if (m_HudRoot.parent != null)
            {
                if (m_HudRoot.parent == transform)
                {
                    m_HudRoot.SetParent(camera.transform, false);
                    m_HudRoot.localPosition = layout != null
                        ? layout.HudLocalPositionMeters
                        : m_DefaultLocalPositionMeters;
                    m_HudRoot.localRotation = Quaternion.identity;
                    m_HudRoot.localScale = Vector3.one;
                }
                // Preserve the rendered pose at the exact moment Beam Pro selects "Hover".
                m_HudRoot.SetParent(null, true);
            }

            if (layout != null && m_HudVisible != layout.HudVisible)
                SetHudVisible(layout.HudVisible);
        }

        void OnDestroy()
        {
            if (s_Instance == this)
                s_Instance = null;

            if (m_HudRoot != null)
                Destroy(m_HudRoot.gameObject);

            DestroySprite(m_CircleSprite);
            DestroySprite(m_RingSprite);
            DestroySprite(m_WhiteSprite);
            DestroySprite(m_TriangleSprite);
        }

        public void SetHudVisible(bool visible)
        {
            if (m_HudVisible != visible)
                m_HasRenderedSnapshot = false;
            m_HudVisible = visible;
            if (m_Views == null || m_Views.RootGroup == null)
                return;

            m_Views.RootGroup.alpha = visible ? 1f : 0f;
            m_Views.RootGroup.interactable = false;
            m_Views.RootGroup.blocksRaycasts = false;
        }

        // Kept for the Beam Pro surface and old scene serialization. The model is
        // independently positioned, so the v2 HUD no longer draws a placeholder frame.
        public void SetWidgetFrameVisible(bool visible)
        {
            m_WidgetFrameVisible = visible;
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
            m_Canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1
                | AdditionalCanvasShaderChannels.Normal
                | AdditionalCanvasShaderChannels.Tangent;

            m_Views = new HudViews { RootGroup = canvasGo.GetComponent<CanvasGroup>() };
            m_Views.RootGroup.interactable = false;
            m_Views.RootGroup.blocksRaycasts = false;

            BuildStatus(canvasRt);
            BuildAlarm(canvasRt);
            BuildCtTargetCard(canvasRt);
            BuildDepthCard(canvasRt);
        }

        void BuildStatus(RectTransform canvas)
        {
            var card = CreateCard(canvas, "StatusCapsule", new Vector2(0f, 244f), new Vector2(520f, 34f), Plate);
            m_Views.LinkDot = CreateImage(card, "LinkDot", new Vector2(-242f, 0f), new Vector2(12f, 12f), m_CircleSprite, Gray);
            m_Views.LinkLabel = CreateTmp(card, "LinkLabel", new Vector2(-184f, 0f), new Vector2(96f, 30f), 17f,
                FontStyles.Bold, TextAlignmentOptions.MidlineLeft, TextPrimary);
            m_Views.ContextLabel = CreateTmp(card, "Context", new Vector2(6f, 0f), new Vector2(270f, 30f), 15f,
                FontStyles.Normal, TextAlignmentOptions.Center, TextMuted);
            m_Views.AgeLabel = CreateTmp(card, "Age", new Vector2(226f, 0f), new Vector2(58f, 30f), 14f,
                FontStyles.Normal, TextAlignmentOptions.MidlineRight, TextMuted);
        }

        void BuildAlarm(RectTransform canvas)
        {
            var card = CreateCard(canvas, "AlarmBar", new Vector2(0f, 202f), new Vector2(600f, 38f),
                new Color(Red.r, Red.g, Red.b, 0.86f));
            m_Views.AlarmRoot = card.gameObject;
            m_Views.AlarmPlate = card.GetComponent<Image>();
            m_Views.AlarmText = CreateTmp(card, "Alarm", Vector2.zero, new Vector2(576f, 34f), 20f,
                FontStyles.Bold, TextAlignmentOptions.Center, TextPrimary);
            m_Views.AlarmRoot.SetActive(false);
        }

        void BuildCtTargetCard(RectTransform canvas)
        {
            var card = CreateCard(canvas, "CtTargetCard", new Vector2(-112f, -36f), new Vector2(610f, 430f), Plate);
            CreateTmp(card, "Title", new Vector2(-250f, 192f), new Vector2(86f, 26f), 18f,
                FontStyles.Bold, TextAlignmentOptions.MidlineLeft, TextPrimary).text = "CT target";
            m_Views.ChannelStatus = CreateTmp(card, "ChannelStatus", new Vector2(212f, 192f), new Vector2(156f, 26f), 17f,
                FontStyles.Bold, TextAlignmentOptions.MidlineRight, TextPrimary);

            var imageRt = CreateUiObject(card, "CtViewport");
            imageRt.anchoredPosition = new Vector2(-78f, 18f);
            imageRt.sizeDelta = new Vector2(390f, 310f);
            var background = imageRt.gameObject.AddComponent<Image>();
            background.sprite = m_WhiteSprite;
            background.color = CtEmpty;
            background.raycastTarget = false;
            var ctImageRt = CreateUiObject(imageRt, "CtImage");
            ctImageRt.sizeDelta = imageRt.sizeDelta;
            m_Views.CtImage = ctImageRt.gameObject.AddComponent<RawImage>();
            m_Views.CtImage.color = Color.white;
            m_Views.CtImage.raycastTarget = false;

            var origin = CreateUiObject(imageRt, "BullseyeOrigin");
            origin.sizeDelta = Vector2.zero;
            m_Views.AngleRing = CreateImage(origin, "AngleRing", Vector2.zero, new Vector2(190f, 190f), m_RingSprite, Gray);
            m_Views.PositionOuterRing = CreateImage(origin, "PositionOuter", Vector2.zero, new Vector2(158f, 158f), m_RingSprite, Gray);
            m_Views.PositionMidRing = CreateImage(origin, "PositionMid", Vector2.zero, new Vector2(106f, 106f), m_RingSprite, Gray);
            m_Views.PositionInnerRing = CreateImage(origin, "PositionInner", Vector2.zero, new Vector2(54f, 54f), m_RingSprite, Gray);
            CreateImage(origin, "CrosshairH", Vector2.zero, new Vector2(176f, 2f), m_WhiteSprite, TextPrimary);
            CreateImage(origin, "CrosshairV", Vector2.zero, new Vector2(2f, 176f), m_WhiteSprite, TextPrimary);

            m_Views.PositionMarkerImage = CreateImage(origin, "PositionMarker", Vector2.zero, new Vector2(15f, 15f), m_CircleSprite, Gray);
            m_Views.PositionMarker = m_Views.PositionMarkerImage.rectTransform;
            m_Views.AngleDirectionImage = CreateImage(origin, "AngleDirection", Vector2.zero, new Vector2(20f, 16f), m_TriangleSprite, Gray);
            m_Views.AngleDirectionMarker = m_Views.AngleDirectionImage.rectTransform;

            CreateTmp(imageRt, "Buccal", new Vector2(0f, 137f), new Vector2(64f, 24f), 17f,
                FontStyles.Bold, TextAlignmentOptions.Center, TextPrimary).text = "B";
            CreateTmp(imageRt, "Lingual", new Vector2(0f, -137f), new Vector2(64f, 24f), 17f,
                FontStyles.Bold, TextAlignmentOptions.Center, TextPrimary).text = "L";
            CreateTmp(imageRt, "Mesial", new Vector2(-170f, 0f), new Vector2(64f, 24f), 17f,
                FontStyles.Bold, TextAlignmentOptions.Center, TextPrimary).text = "M";
            CreateTmp(imageRt, "Distal", new Vector2(170f, 0f), new Vector2(64f, 24f), 17f,
                FontStyles.Bold, TextAlignmentOptions.Center, TextPrimary).text = "D";

            m_Views.LateralRead = CreateTmp(card, "LateralRead", new Vector2(218f, 64f), new Vector2(166f, 70f), 25f,
                FontStyles.Bold, TextAlignmentOptions.MidlineLeft, TextPrimary);
            m_Views.AngleRead = CreateTmp(card, "AngleRead", new Vector2(218f, -30f), new Vector2(166f, 70f), 25f,
                FontStyles.Bold, TextAlignmentOptions.MidlineLeft, TextPrimary);
            m_Views.CtStatus = CreateTmp(card, "CtStatus", new Vector2(0f, -190f), new Vector2(566f, 26f), 15f,
                FontStyles.Normal, TextAlignmentOptions.Center, TextMuted);
        }

        void BuildDepthCard(RectTransform canvas)
        {
            var card = CreateCard(canvas, "DepthCard", new Vector2(344f, -36f), new Vector2(260f, 430f), Plate);
            m_Views.DepthPlate = card.GetComponent<Image>();
            m_Views.DepthTitle = CreateTmp(card, "Title", new Vector2(0f, 192f), new Vector2(226f, 28f), 19f,
                FontStyles.Normal, TextAlignmentOptions.Center, TextMuted);
            m_Views.DepthValue = CreateTmp(card, "Value", new Vector2(-16f, 142f), new Vector2(168f, 66f), 54f,
                FontStyles.Bold, TextAlignmentOptions.Center, TextPrimary);
            m_Views.DepthUnit = CreateTmp(card, "Unit", new Vector2(90f, 134f), new Vector2(46f, 30f), 20f,
                FontStyles.Normal, TextAlignmentOptions.MidlineLeft, TextMuted);

            var trackImage = CreateImage(card, "DepthTrack", new Vector2(-54f, -44f), new Vector2(14f, DepthTrackHeightMm),
                m_WhiteSprite, new Color(0.165f, 0.192f, 0.235f, 1f));
            m_Views.DepthTrack = trackImage.rectTransform;
            m_Views.DepthFillImage = CreateImage(m_Views.DepthTrack, "DepthFill", Vector2.zero,
                new Vector2(10f, 0f), m_WhiteSprite, TextPrimary);
            m_Views.DepthFill = m_Views.DepthFillImage.rectTransform;
            m_Views.DepthFill.anchorMin = m_Views.DepthFill.anchorMax = new Vector2(0.5f, 1f);
            m_Views.DepthFill.pivot = new Vector2(0.5f, 1f);
            m_Views.DepthFill.anchoredPosition = Vector2.zero;

            m_Views.DepthCurrentMarkerImage = CreateImage(m_Views.DepthTrack, "CurrentMarker", Vector2.zero,
                new Vector2(46f, 4f), m_WhiteSprite, TextPrimary);
            m_Views.DepthCurrentMarker = m_Views.DepthCurrentMarkerImage.rectTransform;
            m_Views.DepthTargetTick = CreateImage(m_Views.DepthTrack, "Target", Vector2.zero,
                new Vector2(54f, 3f), m_WhiteSprite, TextPrimary).rectTransform;

            m_Views.DepthScaleTop = CreateTmp(card, "ScaleTop", new Vector2(-12f, 72f), new Vector2(64f, 24f), 14f,
                FontStyles.Normal, TextAlignmentOptions.MidlineLeft, TextMuted);
            m_Views.DepthScaleTarget = CreateTmp(card, "ScaleTarget", new Vector2(-4f, -146f), new Vector2(90f, 24f), 14f,
                FontStyles.Normal, TextAlignmentOptions.MidlineLeft, TextMuted);
            m_Views.CurrentDepth = CreateTmp(card, "CurrentDepth", new Vector2(64f, -22f), new Vector2(116f, 50f), 18f,
                FontStyles.Normal, TextAlignmentOptions.MidlineLeft, TextPrimary);
            m_Views.TargetDepth = CreateTmp(card, "TargetDepth", new Vector2(64f, -88f), new Vector2(116f, 50f), 18f,
                FontStyles.Normal, TextAlignmentOptions.MidlineLeft, TextMuted);
            m_Views.DepthOutOfRange = CreateTmp(card, "OutOfRange", new Vector2(-54f, -181f), new Vector2(86f, 24f), 15f,
                FontStyles.Bold, TextAlignmentOptions.Center, Red);
        }

        void ApplyStatus(DentalNavigationSnapshot snap, DentalHudEvaluation eval)
        {
            m_Views.LinkLabel.text = DentalConnectionPresentation.Label(snap.Link);
            m_Views.LinkDot.color = DentalConnectionPresentation.IndicatorColor(snap.Link, Green, Amber, Red);
            if (!m_HasRenderedSnapshot || m_RenderedSnapshot.HasContext != snap.HasContext
                || m_RenderedSnapshot.ToothId != snap.ToothId || m_RenderedSnapshot.PlanId != snap.PlanId
                || m_RenderedSnapshot.ToolId != snap.ToolId || m_RenderedSnapshot.DatasetId != snap.DatasetId)
            {
                m_Views.ContextLabel.text = snap.HasContext
                    ? string.Format("{0}  {1}  {2}", Short(snap.ToothId, 8), Short(snap.PlanId, 9), Short(snap.ToolId, 8))
                    : Short(snap.DatasetId, 18);
            }
            m_Views.AgeLabel.text = eval.ShowAge && !float.IsInfinity(snap.AgeSeconds)
                ? Mathf.RoundToInt(snap.AgeSeconds * 1000f) + "ms"
                : string.Empty;
        }

        void ApplyAlarm(DentalHudEvaluation eval)
        {
            var show = eval.ShowAlarm && !string.IsNullOrEmpty(eval.AlarmText);
            if (m_Views.AlarmRoot.activeSelf != show)
                m_Views.AlarmRoot.SetActive(show);
            if (!show)
                return;

            m_Views.AlarmText.text = eval.AlarmText;
            var grade = eval.Overall == DentalMetricGrade.Red ? Red : Amber;
            m_Views.AlarmPlate.color = new Color(grade.r, grade.g, grade.b, 0.86f);
        }

        void ApplyCtAndTarget(DentalNavigationSnapshot snap, DentalHudEvaluation eval)
        {
            if (m_CtSource == null)
                m_CtSource = DentalCtVolumeService.EnsureInstance();

            var texture = m_CtSource != null ? m_CtSource.CurrentSliceTexture : null;
            if (m_LastCtTexture != texture)
            {
                m_LastCtTexture = texture;
                m_Views.CtImage.texture = texture;
            }
            m_Views.CtImage.color = texture != null ? Color.white : Color.clear;
            ApplyCtStatus();
            if (m_HasRenderedSnapshot && SameTargetPresentation(m_RenderedSnapshot, snap,
                    m_RenderedEvaluation, eval))
                return;

            var positionColor = ColorForGrade(eval.Lateral, true);
            m_Views.PositionOuterRing.color = positionColor;
            m_Views.PositionMidRing.color = positionColor;
            m_Views.PositionInnerRing.color = positionColor;
            m_Views.AngleRing.color = ColorForGrade(eval.Angle, true);

            var hasPositionDirection = snap.HasNavigationFrame && snap.FrameValid
                && snap.HasLateralDirection && !eval.DashNumbers;
            var hasAngleDirection = snap.HasNavigationFrame && snap.FrameValid
                && snap.HasTiltDirection && !eval.DashNumbers;
            m_Views.PositionMarker.gameObject.SetActive(hasPositionDirection);
            m_Views.AngleDirectionMarker.gameObject.SetActive(hasAngleDirection && snap.AngleDeg > 0.001f);

            if (hasPositionDirection)
            {
                var displayMax = snap.HasThresholds
                    ? Mathf.Max(0.5f, snap.Thresholds.LateralRedMinMm * 1.25f)
                    : 2f;
                var position = ToAnatomicalDisplay(snap.LateralBuccalMm, snap.LateralMesialMm);
                if (position.magnitude > displayMax)
                    position = position.normalized * displayMax;
                m_Views.PositionMarker.anchoredPosition = position * (PositionRingRadiusMm / displayMax);
                m_Views.PositionMarkerImage.color = positionColor;
            }

            if (hasAngleDirection)
            {
                var tilt = ToAnatomicalDisplay(snap.TiltBuccalDeg, snap.TiltMesialDeg);
                if (tilt.sqrMagnitude > 0.000001f)
                {
                    var direction = tilt.normalized;
                    m_Views.AngleDirectionMarker.anchoredPosition = direction * AngleMarkerRadiusMm;
                    m_Views.AngleDirectionMarker.localEulerAngles = new Vector3(0f, 0f,
                        Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f);
                }
                m_Views.AngleDirectionImage.color = ColorForGrade(eval.Angle, true);
            }

            var lacksV2Semantics = snap.HasMetadata
                && (!snap.HasLateralDirection || !snap.HasTiltDirection || !snap.HasDepthBreakdown);
            m_Views.ChannelStatus.text = lacksV2Semantics
                ? "Direction unavailable"
                : OverallPhrase(eval.Overall);
            m_Views.ChannelStatus.color = lacksV2Semantics
                ? Amber
                : ColorForGrade(eval.Overall, true);

            if (eval.DashNumbers)
            {
                m_Views.LateralRead.text = "Position\n—";
                m_Views.AngleRead.text = "Angle\n—";
            }
            else
            {
                m_Views.LateralRead.text = string.Format("Position {0:0.0} mm\n{1}", snap.LateralMm,
                    snap.HasLateralDirection
                        ? DentalNavigationPresentation.DirectionPhrase(snap.LateralBuccalMm, snap.LateralMesialMm, "toward ")
                        : "Direction unavailable");
                m_Views.AngleRead.text = string.Format("Angle {0:0.0}°\n{1}", snap.AngleDeg,
                    snap.HasTiltDirection
                        ? DentalNavigationPresentation.DirectionPhrase(snap.TiltBuccalDeg, snap.TiltMesialDeg, "tilt toward ")
                        : "Direction unavailable");
            }
            m_Views.LateralRead.color = ColorForGrade(eval.Lateral, true);
            m_Views.AngleRead.color = ColorForGrade(eval.Angle, true);
        }

        void ApplyCtStatus()
        {
            var hasVolume = m_CtSource != null && m_CtSource.HasVolume;
            var patientPlane = hasVolume && m_CtSource.UsesPatientPlane;
            var index = hasVolume ? m_CtSource.SliceIndex : 0;
            var count = hasVolume ? m_CtSource.SliceCount : 0;
            var offset = hasVolume ? m_CtSource.SliceOffsetMm : 0f;
            var message = m_CtSource != null ? m_CtSource.StatusMessage : "CT not loaded";
            if (m_HasRenderedCtStatus && m_RenderedCtHasVolume == hasVolume
                && m_RenderedCtPatientPlane == patientPlane && m_RenderedCtIndex == index
                && m_RenderedCtCount == count && m_RenderedCtOffset == offset
                && m_RenderedCtMessage == message)
                return;
            m_HasRenderedCtStatus = true;
            m_RenderedCtHasVolume = hasVolume;
            m_RenderedCtPatientPlane = patientPlane;
            m_RenderedCtIndex = index;
            m_RenderedCtCount = count;
            m_RenderedCtOffset = offset;
            m_RenderedCtMessage = message;
            m_Views.CtStatus.text = hasVolume
                ? patientPlane
                    ? string.Format("Planned CT slice  {0:+0.0;-0.0;0.0} mm", offset)
                    : string.Format("CT {0}/{1}  {2:+0.0;-0.0;0.0} mm", index + 1, count, offset)
                : message;
        }

        static bool SameTargetPresentation(DentalNavigationSnapshot a, DentalNavigationSnapshot b,
            DentalHudEvaluation before, DentalHudEvaluation after)
        {
            return before.DashNumbers == after.DashNumbers && before.Lateral == after.Lateral
                && before.Angle == after.Angle && before.Overall == after.Overall
                && a.HasMetadata == b.HasMetadata && a.HasNavigationFrame == b.HasNavigationFrame
                && a.FrameValid == b.FrameValid && a.HasLateralDirection == b.HasLateralDirection
                && a.HasTiltDirection == b.HasTiltDirection && a.HasDepthBreakdown == b.HasDepthBreakdown
                && a.LateralMm == b.LateralMm && a.AngleDeg == b.AngleDeg
                && a.LateralBuccalMm == b.LateralBuccalMm && a.LateralMesialMm == b.LateralMesialMm
                && a.TiltBuccalDeg == b.TiltBuccalDeg && a.TiltMesialDeg == b.TiltMesialDeg
                && a.HasThresholds == b.HasThresholds
                && a.Thresholds.LateralRedMinMm == b.Thresholds.LateralRedMinMm;
        }

        static bool SameDepthPresentation(DentalNavigationSnapshot a, DentalNavigationSnapshot b,
            DentalHudEvaluation before, DentalHudEvaluation after)
        {
            return before.DashNumbers == after.DashNumbers && before.Depth == after.Depth
                && a.HasNavigationFrame == b.HasNavigationFrame && a.FrameValid == b.FrameValid
                && a.HasDepthBreakdown == b.HasDepthBreakdown && a.RemainingDepthMm == b.RemainingDepthMm
                && a.CurrentDepthMm == b.CurrentDepthMm && a.TargetDepthMm == b.TargetDepthMm;
        }

        void ApplyDepth(DentalNavigationSnapshot snap, DentalHudEvaluation eval)
        {
            var unavailable = eval.DashNumbers || !snap.HasNavigationFrame || !snap.FrameValid
                || !snap.HasDepthBreakdown;
            var remaining = snap.RemainingDepthMm;
            var current = snap.CurrentDepthMm;
            var target = snap.TargetDepthMm;
            var over = !unavailable && remaining < 0f;
            var beforeEntry = !unavailable && current < 0f;

            m_Views.DepthTitle.text = unavailable ? "Remaining depth" : over ? "Over depth" : beforeEntry ? "To entry point" : "Remaining depth";
            m_Views.DepthValue.text = unavailable ? "—" : Mathf.Abs(over ? remaining : beforeEntry ? current : remaining).ToString("0.0");
            m_Views.DepthUnit.text = unavailable ? string.Empty : "mm";
            m_Views.DepthValue.color = ColorForGrade(eval.Depth, false);
            m_Views.DepthPlate.color = eval.Depth == DentalMetricGrade.Red
                ? new Color(Red.r, Red.g, Red.b, 0.50f)
                : Plate;

            m_Views.CurrentDepth.text = unavailable ? "Current\n—" : string.Format("Current\n{0:0.0} mm", current);
            m_Views.TargetDepth.text = unavailable ? "Target\n—" : string.Format("Target\n{0:0.0} mm", target);
            m_Views.DepthScaleTop.text = "0";
            m_Views.DepthScaleTarget.text = unavailable ? "—" : target.ToString("0.0") + " mm";

            var scaleMax = Mathf.Max(1f, target > 0f ? Mathf.Max(target * 1.15f, target + 1f) : 10f);
            var targetT = target > 0f ? Mathf.Clamp01(target / scaleMax) : 0.9f;
            var currentT = Mathf.Clamp01(current / scaleMax);
            PositionOnVerticalTrack(m_Views.DepthTargetTick, targetT);
            PositionOnVerticalTrack(m_Views.DepthCurrentMarker, currentT);
            m_Views.DepthScaleTarget.rectTransform.anchoredPosition = new Vector2(-4f,
                m_Views.DepthTrack.anchoredPosition.y + m_Views.DepthTargetTick.anchoredPosition.y);
            m_Views.DepthFill.sizeDelta = new Vector2(10f, unavailable ? 0f : DepthTrackHeightMm * currentT);
            m_Views.DepthFillImage.color = ColorForGrade(eval.Depth, false);
            m_Views.DepthCurrentMarkerImage.color = ColorForGrade(eval.Depth, false);
            m_Views.DepthCurrentMarker.gameObject.SetActive(!unavailable);
            m_Views.DepthTargetTick.gameObject.SetActive(!unavailable && target > 0f);

            if (unavailable)
                m_Views.DepthOutOfRange.text = string.Empty;
            else if (current < 0f)
                m_Views.DepthOutOfRange.text = "↑ Before entry";
            else if (current > scaleMax)
                m_Views.DepthOutOfRange.text = "↓ +" + (current - scaleMax).ToString("0.0");
            else
                m_Views.DepthOutOfRange.text = string.Empty;
        }

        static void PositionOnVerticalTrack(RectTransform item, float normalizedFromTop)
        {
            item.anchoredPosition = new Vector2(0f, DepthTrackHeightMm * (0.5f - normalizedFromTop));
        }

        static Vector2 ToAnatomicalDisplay(float buccal, float mesial)
        {
            // Fixed graphic convention: top=buccal, bottom=lingual,
            // left=mesial, right=distal.
            return new Vector2(-mesial, buccal);
        }

        RectTransform CreateCard(RectTransform parent, string name, Vector2 pos, Vector2 size, Color color)
        {
            var rt = CreateUiObject(parent, name);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            var image = rt.gameObject.AddComponent<Image>();
            image.sprite = m_WhiteSprite;
            image.color = color;
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

        TMP_Text CreateTmp(RectTransform parent, string name, Vector2 pos, Vector2 size, float fontSize,
            FontStyles style, TextAlignmentOptions align, Color color)
        {
            var rt = CreateUiObject(parent, name);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.font = m_Font;
            tmp.fontSize = fontSize;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = Mathf.Min(12f, fontSize);
            tmp.fontSizeMax = fontSize;
            tmp.fontStyle = style;
            tmp.alignment = align;
            tmp.color = color;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.raycastTarget = false;
            if (m_Font != null)
            {
                tmp.outlineWidth = 0.18f;
                tmp.outlineColor = Color.black;
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
            m_RingSprite = CreateRadialSprite(64, 0.84f);
            m_TriangleSprite = CreateTriangleSprite(32);
        }

        static Sprite CreateSolidSprite(int size)
        {
            var tex = NewTexture(size);
            var pixels = new Color32[size * size];
            for (var i = 0; i < pixels.Length; i++)
                pixels[i] = Color.white;
            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        }

        static Sprite CreateRadialSprite(int size, float inner01)
        {
            var tex = NewTexture(size);
            var pixels = new Color32[size * size];
            var center = (size - 1) * 0.5f;
            var outer = center - 1.25f;
            var inner = inner01 < 0f ? -8f : outer * inner01;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var d = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                    var alpha = Mathf.Clamp01(outer + 1.25f - d)
                        - (inner01 < 0f ? 0f : Mathf.Clamp01(inner + 1.25f - d));
                    pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        }

        static Sprite CreateTriangleSprite(int size)
        {
            var tex = NewTexture(size);
            var pixels = new Color32[size * size];
            var center = (size - 1) * 0.5f;
            for (var y = 0; y < size; y++)
            {
                var halfWidth = (1f - y / (float)(size - 1)) * center;
                for (var x = 0; x < size; x++)
                    pixels[y * size + x] = Mathf.Abs(x - center) <= halfWidth ? Color.white : Color.clear;
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        }

        static Texture2D NewTexture(int size)
        {
            return new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
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
                default:
                    return Gray;
            }
        }

        static string OverallPhrase(DentalMetricGrade grade)
        {
            switch (grade)
            {
                case DentalMetricGrade.Green: return "Within range";
                case DentalMetricGrade.Amber: return "Near limit";
                case DentalMetricGrade.Red: return "Out of range";
                case DentalMetricGrade.Stale: return "Stale data";
                default: return "Thresholds not synced";
            }
        }

        static string Short(string value, int max)
        {
            if (string.IsNullOrEmpty(value) || value == "-" || value == "—")
                return "—";
            return value.Length <= max ? value : value.Substring(0, Math.Max(1, max - 1)) + "…";
        }
    }
}
