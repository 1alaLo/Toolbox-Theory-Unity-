using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UI;
using UnityEngine;
using UnityEngine.UI;

[CustomEditor(typeof(Image), true)]
[CanEditMultipleObjects]
public class ImageAutoSize : ImageEditor
{
    private Dictionary<Image, Sprite> lastSprites = new Dictionary<Image, Sprite>();

    protected override void OnEnable()
    {
        base.OnEnable();
        lastSprites.Clear();
        foreach (var obj in targets)
        {
            var img = obj as Image;
            if (img != null)
                lastSprites[img] = img.sprite;
        }
    }

    public override void OnInspectorGUI()
    {
        // 先绘制原生 Image Inspector
        base.OnInspectorGUI();

        // 检测每个 Image 的 Sprite 是否发生变化
        bool anyChanged = false;
        foreach (var obj in targets)
        {
            var img = obj as Image;
            if (img == null) continue;

            Sprite current = img.sprite;
            if (!lastSprites.ContainsKey(img) || lastSprites[img] != current)
            {
                lastSprites[img] = current;
                anyChanged = true;
            }
        }

        // 如果 Sprite 变化且工具已启用，则自动调整尺寸
        if (anyChanged && AutoSizeImageWindow.IsEnabled)
        {
            foreach (var obj in targets)
            {
                var img = obj as Image;
                if (img != null && img.sprite != null)
                {
                    ApplySize(img,
                        AutoSizeImageWindow.MatchWidthOnly,
                        AutoSizeImageWindow.MaxWidth,
                        AutoSizeImageWindow.MaxHeight);
                    EditorUtility.SetDirty(img);
                }
            }
        }
    }

    /// <summary>
    /// 根据 Sprite 原始尺寸计算并应用宽高
    /// </summary>
    public static void ApplySize(Image img, bool matchWidthOnly, float maxWidth, float maxHeight)
    {
        if (img == null || img.sprite == null) return;

        RectTransform rt = img.rectTransform;
        Vector2 size = GetNativeSize(img.sprite, img.pixelsPerUnitMultiplier);

        // 仅按宽度等比缩放
        if (matchWidthOnly && size.y > 0f)
        {
            float width = rt.rect.width;
            if (width <= 0f) width = size.x;
            float aspect = size.x / size.y;
            size = new Vector2(width, width / aspect);
        }

        // 最大尺寸限制（等比缩小）
        float scale = 1f;
        if (maxWidth > 0f && size.x > maxWidth)
            scale = Mathf.Min(scale, maxWidth / size.x);
        if (maxHeight > 0f && size.y > maxHeight)
            scale = Mathf.Min(scale, maxHeight / size.y);

        if (scale < 1f) size *= scale;

        // 应用尺寸，兼容 Stretch 锚点
        Undo.RecordObject(rt, "Auto Size Image");
        rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, size.x);
        rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, size.y);
    }

    private static Vector2 GetNativeSize(Sprite sprite, float pixelsPerUnitMultiplier)
    {
        // 直接使用 Sprite 的像素尺寸作为 UI 宽高，不做 PPU 换算
        Vector2 size = sprite.rect.size;

        // 如需兼容 pixelsPerUnitMultiplier，可保留这行；否则直接 return size
        float mult = pixelsPerUnitMultiplier > 0f ? pixelsPerUnitMultiplier : 1f;
        size *= mult;

        return size;
    }
}

public class AutoSizeImageWindow : EditorWindow
{
    private const string PrefKeyEnabled = "AutoSizeImage_Enabled";
    private const string PrefKeyMaxWidth = "AutoSizeImage_MaxWidth";
    private const string PrefKeyMaxHeight = "AutoSizeImage_MaxHeight";
    private const string PrefKeyMatchWidthOnly = "AutoSizeImage_MatchWidthOnly";

    private bool enabled;
    private float maxWidth;
    private float maxHeight;
    private bool matchWidthOnly;

    [MenuItem("TOOL/Auto Size Image Helper")]
    public static void ShowWindow()
    {
        GetWindow<AutoSizeImageWindow>("Auto Size Image Helper");
    }

    private void OnEnable()
    {
        enabled = EditorPrefs.GetBool(PrefKeyEnabled, true);
        maxWidth = EditorPrefs.GetFloat(PrefKeyMaxWidth, 0f);
        maxHeight = EditorPrefs.GetFloat(PrefKeyMaxHeight, 0f);
        matchWidthOnly = EditorPrefs.GetBool(PrefKeyMatchWidthOnly, false);
    }

    private void OnGUI()
    {
        GUILayout.Label("Auto Size Image Helper", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        EditorGUILayout.HelpBox(
            "此工具会在你为原生 Image 组件设置 Sprite 时，自动将 RectTransform 的宽高设置为 Sprite 的原始尺寸。\n" +
            "无需添加任何额外组件，全局生效。\n" +
            "你可以在下方调整最大尺寸限制和等比缩放选项。",
            MessageType.Info);

        EditorGUILayout.Space();

        EditorGUI.BeginChangeCheck();
        enabled = EditorGUILayout.Toggle("启用自动尺寸", enabled);
        matchWidthOnly = EditorGUILayout.Toggle("仅按宽度等比缩放", matchWidthOnly);
        maxWidth = EditorGUILayout.FloatField("最大宽度 (0 = 不限制)", maxWidth);
        maxHeight = EditorGUILayout.FloatField("最大高度 (0 = 不限制)", maxHeight);
        if (EditorGUI.EndChangeCheck())
        {
            EditorPrefs.SetBool(PrefKeyEnabled, enabled);
            EditorPrefs.SetBool(PrefKeyMatchWidthOnly, matchWidthOnly);
            EditorPrefs.SetFloat(PrefKeyMaxWidth, maxWidth);
            EditorPrefs.SetFloat(PrefKeyMaxHeight, maxHeight);
        }

        EditorGUILayout.Space();

        if (GUILayout.Button("立即为选中的 Image 应用尺寸"))
        {
            ApplyToSelected();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("提示：在 Image 组件的 Inspector 中更改 Sprite 时会自动触发。", EditorStyles.miniLabel);
    }

    private void ApplyToSelected()
    {
        var selected = Selection.gameObjects;
        foreach (var go in selected)
        {
            var img = go.GetComponent<UnityEngine.UI.Image>();
            if (img != null && img.sprite != null)
            {
                ImageAutoSize.ApplySize(img, matchWidthOnly, maxWidth, maxHeight);
                EditorUtility.SetDirty(img);
            }
        }
    }

    // 供 Editor 扩展读取的静态属性
    public static bool IsEnabled => EditorPrefs.GetBool(PrefKeyEnabled, true);
    public static bool MatchWidthOnly => EditorPrefs.GetBool(PrefKeyMatchWidthOnly, false);
    public static float MaxWidth => EditorPrefs.GetFloat(PrefKeyMaxWidth, 0f);
    public static float MaxHeight => EditorPrefs.GetFloat(PrefKeyMaxHeight, 0f);
}