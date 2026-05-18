using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Unity.XR.XREAL.Editor
{
    /// <summary>
    /// Keeps Android builds in Development / debug mode for logcat, profiler, and script debugging.
    /// </summary>
    [InitializeOnLoad]
    static class ProjectDebugBuildSettings
    {
        const string k_PrefKey = "XREAL.ProjectDebugBuildEnabled";

        static ProjectDebugBuildSettings()
        {
            if (!SessionState.GetBool(k_PrefKey, true))
                return;

            ApplyDebugBuildOptions();
        }

        [MenuItem("XREAL/Build/Enable Debug Build (Android)", false, 200)]
        static void EnableDebugBuildMenu()
        {
            SessionState.SetBool(k_PrefKey, true);
            ApplyDebugBuildOptions();
            Debug.Log("XREAL: Debug build enabled (Development Build + Script Debugging).");
        }

        [MenuItem("XREAL/Build/Enable Debug Build (Android)", true)]
        static bool EnableDebugBuildMenuValidate()
        {
            return !EditorUserBuildSettings.development;
        }

        [MenuItem("XREAL/Build/Disable Debug Build (Android)", false, 201)]
        static void DisableDebugBuildMenu()
        {
            SessionState.SetBool(k_PrefKey, false);
            EditorUserBuildSettings.development = false;
            EditorUserBuildSettings.allowDebugging = false;
            EditorUserBuildSettings.connectProfiler = false;
            Debug.Log("XREAL: Debug build disabled.");
        }

        [MenuItem("XREAL/Build/Disable Debug Build (Android)", true)]
        static bool DisableDebugBuildMenuValidate()
        {
            return EditorUserBuildSettings.development;
        }

        static void ApplyDebugBuildOptions()
        {
            EditorUserBuildSettings.development = true;
            EditorUserBuildSettings.allowDebugging = true;
            EditorUserBuildSettings.connectProfiler = false;
            EditorUserBuildSettings.buildAppBundle = false;

            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android)
            {
                EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.Generic;
            }

            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        }
    }
}
