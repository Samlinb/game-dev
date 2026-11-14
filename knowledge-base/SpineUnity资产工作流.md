# Spine Unity 资产工作流与缓存机制

适用：spine-unity 4.2 / 4.3 运行时，团结引擎 / Unity 2022.3（兼容范围 2017.1–6000.3），微信 + 抖音小游戏 WebGL。
触发：导入 Spine 资产到 Unity、理解 SkeletonDataAsset 结构、判断能否像 Cocos 一样共享缓存。
核对日期：2026-11-12（Spine 官方 Main Components + Assets 文档、GitHub spine-runtimes 4.2 README）；2026-11-13 增补 4.3 缓存/多线程机制（GitHub 4.3 源码 + On-Demand Loading 文档）。
前置：运行时优化见 [Spine 运行时优化](./Spine运行时优化.md)；选型决策见 [UI 动效选型](./UI动效选型.md)。

## 结论（影响行动的部分）

1. **spine-unity 没有 Cocos SHARED_CACHE 的等效机制**。AnimationState 逐实例独立，每个骨架每帧都算骨骼变换。SkeletonDataAsset 是共享只读数据，不是共享计算。
2. **导入是自动的**：.skel.bytes + .atlas.txt + .png 拖入 Assets → 自动生成 _SkeletonData + _Atlas + _Material。
3. **二进制导出（.skel.bytes）是生产首选**：比 .json 小且加载快。扩展名必须含 `.bytes`，纯 `.skel` 无法加载。
4. **版本必须匹配**：Spine 编辑器 4.2.xx → spine-runtimes 4.2 分支。大版本.小版本对齐。
5. **PMA vs Straight Alpha 是导入最大坑**：本项目 Gamma 色彩空间，Spine 默认 PMA 导出可用；但 spine-unity 4.3+ 默认按 Straight 导入，需手动对齐。
6. **uGUI 用 SkeletonGraphic**：单纹理限制、只能用 Spine/SkeletonGraphic* shader。

## 一、与 Cocos 缓存模式的对应关系

