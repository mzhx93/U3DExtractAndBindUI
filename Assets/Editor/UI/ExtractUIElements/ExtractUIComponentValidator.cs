using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
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

            return ValidateByPath(assetPath, cfg, out prefabAsset, out formComponent, out errorMessage);
        }

        /// <summary>
        /// 从 Hierarchy 选中的节点向上查找最外层 UIForm 预制件并校验。
        /// 同时支持 Prefab Stage 和普通场景中的预制件实例。
        /// </summary>
        public static bool TryGetValidPrefabFromHierarchy(
            out GameObject prefabAsset,
            out Component  formComponent,
            out string     errorMessage)
        {
            prefabAsset   = null;
            formComponent = null;
            errorMessage  = string.Empty;

            var cfg = ExtractUIComponentConfig.GetOrCreate();
            if (cfg == null)
            {
                errorMessage = "未找到插件配置，请先初始化。";
                return false;
            }

            var selected = Selection.activeGameObject;
            if (selected == null)
            {
                errorMessage = "未在 Hierarchy 中选中任何对象。";
                return false;
            }

            string assetPath;

            // ── Prefab Stage 模式 ─────────────────────────────────────────────
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null)
            {
                if (stage.prefabContentsRoot == null)
                {
                    errorMessage = "当前 Prefab Stage 根节点无效。";
                    return false;
                }
                assetPath = stage.assetPath.Replace('\\', '/');
            }
            else
            {
                // ── 普通场景中的预制件实例 ─────────────────────────────────────
                if (!PrefabUtility.IsAnyPrefabInstanceRoot(selected) &&
                    !PrefabUtility.IsPartOfPrefabInstance(selected))
                {
                    errorMessage = "选中对象不是预制件实例或其一部分，请在预制件内部右键。";
                    return false;
                }

                assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(selected)
                                         .Replace('\\', '/');
            }

            if (string.IsNullOrEmpty(assetPath))
            {
                errorMessage = "无法获取预制件资产路径。";
                return false;
            }

            return ValidateByPath(assetPath, cfg, out prefabAsset, out formComponent, out errorMessage);
        }

        // ── 共用路径校验 ──────────────────────────────────────────────────────
        private static bool ValidateByPath(
            string                  assetPath,
            ExtractUIComponentConfig cfg,
            out GameObject          prefabAsset,
            out Component           formComponent,
            out string              errorMessage)
        {
            prefabAsset   = null;
            formComponent = null;
            errorMessage  = string.Empty;

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

            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (asset == null)
            {
                errorMessage = $"无法加载预制件资产：{assetPath}";
                return false;
            }

            var comp = asset.GetComponent(formType);
            if (comp == null)
            {
                errorMessage = $"预制件根节点未挂载 {formType.Name} 组件。";
                return false;
            }

            prefabAsset   = asset;
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
