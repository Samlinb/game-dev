# Unity 小游戏特效实现与性能（粒子 / Shader / 拖尾 / 后处理）

> 适用范围：**团结引擎（2022.3 基线）/ Unity 2022.3+ WebGL 构建**导出的**微信 + 抖音小游戏**，2D 休闲为主。
> 触发条件：要实现粒子爆炸/技能特效、拖尾、发光、溶解等视觉效果，或给特效定性能预算、排查特效引起的卡顿。
> 核对日期：2026-11-13（来源分级见文末汇总表）。
> 边界：本条目管「特效用什么做、怎么做、预算多少」；DrawCall/OverDraw/变体/纹理的通用治理在 [DrawCall 与渲染批处理优化](./DrawCall与渲染批处理优化.md)，WebGL1/2 硬差异在 [Unity WebGL 小游戏特有技术坑](<./Unity WebGL 小游戏特有技术坑.md>) §4，简单 UI 动效（按钮/飘字）先查 [UI 动效选型](./UI动效选型.md)——能用节点动画/Tween 的不要用粒子或 Spine。

---

## 结论速览

| 要做什么 | 结论 | 依据 |
|---------|------|------|
| 粒子特效（爆炸/技能/氛围） | **Built-in Particle System，唯一安全选择** | VFX Graph 需 compute shader 且官方明确不支持 OpenGL ES，WebGL 不可用（§1） |
| VFX Graph 资产想上小游戏 | **不能直接用**，需用 Particle System 重做 | 同上；URP 下 VFX Graph 还只支持 unlit 粒子、不支持 gamma 空间 |
| 运动拖尾（刀光/子弹/冲刺） | **TrailRenderer**（自动记录轨迹）；手动指定点用 LineRenderer | 两者同一渲染算法，URP/内置管线都支持（§3） |
| 发光（不用后处理） | **加法混合（Additive）Sprite/材质**，或 Rim 边缘发光 Shader | 零全屏开销；Bloom 的替代方案（§4/§5） |
| 溶解/消失 | **Alpha Clip（噪声图 + _Cutoff 动画）**，硬边但无透明排序问题 | 普通片元 Shader，无 compute 依赖（§4） |
| Bloom 后处理 | **WebGL 2.0 才可用；默认关闭**，仅高档机按需开 | WebGL 1.0 URP 后处理编译失败（KB-014 §4）；微信建议默认不用后处理（§5） |
| 同屏粒子数上限 | **无官方公开固定数值**，按目标低端机压测定（模板帧率预算 30fps）；官方 Particle Budget 方案按时间预算（ms）调度而非固定粒子数（§2.4） | 微信 ParticleBudget（镜像已核实 §2.4）；UWA 定性建议（§2/§6） |
| 特效卡顿第一排查项 | **半透明 OverDraw**（粒子+UI 是主要来源），其次 CPU 粒子模拟 | UWA 小游戏简谱 + 微信 RenderOptimization（§6/§7） |

---

## 1. 选型：Particle System vs VFX Graph（为什么 VFX Graph 不可用）

### 1.1 证据链（官方规定，三层交叉）