来源：[Spine Main Components](https://esotericsoftware.com/spine-unity-main-components)，核对 2026-11-12。官方规定。

| Cocos 模式 | 机制 | spine-unity 等效 | 说明 |
|-----------|------|-----------------|------|
| REALTIME | 每实例独立骨骼计算，支持换装/融合/IK | **默认行为**（SkeletonAnimation） | 完全等效，每实例 AnimationState 独立 |
| SHARED_CACHE | 同骨架同动作共享烘焙结果，查表播放 | **无免费等效** | AnimationState 逐实例，无共享计算模式 |
| PRIVATE_CACHE | 单实例烘焙缓存，不支持换装 | **无直接等效** | spine-unity 始终实时计算 |

**关键差异**：Cocos 的 SHARED_CACHE 能把「N 个同骨架同动作」的 CPU 开销从 N 降到 1（查表）。spine-unity 做不到这一点——每个实例都独立计算。

**Unity 侧替代方案**（按规模）：
- 数十同屏：Freeze + round-robin 分帧（见 [Spine 运行时优化](./Spine运行时优化.md) §1）
- 数百同屏：减骨骼复杂度 + 减数量 + atlas 共享降 DrawCall
- 万级同屏：GPU 骨骼动画商业方案（纹理烘焙，见 [Spine 运行时优化](./Spine运行时优化.md) §4）

## 二、资产导入流程（官方规定）

来源：[Spine Assets 导入文档](https://esotericsoftware.com/spine-unity-assets)，核对 2026-11-12。

### 2.1 标准导入（推荐）

```
1. Spine 编辑器导出：Binary (.skel.bytes) + .atlas.txt + .png
2. 将三个文件拖入 Unity Assets/ 子目录
3. spine-unity 自动检测并生成：
   - xxx_SkeletonData（SkeletonDataAsset）
   - xxx_Atlas（SpineAtlasAsset）
   - xxx_Material（每页纹理一个材质）
4. 拖 _SkeletonData 到 Scene → 自动创建配置好的 GameObject
```

### 2.2 生成资产结构

| 资产后缀 | 来源 | 内容 |
|----------|------|------|
| `_SkeletonData` | .skel.bytes / .json | 骨骼层级 + 槽位 + 动画数据 + 引用 _Atlas |
| `_Atlas` | .atlas.txt | atlas 页信息 + UV 坐标 + 引用材质 |
| `_Material` | 每页 .png | shader + 纹理引用 |

**SkeletonDataAsset 是共享只读数据**：多个实例引用同一个 _SkeletonData，骨骼/动画数据只加载一份。但运行时状态（AnimationState、Skeleton pose）逐实例独立。

### 2.3 二进制 vs JSON

| 格式 | 扩展名 | 优劣 |
|------|--------|------|
| **Binary（推荐）** | `.skel.bytes` | 更小、加载更快、GC 更少 |
| JSON | `.json` | 可读、调试用 |

**注意**：spine-unity 不能加载纯 `.skel` 扩展名，必须含 `.bytes`。atlas 必须是 `.atlas.txt`。

### 2.4 更新资产

重新从 Spine 导出 → 覆盖原文件 → Unity 自动 reimport，所有引用保持不变。若未检测到变化：右键文件夹 → Reimport。

### 2.5 运行时动态创建（不推荐，仅特殊场景）

```csharp
// 仅当无法走 Unity 导入管线时使用
var atlas = SpineAtlasAsset.CreateRuntimeInstance(atlasTxt, textures, materialPropertySource, true);
var skeletonData = SkeletonDataAsset.CreateRuntimeInstance(skeletonBytes, atlas, true);
var go = SkeletonAnimation.NewSkeletonAnimationGameObject(skeletonData);
// uGUI 用：
var gui = SkeletonGraphic.NewSkeletonGraphicGameObject(skeletonData);
```

## 三、PMA vs Straight Alpha（导入最大坑）

来源：[Spine Assets 导入文档](https://esotericsoftware.com/spine-unity-assets)，核对 2026-11-12。官方规定。

### 3.1 选择依据

| 色彩空间 | 推荐工作流 | 原因 |
|----------|-----------|------|
| **Gamma（本项目）** | PMA 或 Straight 均可 | PMA 在 Gamma 下 mipmap 更好、半透明边缘更锐 |
| Linear | **必须 Straight** | PMA 纹理在 Linear 下 Gamma→Linear 转换产生错误边框 |

### 3.2 导出设置（Spine Texture Packer）

| 工作流 | Premultiply alpha | Bleed |
|--------|-------------------|-------|
| PMA | ✅ 开 | 关 |
| Straight | 关 | ✅ 开 |

### 3.3 Unity 导入设置匹配

| 设置项 | PMA | Straight |
|--------|-----|----------|
| sRGB (Color Texture) | **关** | **开** |
| Alpha Is Transparency | **关** | **开** |
| 材质 Straight Alpha Texture | **关** | **开** |

**错误症状**：透明区域暗边框、附件周围彩色条纹。

### 3.4 版本默认值变化（重要）

| 版本 | 导入默认 |
|------|----------|
| spine-unity ≤ 4.2 | PMA |
| spine-unity ≥ 4.3 | **Straight** |

**本项目注意**：模板用 Gamma 色彩空间。若用 4.3+ runtime + Spine 默认 PMA 导出，导入后需手动切换纹理设置为 PMA 模式（或改 Spine 导出为 Straight）。Edit → Preferences → Spine → Switch Texture Workflow 一键切换（4.3+）。

## 四、组件选择（4.3 架构）

来源：[Spine Main Components](https://esotericsoftware.com/spine-unity-main-components)，核对 2026-11-12。官方规定。

### 4.1 渲染 + 动画分离（4.3 新架构）

| 渲染组件 | 适用 | 限制 |
|----------|------|------|
| **SkeletonRenderer** | 3D/2D 场景（MeshRenderer） | 推荐默认；支持 SpriteMask |
| **SkeletonGraphic** | uGUI Canvas（CanvasRenderer） | 单纹理限制；只能用 Spine/SkeletonGraphic* shader |

| 动画组件 | 适用 | 限制 |
|----------|------|------|
| **SkeletonAnimation** | Spine 原生动画系统 | 推荐；最高自定义度 |
| SkeletonMecanim | Unity Mecanim 状态机 | 过渡不保证与 Spine 预览一致；主线程查询性能差 |

### 4.2 本项目选择

**SkeletonGraphic + SkeletonAnimation**（uGUI 项目）：
- 拖 _SkeletonData 到 Canvas 下 → 选 SkeletonGraphic (UI) 组合
- Shader：Spine/SkeletonGraphic（单纹理）或 Spine/SkeletonGraphic Tint Black（Additive + CanvasGroup）
- 不能用 URP/Lit、Spine/Skeleton 等非 UI shader

### 4.3 SkeletonGraphic 多纹理

默认单纹理限制（CanvasRenderer）。若骨架有多 atlas page：
- 开启 Multiple CanvasRenderers（组件设置）
- 或合并 atlas 为单页（见 [Spine 运行时优化](./Spine运行时优化.md) §2）

## 五、版本兼容与安装

来源：[GitHub spine-runtimes 4.2 README](https://github.com/EsotericSoftware/spine-runtimes/blob/4.2/spine-unity/README.md)，核对 2026-11-12。官方规定。

### 5.1 版本匹配规则

**Spine 编辑器 major.minor = spine-runtimes 分支号**。

| Spine 编辑器 | runtime 分支 | 文档 |
|-------------|-------------|------|
| 4.2.xx | 4.2 | esotericsoftware.com/spine-unity（4.2 版） |
| 4.3.xx | 4.3 | esotericsoftware.com/spine-unity（4.3 版，当前默认） |

### 5.2 Unity 版本兼容

spine-unity 4.2：Unity 2017.1 – 6000.3。本项目 Unity 2022.3 / 团结引擎在范围内。

### 5.3 安装方式

- **unitypackage**（官方推荐）：从 esotericsoftware.com/spine-unity-download/ 下载对应版本 → Import Package
- 无 UPM git URL（README 未提供）
- 示例场景：`Assets/Spine Examples/Scenes`

### 5.4 许可证

- 评估/学习免费。
- 集成发布：需 Spine 许可证（集成时持有，或最终用户各自持有）。
- 美术用 Spine 编辑器制作资产本身需要编辑器许可证。

### 5.5 纹理导入注意

- Texture Type 必须是 **Texture**（非 Sprite）。Unity 2D 模板默认 Sprite 会导致 Spine/Skeleton shader 渲染异常。
- 自动导入会尝试设置，但纹理更新时可能被覆盖。
- 需要 repack 时：Read/Write Enabled + Compression None + NPOT None。

## 六、纹理导入设置清单（本项目 Gamma + PMA）

| 设置 | 值 | 原因 |
|------|-----|------|
| Texture Type | Texture（非 Sprite） | Spine shader 要求 |
| sRGB | 关（PMA） | 避免 mipmap 白边 |
| Alpha Is Transparency | 关（PMA） | PMA 已预乘 |
| Generate Mip Maps | 按需（2D 通常关） | 小游戏省内存 |
| Max Size | ≤ 1024（抖音建议） | 见启动优化条目 |
| Compression | ASTC（移动端） | 见 DrawCall 条目 |
| Read/Write | 关（除非需 runtime repack） | 省内存 |
| 材质 Straight Alpha Texture | 关（PMA） | 匹配导出 |

## 七、spine-unity 4.3 的缓存与多线程机制

来源：[spine-unity On-Demand Loading 文档](https://esotericsoftware.com/spine-unity-on-demand-loading)（4.3 版正文）、GitHub [spine-runtimes 4.3 分支源码](https://github.com/EsotericSoftware/spine-runtimes/tree/4.3/spine-unity/Assets/Spine/Runtime/spine-unity/Components)（SkeletonRenderer.cs / SkeletonRenderer.Common.cs / SkeletonAnimation.cs / SkeletonAnimationBase.cs），核对 2026-11-13。官方规定 + 源码考证。

**结论：4.3 没有新增「共享计算缓存」（仍无 SHARED_CACHE 等效），新增的是「纹理按需加载」与「多线程更新」两项，且多线程在 WebGL 被编译排除。**

| 机制 | 4.3 状态 | 对项目的含义 |
|------|----------|--------------|
| 共享骨骼计算缓存 | ❌ 仍无。SkeletonData 由引用同一 SkeletonDataAsset 的所有骨架共享（只读数据），AnimationState 仍逐实例 | 同骨架同动作仍是 N 份 CPU 计算，继续用 Freeze/分帧/减数量（见 [Spine 运行时优化](./Spine运行时优化.md)） |
| atlas 纹理按需加载 | ✅ 新增扩展 UPM 包：`com.esotericsoftware.spine.on-demand-loading`（基础设施）+ `com.esotericsoftware.spine.addressables`（基于 Addressables 的现成实现） | 默认 SkeletonDataAsset 间接引用全部 atlas 纹理，加载骨架即全部加载；多皮肤/高清图集可用低分辨率占位 + 按需加载高清。依赖 Addressables，与模板现有 ResManager 的适配未验证 |
| 多线程骨骼/网格更新 | ✅ 4.3 新增 `SkeletonUpdateSystem`，但源码 `#if !SPINE_DISABLE_THREADING && !UNITY_WEBGL` → **WebGL/小游戏不编译该路径**，仍为主线程更新 | 微信/抖音小游戏不要指望多线程更新省 CPU；优化只能从数量、复杂度、冻结、帧率入手 |
| 组件自动升级 | ✅ `AUTO_UPGRADE_TO_43_COMPONENTS`：编辑器内旧组件在 Awake 时自动升级到 4.3 组件结构 | 从 4.2 升级到 4.3 时先备份工程；自动升级会在 Editor 内改写组件数据 |

**按需加载接入要点**（官方文档步骤，核对 2026-11-13）：

1. 先装 `com.esotericsoftware.spine.on-demand-loading`，再装 `com.esotericsoftware.spine.addressables`（后者依赖前者）。
2. 把原高清材质纹理标记为 Addressable。
3. 选中 `SpineAtlasAsset`，右键 Inspector 标题 → `Add Addressables Loader`，生成 `AddressableTextureLoader` 资产。
4. 正常构建 Addressables 内容；构建 Player 时自动替换为低分辨率占位纹理，运行时按需加载高清。
5. 编辑器内默认看不到占位效果；要预览时选中 loader 资产执行 `Testing → Assign Placeholders`。

附加纹理（法线贴图等）需把 shader 属性名（如 `_BumpMap`）加入 `Additional Texture Properties`；`SkeletonGraphic` 只用主纹理，附加纹理不适用。

## 八、待核对项

| 内容 | 来源 | 恢复条件 |
|------|------|----------|
| ~~SkeletonGraphic freeze 属性具体 API 名~~ | **已解决（round 34 官方文档，round 36 审计确认）** | `freeze` bool 属性（官方 main-components：「set true 不再更新」），仅 SkeletonGraphic；见 [Spine 运行时优化](./Spine运行时优化.md) §1.1 |
| 团结引擎对 spine-unity 的兼容适配 | 团结文档 / 实测 | 装包后验证 |
| on-demand loading 扩展包与模板 ResManager 的适配成本 | 上述 4.3 文档 + 模板代码 | 装包后评估是否引入 Addressables（未验证） |

## 相关条目

- [Spine 运行时优化](./Spine运行时优化.md) — Freeze/分帧/atlas 共享/对象池/GPU 动画（决定用 Spine 后怎么优化）
- [UI 动效选型](./UI动效选型.md) — 选型决策 + Cocos 三种缓存模式详解（对比参考）
- [DrawCall 与渲染批处理优化](./DrawCall与渲染批处理优化.md) — 纹理压缩格式、Canvas 合批
- [小游戏启动优化](./小游戏启动优化.md) — 首包精简（Spine 资产走 AB 不进首包）
- [Unity 小游戏项目架构](<./Unity 小游戏项目架构.md>) — 资源管理 IAssetProvider、AB 加载