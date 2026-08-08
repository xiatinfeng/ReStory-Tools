# ReStory 便捷工具 Mod 设计 v1（实现调查 + 方案）

> 2026-08-08 · 项目：`Claw/tools/ReStory-Tools`
> 目标：三个便捷功能，做成 **SRDebugger 面板开关**（开启/关闭）
> 前置调查已完成：以下全部基于反编译源码（`restory-reverse/decompiled/Restory.Assembly`）实证，非猜测。

---

## 0. 游戏机制速览（三功能共同的底层）

| 概念 | 实现 | 关键类 |
|---|---|---|
| 螺丝 | `ThreadedElement`（ElementBase 子类），拆解 = Tween 动画旋转退出 | `ThreadedElement` |
| 元件 | `ElementBase`（MonoBehaviour），事件链齐全 | `ElementBase` |
| 拆解动作 | 长按按钮 71 → `InitInteraction(tool)` → tweener 播放（速度=工具 `UnscrewingSpeed`）→ `CompleteInteraction` → `OnDetached` | `DismantleDisassembleState` |
| 电动螺丝刀"秒出" | **`AutoUnscrewing=true` → `PlayImmediately()` 直接跳动画 + 立即 `CompleteInteraction()`**（不是速度快，是直接完成） | `ThreadedElement.InitInteraction` |
| 设备分层 | `ElementSocket` 依赖图：`blockers`（挡我的）/`blockedSockets`（我挡的）；元件拆下 → `NotifySockets()` → 下游 socket 的 `nestedElement.IsBlocked` 自动更新（解锁） | `ElementSocket` / `SerialElementSocket` / `SubordinateElementSocket` |
| 从属层 | `SubordinateElementSocket.IsBlocked => true`（恒被主件盖住，需要先移主件） | `SubordinateElementSocket` |
| 设备根 | `Device.ElementSockets` = **设备上所有 Socket 的直接列表**（遍历入口） | `Device` |
| 拆下元件去向 | `ResolveElementDetached` → `workSurface.AddElement(nestedElement)`（元件上工作台）；Small 类 → `SmallElementBin` | `ElementSocket` / `WorkSurface` |
| 超声波清洗 | `SonicBath`：放入（`TryInsertElement`）→ 清洗 → `MakeInsertedElementsClean`（**游戏已有一键全清洗**）→ `IsCleaningDone=true`；单取 = `TryRetrieveElement`；**`TryTakeOutAllInsertedElements`（游戏已有一键全取出，返回列表）** + Service 层包装 `UltrasonicService.TryRetrieveAllElements` | `SonicBath` / `UltrasonicService` |
| 材料堆/存储 | `StorageElasticElements`（弹性自动排列，收 `StorageItemElement`）+ **`InventoryBox`（库存箱，元件收料目标，`UltrasonicService` 已注入）** | `StorageElasticElements` / `InventoryBox` |
| 当前设备 | `DeviceService.PlacedDeviceContainer`（工作台当前设备的容器）→ 拆解遍历入口更明确 | `DeviceService` |

---

## 1. 面板方案（SRDebugger，零 UI 开发）

游戏自带 **SRDebugger** 调试面板（21 个 cheat 类都挂在上面）。mod 照抄该模式：

```csharp
// 仿 Restory.Gameplay.Cheats.SRDebugCheatBase
public class QoLToolsPanel : IInitializable, IDisposable {
    void IInitializable.Initialize() => SRDebug.Instance.AddOptionContainer(this);
    void IDisposable.Dispose()      => SRDebug.Instance?.RemoveOptionContainer(this);

    [Category("QoL Tools")] [DisplayName("批量拆螺丝")] public void ToggleBatchUnscrew() { ... }
    [Category("QoL Tools")] [DisplayName("元件自动拆卸")] public void ToggleAutoDismantle() { ... }
    [Category("QoL Tools")] [DisplayName("超声波一键收料")] public void ToggleBulkCollect() { ... }
}
```

