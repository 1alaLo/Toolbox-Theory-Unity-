using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spine.Unity;
using Spine.Unity.Editor;

namespace TOOL
{
    public enum SpineImportStatus
    {
        New,                // 新增
        Overwrite,          // 覆盖（源比目标新）
        Skip,               // 跳过（目标比源新或时间相同）
        SourceDuplicate     // 源内重名（同批次出现同名文件夹，仅保留最新）
    }

    public class SpineFolderInfo
    {
        public string sourcePath;
        public string destPath;
        public string folderName;
        public SpineImportStatus status;
        public DateTime srcTime;
        public DateTime destTime;
    }

    public class SpineBatchImporter : EditorWindow
    {
        // ========== 配置字段 ==========
        private string sourceFolder = "";
        private string targetFolder = "Assets/SpineImported";

        // ========== 状态字段 ==========
        private Vector2 scrollPos;
        private List<SpineFolderInfo> scanResults = new List<SpineFolderInfo>();
        private bool showPreview = false;

        private int countNew = 0;
        private int countOverwrite = 0;
        private int countSkip = 0;
        private int countSourceDup = 0;

        // 骨架数据缩放（Spine 常规导出为 0.01）
        private float skeletonScale = 0.01f;

        [MenuItem("TOOL/Spine Batch Importer")]
        public static void ShowWindow()
        {
            var win = GetWindow<SpineBatchImporter>("Spine Batch Importer");
            win.minSize = new Vector2(600, 480);
        }

        private void OnGUI()
        {
            DrawHeader();
            DrawPathSelection();
            EditorGUILayout.Space();

            DrawScanButton();

            if (showPreview)
            {
                EditorGUILayout.Space();
                DrawStatistics();
                EditorGUILayout.Space();
                DrawResultList();
                EditorGUILayout.Space();
                DrawImportButton();
            }
        }

        // ==================== UI 区块 ====================

        private void DrawHeader()
        {
            GUILayout.Label("Spine 批量导入工具", EditorStyles.boldLabel);
            EditorGUILayout.Space();
        }

        private void DrawPathSelection()
        {
            // 源文件夹
            EditorGUILayout.BeginHorizontal();
            sourceFolder = EditorGUILayout.TextField("源文件夹", sourceFolder);
            if (GUILayout.Button("浏览...", GUILayout.Width(80)))
            {
                string path = EditorUtility.OpenFolderPanel("选择包含 Spine 资源的文件夹", "", "");
                if (!string.IsNullOrEmpty(path))
                {
                    sourceFolder = path.Replace("\\", "/").TrimEnd('/');
                    ResetScanState();
                }
            }
            EditorGUILayout.EndHorizontal();

            // 目标文件夹
            EditorGUILayout.BeginHorizontal();
            targetFolder = EditorGUILayout.TextField("目标文件夹", targetFolder);
            if (GUILayout.Button("浏览...", GUILayout.Width(80)))
            {
                string path = EditorUtility.OpenFolderPanel("选择目标文件夹", Application.dataPath, "");
                if (!string.IsNullOrEmpty(path))
                {
                    string dataPath = Application.dataPath.Replace("\\", "/");
                    if (path.Replace("\\", "/").StartsWith(dataPath))
                    {
                        targetFolder = "Assets" + path.Replace("\\", "/").Substring(dataPath.Length);
                    }
                    else
                    {
                        targetFolder = "Assets/" + Path.GetFileName(path);
                        EditorUtility.DisplayDialog("提示",
                            "目标文件夹必须在 Assets 目录下，已自动调整为:\n" + targetFolder, "OK");
                    }
                    ResetScanState();
                }
            }
            EditorGUILayout.EndHorizontal();

            // 缩放设置
            skeletonScale = EditorGUILayout.FloatField("SkeletonData 缩放", skeletonScale);
        }

        private void DrawScanButton()
        {
            if (GUILayout.Button("扫描 Spine 资源", GUILayout.Height(30)))
            {
                if (string.IsNullOrEmpty(sourceFolder))
                {
                    EditorUtility.DisplayDialog("错误", "请先选择源文件夹", "OK");
                    return;
                }
                if (!Directory.Exists(sourceFolder))
                {
                    EditorUtility.DisplayDialog("错误", "源文件夹不存在:\n" + sourceFolder, "OK");
                    return;
                }
                ScanSpineFolders();
                showPreview = true;
            }
        }

