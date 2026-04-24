using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace CQEditorTools
{
    /// <summary>编译后把组件实例绑定到 partial 类字段的持久化任务数据。</summary>
    [Serializable]
    public sealed class ExtractUIComponentPendingBindings
    {
        /// <summary>预制件的 Assets/... 资产路径，编译后仍有效。</summary>
        public string PrefabAssetPath;
        /// <summary>UIForm 组件的 AssemblyQualifiedName，用于编译后反射获取类型。</summary>
        public string FormTypeName;
        public List<ExtractUIComponentFieldBinding> Fields = new List<ExtractUIComponentFieldBinding>();
        public int RetryLeft;
    }

    /// <summary>单条字段绑定数据。</summary>
    [Serializable]
    public sealed class ExtractUIComponentFieldBinding
    {
        public string FieldName;
        /// <summary>从预制件根节点到目标节点的相对路径（Transform.Find 格式）。</summary>
        public string NodePath;
        /// <summary>组件类型的 AssemblyQualifiedName。</summary>
        public string ComponentTypeName;
    }

    /// <summary>
    /// 编译完成后将组件实例绑定到 UIForm partial 字段并保存预制件。
    /// 使用独立 SessionState 键，不与旧版 UIFormExport 冲突。
    /// </summary>
    [InitializeOnLoad]
    public static class ExtractUIComponentBindingApplier
    {
        private const string PendingKey   = "CQEditorTools.ExtractUIComponent.PendingBindings";
        private const int    DefaultRetry = 5;

        static ExtractUIComponentBindingApplier()
        {
            AssemblyReloadEvents.afterAssemblyReload += TryApplyPendingBindings;
            EditorApplication.delayCall               += TryApplyPendingBindings;
        }

        // ── 存储待绑定任务 ────────────────────────────────────────────────────
        /// <summary>在 AssetDatabase.Refresh() 前调用，将绑定任务写入 SessionState。</summary>
        public static void StorePendingBindings(
            string                             prefabAssetPath,
            string                             formTypeName,
            List<ComponentFieldEntry>          fields)
        {
            if (string.IsNullOrEmpty(prefabAssetPath) || fields == null || fields.Count == 0)
                return;

            var bindings = new List<ExtractUIComponentFieldBinding>(fields.Count);
            foreach (var f in fields)
            {
                bindings.Add(new ExtractUIComponentFieldBinding
                {
                    FieldName         = f.FieldName,
                    NodePath          = f.NodePath,
                    ComponentTypeName = f.AssemblyQualifiedTypeName
                });
            }

            var data = new ExtractUIComponentPendingBindings
            {
                PrefabAssetPath = prefabAssetPath,
                FormTypeName    = formTypeName,
                Fields          = bindings,
                RetryLeft       = DefaultRetry
            };
            SessionState.SetString(PendingKey, JsonUtility.ToJson(data));
        }

        // ── 编译后应用绑定 ────────────────────────────────────────────────────
        private static void TryApplyPendingBindings()
        {
            var json = SessionState.GetString(PendingKey, string.Empty);
            if (string.IsNullOrEmpty(json))
                return;

            ExtractUIComponentPendingBindings data;
            try
            {
                data = JsonUtility.FromJson<ExtractUIComponentPendingBindings>(json);
            }
            catch
            {
                SessionState.EraseString(PendingKey);
                return;
            }

            if (data == null || string.IsNullOrEmpty(data.PrefabAssetPath) || data.Fields == null)
            {
                SessionState.EraseString(PendingKey);
                return;
            }

            // 加载预制件资产
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(data.PrefabAssetPath);
            if (prefab == null)
            {
                RetryOrClear(data);
                return;
            }

            // 反射获取 UIForm 组件类型（编译后已有新类型定义）
            var formType = Type.GetType(data.FormTypeName);
            if (formType == null)
            {
                RetryOrClear(data);
                return;
            }

            var formComp = prefab.GetComponent(formType);
            if (formComp == null)
            {
                RetryOrClear(data);
                return;
            }

            // 逐字段绑定
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Undo.RecordObject(formComp, "ExtractUIComponent 绑定字段");

            bool anyBound = false;
            foreach (var binding in data.Fields)
            {
                if (string.IsNullOrEmpty(binding.FieldName))
                    continue;

                var fieldInfo = formType.GetField(binding.FieldName, flags);
                if (fieldInfo == null)
                    continue;

                var comp = FindComponentByPath(prefab.transform, binding.NodePath, binding.ComponentTypeName);
                if (comp == null || !fieldInfo.FieldType.IsAssignableFrom(comp.GetType()))
                    continue;

                fieldInfo.SetValue(formComp, comp);
                anyBound = true;
            }

            if (!anyBound)
            {
                // 字段尚未编译进程序集，重试
                RetryOrClear(data);
                return;
            }

            // 保存预制件
            EditorUtility.SetDirty(formComp);
            PrefabUtility.SavePrefabAsset(prefab);
            AssetDatabase.SaveAssets();
            SessionState.EraseString(PendingKey);

            Debug.Log($"[ExtractUIComponent] 绑定完成：{data.PrefabAssetPath}，共绑定 {data.Fields.Count} 个字段。");
        }

        // ── 工具方法 ──────────────────────────────────────────────────────────
        private static Component FindComponentByPath(Transform root, string path, string componentTypeName)
        {
            if (root == null || string.IsNullOrEmpty(componentTypeName))
                return null;

            var target = string.IsNullOrEmpty(path) ? root : root.Find(path);
            if (target == null)
                return null;

            var type = Type.GetType(componentTypeName);
            if (type == null)
                return null;

            return target.GetComponent(type);
        }

        private static void RetryOrClear(ExtractUIComponentPendingBindings data)
        {
            data.RetryLeft--;
            if (data.RetryLeft <= 0)
            {
                Debug.LogWarning($"[ExtractUIComponent] 绑定超时，已放弃：{data.PrefabAssetPath}");
                SessionState.EraseString(PendingKey);
                return;
            }

            SessionState.SetString(PendingKey, JsonUtility.ToJson(data));
            EditorApplication.delayCall += TryApplyPendingBindings;
        }
    }
}
