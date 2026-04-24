using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CQEditorTools
{
    /// <summary>单个待导出的组件条目：所在节点路径、组件实例、对应的检索规则（含字段前缀）。</summary>
    public sealed class ExtractedComponentEntry
    {
        /// <summary>相对于 Prefab 根节点的路径，根节点本身为空串。</summary>
        public string NodePath;
        /// <summary>节点名（不含标记符前缀）。</summary>
        public string NodeName;
        /// <summary>待导出的组件实例。</summary>
        public Component Component;
        /// <summary>命中的检索规则（用于取 FieldPrefix）。</summary>
        public ComponentExportRule Rule;
        /// <summary>
        /// 是否被互斥规则命中。为 true 时在预览窗口中默认不勾选，但仍显示供用户手动启用。
        /// </summary>
        public bool IsExcludedByDefault;
    }

    /// <summary>
    /// 按照配置的标记规则遍历 UIForm 预制件层级，收集所有需要导出的组件。
    /// 遍历算法：
    ///   @ 仅导出此节点，不影响子节点继承默认值
    ///   # 仅跳过此节点，不影响子节点继承默认值，子节点继续遍历
    ///   = 导出此节点，同时将子节点继承默认值设为"导出"
    ///   - 跳过此节点，同时将子节点继承默认值设为"跳过"
    ///   无标记 使用最近祖先传播的继承默认值，初始全局默认为"导出"
    /// </summary>
    public static class ExtractUIComponentHierarchyCollector
    {
        private enum NodeMarker { Export, Skip, ExportPropagate, SkipPropagate, None }

        // ── 公共入口 ──────────────────────────────────────────────────────────
        /// <summary>
        /// 遍历 prefabRoot 的全部层级，返回按配置规则确定需要导出的组件条目列表。
        /// </summary>
        public static List<ExtractedComponentEntry> Collect(GameObject prefabRoot, ExtractUIComponentConfig cfg)
        {
            var results = new List<ExtractedComponentEntry>();

            var enabledRules = (cfg.ComponentRules ?? new List<ComponentExportRule>())
                .Where(r => r.IsEnabled && !string.IsNullOrWhiteSpace(r.ComponentTypeFullName))
                .ToList();

            if (enabledRules.Count == 0)
                return results;

            // 全局继承默认值：导出（= true）
            Traverse(
                node: prefabRoot.transform,
                root: prefabRoot.transform,
                isRoot: true,
                inheritedExport: true,
                markers: cfg.Markers ?? new ExportMarkers(),
                enabledRules: enabledRules,
                exclusionRules: cfg.ExclusionRules ?? new List<ComponentExclusionRule>(),
                results: results);

            return results;
        }

        // ── 递归遍历 ──────────────────────────────────────────────────────────
        private static void Traverse(
            Transform node,
            Transform root,
            bool isRoot,
            bool inheritedExport,
            ExportMarkers markers,
            List<ComponentExportRule> enabledRules,
            List<ComponentExclusionRule> exclusionRules,
            List<ExtractedComponentEntry> results)
        {
            var marker = ParseMarker(node.name, markers);
            var nodeName = StripMarker(node.name, markers);   // 去掉前缀后的纯名称

            // 确定此节点是否导出（根节点遵循标记规则，不再特殊处理）
            bool exportSelf = marker switch
            {
                NodeMarker.Export => true,
                NodeMarker.Skip => false,
                NodeMarker.ExportPropagate => true,
                NodeMarker.SkipPropagate => false,
                NodeMarker.None => inheritedExport
            };

            // 确定子节点继承默认值
            bool childInherited = marker switch
            {
                NodeMarker.ExportPropagate => true,
                NodeMarker.SkipPropagate => false,
                _ => inheritedExport
            };

            if (exportSelf && !isRoot)
            {
                var nodePath = GetRelativePath(root, node);
                CollectComponents(node, nodePath, nodeName, enabledRules, exclusionRules, results);
            }

            // 始终遍历子节点
            for (int i = 0; i < node.childCount; i++)
            {
                Traverse(
                    node: node.GetChild(i),
                    root: root,
                    isRoot: false,
                    inheritedExport: childInherited,
                    markers: markers,
                    enabledRules: enabledRules,
                    exclusionRules: exclusionRules,
                    results: results);
            }
        }

        // ── 组件收集（两阶段评估）─────────────────────────────────────────────
        /// <summary>
        /// 两阶段评估：
        ///   第一阶段：收集节点上所有命中启用检索规则的组件（按规则优先级排序）。
        ///   第二阶段：应用互斥规则，过滤掉应被跳过的组件。
        /// </summary>
        private static void CollectComponents(
            Transform node,
            string nodePath,
            string nodeName,
            List<ComponentExportRule> enabledRules,
            List<ComponentExclusionRule> exclusionRules,
            List<ExtractedComponentEntry> results)
        {
            // 第一阶段：匹配组件
            var candidates = new List<(Component comp, ComponentExportRule rule)>();
            foreach (var rule in enabledRules)
            {
                var type = ExtractUIComponentValidator.FindTypeInAllAssemblies(rule.ComponentTypeFullName);
                if (type == null)
                    continue;

                var comps = node.GetComponents(type);
                foreach (var comp in comps)
                {
                    if (comp != null)
                        candidates.Add((comp, rule));
                }
            }

            if (candidates.Count == 0)
                return;

            // 第二阶段：应用互斥规则
            // 先收集此节点上已命中的所有类型全名（用于互斥判断）
            var presentTypeNames = new HashSet<string>(
                candidates.Select(c => c.rule.ComponentTypeFullName));

            var excluded = new HashSet<string>();
            foreach (var excRule in exclusionRules)
            {
                if (!presentTypeNames.Contains(excRule.TriggerTypeFullName))
                    continue;
                if (excRule.ExcludedTypeFullNames == null)
                    continue;

                foreach (var exTypeName in excRule.ExcludedTypeFullNames)
                    excluded.Add(exTypeName);
            }

            foreach (var (comp, rule) in candidates)
            {
                results.Add(new ExtractedComponentEntry
                {
                    NodePath            = nodePath,
                    NodeName            = nodeName,
                    Component           = comp,
                    Rule                = rule,
                    IsExcludedByDefault = excluded.Contains(rule.ComponentTypeFullName)
                });
            }
        }

        // ── 标记解析 ──────────────────────────────────────────────────────────
        private static NodeMarker ParseMarker(string nodeName, ExportMarkers m)
        {
            if (string.IsNullOrEmpty(nodeName))
                return NodeMarker.None;

            var first = nodeName[0].ToString();
            if (first == m.ExportPropagate)
                return NodeMarker.ExportPropagate;
            if (first == m.SkipPropagate)
                return NodeMarker.SkipPropagate;
            if (first == m.Export)
                return NodeMarker.Export;
            if (first == m.Skip)
                return NodeMarker.Skip;
            return NodeMarker.None;
        }

        private static string StripMarker(string nodeName, ExportMarkers m)
        {
            if (string.IsNullOrEmpty(nodeName))
                return nodeName;

            var first = nodeName[0].ToString();
            if (first == m.ExportPropagate ||
                first == m.SkipPropagate ||
                first == m.Export ||
                first == m.Skip)
                return nodeName.Length > 1 ? nodeName.Substring(1) : string.Empty;

            return nodeName;
        }

        // ── 路径工具 ──────────────────────────────────────────────────────────
        private static string GetRelativePath(Transform root, Transform target)
        {
            if (target == root)
                return string.Empty;

            var parts = new Stack<string>();
            var cur = target;
            while (cur != null && cur != root)
            {
                parts.Push(cur.name);
                cur = cur.parent;
            }

            return string.Join("/", parts);
        }
    }
}
