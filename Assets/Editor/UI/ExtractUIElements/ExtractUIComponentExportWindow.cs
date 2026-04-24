using UnityEditor;
using UnityEngine;

namespace CQEditorTools
{
    /// <summary>
    /// 面板4：导出设置窗口。
    /// 上半部分：4个导出标识符的配置表格，支持单字符编辑、保存、重置。
    /// 下半部分：滚动显示由标识符填充的导出规则说明文档。
    /// </summary>
    public sealed class ExtractUIComponentExportWindow : EditorWindow
    {
        // ── 数据 ──────────────────────────────────────────────────────────────
        /// <summary>编辑区暂存的4个标识符字符串（与 Config 脱耦，保存时才写回）。</summary>
        private readonly string[] m_EditValues = new string[4];

        private string m_ValidationError;
        private bool m_IsDirty;
        private string m_CachedDoc;

        // ── 样式（首次 OnGUI 时创建）────────────────────────────────────────
        private GUIStyle m_DocStyle;
        private GUIStyle m_DescLabelStyle;

        // ── 布局常量 ──────────────────────────────────────────────────────────
        private const float ColBehavior = 140f;
        private const float ColChar = 42f;
        private const float RowH = 22f;

        // ── 静态描述（与配置无关）────────────────────────────────────────────
        private static readonly string[] s_BehaviorLabels =
        {
            "仅导出自己",
            "仅跳过自己",
            "导出自己和子级",
            "跳过自己和子级"
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
        /// <summary>打开导出设置窗口，从当前配置加载标识符。</summary>
        public static void Open()
        {
            var win = GetWindow<ExtractUIComponentExportWindow>(false, "界面工具 - 导出设置", true);
            win.minSize = new Vector2(520f, 540f);
            win.LoadFromConfig();
            win.Show();
        }

        private void OnEnable()
        {
            LoadFromConfig();
        }

        // ── 数据加载 ──────────────────────────────────────────────────────────
        private void LoadFromConfig()
        {
            var cfg = ExtractUIComponentConfig.GetOrCreate();
            var m = cfg?.Markers ?? new ExportMarkers();

            m_EditValues[0] = m.Export;
            m_EditValues[1] = m.Skip;
            m_EditValues[2] = m.ExportPropagate;
            m_EditValues[3] = m.SkipPropagate;

            m_ValidationError = null;
            m_IsDirty = false;
            RefreshDoc();
        }

        // ── 样式初始化 ────────────────────────────────────────────────────────
        private void EnsureStyles()
        {
            if (m_DocStyle != null)
                return;

            m_DocStyle = new GUIStyle(EditorStyles.textArea)
            {
                // 文档字体比默认 label 大 20%，不超过 15pt
                fontSize = Mathf.Min(15, Mathf.RoundToInt(EditorStyles.label.fontSize * 1.2f)),
                wordWrap = false,
                richText = false
            };

            m_DescLabelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                wordWrap = false
            };
        }

        // ── 规则说明缓存刷新 ──────────────────────────────────────────────────
        private void RefreshDoc()
        {
            m_CachedDoc = ExtractUIComponentRuleDoc.Format(BuildTempMarkers());
        }

        private ExportMarkers BuildTempMarkers() => new ExportMarkers
        {
            Export = m_EditValues[0] ?? string.Empty,
            Skip = m_EditValues[1] ?? string.Empty,
            ExportPropagate = m_EditValues[2] ?? string.Empty,
            SkipPropagate = m_EditValues[3] ?? string.Empty
        };

        // ── OnGUI ─────────────────────────────────────────────────────────────
        private void OnGUI()
        {
            EnsureStyles();

            EditorGUILayout.Space(6f);
            DrawMarkersSection();

            EditorGUILayout.Space(6f);
            DrawSeparator();
            EditorGUILayout.Space(4f);

            DrawDocSection();
        }

        // ── 标识符配置表格 ────────────────────────────────────────────────────
        private void DrawMarkersSection()
        {
            EditorGUILayout.LabelField("标识符配置", EditorStyles.boldLabel);
            EditorGUILayout.Space(4f);

            DrawTableHeader();

            for (int i = 0; i < 4; i++)
                DrawMarkerRow(i);

            EditorGUILayout.Space(4f);

            if (!string.IsNullOrEmpty(m_ValidationError))
                EditorGUILayout.HelpBox(m_ValidationError, MessageType.Error);

            EditorGUILayout.Space(4f);
            DrawActionButtons();
        }

        private void DrawTableHeader()
        {
            var rect = EditorGUILayout.BeginHorizontal(GUILayout.Height(RowH));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.12f));

            GUILayout.Label("行为", EditorStyles.boldLabel, GUILayout.Width(ColBehavior));
            GUILayout.Label("字符", EditorStyles.boldLabel, GUILayout.Width(ColChar));
            GUILayout.Label("说明", EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();
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

            // 强制单字符：若用户输入超过1个字符，取最后输入的那个（末尾字符）
            if (newVal.Length > 1)
                newVal = newVal[newVal.Length - 1].ToString();

            if (EditorGUI.EndChangeCheck())
            {
                m_EditValues[i] = newVal;
                m_IsDirty = true;
                m_ValidationError = null;
                RefreshDoc();
                Repaint();
            }

            GUILayout.Label(s_Descriptions[i], m_DescLabelStyle);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawActionButtons()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("重置为默认值", GUILayout.Width(110f)))
            {
                var def = new ExportMarkers();
                m_EditValues[0] = def.Export;
                m_EditValues[1] = def.Skip;
                m_EditValues[2] = def.ExportPropagate;
                m_EditValues[3] = def.SkipPropagate;
                m_ValidationError = null;
                m_IsDirty = true;
                RefreshDoc();
                GUI.FocusControl(null);
                SaveToConfig();
            }

            GUILayout.Space(8f);

            GUI.enabled = m_IsDirty;
            if (GUILayout.Button("保存", GUILayout.Width(60f)))
                SaveToConfig();
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();
        }

        // ── 保存 ─────────────────────────────────────────────────────────────
        private void SaveToConfig()
        {
            var cfg = ExtractUIComponentConfig.GetOrCreate();
            if (cfg == null)
            {
                EditorUtility.DisplayDialog("提示", "未找到配置文件，请先初始化配置。", "确定");
                return;
            }

            var temp = BuildTempMarkers();
            var error = temp.Validate();
            if (!string.IsNullOrEmpty(error))
            {
                m_ValidationError = error;
                return;
            }

            if (cfg.Markers == null)
                cfg.Markers = new ExportMarkers();

            cfg.Markers.Export = temp.Export;
            cfg.Markers.Skip = temp.Skip;
            cfg.Markers.ExportPropagate = temp.ExportPropagate;
            cfg.Markers.SkipPropagate = temp.SkipPropagate;

            EditorUtility.SetDirty(cfg);
            AssetDatabase.SaveAssets();

            m_ValidationError = null;
            m_IsDirty = false;
            EditorUtility.DisplayDialog("ExtractUIComponent", "标识符已保存。", "确定");
        }

        // ── 规则说明区域 ──────────────────────────────────────────────────────
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

        // ── 分隔线 ────────────────────────────────────────────────────────────
        private static void DrawSeparator()
        {
            var rect = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.4f));
        }
    }
}
