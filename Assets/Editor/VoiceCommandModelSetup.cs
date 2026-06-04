using System.IO;
using System.IO.Compression;
using Unity.XR.XREAL.Samples.VoiceCommands;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Unity.XR.XREAL.Editor
{
    public static class VoiceCommandModelSetup
    {
        const string ModelZipUrl = "https://alphacephei.com/vosk/models/vosk-model-small-cn-0.22.zip";
        const string ModelFolderName = "vosk-model-small-cn-0.22";

        [MenuItem("XREAL/Download Chinese Vosk Model")]
        public static void DownloadChineseModel()
        {
            var voiceCommandsDir = Path.Combine(Application.streamingAssetsPath, "VoiceCommands");
            var targetDir = Path.Combine(voiceCommandsDir, ModelFolderName);
            if (Directory.Exists(targetDir) && File.Exists(Path.Combine(targetDir, "am", "final.mdl")))
            {
                Debug.Log($"[VoiceCmd] Model already exists at {targetDir}");
                VoskModelLoader.RegenerateManifest(ModelFolderName);
                AssetDatabase.Refresh();
                return;
            }

            var zipPath = Path.Combine(Application.dataPath, "../Temp", ModelFolderName + ".zip");
            Directory.CreateDirectory(Path.GetDirectoryName(zipPath) ?? Application.dataPath);

            EditorUtility.DisplayProgressBar("Vosk Model", "Downloading Chinese model (~42 MB)...", 0.2f);
            try
            {
                using var request = UnityWebRequest.Get(ModelZipUrl);
                request.downloadHandler = new DownloadHandlerFile(zipPath);
                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    EditorUtility.DisplayProgressBar("Vosk Model", "Downloading...", Mathf.Clamp01(request.downloadProgress));
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[VoiceCmd] Download failed: {request.error}");
                    return;
                }

                EditorUtility.DisplayProgressBar("Vosk Model", "Extracting...", 0.85f);
                Directory.CreateDirectory(voiceCommandsDir);
                if (Directory.Exists(targetDir))
                    Directory.Delete(targetDir, true);
                ZipFile.ExtractToDirectory(zipPath, voiceCommandsDir);

                VoskModelLoader.RegenerateManifest(ModelFolderName);
                AssetDatabase.Refresh();
                Debug.Log($"[VoiceCmd] Model installed to {targetDir}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        [MenuItem("XREAL/Regenerate Vosk Model Manifest")]
        public static void RegenerateManifest()
        {
            VoskModelLoader.RegenerateManifest(ModelFolderName);
            AssetDatabase.Refresh();
        }
    }
}
