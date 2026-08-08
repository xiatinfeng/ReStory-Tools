using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Restory.Data.Elements.Condition;
using Restory.Gameplay.Devices;
using Restory.Gameplay.Elements;
using Restory.Gameplay.Workplace;
using UnityEngine;

namespace ReStoryTools.Patches
{
    /// <summary>
    /// M3 元件自动拆卸（连锁拆 + 自动摆放）。
    ///
    /// 机制（反编译实证）：
    /// - 元件（InsertableElement）拆解入口 = InsertableElement.CompleteInteraction
    ///   （Progress=1 → base → OnDetached；只有"拆"触发它——元件安装是 AttachToDevice snap）。
    /// - 连锁拆的元件拆下后 socket 的 ResolveElementDetached 自动 workSurface.AddElement
    ///   （进工作台列表），但没人摆位置 → 留在原 socket 位置。
    /// - 自动摆放参考清灰机制（CleanedElementDestinationHandler）。
    /// - 损坏元件：连锁拆**跳过**（保留设备上），由面板"一键提取损坏件"按钮专门处理
    ///   （ExtractDamagedElements：全设备提取 → 移出 PlacedElements 免疫检查 → 角落暂存）。
    ///
    /// 安全关键（用户追问"角落会被工作台自动处理吗"）：
    /// - 唯一销毁点 = PlacedElementsHandler.ResolveElementsInvalidPosition（DisassembleGameMode
    ///   特定时机调用）：遍历 workSurface.PlacedElements，y &lt; elementAltitudeControlValue(1f)
    ///   的才处理损坏件（掉到表面下）。**摆稳 + 移出列表 = 不会被处理**。
    /// </summary>
    [HarmonyPatch(typeof(InsertableElement), "CompleteInteraction")]
    public static class AutoDismantlePatch
    {
        private static bool _inProgress;

        /// <summary>反射缓存：InsertableElement.CompleteInteraction（protected）——连锁拆直接强制完成</summary>
        private static readonly Action<InsertableElement> _forceComplete =
            (Action<InsertableElement>)Delegate.CreateDelegate(
                typeof(Action<InsertableElement>),
                typeof(InsertableElement).GetMethod("CompleteInteraction",
                    BindingFlags.Instance | BindingFlags.NonPublic));

        [HarmonyPrefix]
        private static void Prefix(InsertableElement __instance)
        {
            if (!Plugin.Instance.AutoDismantle) return;
            if (_inProgress) return;

            _inProgress = true;
            try
            {
                var device = __instance.GetComponentInParent<Device>();
                if (device == null) return;

                var chained = new List<InsertableElement>();
                foreach (var socket in device.ElementSockets)
                {
                    var nested = socket.NestedElement;
                    if (nested == null || nested == __instance) continue;
                    if (!(nested is InsertableElement elem)) continue; // 螺丝归 M2，不碰
                    if (elem.IsBlocked) continue;
                    // 损坏元件跳过（保留设备上）：由"一键提取损坏件"按钮统一处理
                    if (elem.ConditionHandler?.ElementData?.Condition is DamagedElementCondition) continue;

                    try
                    {
                        _forceComplete(elem); // 强制拆下（OnDetached → socket 释放 + workSurface.AddElement）
                        chained.Add(elem);
                    }
                    catch (Exception e)
                    {
                        Plugin.Instance.LogError($"[ReStoryTools] 连锁拆元件异常 {elem.name}：{e.Message}");
                    }
                }

                if (chained.Count > 0)
                {
                    PlaceOnWorkSurface(chained);
                }

                Plugin.Instance.LogInfo($"[ReStoryTools] 元件批量自动拆卸：连锁拆 {chained.Count} 个");
            }
            finally
            {
                _inProgress = false;
            }
        }

        /// <summary>把连锁拆下的元件自动摆到工作台角落空位（参考清灰自动摆放机制）</summary>
        private static void PlaceOnWorkSurface(List<InsertableElement> elements)
        {
            var placementController = Plugin.Instance.TryResolve<ElementPlacementController>();
            var workSurface = Plugin.Instance.TryResolve<WorkSurface>();
            if (placementController == null || workSurface == null)
            {
                Plugin.Instance.LogWarning("[ReStoryTools] 元件批量自动拆卸：找不到摆放服务，元件留在原位");
                return;
            }

            int placed = 0;
            foreach (var elem in elements)
            {
                try
                {
                    // 清灰同款：SetTargetElement 计算可用空位（CleanedElementPlacementPosition 为角落基准点）
                    placementController.SetTargetElement(elem);
                    var pos = workSurface.CleanedElementPlacementPosition;
                    if (!placementController.TryFindAvailablePlacementPosition(pos, out var available))
                    {
                        available = workSurface.DefaultPlacementPosition;
                    }

                    elem.transform.SetParent(workSurface.transform);
                    elem.transform.position = available;
                    elem.BehaviorSwitcher.SwitchToPlacedBehavior();
                    placed++;
                }
                catch (Exception e)
                {
                    Plugin.Instance.LogError($"[ReStoryTools] 元件摆放异常 {elem.name}：{e.Message}");
                }
            }

            Plugin.Instance.LogInfo($"[ReStoryTools] 元件批量自动拆卸：摆放 {placed} 个到工作台角落");
        }
    }
}
