using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CQEditorTools
{
    /// <summary>
    /// 面板4使用的节点导出标记符配置，支持自定义及一键重置为默认值。
    /// </summary>
    [System.Serializable]
    public class ExportMarkers
    {
        /// <summary>仅导出：检索此节点组件，不影响子节点继承默认值。默认 @</summary>
        public string Export = "@";
        /// <summary>仅跳过：不检索此节点组件，子节点继续遍历。默认 #</summary>
        public string Skip = "#";
        /// <summary>传播-导出：检索此节点，并将所有后代隐式默认改为导出。默认 =</summary>
        public string ExportPropagate = "=";
        /// <summary>传播-跳过：跳过此节点，并将所有后代隐式默认改为跳过。默认 -</summary>
        public string SkipPropagate = "-";

        /// <summary>将所有标记重置为内置默认值。</summary>
        public void ResetToDefault()
        {
            Export = "@";
            Skip = "#";
            ExportPropagate = "=";
            SkipPropagate = "-";
        }

        /// <summary>返回当前标记符是否与默认值完全一致。</summary>
        public bool IsDefault =>
            Export == "@" && Skip == "#" &&
            ExportPropagate == "=" && SkipPropagate == "-";

        /// <summary>
        /// 校验当前配置是否合法：
        /// 每个标记必须恰好为 1 个非空白 ASCII 字符，且四个标记两两不同。
        /// 返回 null 表示合法；否则返回第一条错误描述。
        /// </summary>
        public string Validate()
        {
            string[] markers = { Export, Skip, ExportPropagate, SkipPropagate };
            string[] labels = { "导出标记", "跳过标记", "传播-导出标记", "传播-跳过标记" };

            for (int i = 0; i < markers.Length; i++)
            {
                var m = markers[i];
                if (string.IsNullOrEmpty(m))
                    return $"{labels[i]} 不能为空";

                if (m.Length != 1)
                    return $"{labels[i]} 必须是单个字符，当前长度为 {m.Length}";

                char c = m[0];
                if (c > 127)
                    return $"{labels[i]} 必须是 ASCII 字符（0–127），当前字符 '{c}' 为全角或非 ASCII";

                if (char.IsWhiteSpace(c))
                    return $"{labels[i]} 不能是空白字符";
            }

            for (int i = 0; i < markers.Length; i++)
                for (int j = i + 1; j < markers.Length; j++)
                {
                    if (markers[i] == markers[j])
                        return $"{labels[i]} 与 {labels[j]} 值相同（'{markers[i]}'），四个标记必须两两不同";
                }

            return null;
        }
    }

    /// <summary>
    /// 单条组件互斥规则：当节点上存在 TriggerTypeFullName 组件时，
    /// 跳过 ExcludedTypeFullNames 中所有组件的检索（采用两阶段评估，与组件顺序无关）。
    /// </summary>
    [System.Serializable]
    public class ComponentExclusionRule
    {
        /// <summary>触发条件：节点上存在此组件类型时生效。</summary>
        public string TriggerTypeFullName;
        /// <summary>被跳过的组件类型全名列表。</summary>
        public List<string> ExcludedTypeFullNames = new List<string>();
    }

    /// <summary>检索规则窗口的布局持久化数据，由窗口自动读写，无需手动编辑。</summary>
    [System.Serializable]
    public class RulesWindowLayout
    {
        /// <summary>字段开头列的宽度，由用户拖拽分隔条后自动保存。</summary>
        public float PrefixColWidth = 120f;
    }

    /// <summary>
    /// 单条组件导出规则，定义某类组件是否参与检索及生成字段时的前缀。
    /// </summary>
    [System.Serializable]
    public class ComponentExportRule
    {
        /// <summary>是否在遍历 Prefab 时检索该组件类型。</summary>
        public bool IsEnabled;
        /// <summary>组件完整类型名（含命名空间），例：UnityEngine.UI.Button。</summary>
        public string ComponentTypeFullName;
        /// <summary>生成字段名时的前缀，最终字段名格式为 {前缀}{节点名}。</summary>
        public string FieldPrefix;
    }

    /// <summary>
    /// ExtractUIComponent 插件的项目级配置资产，存储 UIForm 路径规则、基类信息及组件导出规则。
    /// 通过 Window/界面工具/初始化 创建，保存于 Assets/Editor Default Resources/。
    /// </summary>
    [CreateAssetMenu(menuName = "CQEditorTools/ExtractUIComponentConfig")]
    public class ExtractUIComponentConfig : ScriptableObject
    {
        /// <summary>有效 UIForm Prefab 的根路径列表，右键菜单触发时用于校验选中对象是否在范围内。</summary>
        public List<string> ValidPrefabRootPaths;
        /// <summary>UIForm 体系的根基类完整限定名（命名空间.类名），用于反射查找 Prefab 上的 UIForm 组件。</summary>
        public string FormBaseTypeFullName;
        /// <summary>组件导出规则列表，控制哪些组件类型参与检索及其字段前缀。</summary>
        public List<ComponentExportRule> ComponentRules;
        /// <summary>
        /// 组件互斥规则列表：定义当某组件存在时需跳过哪些其他组件的检索。
        /// 在 Window/界面工具/互斥规则 中配置。
        /// </summary>
        public List<ComponentExclusionRule> ExclusionRules;
        /// <summary>检索规则窗口的布局信息，由窗口自动读写，用于还原用户上次的布局。</summary>
        public RulesWindowLayout RulesLayout = new RulesWindowLayout();
        /// <summary>面板4导出标记符配置，可自定义，支持重置为默认值。</summary>
        public ExportMarkers Markers = new ExportMarkers();

        private const string SaveFolder = "Assets/Editor Default Resources";
        private const string SavePath = SaveFolder + "/ExtractUIComponentConfig.asset";

        /// <summary>运行时缓存，避免每次重复查找 Asset。</summary>
        private static ExtractUIComponentConfig s_Cached;

        /// <summary>
        /// 获取当前项目的配置实例。优先返回缓存，其次扫描 AssetDatabase，找不到则返回 null。
        /// </summary>
        public static ExtractUIComponentConfig GetOrCreate()
        {
            if (s_Cached != null)
                return s_Cached;

            var guids = AssetDatabase.FindAssets("t:ExtractUIComponentConfig");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var cfg = AssetDatabase.LoadAssetAtPath<ExtractUIComponentConfig>(path);
                if (cfg != null)
                {
                    s_Cached = cfg;
                    return s_Cached;
                }
            }

            return null;
        }

        /// <summary>返回配置资产的目标保存路径。</summary>
        public static string ResolveSavePath() => SavePath;

        /// <summary>返回配置资产的目标保存目录。</summary>
        public static string ResolveSaveFolder() => SaveFolder;

        /// <summary>
        /// 清空缓存并删除项目中所有 ExtractUIComponentConfig 资产文件。
        /// </summary>
        public static void DeleteConfig()
        {
            s_Cached = null;
            var guids = AssetDatabase.FindAssets("t:ExtractUIComponentConfig");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                AssetDatabase.DeleteAsset(path);
            }
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// 返回内置的默认组件导出规则列表，涵盖常用 Unity UI 组件。
        /// 新建配置时自动填充，用户可在检索规则窗口中修改。
        /// </summary>
        public static List<ComponentExportRule> GetDefaultRules()
        {
            return new List<ComponentExportRule>
            {
                new ComponentExportRule { IsEnabled = true,  ComponentTypeFullName = "UnityEngine.RectTransform",    FieldPrefix = "RTrs"     },
                new ComponentExportRule { IsEnabled = true,  ComponentTypeFullName = "UnityEngine.UI.Image",         FieldPrefix = "Img"      },
                new ComponentExportRule { IsEnabled = true,  ComponentTypeFullName = "UnityEngine.UI.Button",        FieldPrefix = "Btn"      },
                new ComponentExportRule { IsEnabled = true,  ComponentTypeFullName = "UnityEngine.UI.Toggle",        FieldPrefix = "Tog"      },
                new ComponentExportRule { IsEnabled = true,  ComponentTypeFullName = "UnityEngine.UI.Slider",        FieldPrefix = "Sld"      },
                new ComponentExportRule { IsEnabled = true,  ComponentTypeFullName = "UnityEngine.UI.ScrollRect",    FieldPrefix = "Scroll"   },
                new ComponentExportRule { IsEnabled = true,  ComponentTypeFullName = "UnityEngine.UI.Scrollbar",     FieldPrefix = "Sbar"},
                new ComponentExportRule { IsEnabled = true,  ComponentTypeFullName = "TMPro.TextMeshProUGUI",        FieldPrefix = "Txt"      },
                new ComponentExportRule { IsEnabled = true,  ComponentTypeFullName = "TMPro.TMP_InputField",         FieldPrefix = "Input"    },
                new ComponentExportRule { IsEnabled = false, ComponentTypeFullName = "UnityEngine.UI.InputField",    FieldPrefix = "Input"    },
                new ComponentExportRule { IsEnabled = true,  ComponentTypeFullName = "UnityEngine.CanvasGroup",      FieldPrefix = "CG"       },
            };
        }
    }

    /// <summary>
    /// 插件内各表格窗口共享的行配色方案。
    /// 偶数行叠加半透明暗色，奇数行不改色，选中行用蓝色高亮。
    /// </summary>
    public static class ExtractUITableColors
    {
        /// <summary>偶数行叠加色（alpha 较低，适配亮色/暗色编辑器主题）。</summary>
        public static readonly Color RowEven = new Color(0f, 0f, 0f, 0.06f);
        /// <summary>奇数行不改色（完全透明）。</summary>
        public static readonly Color RowOdd = new Color(0f, 0f, 0f, 0f);
        /// <summary>选中行高亮色。</summary>
        public static readonly Color RowSelected = new Color(0.17f, 0.36f, 0.53f, 1f);
        /// <summary>列分隔条颜色。</summary>
        public static readonly Color Splitter = new Color(0.5f, 0.5f, 0.5f, 0.4f);
    }
}
