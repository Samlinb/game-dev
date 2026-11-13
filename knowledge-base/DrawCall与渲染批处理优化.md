# DrawCall 与渲染批处理优化

> 适用范围：**团结引擎 / Unity 2022.3+ WebGL 构建**导出的**微信 + 抖音小游戏**。
> 核心约束：WebGL 单线程模型 + 移动端 GPU 带宽受限，DrawCall 数量与渲染状态切换是 CPU 端主要瓶颈。
> 互链：DrawCall 查看方法 → [Unity 真机 Profiling 与内存分析](./Unity真机Profiling与内存分析.md) §2.3；Shader 变体裁剪 → [Unity WebGL 小游戏特有技术坑](<./Unity WebGL 小游戏特有技术坑.md>) §4；启动链路里的 DrawCall 开销 → [小游戏启动优化](./小游戏启动优化.md)；UI 合批规则 → [UI 动效选型](./UI动效选型.md) 合批锚点。

---

## 结论表格（先扫一眼）

| 场景 | 推荐方案 | 阈值 / 配置 | 备注 |
|------|---------|------------|------|
| **总 DrawCall 控制** | — | **≤ 200**（大型游戏可放宽至 250） | 微信官方建议；SetPass Call 应明显小于 DrawCall 数 |
| **静态物体合批** | Static Batching（WebGL Player Settings 开启 + 标 Static Editor Flags） | 顶点上限 64K/批，Unity 自动分多批 | URP 下因 GPU Resident Drawer 在 WebGL 不生效，Static Batching **没有冲突** |
| **动态物体合批** | SRP Batcher（URP Asset 默认开启） | — | WebGL2.0 下**只减渲染状态切换，不减 DrawCall 数量**；WebGL1.0 不支持 |
| **大量重复动态物体** | GPU Instancing | 需 WebGL2.0；与 SRP Batcher **互斥**（URP 下要禁用 SRP Batcher 或让 Shader 不兼容） | Shader 需支持 Instancing（内置 Shader 勾选即可，自定义 Shader 要加 `multi_compile_instancing`） |
| **UI 图集** | Sprite Atlas（Editor 静态合图）+ 动态图集（UI 系统自动合） | 图集 ≤ 1024×1024（微信建议）；低端设备动态图集 Max Atlas Size 降到 2048 | 老设备 2048 最大纹理限制，4096 会被静默降采样 |
| **UI Canvas** | 1–3 个 Canvas，动静分离用 Sub Canvas | 相同材质+贴图+Shader 才能合批；Canvas 切换 = 新 DrawCall | Sub Canvas 本身是独立 DrawCall，不要为了动静分离开太多 |
| **OverDraw 治理** | 半透明 UI 叠层≤3 层；全屏遮罩用 Mask 时改用网格化圆替代 | Scene 视图 Overdraw 模式检查；URP Rendering Debugger 有 Overdraw 可视化 | 弹窗+遮罩+面板+文字是 Overdraw 重灾区 |
| **Shader 变体** | 精简 `#pragma multi_compile` / `shader_feature` 数量；不用的 Keywords 删干净 | 变体数 = 各 keyword 组合乘积；10 组 ON/OFF keyword = 1024 变体 | 不同变体**无法合批**，直接拆 DrawCall |
| **纹理压缩** | ASTC（Android+iOS 通用），压缩比 8×8 块优先 | 微信压缩纹理工具支持 ETC2/ASTC/DXT 多格式自适应加载 | Unity WebGL Player Settings 默认 DXT1（桌面），小游戏用 ASTC |
| **WebGL 版本选择** | WebGL2.0（需要 GPU Instancing / SRP Batcher）或 WebGL1.0（材质简单、WebGL2.0 覆盖低时） | WebGL2.0 用户占比低于 1.0；用 WebGL2.0 必然线性颜色空间（Gamma 空间有全屏校正 Pass 开销） | 微信官方建议分别打两个包对比 |

---

## 1. DrawCall 是什么，为什么小游戏里它关键

