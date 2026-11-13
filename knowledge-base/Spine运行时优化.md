# Spine 运行时优化

适用：spine-unity 运行时（SkeletonAnimation / SkeletonGraphic），团结引擎 / Unity 2022.3，微信 + 抖音小游戏 WebGL。
触发：同屏 Spine 骨架多导致 CPU 卡顿、DrawCall 过高、实例化掉帧。
核对日期：2026-11-12（Spine 官方 FAQ + Rendering 文档、UWA 实战帖、团结 GpuSkinning 手册）。
前置：选型决策（要不要用 Spine）见 [UI 动效选型](./UI动效选型.md)；本条目只讲「决定用 Spine 后怎么优化」。

## 结论（影响行动的部分）

1. **Spine 预算单位是「激活骨架数 × 骨骼复杂度」，不是 DrawCall**。合批解决不了骨骼计算（CPU 侧）。
2. **屏幕外 Freeze，不要 disable**：SkeletonGraphic 是复杂 UI 元素，激活/隐藏触发网格重建额外开销。
3. **atlas 页数 = 材质数 = DrawCall 数**。多骨架共享一张 atlas 是最有效的合批手段。
4. **实例化掉帧 → 二进制导出 + 对象池预热**，不在游戏中 Instantiate/Destroy。
5. **GPU 骨骼动画（纹理烘焙）是万级同屏的终极方案**，但属商业插件范畴；团结 GpuSkinning 是 3D 蒙皮，与 2D Spine 无关。
6. **SkeletonGraphic 只能用 Spine/SkeletonGraphic* shader**，不能用 URP/Lit/Spine/Skeleton。

## 一、CPU 优化：减少激活骨架的计算量

### 1.1 视域外冻结（Freeze）

