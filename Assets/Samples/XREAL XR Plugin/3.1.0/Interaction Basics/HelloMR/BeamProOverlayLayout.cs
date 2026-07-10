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
        public const float BottomPreviewMaxFraction = 0.24f;
        public const float RightColumnMaxHeightFraction = 0.9f;
        public const int MaxButtonRows = 9;

        public static float RightColumnX => Screen.width - GetRightColumnWidth() - Margin;

        public static float RightColumnReservedWidth => GetReservedRightWidth();

        public static float LeftContentMaxWidth => Mathf.Max(120f, Screen.width - RightColumnReservedWidth - Margin);

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
            var width = GetRightColumnWidth();
            var layout = new RightColumnButtonLayout
            {
                X = Screen.width - width - Margin,
                Y = Margin,
                Width = width,
                RowSpacing = 6f,
            };

            var maxHeight = Mathf.Max(120f, Screen.height * RightColumnMaxHeightFraction - Margin * 2f);
            layout.ButtonHeight = Mathf.Clamp(
                (maxHeight - layout.RowSpacing * (rowCount - 1)) / rowCount,
                28f,
                68f);
            layout.TotalHeight = layout.ButtonHeight * rowCount + layout.RowSpacing * (rowCount - 1);
            return layout;
        }

        public static float GetReservedRightWidth()
        {
            return GetRightColumnWidth() + Margin + ColumnGap;
        }

        static float GetRightColumnWidth()
        {
            return Mathf.Clamp(Screen.width * 0.24f, 120f, RightColumnWidth);
        }

        public static Rect GetMainLogRect(int rightButtonRows)
        {
            var previewRect = GetBottomPreviewRect(rightButtonRows, BottomPreviewMaxFraction);
            var width = Mathf.Max(120f, LeftContentMaxWidth);
            var height = Mathf.Max(180f, previewRect.y - Margin - ColumnGap);
            return new Rect(Margin, Margin, width, height);
        }

        public static Rect GetBottomPreviewRect(int rightButtonRows, float maxHeightFraction)
        {
            var maxFraction = Mathf.Clamp(maxHeightFraction, 0.12f, 0.28f);
            var width = Mathf.Max(120f, LeftContentMaxWidth);
            var height = Mathf.Clamp(Screen.height * maxFraction, 120f, Screen.height * 0.28f);
            var y = Mathf.Max(Margin, Screen.height - height - Margin);
            return new Rect(Margin, y, width, height);
        }

        public static float GetMiddleBandHeight(int buttonRows, float rgbPanelHeight)
        {
            return GetMainLogRect(buttonRows).height;
        }

        public static Rect ClampRgbDebugPanelRect(float preferredWidth, float preferredHeight, Vector2 position)
        {
            var mainRect = GetMainLogRect(MaxButtonRows);
            var maxWidth = mainRect.width;
            var maxHeight = mainRect.height;

            var minWidth = Mathf.Min(320f, maxWidth);
            var minHeight = Mathf.Min(160f, maxHeight);
            var width = Mathf.Clamp(preferredWidth, minWidth, maxWidth);
            var height = Mathf.Clamp(preferredHeight, minHeight, maxHeight);
            var x = Mathf.Clamp(position.x, mainRect.x, mainRect.xMax - width);
            var y = Mathf.Clamp(position.y, mainRect.y, mainRect.yMax - height);
            return new Rect(x, y, width, height);
        }

        public static Rect GetVoicePanelRect(int buttonRows, float rgbPanelHeight, float preferredWidth, float preferredHeight)
        {
            var mainRect = GetMainLogRect(buttonRows);
            var width = Mathf.Min(preferredWidth, mainRect.width);
            var height = Mathf.Min(preferredHeight, mainRect.height);
            return new Rect(mainRect.x, mainRect.y, width, height);
        }

        public static Rect GetDentalRobotPanelRect(int buttonRows, float rgbPanelHeight, float preferredWidth, float preferredHeight)
        {
            var mainRect = GetMainLogRect(buttonRows);
            var width = Mathf.Min(preferredWidth, mainRect.width);
            var height = Mathf.Min(preferredHeight, mainRect.height);
            return new Rect(mainRect.x, mainRect.y, width, height);
        }

        public static Rect GetLeftEyePreviewRegion(int buttonRows, float rgbPanelHeight, float maxHeightFraction)
        {
            return GetBottomPreviewRect(buttonRows, Mathf.Min(maxHeightFraction, BottomPreviewMaxFraction));
        }

        public static Rect GetMicrophoneOverlayRect(int buttonRows, float rgbPanelHeight)
        {
            const float width = 420f;
            const float height = 108f;
            var mainRect = GetMainLogRect(buttonRows);
            var y = mainRect.yMax - height - Margin;
            return new Rect(mainRect.x, y, Mathf.Min(width, mainRect.width), height);
        }

        public static float EstimateRgbPanelHeight(float preferredHeight)
        {
            return Mathf.Min(
                preferredHeight,
                Screen.height * BottomPreviewMaxFraction,
                Screen.height - Margin * 2f);
        }
    }
}