### 1.1 基本概念

**DrawCall** 是 CPU 向 GPU 发送的一次绘制指令。每个 DrawCall 包含：顶点/索引缓冲区绑定、材质属性上传、Shader 程序切换、渲染状态（深度测试/混合模式等）设置。

**SetPass Call** 是 Unity 的另一个指标——它表示渲染状态切换的次数。SetPass Call ≤ DrawCall：如果多个物体共享同一材质+Shader+渲染状态，Unity 可以把它们合并成一个 SetPass Call 加多个 DrawCall（Static Batching 就是这样）。

### 1.2 为什么小游戏里 DrawCall 尤其关键

| 因素 | 影响 | 来源 |
|------|------|------|
| **WebGL 单线程模型** | JavaScript 主线程同时负责渲染逻辑 + WASM 执行 + DOM 操作，DrawCall 的 CPU 开销直接抢占 WASM 编译和业务逻辑时间 | KB-002 §一（单线程模型）；团结引擎 WebGLPlatform 页实测 CPU 慢 3× |
| **移动端 GPU 带宽受限** | 中低端安卓 GPU 带宽约桌面 1/5–1/10，每个 DrawCall 触发的属性上传都会占用带宽 | 微信 RenderOptimization（核对 2026-04-09） |
| **没有多线程渲染** | WebGL 不支持 Unity 的 Graphics Jobs（DX12/Vulkan 才支持），所有 DrawCall 提交串行 | Unity SRP Batcher 手册标注 "DX12, Vulkan, and compatible console APIs only, if you enable Graphics Jobs" |

### 1.3 DrawCall 在哪里看

- Unity Profiler → Rendering 模块 → Draw Calls / Batches / SetPass Calls 三行直接可见（development 包 + Autoconnect Profiler）
- Frame Debugger：Web 平台**不可用**（KB-001 §2.3）
- Rendering Statistics 窗口：Edit → Project Settings → Graphics → 勾 "Show Stats"，Game 视图右上角会显示 Draw Calls / Batches / Tris / SetPass
- 微信 Unity Profiling 插件 / 抖音 Performance Profiler：真机抓包看 DrawCall 实时值

---

## 2. Unity 合批机制全景

Unity 有四种合批手段，在 WebGL 小游戏下的可用性和收益各不相同。

### 2.1 Static Batching（静态合批）

**原理**：Unity Editor 把场景中标记为 Static（Batching Static）的物体，合并成一个大顶点缓冲区 + 索引缓冲区。每个批次最多 64,000 顶点，Unity 自动分多批。运行时一次 DrawCall 渲染整个批次。

**操作步骤**：
1. 选中场景里永不移动的物体（地形、装饰、静态 UI 背景）
2. Inspector 顶部 Static 下拉 → 勾选 `Batching Static`
3. Edit → Project Settings → Player → WebGL → Other Settings → 勾 **Static Batching**（默认就勾）

**WebGL 小游戏下的限制**：
- 只支持 Mesh Renderer，不支持 Skinned Mesh Renderer（骨骼动画不能静态合批）
- 透明物体因为要 back-to-front 排序，合批效果可能打折扣
- **内存代价**：合批后的顶点缓冲区会常驻内存，大量静态物体可能吃内存

**URP 下的关键结论**：Unity 新文档（6000.x）建议 URP 下 Disable Static Batching，原因是它与 **GPU Resident Drawer / BRG API** 冲突。但 WebGL **不支持 GPU Resident Drawer**（官方明确 "except OpenGL ES"，而 WebGL 基于 OpenGL ES），所以这个冲突在 WebGL 下不存在——**WebGL + URP 下 Static Batching 仍然有效，建议开启**。

**来源**：Unity Manual Static Batching、Unity Choose a draw call optimization method、团结引擎 GPU Resident Drawer 手册（核对 2026-11-12）。

### 2.2 Dynamic Batching（动态合批）

