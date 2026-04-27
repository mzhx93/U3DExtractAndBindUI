using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CQEditorTools
{
    /// <summary>
    /// 面板1（合并面板4）：项目级配置 + 节点标识符配置 + 导出规则说明。
    /// 有配置时预填参数，支持直接更新或整体覆盖；同时管理 4 个导出标识符。
    /// </summary>
    public sealed class ExtractUIComponentProjectConfigWindow : EditorWindow
    {
        // ── 项目配置数据 ──────────────────────────────────────────────────────
        private List<string> m_PrefabRootPaths     = new() { string.Empty };
        private string       m_FormBaseTypeFullName = string.Empty;
        private List<string> m_PathErrors           = new();
        private string       m_FormTypeError;
        private string       m_SubmitError;
        private bool         m_IsEditing;
        private Action<ExtractUIComponentConfig> m_OnCreated;

        // ── 标识符配置数据 ────────────────────────────────────────────────────
        private readonly string[] m_EditValues = new string[4];
        private string m_MarkerValidationError;
        private string m_CachedDoc;

        // ── 样式 ──────────────────────────────────────────────────────────────
        private GUIStyle m_DocStyle;
        private GUIStyle m_DescLabelStyle;

        // ── 布局常量 ──────────────────────────────────────────────────────────
        private const float ColBehavior = 140f;
        private const float ColChar     = 42f;
        private const float RowH        = 22f;

        // ── 标识符描述（静态，与配置无关）────────────────────────────────────
        private static readonly string[] s_BehaviorLabels =
        {
            "仅导出自己", "仅跳过自己", "导出自己和子级", "跳过自己和子级"
        };

        private static readonly string[] s_Descriptions =
        {
            "此节点的组件导出为字段  |  仅作用于此节点本身，子节点各自独立",
            "此节点的组件不导出      |  仅作用于此节点本身，子节点各自独立",
            "此节点的组件导出为字段  |  子树中未声明标记的节点默认也导出（有标记的保持不变）",
            "此节点的组件不导出      |  子树中未声明标记的节点默认也不导出（有标记的保持不变）"
        };

        // ── 滚动 ──────────────────────────────────────────────────────────────
        private Vector2 m_DocScroll;

        // ── 入口 ──────────────────────────────────────────────────────────────
        /// <summary>打开项目配置窗口。existing 非空时进入编辑模式并预填参数。</summary>
        public static void Open(ExtractUIComponentConfig existing, Action<ExtractUIComponentConfig> onCreated)
        {
            var win = GetWindow<ExtractUIComponentProjectConfigWindow>(
                true, "界面工具 - 项目配置", true);
            win.minSize   = new Vector2(520f, 660f);
            win.m_OnCreated = onCreated;
            win.m_IsEditing = existing != null;

            if (existing != null)
            {
                win.m_PrefabRootPaths = existing.ValidPrefabRootPaths != null &&
                                        existing.ValidPrefabRootPaths.Count > 0
                    ? new List<string>(existing.ValidPrefabRootPaths)
                    : new List<string> { string.Empty };
                win.m_FormBaseTypeFullName = existing.FormBaseTypeFullName ?? string.Empty;
                win.m_PathErrors           = new List<string>(new string[win.m_PrefabRootPaths.Count]);
            }
            else
            {
                win.m_PrefabRootPaths      = new List<string> { string.Empty };
                win.m_FormBaseTypeFullName  = string.Empty;
                win.m_PathErrors            = new List<string>();
            }

            win.m_SubmitError = null;
            win.LoadMarkersFromConfig(existing);
            win.Show();
        }

        private void OnEnable()
        {
            LoadMarkersFromConfig(ExtractUIComponentConfig.GetOrCreate());
        }

        // ── 数据加载 ──────────────────────────────────────────────────────────
        private void LoadMarkersFromConfig(ExtractUIComponentConfig cfg)
        {
            var m = cfg?.Markers ?? new ExportMarkers();
            m_EditValues[0]         = m.Export;
            m_EditValues[1]         = m.Skip;
            m_EditValues[2]         = m.ExportPropagate;
            m_EditValues[3]         = m.SkipPropagate;
            m_MarkerValidationError = null;
            RefreshDoc();
        }

        // ── 样式初始化 ────────────────────────────────────────────────────────
        private void EnsureStyles()
        {
            if (m_DocStyle != null)
                return;

            m_DocStyle = new GUIStyle(EditorStyles.textArea)
            {
                fontSize = Mathf.Min(15, Mathf.RoundToInt(EditorStyles.label.fontSize * 1.2f)),
                wordWrap = false,
                richText = false
            };
            m_DescLabelStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = false };
        }

        private void RefreshDoc()
        {
            m_CachedDoc = ExtractUIComponentRuleDoc.Format(BuildTempMarkers());
        }

        private ExportMarkers BuildTempMarkers() => new ExportMarkers
        {
            Export          = m_EditValues[0] ?? string.Empty,
            Skip            = m_EditValues[1] ?? string.Empty,
            ExportPropagate = m_EditValues[2] ?? string.Empty,
            SkipPropagate   = m_EditValues[3] ?? string.Empty
        };

        // ── OnGUI ─────────────────────────────────────────────────────────────
        private void OnGUI()
        {
            EnsureStyles();

            // ── 项目配置（仅输入，无按钮）────────────────────────────────────
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("UIForm Prefab 根路径", EditorStyles.boldLabel);
            DrawPrefabRootPaths();

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("UIForm 基类全称（命名空间.类名）", EditorStyles.boldLabel);
            DrawValidatedTextField(ref m_FormBaseTypeFullName, ref m_FormTypeError, ValidateFormBaseType);

            DrawSeparator();

            // ── 标识符配置 ────────────────────────────────────────────────────
            EditorGUILayout.Space(4f);
            DrawMarkersSection();
            EditorGUILayout.Space(4f);

            // ── 错误提示 + 统一操作按钮 ──────────────────────────────────────
            DrawSeparator();
            if (!string.IsNullOrEmpty(m_SubmitError))
                EditorGUILayout.HelpBox(m_SubmitError, MessageType.Error);
            EditorGUILayout.Space(4f);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (m_IsEditing && GUILayout.Button("更新", GUILayout.Width(80f)))
                TryUpdate();
            var btnLabel = m_IsEditing ? "覆盖配置" : "创建配置";
            if (GUILayout.Button(btnLabel, GUILayout.Width(90f)))
                TryCreate();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4f);

            // ── 规则说明（展开填充剩余空间）──────────────────────────────────
            DrawSeparator();
            EditorGUILayout.Space(4f);
            DrawDocSection();
        }

        // ── 项目配置绘制 ──────────────────────────────────────────────────────
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
                    m_PathErrors[i]       = ValidatePrefabPath(newVal);
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

        // ── 标识符配置绘制 ────────────────────────────────────────────────────
        private void DrawMarkersSection()
        {
            // 标题行：标签 + 紧邻的重置按钮
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("节点标识符", EditorStyles.boldLabel,
                GUILayout.Width(90f));
            if (GUILayout.Button("重置为默认值", GUILayout.Width(110f)))
                ResetAndSaveMarkers();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4f);

            // 表头
            var headerRect = EditorGUILayout.BeginHorizontal(GUILayout.Height(RowH));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(headerRect, new Color(0f, 0f, 0f, 0.12f));
            GUILayout.Label("行为",   EditorStyles.boldLabel, GUILayout.Width(ColBehavior));
            GUILayout.Label("字符",   EditorStyles.boldLabel, GUILayout.Width(ColChar));
            GUILayout.Label("说明",   EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();

            for (int i = 0; i < 4; i++)
                DrawMarkerRow(i);

            if (!string.IsNullOrEmpty(m_MarkerValidationError))
            {
                EditorGUILayout.Space(4f);
                EditorGUILayout.HelpBox(m_MarkerValidationError, MessageType.Error);
            }
        }

        private void DrawMarkerRow(int i)
        {
            var bgColor = i % 2 == 0 ? ExtractUITableColors.RowEven : ExtractUITableColors.RowOdd;
            var rowRect = EditorGUILayout.BeginHorizontal(GUILayout.Height(RowH));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rowRect, bgColor);

            GUILayout.Label(s_BehaviorLabels[i], GUILayout.Width(ColBehavior));

            EditorGUI.BeginChangeCheck();
            var newVal = EditorGUILayout.TextField(m_EditValues[i], GUILayout.Width(ColChar));
            if (newVal.Length > 1)
                newVal = newVal[newVal.Length - 1].ToString();
            if (EditorGUI.EndChangeCheck())
            {
                m_EditValues[i]         = newVal;
                m_MarkerValidationError = null;
                RefreshDoc();
                Repaint();
            }

            GUILayout.Label(s_Descriptions[i], m_DescLabelStyle);
            EditorGUILayout.EndHorizontal();
        }

        // ── 规则说明绘制 ──────────────────────────────────────────────────────
        private void DrawDocSection()
        {
            EditorGUILayout.LabelField("导出规则说明", EditorStyles.boldLabel);
            EditorGUILayout.Space(4f);

            m_DocScroll = EditorGUILayout.BeginScrollView(m_DocScroll, GUILayout.ExpandHeight(true));
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextArea(
                    m_CachedDoc ?? string.Empty,
                    m_DocStyle,
                    GUILayout.ExpandHeight(true));
            }
            EditorGUILayout.EndScrollView();
        }

        // ── 重置标识符并立即保存 ──────────────────────────────────────────────
        private void ResetAndSaveMarkers()
        {
            var def = new ExportMarkers();
            m_EditValues[0]         = def.Export;
            m_EditValues[1]         = def.Skip;
            m_EditValues[2]         = def.ExportPropagate;
            m_EditValues[3]         = def.SkipPropagate;
            m_MarkerValidationError = null;
            RefreshDoc();
            GUI.FocusControl(null);

            var cfg = ExtractUIComponentConfig.GetOrCreate();
            if (cfg == null) return;

            if (cfg.Markers == null) cfg.Markers = new ExportMarkers();
            cfg.Markers.Export          = def.Export;
            cfg.Markers.Skip            = def.Skip;
            cfg.Markers.ExportPropagate = def.ExportPropagate;
            cfg.Markers.SkipPropagate   = def.SkipPropagate;

            EditorUtility.SetDirty(cfg);
            AssetDatabase.SaveAssets();
        }

        // ── 项目配置 + 标识符合并保存 ─────────────────────────────────────────
        private void TryUpdate()
        {
            m_SubmitError = null;

            for (int i = 0; i < m_PrefabRootPaths.Count; i++)
                m_PathErrors[i] = ValidatePrefabPath(m_PrefabRootPaths[i]);
            m_FormTypeError = ValidateFormBaseType(m_FormBaseTypeFullName);

            var tempMarkers = BuildTempMarkers();
            m_MarkerValidationError = tempMarkers.Validate();

            var errors = CollectConfigErrors();
            if (!string.IsNullOrEmpty(m_MarkerValidationError))
                errors.Add(m_MarkerValidationError);

            if (errors.Count > 0) { m_SubmitError = string.Join("\n", errors); return; }

            var cfg = ExtractUIComponentConfig.GetOrCreate();
            if (cfg == null) { m_SubmitError = "未找到配置文件，请使用「创建配置」新建。"; return; }

            cfg.ValidPrefabRootPaths = m_PrefabRootPaths.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
            cfg.FormBaseTypeFullName = m_FormBaseTypeFullName.Trim();

            if (cfg.Markers == null) cfg.Markers = new ExportMarkers();
            cfg.Markers.Export          = tempMarkers.Export;
            cfg.Markers.Skip            = tempMarkers.Skip;
            cfg.Markers.ExportPropagate = tempMarkers.ExportPropagate;
            cfg.Markers.SkipPropagate   = tempMarkers.SkipPropagate;

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

            var errors = CollectConfigErrors();
            if (errors.Count > 0) { m_SubmitError = string.Join("\n", errors); return; }

            if (m_IsEditing)
            {
                if (!EditorUtility.DisplayDialog("ExtractUIComponent", "已存在配置文件，是否覆盖？", "覆盖", "取消"))
                    return;
                ExtractUIComponentConfig.DeleteConfig();
            }

            var saveFolder = ExtractUIComponentConfig.ResolveSaveFolder();
            var savePath   = ExtractUIComponentConfig.ResolveSavePath();

            if (!AssetDatabase.IsValidFolder(saveFolder))
                AssetDatabase.CreateFolder("Assets", "Editor Default Resources");

            var config = CreateInstance<ExtractUIComponentConfig>();
            config.ValidPrefabRootPaths = m_PrefabRootPaths.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
            config.FormBaseTypeFullName = m_FormBaseTypeFullName.Trim();
            config.ComponentRules       = ExtractUIComponentConfig.GetDefaultRules();

            // 将当前编辑的标识符写入新配置（若合法）
            var tempMarkers = BuildTempMarkers();
            if (string.IsNullOrEmpty(tempMarkers.Validate()))
                config.Markers = tempMarkers;

            AssetDatabase.CreateAsset(config, savePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            m_OnCreated?.Invoke(config);
            Close();
            ExtractUIComponentSearchRuleWindow.Open();
        }

        private List<string> CollectConfigErrors()
        {
            var errors = new List<string>();
            if (m_PathErrors.Any(e => !string.IsNullOrEmpty(e)))
                errors.Add("UIForm Prefab 根路径存在无效项");
            if (!string.IsNullOrEmpty(m_FormTypeError))
                errors.Add(m_FormTypeError);
            if (m_PrefabRootPaths.Count == 0 || m_PrefabRootPaths.All(string.IsNullOrWhiteSpace))
                errors.Add("至少需要一条有效的 UIForm Prefab 根路径");
            return errors;
        }

        // ── 校验工具 ──────────────────────────────────────────────────────────
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

        // ── 分隔线 ────────────────────────────────────────────────────────────
        private static void DrawSeparator()
        {
            EditorGUILayout.Space(4f);
            var rect = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.4f));
            EditorGUILayout.Space(4f);
        }
    }
}
