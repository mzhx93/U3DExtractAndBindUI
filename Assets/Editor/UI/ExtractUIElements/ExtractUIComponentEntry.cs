using System;
using UnityEditor;
using UnityEngine;

namespace CQEditorTools
{
    /// <summary>
    /// 插件所有菜单入口的统一出口。
    /// 顶部菜单路径：Window/界面工具/
    /// Assets 右键路径：Assets/Tools/
    /// </summary>
    public static class ExtractUIComponentEntry
    {
        private const string MenuConfig    = "Window/界面工具/初始化";
        private const string MenuRules     = "Window/界面工具/检索规则";
        private const string MenuExclusion = "Window/界面工具/互斥规则";
        private const string MenuUpdate    = "Assets/Tools/更新UI组件字段";

        // ── Window 菜单 ───────────────────────────────────────────────────────

        /// <summary>打开初始化配置窗口。已有配置时预填参数，支持覆盖。</summary>
        [MenuItem(MenuConfig, false, 1)]
        private static void OpenConfig()
        {
            var existing = ExtractUIComponentConfig.GetOrCreate();
            ExtractUIComponentProjectConfigWindow.Open(existing, null);
        }

        /// <summary>打开组件检索规则编辑窗口。</summary>
        [MenuItem(MenuRules, false, 2)]
        private static void OpenRules()
        {
            ExtractUIComponentSearchRuleWindow.Open();
        }

        /// <summary>检索规则菜单的可用性校验：仅当配置文件存在时允许点击。</summary>
        [MenuItem(MenuRules, true, 2)]
        private static bool OpenRulesValidate()
        {
            return ExtractUIComponentConfig.GetOrCreate() != null;
        }

        /// <summary>打开组件互斥规则配置窗口。</summary>
        [MenuItem(MenuExclusion, false, 3)]
        private static void OpenExclusion()
        {
            ExtractUIComponentExclusionRuleWindow.Open();
        }

        /// <summary>互斥规则菜单的可用性校验：仅当配置文件存在时允许点击。</summary>
        [MenuItem(MenuExclusion, true, 3)]
        private static bool OpenExclusionValidate()
        {
            return ExtractUIComponentConfig.GetOrCreate() != null;
        }

        // ── Assets 右键菜单 ───────────────────────────────────────────────────

        /// <summary>对选中的 UIForm 预制件执行组件字段更新。</summary>
        [MenuItem(MenuUpdate, false, 100)]
        private static void UpdateUIComponentFields()
        {
            var cfg = ExtractUIComponentConfig.GetOrCreate();
            if (cfg == null)
            {
                EditorUtility.DisplayDialog("提示", "未找到插件配置，请先通过 Window/界面工具/初始化 完成配置。", "确定");
                return;
            }

            if (!ExtractUIComponentValidator.TryGetValidPrefab(out var prefabAsset, out var formComponent, out var errorMsg))
            {
                EditorUtility.DisplayDialog("无效的 UIForm 预制件", errorMsg, "确定");
                return;
            }

            // Step 2：遍历收集完成，打开预览窗口供用户调整
            var assetPath = AssetDatabase.GetAssetPath(prefabAsset);
            var entries   = ExtractUIComponentHierarchyCollector.Collect(prefabAsset, cfg);

            if (entries.Count == 0)
            {
                EditorUtility.DisplayDialog("提示",
                    $"{prefabAsset.name}：未收集到任何需要导出的组件。\n" +
                    "请确认标记符和检索规则已正确配置。", "确定");
                return;
            }

            ExtractUIComponentPreviewWindow.Open(prefabAsset, formComponent, entries);
        }

        /// <summary>Assets 右键菜单可用性校验（高频调用，仅做最轻量检查）。</summary>
        [MenuItem(MenuUpdate, true, 100)]
        private static bool UpdateUIComponentFieldsValidate()
        {
            if (ExtractUIComponentConfig.GetOrCreate() == null)
                return false;

            var obj = Selection.activeObject;
            if (obj == null)
                return false;

            var path = AssetDatabase.GetAssetPath(obj);
            return path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);
        }

        // ── Hierarchy 右键菜单 ────────────────────────────────────────────────

        private const string MenuHierarchyUpdate = "GameObject/Tools/更新UI组件字段";

        /// <summary>Hierarchy 右键触发：向上查找最外层 UIForm 预制件后打开预览窗口。</summary>
        [MenuItem(MenuHierarchyUpdate, false, 100)]
        private static void HierarchyUpdateUIComponentFields()
        {
            var cfg = ExtractUIComponentConfig.GetOrCreate();
            if (cfg == null)
            {
                EditorUtility.DisplayDialog("提示", "未找到插件配置，请先通过 Window/界面工具/初始化 完成配置。", "确定");
                return;
            }

            if (!ExtractUIComponentValidator.TryGetValidPrefabFromHierarchy(
                    out var prefabAsset, out var formComponent, out var errorMsg))
            {
                EditorUtility.DisplayDialog("无效的 UIForm 预制件", errorMsg, "确定");
                return;
            }

            var entries = ExtractUIComponentHierarchyCollector.Collect(prefabAsset, cfg);
            if (entries.Count == 0)
            {
                EditorUtility.DisplayDialog("提示",
                    $"{prefabAsset.name}：未收集到任何需要导出的组件。\n" +
                    "请确认标记符和检索规则已正确配置。", "确定");
                return;
            }

            ExtractUIComponentPreviewWindow.Open(prefabAsset, formComponent, entries);
        }

        /// <summary>Hierarchy 右键菜单可用性校验（轻量：有配置 + 有选中对象即可）。</summary>
        [MenuItem(MenuHierarchyUpdate, true, 100)]
        private static bool HierarchyUpdateUIComponentFieldsValidate()
        {
            if (ExtractUIComponentConfig.GetOrCreate() == null)
                return false;
            return Selection.activeGameObject != null;
        }
    }
}
