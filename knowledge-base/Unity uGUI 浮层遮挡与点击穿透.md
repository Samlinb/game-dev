# Unity uGUI 浮层遮挡与点击穿透

适用于 Unity uGUI 的 `Screen Space - Overlay` 界面。Ninja 在 Unity 2021.3.14f1c1 的 Boot 场景中验证了统一 Canvas 排序，用于解决 Tooltip、下拉菜单和弹窗被公共资源栏覆盖，或显示在前面却由后方按钮收到点击的问题。

## 复现与定位

在 Ninja 的训练所点击 Lv.20 里程碑。`Training Milestone Tooltip` 向上展开，与 `Common UI - Top Resources` 重叠。原实现把资源栏放在 `Page` 之后，Tooltip 和透明关闭区都在 `Page` 的子树里。对关闭区调用 `SetAsLastSibling()` 只能改变它在页面内部的顺序，整个页面仍先于资源栏绘制。首页难度菜单有同样的层级关系。 [Unity 2021.3 Canvas 绘制顺序](https://docs.unity3d.com/2021.3/Documentation/Manual/UICanvas.html)

先在 Play Mode 记录浮层和遮挡对象的父节点、所属 Canvas、`overrideSorting` 和 `sortingOrder`。再对重叠点调用 `EventSystem.RaycastAll`，看第一条命中及其所属按钮。画面顺序正确但点击落到后方时，检查浮层 Canvas 的 `GraphicRaycaster`、命中图形的 `raycastTarget`、父级 `CanvasGroup` 和命中矩形。若只在滚动区边缘消失，先查 `Mask` 或 `RectMask2D` 的裁剪范围。 [uGUI 1.0 Graphic Raycaster](https://docs.unity3d.com/Packages/com.unity.ugui@1.0/manual/script-GraphicRaycaster.html)

## 统一排序与接入

在 UI 组合根集中定义层级。Ninja Boot 使用以下顺序，数值由项目的 `PlannedBootstrap.UiLayer` 管理。

| 从后到前 | sortingOrder | 典型内容 |
| --- | ---: | --- |
| 页面 | 100 | 大厅、训练所、战斗 HUD |
| 公共资源栏 | 110 | 体力、金币、钻石、灵玉 |
| 页面浮层 | 120 | 里程碑 Tooltip、首页难度菜单 |
| 模态弹窗 | 130 | 奖励时间轴、物品详情、战斗结算 |
| 弹窗内浮层 | 140 | 奖励时间轴上的 Tooltip |
| 画面黑边 | 150 | 窗口比例不足时限制可见和可点区域 |

1. **浮层保留在所属页面或弹窗的子树。** 把要一起排序的内容与关闭区放在同一个子 Canvas 下。训练所的 Tooltip 容器拉伸到设计容器尺寸，保留原锚点和定位坐标。关闭区再按所属页面或弹窗的安全区域换算边界，覆盖宽屏上超出设计容器的区域。
2. **显示后配置排序。** 先激活浮层容器，再设置子 Canvas 的 `overrideSorting = true` 和项目定义的 `sortingOrder`。在本项目的 Unity 2021.3.14f1c1 实测中，对未激活的新子 Canvas 设置排序覆盖，激活时可能回到父 Canvas 的顺序。接入时通过 `PlannedBootstrap.SetOverlayLayer` 自动按所属页面或弹窗取值。 [Canvas.overrideSorting](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/Canvas-overrideSorting.html)
3. **渲染与输入按相同层级验收。** 交互层配置 `GraphicRaycaster`。需要点任意位置关闭的 Tooltip，可用同层的透明 `Image` 和 `Button` 接收这次点击。需要交互的下拉菜单，把关闭区放在选项后面。浮层隐藏时同步关闭命中区，退出页面时由所属页面回收。
4. **模态弹窗走统一入口。** Ninja 的代码弹窗使用 `CreatePopup`，预制体弹窗使用 `InstantiatePopup`。这样页面浮层打开期间仍能显示更高层的模态弹窗，而弹窗自己的 Tooltip 继续显示在弹窗内容前面。

## 回归验证

- 在 16:9 和带黑边窗口分别打开里程碑 Tooltip，确认重叠位置显示 Tooltip。点击金币按钮所在的屏幕坐标，首次点击应收起 Tooltip，下一次点击才打开金币详情。首页难度菜单也做同样检查，并确认难度选项可点。
- 在页面 Tooltip 显示时打开模态弹窗，确认弹窗按钮先收到点击。再打开全部训练奖励，确认时间轴 Tooltip 显示在弹窗内容前面，收起后可以关闭奖励弹窗。离开训练所后检查旧 Tooltip 与关闭区随页面销毁。
- Ninja 的 `HomePrefabVerification.RunUiLayerChecks()` 在上述两种窗口条件下各通过 21 项检查。16:9 截图确认 Lv.20 Tooltip 覆盖在资源栏前。验证范围为 Unity Editor Play Mode。

## 项目实现

以下链接基于 `game-dev` 与 `ninja` 位于同一父目录的工作区结构。

- [Boot 层级、弹窗入口和关闭区边界换算](../../ninja/unity/Assets/Scripts/App/Planned/PlannedUiLayers.cs)
- [训练所 Tooltip 与关闭区](../../ninja/unity/Assets/Scripts/App/Planned/Views/PlannedTrainingView.cs)
- [首页难度菜单](../../ninja/unity/Assets/Scripts/App/Planned/Views/PlannedHomeView.cs)
- [Boot UI 交互回归](../../ninja/unity/Assets/Scripts/Editor/Planned/HomePrefabVerification.cs)
