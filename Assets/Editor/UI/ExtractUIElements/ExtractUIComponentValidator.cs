using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CQEditorTools
{
    /// <summary>
    /// UIForm 预制件合法性校验辅助类，供菜单入口与提取逻辑共用。
    /// </summary>
    public static class ExtractUIComponentValidator
    {
        /// <summary>
        /// 校验当前 Project 窗口中选中的 .prefab 资产是否为有效 UIForm：
        /// 1. 路径在 ValidPrefabRootPaths 之内
        /// 2. 根节点挂载了 FormBaseTypeFullName 指定的组件
        /// 返回 true 时 prefabAsset / formComponent 有效；否则 errorMessage 包含原因。
        /// </summary>
        public static bool TryGetValidPrefab(out GameObject prefabAsset, out Component formComponent, out string errorMessage)
        {
            prefabAsset = null;
            formComponent = null;
            errorMessage = string.Empty;

            var cfg = ExtractUIComponentConfig.GetOrCreate();
            if (cfg == null)
            {
                errorMessage = "未找到插件配置，请先初始化。";
                return false;
            }

            var selected = Selection.activeObject as GameObject;
            if (selected == null)
            {
                errorMessage = "未选中有效的 GameObject 资产。";
                return false;
            }

            var assetPath = AssetDatabase.GetAssetPath(selected).Replace('\\', '/');
            if (string.IsNullOrEmpty(assetPath) || !assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = "选中对象不是预制件资产。";
                return false;
            }

            var validPaths = cfg.ValidPrefabRootPaths;
            if (validPaths == null || validPaths.Count == 0)
            {
                errorMessage = "配置中未设置有效的 UIForm Prefab 根路径，请在面板1中配置。";
                return false;
            }

            bool inValidPath = validPaths.Any(root =>
                !string.IsNullOrWhiteSpace(root) &&
                assetPath.StartsWith(root.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));

            if (!inValidPath)
            {
                errorMessage = $"预制件不在配置的根路径下。\n当前路径：{assetPath}\n" +
                               $"有效根路径：\n  {string.Join("\n  ", validPaths)}";
                return false;
            }

            var formType = FindTypeInAllAssemblies(cfg.FormBaseTypeFullName);
            if (formType == null)
            {
                errorMessage = $"未找到 UIForm 基类类型：{cfg.FormBaseTypeFullName}，请检查面板1配置。";
                return false;
            }

            var comp = selected.GetComponent(formType);
            if (comp == null)
            {
                errorMessage = $"预制件根节点未挂载 {formType.Name} 组件。";
                return false;
            }

            prefabAsset = selected;
            formComponent = comp;
            return true;
        }

        // ── 工具方法 ──────────────────────────────────────────────────────────
        /// <summary>在所有已加载程序集中按完整类型名查找类型。</summary>
        public static Type FindTypeInAllAssemblies(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return null;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(typeName);
                if (t != null)
                    return t;
            }
            return null;
        }
    }
}
