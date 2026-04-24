namespace CQEditorTools
{
    /// <summary>
    /// 面板4导出规则说明文档，使用占位符描述4个标记符：
    ///   {0} = Export（仅导出，默认 @）
    ///   {1} = Skip  （仅跳过，默认 #）
    ///   {2} = ExportPropagate（传播-导出，默认 =）
    ///   {3} = SkipPropagate （传播-跳过，默认 -）
    /// 调用 Format(markers) 生成带实际标记符的最终文本。
    /// </summary>
    public static class ExtractUIComponentRuleDoc
    {
        /// <summary>
        /// 规则说明模板，{0}{1}{2}{3} 依次对应
        /// Export / Skip / ExportPropagate / SkipPropagate。
        /// </summary>
        public const string Template =
            "━━ 标记符一览 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
            "\n" +
            "{2}  （传播-导出）\n" +
            "  导出此节点的组件；同时将所有后代节点的隐式默认标记设为\"导出\"。\n" +
            "  后代中自身已声明标记的节点不受影响。\n" +
            "\n" +
            "{3}  （传播-跳过）\n" +
            "  跳过此节点的组件；同时将所有后代节点的隐式默认标记设为\"跳过\"。\n" +
            "  后代中自身已声明标记的节点不受影响。\n" +
            "\n" +
            "{0}  （仅导出）\n" +
            "  导出此节点的组件，不影响子节点继承的默认标记。\n" +
            "\n" +
            "{1}  （仅跳过）\n" +
            "  跳过此节点的组件，不影响子节点继承的默认标记，子节点继续遍历。\n" +
            "\n" +
            "无标记\n" +
            "  使用最近祖先 {2}/{3} 传播的默认值；若无传播祖先，默认等同 {0}。\n" +
            "\n" +
            "━━ 优先级规则 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
            "\n" +
            "  节点自身声明的标记  >  最近祖先 {2}/{3} 传播的默认值  >  全局默认 {0}\n" +
            "\n" +
            "━━ 标记解析 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
            "\n" +
            "  取节点名第一个字符，匹配 {0} / {1} / {2} / {3} 其中之一。\n" +
            "  未命中则视为无标记，使用继承默认值。\n" +
            "\n" +
            "━━ 遍历规则 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
            "\n" +
            "  任何标记都不会中断子节点的遍历。\n" +
            "  {1} 和 {3} 只跳过当前节点的组件检索，子节点照常递归。\n" +
            "\n" +
            "━━ 示例 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
            "\n" +
            "  {2}Panel            → 导出自身；后代无标记时默认导出\n" +
            "    {1}Background     → 仅跳过自身；后代继续继承 {2}Panel 的默认\n" +
            "    {3}TempGroup      → 跳过自身；后代无标记时改为跳过\n" +
            "      {0}ConfirmBtn   → 自身声明 {0}，免疫 {3}TempGroup，强制导出\n" +
            "      CancelBtn       → 无标记，继承 {3}TempGroup → 跳过\n" +
            "    SubmitBtn         → 无标记，继承 {2}Panel → 导出\n" +
            "\n" +
            "━━ 自定义标记注意事项 ━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
            "\n" +
            "  • 标记不能为空，不能包含空白字符\n" +
            "  • 四个标记必须两两不同\n" +
            "  • 建议使用不会被中文输入法全角替换的字符（如 @ # = -）";

        /// <summary>用实际标记符替换占位符，生成最终说明文本。</summary>
        public static string Format(ExportMarkers m) =>
            Template
                .Replace("{0}", m.Export)
                .Replace("{1}", m.Skip)
                .Replace("{2}", m.ExportPropagate)
                .Replace("{3}", m.SkipPropagate);

        /// <summary>用默认标记符生成说明文本（不依赖 Config）。</summary>
        public static string FormatDefault()
        {
            var def = new ExportMarkers();
            return Format(def);
        }
    }
}