**原理**：Unity 在运行时把小网格（顶点数 ≤ 300 的物体）动态合并成一个大网格再提交 GPU。

**WebGL 小游戏下的限制**：
- **有 SRP（URP/HDRP）时自动禁用**——Unity 认为 SRP Batcher 已经覆盖了动态物体场景
- **无 SRP（Built-in RP）时可用**，但需 Player Settings → Other Settings → 勾 Dynamic Batching
- CPU 开销较大——每帧都要合并网格，WebGL 单线程下可能得不偿失
- 顶点数阈值 300，超过的动态物体不合批

**结论**：用 URP 的话这个完全不用管（自动被覆盖）；Built-in RP 小游戏场景极少，除非是纯 2D 小网格游戏才考虑开。

**来源**：Unity WebGL Player Settings、Unity Draw Call Batching 手册（核对 2026-11-12）。

### 2.3 SRP Batcher（URP/HDRP 专用）

**原理**：SRP Batcher 不是减少 DrawCall 数量，而是减少**每次 DrawCall 之间的渲染状态切换**。传统流程每次切换材质都要：收集属性 → 上传 GPU Constant Buffer → 切换 Shader Program。SRP Batcher 把**材质属性常驻在 GPU 内存**里，每次 DrawCall 只更新 Per-Object 属性（世界矩阵等），省去材质属性重传。

**WebGL 小游戏下的关键细节**：

| 维度 | 结论 | 来源 |
|------|------|------|
| 是否减少 DrawCall 数量 | **不减**——它优化的是 SetPass Call（渲染状态切换次数） | 微信 RenderOptimization："它并不能减少 Draw Call 的数量，它的目的是减少渲染状态的切换"（核对 2026-04-09） |
| WebGL1.0 支持 | **不支持**——需要 Uniform Buffer（WebGL2.0 才有） | Unity Manual SRP Batcher；微信文档 WebGL1.0 vs WebGL2.0 章节 |
| WebGL2.0 默认开启 | **是**——URP Asset 的 SRP Batcher 选项默认勾上 | Unity URP Manual |
| 对 GPU Instancing 的影响 | **互斥**——URP 下用 GPU Instancing 必须 Disable SRP Batcher，或让 Shader 声明 `#pragma exclude_renderers` 不兼容 SRP Batcher | Unity GPU Instancing Manual |

**操作步骤**：
1. 确保用 WebGL2.0（Player Settings → Graphics APIs → 勾 WebGL2.0）
2. Project 窗口选 URP Asset → Inspector → Rendering 区 → 勾 **SRP Batcher**（默认就勾）
3. 确保 Shader 兼容 SRP Batcher：
   - 内置 URP Shader（Lit / SimpleLit / Unlit / Sprite Lit / Sprite Unlit）都兼容
   - 自定义 Shader 要符合 URP 的 Constant Buffer 布局（参考 URP Shader 模板）
   - 不兼容的 Shader 会自动 fallback 到传统路径，不影响其他 Shader 的 SRP Batcher 合批

**什么时候该关 SRP Batcher**：
- 场景里有**大量重复的动态物体**（比如 2D 射击游戏里的子弹、消除游戏里的方块），需要靠 GPU Instancing 减少 DrawCall
- 关了 SRP Batcher 才能让 GPU Instancing 生效

**来源**：Unity Manual SRPBatcher.html、Unity GPUInstancing.html、微信 RenderOptimization（核对 2026-04-09）。

### 2.4 GPU Instancing（GPU 实例化）

**原理**：把相同 Mesh + 相同 Material 的多个物体，用**一次 DrawCall** 渲染。每个实例可以有独立属性（颜色、缩放等），通过 Per-Instance Data Buffer 上传。

**WebGL 小游戏下的限制**：

