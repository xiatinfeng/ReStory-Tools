using System;
using System.Reflection;
using HarmonyLib;
using Restory.Data.Elements.Condition;
using Restory.Gameplay.Disassemble.StateMachine;
using Restory.Gameplay.Elements;
using Restory.Gameplay.Equipment.Ultrasonic;
using Restory.Gameplay.Workplace;
using UnityEngine;

namespace ReStoryTools.Patches
{
    /// <summary>
    /// v0.6.0：污染件拖拽聚集（用户方案，替代不可靠的自动投洗）。
    ///
    /// 机制（反编译实证）：
    /// - 自动投洗不可靠的根因：投洗前置链 = TryFitElementToSonicBath（需要 ActiveTool +
    ///   拖拽状态 isDraggingElementCanBeInsertedToSonicBath）→ ElementFitter.TryFitElement（fit 到
    ///   清洗机平面，即玩家看到的"白色半透明圈"）→ TryInsertElement（TryGetInsertedElementFitData
    ///   要求元件必须"已 fit 过"）。从工作台直接投（无 fit）机制上就失败。
    /// - 用户方案：玩家拖一个污染件 → 其他污染件跟随鼠标（吸附聚集）→ 拖到清洗机松手一起投洗。
    ///
    /// 实现：
    /// - Enter Postfix：拖的是污染件（DirtyElementCondition + InsertableElement）→ 收集工作台
    ///   其他污染件入 FollowerGroup + 记录原位置。
    /// - Plugin.Update：跟随件环绕主件（环形偏移）。
    /// - Exit Postfix：玩家松手/拖拽结束 → 若主件已投进清洗机（InsertedElements 含主件），跟随件
    ///   逐个 fit + insert（IsFull 自动停）；否则全部落回原位。
    /// </summary>
    [HarmonyPatch(typeof(DraggingDisassembleState))]
    public static class DirtyFollowPatch
    {
        /// <summary>反射缓存：UltrasonicService.private sonicBath 字段</summary>
        private static readonly FieldInfo _sonicBathField =
            typeof(UltrasonicService).GetField("sonicBath", BindingFlags.Instance | BindingFlags.NonPublic);

        private static SonicBath GetSonicBath()
        {
            try
            {
                var service = Plugin.Instance.TryResolve<UltrasonicService>();
                if (service == null || _sonicBathField == null) return null;
                return _sonicBathField.GetValue(service) as SonicBath;
            }
            catch
            {
                return null;
            }
        }

        [HarmonyPatch("Enter")]
        [HarmonyPostfix]
        private static void OnEnter(ElementBase selectedElement)
        {
            // 只处理：拖的是污染元件
            if (selectedElement == null || !(selectedElement is InsertableElement)) return;
            if (!(selectedElement.ConditionHandler?.ElementData?.Condition is DirtyElementCondition)) return;

            var workSurface = Plugin.Instance.TryResolve<WorkSurface>();
            if (workSurface == null) return;

            Plugin.DragMain = selectedElement;
            Plugin.FollowerGroup.Clear();
            Plugin.FollowerOrigins.Clear();

            foreach (var e in workSurface.PlacedElements)
            {
                if (e == null || e == selectedElement || e.IsDragging) continue;
                if (!(e is InsertableElement)) continue;
                if (!(e.ConditionHandler?.ElementData?.Condition is DirtyElementCondition)) continue;
                Plugin.FollowerGroup.Add(e);
                Plugin.FollowerOrigins[e] = e.transform.position;
            }

            if (Plugin.FollowerGroup.Count > 0)
            {
                Plugin.Instance.LogInfo($"[ReStoryTools] 污染件聚集：{Plugin.FollowerGroup.Count} 个跟随鼠标，拖到清洗机松手一起投洗");
            }
        }

        [HarmonyPatch("Exit")]
        [HarmonyPostfix]
        private static void OnExit()
        {
            if (Plugin.FollowerGroup.Count == 0)
            {
                Plugin.DragMain = null;
                return;
            }

            var bath = GetSonicBath();
            bool mainInserted = Plugin.DragMain != null && bath != null && bath.InsertedElements.ContainsKey(Plugin.DragMain);

            if (mainInserted)
            {
                // 主件已投洗 → 跟随件逐个 fit + insert（容量满自动停，投不进的落回）
                foreach (var follower in Plugin.FollowerGroup)
                {
                    if (follower == null) continue;
                    try
                    {
                        if (bath.IsFull)
                        {
                            Plugin.Instance.LogInfo($"[ReStoryTools] 污染件聚集：清洗机已满，{follower.name} 留在工作台");
                            Restore(follower);
                            continue;
                        }
                        // fit 到清洗机平面（用主件位置做锚点，fit 内部会 clamp 到平面范围）→ 投洗
                        if (bath.ElementFitter.TryFitElement(follower, Plugin.DragMain.transform.position)
                            && bath.TryInsertElement(follower))
                        {
                            continue;
                        }
                    }
                    catch (Exception e)
                    {
                        Plugin.Instance.LogError($"[ReStoryTools] 污染件投洗异常 {follower.name}：{e.Message}");
                    }
                    Restore(follower);
                }
                Plugin.Instance.LogInfo("[ReStoryTools] 污染件聚集：跟随件已随主件投入清洗机");
            }
            else
            {
                // 主件没投洗（放下/装回）→ 跟随件全部落回原位
                foreach (var follower in Plugin.FollowerGroup)
                {
                    if (follower != null) Restore(follower);
                }
            }

            Plugin.FollowerGroup.Clear();
            Plugin.FollowerOrigins.Clear();
            Plugin.DragMain = null;
        }

        private static void Restore(ElementBase e)
        {
            if (Plugin.FollowerOrigins.TryGetValue(e, out var pos))
            {
                e.transform.position = pos;
            }
        }
    }
}
