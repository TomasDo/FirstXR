using System.IO;
using Unity.XR.XREAL;
using UnityEditor;
using UnityEngine;

namespace Unity.XR.XREAL.Editor
{
    /// <summary>
    /// Assigns nrsdk_license.bin from Assets/XR/License to XREALSettings when present.
    /// </summary>
    [InitializeOnLoad]
    static class XREALLicenseSetup
    {
        const string LicenseDirectory = "Assets/XR/License";
        const string LicenseFileName = "nrsdk_license.bin";

        static XREALLicenseSetup()
        {
            EditorApplication.delayCall += TryAssignLicense;
        }

        static void TryAssignLicense()
        {
            var licensePath = Path.Combine(LicenseDirectory, LicenseFileName);
            if (!File.Exists(licensePath))
                return;

            var settings = XREALSettings.GetSettings();
            if (settings == null || settings.LicenseAsset != null)
                return;

            var licenseAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(licensePath);
            if (licenseAsset == null)
                return;

            settings.LicenseAsset = licenseAsset;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Debug.Log($"XREAL: Assigned license from {licensePath}");
        }
    }
}
