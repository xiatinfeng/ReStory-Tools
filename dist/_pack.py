# -*- coding: utf-8 -*-
"""ReStory-Tools mod 打包脚本（纯 zipfile 组装，无中间目录）：
只含 mod dll（BepInEx/plugins/ReStoryTools/）+ 中英说明，依赖 ReStory-BepInEx-Framework。"""
import zipfile, os

BASE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))  # ReStory-Tools 根目录
VERSION = "v0.8.2"

zh = """================================================================
  ReStory-Tools v0.8.2 - 复古物语 便捷工具（mod 本体）
================================================================

【前置依赖】
  必须先安装 ReStory BepInEx Framework（框架包）：
  https://github.com/xiatinfeng/ReStory-BepInEx-Framework
  （解压框架包到游戏根目录，装过一次即可）

【安装】
1. 解压本包全部内容到游戏根目录（覆盖）：
   游戏目录/BepInEx/plugins/ReStoryTools/ReStoryTools.dll
2. 启动游戏，进工作台后按 F9 打开面板，或用快捷键

【快捷键】
  R         元件一键拼合（工作台当前层，损坏件自动回收）
  T         超声波一键收料（清洗机元件全部入库）
  Ctrl+R    批量拆装螺丝 开关（电动螺丝刀专属）
  Ctrl+T    元件批量自动拆卸 开关
  F9        便捷面板 开关

【功能】
  - 批量拆装螺丝：拆一颗连拆当前层，装一颗连装当前层（装回拆下的原件）
  - 元件批量自动拆卸：连锁拆当前层元件，自动摆到工作台角落
  - 元件一键拼合：干净件拼合 / 污染件跳过（拖拽聚集清洗）/ 损坏件自动回收
  - 污染件拖拽聚集：拖一个污染件，其他污染件跟随鼠标，拖到清洗机一起投洗
  - 超声波一键收料：清洗完的元件一键收进库存

【语言】
  UI 跟随系统语言（中/英）。强制切换：在插件目录建 lang.txt 填 en 或 zh
  （插件目录 = 游戏目录/BepInEx/plugins/ReStoryTools/）

【卸载】
  删除 游戏目录/BepInEx/plugins/ReStoryTools/ 文件夹

【许可】
  本 mod 为 MIT 许可。依赖的 BepInEx 框架为 LGPL-2.1（见框架包声明）。

【免责声明】
  仅供学习与个人使用，修改游戏文件风险自负，与游戏开发商无关。
================================================================
"""

en = """================================================================
  ReStory-Tools v0.8.2 - QoL plugin for ReStory: Chill Electronics Repairs (mod only)
================================================================

[Prerequisite]
  You MUST install the ReStory BepInEx Framework first:
  https://github.com/xiatinfeng/ReStory-BepInEx-Framework
  (Extract the framework to your game root. Install once, all mods share it.)

[Install]
1. Extract everything in this package to your game root (overwrite).
   Result: <game>/BepInEx/plugins/ReStoryTools/ReStoryTools.dll
2. Launch the game. In the workshop press F9 for the panel, or use hotkeys.

[Hotkeys]
  R         Assemble all current-layer elements (damaged parts auto-recycled)
  T         Collect all elements from the ultrasonic bath into storage
  Ctrl+R    Toggle 'Batch Screws' (electric screwdriver only)
  Ctrl+T    Toggle 'Auto-Dismantle Elements'
  F9        Toggle the QoL panel

[Features]
  - Batch Screws: chain-dismantle current-layer screws / chain-install originals
  - Auto-Dismantle Elements: chain-dismantle current-layer, auto-place on bench corner
  - Assemble Elements: clean snap in / dirty skipped (drag-follow cleaning) / damaged auto-recycled
  - Dirty-Element Drag Follow: pick up one dirty part, others follow; drop at the ultrasonic
    bath to wash all at once (stops when bath is full)
  - One-Key Collect: take all washed elements out of the bath into storage

[Language]
  UI follows your system language (Chinese/English). To force: create 'lang.txt'
  (content 'en' or 'zh') in <game>/BepInEx/plugins/ReStoryTools/

[Uninstall]
  Delete <game>/BepInEx/plugins/ReStoryTools/

[License]
  This mod is MIT. Depends on BepInEx framework (LGPL-2.1, see framework pack).

[Disclaimer]
  For learning and personal use only. Modding is at your own risk.
  Not affiliated with the game developer.
================================================================
"""

dll = os.path.join(BASE, "ReStoryTools", "bin", "Release", "netstandard2.1", "ReStoryTools.dll")
if not os.path.exists(dll):
    print("错误: 未找到编译产物，先构建：dotnet build -c Release -p:GameDir=...")
    raise SystemExit(1)

out = os.path.join(BASE, "dist", "ReStory-Tools-%s.zip" % VERSION)
# 注意：不 os.remove（沙箱安全机制拦截），zipfile 'w' 模式直接覆盖截断

with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
    z.write(dll, "BepInEx/plugins/ReStoryTools/ReStoryTools.dll")
    z.writestr("安装说明.txt", zh.encode("utf-8-sig"))
    z.writestr("README-EN.txt", en.encode("utf-8-sig"))

with zipfile.ZipFile(out) as z:
    n = len(z.namelist())
print("=== 打包完成: %s (%.1f KB, %d 项) ===" % (out, os.path.getsize(out) / 1024.0, n))
for name in zipfile.ZipFile(out).namelist():
    print("  ", name)