        private void DrawStatistics()
        {
            EditorGUILayout.LabelField($"扫描结果：共 {scanResults.Count} 个", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();

            GUI.color = Color.green;
            GUILayout.Label($"新增: {countNew}", GUILayout.Width(100));
            GUI.color = new Color(1f, 0.85f, 0.3f);
            GUILayout.Label($"可覆盖: {countOverwrite}", GUILayout.Width(120));
            GUI.color = Color.gray;
            GUILayout.Label($"跳过: {countSkip}", GUILayout.Width(120));

            if (countSourceDup > 0)
            {
                GUI.color = new Color(1f, 0.45f, 0.45f);
                GUILayout.Label($"源内重名(保留最新): {countSourceDup}", GUILayout.Width(220));
            }

            GUI.color = Color.white;
            EditorGUILayout.EndHorizontal();
        }

        private void DrawResultList()
        {
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.MaxHeight(280));

            foreach (var info in scanResults)
            {
                string statusTag;
                switch (info.status)
                {
                    case SpineImportStatus.New:
                        GUI.color = Color.green;
                        statusTag = "[新增] ";
                        break;
                    case SpineImportStatus.Overwrite:
                        GUI.color = new Color(1f, 0.85f, 0.3f);
                        statusTag = "[覆盖] ";
                        break;
                    case SpineImportStatus.Skip:
                        GUI.color = Color.gray;
                        statusTag = "[跳过] ";
                        break;
                    case SpineImportStatus.SourceDuplicate:
                        GUI.color = new Color(1f, 0.45f, 0.45f);
                        statusTag = "[源内重名-将被覆盖] ";
                        break;
                    default:
                        GUI.color = Color.white;
                        statusTag = "[未知] ";
                        break;
                }

                string relativePath = GetRelativePath(info.sourcePath, sourceFolder);
                EditorGUILayout.LabelField(statusTag + relativePath);
                GUI.color = Color.white;

                // 细节信息
                if (info.status == SpineImportStatus.SourceDuplicate)
                {
                    EditorGUI.indentLevel++;
                    GUI.color = new Color(1f, 0.6f, 0.6f);
                    EditorGUILayout.LabelField(
                        $"⚠️ 与其他路径下的同名文件夹冲突，仅保留修改时间最新的那个。此路径时间: {info.srcTime:yyyy-MM-dd HH:mm:ss}",
                        EditorStyles.miniLabel);
                    GUI.color = Color.white;
                    EditorGUI.indentLevel--;
                }
                else if (info.status == SpineImportStatus.Overwrite || info.status == SpineImportStatus.Skip)
                {
                    EditorGUI.indentLevel++;
                    GUI.color = new Color(0.7f, 0.7f, 0.7f);
                    EditorGUILayout.LabelField(
                        $"源时间: {info.srcTime:yyyy-MM-dd HH:mm:ss} | 目标时间: {info.destTime:yyyy-MM-dd HH:mm:ss}",
                        EditorStyles.miniLabel);
                    GUI.color = Color.white;
                    EditorGUI.indentLevel--;
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawImportButton()
        {
            if (scanResults.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "未找到任何 Spine 资源文件夹。\n确保文件夹中包含 .json / .skel.bytes / .atlas.txt 文件。",
                    MessageType.Info);
                return;
            }

            int effectiveCount = scanResults.Count(x =>
                x.status == SpineImportStatus.New || x.status == SpineImportStatus.Overwrite);

            using (new EditorGUI.DisabledScope(effectiveCount == 0))
            {
                if (GUILayout.Button($"开始批量导入（将处理 {effectiveCount} 个）", GUILayout.Height(40)))
                {
                    if (string.IsNullOrEmpty(targetFolder))
                    {
                        EditorUtility.DisplayDialog("错误", "请指定目标文件夹", "OK");
                        return;
                    }
                    BatchImport();
                }
            }
        }

        // ==================== 扫描逻辑 ====================

        private void ResetScanState()
        {
            scanResults.Clear();
            showPreview = false;
            countNew = countOverwrite = countSkip = countSourceDup = 0;
        }

        private void ScanSpineFolders()
        {
            ResetScanState();
            sourceFolder = sourceFolder.TrimEnd('/', '\\');

            // 收集所有包含 Spine 资源的文件夹
            var allDirs = new List<string>();
            try
            {
                allDirs.AddRange(Directory.GetDirectories(sourceFolder, "*", SearchOption.AllDirectories));
            }
            catch (Exception e)
            {
                Debug.LogError($"[SpineBatchImporter] 遍历目录失败: {e.Message}");
                return;
            }

            if (IsSpineFolder(sourceFolder))
                allDirs.Insert(0, sourceFolder);

            // 按文件夹名分组（不含父路径）
            var grouped = new Dictionary<string, List<SpineFolderInfo>>();

            foreach (var dir in allDirs)
            {
                if (!IsSpineFolder(dir)) continue;

                var folderName = Path.GetFileName(dir);
                var destFolder = targetFolder + "/" + folderName;

                var info = new SpineFolderInfo
                {
                    sourcePath = dir,
                    destPath = destFolder,
                    folderName = folderName,
                    srcTime = Directory.GetLastWriteTime(dir)
                };

                if (!grouped.TryGetValue(folderName, out var list))
                {
                    list = new List<SpineFolderInfo>();
                    grouped[folderName] = list;
                }
                list.Add(info);
            }

            // 每个分组：按源修改时间降序排序，第一个为最新（有效），其余标记为源内重名
            foreach (var kvp in grouped)
            {
                var list = kvp.Value;
                list.Sort((a, b) => b.srcTime.CompareTo(a.srcTime));

                for (int i = 0; i < list.Count; i++)
                {
                    var info = list[i];

                    if (i == 0)
                    {
                        // 最新版本，与目标对比
                        if (AssetDatabase.IsValidFolder(info.destPath))
                        {
                            string absoluteDestFolder = GetAbsolutePath(info.destPath);
                            info.destTime = Directory.GetLastWriteTime(absoluteDestFolder);

                            if (info.srcTime > info.destTime)
                            {
                                info.status = SpineImportStatus.Overwrite;
                                countOverwrite++;
                            }
                            else
                            {
                                info.status = SpineImportStatus.Skip;
                                countSkip++;
                            }
                        }
                        else
                        {
                            info.status = SpineImportStatus.New;
                            countNew++;
                        }
                    }
                    else
                    {
                        // 源内重名的旧版本
                        info.status = SpineImportStatus.SourceDuplicate;
                        countSourceDup++;
                    }

                    scanResults.Add(info);
                }
            }

            // 排序：按文件夹名 + 状态显示
            scanResults = scanResults
                .OrderBy(x => x.folderName)
                .ThenBy(x => x.status)
                .ToList();
        }

        /// <summary>
        /// 判断文件夹是否包含 Spine 资源（骨架 + 图集）
        /// </summary>
        private bool IsSpineFolder(string dir)
        {
            if (!Directory.Exists(dir)) return false;

            var files = Directory.GetFiles(dir, "*", SearchOption.TopDirectoryOnly);
            bool hasSkeleton = files.Any(f =>
                f.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".skel.bytes", StringComparison.OrdinalIgnoreCase));
            bool hasAtlas = files.Any(f =>
                f.EndsWith(".atlas.txt", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".atlas", StringComparison.OrdinalIgnoreCase));

            return hasSkeleton && hasAtlas;
        }

        // ==================== 导入逻辑 ====================

        private void BatchImport()
        {
            EnsureFolderExists(targetFolder);

            int successNew = 0;
            int successOverwrite = 0;
            int skippedCount = 0;
            int failCount = 0;
            List<string> failedFolders = new List<string>();

            var toProcess = scanResults
                .Where(x => x.status == SpineImportStatus.New || x.status == SpineImportStatus.Overwrite)
                .ToList();

            for (int i = 0; i < toProcess.Count; i++)
            {
                var info = toProcess[i];

                bool cancelled = EditorUtility.DisplayCancelableProgressBar(
                    "批量导入 Spine",
                    $"正在处理: {info.folderName} ({i + 1}/{toProcess.Count})",
                    (float)i / toProcess.Count);

                if (cancelled)
                {
                    Debug.LogWarning("[SpineBatchImporter] 用户取消了批量导入。");
                    break;
                }

                try
                {
                    // 覆盖时，彻底删除旧文件夹，保证目标目录干净
                    if (info.status == SpineImportStatus.Overwrite)
                    {
                        Debug.Log($"[SpineBatchImporter] 源文件较新，删除并覆盖: {info.destPath}");
                        AssetDatabase.DeleteAsset(info.destPath);
                        AssetDatabase.Refresh();
                    }

                    EnsureFolderExists(info.destPath);
                    CopySpineFiles(info.sourcePath, info.destPath);
                    AssetDatabase.Refresh();

                    try
                    {
                        IngestSpineAssets(info.destPath);
                    }
                    catch (Exception e)
                    {
                        // Spine 的 AssetPostprocessor 通常会在 Refresh 后自动处理导入，
                        // 这里失败也不代表最终失败，仅作为警告。
                        Debug.LogWarning(
                            $"[SpineBatchImporter] 手动生成 SkeletonDataAsset 跳过: {info.folderName}。原因: {e.Message}");
                    }

                    if (info.status == SpineImportStatus.New) successNew++;
                    else successOverwrite++;
                }
                catch (Exception e)
                {
                    failCount++;
                    failedFolders.Add($"{info.folderName}: {e.Message}");
                    Debug.LogError($"导入失败 [{info.folderName}]: {e.Message}");
                }
            }

            AssetDatabase.Refresh();
            EditorUtility.ClearProgressBar();

            // 统计源内重名（被静默跳过）的数量
            int sourceDupSkipped = scanResults.Count(x => x.status == SpineImportStatus.SourceDuplicate);
            int targetSkipped = scanResults.Count(x => x.status == SpineImportStatus.Skip);
            skippedCount = sourceDupSkipped + targetSkipped;

            string resultMsg = "导入完成！\n\n" +
                               $"新增导入: {successNew}\n" +
                               $"覆盖导入: {successOverwrite}\n" +
                               $"跳过(目标较新): {targetSkipped}\n" +
                               $"跳过(源内重名): {sourceDupSkipped}\n" +
                               $"失败: {failCount}";

            if (failedFolders.Count > 0)
            {
                resultMsg += "\n\n失败详情:\n" + string.Join("\n", failedFolders.Take(10));
                if (failedFolders.Count > 10)
                    resultMsg += $"\n... 还有 {failedFolders.Count - 10} 条，请查看 Console。";
            }

            EditorUtility.DisplayDialog("批量导入结果", resultMsg, "OK");
        }

        /// <summary>
        /// 复制 Spine 相关文件到目标文件夹
        /// </summary>
        private void CopySpineFiles(string srcDir, string destDir)
        {
            string absoluteDestDir = GetAbsolutePath(destDir);
            if (!Directory.Exists(absoluteDestDir))
                Directory.CreateDirectory(absoluteDestDir);

            var files = Directory.GetFiles(srcDir);
            foreach (var file in files)
            {
                string fileName = Path.GetFileName(file);
                if (IsSpineFile(fileName))
                {
                    string destFile = Path.Combine(absoluteDestDir, fileName);
                    File.Copy(file, destFile, true);
                }
            }
        }

        private bool IsSpineFile(string fileName)
        {
            string lower = fileName.ToLowerInvariant();
            return lower.EndsWith(".json")
                || lower.EndsWith(".skel.bytes")
                || lower.EndsWith(".atlas.txt")
                || lower.EndsWith(".atlas")
                || lower.EndsWith(".png")
                || lower.EndsWith(".jpg")
                || lower.EndsWith(".jpeg");
        }

        /// <summary>
        /// 使用 Spine 官方工具生成 SkeletonDataAsset（适配 spine-unity 4.x+）
        /// </summary>
        private void IngestSpineAssets(string folderPath)
        {
            // 查找 JSON 骨架文件（排除 .atlas.json）
            var jsonFiles = Directory.GetFiles(folderPath, "*.json", SearchOption.TopDirectoryOnly)
                .Where(f => !f.ToLowerInvariant().EndsWith(".atlas.json"))
                .ToArray();

            // 也支持 .skel.bytes 二进制骨架
            var skelFiles = Directory.GetFiles(folderPath, "*.skel.bytes", SearchOption.TopDirectoryOnly);

            if (jsonFiles.Length == 0 && skelFiles.Length == 0)
                throw new Exception("未找到骨架文件 (.json 或 .skel.bytes)");

            AssetDatabase.Refresh();

            string targetFile = jsonFiles.Length > 0 ? jsonFiles[0] : skelFiles[0];
            string assetPath = "Assets" + targetFile
                .Substring(Application.dataPath.Length)
                .Replace("\\", "/");

            TextAsset skeletonAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
            if (skeletonAsset == null)
            {
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                skeletonAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
            }

            if (skeletonAsset == null)
                throw new Exception($"无法加载骨架文件: {assetPath}");

            // 查找图集文件
            var atlasFiles = Directory.GetFiles(folderPath, "*.atlas.txt", SearchOption.TopDirectoryOnly)
                .Concat(Directory.GetFiles(folderPath, "*.atlas", SearchOption.TopDirectoryOnly))
                .ToArray();

            List<AtlasAssetBase> atlasAssetBases = new List<AtlasAssetBase>();
            foreach (var atlasFile in atlasFiles)
            {
                string atlasPath = "Assets" + atlasFile
                    .Substring(Application.dataPath.Length)
                    .Replace("\\", "/");
                AssetDatabase.ImportAsset(atlasPath, ImportAssetOptions.ForceUpdate);

                var assets = AssetDatabase.LoadAllAssetsAtPath(atlasPath);
                if (assets != null)
                    atlasAssetBases.AddRange(assets.OfType<AtlasAssetBase>());
            }

            if (atlasAssetBases.Count == 0)
                throw new Exception("未找到 AtlasAssetBase，请确认 Atlas 文件已正确导入。");

            CreateSkeletonDataAsset(skeletonAsset, atlasAssetBases.ToArray(), folderPath);
        }

        /// <summary>
        /// 创建或更新 SkeletonDataAsset（存在则更新，避免 GUID 变化）
        /// </summary>
        private void CreateSkeletonDataAsset(TextAsset skeletonAsset, AtlasAssetBase[] atlasAssets, string folderPath)
        {
            string destFolder = targetFolder + "/" + Path.GetFileName(folderPath);
            EnsureFolderExists(destFolder);

            string assetFileName = Path.GetFileNameWithoutExtension(skeletonAsset.name) + "_SkeletonData.asset";
            string assetFullPath = destFolder + "/" + assetFileName;

            SkeletonDataAsset existingAsset = AssetDatabase.LoadAssetAtPath<SkeletonDataAsset>(assetFullPath);

            if (existingAsset != null)
            {
                // 更新原有资产（保留 GUID，避免引用丢失）
                existingAsset.skeletonJSON = skeletonAsset;
                existingAsset.atlasAssets = atlasAssets;
                existingAsset.scale = skeletonScale;
                EditorUtility.SetDirty(existingAsset);
                AssetDatabase.SaveAssets();
                Debug.Log($"<color=green>[SpineBatchImporter]</color> 已更新 SkeletonDataAsset: {assetFullPath}");
            }
            else
            {
                // 新建
                SkeletonDataAsset skeletonDataAsset = ScriptableObject.CreateInstance<SkeletonDataAsset>();
                skeletonDataAsset.skeletonJSON = skeletonAsset;
                skeletonDataAsset.atlasAssets = atlasAssets;
                skeletonDataAsset.scale = skeletonScale;

                AssetDatabase.CreateAsset(skeletonDataAsset, assetFullPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"<color=green>[SpineBatchImporter]</color> 已创建 SkeletonDataAsset: {assetFullPath}");
            }
        }

        // ==================== 辅助方法 ====================

        private string GetAbsolutePath(string unityPath)
        {
            return Path.Combine(Directory.GetCurrentDirectory(), unityPath);
        }

        private void EnsureFolderExists(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
                return;

            string[] parts = folderPath.Split('/');
            string current = parts[0];

            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }

        private string GetRelativePath(string fullPath, string basePath)
        {
            if (string.IsNullOrEmpty(basePath)) return fullPath;
            string normalizedFull = fullPath.Replace("\\", "/");
            string normalizedBase = basePath.Replace("\\", "/").TrimEnd('/');

            if (normalizedFull.StartsWith(normalizedBase))
            {
                string relative = normalizedFull.Substring(normalizedBase.Length);
                return string.IsNullOrEmpty(relative) ? "(根目录)" : relative.TrimStart('/');
            }
            return fullPath;
        }
    }
}