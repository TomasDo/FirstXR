using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Unity.XR.XREAL.Editor
{
    /// <summary>
    /// Persists the explicit debug-build choice for this project. Normal startup must not
    /// silently turn development and script debugging back on after they were disabled.
    /// </summary>
    [InitializeOnLoad]
    static class ProjectDebugBuildSettings
    {
        static string PreferenceKey => "XREAL.ProjectDebugBuildEnabled." + Application.dataPath;

        static ProjectDebugBuildSettings()
        {
            if (EditorPrefs.GetBool(PreferenceKey, false))
                ApplyDebugBuildOptions();
            else
                ApplyReleaseBuildOptions();
        }

        [MenuItem("XREAL/Build/Enable Debug Build (Android)", false, 200)]
        static void EnableDebugBuildMenu()
        {
            EditorPrefs.SetBool(PreferenceKey, true);
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
            EditorPrefs.SetBool(PreferenceKey, false);
            ApplyReleaseBuildOptions();
            Debug.Log("XREAL: Debug build disabled.");
        }

        static void ApplyReleaseBuildOptions()
        {
            EditorUserBuildSettings.development = false;
            EditorUserBuildSettings.allowDebugging = false;
            EditorUserBuildSettings.connectProfiler = false;
            EditorUserBuildSettings.buildWithDeepProfilingSupport = false;
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
