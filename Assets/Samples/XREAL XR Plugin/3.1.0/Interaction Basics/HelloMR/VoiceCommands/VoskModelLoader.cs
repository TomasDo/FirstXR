using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace Unity.XR.XREAL.Samples.VoiceCommands
{
    public static class VoskModelLoader
    {
        const string ManifestFileName = "model_files.txt";

        public static IEnumerator EnsureModelReady(string modelFolderName, System.Action<string, string> onComplete)
        {
            var streamingModelPath = Path.Combine(Application.streamingAssetsPath, "VoiceCommands", modelFolderName);
            var persistentModelPath = Path.Combine(Application.persistentDataPath, modelFolderName);

#if UNITY_EDITOR
            if (Directory.Exists(streamingModelPath) && File.Exists(Path.Combine(streamingModelPath, "am", "final.mdl")))
            {
                onComplete?.Invoke(streamingModelPath, null);
                yield break;
            }
#endif

            if (Directory.Exists(persistentModelPath) && File.Exists(Path.Combine(persistentModelPath, "am", "final.mdl")))
            {
                onComplete?.Invoke(persistentModelPath, null);
                yield break;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            yield return CopyModelToPersistent(streamingModelPath, persistentModelPath, modelFolderName, onComplete);
#else
            onComplete?.Invoke(null,
                $"未找到中文语音模型。请将 {modelFolderName} 解压到 Assets/StreamingAssets/VoiceCommands/ ，\n" +
                "并在 Unity 菜单执行 XREAL → Download Chinese Vosk Model。");
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        static IEnumerator CopyModelToPersistent(string streamingRoot, string persistentRoot, string modelFolderName, System.Action<string, string> onComplete)
        {
            var manifestUrl = Path.Combine(Application.streamingAssetsPath, "VoiceCommands", ManifestFileName).Replace("\\", "/");
            using var manifestRequest = UnityWebRequest.Get(manifestUrl);
            yield return manifestRequest.SendWebRequest();

            if (manifestRequest.result != UnityWebRequest.Result.Success)
            {
                onComplete?.Invoke(null,
                    "缺少 VoiceCommands/model_files.txt。请在 Editor 中执行 XREAL → Regenerate Vosk Model Manifest。");
                yield break;
            }

            Directory.CreateDirectory(persistentRoot);
            var prefix = $"VoiceCommands/{modelFolderName}/";
            var lines = manifestRequest.downloadHandler.text.Split('\n');

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim().Replace("\\", "/");
                if (string.IsNullOrEmpty(line) || line.StartsWith("#") || !line.StartsWith(prefix))
                    continue;

                var relative = line.Substring(prefix.Length);
                var sourceUrl = $"{Application.streamingAssetsPath}/{line}";
                var destFile = Path.Combine(persistentRoot, relative);
                var destDir = Path.GetDirectoryName(destFile);
                if (!string.IsNullOrEmpty(destDir))
                    Directory.CreateDirectory(destDir);

                using var fileRequest = UnityWebRequest.Get(sourceUrl);
                yield return fileRequest.SendWebRequest();
                if (fileRequest.result != UnityWebRequest.Result.Success)
                {
                    onComplete?.Invoke(null, $"复制模型文件失败：{relative}");
                    yield break;
                }

                File.WriteAllBytes(destFile, fileRequest.downloadHandler.data);
            }

            if (!File.Exists(Path.Combine(persistentRoot, "am", "final.mdl")))
            {
                onComplete?.Invoke(null, "模型复制完成但结构无效，请检查 StreamingAssets/VoiceCommands 下的模型目录。");
                yield break;
            }

            onComplete?.Invoke(persistentRoot, null);
        }
#endif

        public static void RegenerateManifest(string modelFolderName)
        {
            var modelRoot = Path.Combine(Application.streamingAssetsPath, "VoiceCommands", modelFolderName);
            if (!Directory.Exists(modelRoot))
            {
                Debug.LogError($"[VoiceCmd] Model folder not found: {modelRoot}");
                return;
            }

            var lines = new List<string>();
            var prefix = $"VoiceCommands/{modelFolderName}/";
            foreach (var file in Directory.GetFiles(modelRoot, "*", SearchOption.AllDirectories))
            {
                var relative = file.Substring(modelRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                lines.Add(prefix + relative.Replace("\\", "/"));
            }

            var manifestPath = Path.Combine(Application.streamingAssetsPath, "VoiceCommands", ManifestFileName);
            File.WriteAllLines(manifestPath, lines);
            Debug.Log($"[VoiceCmd] Wrote {lines.Count} entries to {manifestPath}");
        }
    }
}