- 开关状态持久化：可仿照游戏 `PlayerProfileService`/存档，或先内存开关（重启复位），v1 先内存。
- 玩家开面板：游戏内 SRDebugger 触发键（通常 F 系列/设备厂商默认，游戏里 cheat 面板就是这么开的）。

---

## 2. 功能 1：批量处理螺丝（拆一颗连拆 / 装一颗连装）——v3 已实现

> **需求修正（2026-08-08 用户实测反馈）**：不是"只批量拆"，而是"批量处理"——
> 拆一颗连带拆当前层，装一颗连带装当前层。v1/v2 三次迭代后成型。

**实现调查（实证）**：
- 拆解入口：`DismantleDisassembleState.Enter(selectedElement)` → `DismantleElement` → `selectedElement.InitInteraction(unscrewingToolInfo)`
- 螺丝拆解：`ThreadedElement.InitInteraction` → `AutoUnscrewing` 工具直接 `PlayImmediately()+CompleteInteraction()`；否则 tweener 按 `UnscrewingSpeed` 播放
- **拆/装共用入口**：`ThreadedElement.CompleteInteraction`（override）→ 基类 `ElementBase.CompleteInteraction`：`IsInstalling=false` 走 `OnDetached`（拆），`true` 走 `OnInstalled`（装）。**Prefix 执行时 IsInstalling 是原值 → 可区分**
- "可拆"判定：`ElementBase.IsBlocked == false` + `CanInteraction(当前工具)`；遍历入口：`GetComponentInParent<Device>()` → `Device.ElementSockets` → `NestedElement is ThreadedElement`
- **收纳**：玩家手动拆的螺丝由 `DismantleDisassembleState.ResolveElementDetached`（监听 selectedElement.OnDetached）收进 `SmallElementBin`（Small 类）；**连锁拆的螺丝没人监听 → 悬浮，需手动 `bin.PutElement`**（PutElement 无状态检查，拆下后调用安全）
- **连锁装**：玩家流程 = `DraggingDisassembleState.CompleteDrag` → `socket.AttachElement(screw)`（snap+AttachToDevice 设 IsInstalling=true）→ `InstallingDisassembleState` 拧入。连锁装 = 空 socket（`IsAvailable` + `CompatibleElementInfo.Category==Small`）→ bin 内 `GetComponentsInChildren<ThreadedElement>` 按 **Info 引用相等**（`ValidateElementCompatibility` 引用比较）匹配 → `AttachElement` + `InitInteraction` 完整拧入

**patch 方案（v3，Harmony Prefix on `ThreadedElement.CompleteInteraction`）**：
- 开关开启 + 防重入标记下：
  - `IsInstalling=false`（拆）→ 遍历当前层其他可拆螺丝逐个 `InitInteraction(tool)`（同步拆下）→ **拆下后统一 `smallElementBin.PutElement` 收纳**
  - `IsInstalling=true`（装）→ 遍历当前层空螺丝位 → 从 bin 取 Info 匹配螺丝 → `AttachElement` + `InitInteraction`（完整拧入，auto 工具秒拧）

**三次迭代教训（实锤）**：
1. **v1 Postfix 静默失败**：`CompleteInteraction` 内 `OnDetached` 同步触发 socket 释放 → Postfix 时螺丝已脱离设备，`GetComponentInParent<Device>()` 找不到 → 连锁无效果。→ 改 **Prefix**（v2）。
2. **v2 装螺丝也被连锁拆**：未区分拆/装，装也触发连锁拆 → 把装好的螺丝拆掉（用户实测）。→ v3 用 `IsInstalling` 区分。
3. **v2 连锁拆的螺丝悬浮**：连锁拆绕过玩家状态机，无人监听 OnDetached → 悬浮半空；玩家捡起后状态错乱（进大元件箱不可见）。→ v3 拆后统一 `PutElement`。

**难点（已解决）**：
1. 连锁装螺丝匹配：socket 兼容用 **ElementInfo 引用比较**（`screw.Info == socket.CompatibleElementInfo`），不是 ID 字符串。
2. `ElementInfo` 继承 Odin `SerializedScriptableObject` → csproj 需引用 `Sirenix.Serialization.dll`。
3. 双重 OnInstalled：`AttachToDevice`（Small 分支）+ `CompleteInteraction`（装分支）各触发一次——玩家正常流程本就如此，游戏免疫，照抄安全。

