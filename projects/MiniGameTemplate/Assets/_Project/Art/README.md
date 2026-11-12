# Art

按功能分子目录,不放散文件:

- `Sprites/` — UI 图、角色、场景图(统一 ASTC 压缩)
- `Audio/` — 音效(SFX)与音乐(BGM);BGM 出包走远程 CDN,不进首包
- `Spine/` — Spine 导出资源(如有)

命名:`prefix_name_variant`,如 `ui_btn_start`、`char_hero_idle`。
