# 游戏界面从设计稿到 Prefab 交付

已有 PNG 切图和布局时，从仓库的 `ui-workflow` 预检样例开始。适用于由 Group、Image、单行文字和 Button 组成的界面，交付目标为 Unity uGUI 或 Cocos Creator 3.8。动态列表、本地化和安全区由宿主项目接入。 [UI Workflow 说明](../ui-workflow/README.md)

## 预检奖励弹窗

在仓库的 `ui-workflow` 目录执行以下命令。带着实际交付稿操作时，把 `$screen` 换成该界面的 `screen.json`。

```powershell
$screen = 'examples/reward/screen.json'
node cli.mjs check $screen
node cli.mjs preview $screen out/reward.html
$packed = node cli.mjs pack $screen out/bundles | ConvertFrom-Json
$bundle = Join-Path $packed.output 'bundle.json'
```

- `check` 应返回 `ok: true`。样例的 `claim_button` 对应 `claim_reward`，`close_button` 对应 `close_dialog`。有文字或按钮状态时，在预览中核对字形和贴图，再点按钮核对动作 ID。
- 单张效果图可按切图计划走 `split`。修改 `layers.pcraft` 后可用 `resync` 重导到新目录。如果变更坐标、字号或控件状态，编辑 `screen.json` 中的布局数据。 [切图计划样例](../ui-workflow/examples/photo-crop/plan.json) · [布局 Schema](../ui-workflow/screen.schema.json)

## 导入引擎并验收

在引擎工程安装相应适配器后执行 `node cli.mjs stage $bundle '<引擎工程根目录>'`，再选中输出目录的 `bundle.json`。

| 目标 | 导入与验收 |
| --- | --- |
| Unity uGUI | 执行 **Tools → UI Workflow → Import Selected Bundle**，再执行 **Verify Selected Bundle**。对照参考稿检查 `unity-preview.png`，从 `unity-verification.json` 逐个核对 `raycastTarget` 和 `clicked: true`。随后在宿主场景验证 Canvas 缩放和业务动作。 [Unity 验证实现](../ui-workflow/adapters/unity/UIWorkflow/Editor/UiWorkflowVerify.cs) |
| Cocos Creator | 启用扩展后执行 **扩展 → UI Workflow → Import Selected Bundle**。将 Prefab 挂到项目 Canvas，实例化后将 Bundle 的 `actions` 传给 `bindUiWorkflowActions`。在场景中点每个按钮核对动作，解除绑定时调用返回的清理函数。 [Cocos 动作绑定](../ui-workflow/adapters/cocos/UiWorkflowActions.ts) |

**改稿后换版本。** 继续使用同一 `screenId` 和稳定的节点 ID。打包后执行 `node cli.mjs diff <旧bundle.json> <新bundle.json>`，核对参考尺寸、节点层序和素材差异。确认新 Prefab 的视觉和动作后，再由宿主页面切换引用。 [差异检查实现](../ui-workflow/src/bundle.mjs)

**项目核验（2026-11-14）。** Node.js 26.13.0 下执行 `check`、`preview`、`pack` 和同版本 `diff` 均通过。`npm test` 的 3 项 CLI 测试通过，覆盖状态和动作映射、重复打包和入库、节点与层序变更。PhotoCraft 测试因本轮未配置 CLI 而跳过。引擎导入与截图的既有验证记录见仓库说明。 [回归测试](../ui-workflow/test/workflow.test.mjs) · [引擎验证记录](../ui-workflow/README.md#验证)
