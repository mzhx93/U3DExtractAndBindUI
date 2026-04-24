using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace CQEditorTools
{
    /// <summary>
    /// 组件检索规则编辑窗口，配置哪些组件类型参与检索及导出字段前缀。
    /// 列表顺序即优先级（从上到下由低到高），支持拖拽调整顺序；
    /// 保存后写回 ExtractUIComponentConfig 资产。
    /// </summary>
    public sealed class ExtractUIComponentSearchRuleWindow : EditorWindow
    {
        private List<ComponentExportRule> m_Rules = new();
        private ReorderableList m_List;
        private Vector2 m_Scroll;

        private const float ColEnabled    = 56f;
        private const float ColSplitWidth = 5f;
        private const float RowHeight     = 22f;
        private const float PrefixMinWidth = 60f;
        private const float PrefixMaxWidth = 300f;

        /// <summary>字段开头列宽，支持分隔条拖拽调整。</summary>
        private float m_PrefixColWidth = 120f;
        /// <summary>是否正在拖拽列分隔条。</summary>
        private bool  m_IsDraggingSplitter;

        // ── 入口 ──────────────────────────────────────────────────────────────
        /// <summary>打开组件检索规则窗口，从当前配置加载规则列表。</summary>
        public static void Open()
        {
            var win = GetWindow<ExtractUIComponentSearchRuleWindow>(false, "界面工具 - 检索规则", true);
            win.minSize = new Vector2(520f, 400f);
            win.LoadFromConfig();
            win.Show();
        }

        private void OnEnable()
        {
            // 脚本重载后恢复列表（已有数据则只重建 ReorderableList，否则从 Config 重新加载）
            if (m_Rules != null && m_Rules.Count > 0)
                BuildList();
            else
                LoadFromConfig();
        }

        // ── 数据加载 ──────────────────────────────────────────────────────────
        private void LoadFromConfig()
        {
            var cfg = ExtractUIComponentConfig.GetOrCreate();
            if (cfg == null)
                return;

            if (cfg.ComponentRules == null || cfg.ComponentRules.Count == 0)
                cfg.ComponentRules = ExtractUIComponentConfig.GetDefaultRules();

            m_Rules = new List<ComponentExportRule>();
            foreach (var r in cfg.ComponentRules)
                m_Rules.Add(new ComponentExportRule
                {
                    IsEnabled             = r.IsEnabled,
                    ComponentTypeFullName = r.ComponentTypeFullName,
                    FieldPrefix           = r.FieldPrefix
                });

            if (cfg.RulesLayout != null)
                m_PrefixColWidth = Mathf.Clamp(cfg.RulesLayout.PrefixColWidth, PrefixMinWidth, PrefixMaxWidth);

            BuildList();
        }

        // ── ReorderableList 构建 ──────────────────────────────────────────────
        private void BuildList()
        {
            m_List = new ReorderableList(m_Rules, typeof(ComponentExportRule),
                draggable: true, displayHeader: true,
                displayAddButton: true, displayRemoveButton: true);

            m_List.elementHeight = RowHeight + 2f;

            m_List.drawHeaderCallback         = DrawListHeader;
            m_List.drawElementCallback        = DrawListElement;
            m_List.drawElementBackgroundCallback = DrawElementBackground;

            m_List.onAddCallback = list =>
            {
                m_Rules.Add(new ComponentExportRule { IsEnabled = true });
                list.index = m_Rules.Count - 1;
                Repaint();
            };
            m_List.onRemoveCallback = list =>
            {
                if (list.index >= 0 && list.index < m_Rules.Count)
                    m_Rules.RemoveAt(list.index);
                list.index = Mathf.Clamp(list.index, 0, Mathf.Max(0, m_Rules.Count - 1));
                Repaint();
            };
        }

        // ── 列标题 ────────────────────────────────────────────────────────────
        private void DrawListHeader(Rect rect)
        {
            float prefixX  = rect.xMax - m_PrefixColWidth;
            float typeX    = rect.x + ColEnabled + 4f;
            float typeW    = prefixX - ColSplitWidth - 4f - typeX;

            EditorGUI.LabelField(new Rect(rect.x,    rect.y, ColEnabled, rect.height), "是否检索", EditorStyles.boldLabel);
            EditorGUI.LabelField(new Rect(typeX,     rect.y, typeW,      rect.height), "组件类（完整类名）", EditorStyles.boldLabel);
            EditorGUI.LabelField(new Rect(prefixX,   rect.y, m_PrefixColWidth, rect.height), "字段开头", EditorStyles.boldLabel);
        }

        // ── 行背景 ────────────────────────────────────────────────────────────
        private void DrawElementBackground(Rect rect, int index, bool isActive, bool isFocused)
        {
            if (Event.current.type != EventType.Repaint)
                return;

            var color = isActive
                ? ExtractUITableColors.RowSelected
                : (index % 2 == 0 ? ExtractUITableColors.RowEven : ExtractUITableColors.RowOdd);
            EditorGUI.DrawRect(rect, color);
        }

        // ── 行内容 ────────────────────────────────────────────────────────────
        private void DrawListElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            if (index < 0 || index >= m_Rules.Count)
                return;

            var rule   = m_Rules[index];
            float y    = rect.y + 1f;
            float h    = RowHeight;
            float prefixX   = rect.xMax - m_PrefixColWidth;
            float splitterX = prefixX - ColSplitWidth;
            float typeX     = rect.x + ColEnabled + 4f;
            float typeW     = splitterX - typeX;

            // 是否检索
            rule.IsEnabled = EditorGUI.Toggle(
                new Rect(rect.x + (ColEnabled - 16f) * 0.5f, y + (h - 16f) * 0.5f, 16f, 16f),
                rule.IsEnabled);

            // 组件完整类名
            rule.ComponentTypeFullName = EditorGUI.TextField(
                new Rect(typeX, y, typeW, h),
                rule.ComponentTypeFullName);

            // 分隔条
            var splitRect = new Rect(splitterX, rect.y, ColSplitWidth, rect.height);
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(splitRect, ExtractUITableColors.Splitter);
            EditorGUIUtility.AddCursorRect(splitRect, MouseCursor.ResizeHorizontal);
            if (Event.current.type == EventType.MouseDown && splitRect.Contains(Event.current.mousePosition))
            {
                m_IsDraggingSplitter = true;
                Event.current.Use();
            }

            // 字段前缀
            rule.FieldPrefix = EditorGUI.TextField(
                new Rect(prefixX, y, m_PrefixColWidth, h),
                rule.FieldPrefix);
        }

        // ── GUI 主流程 ────────────────────────────────────────────────────────
        private void OnGUI()
        {
            HandleSplitterDrag();

            if (m_List == null)
                BuildList();

            EditorGUILayout.HelpBox(
                "检索规则说明：\n" +
                "• 列表顺序即优先级，从上到下由低到高\n" +
                "• 优先级决定互斥规则的判定顺序：当同一节点同时命中多条互斥规则时，优先级高（靠下）的组件检索结果优先保留\n" +
                "• 拖拽行左侧手柄可调整顺序\n" +
                "• 保存后顺序立即写入配置，互斥规则面板中的组件顺序同步更新",
                MessageType.Info);

            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            m_List.DoLayoutList();
            EditorGUILayout.EndScrollView();

            DrawFooter();
        }

        // ── 分隔条拖拽 ────────────────────────────────────────────────────────
        private void HandleSplitterDrag()
        {
            if (!m_IsDraggingSplitter)
                return;

            if (Event.current.type == EventType.MouseDrag)
            {
                m_PrefixColWidth = Mathf.Clamp(
                    m_PrefixColWidth - Event.current.delta.x, PrefixMinWidth, PrefixMaxWidth);
                Event.current.Use();
                Repaint();
            }
            else if (Event.current.type == EventType.MouseUp)
            {
                m_IsDraggingSplitter = false;
                Event.current.Use();
                SaveLayoutToConfig();
            }
        }

        /// <summary>将当前列宽静默写回 Config。</summary>
        private void SaveLayoutToConfig()
        {
            var cfg = ExtractUIComponentConfig.GetOrCreate();
            if (cfg == null)
                return;

            if (cfg.RulesLayout == null)
                cfg.RulesLayout = new RulesWindowLayout();

            cfg.RulesLayout.PrefixColWidth = m_PrefixColWidth;
            EditorUtility.SetDirty(cfg);
            AssetDatabase.SaveAssets();
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

        /// <summary>
        /// 将当前列表保存回 Config：剔除空行，校验类型，存在无效类型时弹窗询问。
        /// </summary>
        private void SaveToConfig()
        {
            var cfg = ExtractUIComponentConfig.GetOrCreate();
            if (cfg == null)
            {
                EditorUtility.DisplayDialog("提示", "未找到配置文件，请先初始化配置。", "确定");
                return;
            }

            var validRules = m_Rules.FindAll(r => !string.IsNullOrWhiteSpace(r.ComponentTypeFullName));

            var invalidNames = new List<string>();
            foreach (var rule in validRules)
            {
                if (FindTypeInAllAssemblies(rule.ComponentTypeFullName) == null)
                    invalidNames.Add(rule.ComponentTypeFullName);
            }

            if (invalidNames.Count > 0)
            {
                var joined = string.Join("\n  · ", invalidNames);
                var confirm = EditorUtility.DisplayDialog(
                    "存在无效组件类型",
                    $"以下组件类型无法识别，是否剔除后保存？\n\n  · {joined}",
                    "剔除并保存",
                    "取消");

                if (!confirm)
                    return;

                validRules = validRules.FindAll(r => !invalidNames.Contains(r.ComponentTypeFullName));
            }

            m_Rules = validRules;
            BuildList();

            cfg.ComponentRules = new List<ComponentExportRule>(m_Rules);
            EditorUtility.SetDirty(cfg);
            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("ExtractUIComponent", "组件规则已保存。", "确定");
        }

        /// <summary>在所有已加载程序集中按完整类型名查找类型，优先搜索 Assembly-CSharp。</summary>
        private static System.Type FindTypeInAllAssemblies(string typeName)
        {
            var mainAsm = System.AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
            var type = mainAsm?.GetTypes().FirstOrDefault(t => t.FullName == typeName);
            if (type != null)
                return type;

            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                type = asm.GetType(typeName);
                if (type != null)
                    return type;
            }
            return null;
        }
    }
}
