using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CQEditorTools
{
    /// <summary>
    /// 遍历结果预览窗口：树形结构，一行一节点，组件勾选框内联显示。
    /// 格式：[缩进] 节点名: [✓]ComponentType (FieldName)  ...
    /// 勾选列表中若存在相同字段名，以红色色块高亮，并在导出时拦截提示。
    /// </summary>
    public sealed class ExtractUIComponentPreviewWindow : EditorWindow
    {
        // ── 数据模型 ──────────────────────────────────────────────────────────
        private GameObject m_PrefabAsset;
        private Component  m_FormComponent;

        private sealed class ComponentItem
        {
            public ExtractedComponentEntry Entry;
            public bool   IsSelected;
            public string FieldName;
        }

        private sealed class NodeRow
        {
            public string            NodeName;
            public int               Depth;
            public List<ComponentItem> Items = new();
        }

        private List<NodeRow>      m_Rows              = new();
        /// <summary>当前勾选范围内存在重复的字段名集合，每帧刷新。</summary>
        private HashSet<string>    m_DuplicateFieldNames = new();

        // ── 布局常量 ──────────────────────────────────────────────────────────
        private const float IndentWidth    = 14f;
        private const float NodeLabelWidth = 130f;
        private const float ToggleWidth    = 18f;
        private const float TypeLabelWidth = 180f;   // 增大以容纳 "Type (FieldName)"
        private const float RowH           = 22f;
        private const float ComponentGap   = 6f;

        private static readonly Color s_DuplicateHighlight = new Color(1f, 0.25f, 0.2f, 0.35f);

        // ── 滚动 ──────────────────────────────────────────────────────────────
        private Vector2 m_Scroll;

        // ── 样式 ──────────────────────────────────────────────────────────────
        private GUIStyle m_NodeLabelStyle;
        private GUIStyle m_TypeLabelStyle;
        private GUIStyle m_DupWarnStyle;

        // ── 入口 ──────────────────────────────────────────────────────────────
        public static void Open(
            GameObject                    prefabAsset,
            Component                     formComponent,
            List<ExtractedComponentEntry> entries)
        {
            var win = GetWindow<ExtractUIComponentPreviewWindow>(
                true, "更新UI组件字段 - 预览", true);
            win.minSize       = new Vector2(620f, 480f);
            win.m_PrefabAsset   = prefabAsset;
            win.m_FormComponent = formComponent;
            win.BuildRows(entries);
            win.Show();
        }

        // ── 数据构建 ──────────────────────────────────────────────────────────
        private void BuildRows(List<ExtractedComponentEntry> entries)
        {
            m_Rows.Clear();
            var pathToRow = new Dictionary<string, NodeRow>();

            foreach (var entry in entries)
            {
                if (!pathToRow.TryGetValue(entry.NodePath, out var row))
                {
                    int depth = string.IsNullOrEmpty(entry.NodePath)
                        ? 0
                        : entry.NodePath.Split('/').Length;

                    row = new NodeRow { NodeName = entry.NodeName, Depth = depth };
                    m_Rows.Add(row);
                    pathToRow[entry.NodePath] = row;
                }

                row.Items.Add(new ComponentItem
                {
                    Entry      = entry,
                    IsSelected = !entry.IsExcludedByDefault,
                    FieldName  = BuildFieldName(entry)
                });
            }
        }

        /// <summary>
        /// 扫描所有已勾选条目，将出现超过 1 次的字段名写入 m_DuplicateFieldNames。
        /// 未勾选的条目不参与计数（不会被写入代码，无实际冲突）。
        /// 每帧在 OnGUI 顶部调用，保证勾选状态变化后立即反映。
        /// </summary>
        private void RefreshDuplicates()
        {
            var counter = new Dictionary<string, int>();
            foreach (var row in m_Rows)
                foreach (var item in row.Items)
                    if (item.IsSelected)
                    {
                        counter.TryGetValue(item.FieldName, out var c);
                        counter[item.FieldName] = c + 1;
                    }

            m_DuplicateFieldNames.Clear();
            foreach (var kv in counter)
                if (kv.Value > 1)
                    m_DuplicateFieldNames.Add(kv.Key);
        }

        // ── 样式初始化 ────────────────────────────────────────────────────────
        private void EnsureStyles()
        {
            if (m_NodeLabelStyle != null)
                return;

            m_NodeLabelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleLeft
            };
            m_TypeLabelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft
            };
            m_DupWarnStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight,
                normal    = { textColor = new Color(1f, 0.4f, 0.3f) }
            };
        }

        // ── OnGUI ─────────────────────────────────────────────────────────────
        private void OnGUI()
        {
            EnsureStyles();
            RefreshDuplicates();   // 每帧刷新重复集合
            DrawInfoBar();
            DrawSeparator();
            DrawTreeArea();
            DrawSeparator();
            DrawFooter();
        }

        // ── 顶部信息栏 ────────────────────────────────────────────────────────
        private void DrawInfoBar()
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("预制件：", GUILayout.Width(50f));
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField(m_PrefabAsset, typeof(GameObject), false);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("UIForm：", GUILayout.Width(50f));
            EditorGUILayout.LabelField(
                m_FormComponent != null ? m_FormComponent.GetType().Name : "-",
                EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            // 重复字段警告
            if (m_DuplicateFieldNames.Count > 0)
                EditorGUILayout.LabelField(
                    $"⚠ {m_DuplicateFieldNames.Count} 个字段名重复",
                    m_DupWarnStyle, GUILayout.Width(160f));

            EditorGUILayout.LabelField(
                $"节点：{m_Rows.Count}    组件：{CountSelected()} / {CountTotal()}",
                EditorStyles.miniLabel, GUILayout.Width(180f));
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4f);
        }

        // ── 树形滚动区域 ──────────────────────────────────────────────────────
        private void DrawTreeArea()
        {
            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll, GUILayout.ExpandHeight(true));

            for (int i = 0; i < m_Rows.Count; i++)
                DrawNodeRow(m_Rows[i], i);

            EditorGUILayout.EndScrollView();
        }

        private void DrawNodeRow(NodeRow row, int rowIndex)
        {
            var rowRect = EditorGUILayout.BeginHorizontal(GUILayout.Height(RowH));

            if (Event.current.type == EventType.Repaint)
            {
                var bg = rowIndex % 2 == 0
                    ? ExtractUITableColors.RowEven
                    : ExtractUITableColors.RowOdd;
                EditorGUI.DrawRect(rowRect, bg);
            }

            GUILayout.Space(row.Depth * IndentWidth);

            EditorGUILayout.LabelField(row.NodeName + ":", m_NodeLabelStyle,
                GUILayout.Width(NodeLabelWidth));

            foreach (var item in row.Items)
            {
                bool prevSelected = item.IsSelected;
                item.IsSelected = EditorGUILayout.Toggle(item.IsSelected, GUILayout.Width(ToggleWidth));

                bool isDup  = item.IsSelected && m_DuplicateFieldNames.Contains(item.FieldName);
                var  label  = $"{item.Entry.Component.GetType().Name} ({item.FieldName})";
                var  tip    = isDup
                    ? $"⚠ 字段名重复：{item.FieldName}"
                    : $"字段名：{item.FieldName}";
                var  content = new GUIContent(label, tip);

                using (new EditorGUI.DisabledScope(!item.IsSelected))
                {
                    var itemRect = GUILayoutUtility.GetRect(
                        content, m_TypeLabelStyle, GUILayout.Width(TypeLabelWidth));

                    if (isDup && Event.current.type == EventType.Repaint)
                        EditorGUI.DrawRect(itemRect, s_DuplicateHighlight);

                    GUI.Label(itemRect, content, m_TypeLabelStyle);
                }

                GUILayout.Space(ComponentGap);
            }

            EditorGUILayout.EndHorizontal();
        }

        // ── 底部按钮栏 ────────────────────────────────────────────────────────
        private void DrawFooter()
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("全选", GUILayout.Width(60f)))
                SetAll(true);
            if (GUILayout.Button("全取消", GUILayout.Width(60f)))
                SetAll(false);
            if (GUILayout.Button("刷新", GUILayout.Width(60f)))
                RefreshFromPrefab();

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("取消", GUILayout.Width(80f)))
                Close();

            GUI.enabled = CountSelected() > 0;
            if (GUILayout.Button("导出", GUILayout.Width(80f)))
                ExecuteExport();
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4f);
        }

        /// <summary>重新遍历预制件层级，用最新结果覆盖当前行列表。</summary>
        private void RefreshFromPrefab()
        {
            var cfg = ExtractUIComponentConfig.GetOrCreate();
            if (cfg == null || m_PrefabAsset == null)
            {
                EditorUtility.DisplayDialog("提示", "配置或预制件丢失，无法刷新。", "确定");
                return;
            }

            var entries = ExtractUIComponentHierarchyCollector.Collect(m_PrefabAsset, cfg);
            if (entries.Count == 0)
            {
                EditorUtility.DisplayDialog("提示",
                    $"{m_PrefabAsset.name}：刷新后未收集到任何需要导出的组件。\n" +
                    "请确认标记符和检索规则已正确配置。", "确定");
                return;
            }

            BuildRows(entries);
            Repaint();
        }

        // ── 导出（含重复字段拦截）────────────────────────────────────────────
        private void ExecuteExport()
        {
            // 导出前再刷新一次，确保拦截状态最新
            RefreshDuplicates();

            if (m_DuplicateFieldNames.Count > 0)
            {
                var list = string.Join("\n  • ", m_DuplicateFieldNames);
                EditorUtility.DisplayDialog(
                    "存在重复字段名",
                    "以下字段名在已勾选的组件中重复出现，导出已中断。\n" +
                    "请修改 Hierarchy 中对应节点名称后重试：\n\n" +
                    $"  • {list}",
                    "确定");
                return;
            }

            var fields = BuildFieldEntries();

            if (ExtractUIComponentCodeWriter.Execute(m_FormComponent, fields, out var error))
            {
                EditorUtility.DisplayDialog(
                    "ExtractUIComponent",
                    $"成功写入 {fields.Count} 个字段到 {m_FormComponent.GetType().Name}.Components.cs。\n" +
                    "脚本编译完成后将自动完成组件绑定。",
                    "确定");
                Close();
            }
            else
            {
                EditorUtility.DisplayDialog("写入失败", error, "确定");
            }
        }

        private List<ComponentFieldEntry> BuildFieldEntries()
        {
            var list = new List<ComponentFieldEntry>();
            foreach (var row in m_Rows)
                foreach (var item in row.Items)
                    if (item.IsSelected)
                        list.Add(new ComponentFieldEntry
                        {
                            FieldName                 = item.FieldName,
                            ComponentType             = item.Entry.Component.GetType(),
                            NodePath                  = item.Entry.NodePath,
                            AssemblyQualifiedTypeName = item.Entry.Component.GetType().AssemblyQualifiedName
                        });
            return list;
        }

        // ── 工具方法 ──────────────────────────────────────────────────────────
        private int CountTotal()
        {
            int n = 0;
            foreach (var r in m_Rows) n += r.Items.Count;
            return n;
        }

        private int CountSelected()
        {
            int n = 0;
            foreach (var r in m_Rows)
                foreach (var i in r.Items)
                    if (i.IsSelected) n++;
            return n;
        }

        private void SetAll(bool value)
        {
            foreach (var r in m_Rows)
                foreach (var i in r.Items)
                    i.IsSelected = value;
            Repaint();
        }

        /// <summary>字段名：{FieldPrefix}{NodeName首字母大写，过滤非法字符}。</summary>
        private static string BuildFieldName(ExtractedComponentEntry e)
        {
            var name = e.NodeName ?? string.Empty;
            if (name.Length > 0)
                name = char.ToUpper(name[0]) + name.Substring(1);

            var sb = new StringBuilder(e.Rule.FieldPrefix);
            foreach (var ch in name)
                if (char.IsLetterOrDigit(ch) || ch == '_')
                    sb.Append(ch);

            return sb.ToString();
        }

        private static void DrawSeparator()
        {
            var rect = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.35f));
        }
    }
}
