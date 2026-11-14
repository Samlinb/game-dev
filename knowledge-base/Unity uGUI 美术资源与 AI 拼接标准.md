# Unity uGUI 美术资源与 AI 拼接标准

已有切图或效果图，要走本仓库的预检、版本化打包和引擎验收时，见[游戏界面从设计稿到 Prefab 交付](./游戏界面从设计稿到Prefab交付.md)。

适用于用 AI 设计或还原游戏界面，并在 Unity 中交付可编辑、可适配的 uGUI Prefab。已有工程优先复用现行 Canvas、字体、组件库和资源导入规则。文中的竖屏尺寸、设计参数和验收阈值是首屏试点示例。
核对日期：2026-11-13（外部 Unity 文档 CanvasScaler / Screen.safeArea / RectTransform / 9SliceSprites 本轮 WebFetch 复核可达；PSD2UGUI_X/PSD2Unity/PSD2UI 本地源码与 118 项测试由来源会话核对，日期未登记；上游版本 commit 见「三个项目的评估」）。

**工具选型。** 要快速从已有 PSD 得到 Unity 控件初稿，优先试 **PSD2UGUI_X**。要把美术与 AI 的组件、列表和资源交付做成可预检的长期流程，优先评估 **PSD2UI + 项目 Unity Adapter**，由 PSD2UI 导出布局和资源，由 Adapter 映射项目组件并保存 Prefab。**PSD2Unity** 适合纯图界面、静态摆位和切图对照。

## 三个项目的评估

| 维度 | PSD2UGUI_X | PSD2Unity | PSD2UI（YoyoRD） |
| --- | --- | --- | --- |
| 本地评估版本 | `2c78da1`，示例工程为 Unity 2022.3.62f3。随仓交付 DLL、配置、示例 PSD、PS 脚本及控件模板 | `30d9b9a`，UPM 包 1.1.2。随仓交付 Photoshop JSX、Unity Editor C#，包声明 Newtonsoft.Json 3.2.1 | `248d891`，`main` 分支 CEP 0.4.6，目标 Photoshop CC 2019 20.0.4。上游提供 Photoshop 25.0+ 的 CCX 分支 |
| 交付终点 | PSD → Unity 可编辑节点树 → uGUI Prefab。可以用后缀标注类型及子部件，模板覆盖 Button、TMP、Toggle、Slider、Scroll View 等 | Photoshop 导出 PNG + `.ps.data` → 在 Unity 选择数据文件并执行 `Assets/PSDTools/PSD2UGUI`，生成 Canvas 下的组节点和 Image。保存 Prefab 需另做 | Photoshop 配置组件 → JSON Bundle + PNG。仓库提供面板、Schema 和 PS-MCP，Unity Reader、控件生成与 Prefab 保存由项目实现 |
| AI 能从中获得什么 | 本地提示词展示了主控件、子部件和节点分类的识别协议。人工或 AI 可先写显式标签，再逐项核对导入结果 | JSON 提供画布尺寸、组路径、图层名、图层 ID、层序、坐标和尺寸。C# 可直接审查，预留创建节点和缺图回调，适合扩展 | MCP 可以读取真实图层、已有配置，按工具支持范围配置和整理图层，再导出可预检的组件角色、列表模板、状态、文字和资源 |
| 量产需验证什么 | DLL 决定实际行为。上游将 AI 自动识别、智能九宫格和增量更新列为 Pro 能力，按目标版本逐项验证。正式发布前确认随仓 DLL 和 Pro 授权范围 | 当前路径只生成 Image，使用绝对位置和 Sprite 原生尺寸。TMP、按钮、九宫格、锚点及交互需后处理。分组路径的首尾 `/` 会产生空段，嵌套组、不同 Canvas 设定及图层遮挡顺序须实测 | 验证目标 Photoshop 宿主、字体还原、状态图和九宫效果。Unity 端需验证坐标、层序、锚点、组件映射和引用保留。仓库迁移说明记录项目级许可证待维护者决定，分发前核对授权 |

