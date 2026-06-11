using UnityEngine;

namespace Unity.XR.XREAL.Samples
{
    /// <summary>
    /// Shared screen regions for Beam Pro OnGUI overlays so panels and buttons do not overlap.
    /// </summary>
    public static class BeamProOverlayLayout
    {
        public const float Margin = 16f;
        public const float ColumnGap = 12f;
        public const float RightColumnWidth = 280f;
        public const float BottomBandMaxFraction = 0.42f;
        public const float TopBandRgbMaxHeightFraction = 0.28f;
        public const float RightColumnMaxHeightFraction = 0.36f;
        public const int MaxButtonRows = 9;

        public static float RightColumnX => Screen.width - RightColumnWidth - Margin;

        public static float RightColumnReservedWidth => RightColumnWidth + Margin + ColumnGap;

        public static float LeftContentMaxWidth => Screen.width - RightColumnReservedWidth - Margin;

        public struct RightColumnButtonLayout
        {
            public float X;
            public float Y;
            public float Width;
            public float ButtonHeight;
            public float RowSpacing;
            public float TotalHeight;
        }

        public static RightColumnButtonLayout ComputeRightColumnButtons(int rowCount)
        {
            rowCount = Mathf.Max(1, rowCount);
            var layout = new RightColumnButtonLayout
            {
                X = RightColumnX,
                Y = Margin,
                Width = RightColumnWidth,
                RowSpacing = 6f,
            };

            var maxHeight = Screen.height * RightColumnMaxHeightFraction;
            layout.ButtonHeight = Mathf.Clamp(
                (maxHeight - layout.RowSpacing * (rowCount - 1)) / rowCount,
                42f,
                68f);
            layout.TotalHeight = layout.ButtonHeight * rowCount + layout.RowSpacing * (rowCount - 1);
            return layout;
        }

        public static float GetTopBandBottom(int buttonRows, float rgbPanelHeight)
        {
            var buttonLayout = ComputeRightColumnButtons(buttonRows);
            var rgbBottom = Margin + rgbPanelHeight;
            var buttonBottom = buttonLayout.Y + buttonLayout.TotalHeight;
            return Mathf.Max(rgbBottom, buttonBottom) + ColumnGap;
        }

        public static float GetBottomBandTop(out float bottomBandHeight)
        {
            bottomBandHeight = Screen.height * BottomBandMaxFraction;
            return Screen.height - bottomBandHeight - Margin;
        }

        public static float GetMiddleBandHeight(int buttonRows, float rgbPanelHeight)
        {
            var top = GetTopBandBottom(buttonRows, rgbPanelHeight);
            var bottom = GetBottomBandTop(out _);
            return Mathf.Max(0f, bottom - top - Margin);
        }

        public static Rect ClampRgbDebugPanelRect(float preferredWidth, float preferredHeight, Vector2 position)
        {
            var bottomTop = GetBottomBandTop(out _);
            var maxWidth = LeftContentMaxWidth - Margin;
            var maxHeight = Mathf.Min(
                bottomTop - Margin * 2f,
                Screen.height * TopBandRgbMaxHeightFraction);

            var width = Mathf.Clamp(preferredWidth, 320f, maxWidth);
            var height = Mathf.Clamp(preferredHeight, 160f, maxHeight);
            var x = Mathf.Clamp(position.x, Margin, Margin + maxWidth - width);
            var y = Mathf.Clamp(position.y, Margin, bottomTop - height - Margin);
            return new Rect(x, y, width, height);
        }

        public static Rect GetVoicePanelRect(int buttonRows, float rgbPanelHeight, float preferredWidth, float preferredHeight)
        {
            var top = GetTopBandBottom(buttonRows, rgbPanelHeight);
            var bottom = GetBottomBandTop(out _);
            var availableHeight = bottom - top - Margin;

            var width = Mathf.Min(preferredWidth, LeftContentMaxWidth * 0.52f);
            var height = Mathf.Min(preferredHeight, Mathf.Max(120f, availableHeight * 0.62f));
            return new Rect(Margin, top + Margin, width, height);
        }

        public static Rect GetHandDiagnosticsRect(int buttonRows, float rgbPanelHeight, float preferredWidth, float preferredHeight)
        {
            var voiceRect = GetVoicePanelRect(buttonRows, rgbPanelHeight, 520f, 300f);
            var top = GetTopBandBottom(buttonRows, rgbPanelHeight);
            var bottom = GetBottomBandTop(out _);

            var x = voiceRect.xMax + ColumnGap;
            var width = RightColumnX - x - ColumnGap;
            var height = Mathf.Min(preferredHeight, bottom - top - Margin * 2f);

            if (width < 220f)
            {
                x = Margin;
                width = LeftContentMaxWidth;
                var stackedTop = voiceRect.yMax + ColumnGap;
                height = Mathf.Min(preferredHeight, bottom - stackedTop - Margin);
                return new Rect(x, stackedTop, width, Mathf.Max(120f, height));
            }

            return new Rect(x, top + Margin, width, Mathf.Max(120f, height));
        }

        public static Rect GetLeftEyePreviewRegion(int buttonRows, float rgbPanelHeight, float maxHeightFraction)
        {
            var top = GetTopBandBottom(buttonRows, rgbPanelHeight) + Margin;
            var bottomTop = GetBottomBandTop(out var bottomBandHeight);
            var maxHeight = Mathf.Min(
                Screen.height * maxHeightFraction,
                bottomBandHeight,
                Screen.height - top - Margin);
            var maxWidth = LeftContentMaxWidth - Margin;

            var previewWidth = maxWidth;
            var previewHeight = maxHeight;
            var totalHeight = previewHeight + Margin;
            var x = Margin + (LeftContentMaxWidth - previewWidth) * 0.5f;
            var y = Screen.height - totalHeight - Margin;

            if (y < top)
            {
                previewHeight = Mathf.Max(120f, Screen.height - top - Margin * 2f);
                totalHeight = previewHeight + Margin;
                y = Screen.height - totalHeight - Margin;
            }

            return new Rect(x, y, previewWidth, previewHeight);
        }

        public static Rect GetMicrophoneOverlayRect(int buttonRows, float rgbPanelHeight)
        {
            const float width = 420f;
            const float height = 108f;
            var bottomTop = GetBottomBandTop(out _);
            var y = bottomTop - height - Margin;
            return new Rect(Margin, y, Mathf.Min(width, LeftContentMaxWidth * 0.55f), height);
        }

        public static float EstimateRgbPanelHeight(float preferredHeight)
        {
            return Mathf.Min(
                preferredHeight,
                Screen.height * TopBandRgbMaxHeightFraction,
                GetBottomBandTop(out _) - Margin * 2f);
        }
    }
}
