using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using HarmonyLib;
using Restory.Data.Elements.Condition;
using Restory.Gameplay.Devices;
using Restory.Gameplay.Elements;
using Restory.Gameplay.Equipment.Ultrasonic;
using Restory.Gameplay.Inventory;
using Restory.Gameplay.Workplace;
using Restory.StorageSystem.StorageElements;
using UnityEngine;

namespace ReStoryTools
{
    /// <summary>
    /// ReStory QoL Tools — BepInEx 5.4.23.5 插件。
    /// M1：超声波一键收料（全部调用游戏现有 API，零游戏逻辑改动）。
    ///    UltrasonicService.TryRetrieveAllElements（现成全取）→ ElementService.TrySendItemToStorage（现成入库）
    /// 面板：F9 切换显示；按钮触发收料（队列到 Update 执行，避免 OnGUI 内做场景操作）。
    /// </summary>
    [BepInPlugin("com.restorytools.qol", "ReStory QoL Tools", "0.8.2")]
    public class Plugin : BaseUnityPlugin
    {
        private static Plugin _instance;
        public static Plugin Instance => _instance;

        /// <summary>污染件拖拽聚集（v0.6.0）：玩家拖一个污染件，其他污染件跟随鼠标</summary>
        public static readonly List<ElementBase> FollowerGroup = new List<ElementBase>();
        public static readonly Dictionary<ElementBase, Vector3> FollowerOrigins = new Dictionary<ElementBase, Vector3>();
        public static ElementBase DragMain;

        private bool _panelRegistered;
        private bool _showPanel;
        private bool _batchScrew;
        private bool _autoDismantle;
        private bool _solderMode;
        private bool _collectRequested;
        private bool _installAllRequested;
        private bool _collectWorkbenchRequested;
        private Rect _windowRect = new Rect(60, 60, 340, 290);

        private void Awake()
        {
            _instance = this;
            // 注册 Harmony 补丁（M2 批量拆螺丝等）
            try
            {
                var harmony = new Harmony("com.restorytools.qol");
                harmony.PatchAll();
                Logger.LogInfo("[ReStoryTools] Harmony 补丁已注册");
            }
            catch (Exception e)
            {
                Logger.LogError($"[ReStoryTools] Harmony 注册失败：{e.Message}");
            }
            Logger.LogInfo("[ReStoryTools] 插件加载（v0.3.0 M2），等待 SRDebugger 初始化...");
        }

        private void Update()
        {
            // 污染件拖拽聚集：跟随主件（环形偏移），玩家拖到清洗机松手时一起投洗（见 DirtyFollowPatch）
            if (DragMain != null && FollowerGroup.Count > 0)
            {
                var mainPos = DragMain.transform.position;
                int count = FollowerGroup.Count;
                for (int i = 0; i < count; i++)
                {
                    var follower = FollowerGroup[i];
                    if (follower == null) continue;
                    float angle = i * (360f / count) * Mathf.Deg2Rad;
                    var offset = new Vector3(Mathf.Cos(angle) * 0.45f, 0.15f, Mathf.Sin(angle) * 0.45f);
                    follower.transform.position = mainPos + offset;
                }
            }

            // 快捷键（v0.5.0）：R=元件一键拼合、T=超声波一键收料、Ctrl+R=批量拆装螺丝、Ctrl+T=元件批量自动拆卸
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (Input.GetKeyDown(KeyCode.R))
            {
                if (ctrl)
                {
                    _batchScrew = !_batchScrew;
                    Logger.LogInfo($"[ReStoryTools] 批量拆装螺丝 {( _batchScrew ? "开启" : "关闭" )}（Ctrl+R）");
                }
                else
                {
                    _installAllRequested = true;
                    Logger.LogInfo("[ReStoryTools] 元件一键拼合（R）");
                }
            }
            else if (Input.GetKeyDown(KeyCode.T))
            {
                if (ctrl)
                {
                    _autoDismantle = !_autoDismantle;
                    Logger.LogInfo($"[ReStoryTools] 元件批量自动拆卸 {(_autoDismantle ? "开启" : "关闭")}（Ctrl+T）");
                }
                else
                {
                    _collectRequested = true;
                    Logger.LogInfo("[ReStoryTools] 超声波一键收料（T）");
                }
            }
            else if (Input.GetKeyDown(KeyCode.E))
            {
                if (ctrl)
                {
                    _solderMode = !_solderMode;
                    Logger.LogInfo($"[ReStoryTools] 一键电焊 {(_solderMode ? "开启" : "关闭")}（Ctrl+E）");
                }
                else
                {
                    _collectWorkbenchRequested = true;
                    Logger.LogInfo("[ReStoryTools] 工作台收料（E）");
                }
            }

            // F9：切换 IMGUI 面板
            if (Input.GetKeyDown(KeyCode.F9))
            {
                _showPanel = !_showPanel;
                Logger.LogInfo($"[ReStoryTools] QoL 面板 {(_showPanel ? "显示" : "隐藏")}");
            }

            // SRDebugger 若可用则注册选项容器（备用通道）
            if (!_panelRegistered && SRDebug.Instance != null)
            {
                SRDebug.Instance.AddOptionContainer(new QoLToolsPanel(Logger));
                _panelRegistered = true;
                Logger.LogInfo("[ReStoryTools] SRDebugger 选项容器已注册（备用通道）");
            }

            // 处理排队的一键收料请求（不在 OnGUI 内执行场景操作）
            if (_collectRequested)
            {
                _collectRequested = false;
                DoCollectFromSonicBath();
            }

            // 处理排队的"元件一键拼合"请求
            if (_installAllRequested)
            {
                _installAllRequested = false;
                DoInstallAllFromWorkSurface();
            }

            // 处理排队的"工作台收料"请求
            if (_collectWorkbenchRequested)
            {
                _collectWorkbenchRequested = false;
                DoCollectFromWorkbench();
            }
        }