来源：[UWA：Spine 动画更新耗时治理](https://blog.uwa4d.com/archives/TechSharing_471.html)，核对 2026-11-12。外部案例：Unity 三消游戏，GOT Online 线上数据。

- 进场景后 `SkeletonGraphic.Update/LateUpdate` 调用 50+ 次 = 50+ 个激活 Spine 组件。
- **屏幕外骨架必须 Freeze**：停止骨骼变换计算，回视域前解冻。
- **Freeze 优于 disable**：SkeletonGraphic 属 UI 组件，激活/隐藏可能触发网格重建（Canvas rebuild），额外开销大于 Freeze。

```csharp
// 示例：视域外冻结（具体 API 以 spine-unity 版本为准）
skeletonGraphic.freeze = true;   // 停止更新
skeletonGraphic.freeze = false;  // 恢复更新
```

### 1.2 round-robin 分帧更新（示例，待论坛原文核实）

来源线索：[Spine 官方论坛：性能优化](https://zh.esotericsoftware.com/forum/d/25472-spine性能优化)（JS 渲染，本轮未取到正文，待浏览器核实）。

**原理**：N 个骨架不必每帧都更新。将骨架分组，每帧只更新其中一组，N 帧轮完一圈。视觉帧率降为 1/N，但 CPU 开销降为 1/N。适用于远处/次要/装饰性骨架。

```csharp
// 示例：round-robin 分帧（非官方 API，需按 spine-unity 版本适配）
// 前提：SkeletonAnimation 支持手动更新模式
public class SpineRoundRobin : MonoBehaviour
{
    public SkeletonAnimation[] skeletons;
    public int groupCount = 4; // 分 4 组，每骨架 4 帧更新一次
    private int frame;

    void Update()
    {
        int group = frame % groupCount;
        for (int i = group; i < skeletons.Length; i += groupCount)
        {
            // 手动驱动更新（需确认组件支持 ManualUpdate 或等效 API）
            skeletons[i].Update(Time.deltaTime * groupCount);
        }
        frame++;
    }
}
```

**适用条件**：
- 骨架动作缓慢/装饰性（远处 NPC、场景特效）→ 分帧不明显
- 骨架动作快/主角 → 不适用（会看到卡顿）
- 与 Freeze 组合：屏幕外 Freeze，屏幕内次要骨架分帧

**待核实**：spine-unity 是否暴露 ManualUpdate / UpdateMode 属性；论坛帖中的具体实现方式。恢复条件：浏览器渲染论坛帖，或查 spine-unity 源码 `SkeletonAnimation.cs`。

### 1.3 简单动画不用 Spine

来源：[UWA](https://blog.uwa4d.com/archives/TechSharing_471.html)，核对 2026-11-12。

- 变化幅度不大的小图标特效「确实没有太大必要用 Spine 来做」。
- 替代：节点动画 / Tween / 帧序列（见 [UI 动效选型](./UI动效选型.md) 决策树）。

### 1.4 骨骼复杂度裁剪

来源：[Spine 官方 FAQ](https://esotericsoftware.com/spine-unity-faq)，核对 2026-11-12。官方规定。

| 手段 | 说明 |
|------|------|
| 减少 clipping polygon 顶点 | 面积无关，只看顶点数；矩形用 1 个大三角形而非 2 个 |
| 减少 mesh deformation keys | 网格变形键越少，每帧计算越少 |
| 减少顶点数 | 附件网格顶点直接影响变换计算量 |
| 删除不必要的 key | 不要给所有属性都打键 |
| 用 Unity Mask 替代 Spine clipping | 避免运行时多边形裁剪 |
| 参考 Spine Editor Metrics 视图 | 编辑器内直接看复杂度指标 |

## 二、DrawCall 优化：atlas 与材质

### 2.1 核心规则（官方规定）

来源：[Spine Rendering 文档](https://esotericsoftware.com/spine-unity-rendering)，核对 2026-11-12。

- **每个 atlas page 纹理 = 1 个材质 = 至少 1 个 DrawCall**。
- 非 Normal 混合模式（Additive/Multiply/Screen）额外创建材质（PMA Additive 例外）。
- Materials 数组中交替排列会成倍增加 DrawCall（A,A,B,A → 3 个 entry）。
- **SkeletonRenderer 每帧 LateUpdate 重写 Materials 数组**，直接改 MeshRenderer.materials 无效。

### 2.2 多骨架共享 atlas（最有效合批）

来源：[Spine FAQ](https://esotericsoftware.com/spine-unity-faq)，核对 2026-11-12。

**操作**：多个 SkeletonDataAsset 的 atlas assets 数组指向同一个 AtlasAsset。

```
SkeletonDataAsset A → atlasAssets[0] = SharedAtlas
SkeletonDataAsset B → atlasAssets[0] = SharedAtlas
→ 两者共享材质 → 可合批
```

**前提**：
- 所有骨架的附件打包在同一张 atlas 纹理上（用 Spine Texture Packer 的 folder structure 或 CLI 打包）。
- 按 draw order 分组排列附件，避免交替。
- 混合模式一致（都 Normal，或都 PMA Additive）。

### 2.3 单骨架减 DrawCall

| 手段 | 操作 |
|------|------|
| 减少 atlas page 数 | Spine 导出时尽量打包到 1 页（增大 atlas 尺寸） |
| 按 draw order 分组 | 相邻绘制的附件放同一 atlas page |
| 运行时 repack | 换装跨多 atlas 时，运行时合并为 1 张（接受一次 repack 开销） |
| 重排 draw order | 减少材质切换次数 |

### 2.4 着色不破坏合批

来源：[Spine Rendering](https://esotericsoftware.com/spine-unity-rendering)，核对 2026-11-12。

- **用顶点色着色**：`Skeleton.R/.G/.B/.A` 和 `slot.R/.G/.B/.A`，需开启 Advanced → PMA Vertex Colors。
- **不要用独立材质或 PropertyBlock 做逐实例着色**（不同值会打断合批）。
- MaterialPropertyBlock：所有实例值相同时仍可合批；值不同则断。用 `Shader.PropertyToID` 缓存属性名。
- 逐实例材质覆盖：用 `SkeletonRendererCustomMaterials` / `SkeletonGraphicCustomMaterials` 组件，或 `OnMeshAndMaterialsUpdated` 回调。

## 三、实例化与内存优化

来源：[Spine FAQ](https://esotericsoftware.com/spine-unity-faq)，核对 2026-11-12。官方规定。

| 问题 | 解法 |
|------|------|
| Instantiate 掉帧 + GC | 改用二进制导出 `.skel.bytes`（比 .json 解析快、分配少） |
| 游戏中频繁创建/销毁 | 对象池：关卡加载时预热（如 10 个实例），运行时 enable/reposition |
| 归还池时状态残留 | `AnimationState.ClearTracks()` 后再 disable |
| 懒加载 | 按需实例化，不要启动时全部创建（[掘金](https://juejin.cn/post/7146016731905916965)，外部案例） |

## 四、GPU 骨骼动画（万级同屏终极方案）

### 4.1 原理与适用条件

来源：[cnblogs：10 万 Spine 同屏终极优化](https://www.cnblogs.com/efunstudio/p/18981572)，核对 2026-11-12。外部案例（商业插件「万人同屏方案Pro」，efunstudio）。

**核心思路**：将骨骼矩阵烘焙到纹理，纯 Shader 采样驱动动画，不依赖运行时脚本 → 可用 Graphics 接口 / BatchRendererGroup 批量合批。

| 维度 | 传统 Spine | GPU 骨骼动画 |
|------|-----------|-------------|
| 动画驱动 | CPU 每帧算骨骼矩阵 | Shader 采样动画纹理 |
| 合批 | 受材质/atlas 限制 | 支持 GPU Instancing / BRG |
| 逻辑交互 | 完整（事件/挂载/IK） | 需额外实现（骨骼 Transform 查询、事件触发） |
| 适用规模 | 数十~数百同屏 | 万级同屏 |
| 平台 | 全平台 | 宣称兼容 WebGL（待实测） |

**关键认知**：「2D 游戏常用的 Spine 动画，性能甚至还不及 Animator」——这是 GPU 动画方案的出发点。

**局限**：
- 商业插件，非免费开源方案。
- 原文无具体帧率/设备数据（仅营销宣称「10 万同屏」「帧数提升数十倍」）。
- 半透明排序是难点（合并 Mesh 后 Sorting Order 失效）。
- 元素显隐用「矩阵缩放置 0」方案（法线反转方案在 Spine 镜像转身时失效）。

### 4.2 团结 GpuSkinning ≠ Spine GPU 动画

来源：[团结 GpuSkinning 手册](https://docs.unity.cn/cn/tuanjiemanual/Manual/GpuSkinning.html)，核对 2026-11-12。官方规定。

**团结 GpuSkinning 是 3D SkinnedMeshRenderer 的蒙皮加速，与 2D Spine 无关。**

| 项 | 说明 |
|----|------|
| 适用对象 | 3D 骨骼蒙皮（SkinnedMeshRenderer），非 Spine |
| WebGL 方案 | Vertex Shader Skinning（WebGL 不支持 Compute Shader） |
| 骨骼上限 | 基础 64 根；团结 ≥1.5.2 + WebGL2 用 MORE_BONES 关键字支持 320 根 |
| 配置 | Player Settings → Other Settings → Mesh Skinning → GPU - Vertex Shader |
| Shader 关键字 | `ENABLE_VS_SKINNING`（基础）/ `ENABLE_VS_SKINNING_MORE_BONES`（扩展） |
| 限制 | 不支持运行时切 Material、BlendShape、MotionVector |

**对本项目的意义**：如果未来有 3D 角色（非 Spine），可用此方案加速蒙皮。2D Spine 不走这条路。

## 五、SkeletonGraphic 特有约束（uGUI 项目必读）

来源：[Spine Rendering](https://esotericsoftware.com/spine-unity-rendering)，核对 2026-11-12。官方规定。

| 约束 | 说明 |
|------|------|
| Shader 限制 | **只能用 Spine/SkeletonGraphic* shader**。不能用 URP/Lit、Spine/Skeleton |
| 单纹理限制 | CanvasRenderer 限制，Spine/Skeleton Graphic 只支持单纹理 |
| Additive + CanvasGroup | 用 Spine/SkeletonGraphic Tint Black；4.2+ 开 CanvasGroup Compatible |
| Tint Black 设置 | 开 Advanced → Tint Black + 对应材质 + Canvas 开 TexCoord1/TexCoord2 |
| Freeze | 组件面板 freeze 属性或代码控制（优于 disable） |
| 排序 | Canvas sortingOrder 控制；多骨架间用 Sorting Group |

**本项目注意**：模板使用 uGUI（Canvas），Spine 组件必须用 SkeletonGraphic（非 SkeletonAnimation）。Shader 必须选 Spine/SkeletonGraphic 系列。URP 项目不能直接用 Spine/Skeleton Lit。

## 六、优化决策树

```
Spine 卡顿？
├─ CPU 瓶颈（Profiler 看 SkeletonGraphic.Update 耗时）
│   ├─ 屏幕外未冻结 → Freeze（§1.1）
│   ├─ 简单动画用了 Spine → 换节点动画（§1.3）
│   ├─ 同屏骨架太多 → 分帧 round-robin（§1.2）+ 减数量
│   └─ 骨骼太复杂 → 裁顶点/减 key/减 clipping（§1.4）
├─ DrawCall 瓶颈（Profiler 看 Batches）
│   ├─ 多 atlas page → 合并 atlas / 共享 AtlasAsset（§2.2）
│   ├─ 混合模式交替 → 重排 draw order（§2.3）
│   └─ 逐实例材质 → 改顶点色着色（§2.4）
├─ 实例化掉帧 → 二进制导出 + 对象池（§3）
└─ 万级同屏需求 → GPU 骨骼动画商业方案（§4）
```

## 七、待核对项

| 内容 | 来源 | 恢复条件 |
|------|------|----------|
| round-robin 论坛原文实现 | zh.esotericsoftware.com/forum/d/25472 | IAB 浏览器渲染 |
| spine-unity ManualUpdate / UpdateMode API | spine-unity 源码或 Main Components 文档 | 查 GitHub spine-runtimes 或安装 runtime 后看 API |
| GPU 骨骼动画 WebGL 真机性能 | efunstudio.cn/web-demo | 真机实测 |
| 少量动画移动端性能差（atlas 用法） | zh.esotericsoftware.com/forum/d/17526 | IAB 浏览器渲染 |

## 相关条目

- [UI 动效选型](./UI动效选型.md) — 选型决策（要不要用 Spine）、Cocos 三种缓存模式、同屏规模压测法
- [DrawCall 与渲染批处理优化](./DrawCall与渲染批处理优化.md) — Canvas 合批规则、图集策略（通用 uGUI 侧）
- [对象池接入与复用](./对象池接入与复用.md) — 对象池通用流程（Spine 实例池化同理）
- [Unity 真机 Profiling 与内存分析](./Unity真机Profiling与内存分析.md) — CPU 模块定位 SkeletonGraphic.Update 耗时
- [小游戏启动优化](./小游戏启动优化.md) — 首包精简（Spine 资产不进首包）
- [Spine Unity 资产工作流](./SpineUnity资产工作流.md) — 导入流程、SkeletonDataAsset 结构、无 SHARED_CACHE 等效、PMA 设置、版本匹配