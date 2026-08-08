using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace ReStoryTools
{
    /// <summary>
    /// 多语言（v0.7.0）：面板 UI 中/英自动切换。
    /// 语言判定优先级：插件目录 lang.txt 强制覆盖（内容 "en" 或 "zh"）> Unity 系统语言。
    /// 日志保留中文（开发调试用，不影响英文用户）。
    /// </summary>
    public static class L
    {
        public enum Lang { Zh, En }

        public static Lang Current { get; private set; } = Lang.Zh;

        private static readonly Dictionary<string, (string zh, string en)> _table =
            new Dictionary<string, (string, string)>
            {
                ["panel_title"] = ("ReStory QoL Tools", "ReStory QoL Tools"),
                ["panel_hint"] = ("便捷工具（F9 关闭）", "QoL Tools (F9 to close)"),
                ["toggle_screw"] = ("批量拆装螺丝（拆一颗连拆 / 装一颗连装）",
                                    "Batch Screws (chain dismantle / chain install)"),
                ["toggle_element"] = ("元件批量自动拆卸", "Auto-Dismantle Elements"),
                ["toggle_solder"] = ("一键电焊（焊接自动完成） [Ctrl+E]", "Auto-Solder (complete soldering instantly) [Ctrl+E]"),
                ["btn_assemble"] = ("元件一键拼合（工作台当前层） [R]",
                                    "Assemble Elements (workbench) [R]"),
                ["btn_collect"] = ("超声波一键收料 [T]", "Collect from Sonic Bath [T]"),
                ["btn_collect_workbench"] = ("工作台收料进库存 [E]", "Collect workbench parts to storage [E]"),
                ["state_line"] = ("开关：批量拆装={0} 元件拆装={1} 电焊={2}",
                                  "Toggles: BatchScrew={0} ElementDism={1} Solder={2}"),
                ["hotkeys_line"] = ("快捷键：R=拼合 T=收料 E=台面收料 Ctrl+R/T=开关 Ctrl+E=电焊",
                                    "Hotkeys: R=Assemble T=Collect E=BenchCollect Ctrl+R/T=Toggle Ctrl+E=Solder"),
            };

        static L()
        {
            try
            {
                // 1) lang.txt 强制覆盖（插件 dll 同目录）
                var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                var file = Path.Combine(dir ?? "", "lang.txt");
                if (File.Exists(file))
                {
                    var text = File.ReadAllText(file).Trim().ToLowerInvariant();
                    if (text.StartsWith("en")) { Current = Lang.En; return; }
                    if (text.StartsWith("zh")) { Current = Lang.Zh; return; }
                }

                // 2) 系统语言
                switch (Application.systemLanguage)
                {
                    case SystemLanguage.ChineseSimplified:
                    case SystemLanguage.ChineseTraditional:
                    case SystemLanguage.Chinese:
                        Current = Lang.Zh;
                        break;
                    default:
                        Current = Lang.En;
                        break;
                }
            }
            catch
            {
                Current = Lang.Zh;
            }
        }

        public static string T(string key, params object[] args)
        {
            if (_table.TryGetValue(key, out var pair))
            {
                var text = Current == Lang.Zh ? pair.zh : pair.en;
                return args.Length > 0 ? string.Format(text, args) : text;
            }
            return key;
        }
    }
}