        /// <summary>Unity 原生 IMGUI 面板</summary>
        private void OnGUI()
        {
            if (!_showPanel) return;
            _windowRect = GUILayout.Window(9001, _windowRect, DrawWindow, "ReStory QoL Tools");
        }

        private void DrawWindow(int windowId)
        {
            GUILayout.BeginVertical();

            GUILayout.Label(L.T("panel_hint"));

            _batchScrew = GUILayout.Toggle(_batchScrew, L.T("toggle_screw"));
            _autoDismantle = GUILayout.Toggle(_autoDismantle, L.T("toggle_element"));
            _solderMode = GUILayout.Toggle(_solderMode, L.T("toggle_solder"));

            GUILayout.Space(4);
            if (GUILayout.Button(L.T("btn_assemble")))
            {
                _installAllRequested = true;
                Logger.LogInfo("[ReStoryTools] 元件一键拼合请求已排队");
            }
            if (GUILayout.Button(L.T("btn_collect")))
            {
                _collectRequested = true;
                Logger.LogInfo("[ReStoryTools] 一键收料请求已排队");
            }
            if (GUILayout.Button(L.T("btn_collect_workbench")))
            {
                _collectWorkbenchRequested = true;
                Logger.LogInfo("[ReStoryTools] 工作台收料请求已排队");
            }

            GUILayout.Space(6);
            GUILayout.Label(L.T("state_line", _batchScrew, _autoDismantle, _solderMode));

            GUILayout.Space(6);
            GUILayout.Label(L.T("hotkeys_line"));
            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        /// <summary>
        /// M1：超声波一键收料。
        /// 全取清洗机内元件 → 逐个存入元件库存（游戏现有 API）。
        /// </summary>
        private void DoCollectFromSonicBath()
        {
            try
            {
                // 解析服务：先 ProjectContext，再遍历 SceneContext（游戏服务绑在场景级容器）
                var ultrasonic = TryResolve<UltrasonicService>();
                var elementService = TryResolve<ElementService>();

                if (ultrasonic == null || elementService == null)
                {
                    Logger.LogWarning($"[ReStoryTools] 服务解析失败：Ultrasonic={ultrasonic != null} ElementService={elementService != null}");
                    return;
                }

                if (!ultrasonic.TryRetrieveAllElements(out var elements) || elements.Count == 0)
                {
                    Logger.LogInfo("[ReStoryTools] 超声波机内没有元件（先放元件清洗）");
                    return;
                }

                int stored = 0;
                foreach (var element in elements)
                {
                    if (elementService.TrySendItemToStorage(element)) stored++;
                }
                Logger.LogInfo($"[ReStoryTools] 一键收料完成：取回 {elements.Count} 个，入库 {stored} 个");
            }
            catch (Exception e)
            {
                Logger.LogError($"[ReStoryTools] 一键收料异常：{e}");
            }
        }

        /// <summary>
        /// M3：元件一键拼合（R 键 / 面板按钮）。
        /// 遍历工作台游离元件，分类处理：
        /// - 正常件：当前层可装 → snap 拼合回设备；非当前层 → 留在工作台
        /// - 污染件：自动投入微波清洗（清洗机未开/满则留工作台，容量由 IsFull 自动控制）
        /// - 损坏件：自动回收（原版损坏件就是拖垃圾桶丢；摆角落会沉被 under-surface 回收，直接回收）
        /// </summary>
        private void DoInstallAllFromWorkSurface()
        {
            try
            {
                var deviceService = TryResolve<DeviceService>();
                var workSurface = TryResolve<WorkSurface>();
                var ultrasonic = TryResolve<UltrasonicService>();
                var elementService = TryResolve<ElementService>();
                if (deviceService?.PlacedDeviceContainer == null || workSurface == null)
                {
                    Logger.LogWarning("[ReStoryTools] 元件一键拼合：没有设备在工作台或服务不可用");
                    return;
                }

                // 快照遍历（AttachElement 会从 PlacedElements 移除元件，避免修改集合）
                var candidates = workSurface.PlacedElements
                    .Where(e => e != null && !e.IsDragging && e is InsertableElement)
                    .Cast<InsertableElement>()
                    .ToList();

                int installed = 0, washed = 0, bathSkipped = 0, recycled = 0, skippedNoSlot = 0, skippedDirty = 0;
                foreach (var elem in candidates)
                {
                    var condition = elem.ConditionHandler?.ElementData?.Condition;

                    // 损坏件 → 自动回收（不能装、投不了清洗、摆角落会沉，直接回收）
                    if (condition is DamagedElementCondition)
                    {
                        try
                        {
                            elementService?.DestroyElement(elem);
                            recycled++;
                        }
                        catch (Exception e)
                        {
                            Logger.LogError($"[ReStoryTools] 损坏件回收异常 {elem.name}：{e.Message}");
                        }
                        continue;
                    }

                    // 污染件 → 跳过（自动投洗不可靠：需先 fit 到清洗机平面 + ActiveTool，玩家拖拽聚集处理）
                    if (condition is DirtyElementCondition)
                    {
                        skippedDirty++;
                        continue;
                    }

                    // 正常件 → 当前层可装则拼合
                    var sockets = deviceService.GetAvailableSockets(elem);
                    if (sockets.Count == 0)
                    {
                        skippedNoSlot++; // 非当前层 → 留在工作台
                        continue;
                    }

                    // 目标：优先"拆下来的原件"位置（socket.LastNestedElement == 该元件），否则第一个可装位
                    var target = sockets.FirstOrDefault(s => s.LastNestedElement == elem) ?? sockets[0];
                    try
                    {
                        target.AttachElement(elem);
                        installed++;
                    }
                    catch (Exception e)
                    {
                        Logger.LogError($"[ReStoryTools] 元件拼合异常 {elem.name}：{e.Message}");
                    }
                }

                Logger.LogInfo($"[ReStoryTools] 元件一键拼合：装上 {installed}，污染跳过 {skippedDirty}（拖拽聚集清洗），损坏回收 {recycled}，非当前层跳过 {skippedNoSlot}");

                // ★ v0.8.1：库存补充（材料堆优先原则）——工作台没有的元件，从元件库存取来装上
                // 工作台优先（上面已拼合）→ 库存补充（这里）→ 缺货统计
                var inventory = TryResolve<IInventory>();
                var elementService2 = TryResolve<ElementService>();
                int stocked = 0;
                var missingSet = new HashSet<string>();
                if (deviceService?.PlacedDeviceContainer?.Device != null && inventory != null && elementService2 != null)
                {
                    foreach (var socket in deviceService.PlacedDeviceContainer.Device.ElementSockets)
                    {
                        if (socket.NestedElement != null) continue;  // 已有元件（含刚拼合的）
                        if (!socket.IsAvailable) continue;            // 被上层挡住（未解锁）
                        var need = socket.CompatibleElementInfo;
                        if (need == null) continue;

                        // 从库存找"类型匹配 + 干净"的元件
                        StorageItemElement found = null;
                        int foundIndex = -1;
                        var storage = inventory.StorageElements;
                        if (storage != null)
                        {
                            for (int i = 0; i < storage.Size; i++)
                            {
                                var slot = storage[i];
                                if (slot == null || slot.IsEmpty()) continue;
                                if (!(slot.Item is StorageItemElement sie)) continue;
                                if (!ReferenceEquals(sie.Info, need)) continue; // 引用比较（与游戏 socket 校验一致）
                                if (sie.ElementData?.Condition is DamagedElementCondition) continue; // 损坏件留给玩家
                                if (sie.ElementData?.Condition is DirtyElementCondition) continue;     // 污染件留给玩家
                                found = sie;
                                foundIndex = slot.Index;
                                break;
                            }
                        }

                        if (found == null)
                        {
                            var key = need.NameLocalizationKey;
                            if (!string.IsNullOrEmpty(key)) missingSet.Add(key); // 缺货清单
                            continue;
                        }

                        try
                        {
                            // 玩家原版同款：StorageItemElement.ElementData.Clone() → CreateElementOnSurface 实体化
                            var elem = elementService2.CreateElementOnSurface(found.ElementData.Clone());
                            if (elem != null)
                            {
                                socket.AttachElement(elem);
                                storage.ClearItem(foundIndex); // 从库存移除该槽
                                stocked++;
                            }
                        }
                        catch (Exception e)
                        {
                            Logger.LogError($"[ReStoryTools] 库存补充异常 {need.NameLocalizationKey}：{e.Message}");
                        }
                    }
                }

                Logger.LogInfo($"[ReStoryTools] 元件一键拼合：装上 {installed}，库存补充 {stocked}，缺货 {missingSet.Count} 种{(missingSet.Count > 0 ? "（" + string.Join(", ", missingSet.Take(5)) + "）" : "")}");
            }
            catch (Exception e)
            {
                Logger.LogError($"[ReStoryTools] 元件一键拼合异常：{e}");
            }
        }

        /// <summary>
        /// E 键 / 面板按钮：工作台收料。
        /// 遍历工作台表面游离元件（PlacedElements）→ TrySendItemToStorage 入库。
        /// 只收"工作台表面的"元件——设备上装着的（socket.NestedElement）不在此列表 → 不影响组装中的设备；
        /// 损坏件会被 TrySendItemToStorage 自动拒绝（留在工作台，玩家手动处理）。
        /// </summary>
        private void DoCollectFromWorkbench()
        {
            try
            {
                var workSurface = TryResolve<WorkSurface>();
                var elementService = TryResolve<ElementService>();
                if (workSurface == null || elementService == null)
                {
                    Logger.LogWarning($"[ReStoryTools] 工作台收料：服务不可用 WorkSurface={workSurface != null} ElementService={elementService != null}");
                    return;
                }

                // 快照遍历（TrySendItemToStorage 会从 PlacedElements 移除元件）
                var candidates = workSurface.PlacedElements
                    .Where(e => e != null && !e.IsDragging)
                    .ToList();

                int stored = 0, skipped = 0;
                foreach (var element in candidates)
                {
                    if (elementService.TrySendItemToStorage(element)) stored++;
                    else skipped++; // 损坏件等被拒 → 留工作台
                }
                Logger.LogInfo($"[ReStoryTools] 工作台收料完成：入库 {stored}，跳过 {skipped}（损坏件等）");
            }
            catch (Exception e)
            {
                Logger.LogError($"[ReStoryTools] 工作台收料异常：{e}");
            }
        }

        /// <summary>从 Zenject 容器解析服务：ProjectContext 优先，SceneContext 兜底（游戏服务绑在场景级）</summary>
        public T TryResolve<T>() where T : class
        {
            try
            {
                if (Zenject.ProjectContext.HasInstance)
                {
                    var s = Zenject.ProjectContext.Instance.Container.TryResolve<T>();
                    if (s != null) return s;
                }
                foreach (var scene in UnityEngine.Object.FindObjectsByType<Zenject.SceneContext>(UnityEngine.FindObjectsSortMode.None))
                {
                    var s = scene.Container.TryResolve<T>();
                    if (s != null) return s;
                }
            }
            catch (Exception e)
            {
                Logger.LogError($"[ReStoryTools] TryResolve<{typeof(T).Name}> 异常：{e.Message}");
            }
            return null;
        }

        /// <summary>供 M2-M3 使用的开关状态</summary>
        public bool BatchScrew => _batchScrew;
        public bool AutoDismantle => _autoDismantle;
        public bool SolderMode => _solderMode;

        /// <summary>供补丁类使用的日志</summary>
        public void LogInfo(string msg) => Logger.LogInfo(msg);
        public void LogWarning(string msg) => Logger.LogWarning(msg);
        public void LogError(string msg) => Logger.LogError(msg);
    }
}