| 维度 | 结论 | 来源 |
|------|------|------|
| WebGL1.0 支持 | **不支持**——需要 Uniform Buffer + Instanced Drawing（WebGL2.0 才有） | Unity Manual GPU Instancing |
| WebGL2.0 支持 | **支持** | 微信 RenderOptimization |
| URP 下是否与 SRP Batcher 兼容 | **不兼容**——必须二选一 | Unity GPU Instancing Manual |
| 性能收益 | 移动端 > 桌面（桌面 DrawCall 开销小）；但每帧最多约 256–512 实例收益最大 | Unity Manual |
| Skinned Mesh Renderer | **不支持**（骨骼动画的顶点变换是 per-instance 自定义的，超出 GPU Instancing 范围） | Unity Manual |

**操作步骤**：
1. Player Settings → Graphics APIs → **确保只有 WebGL2.0**（不能同时有 WebGL1.0 fallback）
2. 如果用 URP，**Disable SRP Batcher**（URP Asset Inspector → Rendering → 取消 SRP Batcher）
3. Material Inspector → Advanced → 勾 **Enable GPU Instancing**（内置 Shader）
4. 自定义 Shader 要加：
   ```hlsl
   #pragma multi_compile_instancing
   // 顶点 Shader 里：
   #include "UnityInstancing.cginc"
   ```

**来源**：Unity Manual GPUInstancing.html、Unity GPUInstancing-landing.html（核对 2026-11-12）。

---

## 3. 图集策略

### 3.1 手动合图集 vs Sprite Atlas vs 动态图集

| 方案 | 本质 | DrawCall 收益 | 适用场景 | 代价 |
|------|------|--------------|---------|------|
| **手动合图集**（TexturePacker / PS 手动拼） | 美术手动把多张图拼成 1 张大图集 + 导出 UV 数据 | 完全可控，相同图集的图共享一个 Material + 一个 DrawCall | 需要精确控制图集结构（像素对齐、过滤模式分区） | 美术工作量大；新增图片要重新打包 |
| **Sprite Atlas**（Unity 内置，Editor 静态合图） | Editor 把指定文件夹的 Sprite 自动打包成 2D Array + 生成 Atlas Material | 与手动合图集等价；自动处理 Sprite 拆分后 UV 偏移 | 大多数 2D 游戏的标准做法 | 打包时间；Build Player 时额外 Atlas 构建 |
| **动态图集**（Dynamic Atlas / UI 系统自动合图） | 运行时把 UI Sprite 临时打包到大图集 | 仅对 UI 元素生效；临时合并后共享 DrawCall | UI 弹窗、角色头像等运行时按需显示的图 | CPU 合批开销；低端设备可能卡顿 |

### 3.2 Sprite Atlas 配置指南

**创建步骤**：
1. Project 窗口右键 → Create → 2D → Sprite Atlas
2. 选中 Sprite Atlas → Inspector → Add to Include in Build 列表（把要合的 Sprite 文件夹拖进去）
3. Texture Type 必须是 Sprite (2D and UI)
4. 点 **Pack Preview** 看打包结果，调整 Max Size / Padding / Filter Mode

**关键参数**：

| 参数 | 小游戏推荐值 | 原因 |
|------|------------|------|
| Max Size | **1024**（微信建议），最多 2048（低端设备上限） | 4096 在老设备上会被静默降采样，画质损失无警告；图集越小内存越少 |
| Padding | 4–8 pixels | 防止采样 Bleeding（UV 边界纹理泄漏） |
| Filter Mode | Point（像素风）或 Bilinear（普通） | 混合两种需要不同图集 |
| Compression | ASTC 6×6 或 8×8（微信压缩纹理工具） | 减少 GPU 内存占用 |

### 3.3 动态图集（UI 系统）

Unity UI 系统有内置的动态图集机制（UGUI Dynamic Atlas），把 Canvas 里的 Sprite 临时打包到大图集来减少 DrawCall。

**配置位置**：
- Unity UI Toolkit：UI Toolkit Settings → Panel Text Settings → Dynamic Atlas Settings
- Unity uGUI：通过 `CanvasRenderer` 的 `cullTransparentMesh` 配合手动配置；或用第三方 Dynamic Atlas 工具

