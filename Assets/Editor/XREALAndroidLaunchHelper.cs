#if UNITY_EDITOR && UNITY_ANDROID
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Unity.XR.XREAL.Editor
{
    /// <summary>
    /// Deploys and launches the XREAL Android player via adb. Unity's built-in "Build And Run"
    /// often throws NullReferenceException in AndroidDeploymentTargetsExtension.StartApplication;
    /// this helper installs the APK and starts NRXRActivity instead.
    /// </summary>
    public sealed class XREALAndroidLaunchHelper : IPostprocessBuildWithReport
    {
        const string LaunchActivity = "ai.nreal.activitylife.NRXRActivity";
        const string UnityPlayerActivity = "com.unity3d.player.UnityPlayerActivity";

        public int callbackOrder => 1000;

        [MenuItem("XREAL/Launch App On Android Device")]
        public static void LaunchFromMenu()
        {
            if (TryDeployAndLaunch(null, showDialogs: true))
                return;

            EditorUtility.DisplayDialog(
                "XREAL Launch",
                "Failed to launch on a connected Android device.\n\n" +
                "• Connect the device (USB debugging on)\n" +
                "• Run: adb devices\n" +
                "• Set Android SDK in Unity > Preferences > External Tools\n" +
                "• Or use Build (without Run) then launch from the Beam Pro / glasses",
                "OK");
        }

        [MenuItem("XREAL/Build/Android APK (Build Only, No Unity Launch)")]
        public static void BuildApkOnly()
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
            {
                EditorUtility.DisplayDialog("XREAL Build", "No scenes enabled in Build Settings.", "OK");
                return;
            }

            var options = BuildOptions.None;
            if (EditorUserBuildSettings.development)
                options |= BuildOptions.Development;

            var report = BuildPipeline.BuildPlayer(scenes, GetDefaultApkOutputPath(), BuildTarget.Android, options);
            if (report.summary.result == BuildResult.Succeeded)
            {
                UnityEngine.Debug.Log($"XREAL: APK built at {report.summary.outputPath}. Use XREAL > Launch App On Android Device to install and run.");
            }
            else
            {
                throw new BuildFailedException($"XREAL: Android build ended with {report.summary.result}. See the build log for details.");
            }
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android)
                return;

            if (report.summary.result != BuildResult.Succeeded)
                return;

            if ((report.summary.options & BuildOptions.AutoRunPlayer) == 0)
                return;

            var outputPath = report.summary.outputPath;
            EditorApplication.delayCall += () =>
            {
                if (TryDeployAndLaunch(outputPath, showDialogs: false))
                {
                    UnityEngine.Debug.Log(
                        "XREAL: Installed and launched via adb. " +
                        "If Unity also logged NullReferenceException in AndroidDeploymentTargetsExtension, you can ignore it — the app was started by the XREAL launch helper.");
                }
                else
                {
                    UnityEngine.Debug.LogWarning(
                        "XREAL: Unity Build And Run may have failed to launch the app (NullReferenceException in Editor is common). " +
                        "APK was built successfully. Use menu XREAL > Launch App On Android Device after connecting adb.");
                }
            };
        }

        static bool TryDeployAndLaunch(string buildOutputPath, bool showDialogs)
        {
            var adbPath = GetAdbPath();
            if (string.IsNullOrEmpty(adbPath))
            {
                UnityEngine.Debug.LogError("XREAL launch helper: Android SDK platform-tools/adb not found. Set SDK path in Unity > Preferences > External Tools.");
                return false;
            }

            var deviceId = GetFirstDeviceId(adbPath);
            if (string.IsNullOrEmpty(deviceId))
            {
                UnityEngine.Debug.LogError("XREAL launch helper: No Android device found. Connect USB debugging and run: adb devices");
                return false;
            }

            var apkPath = ResolveApkPath(buildOutputPath);
            if (!string.IsNullOrEmpty(apkPath))
            {
                var installCode = RunProcess(adbPath, $"-s {deviceId} install -r \"{apkPath}\"", out var installOut, out var installErr);
                if (installCode != 0)
                {
                    UnityEngine.Debug.LogWarning($"XREAL launch helper: adb install returned {installCode}. {installErr}\n{installOut}");
                }
            }
            else if (!string.IsNullOrEmpty(buildOutputPath))
            {
                UnityEngine.Debug.LogWarning($"XREAL launch helper: Could not find APK at '{buildOutputPath}'. Skipping install; trying to start existing package.");
            }

            var packageName = PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android);
            if (string.IsNullOrEmpty(packageName))
            {
                UnityEngine.Debug.LogError("XREAL launch helper: Android application identifier (package name) is empty.");
                return false;
            }

            if (TryStartActivity(adbPath, deviceId, packageName, LaunchActivity, out var output))
            {
                if (!string.IsNullOrEmpty(output))
                    UnityEngine.Debug.Log(output.Trim());
                return true;
            }

            UnityEngine.Debug.LogWarning("XREAL launch helper: NRXRActivity failed, trying UnityPlayerActivity.");
            if (TryStartActivity(adbPath, deviceId, packageName, UnityPlayerActivity, out output))
            {
                if (!string.IsNullOrEmpty(output))
                    UnityEngine.Debug.Log(output.Trim());
                return true;
            }

            return false;
        }

        static bool TryStartActivity(string adbPath, string deviceId, string packageName, string activityClass, out string output)
        {
            var component = $"{packageName}/{activityClass}";
            var exitCode = RunProcess(adbPath, $"-s {deviceId} shell am start -n {component}", out output, out var error);
            if (exitCode != 0)
                UnityEngine.Debug.LogError($"XREAL launch helper: am start {component} failed ({exitCode}): {error}\n{output}");

            return exitCode == 0;
        }

        static string ResolveApkPath(string buildOutputPath)
        {
            if (string.IsNullOrEmpty(buildOutputPath))
                return null;

            if (File.Exists(buildOutputPath) && buildOutputPath.EndsWith(".apk", System.StringComparison.OrdinalIgnoreCase))
                return buildOutputPath;

            if (Directory.Exists(buildOutputPath))
            {
                var apks = Directory.GetFiles(buildOutputPath, "*.apk", SearchOption.AllDirectories);
                if (apks.Length > 0)
                    return apks.OrderByDescending(File.GetLastWriteTimeUtc).First();
            }

            var sibling = buildOutputPath + ".apk";
            if (File.Exists(sibling))
                return sibling;

            return null;
        }

        static string GetDefaultApkOutputPath()
        {
            var product = string.IsNullOrEmpty(PlayerSettings.productName) ? "App" : PlayerSettings.productName;
            foreach (var c in Path.GetInvalidFileNameChars())
                product = product.Replace(c, '_');

            return Path.Combine(Directory.GetCurrentDirectory(), "Builds", $"{product}.apk");
        }

        static string GetFirstDeviceId(string adbPath)
        {
            var exitCode = RunProcess(adbPath, "devices", out var output, out _);
            if (exitCode != 0 || string.IsNullOrEmpty(output))
                return null;

            foreach (var line in output.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.EndsWith("\tdevice"))
                    return trimmed.Split('\t')[0].Trim();
            }

            return null;
        }

        static string GetAdbPath()
        {
            var sdkRoot = AndroidExternalToolsSettings.sdkRootPath;
            if (string.IsNullOrEmpty(sdkRoot))
                return null;

            var adb = Path.Combine(sdkRoot, "platform-tools", Application.platform == RuntimePlatform.WindowsEditor ? "adb.exe" : "adb");
            return File.Exists(adb) ? adb : null;
        }

        static int RunProcess(string fileName, string arguments, out string output, out string error)
        {
            output = string.Empty;
            error = string.Empty;

            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                error = "Failed to start adb process.";
                return -1;
            }

            output = process.StandardOutput.ReadToEnd();
            error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode;
        }
    }
}
#endif
