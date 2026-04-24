using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CQEditorTools
{
    /// <summary>
    /// 遍历结果预览窗口：树形结构，一行一节点，组件勾选框内联显示。
    /// 格式：[缩进] 节点名: [✓]组件类型  [✓]组件类型  ...
    /// </summary>
    public sealed class ExtractUIComponentPreviewWindow : EditorWindow
    {
        // ── 数据模型 ──────────────────────────────────────────────────────────
        private GameObject m_PrefabAsset;
        private Component m_FormComponent;

        private sealed class ComponentItem
        {
            public ExtractedComponentEntry Entry;
            public bool IsSelected;
            public string FieldName;
        }

        /// <summary>一个节点对应一行，含层级深度与该节点上的组件列表。</summary>
        private sealed class NodeRow
        {
            public string NodeName;
            public int Depth;
            public List<ComponentItem> Items = new();
        }

        private List<NodeRow> m_Rows = new();

        // ── 布局常量 ──────────────────────────────────────────────────────────
        private const float IndentWidth = 14f;
        private const float NodeLabelWidth = 130f;
        private const float ToggleWidth = 18f;
        private const float TypeLabelWidth = 130f;
        private const float RowH = 22f;
        private const float ComponentGap = 6f;

        // ── 滚动 ──────────────────────────────────────────────────────────────
        private Vector2 m_Scroll;

        // ── 样式（首次 OnGUI 时创建）────────────────────────────────────────
        private GUIStyle m_NodeLabelStyle;
        private GUIStyle m_TypeLabelStyle;

        // ── 入口 ──────────────────────────────────────────────────────────────
        public static void Open(
            GameObject prefabAsset,
            Component formComponent,
            List<ExtractedComponentEntry> entries)
        {
            var win = GetWindow<ExtractUIComponentPreviewWindow>(
                true, "更新UI组件字段 - 预览", true);
            win.minSize = new Vector2(580f, 480f);
            win.m_PrefabAsset = prefabAsset;
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
                    Entry = entry,
                    IsSelected = !entry.IsExcludedByDefault,
                    FieldName = BuildFieldName(entry)
                });
            }
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
        }

        // ── OnGUI ─────────────────────────────────────────────────────────────
        private void OnGUI()
        {
            EnsureStyles();
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

        /// <summary>
        /// 一行格式：[缩进] 节点名:  [toggle]类型名  [toggle]类型名 ...
        /// </summary>
        private void DrawNodeRow(NodeRow row, int rowIndex)
        {
            var rowRect = EditorGUILayout.BeginHorizontal(GUILayout.Height(RowH));

            // 交替行背景
            if (Event.current.type == EventType.Repaint)
            {
                var bg = rowIndex % 2 == 0
                    ? ExtractUITableColors.RowEven
                    : ExtractUITableColors.RowOdd;
                EditorGUI.DrawRect(rowRect, bg);
            }

            // 层级缩进
            GUILayout.Space(row.Depth * IndentWidth);

            // 节点名标签（含冒号，固定宽度）
            EditorGUILayout.LabelField(row.NodeName + ":", m_NodeLabelStyle,
                GUILayout.Width(NodeLabelWidth));

            // 内联组件勾选框列表
            foreach (var item in row.Items)
            {
                item.IsSelected = EditorGUILayout.Toggle(
                    item.IsSelected, GUILayout.Width(ToggleWidth));

                // tooltip 显示完整字段名
                var content = new GUIContent(
                    item.Entry.Component.GetType().Name,
                    $"字段名：{item.FieldName}");

                using (new EditorGUI.DisabledScope(!item.IsSelected))
                    EditorGUILayout.LabelField(content, m_TypeLabelStyle,
                        GUILayout.Width(TypeLabelWidth));

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

        // ── 导出（Step 3：生成 Components.cs）───────────────────────────────
        private void ExecuteExport()
        {
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
                            FieldName = item.FieldName,
                            ComponentType = item.Entry.Component.GetType(),
                            NodePath = item.Entry.NodePath,
                            AssemblyQualifiedTypeName = item.Entry.Component.GetType().AssemblyQualifiedName
                        });
            return list;
        }

        // ── 工具方法 ──────────────────────────────────────────────────────────
        private int CountTotal()
        {
            int n = 0;
            foreach (var r in m_Rows)
                n += r.Items.Count;
            return n;
        }

        private int CountSelected()
        {
            int n = 0;
            foreach (var r in m_Rows)
                foreach (var i in r.Items)
                    if (i.IsSelected)
                        n++;
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