**动态图集 Max Size**：默认 4096。低端设备建议设为 **2048**（Texture2D 构造函数里限制，创建大纹理可能失败或被降采样）。

**来源**：Unity Manual class-SpriteAtlas.html（核对 2026-11-12）、微信 RenderOptimization（核对 2026-04-09）、Unity Manual Control textures of the dynamic atlas（核对 2026-11-12）。

---

## 4. OverDraw 治理

### 4.1 什么是 OverDraw

**OverDraw** 是 GPU 对同一屏幕像素执行多次 Fragment Shader 的情况。半透明物体叠加是最大重灾区——透明物从前往后画，每个半透明层都会覆盖底下的像素，GPU 白白跑 N 次 Fragment Shader。

小游戏场景常见的 OverDraw 重灾区：

| 场景 | OverDraw 层数 | 原因 |
|------|-------------|------|
| 弹窗 + 全屏半透明遮罩 + 面板 + 标题文字 | 4–6 层 | 每层都是半透明，无 Early-Z 机会 |
| 战斗特效叠加（技能特效 + 子弹 + 爆炸粒子） | 5–10 层 | 大粒子系统大量半透明 |
| UI 头像圆形 Mask | 3–4 层 | Mask 组件 + 圆形占位图 + 头像图 |

### 4.2 怎么测 OverDraw

**Editor 可视化**：
1. Scene 视图左上角 → 绘制模式下拉 → **Overdraw**
2. 画面颜色越暖（红/橙），OverDraw 越严重；蓝色 = 单次绘制
3. URP Rendering Debugger（Window → Analysis → Rendering Debugger）→ Rendering → Overdraw

**URP Rendering Debugger 额外功能**：Play Mode 下可以在 Game 视图叠加 Overdraw 视图。

### 4.3 治理手段

| 手段 | 效果 | 代价 | 适用 |
|------|------|------|------|
| **UI 叠层≤3 层** | 弹窗/遮罩/面板尽量三层内合并 | 设计约束 | 所有游戏 |
| **Mask 组件替换** | 用网格化圆形替代 Mask 组件（比如头像） | 需要预制带 Alpha 的圆形 Sprite | UI 头像、圆形裁剪 |
| **禁用 UI 后处理** | URP Asset → Renderer Data → Post Processing 关闭 | 无 | WebGL 小游戏默认不建议后处理（微信 RenderOptimization） |
| **半透明物体 ZWrite** | 透明 Shader 加 `ZWrite On`（谨慎用） | 可能导致透明物体排序错乱 | 独立半透明物体（非叠加场景） |
| **特效分批显示** | 大特效拆成 2–3 帧释放 | 玩家体验微调 | 战斗高潮特效 |
| **Early-Z 利用** | 不透明物体先画（Queue = Geometry +0），让 Fragment Shader 被 Z-Buffer 剔除 | 渲染顺序约束 | 有大量不透明物体的 3D 场景 |

**微信官方特别警告**：避免大量大屏幕半透明特效重叠导致 Overdraw（尤其是战斗释放技能时）。

**来源**：微信 RenderOptimization（核对 2026-04-09）、Unity URP Rendering Debugger（核对 2026-11-12）。

---

## 5. Shader 对 DrawCall 的影响

### 5.1 Shader 变体 = DrawCall 拆分

Unity Shader 里的 `#pragma multi_compile` 和 `#pragma shader_feature` 会产生**不同的 Shader 变体**。每个变体需要单独编译 Shader Program，运行时不同变体的物体**无法合批**。

**变体数计算公式**：`总变体数 = ∏(各组 keyword 的组合数)`

例如：
```hlsl
// 每组是 2 个 keyword（ON/OFF）
#pragma multi_compile _ FOG_ON FOG_OFF        // 2 个组合
#pragma multi_compile _ SHADOW_ON SHADOW_OFF  // 2 个组合
#pragma multi_compile _ SPECULAR_ON SPECULAR_OFF  // 2 个组合
// 总变体数 = 2 × 2 × 2 = 8
```

