# Cocos 场景切换后内存排查

适用：Cocos Creator 3.x（按 3.8 官方手册核对），微信/抖音小游戏平台是重灾区（iOS 尤甚）。触发条件：反复切场景后内存持续上涨、不回落，或 iOS 低端机内存闪退。核对日期：2026-11-11。

## 结论（机制，决定排查方向）

1. **切场景默认销毁场景内所有节点，但资源不等于节点**：加载过的资源缓存在 `assetManager` 里供复用，节点销毁不会连带释放资源缓存。是否释放取决于场景的「自动释放资源」设置与引用计数。（[加载和切换场景](https://github.com/cocos/cocos-docs/blob/master/versions/3.8/zh/scripting/scene-managing.md) · [资源释放](https://github.com/cocos/cocos-docs/blob/master/versions/3.8/zh/asset/release-manager.md)，官方，核对 2026-11-11）
2. **动态加载的资源不在场景自动释放范围内**：`resources.load` / `bundle.load` / `loadRemote` 加载后赋给组件的引用是「动态引用」，引擎不统计（JS 有 GC、无法重载赋值运算符），引用计数保持 0 但资源仍在缓存里。场景自动释放只处理场景的**静态依赖**。这就是「动态加载资源场景切换不会自动释放」的机制根源。（[资源释放](https://github.com/cocos/cocos-docs/blob/master/versions/3.8/zh/asset/release-manager.md)「资源的动态引用」，官方）
3. **常驻节点跨场景不销毁**：`director.addPersistRootNode` 的节点及其组件持有的缓存（包括对象池）切场景时保留——查内存时先列出所有常驻节点。（[场景管理](https://github.com/cocos/cocos-docs/blob/master/versions/3.8/zh/scripting/scene-managing.md)，官方）
4. **对象池未 clear 的孤儿节点**：池内节点游离在场景树外，切场景不销毁。见 [对象池接入与复用](./对象池接入与复用.md)。

## 第一步：怎样复现增长

1. **固定路径循环**：A 场景 → B 场景 → 回 A，循环 N 次（建议 ≥10），每轮走相同的操作（相同的动态加载、相同的 UI 打开关闭）。
2. **每轮记录两个数**：切场景前的稳态内存、本轮峰值内存。工具见下节。
3. **判定**：
   - 逐轮上涨且不回落 → 泄漏或资源未释放，继续查。
   - 首轮上涨后稳定 → 是缓存行为，判断缓存是否合理（高频复用资源常驻是正常策略）。
   - 峰值逐轮上涨但稳态不变 → 瞬时分配未复用（GC 压力），查每轮新建对象。

## 第二步：先看什么证据（工具）

| 工具 | 看什么 | 适用 | 来源 |
| --- | --- | --- | --- |
| 抖音开发者工具调试器 Profiles | **Heap snapshot**（对象自身+引用大小）、**Allocation instrumentation on timeline**（JS Heap 峰值曲线）、Allocation sampling（按执行栈的分配近似值） | 抖音，IDE 上 | [iOS 抖音内存优化指南](https://github.com/cocos/cocos-docs/blob/master/versions/3.8/zh/editor/publish/bytedance-ios-optimize.md)「内存问题诊断」（官方，核对 2026-11-11） |
| 抖音**实时内存诊断** | 柱状图出现=内存块创建、消失=释放；垃圾桶按钮主动触发 GC；选中区域看 **Retainers 引用链**，推理对象是否应被释放 | 抖音，定位泄漏首选 | 同上 |
| Xcode Instruments（Activity Monitor） | 真机进程内存；过滤 webkit；**高性能模式进程名含 WebContent，普通模式含 Aweme** | iOS 真机 | 同上 |
| Perfdog | 真机内存/帧率曲线 | iOS/Android 真机 | 同上（[perfdog.qq.com](https://perfdog.qq.com/)） |
| 浏览器 DevTools Memory | 打 web-mobile 包，Safari/Chrome 远程调试抓 Heap snapshot | 开发期快速验证 | 同上「JavaScript Heap」节 |

**注意**：JS Heap 通常看不出具体物理内存使用；总内存还包含 WASM、基础库、Canvas、音频、GPU 资源（见下文内存结构）。JS Heap 平稳不代表总内存平稳。

## 第三步：不同结果分别指向哪里

| 证据 | 指向 | 下一步 |
| --- | --- | --- |
| Heap snapshot 大量 Texture/SpriteFrame 未释放，Retainers 指向 assetManager 缓存 | 动态资源没 decRef/release | 持有方 `decRef()` 并置 null；或 `assetManager.releaseAsset(asset)` |
| Retainers 指向常驻节点/全局单例的成员 | 常驻持有方缓存无上限 | 给常驻缓存加容量上限/切场景清理策略 |
| 节点数逐轮增长，节点无父节点 | 池未 clear、事件监听持有节点 | `pool.clear()`；查全局事件总线退订 |
| JS Heap 平稳但总内存涨 | GPU 资源/Canvas/音频 | 渲染分辨率倍数、音频实例释放、压缩纹理（见下） |
| TTF 文本 Canvas 缓存随字号种类增长 | 引擎无回收机制 | 换 Bitmap/SDF 字体，或定制引擎释放缓存池 |
| 场景静态资源也没释放 | 场景未勾选「自动释放资源」 | 编辑器场景属性勾选（高频主场景除外） |

## 释放机制速查（官方规定）

来源：[资源释放](https://github.com/cocos/cocos-docs/blob/master/versions/3.8/zh/asset/release-manager.md)（核对 2026-11-11）：

- **自动释放**：场景属性检查器勾选「自动释放资源」→ 切出该场景时自动释放其所有依赖资源。官方建议除高频场景（如主场景）外都勾选。
- **引用计数**：`Asset.addRef()` / `Asset.decRef()`；计数归零触发自动释放，但要先过**释放检查**：计数 0 直接销毁 → 移除缓存会对其**直接依赖**（不含后代）级联 decRef 并触发检查 → 计数非 0 时做循环引用检查，仍非 0 则终止释放。
- **手动释放**：`assetManager.releaseAsset(asset)` 只释放单个资源、必须传资源本身（不能用 uuid/url）；引擎自动释放其依赖；`release` 系列接口**不做释放检查**，资源本身一定被释放（依赖仍走检查）。
- **静态引用引擎自动统计，动态引用必须自己管**：动态加载后长期持有要 `addRef()`，节点销毁/不再引用时**务必** `decRef()` 并置 null。
- **文件级缓存**：小游戏平台有文件系统缓存，`assetManager.cacheManager.removeCache(url)` 清除（[Asset Manager](https://github.com/cocos/cocos-docs/blob/master/versions/3.8/zh/asset/asset-manager.md)「缓存管理器」）。

最小正确姿势（官方示例改注）：

```typescript
resources.load('images/background/spriteFrame', SpriteFrame, (err, sf) => {
    this.getComponent(Sprite).spriteFrame = sf;
    sf.addRef();                 // 动态持有：手动加引用
});
// 组件销毁时：
onDestroy () {
    this.spriteFrame.decRef();   // 减引用，触发自动释放检查
    this.spriteFrame = null;     // 断开 JS 侧引用
}
```

## 平台内存红线与结构（抖音 iOS，官方）

来源：[iOS 抖音小游戏内存与性能优化指南](https://github.com/cocos/cocos-docs/blob/master/versions/3.8/zh/editor/publish/bytedance-ios-optimize.md)（Cocos 3.8 官方手册，核对 2026-11-11）：

- **iOS 内存阈值**：2G RAM 机型（iPhone 6s/7/8）限制 **1G**；3G RAM 机型（7P/8P/X/XR/XS Max/11）限制 **1.4G**；超过即被系统杀进程。内存峰值必须低于阈值。
- **基础库常驻约 65M，不可优化**——做内存预算时先扣掉这块。
- **高性能模式**（`game.json` 设 `enableIOSHighPerformanceMode: true`）：iOS 获得 JIT，内测 80 分位 FPS 平均提升 69.01%（32.53→54.98）；但运行于浏览器内核环境，**内存要求更严格**，内存没优化好不建议开；普通模式与高性能模式的测试结果**不能互相复用**。
- **运行时内存结构**：JS Heap、WASM 资源、基础库+Canvas、音频、GPU 资源等；Canvas 内存与宽高成正比。
- **分项优化技巧**（官方，数字为官方给出的量级）：
  - 音频：单个双通道实例可达 **约 20M**，播完释放、精简单通道。
  - 字体：中文 TTF 加载后 **≥10M**；尽量系统字体/共享资源，或 Bitmap/SDF 字体。
  - 图片：iOS 建议 **ASTC 压缩纹理 + 禁用动态合批**，可释放 image 资源内存；ASTC 运行内存比 PNG 小超 50%，但**文件更大、增加包体**（与 [发布检查](./微信与抖音小游戏发布检查.md) 的包体约束权衡，图片放分包/远程）。
  - TTF 渲染创建的 Canvas 进缓存池，字号种类越多池越大，**引擎无回收机制**，必要时改引擎；高内存项目用 Bitmap 字体替代。
  - 对象池设最大数量阈值；按需勾选引擎模块（功能裁剪）；渲染分辨率倍数可调主 Canvas 内存。

## 第四步：如何用同一条件复测

1. **四同**：同设备、同模式（高性能/普通不混测）、同循环路径与轮数、同操作序列。
2. 修复前后各跑一遍 N 轮循环，对比**稳态内存**与**峰值内存**两条曲线；修复有效的标志是 N 轮后稳态不再逐轮抬升。
3. 主动 GC 后再读数（抖音实时内存诊断的垃圾桶按钮），排除「待回收」假象。
4. 把每轮数字记进项目记录（与[资源汇总](<./游戏开发经验与优化资源汇总.md>)「建立参数记录体系」一致），下次回归有基线。

## 本工作区项目对照（Unity）

项目是团结引擎：机制同构——Unity 的 `Resources.Load` / AssetBundle 加载的资源同样不随场景卸载自动释放，模板用 `IAssetProvider` 抽象 + `PrefabPool.ClearIdle`（bundle 卸载前清池防空引用）管理。Unity 侧排查用 Profiler Memory 模块 + Memory Profiler 包抓快照对比，思路与本文「复现→证据→分支→复测」一致；真机连接步骤、GC/纹理定位、内存红线与预算见 [Unity 真机 Profiling 与内存分析](./Unity真机Profiling与内存分析.md)。工程结构见 [Unity 小游戏项目架构](<./Unity 小游戏项目架构.md>)。

## 相关条目

- [Unity 真机 Profiling 与内存分析](./Unity真机Profiling与内存分析.md) —— Unity/团结侧真机连接、GC/纹理定位、内存红线与预算（与本文同构）。
- [对象池接入与复用](./对象池接入与复用.md) —— 池未 clear 的孤儿节点；清理责任归对象。
- [微信与抖音小游戏发布检查](./微信与抖音小游戏发布检查.md) —— ASTC 包体权衡、iOS 闪退与首包约束。
- [UI 动效选型](./UI动效选型.md) —— Spine 贴图/骨骼数据的释放与缓存模式内存差异。
- [游戏开发经验与优化资源汇总](<./游戏开发经验与优化资源汇总.md>) —— 微信内存调优、UWA 等来源导航。