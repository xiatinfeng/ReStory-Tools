# ReStory-Tools（复古物语便捷工具）

《ReStory: Chill Electronics Repairs》的 BepInEx 便捷工具插件（QoL Mod），
用 Harmony 补丁 + Unity IMGUI 面板为后期大量设备维修减负。

> 游戏是 Unity **Mono** 构建（主程序集 `Restory.Assembly.dll`），mod 极其友好，
> 所有功能均走游戏**原生 API/路径**（反编译实证），不改游戏逻辑。

## 功能

| 功能 | 触发 | 说明 |
|---|---|---|
| **批量拆装螺丝** | `Ctrl+R` 开关 | 拆一颗连拆当前层可拆螺丝；装一颗连装当前层（自动装回"拆下来的原件"）。**电动螺丝刀专属**（秒拆/秒拧），普通螺丝刀不连锁 |
| **元件批量自动拆卸** | `Ctrl+T` 开关 | 拆一个元件连锁拆当前层其他元件，自动摆到工作台角落（跳过螺丝与损坏件） |
| **一键电焊** | `Ctrl+E` 开关 | 开启后元件进入焊接模式自动完成所有焊点（`ForceCompleteSoldering`），省去移动电烙铁；只焊不清洁（烟灰清洁仍原版手动） |
| **元件一键拼合** | `R` 键 / 面板按钮 | 三层取件：**工作台优先** → **库存（材料堆）补充** → **缺货统计**（日志报告缺货清单）；损坏件自动回收；污染件跳过（交给拖拽聚集） |
| **工作台收料** | `E` 键 / 面板按钮 | 工作台表面游离元件一键收进库存（不影响组装中的设备；损坏件自动留台面） |
| **污染件拖拽聚集** | 自动（拖污染件时） | 拿起一个污染件，其他污染件跟随鼠标；拖到清洗机松手一起投洗（容量满自动停），拖别处松手落回原位 |
| **超声波一键收料** | `T` 键 / 面板按钮 | 清洗机内全部元件取回并入库（游戏原生 API） |

面板：`F9` 切换（IMGUI 自绘，不依赖 SRDebugger）。所有按钮/开关均面板 + 快捷键双通道。

## 安装

> **前置**：先安装 [ReStory BepInEx Framework](https://github.com/xiatinfeng/ReStory-BepInEx-Framework)
> （BepInEx 5.4.23.5 + 防卡顿配置，一次安装所有 mod 共用）。
> 游戏是 Unity 6.3（6000.3.10f1）——**必须用 5.4.23.5+**（含 Unity 6 修复），BepInEx 6.0.0-pre 系列会崩。

1. 安装框架包（解压到游戏根目录，见框架包说明）
2. 把编译出的 `ReStoryTools.dll` 放入 `<游戏目录>/BepInEx/plugins/ReStoryTools/`
3. 启动游戏（首次会弹 BepInEx 控制台），进工作台后按 `F9` 或快捷键使用

> 界面语言跟随系统（中/英）；强制切换：插件目录建 `lang.txt`（内容 `en`/`zh`）。

## 构建

```bash
# 需要 .NET SDK（10.0+ 已验证；netstandard2.1 target）
dotnet build -c Release -p:GameDir="D:/你的/游戏安装目录"
```

- `ReStoryTools.csproj` 引用游戏 dll（UnityEngine、Restory.Assembly、Zenject 等）和
  BepInEx core，全部用 `$(GameDir)` 属性定位——**把 GameDir 改成你的游戏路径即可**。
- 产物：`ReStoryTools/bin/Release/netstandard2.1/ReStoryTools.dll`

## 技术要点

- **Harmony 补丁**：`ThreadedElement.CompleteInteraction`（螺丝拆/装，Prefix 区分 `IsInstalling`）、
  `InsertableElement.CompleteInteraction`（元件连锁拆）、`DraggingDisassembleState.Enter/Exit`（污染件聚集）、
  `SolderingService.UpdateSolderingProcess`（一键电焊）
- **拼合取件链**：工作台 `WorkSurface.PlacedElements` → 库存 `IInventory.StorageElements`
  （`StorageItemElement.ElementData.Clone()` + `ElementService.CreateElementOnSurface` 实体化，游戏原版同款）→ 缺货统计
- **Zenject 容器**：`ProjectContext.Instance.Container.TryResolve<T>()` + 遍历 `SceneContext` 兜底
  （游戏服务绑在场景级容器）
- **私有成员访问**：反射缓存委托（`CompleteInteraction`、`HideSmallElementProjection`、`SetSelectedSocket`、
  `UltrasonicService.sonicBath` 字段）
- **面板**：Unity IMGUI `OnGUI`，F9 切换，不依赖 SRDebugger（该游戏 SRDebugger 面板 UI 未实例化）

## 依赖与许可

- 本插件：MIT（见 LICENSE）
- 运行时依赖 [BepInEx 5](https://github.com/BepInEx/BepInEx)（**LGPL-2.1**，需用户自行安装，插件不捆绑分发）
- 引用游戏 dll 仅编译期引用（`Private=false`），不包含游戏代码/资源

## 免责声明

本插件仅供学习与个人使用。对游戏文件的修改风险自负，与游戏开发商无关。
