using UnityEngine;

namespace Unity.XR.XREAL.Samples
{
    /// <summary>
    /// Session connectivity is independent of navigation validity, frame age and asset transfer progress.
    /// Navigation warnings and stale-value hiding remain the responsibility of DentalNavigationBand.
    /// </summary>
    public static class DentalConnectionPresentation
    {
        public static string Label(DentalLinkState link)
        {
            if (link == DentalLinkState.Live)
                return "已连接";
            if (link == DentalLinkState.Connecting)
                return "连接中";
            return "未连接";
        }

        public static Color IndicatorColor(DentalLinkState link, Color connected, Color connecting, Color disconnected)
        {
            if (link == DentalLinkState.Live)
                return connected;
            if (link == DentalLinkState.Connecting)
                return connecting;
            return disconnected;
        }
    }
}
