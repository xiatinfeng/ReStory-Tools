================================================================
  ReStory-Tools v0.7.0 - QoL plugin for ReStory: Chill Electronics Repairs (mod only)
================================================================

[Prerequisite]
  You MUST install the ReStory BepInEx Framework first:
  https://github.com/xiatinfeng/ReStory-BepInEx-Framework
  (Extract the framework to your game root. Install once, all mods share it.)

[Install]
1. Extract this package and merge the 'plugins' folder into
   <game>/BepInEx/  (result: <game>/BepInEx/plugins/ReStoryTools/ReStoryTools.dll)
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