using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TOOL
{
    public class EffectSortingOrderWindow : EditorWindow
    {
        [SerializeField] private List<GameObject> effectRoots = new List<GameObject>();
        private int baseOrder;
        private Vector2 effectListScrollPosition;
    
        [MenuItem("TOOL/特效层级重置")]
        private static void OpenWindow()
        {
            GetWindow<EffectSortingOrderWindow>(true, "特效层级重置", true);
        }
    
        private void OnEnable()
        {
            minSize = new Vector2(380f, 240f);
        }
    
        private void OnGUI()
        {
            baseOrder = EditorGUILayout.IntField("起始层级", baseOrder);
            EditorGUILayout.HelpBox("可以添加多个特效根物体。每个特效独立按原层级从小到大压缩；相同层级保持相同。", MessageType.Info);
            DrawEffectList();
    
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("添加当前选中物体"))
                {
                    AddSelectedObjects();
                }
    
                if (GUILayout.Button("清空列表"))
                {
                    effectRoots.Clear();
                }
            }
    
            using (new EditorGUI.DisabledScope(effectRoots.Count == 0))
            {
                if (GUILayout.Button("批量重置特效层级"))
                {
                    ResetEffectSortingOrders();
                }
            }
        }
    
        private void DrawEffectList()
        {
            using (var scrollView = new EditorGUILayout.ScrollViewScope(effectListScrollPosition, GUILayout.MinHeight(90f)))
            {
                effectListScrollPosition = scrollView.scrollPosition;
                for (int i = 0; i < effectRoots.Count; i++)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        effectRoots[i] = EditorGUILayout.ObjectField(
                            "特效 " + (i + 1),
                            effectRoots[i],
                            typeof(GameObject),
                            true) as GameObject;
                        if (GUILayout.Button("-", GUILayout.Width(24f)))
                        {
                            effectRoots.RemoveAt(i);
                            GUIUtility.ExitGUI();
                        }
                    }
                }
            }
    
            DrawDropArea();
    
            if (GUILayout.Button("+ 添加一行"))
            {
                effectRoots.Add(null);
            }
        }
    
        private void DrawDropArea()
        {
            Rect dropArea = GUILayoutUtility.GetRect(0f, 42f, GUILayout.ExpandWidth(true));
            GUI.Box(dropArea, "将多个特效从 Hierarchy 或 Project 拖到这里");
    
            Event currentEvent = Event.current;
            if (!dropArea.Contains(currentEvent.mousePosition))
            {
                return;
            }
    
            if (currentEvent.type == EventType.DragUpdated || currentEvent.type == EventType.DragPerform)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                if (currentEvent.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    foreach (UnityEngine.Object draggedObject in DragAndDrop.objectReferences)
                    {
                        AddEffectRoot(draggedObject as GameObject);
                    }
    
                    currentEvent.Use();
                    Repaint();
                    return;
                }
    
                currentEvent.Use();
            }
        }
    
        private void AddSelectedObjects()
        {
            foreach (GameObject selectedObject in Selection.gameObjects)
            {
                AddEffectRoot(selectedObject);
            }
        }
    
        private void AddEffectRoot(GameObject root)
        {
            if (root != null && !effectRoots.Contains(root))
            {
                effectRoots.Add(root);
            }
        }
    
        private void ResetEffectSortingOrders()
        {
            var rootsToProcess = new List<GameObject>();
            foreach (GameObject root in effectRoots)
            {
                if (root != null && !rootsToProcess.Contains(root))
                {
                    rootsToProcess.Add(root);
                }
            }
    
            if (rootsToProcess.Count == 0)
            {
                return;
            }
    
            foreach (GameObject root in rootsToProcess)
            {
                if (EditorUtility.IsPersistent(root) && !PrefabUtility.IsPartOfPrefabAsset(root))
                {
                    EditorUtility.DisplayDialog("无法处理", "列表包含不支持的资源，请使用场景中的特效或 Project 面板中的 Prefab。", "确定");
                    return;
                }
            }
    
            int processedEffects = 0;
            int emptyEffects = 0;
            int rendererCount = 0;
            int distinctOrderCount = 0;
            foreach (GameObject root in rootsToProcess)
            {
                bool isPrefabAsset = PrefabUtility.IsPartOfPrefabAsset(root);
                int effectRendererCount;
                int effectDistinctOrderCount;
                if (isPrefabAsset)
                {
                    ResetPrefabAsset(root, out effectRendererCount, out effectDistinctOrderCount);
                }
                else
                {
                    ResetSceneObject(root, out effectRendererCount, out effectDistinctOrderCount);
                }
    
                if (effectRendererCount == 0)
                {
                    emptyEffects++;
                    continue;
                }
    
                processedEffects++;
                rendererCount += effectRendererCount;
                distinctOrderCount += effectDistinctOrderCount;
            }
    
            ShowBatchResult(processedEffects, emptyEffects, rendererCount, distinctOrderCount);
        }
    
        private void ResetPrefabAsset(GameObject effectRoot, out int rendererCount, out int distinctOrderCount)
        {
            string assetPath = AssetDatabase.GetAssetPath(effectRoot);
            GameObject prefabContents = PrefabUtility.LoadPrefabContents(assetPath);
    
            try
            {
                List<Renderer> renderers = GetEffectRenderers(prefabContents);
                if (renderers.Count == 0)
                {
                    rendererCount = 0;
                    distinctOrderCount = 0;
                    return;
                }
    
                Dictionary<int, int> orderMap = CreateOrderMap(renderers);
                ApplyOrderMap(renderers, orderMap);
    
                GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(prefabContents, assetPath);
                if (savedPrefab == null)
                {
                    throw new InvalidOperationException("保存特效 Prefab 失败：" + assetPath);
                }
    
                rendererCount = renderers.Count;
                distinctOrderCount = orderMap.Count;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabContents);
            }
        }
    
        private void ResetSceneObject(GameObject root, out int rendererCount, out int distinctOrderCount)
        {
            List<Renderer> renderers = GetEffectRenderers(root);
            if (renderers.Count == 0)
            {
                rendererCount = 0;
                distinctOrderCount = 0;
                return;
            }
    
            Dictionary<int, int> orderMap = CreateOrderMap(renderers);
            var undoObjects = new UnityEngine.Object[renderers.Count];
            for (int i = 0; i < renderers.Count; i++)
            {
                undoObjects[i] = renderers[i];
            }
    
            Undo.RecordObjects(undoObjects, "重置特效层级");
            ApplyOrderMap(renderers, orderMap);
            rendererCount = renderers.Count;
            distinctOrderCount = orderMap.Count;
        }
    
        private List<Renderer> GetEffectRenderers(GameObject root)
        {
            var renderers = new List<Renderer>();
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is ParticleSystemRenderer || renderer is TrailRenderer)
                {
                    renderers.Add(renderer);
                }
            }
    
            return renderers;
        }
    
        private Dictionary<int, int> CreateOrderMap(List<Renderer> renderers)
        {
            var sourceOrders = new SortedSet<int>();
            foreach (Renderer renderer in renderers)
            {
                sourceOrders.Add(renderer.sortingOrder);
            }
    
            if (sourceOrders.Count > 0 && (long)baseOrder + sourceOrders.Count - 1 > int.MaxValue)
            {
                throw new InvalidOperationException("起始层级过大，连续层级会超出整数范围。");
            }
    
            var orderMap = new Dictionary<int, int>();
            int nextOrder = baseOrder;
            foreach (int sourceOrder in sourceOrders)
            {
                orderMap.Add(sourceOrder, nextOrder);
                nextOrder++;
            }
    
            return orderMap;
        }
    
        private void ApplyOrderMap(List<Renderer> renderers, Dictionary<int, int> orderMap)
        {
            foreach (Renderer renderer in renderers)
            {
                renderer.sortingOrder = orderMap[renderer.sortingOrder];
                if (PrefabUtility.IsPartOfPrefabInstance(renderer))
                {
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                }
            }
        }
    
        private void ShowBatchResult(int processedEffects, int emptyEffects, int rendererCount, int distinctOrderCount)
        {
            EditorUtility.DisplayDialog(
                "特效层级已重置",
                "已处理 " + processedEffects + " 个特效、" + rendererCount + " 个 Renderer，压缩 " + distinctOrderCount + " 个不同层级。"
                + (emptyEffects > 0 ? "\n另有 " + emptyEffects + " 个特效没有粒子或拖尾 Renderer。" : string.Empty),
                "确定");
        }
    }
}