using System.ComponentModel;
using BepInEx.Logging;

namespace ReStoryTools
{
    /// <summary>
    /// SRDebugger 面板：三个便捷功能的开关/动作按钮。
    /// 仿游戏自带 SRDebugCheatBase 的注册方式（[Category]/[DisplayName] 特性驱动面板组织）。
    /// M0：空实现占位，只验证面板能出现在 SRDebugger 里。
    /// </summary>
    public class QoLToolsPanel
    {
        private readonly ManualLogSource _log;

        public QoLToolsPanel(ManualLogSource log)
        {
            _log = log;
        }

        [Category("QoL Tools")]
        [DisplayName("批量拆螺丝（开关）")]
        public void ToggleBatchUnscrew()
        {
            _log.LogInfo("[ReStoryTools] 批量拆螺丝 切换（M2 实现）");
        }

        [Category("QoL Tools")]
        [DisplayName("元件自动拆卸（开关）")]
        public void ToggleAutoDismantle()
        {
            _log.LogInfo("[ReStoryTools] 元件自动拆卸 切换（M3 实现）");
        }

        [Category("QoL Tools")]
        [DisplayName("超声波一键收料")]
        public void CollectAllFromSonicBath()
        {
            _log.LogInfo("[ReStoryTools] 超声波一键收料（M1 实现）");
        }
    }
}
