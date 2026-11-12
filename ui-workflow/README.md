# UI Workflow

把分层美术交付或一张效果图变成可复核的 Unity uGUI 和 Cocos Creator Prefab。使用同一份布局和 PNG 交付两端。AI 负责读图、识别组件意图和拟定切图计划，PhotoCraft 负责图像编辑，CLI 校验资源并生成版本化交付包，引擎适配器创建原生节点与控件。

项目独立实现。借鉴 PSD2UI 的组件语义、稳定资源身份和预检，PSD2UGUI_X 的可编辑控件初稿，以及 PSD2Unity 的透明坐标交付。

## 从设计稿开始

| 设计输入 | 交付方法 |
| --- | --- |
| 单张 PNG 或 JPEG 效果图 | AI 或美术先写切图计划，使用 PhotoCraft 执行裁切、底板挖空或文字区修补，输出分层 `.pcraft`、PNG 和 `screen.json` |
| 已有分层稿、独立切图或 AI 生成的素材 | 保留可编辑源文件，按 `screen.json` 提交 PNG、控件矩形和状态。使用现有组件清单和字体约定确定角色 |

使用 Node.js 22+。效果图切图需要 [PhotoCraft CLI](https://github.com/storytold/photocraft/releases)，已验证 0.22.0。可将 CLI 加入 PATH，或通过 `PHOTOCRAFT_CLI` 指定其路径。

```powershell
# 在 ui-workflow 目录运行。以项目自带的奖励弹窗为例。
$env:PHOTOCRAFT_CLI = 'C:\Tools\PhotoCraft\photocraft-cli.exe'
$split = node cli.mjs split examples/photo-crop/plan.json out/art | ConvertFrom-Json
node cli.mjs check $split.screen
node cli.mjs preview $split.screen out/art-preview.html
$bundle = node cli.mjs pack $split.screen out/bundles | ConvertFrom-Json
node cli.mjs stage (Join-Path $bundle.output 'bundle.json') 'D:\YourGame'
```

`split` 输出到新目录，每次试验可给新路径。独立 PNG 已经齐备时，从 `check` 开始，输入可用 [可编辑文字和按钮状态的样例](examples/reward/screen.json)。所有 CLI 命令返回 JSON，失败时返回非零退出码及节点或资源诊断，方便 AI 定位问题。

## AI 和美术的交付约定

1. **读图和确认结构。** 从参考尺寸出发，列出页面功能区、静态装饰、每个按钮的命中范围和动作 ID。对照项目已有字体、控件、共享图和尺寸。确认哪些文字需要本地化、计数或运行时更新。
2. **写出可检查的切图计划。** 参考 [plan.json](examples/photo-crop/plan.json)。区域以父组左上角为原点。同级矩形切片互不重叠，重叠的视觉元素可用父子嵌套表达，精细边缘或需要独立移动的部件可在 PhotoCraft 中修蒙版后按 PNG 交付。
3. **校对图层与文字。** 对照效果图检查 `layers.pcraft` 的组、留白和透明边缘。静态艺术字可交付 PNG。动态文字在计划中声明 `type: "text"` 并提供实际文字、字号、颜色、对齐。PhotoCraft 可修补原字下方的底图，Unity 以 TMP 生成可编辑文字。复杂遮挡区需要美术校对或补绘。
4. **预检和预览。** 核对 ID、路径、父子层级、前后顺序、状态、九宫边距和按钮动作。浏览器预览可改画布宽高并点击按钮核对动作 ID。其字体与渲染为浏览器预览，最终对照引擎截图。
5. **交付和复验。** 用 `stage` 把同一包交到目标引擎。导入后核对层级、点按、遮挡和默认画面。编辑 `layers.pcraft` 后用 `resync` 重导图片与可编辑文字，再用 `diff` 检查变更，确认新版本后由游戏页面引用。

PhotoCraft 切图计划使用 `screen.json` 的节点字段，由工具为 `image`、`button` 分配当前裁片。组可用 `capture: true` 保留底板并为子切片挖空。默认保留画布剩余区域，`captureBackground: false` 可用来只导出指定区域。分层稿中的切片是嵌入式智能对象，可以单独替换。效果图原本遮住的像素需要另行补绘。

```powershell
node cli.mjs resync out/art/screen.json out/art-revised
node cli.mjs pack out/art-revised/screen.json out/bundles
```

`resync` 从同目录的 `layers.pcraft` 按稳定图层名导出。节点坐标、字号、颜色和控件状态以 `screen.json` 为准，变更这些值时更新布局文件。维持同一裁片的边距约定可以复用九宫配置。

## 布局和资源

机器可读字段见 [Screen Schema](screen.schema.json)，示例见 [奖励弹窗](examples/reward/screen.json)。图层保持后景到前景顺序，子节点使用父节点左上原点、参考像素单位。ID 在屏幕内唯一且跨版本保持稳定。锚点支持左上、右上、居中、底部居中及四边拉伸。

- 图片与按钮都引用 `assets` 内的 PNG。透明资源使用 RGBA，九宫边框以源图像素表示并按 `left/top/right/bottom` 命名。按钮可有按下和禁用图，三态尺寸与边距一致。
- 一行可编辑文字采用项目字体，UI 程序接入正式本地化与字体样式。控件由 `action` 引用项目动作，业务结果由宿主游戏处理。
- 可视节点当前支持 Group、Image、单行 Text 和 Button。列表、复杂输入、多行和富文本、Safe Area 策略由宿主项目扩展。默认根节点保持参考尺寸，可作为项目 Canvas 下的页面内容。
- 版本化目录由清单及 PNG 的内容确定。同一版本重复打包、入库或导入可复用结果。新版本保存为可独立审查的 Prefab，已有版本和业务引用保留，切换版本由页面持有方完成。

```powershell
node cli.mjs diff out/bundles/<旧版本目录>/bundle.json out/bundles/<新版本目录>/bundle.json
```

## Unity uGUI

1. 把 [Unity 适配器](adapters/unity/UIWorkflow/) 复制到项目 `Assets/Tools/UIWorkflow`。项目需要 uGUI 与 TextMesh Pro。包含可编辑文字的界面使用 TMP Settings 中配置的默认字体。
2. 执行 `stage`。在 Project 面板选择 `Assets/UIWorkflow/<版本>/bundle.json`，执行 **Tools → UI Workflow → Import Selected Bundle**。同目录生成可编辑 Prefab。挂到项目现有 Canvas，使用项目 CanvasScaler、安全区和 EventSystem。
3. 用 `UiWorkflowAction.actionId` 获取动作 ID，在 Inspector 或宿主脚本订阅 `invoked`。导入器已将 Button 的 OnClick 接到该事件，装饰图和文字关闭 Raycast Target。
4. 选中同一 Bundle，执行 **Tools → UI Workflow → Verify Selected Bundle**。输出引擎截图 `unity-preview.png` 与 `unity-verification.json`。报告逐个检查按钮中心的 UI 射线命中和动作派发，可定位被遮挡的按钮。验证在临时场景运行。

Unity 也可批量验收。使用带图形设备的 Unity Editor，以便输出实际截图。

```powershell
Unity.exe -batchmode -quit -projectPath "D:\YourGame" `
  -executeMethod UIWorkflow.UiWorkflowVerify.RunBatch `
  -uiBundle Assets/UIWorkflow/<版本>/bundle.json -uiOutput "D:\YourGame\UiVerification"
```

## Cocos Creator 3.8

1. 把 [Creator 扩展](adapters/cocos/ui-workflow/) 复制到项目 `extensions/ui-workflow`，在扩展管理中启用。执行 `stage`，打开一个场景，并选中 `assets/UIWorkflow/<版本>/bundle.json`。
2. 执行 **扩展 → UI Workflow → Import Selected Bundle**。同目录生成 Prefab。放到项目 Canvas 下预览。适配器使用 Sprite、Label、Button 和 Widget，并为九宫 PNG 保存对应 SpriteFrame 边距。项目默认把 PNG 导入为其他类型时，将交付 PNG 的类型设为 Sprite Frame。
3. 将 [动作绑定模块](adapters/cocos/UiWorkflowActions.ts) 放入项目的脚本目录。实例化后将 Bundle 的 `actions` 与节点一起传给 `bindUiWorkflowActions`，由宿主处理动作。解除绑定时调用返回的清理函数。

## 验证

`npm test` 检查动作、层级、状态、版本与重复入库。设置 `PHOTOCRAFT_CLI` 后，同一套测试还会打开 PhotoCraft 输出的分层文件，核对裁片坐标。

已用 Unity 2022.3.28f1 验证生成 Prefab、重复导入、九宫、TMP、按钮状态、截图与点击派发。480 × 640 奖励弹窗从样例效果图切片重建后，PhotoCraft 合成图与 Unity 截图均有 0 个 RGB 像素差异，两处按钮命中并派发动作。已用 Cocos Creator 3.8.8 验证生成和重复导入 Prefab，并用该版本类型声明检查动作绑定模块。目标游戏的业务动作、设备适配和正式验收稿由各自的验收场景验证。
