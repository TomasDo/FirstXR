using System.IO;
using UnityEditor;
using UnityEngine;
using Unity.XR.XREAL.Samples;

/// <summary>
/// Editor entry points for inspecting the same IMGUI code that runs on Beam Pro.
/// The debug preview temporarily enables engineer mode in Play Mode and never changes scene data.
/// </summary>
[InitializeOnLoad]
public static class BeamProPagedPreviewMenu
{
    const string PendingPageKey = "BeamProPagedPreview.PendingPage";
    const string PendingCaptureAllKey = "BeamProPagedPreview.PendingCaptureAll";
    const int NoPendingPage = -1;

    static readonly BeamProPage[] CapturePages =
    {
        BeamProPage.Monitor,
        BeamProPage.Connection,
        BeamProPage.Debug,
    };

    static int s_CaptureIndex;
    static int s_WaitFrames;
    static int s_FindControllerAttempts;
    static bool s_PagePrepared;
    static string s_PendingCapturePath;

    static BeamProPagedPreviewMenu()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    [MenuItem("Tools/Beam Pro Preview/Show Monitor")]
    static void ShowMonitor()
    {
        RequestPage(BeamProPage.Monitor);
    }

    [MenuItem("Tools/Beam Pro Preview/Show Connection")]
    static void ShowConnection()
    {
        RequestPage(BeamProPage.Connection);
    }

    [MenuItem("Tools/Beam Pro Preview/Show Debug")]
    static void ShowDebug()
    {
        RequestPage(BeamProPage.Debug);
    }

    [MenuItem("Tools/Beam Pro Preview/Capture All Three Pages")]
    static void CaptureAllThreePages()
    {
        if (!EditorApplication.isPlaying)
        {
            SessionState.SetBool(PendingCaptureAllKey, true);
            EditorApplication.EnterPlaymode();
            return;
        }

        BeginCaptureAll();
    }

    static void RequestPage(BeamProPage page)
    {
        if (!EditorApplication.isPlaying)
        {
            SessionState.SetInt(PendingPageKey, (int)page);
            EditorApplication.EnterPlaymode();
            return;
        }

        ApplyPage(page);
    }

    static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode)
            return;

        if (SessionState.GetBool(PendingCaptureAllKey, false))
        {
            SessionState.EraseBool(PendingCaptureAllKey);
            EditorApplication.delayCall += BeginCaptureAll;
            return;
        }

        var pendingPage = SessionState.GetInt(PendingPageKey, NoPendingPage);
        if (pendingPage == NoPendingPage)
            return;

        SessionState.EraseInt(PendingPageKey);
        EditorApplication.delayCall += () => ApplyPage((BeamProPage)pendingPage);
    }

    static bool ApplyPage(BeamProPage page)
    {
        var controller = Object.FindFirstObjectByType<BeamProPagedController>();
        if (controller == null)
        {
            Debug.LogWarning("Beam Pro 分页预览不可用：场景中没有运行中的 BeamProPagedController。");
            return false;
        }

        controller.SelectEditorPreviewPage(page);
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        return true;
    }

    static void BeginCaptureAll()
    {
        if (!EditorApplication.isPlaying)
            return;

        s_CaptureIndex = 0;
        s_WaitFrames = 0;
        s_FindControllerAttempts = 0;
        s_PagePrepared = false;
        s_PendingCapturePath = null;
        EditorApplication.update -= CaptureNextFrame;
        EditorApplication.update += CaptureNextFrame;
    }

    static void CaptureNextFrame()
    {
        if (!EditorApplication.isPlaying)
        {
            FinishCapture("Beam Pro 页面截图已取消：Play Mode 已结束。", true);
            return;
        }

        if (s_WaitFrames-- > 0)
            return;

        if (!string.IsNullOrEmpty(s_PendingCapturePath))
        {
            if (!File.Exists(s_PendingCapturePath) || new FileInfo(s_PendingCapturePath).Length == 0)
                return;

            s_PendingCapturePath = null;
            s_CaptureIndex++;
            s_PagePrepared = false;
            s_WaitFrames = 3;
        }

        if (s_CaptureIndex >= CapturePages.Length)
        {
            FinishCapture("Beam Pro 三页编辑器截图已写入 Docs/BeamProScreenshots。", false);
            return;
        }

        var page = CapturePages[s_CaptureIndex];
        if (!s_PagePrepared)
        {
            if (!ApplyPage(page))
            {
                if (++s_FindControllerAttempts > 120)
                    FinishCapture("Beam Pro 页面截图失败：未找到运行中的分页控制器。", true);
                return;
            }

            s_PagePrepared = true;
            s_WaitFrames = 5;
            return;
        }

        var outputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Docs/BeamProScreenshots"));
        Directory.CreateDirectory(outputDirectory);
        var filename = page.ToString().ToLowerInvariant() + "-editor.png";
        s_PendingCapturePath = Path.Combine(outputDirectory, filename);
        if (File.Exists(s_PendingCapturePath))
            File.Delete(s_PendingCapturePath);
        ScreenCapture.CaptureScreenshot(s_PendingCapturePath, 1);
    }

    static void FinishCapture(string message, bool warning)
    {
        EditorApplication.update -= CaptureNextFrame;
        s_PendingCapturePath = null;
        if (warning)
            Debug.LogWarning(message);
        else
            Debug.Log(message);
    }
}
