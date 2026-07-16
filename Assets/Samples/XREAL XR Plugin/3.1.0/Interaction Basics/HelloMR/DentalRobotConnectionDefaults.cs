namespace Unity.XR.XREAL.Samples
{
    /// <summary>
    /// Default connection settings shared by the dental robot components.
    /// Change the server endpoint here when the robot server address changes.
    /// </summary>
    static class DentalRobotConnectionDefaults
    {
        public const string ServerHost = "192.168.31.166";
        public const int ServerPort = 50051;
        public const string DeviceId = "beam-pro";
        public const string DatasetId = "default";
    }
}