如果有 **10 组 ON/OFF keyword**，变体数 = `2^10 = 1024`。这就是 Shader 变体裁剪要控制的范围。

### 5.2 keyword 类型选择

| 类型 | 行为 | 适用 |
|------|------|------|
| `#pragma shader_feature` | Unity **自动裁剪**没有用到的变体（Build 时扫描材质引用了哪些 keyword） | 开关特性（比如要不要阴影） |
| `#pragma multi_compile` | Unity **编译所有变体**，Build 时不裁剪 | 必须在运行时动态切换的 keyword |
| `#pragma multi_compile_local` / `shader_feature_local` | 限定在单个 Pass 内，不污染其他 Pass | 局部特性开关 |

**结论**：能用 `shader_feature` 就不要用 `multi_compile`；不用的 keyword 组直接删掉。

### 5.3 其他 Shader 因素

| 因素 | 对 DrawCall 的影响 | 应对 |
|------|-------------------|------|
| **Shader 类型不同**（Standard vs URP Unlit） | 不同 Shader 必然不同 DrawCall | 尽量统一 Shader |
| **Shader Pass 数量** | 多 Pass Shader 每个 Pass 一个 DrawCall | 减少 Pass 数（URP Unlit 只有 1 个 Pass） |
| **Stencil 操作** | 额外 API 调用开销（iOS 15.4 已知有渲染异常） | 不需要遮罩效果时注释掉 Shader 里的 Stencil 代码 |
| **浮点精度** | 高浮点精度（float）开销大，尽量用半精度（half） | 颜色/法线用 half，坐标/UV 用 half 可能需验证 |

**来源**：Unity Manual shader-variants.html（核对 2026-11-12）、微信 RenderOptimization §材质和 Shader（核对 2026-04-09）、KB-014 §4。

---

## 6. 纹理压缩与 GPU 带宽

纹理压缩不直接减少 DrawCall，但通过**减少 GPU 内存占用 + 减少带宽占用**间接提升渲染性能。在移动端带宽受限环境下，压缩纹理几乎是必选项。

### 6.1 小游戏平台压缩格式覆盖

| 平台 | 推荐格式 | 备选格式 | 不支持的格式 | 来源 |
|------|---------|---------|-------------|------|
| **微信小游戏 Android** | **ASTC**（最近 3-4 年机型全覆盖） | ETC2（千元级安卓解码快 10-15%） | DXT/PVRTC（移动端基本不支持） | 微信 UnityVersion（核对 2026-04-09） |
| **微信小游戏 iOS** | **ASTC** | — | DXT | 同前；Apple A7+ 支持 ASTC（2013 起） |
| **抖音小游戏 Android** | **ASTC** | ETC2 | 同微信 | 抖音 Unity 小游戏同架构 |
| **抖音小游戏 iOS** | **ASTC** | — | 同微信 | 同微信 |

**Unity WebGL Player Settings 默认值**：DXT1（`Edit → Project Settings → Player → WebGL → Other Settings → Texture Compression`）——这是桌面浏览器的默认格式，**小游戏下要手动改 ASTC**。

### 6.2 微信压缩纹理工具 vs Unity 自带

| 维度 | Unity 自带压缩纹理 | 微信小游戏压缩纹理工具 |
|------|-------------------|---------------------|
| 版本支持 | Unity 2019 部分 / 2020 部分 / 2021+ 全支持 | Unity 2019/2020/2021 均支持 |
| 多格式自适应 | 运行时固定用 Editor 选的格式 | 同一纹理生成 ASTC/ETC2/DXT 多版本，运行时按设备 GPU 选择 |
| 包体影响 | 纹理留在 AB 里 | 纹理从 AB 剥离，AB 变小（首包更快） |
| PC 端兼容 | Unity 自带 ASTC 但 PC 浏览器不支持，自动解压为 RGBA（内存翻倍） | 同样 ASTC 解压问题，但纹理在 AB 外，不阻塞主资源加载 |

