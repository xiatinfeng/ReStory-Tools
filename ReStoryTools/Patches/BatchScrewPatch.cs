using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Restory.Data.Equipment;
using Restory.Gameplay.Devices;
using Restory.Gameplay.Elements;
using Restory.Gameplay.Equipment;

namespace ReStoryTools.Patches
{
    /// <summary>
    /// M2 v4：批量处理螺丝（同时拆 + 同时装）——电动螺丝刀专属。
    ///
    /// 需求修正（用户实测反馈）：
    /// - v2：装螺丝也被连锁拆（CompleteInteraction 是拆/装共用入口，未区分）。
    /// - v3：拆 OK，但连锁装失败。深挖玩家安装流程发现——游戏真实机制是
    ///   socket 缓存拆下的原件（LastNestedElement）+ 投影，玩家点击 socket 装回原件，
    ///   **不是从箱子拿螺丝拖拽**。v3 从 bin 找螺丝匹配是错误模型。
    /// - v4：连锁装 = 模拟玩家点击 socket 装回原件（socket.LastNestedElement）。
    ///   **电动螺丝刀专属**：非 AutoUnscrewing 工具的拧入需要玩家按住按钮推进进度，
    ///   直接 InitInteraction 会卡半拧（用户提示：秒拧只有顶级电动螺丝刀能做到）。
    ///
    /// 机制（反编译实证）：
    /// - ThreadedElement.CompleteInteraction = 拆/装共用：IsInstalling=false=拆（OnDetached），
    ///   true=装（OnInstalled）。Prefix 执行时 IsInstalling 是原值，可区分。
    /// - 玩家装螺丝流程：Detection 点击 socket → ShowSmallElementProjection（投影）→
    ///   按住 71 → projection.Activate → ElementSocket.ResolveProjectionActivated →
    ///   AttachElement(LastNestedElement) + Enter&lt;InstallingDisassembleState&gt; → 拧入。
    /// - 连锁装 = 对每个空 socket（IsAvailable + LastNestedElement 是螺丝）执行
    ///   AttachElement(原件) + InitInteraction(tool)（auto 秒拧，与玩家拧入同款）。
    /// - 连锁拆（v3 已验 OK）：Prefix 时螺丝仍在 socket，遍历当前层可拆螺丝逐个拆下，
    ///   拆下后统一 smallElementBin.PutElement 收纳（连锁拆的螺丝无状态机监听会悬浮）。
    /// </summary>
    [HarmonyPatch(typeof(ThreadedElement), "CompleteInteraction")]
    public static class BatchScrewPatch
    {
        /// <summary>批量连锁进行中标记（防递归）</summary>
        private static bool _batchInProgress;

        /// <summary>
        /// 缓存的 HideSmallElementProjection 委托（v5 修复：清理连锁装残留的高亮虚影）。
        /// socket 的这个方法会解绑 OnActivated + 销毁 smallElementProjection + 置 null；
        /// 玩家正常装走 ResolveProjectionActivated 会自动 Hide，但连锁装绕开了它 → 残留。
        /// </summary>
        private static readonly Action<ElementSocket> _hideSmallProjection =
            (Action<ElementSocket>)Delegate.CreateDelegate(
                typeof(Action<ElementSocket>),
                typeof(ElementSocket).GetMethod("HideSmallElementProjection",
                    BindingFlags.Instance | BindingFlags.NonPublic));

        [HarmonyPrefix]
        private static void Prefix(ThreadedElement __instance)
        {
            if (!Plugin.Instance.BatchScrew) return;
            if (_batchInProgress) return;

            _batchInProgress = true;
            try
            {
                if (__instance.IsInstalling)
                {
                    ChainInstall(__instance);
                }
                else
                {
                    ChainDismantle(__instance);
                }
            }
            finally
            {
                _batchInProgress = false;
            }
        }

        /// <summary>
        /// 当前工具是否为"可秒拆/秒拧"的电动螺丝刀。
        /// 批量功能专属电动螺丝刀：非 auto 工具需要玩家按住按钮推进进度，
        /// 连锁直接 InitInteraction 会卡半途 → 跳过（回归手动，不破坏游戏）。
        /// </summary>
        private static bool HasAutoScrewdriver()
        {
            var tool = Plugin.Instance.TryResolve<UnscrewingToolSelectionService>()?.CurrentlySelectedTool;
            return tool is UnscrewingToolInfo { AutoUnscrewing: true };
        }

