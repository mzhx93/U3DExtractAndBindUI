using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CQEditorTools
{
    /// <summary>
    /// 组件互斥规则配置窗口，以二维矩阵呈现组件间的互斥关系。
    /// 行（纵轴）= 触发条件（存在此组件时）；列（横轴）= 被跳过的组件。
    /// 数据来源为面板2中 IsEnabled=true 的组件，采用两阶段评估确保结果与组件顺序无关。
    /// 存储键使用 ComponentTypeFullName，显示标签使用完整类名的最后一段。
    /// </summary>
    public sealed class ExtractUIComponentExclusionRuleWindow : EditorWindow
    {
        // ── 数据 ─────────────────────────────────────────────────────────────
        /// <summary>当前参与互斥配置的组件规则（仅 IsEnabled=true 的条目）。</summary>
        private List<ComponentExportRule> m_Components = new();
        /// <summary>
        /// 互斥矩阵：m_Matrix[row, col]=true 表示节点上存在 row 组件时跳过 col 组件的检索。
        /// 对角线（row==col）永远为 false 且禁用。
        /// </summary>
        private bool[,] m_Matrix;

        // ── 布局（打开时计算一次，OnGUI 只读） ──────────────────────────────
        /// <summary>列标题区域高度，根据最长列名字符数（限制在 [10,28]）估算，仅在 LoadFromConfig 时赋值。</summary>
        private float m_HeaderHeight = 90f;
        /// <summary>行标签列宽度，根据最长行名字符数（限制在 [10,28]）估算，仅在 LoadFromConfig 时赋值。</summary>
        private float m_RowLabelWidth = 150f;

        private const float CellSize  = 38f;
        private const float RowHeight = 28f;
        private const float CharWidth = 7f;
        private const float LayoutPadding = 20f;
        private const int MinLayoutChars = 10;
        private const int MaxLayoutChars = 28;
        /// <summary>标题与行标签字号相对 miniLabel 的放大倍率，同时用于布局估算。</summary>
        private const float LabelFontScale = 1.5f;

        private Vector2 m_Scroll;

        /// <summary>列标题样式：+90° 旋转后上到下显示，UpperRight 使末字贴矩阵上沿。</summary>
        private GUIStyle m_ColHeaderStyle;
        /// <summary>行标签样式：右侧依靠（贴近矩阵左沿）。首次 OnGUI 时创建。</summary>
        private GUIStyle m_RowLabelStyle;

        // ── 入口 ─────────────────────────────────────────────────────────────
        /// <summary>打开互斥规则配置窗口，从当前配置加载矩阵数据并计算布局尺寸。</summary>
        public static void Open()
        {
            var win = GetWindow<ExtractUIComponentExclusionRuleWindow>(false, "界面工具 - 互斥规则", true);
            win.minSize = new Vector2(520f, 420f);
            win.LoadFromConfig();
            win.Show();
        }

        // ── 数据加载 ──────────────────────────────────────────────────────────
        /// <summary>从 Config 加载已启用组件列表、互斥矩阵数据，并一次性计算布局尺寸。</summary>
        private void LoadFromConfig()
        {
            var cfg = ExtractUIComponentConfig.GetOrCreate();
            if (cfg == null)
                return;

            m_Components = (cfg.ComponentRules ?? new List<ComponentExportRule>())
                .Where(r => r.IsEnabled && !string.IsNullOrWhiteSpace(r.ComponentTypeFullName))
                .ToList();

            int n = m_Components.Count;
            m_Matrix = new bool[n, n];

            if (cfg.ExclusionRules != null)
            {
                for (int r = 0; r < n; r++)
                {
                    var rule = cfg.ExclusionRules
                        .FirstOrDefault(er => er.TriggerTypeFullName == m_Components[r].ComponentTypeFullName);
                    if (rule?.ExcludedTypeFullNames == null)
                        continue;

                    for (int c = 0; c < n; c++)
                    {
                        if (r == c) continue;
                        m_Matrix[r, c] = rule.ExcludedTypeFullNames.Contains(m_Components[c].ComponentTypeFullName);
                    }
                }
            }

            CalculateLayout();
        }

        /// <summary>
        /// 根据各组件短名的字符数估算列标题高度和行标签宽度；字符数 clamp 到 [10,28] 防止过短/过长。
        /// 仅在 LoadFromConfig 末尾调用，OnGUI 期间不重复计算。
        /// </summary>
        private void CalculateLayout()
        {
            int maxColLen = 0;
            int maxRowLen = 0;
            foreach (var comp in m_Components)
            {
                int len = ShortName(comp).Length;
                if (len > maxColLen) maxColLen = len;
                if (len > maxRowLen) maxRowLen = len;
            }

            int colChars = Mathf.Clamp(maxColLen, MinLayoutChars, MaxLayoutChars);
            int rowChars = Mathf.Clamp(maxRowLen, MinLayoutChars, MaxLayoutChars);

            m_HeaderHeight  = colChars * CharWidth * LabelFontScale + LayoutPadding;
            m_RowLabelWidth = rowChars * CharWidth * LabelFontScale + LayoutPadding;
        }

        /// <summary>延迟创建标签样式，避免每帧分配。</summary>
        private void EnsureLabelStyles()
        {
            if (m_ColHeaderStyle != null)
                return;

            int scaledFontSize = Mathf.RoundToInt(EditorStyles.miniLabel.fontSize * LabelFontScale);
            m_ColHeaderStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                // UpperRight：文字右对齐，末字在 rect 右边；旋转后右边 = 视觉底，贴矩阵上沿
                alignment = TextAnchor.UpperRight,
                wordWrap = false,
                fontSize = scaledFontSize
            };
            m_RowLabelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight,
                wordWrap = false,
                fontSize = scaledFontSize
            };
        }

        // ── GUI ───────────────────────────────────────────────────────────────
        private void OnGUI()
        {
            if (m_Components == null || m_Components.Count == 0)
            {
                EditorGUILayout.HelpBox("未找到已启用的组件规则，请先在「检索规则」中配置并启用组件。", MessageType.Warning);
                return;
            }

            EnsureLabelStyles();

            EditorGUILayout.HelpBox(
                "互斥规则配置说明：\n" +
                "• 行（左侧）= 触发条件：当节点上存在该组件时生效\n" +
                "• 列（顶部）= 被跳过的组件：勾选后该组件将默认不导出\n" +
                "• 规则不对称：A→B 与 B→A 是独立的，需分别勾选\n" +
                "• 对角线格（—）表示自身，始终禁用\n" +
                "• 配置完成后点击\"保存\"写回配置文件",
                MessageType.Info);
            EditorGUILayout.Space(4f);

            int n = m_Components.Count;

            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);

            float totalWidth  = m_RowLabelWidth + n * CellSize;
            float totalHeight = m_HeaderHeight  + n * RowHeight;
            var matrixRect = GUILayoutUtility.GetRect(totalWidth, totalHeight);

            DrawColumnHeaders(matrixRect, n);
            DrawRows(matrixRect, n);

            EditorGUILayout.EndScrollView();

            DrawFooter();
        }

        // ── 列标题：+90° 旋转，上到下显示，底部贴矩阵上沿 ───────────────────
        private void DrawColumnHeaders(Rect matrixRect, int n)
        {
            float lineH = Mathf.Max(1f, m_ColHeaderStyle.CalcSize(new GUIContent("A")).y);

            for (int c = 0; c < n; c++)
            {
                var cellRect = new Rect(
                    matrixRect.x + m_RowLabelWidth + c * CellSize,
                    matrixRect.y,
                    CellSize,
                    m_HeaderHeight);

                if (Event.current.type == EventType.Repaint)
                {
                    var savedMatrix = GUI.matrix;
                    // pivot = 列水平中心 × 标题区底边（= 矩阵顶边）
                    var pivot = new Vector2(cellRect.x + CellSize * 0.5f, cellRect.y + m_HeaderHeight);
                    GUIUtility.RotateAroundPivot(90f, pivot);
                    // ry = pivot.y - lineH*0.5f：令 label 竖向居中于 pivot，水平方向被旋转展开。
                    // +90° 旋转下：rect 的右边 → 视觉底；UpperRight 使末字在右 → 贴矩阵上沿。
                    GUI.Label(
                        new Rect(pivot.x - m_HeaderHeight, pivot.y - lineH * 0.5f, m_HeaderHeight, lineH),
                        ShortName(m_Components[c]),
                        m_ColHeaderStyle);
                    GUI.matrix = savedMatrix;
                }

                GUI.Label(cellRect, new GUIContent(string.Empty, m_Components[c].ComponentTypeFullName));
            }
        }

        // ── 矩阵行：行标签右侧依靠矩阵 ───────────────────────────────────────
        private void DrawRows(Rect matrixRect, int n)
        {
            for (int r = 0; r < n; r++)
            {
                float rowY = matrixRect.y + m_HeaderHeight + r * RowHeight;
                float rowWidth = m_RowLabelWidth + n * CellSize;

                if (Event.current.type == EventType.Repaint)
                {
                    var rowBg = r % 2 == 0 ? ExtractUITableColors.RowEven : ExtractUITableColors.RowOdd;
                    EditorGUI.DrawRect(new Rect(matrixRect.x, rowY, rowWidth, RowHeight), rowBg);
                }

                var labelRect = new Rect(matrixRect.x + 4f, rowY, m_RowLabelWidth - 8f, RowHeight);
                GUI.Label(labelRect,
                    new GUIContent(ShortName(m_Components[r]), m_Components[r].ComponentTypeFullName),
                    m_RowLabelStyle);

                for (int c = 0; c < n; c++)
                {
                    float cellX = matrixRect.x + m_RowLabelWidth + c * CellSize;

                    if (r == c)
                    {
                        EditorGUI.LabelField(
                            new Rect(cellX + (CellSize - 14f) * 0.5f, rowY + (RowHeight - 14f) * 0.5f, 14f, 14f),
                            "—", EditorStyles.centeredGreyMiniLabel);
                    }
                    else
                    {
                        var toggleRect = new Rect(
                            cellX + (CellSize - 16f) * 0.5f,
                            rowY  + (RowHeight - 16f) * 0.5f,
                            16f, 16f);
                        var tooltip = $"存在 {ShortName(m_Components[r])} 时，跳过 {ShortName(m_Components[c])}";
                        m_Matrix[r, c] = EditorGUI.Toggle(toggleRect,
                            new GUIContent(string.Empty, tooltip), m_Matrix[r, c]);
                    }
                }
            }
        }

        // ── 底部保存按钮 ──────────────────────────────────────────────────────
        private void DrawFooter()
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("保存", GUILayout.Width(60f)))
                SaveToConfig();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4f);
        }

        /// <summary>将矩阵转换为 ComponentExclusionRule 列表并写回 Config 资产。</summary>
        private void SaveToConfig()
        {
            var cfg = ExtractUIComponentConfig.GetOrCreate();
            if (cfg == null)
            {
                EditorUtility.DisplayDialog("提示", "未找到配置文件，请先初始化配置。", "确定");
                return;
            }

            int n = m_Components.Count;
            var rules = new List<ComponentExclusionRule>();

            for (int r = 0; r < n; r++)
            {
                var excluded = new List<string>();
                for (int c = 0; c < n; c++)
                {
                    if (r != c && m_Matrix[r, c])
                        excluded.Add(m_Components[c].ComponentTypeFullName);
                }

                if (excluded.Count > 0)
                    rules.Add(new ComponentExclusionRule
                    {
                        TriggerTypeFullName   = m_Components[r].ComponentTypeFullName,
                        ExcludedTypeFullNames = excluded
                    });
            }

            cfg.ExclusionRules = rules;
            EditorUtility.SetDirty(cfg);
            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("ExtractUIComponent", "互斥规则已保存。", "确定");
        }

        /// <summary>
        /// 返回组件的显示短名：取 ComponentTypeFullName 最后一段类名。
        /// 仅用于 UI 显示，不作为存储键。
        /// </summary>
        private static string ShortName(ComponentExportRule rule)
        {
            var full = rule.ComponentTypeFullName ?? string.Empty;
            var dot  = full.LastIndexOf('.');
            return dot >= 0 ? full.Substring(dot + 1) : full;
        }
    }
}