1. **VFX Graph 依赖 compute shader**：「Visual Effect graph uses on compute Shaders to simulate effects.」（原文含笔误）且硬件门槛为「Support for compute shaders.」（`SystemInfo.supportsComputeShaders` 为 true）+「Support for Shader Storage Buffer Objects (SSBOs).」（`SystemInfo.maxComputeBufferInputsVertex` > 0）。
   来源：[VFX Graph 14.0 GettingStarted](https://docs.unity3d.com/Packages/com.unity.visualeffectgraph@14.0/manual/GettingStarted.html)、[VFX Graph 14.0 Requirements and compatibility](https://docs.unity3d.com/Packages/com.unity.visualeffectgraph@14.0/manual/System-Requirements.html)（14.x 对应 Unity 2022.2+，与团结 2022.3 基线匹配；核对 2026-11-13）。
2. **VFX Graph 官方明确不支持 OpenGL ES**：「The Visual Effect Graph does not support Open GL ES.」（原文拼写如此）。WebGL 1.0/2.0 分别基于 OpenGL ES 2.0/3.0 → **WebGL 全系不可用**。
   来源：同上 System-Requirements 页（核对 2026-11-13）。
3. **WebGL2 本身无 compute**：Unity 官方 WebGL2/WebGPU 对比表「Compute Shaders — WebGL2: Not natively supported. WebGPU: Supported with limitations」。
   来源：[Compare WebGL2 and WebGPU](https://docs.unity.com/en-us/engine/6000.6/manual/platform-specific/webgl/develop/graphics/web-graphics-apis-intro)（Unity 6.6 手册；核对 2026-11-13）。
4. **团结引擎平台级确认**：「例如Compute Shader，也无法使用。」「一些依赖于Compute Shader的后处理，GPU Skinning等也无法使用。」且团结 WebGL「只支持WebGL 1和WebGL 2」。
   来源：[团结引擎 WebGL 平台特征简介](https://docs.unity.cn/cn/tuanjiemanual/Manual/WebGLPlatform.html)（团结 1.3 / 2022.3 基线，页面发布 2025-07-29；核对 2026-11-13）。

**结论**：小游戏（微信/抖音，团结 WebGL 导出）**只能用 Built-in Particle System**。VFX Graph 的「百万粒子 GPU 模拟」能力在本项目平台不存在；美术若交付 .vfx 资产，必须用 Particle System 重做，不要尝试移植。

### 1.2 两套方案对比（官方表格摘录）

| 维度 | Built-in Particle System | Visual Effect Graph |
|------|--------------------------|---------------------|
| 管线兼容 | Built-in / URP / HDRP | 仅 URP / HDRP |
| 可行粒子量级 | **Thousands（数千）** | Millions（百万，GPU 模拟） |
| 创作方式 | Inspector 模块化（每个模块一种预定义行为） | 节点图编辑器 |
| C# 交互 | 逐粒子读写、碰撞事件、播放控制 API | 暴露图属性 + Event Interface |
| WebGL 小游戏 | **可用（CPU 模拟）** | **不可用**（§1.1） |

来源：[Choosing your particle system solution](https://docs.unity.com/en-us/engine/6000.6/manual/visual-effects/particle-systems/choosing-your-particle-system)（Unity 6.6 手册；核对 2026-11-13。量级/管线结论与 2022.3 一致，VFX Graph 侧已由 14.0 包文档交叉）。

附加限制（即使未来平台支持 compute 也要记住）：URP 下 VFX Graph「only supports unlit particles」且「doesn't support gamma color space」（[System-Requirements](https://docs.unity3d.com/Packages/com.unity.visualeffectgraph@14.0/manual/System-Requirements.html)，核对 2026-11-13）。

### 1.3 WebGPU 会不会改变结论？

Unity 6.6 上游手册已列 WebGPU（compute 有限支持），但**团结 1.3 手册明确 WebGL 1/2 两种**，未提 WebGPU 后端；微信/抖音小游戏壳当前是否放行 WebGPU 无已核实来源。**判断条件**：在团结引擎版本说明 + 微信/抖音适配文档出现 WebGPU 支持声明之前，一律按「无 compute」做特效方案。→ 待核对项 §9。

---

## 2. Particle System 实现与 CPU 预算

### 2.1 为什么粒子在小游戏里吃 CPU

- Built-in Particle System 是 **CPU 模拟**（C# 可逐粒子读写；Job System 优化页说明模拟数据经 `ParticleSystemJobData` 传给 worker thread）。团结 WebGL 平台页明确：「原本从多线程和SIMD获益的引擎模块，如Skinning，粒子系统，Culling也会变慢。」——WASM 单线程、无 SIMD，粒子模拟无法并行加速。
  来源：[Particle System Job System 集成](https://docs.unity.com/en-us/engine/6000.6/manual/visual-effects/particle-systems/particle-system-job-system-integration)（Unity 6.6 手册）+ [团结 WebGLPlatform](https://docs.unity.cn/cn/tuanjiemanual/Manual/WebGLPlatform.html)（核对 2026-11-13）。
- **推论（判断条件）**：网上「用 C# Job System + Burst 优化粒子」的方案依赖 worker threads，WebGL 单线程下**不适用**；小游戏粒子优化只剩「减数量、减模块、减 overdraw、池化」四条路。
- UWA 定性建议（外部案例，无机型数值）：「粒子数量、发射器复杂度直接影响CPU端计算和内存分配，中低端机上应限制Max Particles并精简粒子层级。」
  来源：[UWA Unity 小游戏优化简谱](https://blog.uwa4d.com/archives/UWA_MiniGame.html) §4.2.3（核对 2026-11-13）。

### 2.2 最小接入（示例代码，模板暂无特效实现）

接入点：特效做成 Prefab，走模板对象池 `Assets/_Project/Scripts/Res/PrefabPool.cs` 获取/归还（池化流程见 [对象池接入与复用](./对象池接入与复用.md) Unity 对照节）。

```csharp
// 示例：一次性爆炸特效，播放完归还对象池
// PrefabPool 真实 API（项目代码核对 2026-11-13）：Instance.Spawn(path,pos,rot) / Instance.Despawn(go)
// key 是资源路径（ResManager 加载路径），不是 Prefab 引用
public void PlayExplosion(Vector3 pos)
{
    var go = PrefabPool.Instance.Spawn("Effects/Explosion", pos, Quaternion.identity);
    if (go == null) return;                         // 加载失败池会 LogError
    var ps = go.GetComponent<ParticleSystem>();
    ps.Play();
    StartCoroutine(ReturnWhenDone(go, ps));
}

private IEnumerator ReturnWhenDone(GameObject go, ParticleSystem ps)
{
    // duration + 最大粒子寿命后判定播放结束（示例计时法；子粒子系统多时按最长子系统的 duration+lifetime）
    yield return new WaitForSeconds(ps.main.duration + ps.main.startLifetime.constantMax);
    PrefabPool.Instance.Despawn(go);                // 归还前确认 Stop Action 不会 Destroy 本体
}
```

池化清理契约（项目代码约定）：特效 Prefab 上挂 `IPoolable` 实现，`OnDespawn` 里自己停协程/退订事件/重置状态，`OnSpawn` 里初始化——池只管存取，清理责任在对象自己（`PrefabPool.cs` 注释原文）。

**可观察完成结果**：特效播放一次后 GameObject 回池（SetActive false），Hierarchy 无残留实例；连续释放 20 次无 GC 尖峰（Profiler CPU 模块看 `ParticleSystem.Update`）。

**Inspector 关键设置（判断条件）**：

| 设置 | 小游戏推荐 | 原因 |
|------|-----------|------|
| Main → Max Particles | 按 §6 压测定上限，单系统从 ≤100 起步 | CPU 模拟成本 ∝ 存活粒子数（UWA 定性） |
| Main → Stop Action | **Disable**（不 Destroy） | 池化复用；Destroy 会破坏池 |
| Renderer → Material | 全项目特效共用 1–2 个加法/半透明材质 | 材质不同 = 不合批（DrawCall 条目 §5） |
| Renderer → Render Mode | Billboard（2D 休闲默认） | Mesh 模式顶点成本更高 |
| 不用的模块 | 全部关闭（Collision/Physics/Force 等） | 每模块都是每帧 CPU 开销 |
| Emission → Rate over Time | 爆发型用 Burst 一次性发射，不持续发射 | 持续发射 = 持续 CPU 成本 |

### 2.3 粒子 vs Spine vs 节点动画怎么选

- 简单 UI 反馈（按钮缩放、飘字、图标闪动）→ 节点动画/Tween，**不用粒子也不用 Spine**（[UI 动效选型](./UI动效选型.md) 决策树）。
- 美术驱动的复杂序列特效（技能演出、角色动作）→ Spine，优化走 [Spine 运行时优化](./Spine运行时优化.md)（Freeze/分帧/atlas 共享）；UWA 案例：变化幅度不大的小图标特效没必要用 Spine。
- 大量随机、短命、同质的小元素（爆炸碎片、星星、烟尘）→ Particle System，一个系统一次 DrawCall 画全部粒子，比 N 个 Spine/节点便宜。
- **未知量**：目标机型同屏粒子数上限与 Spine 骨架数上限都要压测，方法见 §6.2。

### 2.4 微信 Particle Budget 方案（官方文档，镜像取得已核实）

来源：微信官方适配文档 `Design/ParticleBudget.md`，经第三方镜像 `BlackTom900131/minigame-unity-webgl-transform` 取全文（gh api，2026-11-13）；原页 developers.weixin.qq.com JS 渲染，待浏览器最终复核。官方文档内容。

**核心机制**：限制每帧粒子模拟预算，按优先级手动调用 `Particle.Simulate`，在性能与表现间平衡（比强制降帧对表现更友好）。两个核心组件：

| 组件 | 职责 |
|------|------|
| `ParticleSystemController` | 与 Particle System 同 GameObject；设优先级预设、注册到 Budget、计算优先级 |
| `ParticleSimulationBudgetManager` | 全局单例；维护 ForceUpdate（每帧强制）+ Managed（按优先级 + 时间预算按需）两个数组；`Max Frame Budget MS` 设每帧预算（毫秒） |

**优先级三因素**：预设类别（Force Update > High Priority > Normal）、距上次更新间隔（越久越高）、与相机距离（越近越高）。公式见 Demo `ParticleSystemController.CalculatePriority`，可自定义。（注：屏占比理论上比距离更合理，但需两次矩阵计算反而伤性能，故用距离。）

**性能数据（官方实测，保留设备条件）**：Mi 8 UD，101 个粒子特效（4 ForceUpdate / 34 HighPriority / 其余 Normal），Budget=2ms/帧 → `ParticleSimulationBudgetManager.LateUpdate` 开销略高于 2ms；原方案全粒子模拟 3.43ms 且不可控。

**接入判断**：粒子密集场景适用；关键特效 Force Update、次要特效优先级管理；高/中/低配只需调预算值。Demo：`wechat-miniprogram/minigame-unity-webgl-transform/Demo/particlebudget`（官方仓库已封，镜像可查）。

---

## 3. 拖尾：TrailRenderer / LineRenderer

### 3.1 机制与选型（官方规定）

- TrailRenderer：「renders a trail of polygons behind a moving GameObject, over time」——随物体移动自动记录位置生成多边形带，用于强调运动感/高亮路径。
- **与 LineRenderer 同一渲染算法**（官方原文：「The Trail Renderer uses the same algorithm for trail rendering as the Line Renderer」）。区别在点的来源：Trail 自动按时间记录，Line 手动设点（画固定弧线/激光用 Line）。
- 管线兼容：Line/Trail 在 URP / HDRP / Built-in 均支持（URP 下还可用 VFX Graph 做自定义拖尾——但本项目 WebGL 不可用，见 §1）。
  来源：[Introduction to trail rendering](https://docs.unity.com/en-us/engine/6000.6/manual/visual-effects/lines-trails-billboards/rendering-trails/trail-rendering-introduction)、[Lines and trails 目录](https://docs.unity.com/en-us/engine/6000.6/manual/visual-effects/lines-trails-billboards)（Unity 6.6 手册；核对 2026-11-13）。

### 3.2 关键参数（组件参考原文）

| 参数 | 官方定义 | 小游戏调参判断条件 |
|------|---------|-------------------|
| Time | 「the lifetime of a point in the trail, in seconds」 | 拖尾长度 = Time × 移速；短促刀光 0.1–0.3s，长拖尾 ≤0.5s（示例值，按手感调） |
| Min Vertex Distance | 「minimum distance between points in the trail, in world units」 | **官方性能建议：「For performance reasons, it is best to use the largest possible value that achieves the effect you are trying to create.」** 低值（0.1）平滑但顶点密，高值（1.5）省但有锯齿；宽拖尾+顶点过密+急转弯会出现视觉瑕疵 |
| Width | 宽度值 + 沿长度曲线，「sampled at each vertex」 | 起点宽终点 0 做渐隐；曲线精度受顶点数限制 |
| Emitting | 开=新增点，关=暂停生成 | 暂停拖尾用 `emitting=false`，不要销毁组件 |
| AutoDestruct | 静止 Time 秒后**销毁 GameObject** | **池化场景必须关闭**，否则池对象被销毁 |
| Texture Mode | Stretch / Tile / DistributePerSegment / RepeatPerSegment | 平铺速率用 `Material.SetTextureScale` 调 |

来源：[Trail Renderer component reference](https://docs.unity.com/en-us/engine/6000.6/manual/visual-effects/lines-trails-billboards/rendering-trails/class-trail-renderer)（Unity 6.6 手册；核对 2026-11-13。属性集在 2022.3 同样存在，默认值该页未列，以 Inspector 为准）。

### 3.3 最小接入（示例代码）

```csharp
// 示例：给移动物体挂加法混合拖尾
var trail = gameObject.AddComponent<TrailRenderer>();
trail.time = 0.25f;                 // 点存活 0.25s
trail.minVertexDistance = 0.3f;     // 满足效果前提下尽量大（官方性能建议）
trail.startWidth = 0.2f;
trail.endWidth = 0f;                // 尾部收尖
trail.material = additiveGlowMat;   // 加法混合材质，与粒子共用（合批）
trail.autodestruct = false;         // 池化对象必须关
trail.emitting = true;
```

**可观察完成结果**：物体移动时身后出现渐细光带，静止 0.25s 后光带消失；`emitting=false` 立即停止新增点。**完成标志**：拖尾材质与粒子材质相同时 Profiler 里不新增 SetPass Call。

---

## 4. Shader 特效：发光 / 溶解（无 compute 依赖）

### 4.1 方案与判断条件

| 效果 | 首选方案 | 判断条件 | 代价 |
|------|---------|---------|------|
| 发光（图标/按钮/拾取物） | **加法混合 Sprite**（Blend SrcAlpha One 的材质 + 光晕贴图） | 发光区域小、数量多、不需要真实泛光 | 零全屏开销；叠加过多仍吃 OverDraw（§6） |
| 发光（角色边缘） | Rim 边缘发光 Shader（视线与法线夹角加权） | 3D 角色/2D 带法线素材 | 每像素多一次点积，成本低于全屏 Bloom |
| 真实泛光（全屏） | URP Bloom Volume | **仅 WebGL 2.0 + 高档机**，见 §5 | 全屏多 pass，默认关闭 |
| 溶解/消失 | **Alpha Clip**：噪声图采样值与 `_Cutoff` 比较，低于阈值 discard；动画 `_Cutoff` 0→1 | 硬边可接受（像素风/卡通） | 无透明排序问题；边缘锯齿可加噪声软化 |
| 溶解（软边） | 透明混合 + alpha 渐变 | 需要柔和过渡 | 回到半透明 OverDraw 与排序问题 |

以上均为普通顶点/片元 Shader 实现，**不依赖 compute shader**，WebGL 1.0/2.0 都可运行（WebGL 1.0 注意 half 精度限制，见 [WebGL 坑 §4.4](<./Unity WebGL 小游戏特有技术坑.md>)）。Alpha Clip 与透明混合的取舍是通用 Unity 渲染知识；具体 Shader 写法从 §4.3 资源入手。

### 4.2 特效 Shader 的变体纪律

特效 Shader 是变体膨胀重灾区（每个特效一个 Shader = 每个都编译一套变体）。规则沿用 [DrawCall 条目 §5](./DrawCall与渲染批处理优化.md)：

- 全项目特效收敛到 **1–2 个 Shader**（加法混合 + Alpha Clip 各一），用材质参数（颜色/贴图/阈值）区分效果，不用新 Shader。
- 开关特性用 `shader_feature`（Build 自动裁剪），不用 `multi_compile`。
- WebGL 内存有限，官方警告不要把多变体 Shader 塞进 Always Included Shaders：「don't include unwanted shader variants because it can lead to unnecessary memory usage」。
  来源：[Unity Manual WebGL2](https://docs.unity.com/en-us/engine/6000.6/manual/platform-specific/webgl/develop/graphics/web-gl2)（Unity 6.6 手册；核对 2026-11-13。变体裁剪操作细节在 DrawCall 条目 §5 与 WebGL 坑 §4.3，不重复）。

### 4.3 复用资源：QianMo/Awesome-Unity-Shader（外部资源，先核许可证）

- **解决什么**：Shader 写法学习——Vol 04 Alpha 测试（溶解基础）、Vol 13 单色透明、Vol 14 Rim Shader 两种实现（边缘发光）、Vol 08–11/15 全屏后处理（径向模糊/像素化/高斯模糊）。同作者 [X-PostProcessing-Library](https://github.com/QianMo/X-PostProcessing-Library) 面向 PPSv2/LWRP/URP/HDRP。
- **从哪读起**：做溶解读 Vol 04，做发光读 Vol 14；仓库按 Volume 01–16 分目录。
- **版本依赖核对（接入前必做）**：仓库是 2014–2017 年教学存档（65 commits，长期未更新），示例基于旧版内置管线 Surface Shader/CG 语法，**URP 下不能直接编译**，需按 URP Shader 结构重写；**仓库无 LICENSE 文件**（默认保留所有权利），商用前必须向作者确认授权——评估流程走 [外部库选型评估](./外部库选型评估.md)。
- **定位**：学习参考，不是可直接接入的生产资产。
  来源：[QianMo/Awesome-Unity-Shader](https://github.com/QianMo/Awesome-Unity-Shader)（4.4k star；核对 2026-11-13）。

---

## 5. 后处理与 Bloom：可用性 + 降级

### 5.1 可用性（平台硬约束）

| 条件 | URP 后处理可用性 | 来源 |
|------|-----------------|------|
| WebGL 1.0（微信适配默认导出） | **不可用**：`Hidden/Universal/CoreBlit`、`LutBuilderLdr` 编译失败 → 黑屏 | [WebGL 坑 §4.1–4.2](<./Unity WebGL 小游戏特有技术坑.md>)（微信适配 QA，核对 2026-11-12） |
| WebGL 2.0 | 可用，但依赖 compute 的后处理效果仍不可用 | [团结 WebGLPlatform](https://docs.unity.cn/cn/tuanjiemanual/Manual/WebGLPlatform.html)：「一些依赖于Compute Shader的后处理…无法使用」（核对 2026-11-13） |
| 微信官方态度 | 默认不建议后处理（URP Asset → Renderer Data → Post Processing 关闭） | 微信 RenderOptimization（核对 2026-04-09，经 [DrawCall 条目 §4.3](./DrawCall与渲染批处理优化.md) 转引；**原页本轮待复核**，见 §9） |

**决策**：2D 休闲小游戏**默认全局关闭后处理**。发光需求用 §4.1 加法混合 Sprite 满足。只有「WebGL 2.0 包 + 设备分档高档 + 美术强需求」三条件同时成立才开 Bloom，且按 §5.2 降级参数。设备分档接 [小游戏分辨率适配与降级策略](./小游戏分辨率适配与降级策略.md)（中档关后处理、低档关后处理+粒子减半已在该条目降级表）。

### 5.2 Bloom 开启时的官方降级参数（Unity 6.6 手册原文 + URP 14 版本边界）

Bloom 是 URP Volume Override（启用路径：Camera 勾 Post Processing + Volume + Bloom Override，见 [URP 后处理目录](https://docs.unity.com/en-us/engine/6000.6/manual/post-processing-and-full-screen-effects/urp)）。官方性能建议逐条：

| 参数 | 官方原文/默认值 | 小游戏设置 |
|------|----------------|-----------|
| High Quality Filtering | 「disable this property to greatly improve performance, especially on lower-end hardware」 | **关闭** |
| Filter | 「Dual is a faster alternative for mobile devices. Kawase… fastest option at low resolution」 | **Dual 或 Kawase**（默认 Gaussian 最贵）⚠️ **仅 Unity 6+/URP 15+；URP 14（本项目）无此下拉**，见下方版本边界 |
| Downscale | 「For best performance, set this value to Quarter」 | **Quarter**（1/4 分辨率起模糊） |
| Max Iterations | 默认 6；「Decreasing this value reduces processing load… especially on mobile devices with high DPI screens」 | **≤4**（高 DPI 手机屏收益明显） |
| Intensity | 默认 0（=关闭）；范围 0–1 | 按需，0 即关 |
| Threshold | 默认 0.9（gamma 空间亮度阈值） | 提高阈值减少参与泛光的像素 |
| Lens Dirt | 「try a lower resolution Texture」 | 不用或低分辨率贴图 |

来源：[Bloom Volume Override reference for URP](https://docs.unity.com/en-us/engine/6000.6/manual/post-processing-and-full-screen-effects/urp/effect-list/bloom)（Unity 6.6 手册；核对 2026-11-13）。
**版本边界（URP 14 已核实，round 31）**：本项目 URP 14（团结 2022.3）Bloom 参数集经 [URP 14 包文档](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@14.0/manual/post-processing-bloom.html) 核实——**有** Threshold(0.9)/Intensity(0)/Scatter(0.7)/Tint/Clamp(65472)/High Quality Filtering/Downscale(Quarter 推荐)/Max Iterations(6)/Lens Dirt；**无 Filter（Gaussian/Dual/Kawase）下拉**（Unity 6+/URP 15+ 才有，URP 14 仅 High Quality Filtering 开关切换 bicubic/bilinear）。Downscale/Max Iterations 藏在 More(⋮) > Show Additional Properties。**故本项目降级用：关 High Quality Filtering + Downscale=Quarter + Max Iterations≤4，不用 Filter 下拉**。「1/4 下采样」方向有 UWA 独立佐证（[UWA 小游戏简谱](https://blog.uwa4d.com/archives/UWA_MiniGame.html) §6.4）。

### 5.3 后处理的隐性成本（外部案例，UWA）

- 「后处理、Blit、Copy等操作同样会产生全屏级别的Overdraw」——每开一个全屏效果 = 每像素多跑一遍。
- 「开启HDR会引入额外的全分辨率RT拷贝操作以及额外的显存开销」——Bloom 常与 HDR 联动，小游戏**不开 HDR**。
- 「尽量使用Local Volume按需开启而非全局常驻」——只在需要的场景/区域生效。
  来源：[UWA Unity 小游戏优化简谱](https://blog.uwa4d.com/archives/UWA_MiniGame.html) §6.3–6.4（核对 2026-11-13；文章为通用指南，无特效专项机型数值）。

---

## 6. 性能预算与交付检查

### 6.1 预算表（检查交付用）

| 项 | 预算/通过标准 | 检查入口 | 依据 |
|----|--------------|---------|------|
| 同屏存活粒子总数 | **无官方公开上限**；以目标低端机 30fps 压测值为准（模板 `GameConfig.targetFrameRate = 30`，项目代码验证 2026-11-13） | Profiler CPU 模块 `ParticleSystem.Update`；真机微信/抖音性能面板 | UWA 定性 + §6.2 压测法 |
| 单个粒子系统 Max Particles | 从 ≤100 起步，压测后上调 | Inspector Main 模块 | 实践起点（示例值，非官方） |
| 特效 OverDraw | 战斗高潮同屏半透明叠加 ≤3–5 层；全屏遮罩+大粒子不同时出现 | Scene 视图 Overdraw 模式 / URP Rendering Debugger | [DrawCall 条目 §4](./DrawCall与渲染批处理优化.md)（微信警告原文在该条目） |
| 特效材质数 | 全项目 1–2 个（加法+Alpha Clip） | Profiler SetPass Calls；Frame Debugger（Editor） | DrawCall 条目 §5 |
| 特效纹理 | 进图集（≤1024），ASTC 压缩；纯透明区域裁掉 | Sprite Atlas + 压缩纹理工具 | DrawCall 条目 §3/§6；UWA「裁剪纯透明贴图面积」 |
| Shader 变体 | 特效 Shader 用 shader_feature；不进 Always Included | Build 报告变体数 | §4.2；WebGL 坑 §4.3 |
| 后处理 | 默认关；开启须 WebGL2+高档机+§5.2 参数 | URP Asset / Volume | §5.1 |
| 池化 | 特效 Prefab 全走 PrefabPool；Stop Action=Disable；Trail AutoDestruct=false | Hierarchy 无残留；GC Alloc 无尖峰 | §2.2/§3.2 |

### 6.2 同屏粒子数压测法（沿用 UI 动效选型的压测框架）

方法与 [UI 动效选型](./UI动效选型.md)「目标设备测量」节同源（Spine 骨架数压测同理）：

1. 真机（目标低端档）+ development 包，只放一种特效排除其他干扰。
2. 从目标同屏数递减释放，记录**不掉帧（30fps）的上限**。
3. 上限远低于需求时依次：减 Max Particles → 关多余模块 → 换更小贴图/裁透明边 → 分帧释放（大特效拆 2–3 帧，DrawCall 条目 §4.3）→ 仍不够则换帧动画/Spine 序列（§2.3）。
4. **分清 CPU 还是 GPU 瓶颈**：Profiler 里 `ParticleSystem.Update` 高 = CPU（减数量/模块）；Overdraw 视图红 = GPU（减面积/层数）。两者优化手段不同。
5. 微信/抖音 PC 开发者工具数据不可尽信（UWA：ASTC 会回退 RGBA32、WebGL2 特性表现有差异），**预算以真机复测为准**。

---

## 7. 排查：特效引起的卡顿 / 显示异常

| 症状 | 先看什么证据 | 分支指向 |
|------|-------------|---------|
| 释放技能瞬间掉帧 | Profiler CPU：`ParticleSystem.Update` 耗时 | 高 → CPU 模拟瓶颈：减 Max Particles/关模块/分帧释放（§2/§6.2） |
| 持续低帧率、特效越多越卡 | Scene Overdraw 视图 / URP Rendering Debugger | 红区集中在特效 → 半透明 OverDraw：减层数/裁透明边/换加法混合小图（§6） |
| 特效材质紫色/全透明 | 真机 vConsole 报错 + 纹理格式 | 压缩格式不支持 → [真机白屏与资源加载失败排查](./真机白屏与资源加载失败排查.md) §3.5 / [WebGL 坑 §3.6](<./Unity WebGL 小游戏特有技术坑.md>) |
| 开后处理后黑屏 + Shader 编译报错 | vConsole 里 CoreBlit/LutBuilder 报错 | WebGL 1.0 跑了 URP 后处理 → 切 WebGL2 或关后处理（§5.1，WebGL 坑 §4.2） |
| 拖尾对象池复用后残留旧轨迹 | 复用时是否 Clear | 池化取出时清空轨迹（TrailRenderer.Clear，API 以 ScriptReference 为准）；AutoDestruct 是否误开（§3.2） |
| 粒子不显示但无报错 | Renderer 模块材质/Sorting Layer | 2D 项目检查 Sorting Layer 与 Order in Layer；材质 Shader 是否 WebGL 兼容（WebGL 坑 §4） |

复测条件：同一低端真机 + 同一特效 Prefab + 同一释放数量，对比修改前后 Profiler 数值（方法见 [Unity 真机 Profiling 与内存分析](./Unity真机Profiling与内存分析.md)）。

---

## 8. 模板工程对照（项目实测，2026-11-13 Grep/find 验证）

- `projects/MiniGameTemplate/Assets/` 全目录 Grep `ParticleSystem|TrailRenderer|LineRenderer|VisualEffect|PostProcess|Bloom` **零命中**；无 `.shader/.shadergraph/.vfx` 文件——模板是代码骨架，**无任何特效实现**，本条目为前瞻指导。
- 可复用入口：`Assets/_Project/Scripts/Res/PrefabPool.cs`（特效池化）、`Assets/_Project/Scripts/Core/GameManager.cs` L41 `Application.targetFrameRate`（默认 30，`Data/GameConfig.cs` L12）。
- 首次加特效的建议落点：`Assets/_Project/Art/Effects/`（Prefab+材质+图集），与现有 Art 目录结构一致；特效材质收敛到 1–2 个（§4.2）。
- 模板无 ProjectSettings（DrawCall 条目 §8 已验证）：URP Asset / Graphics Settings 需首次 Editor 打开时创建，后处理默认关闭在 URP Asset 上设置。

---

## 9. 待核对项

| # | 项 | 恢复条件 | 备注 |
|---|-----|---------|------|
| 1 | ~~微信 ParticleBudget.html 正文~~ | **已解决（round 23 镜像）** | 经镜像 BlackTom900131/minigame-unity-webgl-transform `Design/ParticleBudget.md` 取全文（gh api），机制/参数/Mi 8 UD 实测已写入 §2.4；原页 JS 渲染待浏览器最终复核 |
| 2 | ~~微信 RenderOptimization.html 正文复核~~ | **已解决（round 23 镜像）** | 经同一镜像 `Design/RenderOptimization.md` 取全文（gh api），核实 DrawCall 条目转引结论并补充数值（DrawCall≤200/250、单图≤512/图集≤1024、纹理≤5、阴影贴图≤1024、后处理不建议、Stencil 在 iOS 15.4 异常）；原页待浏览器最终复核 |
| 3 | ~~URP 14 Bloom 属性集是否与 Unity 6.6 手册一致~~ | **已解决（round 31 URP 14 包文档）** | URP 14 有 High Quality Filtering/Downscale/Max Iterations，**无 Filter（Gaussian/Dual/Kawase）下拉**（Unity 6+ 才有）；§5.2 版本边界已修正 |
| 4 | 团结引擎新版本/微信抖音壳是否支持 WebGPU（决定 VFX Graph 未来可用性） | 团结版本说明 + 平台适配文档出现 WebGPU 声明 | 当前团结 1.3 手册明确仅 WebGL 1/2（§1.3） |
| 5 | 目标低端机同屏粒子数上限实测 | 首次出包 + 低端真机 | §6.2 压测法；与 KB-009 出包前置合并执行 |

---

## 来源汇总

| 来源 | 类型 | 核对日期 | 覆盖章节 |
|------|------|---------|---------|
| [VFX Graph 14.0 System-Requirements](https://docs.unity3d.com/Packages/com.unity.visualeffectgraph@14.0/manual/System-Requirements.html) | 官方规定（14.x↔Unity 2022.2+） | 2026-11-13 | §1.1 不支持 OpenGL ES/compute/SSBO、URP unlit-only、无 gamma |
| [VFX Graph 14.0 GettingStarted](https://docs.unity3d.com/Packages/com.unity.visualeffectgraph@14.0/manual/GettingStarted.html) | 官方规定 | 2026-11-13 | §1.1 compute 依赖、SRP 版本匹配 |
| [Compare WebGL2 and WebGPU（Unity 6.6）](https://docs.unity.com/en-us/engine/6000.6/manual/platform-specific/webgl/develop/graphics/web-graphics-apis-intro) | 官方规定 | 2026-11-13 | §1.1 WebGL2 无 compute、WebGPU 有限支持 |
| [团结引擎 WebGLPlatform](https://docs.unity.cn/cn/tuanjiemanual/Manual/WebGLPlatform.html) | 官方规定（团结 1.3/2022.3，发布 2025-07-29） | 2026-11-13 | §1.1/§1.3/§2.1/§5.1 compute 不可用、粒子变慢、仅 WebGL1/2 |
| [Choosing your particle system solution（Unity 6.6）](https://docs.unity.com/en-us/engine/6000.6/manual/visual-effects/particle-systems/choosing-your-particle-system) | 官方规定 | 2026-11-13 | §1.2 对比表（数千 vs 百万、管线兼容） |
| [Particle System Job System 集成（Unity 6.6）](https://docs.unity.com/en-us/engine/6000.6/manual/visual-effects/particle-systems/particle-system-job-system-integration) | 官方规定 | 2026-11-13 | §2.1 CPU 模拟/worker thread（WebGL 不适用推论） |
| [Introduction to trail rendering（Unity 6.6）](https://docs.unity.com/en-us/engine/6000.6/manual/visual-effects/lines-trails-billboards/rendering-trails/trail-rendering-introduction) | 官方规定 | 2026-11-13 | §3.1 机制、同算法、Min Vertex Distance 性能建议 |
| [Trail Renderer component reference（Unity 6.6）](https://docs.unity.com/en-us/engine/6000.6/manual/visual-effects/lines-trails-billboards/rendering-trails/class-trail-renderer) | 官方规定 | 2026-11-13 | §3.2 参数定义 |
| [Lines and trails 目录（Unity 6.6）](https://docs.unity.com/en-us/engine/6000.6/manual/visual-effects/lines-trails-billboards) | 官方规定 | 2026-11-13 | §3.1 管线兼容表 |
| [Bloom Volume Override reference for URP（Unity 6.6）](https://docs.unity.com/en-us/engine/6000.6/manual/post-processing-and-full-screen-effects/urp/effect-list/bloom) | 官方规定 | 2026-11-13 | §5.2 降级参数原文 |
| [URP 14 包文档 Bloom](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@14.0/manual/post-processing-bloom.html) | 官方规定（URP 14↔Unity 2022.3） | 2026-11-13 round 31 | §5.2 版本边界：URP 14 无 Filter 下拉，有 HQF/Downscale/Max Iterations |
| [URP 后处理目录（Unity 6.6）](https://docs.unity.com/en-us/engine/6000.6/manual/post-processing-and-full-screen-effects/urp) | 官方规定 | 2026-11-13 | §5.2 Volume 启用路径 |
| [Unity Manual WebGL2（Unity 6.6）](https://docs.unity.com/en-us/engine/6000.6/manual/platform-specific/webgl/develop/graphics/web-gl2) | 官方规定 | 2026-11-13 | §4.2 变体内存警告 |
| [UWA Unity 小游戏优化简谱](https://blog.uwa4d.com/archives/UWA_MiniGame.html) | 外部案例（通用指南，无特效专项机型数值） | 2026-11-13 | §2.1 Max Particles 定性、§5.2 Bloom 1/4 下采样、§5.3 全屏 OverDraw/HDR、§6.2 PC 工具差异 |
| [QianMo/Awesome-Unity-Shader](https://github.com/QianMo/Awesome-Unity-Shader) | 外部资源（2014–2017 教学存档，无 LICENSE） | 2026-11-13 | §4.3 复用边界 |
| 微信 RenderOptimization（镜像 Design/RenderOptimization.md 全文核实） | 官方文档内容（第三方镜像取得） | 2026-11-13 镜像核实（原页核对 2026-04-09，原页待浏览器复核） | §5.1 默认不用后处理、§6.1 OverDraw 警告 |
| 微信 ParticleBudget（镜像 Design/ParticleBudget.md 全文） | 官方文档内容（第三方镜像取得） | 2026-11-13 | §2.4 Particle Budget 机制/参数/Mi 8 UD 实测 |
| 镜像仓库 BlackTom900131/minigame-unity-webgl-transform | 官方文档第三方镜像（原 wechat-miniprogram 仓库已封）；gh api contents 取 Design/*.md 源文 | 2026-11-13 round 23 验证可达 | A28/A29 替代通道；内容须与官方交叉，原页待浏览器复核 |
| 模板工程 Grep/find + GameManager.cs/GameConfig.cs | 项目实测 | 2026-11-13 | §8 无特效代码、帧率 30、PrefabPool 入口 |

## 相关条目

- [DrawCall 与渲染批处理优化](./DrawCall与渲染批处理优化.md) — OverDraw 治理/Shader 变体/图集/纹理压缩的通用规则（本条目不重复）。
- [Unity WebGL 小游戏特有技术坑](<./Unity WebGL 小游戏特有技术坑.md>) — WebGL1/2 硬差异、URP CoreBlit 黑名单、Shader 精度。
- [UI 动效选型](./UI动效选型.md) — 简单 UI 动效不用粒子/Spine 的决策树、同屏规模压测法。
- [Spine 运行时优化](./Spine运行时优化.md) — 美术序列特效选 Spine 后的优化实操。
- [小游戏分辨率适配与降级策略](./小游戏分辨率适配与降级策略.md) — 设备分档降级表（中档关后处理、低档粒子减半）。
- [对象池接入与复用](./对象池接入与复用.md) — 特效 Prefab 池化流程（Unity PrefabPool 对照）。
- [Unity 真机 Profiling 与内存分析](./Unity真机Profiling与内存分析.md) — 特效 CPU/GPU 耗时测量。
- [真机白屏与资源加载失败排查](./真机白屏与资源加载失败排查.md) — 特效纹理格式/Shader 编译异常排查。
- [外部库选型评估](./外部库选型评估.md) — QianMo 仓库许可证风险评估流程。