**难度：中（已交付）**

---

## 3. 功能 2：元件自动拆卸

**实现调查（实证）**：
- 元件拆解与螺丝同一套：`ElementBase.InitInteraction(tool)` → `Progress` → `CompleteInteraction` → `OnDetached`
- `InsertableElement`（可插入元件）也走这套；`ThreadedElement` 带螺纹
- 拆下后：`ElementSocket.ResolveElementDetached` → `workSurface.AddElement`（Small 类进 `SmallElementBin`）
- 分层解锁：socket `NotifySockets` → 下游 `IsBlocked` 更新（与功能 1 同机制）

**patch 方案**（Harmony，作为功能 1 的扩展/独立开关）：
- 补丁点：`DismantleDisassembleState.DismantleElement` 或 `ThreadedElement.CompleteInteraction`（Postfix）
- 算法：开关开启时，遍历 `Device.ElementSockets` 中所有 `NestedElement != null && !IsBlocked && CanInteraction(当前工具)` 的元件（含 InsertableElement），逐层自动拆：
  - 螺丝（ThreadedElement）→ 按功能 1 逻辑
  - 非螺丝元件（InsertableElement 等）→ `InitInteraction` 需要对应工具（如镊子/手？`CanInteraction` 判定）——**需要确认非螺丝元件用什么工具拆**（待查 `InsertableElement.CanInteraction`）
- 每帧/每层推进，直到设备可拆层清空或用户关开关

**难点**：
1. 非螺丝元件的拆解工具类型（看 `InsertableElement.CanInteraction` —— 待确认）
2. 拆完元件要入库（进 WorkSurface 后玩家还得手动收）——自动拆+自动收是否连做？**v1 只做"自动拆到工作台"，不自动收**（收是功能 3 的事）
3. critical 元件保护

**难度：中-中高**（比功能 1 多：元件类型 + 工具匹配 + 入库流程）

---

## 4. 功能 3：超声波一键收料

**实现调查（实证 + CodeGraph 调用图）**：
- `SonicBath.MakeInsertedElementsClean()`：**游戏已有一键全清洗**（遍历全部元件 `RemoveContaminationFromElement` + `IsCleaningDone=true`）
- `SonicBath.TryTakeOutAllInsertedElements(out list)`：**游戏已有一键全取出**（清空 `insertedElements` 返回列表）
- `UltrasonicService.TryRetrieveAllElements(out list)`：Service 层已包装全取（注入：`sonicBath`/`inventoryBox`/`deviceService` 等）
- 现状缺口：**全取后元件回到玩家操作流，没有"直接收进库存箱/材料堆"**——这正是要补的
- 收料目标：`InventoryBox`（库存箱，UltraSonicService 已注入，取回元件时会 `ToggleIndicator(true)` 提示玩家放库存箱）

**patch 方案（Harmony，最简单的一个）**：
- 面板按钮"超声波一键收料" → 获取 `UltrasonicService`（或 `SonicBath`）→ `TryRetrieveAllElements(out list)` → 遍历 list 全部放入 `InventoryBox`（或转 `StorageItemElement` 入 `StorageElasticElements`）
- 触发时机：面板按钮动作即可；也可 patch `MakeInsertedElementsClean` 完成时自动收（可选）

**难点**：
1. `InventoryBox` 的"放入元件"API 待确认（`AddItem`? 参考 `StorageElasticElements.AddItem(IStorageItem, int)` + `StorageItemElement` 包装）
2. 从 BepInEx 侧拿 `UltrasonicService`/`InventoryBox` 实例：Zenject 容器访问（`ProjectContext`/`SceneContext` 或反射拿静态引用）——**M0 要解决的通用问题**

**难度：低**（比原评估更低——全取/全清洗游戏已具备，只需收料一步）

---

