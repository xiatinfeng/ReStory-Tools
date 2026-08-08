using System;
using HarmonyLib;
using Restory.Gameplay.Soldering;

namespace ReStoryTools.Patches
{
    /// <summary>
    /// 一键电焊模式（Ctrl+E 开关）：开启后，任何元件进入焊接模式（InSolderingMode）
    /// 自动 ForceCompleteSoldering()——省去"移动电烙铁沿接触线"的操作。
    /// 只焊不清洁：烟灰清洁仍走原版（玩家手动）。
    /// 参考游戏官方作弊 DisassembleCheats.CompleteSoldering 的同款调用。
    /// </summary>
    [HarmonyPatch(typeof(SolderingService), "UpdateSolderingProcess")]
    public static class SolderPatch
    {
        private static bool _processing;

        [HarmonyPostfix]
        private static void Postfix(SolderingService __instance)
        {
            // 模式未开 / 已处理中 → 跳过
            if (!Plugin.Instance.SolderMode) return;
            if (_processing) return;
            // 不在焊接模式（比如还在清洁阶段）→ 不动
            if (!__instance.InSolderingMode) return;

            _processing = true;
            try
            {
                // 游戏原生"强制完成所有焊点"（官方作弊同款）
                __instance.ForceCompleteSoldering();
                Plugin.Instance.LogInfo("[ReStoryTools] 一键电焊：已自动完成焊接");
            }
            catch (Exception e)
            {
                Plugin.Instance.LogError($"[ReStoryTools] 一键电焊异常：{e.Message}");
            }
            finally
            {
                _processing = false;
            }
        }
    }
}