**微信官方建议**：Unity 版本 ≥ 2021 时，优先用引擎自带 ASTC 压缩；需要多格式自适应或想减 AB 体积时用微信压缩纹理工具。

### 6.3 压缩块大小选择

ASTC 压缩块大小从 4×4（最小压缩比，16bpp → 256KB/1024×1024）到 12×12（最大压缩比，3.56bpp → 56KB/1024×1024）。

**小游戏推荐 8×8 块**：压缩比大（~2.89bpp → 约 49KB/1024×1024），画质下降在大多数场景下不可感知。只有高清 UI 图标/按钮才用 4×4 块。

**来源**：微信 RenderOptimization §纹理设置（核对 2026-04-09）、微信 CompressedTexture（核对 2026-11-12）、Unity Manual texture-formats-reference.html（核对 2026-11-12）、Unity 开发者社区《ASTC 与 ETC2 的 WebGL 压缩技术深度解析》。

---

## 7. Canvas 合批规则与 UI 优化

虽然不是 3D 渲染的核心，但 uGUI 面板常常是小游戏 DrawCall 数量的**最大单一来源**（2D 小游戏首屏 UI 可能占 50%+ DrawCall）。

### 7.1 uGUI 合批条件

同一 Canvas 下的 UI 元素满足以下条件才能合批：
1. **同一个 Canvas**（跨 Canvas 必须独立 DrawCall）
2. **相同 Material**（Material 包括 Shader + 贴图 + 参数）
3. **相同贴图**（不同 Sprite Atlas Material 不同）
4. **Depth 连续**（中间不能插不共享材质的元素，否则断批次）

### 7.2 Sub Canvas 策略

**Sub Canvas 的作用**：把频繁变化的 UI 元素（比如滚动列表、闪烁特效）隔离到独立 Canvas，避免触发父 Canvas 的 Rebuild/Rebatch。

**Sub Canvas 的代价**：每个 Sub Canvas **至少 1 个额外 DrawCall**（独立 Canvas 不能与父 Canvas 合批）。

**建议**：
- UI 控制在 **1–3 个 Canvas** 总量
- 只有确实需要动静分离时才加 Sub Canvas（比如大型滚动列表）
- 不要为了"优化"把每个弹窗都做成 Sub Canvas——DrawCall 涨得更快

### 7.3 Canvas 拆分实战

| 场景 | Canvas 数量 | 划分方式 |
|------|------------|---------|
| 简单主菜单（背景 + 按钮） | 1 | 一个 Canvas 搞定 |
| 带滚动列表的商店界面 | 2 | 主 Canvas（静态元素）+ Sub Canvas（滚动列表） |
| 战斗界面（血条+技能+飘字） | 3 | 主 Canvas + 飘字 Sub Canvas + 血条 Sub Canvas |
| 弹窗叠加 | 独立 Canvas | 每个弹窗独立 Canvas，关闭后销毁 |

### 7.4 UI DrawCall 快速排查

1. Unity Profiler → UI 模块 → Canvas 行看 Draw Calls 列
2. Frame Debugger（Editor Play Mode 可用，真机不可用）看合批断点
3. 检查 Canvas 数量 → Material 数量 → Sprite Atlas 引用数量，三者直接决定 DrawCall

**来源**：Unity Manual uGUI Optimization、Unity Learn Optimizing Unity UI（核对 2026-11-12）、KB-001 §2.3（真机 Frame Debugger 不可用）。

---

## 8. 模板工程对照

Glob 扫描 `projects/MiniGameTemplate/**` 未发现以下配置文件：

| 应检查的配置 | 模板工程现状 | 影响 |
|-------------|-------------|------|
| GraphicsSettings.asset | **不存在**（无 ProjectSettings 目录） | 需首次在 Unity Editor 打开模板时创建；创建后确认 Static Batching / Dynamic Batching 默认值 |
| Sprite Atlas 文件 | **不存在** | 模板是代码骨架，无美术资源；首次合图集需手动创建 Sprite Atlas |
| EditorBuildSettings.asset | **不存在** | 同上；无场景列表 |
| Canvas 配置 | 无 Prefab 对应 | 模板无 UI Prefab，Canvas 从空场景开始建 |