        /// <summary>连锁拆：拆一颗 → 当前层其他可拆螺丝一起拆，拆下后统一收进小元件箱</summary>
        private static void ChainDismantle(ThreadedElement trigger)
        {
            if (!HasAutoScrewdriver())
            {
                Plugin.Instance.LogInfo("[ReStoryTools] 批量拆装：非电动螺丝刀，批量拆不生效（手动模式）");
                return;
            }

            var device = trigger.GetComponentInParent<Device>();
            if (device == null)
            {
                Plugin.Instance.LogWarning("[ReStoryTools] 批量拆装：未找到设备（螺丝不在设备层级内）");
                return;
            }

            var tool = Plugin.Instance.TryResolve<UnscrewingToolSelectionService>()?.CurrentlySelectedTool;
            var bin = Plugin.Instance.TryResolve<SmallElementBin>();

            var chained = new List<ThreadedElement>();
            int skippedBlocked = 0, skippedTool = 0;
            foreach (var socket in device.ElementSockets)
            {
                var nested = socket.NestedElement;
                if (nested == null || nested == trigger) continue;
                if (!(nested is ThreadedElement screw)) continue;
                if (screw.IsBlocked) { skippedBlocked++; continue; }
                if (!screw.CanInteraction(tool)) { skippedTool++; continue; }

                try
                {
                    screw.InitInteraction(tool); // auto 工具 → 同步拆下（OnDetached → socket 释放）
                    chained.Add(screw);
                }
                catch (Exception e)
                {
                    Plugin.Instance.LogError($"[ReStoryTools] 连锁拆螺丝异常 {screw.name}：{e.Message}");
                }
            }

            // 连锁拆的螺丝没有状态机监听 OnDetached → 悬浮；统一收纳进小元件箱（与游戏手动拆同款收纳）
            int stored = 0;
            if (bin != null)
            {
                foreach (var screw in chained)
                {
                    try
                    {
                        bin.PutElement(screw);
                        stored++;
                    }
                    catch (Exception e)
                    {
                        Plugin.Instance.LogError($"[ReStoryTools] 收纳螺丝异常 {screw.name}：{e.Message}");
                    }
                }
            }

            Plugin.Instance.LogInfo($"[ReStoryTools] 批量拆装：连锁拆 {chained.Count} 个，收纳 {stored} 个（被挡跳过 {skippedBlocked}，工具不适配 {skippedTool}）");
        }

        /// <summary>
        /// 连锁装：装一颗 → 其他空螺丝位自动装回"拆下来的原件"（socket.LastNestedElement）并拧紧。
        /// 模拟玩家点击 socket 的装回流程（ResolveProjectionActivated：AttachElement + Installing）。
        /// 螺丝来源是 socket 自己的 lastNestedElement（拆下的原件，Info 必兼容），不从箱子找。
        /// </summary>
        private static void ChainInstall(ThreadedElement trigger)
        {
            if (!HasAutoScrewdriver())
            {
                Plugin.Instance.LogInfo("[ReStoryTools] 批量拆装：非电动螺丝刀，批量装不生效（手动模式）");
                return;
            }

            var device = trigger.GetComponentInParent<Device>();
            if (device == null)
            {
                Plugin.Instance.LogWarning("[ReStoryTools] 批量拆装：未找到设备（螺丝不在设备层级内）");
                return;
            }

            var tool = Plugin.Instance.TryResolve<UnscrewingToolSelectionService>()?.CurrentlySelectedTool;

            int installed = 0, emptySlots = 0, noOriginal = 0, notScrew = 0;
            foreach (var socket in device.ElementSockets)
            {
                if (socket.NestedElement != null) continue;   // 已装（含玩家正在装的那颗）
                if (!socket.IsAvailable) continue;            // 被上层挡住 / 依赖未满足
                emptySlots++;

                // 拆下来的原件（AttachElement 会清空 LastNestedElement，必须先取引用）
                var original = socket.LastNestedElement;
                if (original == null) { noOriginal++; continue; }
                if (!(original is ThreadedElement screw)) { notScrew++; continue; }

                try
                {
                    // v5 修复：连锁装前先销毁 socket 上残留的 smallElementProjection（玩家悬停高亮虚影），
                    // 否则 AttachElement 绕开 ResolveProjectionActivated → 投影不销毁 → 残留在工作台。
                    _hideSmallProjection(socket);

                    // 与玩家点击 socket 装回同款：AttachElement（snap 放上，IsInstalling=true）
                    // → InitInteraction（auto 秒拧 → CompleteInteraction → OnInstalled）
                    socket.AttachElement(screw);
                    screw.InitInteraction(tool);
                    installed++;
                }
                catch (Exception e)
                {
                    Plugin.Instance.LogError($"[ReStoryTools] 连锁装螺丝异常 {screw.name}：{e.Message}");
                }
            }

            Plugin.Instance.LogInfo($"[ReStoryTools] 批量拆装：连锁装 {installed} 个（空位 {emptySlots}，无拆下原件 {noOriginal}，非螺丝位 {notScrew}）");
        }
    }
}