已核对本地源码、配置、模板和样例元数据。PSD2UI 的 Bundle、列表布局、命名、资源、九宫、文字、MCP 命令和模拟导出等 118 项定向测试通过。目标 Photoshop 实际导出、Unity 端视觉与交互，以及 PSD2UGUI_X 的 Pro 功能留待试点。 [PSD2UGUI_X 控件配置](../projects/PSD2UGUI_X/Assets/Plugins/PSD2UIForm/Psd2UIFormConfig.json) · [PSD2Unity 导入实现](../projects/PSD2Unity/Editor/PSDCreateor.cs#L113) · [PSD2UI 上游说明](https://github.com/YoyoRD/PSD2UI/blob/main/README.md) · [PSD2UI 迁移说明](../projects/PSD2UI/docs/MIGRATION.md)

## 美术资源交付

| 交付项 | 要求 |
| --- | --- |
| 界面稿 | 每屏交付 `preview.png` 和可编辑的分层数据。Photoshop 流程保留 PSD，采用 PSD2UI 时交付原生 Bundle，直接拼接流程用切图和 `layout.json` 表达分层。设计稿为 RGB 8-bit、sRGB，画布等于项目的参考分辨率。图层保留前后顺序，分组按功能区和组件组织 |
| 切图 | 每个独立移动、替换或变状态的元素单独交付。带透明度的资源使用 RGBA PNG，裁掉无用途的透明区，保留阴影和发光。对齐依赖留白时保留约定尺寸，或记录原始尺寸和裁剪偏移 |
| 命名与复用 | PNG 用小写下划线，如 `icon_close.png`。共享资源用稳定 ID，如 `shared/icon_close`，各屏引用已有资源。PSD 同组下层名在去除导入后缀、忽略大小写后保持唯一，组名、层名用 ASCII 字母、数字和下划线 |
| 可编辑文字 | 标题、数值、按钮文字和本地化文案保留文字层。标注字体样式、字号、颜色、对齐、行数、溢出策略和文案键。Logo 等固定艺术字按图片资源交付 |
| 可拉伸资源 | 面板底和按钮底给出九宫格边距，按 Sprite 像素依次为 `[left, bottom, right, top]`。源图左右边距之和小于宽、上下之和小于高，成品尺寸验证边框和圆角 |
| 交互和变体 | 按项目组件约定交付正常、按下、禁用状态。界面用到悬停、焦点、选中、空列表或加载态时，补对应设计及状态说明。每种状态明确复用组件、颜色变化或独立贴图 |
| 图层效果 | 纯装饰的跨层混合、蒙版、滤镜合成为独立切图并和预览比对。运行时需要动态裁剪的区域，在布局中声明视口或遮罩 |

设计新界面时，给 AI 提供界面目标、主操作和返回路径、项目已有 UI 截图、颜色与字体样式、组件和共享资源清单、目标比例、最短和最长文案。先确定信息层级、控件和状态，再出预览与分件。生图用于插画、纹理、装饰等素材，可点击控件、文字和列表用项目组件组装。

## Unity uGUI 拼接

| 设计意图 | 拼接规则 |
| --- | --- |
| 页面与 Canvas | 页面 Prefab 挂到项目现有 UI 根节点。新项目的起步配置用 `Scale With Screen Size` 和设计稿尺寸，`Match` 随目标比例验证。页面根节点全拉伸，关键内容放在使用 `Screen.safeArea` 的容器下，背景可铺满屏幕。 [Canvas Scaler](https://docs.unity3d.com/2022.3/Documentation/Manual/script-CanvasScaler.html) · [Screen.safeArea](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Screen-safeArea.html) |
| 固定、贴边与拉伸 | 中央弹窗锚定中心，右上角关闭按钮锚定父节点右上，底栏按约定贴底，需随父级宽度改变的区域横向拉伸。先定父节点、锚点和 Pivot，再填位置、尺寸或边距。 [RectTransform](https://docs.unity3d.com/2022.3/Documentation/Manual/class-RectTransform.html) |
| Sprite 和九宫格 | 普通切图用 `Sprite (2D and UI)` 与 `Image`，按工程统一 PPU、图集及平台压缩。像素尺寸按参考稿还原时校准 Sprite PPU 与 Canvas Reference PPU。九宫格用 Full Rect Sprite、显式 Border 和 `Image.Type = Sliced`，由 RectTransform 尺寸驱动。 [Sprite 九宫格](https://docs.unity3d.com/2022.3/Documentation/Manual/9SliceSprites.html) |
| 文本 | 使用项目现有 `TextMeshProUGUI` 样式和字体资产，验证 CJK 与其他目标语言的字形和回退。布局声明换行、截断或省略策略，按最长文案测试 |
| 按钮和射线 | 复用项目 Button 等控件 Prefab，核对可点范围、`targetGraphic` 和状态。装饰 Image 和文本关闭 `Raycast Target`，保留交互命中面。页面在有 GraphicRaycaster 和 EventSystem 的正常 UI 场景中验收 |
| 动态列表 | 用 `ScrollRect → Viewport → Content`，矩形视口可用 `RectMask2D`。给 ScrollRect 绑定 Viewport 和 Content，动态项复用 Item Prefab。同一轴的尺寸由布局组或显式尺寸负责，避免多个组件竞争 |
| 重导入 | 导入结果先放到暂存 Prefab 或场景，生产 Prefab 以稳定节点 ID 定位、原位更新。共享 Sprite 沿用路径和 `.meta`，回归已有绑定、动画路径及 Prefab Variant 覆盖 |

## PSD2UGUI_X 标签与 PSD2Unity 导入

PSD2UGUI_X 使用项目随附脚本的后缀。复合控件用组承载，背景、文字和状态作为该控件的直接子层。以下是一个弹窗的 Photoshop 图层树，展示顺序从前到后。

```text
RewardPopup
├─ CloseButton.bt
│  ├─ CloseDisabled.img.disable
│  ├─ ClosePressed.img.press
│  └─ CloseNormal.img.bg
├─ Title.txt.tmp
└─ PanelBg.img.sliced
```

- 常用类型是 `.img`、`.txt.tmp`、`.bt`、`.tg`、`.sld`、`.sv`。按需给按钮文字加 `.bttxt`，滚动视口加 `.vpt`。后缀按「导出 → 主类型 → 文字后端 → 图片类型 → 部件」组合。交付时以 [随仓 Photoshop 标签菜单](../projects/PSD2UGUI_X/Assets/Plugins/PSD2UIForm/PSScript/PSD2UGUI-LayerTagMenu.jsx#L10) 为准。
- 默认态预览只显示正常图，按下和禁用图按状态交付。PSD2UGUI_X 导入后核对 TMP 字体、按钮状态资源和九宫边距。若状态图以隐藏层制作，先验证该版本是否收录，再决定沿用隐藏层还是在拼接时显式绑定 Sprite。
- 用 PSD2Unity 切图时保存 PSD，并解除 Photoshop 画板后导出。选 `Write Layer PNGs`、`Write JsonData`、`Trim whitespace`，建议起步 `Scale 100%`、`Padding 0`。用 `Use groups` 时验证嵌套目录和层序。用 `Ignore hidden layers` 生成默认态时，按下、禁用等隐藏状态图须有独立切图交付。Unity 导入时先确认 PNG 类型为 Sprite。 [Photoshop 导出脚本](../projects/PSD2Unity/Editor/PhotoShopScript/ExportToPNG.jsx)
- PSD2Unity 的 `.ps.data` 使用**画布左下角为原点的图片中心坐标**。转换为下节的矩形时，按 `left = x - width / 2`、`top = canvasHeight - y - height / 2`，与同批裁切尺寸核对。使用额外 Padding 时，先核对 PNG 尺寸和数据内尺寸。 [导出坐标计算](../projects/PSD2Unity/Editor/PhotoShopScript/ExportToPNG.jsx#L147)

## PSD2UI 美术交付与 Unity 映射

1. **配置组件。** 用面板或 PS-MCP 读取真实图层和已有配置，先配置内层按钮及其背景和文字，再配置外层列表或网格。列表指定一个完整条目作为模板，其余展示样例标为「仅预览」，视口、间距、方向和默认状态单独确认。组件用途来自 PSD 内嵌 XMP 中保存的语义配置。 [美术操作指南](../projects/PSD2UI/psd2ui/docs/ARTIST_WORKFLOW.md) · [PS-MCP 工具](../projects/PSD2UI/psd2ui/PS-MCP/README.md)
2. **管理切图。** Schema 1.5 的正式命名流程使用 `comm_panel_gold`、`shop_icon_reward` 一类图片基础名，第一段决定资源模块，组名和文案可以用中文。共享图确认可复用后再使用同名。同名、同类型且尺寸描述一致可能复用现有 PNG，换色版本需要单独命名。未指定类型的图片在面积达到 262144 px² 或最长边超过 2040 px 时默认归为 Texture，手动类型、九宫和必要 Image 角色优先。 [图片尺寸规范](../projects/PSD2UI/psd2ui/docs/IMAGE_SIZE_STANDARD.md)
3. **保留交付目录。** 一起交付 `json/<界面>.psd2ui.json`、`sprite/<模块>/*.png`、`texture/<模块>/*.png` 和预览图。项目导入端从 JSON 所在目录按 `../<kind>/<module>/<fileName>` 解析图片，模块取资源自身。PSD 旁的 `*.psd2ui.authoring.json` 是创作配置镜像，组件修改通过面板或 MCP 保存。 [数据契约](../projects/PSD2UI/psd2ui/Contracts/README.md)

| PSD2UI 字段 | 项目 Unity Adapter 处理 |
| --- | --- |
| `document`、`rect`、`children` | 校验 Schema 版本。使用父节点左上原点和像素尺寸还原。固定布局初稿若使用左上 Anchor 和 Pivot，则 `anchoredPosition = (x, -y)`。同父下反转前景到背景的 children 顺序，再按项目要求添加响应式锚点 |
| `resourceId`、`kind`、`sliceBorder` | 绑定 Image/Sprite 或 RawImage/Texture。将 PSD2UI 的 `{left, top, right, bottom}` 映射为 Unity Border `(left, bottom, right, top)`。九宫 PNG 已压缩可拉伸区域，RectTransform 尺寸取节点 `rect` |
| `text.fontKey`、`text.layoutMode` | 查项目字体映射，区分点文本和段落文本，按项目 TMP 样式验证字号、对齐、描边和多语言 |
| `semantic`、`structure.roles`、模板、布局、`visualStates` | 复用项目控件 Prefab，按角色节点 ID 接引用，列表由一个 Item Prefab 驱动。导出状态给出初始外观，运行时事件、数据和状态切换由项目接入 |
| `document.id`、节点 ID、资源 ID | 保存导入映射，用已匹配的 ID 原位更新，保留现有路径与 `.meta`。缺少匹配时报告节点和差异，重导入实测重名、改名、图层替换和引用 |

首版 Adapter 可先完成 Group、Image、RawImage、TMP 和 Prefab 保存，再用一个带按钮和列表的页面验证语义映射。PSD2UI 的 JSON 提供源布局，项目补充响应式锚点、安全区、行为和本地化键。已有 PSD2UI Bundle 的界面直接读取该 Bundle，无需重复手填同一套节点坐标。 [Unity 接入指南](../projects/PSD2UI/docs/UNITY-INTEGRATION.md) · [JSON Schema](../projects/PSD2UI/psd2ui/Contracts/psd2ui.schema.json)

## 无 PSD2UI Bundle 时的布局数据

直接生成素材或手工整理布局时，每屏交付 `preview.png`、`layout.json`、切图及共享资源引用。有 Photoshop 工作流时附 PSD。下面的项目布局示例可由 AI 按字段拼接，或由项目导入器读取。

- `referenceResolution` 是稿件尺寸。`rect` 统一为全稿坐标 `[left, top, width, height]`，原点左上，单位为参考像素。即使节点挂在别的容器下，矩形仍按全稿记录，拼接时根据父矩形转换为局部位置。父节点用 `parent` 引用，`$root` 表示全屏页面，`$safeArea` 表示项目安全区容器。
- 节点用长期稳定的 `id`、实际组件 `type`、`parent`、`rect`、`anchor` 和 `siblingIndex` 描述。层序仅在同一个父节点下比较，数值越大越靠前。贴边节点以设计稿相对父节点的边距为准，伸缩节点记录四边距或对应布局参数。涉及共享组件时给出 `prefabId` 和要覆盖的字段。
- Image 用 `asset` 引用项目资源 ID，九宫格再给 `imageType` 和 `border`。TMP 提供 `textKey`、`sampleText`、`textStyle`、对齐、最大行数和溢出方式。交互控件给 `actionId` 和状态映射。设计里没有确认的字体、资源或行为列为待确认项。

以下示例以 1080 × 1920 竖屏全矩形为参考稿，标题样式从项目样式库解析，关闭按钮的 normal、pressed、disabled 都引用已有 Sprite。

```json
{
  "screenId": "RewardPopup",
  "referenceResolution": [1080, 1920],
  "nodes": [
    {"id": "Panel", "type": "Image", "parent": "$safeArea", "rect": [120, 640, 840, 640],
     "anchor": "center", "siblingIndex": 0, "asset": "shared/panel_gold",
     "imageType": "Sliced", "border": [24, 24, 24, 24]},
    {"id": "Title", "type": "TextMeshProUGUI", "parent": "Panel", "rect": [240, 700, 600, 72],
     "anchor": "topCenter", "siblingIndex": 0, "textKey": "reward.title", "sampleText": "奖励",
     "textStyle": "DialogTitle", "alignment": "center", "maxLines": 1, "overflow": "ellipsis"},
    {"id": "CloseButton", "type": "Button", "parent": "Panel", "rect": [848, 664, 88, 88],
     "anchor": "topRight", "siblingIndex": 1, "actionId": "Close",
     "states": {"normal": "shared/icon_close", "pressed": "shared/icon_close_pressed",
                "disabled": "shared/icon_close_disabled"}}
  ]
}
```

示例中面板右边距到按钮右边为 24，按钮距面板上边为 24。以父节点右上角为锚点和 Pivot，`CloseButton.anchoredPosition` 为 `(-24, -24)`，尺寸为 `(88, 88)`。

## AI 执行顺序和交付验收

1. **读取项目约定。** 打开同类页面 Prefab、现有 Canvas 配置和共享资源。设计任务先产出预览和布局，拼接任务读取预览和布局来确认画布、层级、组件、样式和状态。
2. **导入前检查。** 核对同组重名、文件缺失、资源 ID、文案键、九宫边距、节点 ID、父子引用和层序。将缺失项定位到节点 ID 和字段，已有定义的节点继续拼接。
3. **生成与补齐。** 通过选定导入路径生成初稿，或按布局直接创建节点。应用工程组件、字体和锚点，检查遮挡与射线。若任务包含行为接入，按 `actionId` 对接项目已有接口。
4. **截图与交互。** 在参考尺寸截图并与预览叠图。首屏试点可用关键组件矩形边缘偏差 ≤ 2 参考像素作为几何门槛。竖屏示例另测 9:16、9:19.5、3:4，以及一台有刘海或系统手势区的目标设备，横屏项目使用对应横屏比例。检查安全区、最长文案、按钮状态和命中。动态列表额外检查空列表、单条、多条及滚动裁切。
5. **回归与提交。** 改一张图、改一段文案后重复导入，检查节点是否重复、Sprite 是否丢失以及已有引用是否有效。交付 Prefab、关键比例截图和仍待确认的节点清单。

首屏试点可选一个含 **TMP 标题、九宫底板、关闭按钮、动态列表** 的真实弹窗。先用同一设计稿比较 PSD2UGUI_X 的 Prefab 初稿和 PSD2UI 的预检及交付数据。项目 Adapter 可用后，再以同一验收场景比较缺失资源数、误判控件数、需手调节点数、参考尺寸偏差和重导入后的引用丢失数。