## 5. 工程结构（BepInEx 插件）

```
Claw/tools/ReStory-Tools/
├─ ReStoryTools/                  ← BepInEx 插件工程（.NET 6，netstandard2.1）
│  ├─ Plugin.cs                   ← BepInPlugin 入口 + Zenject 外部访问
│  ├─ QoLToolsPanel.cs            ← SRDebugger 面板（三开关）
│  ├─ Patches/
│  │  ├─ BatchUnscrewPatch.cs     ← 功能 1
│  │  ├─ AutoDismantlePatch.cs    ← 功能 2
│  │  └─ SonicBathCollectPatch.cs ← 功能 3
│  └─ ReStoryTools.csproj
├─ libs/                          ← 引用：Restory.Assembly.dll / UnityEngine / BepInEx / Harmony
└─ DESIGN.md                      ← 本文件
```

- **加载器**：BepInEx 6.x（Unity 6 支持版，精确版本待确认 Unity 版本后定）+ HarmonyX
- **程序集引用**：`Restory.Assembly.dll`（从游戏 Managed 复制，不发布）、`UnityEngine.CoreModule` 等、`SRDebugger.dll`（面板）、`BepInEx`、`Harmony`
- **安装**：BepInEx 解压进游戏根目录 + 插件 dll 进 `BepInEx/plugins/`

---

## 6. 里程碑

| 里程碑 | 内容 | 状态 |
|---|---|---|
| **M0** | 确认 Unity 版本 + BepInEx 安装 + 最小插件加载（SRDebugger 面板出现 QoL Tools） | ✅ 环境就绪（Unity **6000.3.10f1**；BepInEx **6.0.0-pre.2 Mono x64** 已装游戏根目录；插件已编译部署 `BepInEx/plugins/ReStoryTools/`）⏳ 待用户跑游戏验证面板 |
| **M1** | 功能 3 一键收料（最简单） | 超声波清洗完点按钮全收进材料堆 |
| **M2** | 功能 1 批量拆螺丝 | 拆一颗自动连拆当前层螺丝 |
| **M3** | 功能 2 元件自动拆卸 | 开关开启自动拆完整层元件 |
| **M4** | 打磨：开关持久化、防误拆、连锁时序优化 | 长时间游玩不炸 |

## 7. 待确认（M0 做）

1. **Unity 精确版本**（PowerShell 读 UnityPlayer.dll VersionInfo）→ 定 BepInEx 版本
2. **BepInEx 侧访问 Zenject 容器**（拿 UltrasonicService/InventoryBox/DeviceService 实例）——三功能共同前置，M0 优先验证
3. `InventoryBox` 放入元件的 API（`AddItem`/`StorageItemElement` 包装）——功能 3 收料目标
4. 非螺丝元件（InsertableElement）的 `CanInteraction` 拆解工具类型（功能 2 前置）
5. SRDebugger 面板触发键（游戏内怎么打开）——确认玩家可用性
6. `UltrasonicService.TryRetrieveAllElements` 现有调用方（游戏里是否已接 UI——若已接，只需加"收进库存"一步）

## 8. CodeGraph 辅助侦查记录（2026-08-08）

- 对 `restory-reverse/decompiled/Restory.Assembly` 建了 codegraph 索引（2644 文件 / 43,397 节点 / 82,073 边）
- 命令：`npx @colbymchenry/codegraph init <path>`（CLI 即 CodeGraph MCP 的 `@colbymchenry/codegraph` npm 包）
- 已验证调用链：SonicBath（InsertElement→TryRetrieveElement→MakeInsertedElementsClean→TryTakeOutAllInsertedElements）↔ UltrasonicService（TryRetrieveAllElements 已包装全取）；SRDebugCheatBase→SRDebug.Instance.AddOptionContainer（面板注册模式确认）；HomeDepotShopService.Initialize（商店物品列表=CleaningToolsItemsList 等数组→字典，加工具注入点）
- 收益：发现"游戏已有全取/全清洗"（功能 3 降难度）+ InventoryBox/DeviceService 遍历入口
