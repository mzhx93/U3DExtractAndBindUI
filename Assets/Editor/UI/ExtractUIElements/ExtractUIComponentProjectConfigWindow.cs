using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CQEditorTools
{
    /// <summary>
    /// 项目级配置向导窗口，用于填写并创建 ExtractUIComponentConfig 资产。
    /// 有配置时预填参数，支持直接更新或整体覆盖。
    /// </summary>
    public sealed class ExtractUIComponentProjectConfigWindow : EditorWindow
    {
        /// <summary>用户填写的 UIForm Prefab 根路径列表。</summary>
        private List<string> m_PrefabRootPaths = new() { string.Empty };
        /// <summary>用户填写的 UIForm 根基类完整限定名。</summary>
        private string m_FormBaseTypeFullName = string.Empty;

        private List<string> m_PathErrors = new();
        private string m_FormTypeError;
        private string m_SubmitError;

        /// <summary>是否为覆盖已有配置的编辑模式。</summary>
        private bool m_IsEditing;
        private Action<ExtractUIComponentConfig> m_OnCreated;
        private Vector2 m_Scroll;

        /// <summary>
        /// 打开项目配置窗口。existing 非空时进入覆盖模式并预填参数。
        /// </summary>
        public static void Open(ExtractUIComponentConfig existing, Action<ExtractUIComponentConfig> onCreated)
        {
            var win = GetWindow<ExtractUIComponentProjectConfigWindow>(true, "界面工具 - 项目配置", true);
            win.minSize = new Vector2(480f, 320f);
            win.m_OnCreated = onCreated;
            win.m_IsEditing = existing != null;

            if (existing != null)
            {
                win.m_PrefabRootPaths = existing.ValidPrefabRootPaths != null && existing.ValidPrefabRootPaths.Count > 0
                    ? new List<string>(existing.ValidPrefabRootPaths)
                    : new List<string>() { string.Empty };
                win.m_FormBaseTypeFullName = existing.FormBaseTypeFullName ?? string.Empty;
                win.m_PathErrors = new List<string>(new string[win.m_PrefabRootPaths.Count]);
            }
            else
            {
                win.m_PrefabRootPaths = new List<string>() { string.Empty };
                win.m_FormBaseTypeFullName = string.Empty;
                win.m_PathErrors = new List<string>();
            }

            win.m_SubmitError = null;
            win.Show();
        }

        private void OnGUI()
        {
            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("UIForm Prefab 根路径", EditorStyles.boldLabel);
            DrawPrefabRootPaths();

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("UIForm 基类全称（命名空间.类名）", EditorStyles.boldLabel);
            DrawValidatedTextField(ref m_FormBaseTypeFullName, ref m_FormTypeError, ValidateFormBaseType);

            EditorGUILayout.Space(12f);
            if (!string.IsNullOrEmpty(m_SubmitError))
                EditorGUILayout.HelpBox(m_SubmitError, MessageType.Error);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (m_IsEditing && GUILayout.Button("更新", GUILayout.Width(80f)))
                TryUpdate();
            var btnLabel = m_IsEditing ? "覆盖配置" : "创建配置";
            if (GUILayout.Button(btnLabel, GUILayout.Width(90f)))
                TryCreate();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(6f);
            EditorGUILayout.EndScrollView();
        }

        private void DrawPrefabRootPaths()
        {
            while (m_PathErrors.Count < m_PrefabRootPaths.Count)
                m_PathErrors.Add(null);

            for (int i = 0; i < m_PrefabRootPaths.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                var newVal = EditorGUILayout.TextField(m_PrefabRootPaths[i]);
                if (newVal != m_PrefabRootPaths[i])
                {
                    m_PrefabRootPaths[i] = newVal;
                    m_PathErrors[i] = ValidatePrefabPath(newVal);
                }
                GUI.enabled = m_PrefabRootPaths.Count > 1;
                if (GUILayout.Button("-", GUILayout.Width(22f)))
                {
                    m_PrefabRootPaths.RemoveAt(i);
                    m_PathErrors.RemoveAt(i);
                    GUI.enabled = true;
                    break;
                }
                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();

                if (!string.IsNullOrEmpty(m_PathErrors[i]))
                    EditorGUILayout.HelpBox(m_PathErrors[i], MessageType.Warning);
            }

            if (GUILayout.Button("+ 添加路径", GUILayout.Width(90f)))
            {
                m_PrefabRootPaths.Add(string.Empty);
                m_PathErrors.Add(null);
            }
        }

        private void DrawValidatedTextField(ref string value, ref string error, Func<string, string> validate)
        {
            EditorGUI.BeginChangeCheck();
            var newVal = EditorGUILayout.TextField(value);
            if (EditorGUI.EndChangeCheck())
            {
                value = newVal;
                error = validate(newVal);
            }
            if (!string.IsNullOrEmpty(error))
                EditorGUILayout.HelpBox(error, MessageType.Warning);
        }

        private void TryUpdate()
        {
            m_SubmitError = null;

            for (int i = 0; i < m_PrefabRootPaths.Count; i++)
                m_PathErrors[i] = ValidatePrefabPath(m_PrefabRootPaths[i]);
            m_FormTypeError = ValidateFormBaseType(m_FormBaseTypeFullName);

            var errors = new List<string>();
            if (m_PathErrors.Any(e => !string.IsNullOrEmpty(e)))
                errors.Add("UIForm Prefab 根路径存在无效项");
            if (!string.IsNullOrEmpty(m_FormTypeError))
                errors.Add(m_FormTypeError);
            if (m_PrefabRootPaths.Count == 0 || m_PrefabRootPaths.All(string.IsNullOrWhiteSpace))
                errors.Add("至少需要一条有效的 UIForm Prefab 根路径");

            if (errors.Count > 0)
            {
                m_SubmitError = string.Join("\n", errors);
                return;
            }

            var cfg = ExtractUIComponentConfig.GetOrCreate();
            if (cfg == null)
            {
                m_SubmitError = "未找到配置文件，请使用「创建配置」新建。";
                return;
            }

            // 仅更新路径与基类字段，保留检索规则、互斥规则、标识符等其他配置不变
            cfg.ValidPrefabRootPaths = m_PrefabRootPaths.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
            cfg.FormBaseTypeFullName = m_FormBaseTypeFullName.Trim();

            EditorUtility.SetDirty(cfg);
            AssetDatabase.SaveAssets();

            m_OnCreated?.Invoke(cfg);
            Repaint();
        }

        private void TryCreate()
        {
            m_SubmitError = null;

            for (int i = 0; i < m_PrefabRootPaths.Count; i++)
                m_PathErrors[i] = ValidatePrefabPath(m_PrefabRootPaths[i]);
            m_FormTypeError = ValidateFormBaseType(m_FormBaseTypeFullName);

            var errors = new List<string>();
            if (m_PathErrors.Any(e => !string.IsNullOrEmpty(e)))
                errors.Add("UIForm Prefab 根路径存在无效项");
            if (!string.IsNullOrEmpty(m_FormTypeError))
                errors.Add(m_FormTypeError);
            if (m_PrefabRootPaths.Count == 0 || m_PrefabRootPaths.All(string.IsNullOrWhiteSpace))
                errors.Add("至少需要一条有效的 UIForm Prefab 根路径");

            if (errors.Count > 0)
            {
                m_SubmitError = string.Join("\n", errors);
                return;
            }

            if (m_IsEditing)
            {
                var confirm = EditorUtility.DisplayDialog(
                    "ExtractUIComponent",
                    "已存在配置文件，是否覆盖？",
                    "覆盖",
                    "取消");
                if (!confirm)
                    return;

                ExtractUIComponentConfig.DeleteConfig();
            }

            var saveFolder = ExtractUIComponentConfig.ResolveSaveFolder();
            var savePath = ExtractUIComponentConfig.ResolveSavePath();

            if (!AssetDatabase.IsValidFolder(saveFolder))
                AssetDatabase.CreateFolder("Assets", "Editor Default Resources");

            var config = CreateInstance<ExtractUIComponentConfig>();
            config.ValidPrefabRootPaths = m_PrefabRootPaths.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
            config.FormBaseTypeFullName = m_FormBaseTypeFullName.Trim();
            config.ComponentRules = ExtractUIComponentConfig.GetDefaultRules();

            AssetDatabase.CreateAsset(config, savePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            m_OnCreated?.Invoke(config);
            Close();
            ExtractUIComponentSearchRuleWindow.Open();
        }

        private static string ValidatePrefabPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "路径不能为空";
            if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                return $"路径必须以 Assets/ 开头：{path}";
            return null;
        }

        private static string ValidateFormBaseType(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "基类全称不能为空，格式：命名空间.类名（例：UnityGameFramework.Runtime.UIFormLogic）";
            if (!name.Contains('.'))
                return "请填写完整限定名，格式：命名空间.类名（例：UnityGameFramework.Runtime.UIFormLogic）";
            var type = FindTypeInAllAssemblies(name.Trim());
            if (type == null)
                return $"未找到类型：{name}，请确认命名空间和类名正确";
            return null;
        }

        private static Type FindTypeInAllAssemblies(string typeName)
        {
            var mainAsm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
            var type = mainAsm?.GetTypes().FirstOrDefault(t => t.FullName == typeName);
            if (type != null)
                return type;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = asm.GetType(typeName);
                if (type != null)
                    return type;
            }
            return null;
        }
    }
}