**模板工程当前未配置任何合批相关 ProjectSettings**。首次在 Editor 打开模板后，Unity 会创建默认的 ProjectSettings.asset（包含 GraphicsSettings、EditorBuildSettings）。此时需要：
1. Player Settings → WebGL → Other Settings → 确认 Static Batching 勾选
2. 如果用 URP，创建 URP Asset 并确认 SRP Batcher 勾选
3. 创建 Sprite Atlas 并指定图集策略

---

## 来源汇总

| 来源 | 类型 | 核对日期 | 覆盖章节 |
|------|------|---------|---------|
| Unity Manual - Draw Call Batching | 官方文档 | 2026-11-12 | §2.1 Static Batching |
| Unity Manual - SRP Batcher | 官方文档 | 2026-11-12 | §2.3 SRP Batcher |
| Unity Manual - GPU Instancing | 官方文档 | 2026-11-12 | §2.4 GPU Instancing |
| Unity Manual - Sprite Atlas | 官方文档 | 2026-11-12 | §3.2 Sprite Atlas |
| Unity Manual - Shader Variants | 官方文档 | 2026-11-12 | §5.1-5.2 Shader 变体 |
| Unity Manual - Texture Formats Reference | 官方文档 | 2026-11-12 | §6.1 压缩格式覆盖 |
| Unity Manual - Choose a draw call optimization method | 官方文档 | 2026-11-12 | §2 合批全景 |
| Unity Manual - WebGL Player Settings | 官方文档 | 2026-11-12 | §2 Dynamic Batching、WebGL 版本 |
| Unity Manual - GPU Resident Drawer | 官方文档 | 2026-11-12 | §2.1 Static Batching URP 冲突澄清 |
| Unity URP Manual - Rendering Debugger | 官方文档 | 2026-11-12 | §4.2 OverDraw 测量 |
| Unity Manual - Control textures of the dynamic atlas | 官方文档 | 2026-11-12 | §3.3 动态图集大小 |
| 团结引擎 Manual - GPU Resident Drawer | 官方文档 | 2026-11-12 | §2.1 WebGL 不支持 GPU Resident Drawer |
| 微信开放文档 - RenderOptimization | 官方文档（最后编辑 2026-04-09） | 2026-04-09 + 2026-11-12 复核 | §1.2、§2 全景、§3.2 图集大小、§4.1-4.3 OverDraw、§5.3 Shader、§6 压缩格式 |
| 微信开放文档 - CompressedTexture | 官方文档 | 2026-04-09 + 2026-11-12 复核 | §6.2 微信压缩纹理工具 vs Unity 自带 |
| 微信开放文档 - 推荐引擎版本 | 官方文档 | 2026-04-09 | §6.1 小游戏压缩格式覆盖 |
| Unity 开发者社区 - ASTC 与 ETC2 深度解析 | 技术专栏 | 2026-11-12 | §6.3 块大小选择 |
| 模板工程 Glob 扫描 | 项目实测 | 2026-11-12 | §8 模板工程对照 |

## 相关条目

- [小游戏分辨率适配与降级策略](./小游戏分辨率适配与降级策略.md) — 低端机降级手段（帧率/分辨率/Shader 裁剪）、Canvas Scaler 选型。
- [Unity 真机 Profiling 与内存分析](./Unity真机Profiling与内存分析.md) — DrawCall 测量工具、GPU 耗时定位。
- [小游戏启动优化](./小游戏启动优化.md) — Shader 变体裁剪与首包精简。
- [Unity 小游戏特效实现与性能](./Unity小游戏特效实现与性能.md) — 粒子/拖尾/发光/溶解的实现选型与特效专属预算（OverDraw/变体通用规则在本条目）。
