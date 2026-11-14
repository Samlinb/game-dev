# Unity 真机 Profiling 与内存分析

适用：团结引擎（Tuanjie 1.x，基于 Unity 2022.3）/ Unity 2021–2022 的 WebGL 构建，转微信、抖音小游戏；也适用于标准 Unity WebGL/原生真机。触发条件：出包前性能验收、真机卡顿/掉帧、内存上涨或 iOS 闪退、GC 尖峰定位。核对日期：2026-11-12（Unity 手册按 6000.6 current 版核对，Profiler 工作流跨版本稳定；团结手册按 1.11 版核对）；2026-11-13 round 45 经镜像核实微信高精度时间（见 §2.7）。

> 本条目是 Unity 侧 Profiling 的主条目。Cocos 侧的内存排查见 [Cocos 场景切换后内存排查](./Cocos场景切换后内存排查.md)；工程结构与资源/池化系统见 [Unity 小游戏项目架构](<./Unity 小游戏项目架构.md>)；包体/资质约束见 [微信与抖音小游戏发布检查](./微信与抖音小游戏发布检查.md)。

## 0. 先记住三条会改变行动的结论

1. **Editor / Play mode 的数据不能当真机用。** CPU 含 EditorLoop 开销、内存含 Editor 自身与纹理 CPU 副本（纹理内存翻倍）。最终数值必须真机 development 包 + 进程级工具复核。（官方，[Play mode 与 Editor 样本](https://docs.unity.com/en-us/engine/6000.6/manual/analysis/profiler/play-edit-samples)、[Memory 模块介绍](https://docs.unity.com/en-us/engine/6000.6/manual/analysis/profiler/visualizing-data/memory/introduction)，核对 2026-11-12）
2. **Unity Profiler 看到的内存 ≠ 游戏总内存。** 它只覆盖引擎可监控的 Unity Heap，不含小游戏基础库、Canvas、WASM 编译内存、容器 JS 内存。微信/抖音官方明确：「Profiler 看到 200MB+ 不代表内存没问题」，总内存必须用 PerfDog / Xcode Instruments 看对应进程。（官方，[微信优化内存 QA](https://developers.weixin.qq.com/minigame/dev/guide/game-engine/unity-webgl-transform/Design/OptimizationMemory.html)、[抖音内存与崩溃 QA](https://developer.open-douyin.com/docs/resource/zh-CN/mini-game/develop/guide/performance-optimization-both/unity/running/sc_webgl_memory_optimization)，核对 2026-11-12）
3. **小游戏 Heap 只增不减、单帧内无法 GC。** Mono Heap 与 Native Heap 相互独立、空闲不共享；单帧瞬间分配或 Heap grow 扩容产生尖刺，极易 crash。优化目标是「控峰」而非「事后回收」。（官方，[团结内存优化指引](https://docs.unity.cn/cn/tuanjiemanual/Manual/MemoryOptimizationOverview.html)、[抖音内存与崩溃](https://developer.open-douyin.com/docs/resource/zh-CN/mini-game/develop/guide/performance-optimization-both/unity/running/sc_webgl_memory_optimization)，核对 2026-11-12）

## 1. 选工具：你要回答什么问题

| 你要回答的问题 | 首选工具 | 看不到的部分 | 来源 |
| --- | --- | --- | --- |
| 哪段代码耗时 / 每帧分配多少托管内存（GC 尖峰） | Unity Profiler · CPU 模块（真机 development 包） | 容器/基础库内存 | [CPU 模块参考](https://docs.unity.com/en-us/engine/6000.6/manual/analysis/profiler/visualizing-data/cpu/cpu) |
| 引擎各子系统内存分类、纹理/网格数量与占用 | Unity Profiler · Memory 模块（Simple 视图） | 同上；Detailed 视图已废弃 | [Memory 模块参考](https://docs.unity.com/en-us/engine/6000.6/manual/analysis/profiler/visualizing-data/memory/memory) |
| 谁持有这块内存 / 泄漏引用链 / 快照对比 | Memory Profiler 包（`com.unity.memoryprofiler`） | 容器/基础库/WASM 编译内存 | [包介绍](https://docs.unity3d.com/Packages/com.unity.memoryprofiler@1.1/manual/memory-profiler-introduction.html) |
| 微信 UnityHeap 底层每笔分配堆栈 | 微信 ProfilingMemory（导出 csv→SQL） | WASM 编译/显存/JS 临时内存/Mono/Lua(换分配器后) | [微信 ProfilingMemory](https://developers.weixin.qq.com/minigame/dev/guide/game-engine/unity-webgl-transform/Design/UsingMemoryProfiler.html) |
| 游戏进程总内存（含容器，判断是否撞 OOM 红线） | PerfDog / Xcode Instruments（进程级） | 内部分类（需配合上面工具） | [微信优化内存](https://developers.weixin.qq.com/minigame/dev/guide/game-engine/unity-webgl-transform/Design/OptimizationMemory.html)、[抖音内存与崩溃](https://developer.open-douyin.com/docs/resource/zh-CN/mini-game/develop/guide/performance-optimization-both/unity/running/sc_webgl_memory_optimization) |
| 团结小游戏 AB 加载膨胀（2–3 倍开销） | 团结内置 Memory Profiler 模块增强（AB 三项统计） | 需先卸载 memoryprofiler 包 | [团结 Memory-Profiler](https://docs.unity.cn/cn/tuanjiemanual/Manual/Memory-Profiler.html) |
| 线上玩家内存均值/Crash 率趋势 | 微信小游戏数据助手 / 抖音 GamePerf 评测 | 单机堆栈 | [微信内存调优总览](https://developers.weixin.qq.com/minigame/dev/guide/performance/perf-action-memory-overview.html)、[抖音评测标准](https://developer.open-douyin.com/docs/resource/zh-CN/mini-game/develop/guide/performance-optimization-both/unity/performance-optimization/unity-minigame-scoring-standards) |

**决策顺序**：先进程级工具确认「总内存是否超红线」→ 超了再用 Unity Profiler / Memory Profiler / ProfilingMemory 定位「引擎内哪块」→ 容器侧（基础库/Canvas/WASM）靠分包与压缩纹理压，不在 Profiler 里找。

## 2. 连真机：分平台步骤

### 2.1 微信小游戏（团结/Unity WebGL 转换包）

两条互斥路线，**不要同时勾**（官方明确 ProfilingMemory 路线「请勿同时勾选 Development Build」）：

**路线 A — Unity Profiler 实时连接**（看 CPU/引擎内存分类）：
1. 转换导出选项勾 `Development Build` + `Autoconnect Profiler`。
2. Unity 菜单 `Window > Analysis > Profiler` 打开窗口；Unity 自启监听端口 **34999**，WebGL 版启动 `websockify.js` 做 websocket 转发。
3. 用转换脚本导出微信包并在微信开发者工具启动 → 小游戏自动连上 Unity Profiler。
4. 连不上时：看开发者工具 Network 页签的 `ws://ip:port` 是否可达（默认指向编译机）。手工改 IP/端口在 `webgl.wasm.framework.unityweb.js`（默认 `ws://127.0.0.1:54998`）。
5. 重启监听（命令行用引擎自带 Node）：
   - Windows：`"$UNITY_PATH/Editor/Data/Tools/nodejs/node.exe" "$UNITY_PATH/Editor/Data/PlaybackEngines/WebGLSupport/BuildTools/websockify/websockify.js" 0.0.0.0:port localhost:34999`
   - macOS：`.../PlaybackEngines/WebGLSupport/BuildTools/Emscripten/node/node .../websockify/websockify.js 0.0.0.0:port localhost:34999 -vv`

注意：并非每个 Unity 小版本 Profiler 都稳定，异常时换小版本；Unity 2021 建议用 InstantGame 版（增强 Profiler 数据）。通常 `Build And Run` 打开浏览器会默认启动监听。（官方，[微信 Unity Profiler 性能调优](https://developers.weixin.qq.com/minigame/dev/guide/game-engine/unity-webgl-transform/Design/UnityProfiler.html)，核对 2026-11-12）

**路线 B — ProfilingMemory 分配堆栈导出**（看 UnityHeap 每笔 malloc）：
1. 游戏启动调用 `WeChatWASM.WX.InitSDK`；导出勾 `Profiling Funcs` + `Profiling Memory`，**不勾 Development Build**。
2. 微信开发者工具运行，自动记录所有分配。
3. 导出：性能面板左上角 `ProfilingMemory Dump`（性能面板开关：`unity-namespace.js` 的 `enableProfileStats` 或 C# `OpenProfileStats`）；或开发者工具选 `gameContext`，Console 执行 `GameGlobal.memprofiler.onDump()`。
4. 取 `游戏缓存目录/usr/alloc_used.csv`（Android 在 `data/com.tencent.mm/MicroMsg/wxanewfiles/<最近时间目录>`），导入 SQLite（推荐 DB Browser for SQLite），先格式化堆栈换行：`update alloc_used set callback=replace(callback, 'at ', x'0a')`。
5. 列含义：`callback` 堆栈 / `count` 存活分配次数 / `size` 当前占用 / `malloc` 总分配次数 / `free` 总释放次数。按 `size` 排序找最大占用堆栈。

常用 SQL（Unity 2021，官方示例）：AB 存储 `... callback like '%AssetBundle_LoadFromMemory%' or ... '%OnFinishReceiveData%' or ... '%AssetBundleLoadFromStream%'`；TypeTree `'%TypeTree%'`；Shader `'%ShaderLab%'`；IL2CPP 运行时 `'%MetadataCache%'`；非压缩纹理软解 `'%Mipmap%'`。

限制：只统计业务+引擎 CPU 内存（性能面板 DynamicMemory 可追踪部分），**不含 WASM 编译/显存/JS 临时内存/Mono**；换 Lua 分配器后也不统计 Lua。工具自身增加内存，**建议在开发者工具或 Android 用，iOS WebContent 进程压力大**；真机比编辑器更容易内存崩溃。（官方，[微信 ProfilingMemory 分析内存](https://developers.weixin.qq.com/minigame/dev/guide/game-engine/unity-webgl-transform/Design/UsingMemoryProfiler.html)，核对 2026-11-12）

**路线 C — 微信容器级（V8/JS）工具**（看 JS 侧与进程内存，补充路线 A/B）：
- 微信开发者工具内置 Chrome DevTools：`Performance`（勾 ScreenShots+Memory，看 JS Heap 曲线，点曲线定位 Main 火焰图对应函数）与 `Memory`（Heap snapshot / Allocation instrumentation on timeline / Allocation sampling）。Heap snapshot 是 GC 后的干净内存，Performance 记录的不一定经过 GC。引擎导出时建议选「调试模式」。
- Android 真机（微信 Android **7.0.7+**，开发版/体验版）：胶囊按钮 → `开发调试` → `打开性能监控面板`（实时监控）/ `Take Heap Snapshot`（快照拷到电脑，在开发者工具调试器 Memory 里 Load 查看 V8 快照）/ V8-CPU-Profile。
- PerfDog：iOS 小游戏与微信**同进程**，直接看微信进程 Memory Usage；Android **不同进程**，需在 PerfDog 选小游戏进程（深色活跃进程）。建议 USB 模式（ScreenShot 仅 USB 支持）。
- 来源：[微信开发阶段内存调优](https://developers.weixin.qq.com/minigame/dev/guide/performance/perf-action-memory-dev-profile.html)（官方，核对 2026-11-12）。

### 2.2 抖音小游戏

- 进程级总内存：Android 进程名 `minigame0`–`minigame3`；iOS 高性能模式进程名 `WebContent`。用 Xcode Instruments（Activity Monitor → 设备 → all processes）或 PerfDog 看对应进程。
- UnityHeap 分类：性能面板（MonoHeap / NativeReserved / NativeAllocated / DynamicMemory 等，每项有当前帧/最小/最大三值）；`DynamicMemory = MonoHeap + NativeReserved + 原生插件内存`。
- Unity Profiler / Memory Profiler：UnityHeap（尤其 Native）偏高时进一步分析，连接方式同微信路线 A（development + Autoconnect）。
- 抖音特色：用 `TTAssetBundle` 加载资源可有效防止 UnityHeap 增长；文件访问用平台文件系统，**不要用系统 `File` / `PlayerPrefs` 接口**。（官方，[抖音内存与崩溃](https://developer.open-douyin.com/docs/resource/zh-CN/mini-game/develop/guide/performance-optimization-both/unity/running/sc_webgl_memory_optimization)，核对 2026-11-12）

### 2.3 标准 Unity WebGL（非小游戏容器，浏览器直跑）

- 仅 development 包可 profiling；Frame Debugger 在 Web 平台不可用，只能看基础 draw call 数据。
- 自动连：`File > Build Profiles` → Web → 勾 `Development Build` + `Autoconnect Profiler` → `Build And Run`，临时本地服务器起包自动连 Profiler。
- 手动连（另一台设备/服务器）：Profiler 窗口 `Play Mode > Enable Web Profiling` → 复制窗口里的 IP:端口 → 运行包点画面上的 **Profile** 按钮 → 粘贴 IP:端口。Profile 按钮在 Default/PWA 模板自带，其他模板要自己加（`profiler.js` + `unityProfiler.createButton`）。
- 跨设备抓启动数据：改构建出的 `index.html`，`var config = {arguments: ["--player-connection-ip=IP:PORT"]}`。
- 后台采集：Player Settings → Resolution and Presentation → 勾 `Run In Background`。（官方，[Profile a Web build](https://docs.unity.com/en-us/engine/6000.6/manual/platform-specific/webgl/building-distribution/web-profile)，核对 2026-11-12）

### 2.4 Unity 原生真机（Android/iOS，对照参考）

`File > Build Profiles` → 选平台 → 勾 `Development Build`（必需）→ 可选 `Autoconnect Profiler` / `Deep Profiling Support` → `Build And Run` → Profiler 窗口 Target Selection 下拉选设备（或 `<Enter IP>` 输 IP:端口）→ Record。连接方式：局域网 / 数据线 / IP。（官方，[Collect performance data on a target platform](https://docs.unity.com/en-us/engine/6000.6/manual/analysis/profiler/profiling-applications/profiling-target-device)，核对 2026-11-12）

### 2.5 Memory Profiler 包抓快照

- 版本要求：1.x 需 Unity **2022.2+**（最低 2022.2.0a16，推荐 2022.2.0b1+）；2021.x 用 0.7.x。Package Manager 按名 `com.unity.memoryprofiler` 安装（依赖 Editor Coroutines）。团结引擎（2022.3 基）满足 1.x。（官方，[安装包](https://docs.unity3d.com/Packages/com.unity.memoryprofiler@1.1/manual/memory-profiler-install.html)，核对 2026-11-12）
- `Window > Analysis > Memory Profiler` → `Attach to Player` 下拉选 Editor 或本机/设备上的 player → `Capture`。**只能在 development player 抓快照**。
- 快照存 `MemoryCaptures/*.snap`，可导入旧快照；单击打开，选两个快照对比找泄漏。
- Editor 抓的快照含 Editor 自身内存、与 Play mode 内存无法干净分离，总量偏高——快速迭代可用，**最终结论以 player 包验证**。（官方，[抓快照](https://docs.unity3d.com/Packages/com.unity.memoryprofiler@1.1/manual/snapshot-capture.html)、[包介绍](https://docs.unity3d.com/Packages/com.unity.memoryprofiler@1.1/manual/memory-profiler-introduction.html)，核对 2026-11-12）

### 2.6 团结引擎特有工具

- **内置 Memory Profiler 模块增强**：`Window > Analysis > Profiler` 查看；Simple View — Object Stats 新增三项 AB 统计：`AssetBundles`（AB 对象本身）、`AssetBundle Memory Storages`（解压后缓存在 C++ native heap）、`AssetBundle Disk Storage`（Emscripten JS 文件系统里的 AB 文件）。背景：小游戏无真实文件系统，加载 AB 带来 **AB 文件大小 2–3 倍**开销。`ProfilerRecorder` 可取 6 个 AB 计数器（Count/Bytes，Bytes 仅 development）。**前提：从 PackageManager 卸载 `com.unity.memoryprofiler` 包**才能用此增强（与独立包互斥）。（官方，[团结 Memory-Profiler](https://docs.unity.cn/cn/tuanjiemanual/Manual/Memory-Profiler.html)，核对 2026-11-12）
- **Memory Profiler 包在小游戏平台可用**：打 development 包 + Autoconnect Profiler，启动连上 Editor 后抓内存帧分析冗余。（官方，[团结内存优化指引](https://docs.unity.cn/cn/tuanjiemanual/Manual/MemoryOptimizationOverview.html)，核对 2026-11-12）
- **Connect App（小游戏宿主调试运行）**：构建上传后在「上传记录」点查看 → Connect App 扫码运行（下载 `developer.unity.cn/connectApp/download`）。菜单 `开发调试 > 打开调试`：左上角实时帧率/内存/CPU，右下角 VConsole 按钮；`打开性能监控面板`；`Request Force GC` 强制 v8 GC。注意此页属 Unity 小游戏宿主（MiniHost），微信/抖音真机调试入口见各自平台文档。（官方，[团结调试运行](https://docs.unity.cn/cn/tuanjiemanual/Manual/MiniHostRunAndDebug.html)，核对 2026-11-12）

### 2.7 高精度时间（性能检测精度，微信特有）

来源：微信适配文档 Design/HighPreciseTime.md（官方文档内容·镜像取得 BlackTom900131/minigame-unity-webgl-transform，gh api 核实 2026-11-13；原页待浏览器复核）。

**浏览器时间精度限制**（影响性能测量准确性）：
- `Date.now()`：整型毫秒时间戳，精度固定 **1ms**。
- `performance.now()`：理论浮点微秒级，实际受浏览器限制——**Chrome 0.1ms（100μs）、Safari 1ms**。
- 端游/手游 APP 普遍微秒级；浏览器精度限制制约小游戏性能优化（测帧耗时/尖峰时注意精度下限）。

**微信高精度时间**（基础库 ≥3.5.4，仅 **Android + iOS 普通模式**，其他模式仍毫秒级）：默认关闭，需同时满足 ①勾选 `Development Build` ②启用 `Enable Perf Analysis` 导出选项。配套**性能深度分析工具**（DeepProfileTool）：集成于微信小游戏 Unity 官方插件，聚焦执行效率/内存管理/渲染效果瓶颈定位（镜像 DeepProfileTool.md 核实 2026-11-13）；详细说明书与实战案例在 doc.weixin.qq.com（JS 渲染需浏览器复核，原 wechat-miniprogram.github.io 页已封）。

**手动强制开启**（改 `weapp-adapter.js`）：定位 `const clientPerfAdapter = Object.assign` 段，删除 `isDevelopmentBuild` 与 `isProfilingBuild` 两个条件判断（`wx.getPerformance()` 微秒级，代码内 `*0.001` 统一为 ms）。

**注意**：`performance.now()` 较 `Date.now()` 有性能开销，**不建议正式版本开启**；手动配置未生效先查基础库版本 ≥3.5.4。

## 3. 定位 GC 尖峰

1. 真机 development 包连 Profiler，CPU 模块录一段含卡顿的帧。
2. 看图表 **GarbageCollector** 分类（运行托管 GC 的耗时）——尖峰即 GC 停顿。
3. Hierarchy 视图按 **GC Alloc** 列排序（= 当前帧在 scripting heap 分配的字节数），找每帧分配最大的样本；**Inverted Hierarchy** 适合揪「大量小分配累积」的问题。
4. 判读：`GC Alloc` 持续 >0 的帧 → 查该样本调用栈里的 LINQ/闭包/装箱/每帧 new 集合/字符串拼接；`GarbageCollector` 周期性尖峰 → 累积分配触发回收，降每帧分配而非调 GC 频率。
5. Editor 里查分配时注意 **Collapse EditorOnly Samples**：折叠的 Editor-only 样本（`GetComponentNullErrorWrapper`/`CheckConsistency` 等）的 GC.Alloc 不计入父样本；展开会抬高 GC.Alloc。这些 Editor-only 标记真机不存在，优化真机分配时通常忽略。（官方，[CPU 模块参考](https://docs.unity.com/en-us/engine/6000.6/manual/analysis/profiler/visualizing-data/cpu/cpu)、[Play mode 与 Editor 样本](https://docs.unity.com/en-us/engine/6000.6/manual/analysis/profiler/play-edit-samples)，核对 2026-11-12）

小游戏额外约束：单帧内无法 GC，单帧大量分配直接抬峰甚至 crash——把集中分配摊帧（对象池/分帧实例化），见 [对象池接入与复用](./对象池接入与复用.md)。

## 4. 定位纹理与内存占用

**引擎内分类**（Memory 模块 Simple 视图，官方计数器名）：
- 图表：`Total Used Memory` / `Texture Memory` / `Mesh Memory` / `GC Used Memory` / `GC Allocated In Frame`。
- Total Committed：`Tracked Memory`（in use / Reserved）+ `Untracked Memory`（原生插件/驱动、IL2CPP 元数据、可执行代码等 Unity 不感知的部分）。
- Total Memory Breakdown：`Managed Heap` / `Graphics & Graphics Driver`（纹理/RT/Shader/网格的驱动估算）/ `Audio` / `Video` / `Other` / `Profiler`。
- Objects Stats：Textures/Meshes/Materials/AnimationClips 的数量+内存、Assets、Game Objects、Scene Objects、GC allocated in frame。**这些统计在 release player 不可用**（需 development）。
- Detailed 视图已废弃，改用 Memory Profiler 包（快照对比找泄漏、memory layout 找碎片）。（官方，[Memory 模块参考](https://docs.unity.com/en-us/engine/6000.6/manual/analysis/profiler/visualizing-data/memory/memory)、[模块介绍](https://docs.unity.com/en-us/engine/6000.6/manual/analysis/profiler/visualizing-data/memory/introduction)，核对 2026-11-12）

**纹理内存判读与压缩**（团结/微信/抖音官方一致）：
- 纹理分辨率与屏幕实际显示分辨率基本一致；过大纹理降分辨率。
- ASTC 压缩；提高压缩等级 6x6→8x8→12x12（近纯色背景 12x12 比 6x6 省约 3/4）；同时调时优先保分辨率（宁选 2048×2048 ASTC12x12，不选 1024×1024 ASTC6x6）。`Compressor Quality=100`（只增 Editor 压缩耗时）。
- 不需读写的纹理取消 `Read/Write`，否则纹理内存翻倍；模型同理（上传 GPU 后可释放 vertex/index buffer）。
- UI/CubeMap 取消 `Generate Mip Maps`（省约 1/3；小游戏平台不支持 Texture Streaming）。抖音建议贴图 maxsize ≤1024、尽量不生成 Mipmap、不用可写属性。
- Unity 2021 起才支持移动端压缩纹理；之前用 RGBA/DXT 会巨大膨胀+运行时软解（ProfilingMemory 里 `'%Mipmap%'` 堆栈即软解）。
- 关 HDR：内置管线 Graphics Settings 的 **tier2**（WebGL 用 tier2）取消 `Use HDR`，省约 40MB 显存+少一次 RT 拷贝；URP 在 renderer 配置关。（官方，[团结内存优化指引](https://docs.unity.cn/cn/tuanjiemanual/Manual/MemoryOptimizationOverview.html)、[微信优化内存](https://developers.weixin.qq.com/minigame/dev/guide/game-engine/unity-webgl-transform/Design/OptimizationMemory.html)、[抖音性能优化总览](https://developer.open-douyin.com/docs/resource/zh-CN/mini-game/develop/guide/performance-optimization-both/unity/performance-optimization/sc_webgl_performance_optimization)，核对 2026-11-12）

**AB 与缓存**：首资源包永远占内存且无法释放；AB 按需加载及时释放、解压占 Native 内存故减小 AB；**避免 Unity 自带文件缓存**（首资源包/Addressable Cache/`WWW.LoadFromCacheOrDownload`）——Emscripten 文件系统占用与文件同等大小的内存。微信 `UnityWebRequest` 加请求头 `wechatminigame-preload:1` 可预下载到用户目录而不增 Unity 内存（外部案例：SDK CHANGELOG，核对 2026-11-12）。团结用 AssetStudio 查不同 AB 间重复的 Shader/Font 冗余。（官方，[微信优化内存](https://developers.weixin.qq.com/minigame/dev/guide/game-engine/unity-webgl-transform/Design/OptimizationMemory.html)、[团结内存优化指引](https://docs.unity.cn/cn/tuanjiemanual/Manual/MemoryOptimizationOverview.html)；SDK CHANGELOG 见 [仓库](https://github.com/wechat-miniprogram/minigame-tuanjie-transform-sdk)，核对 2026-11-12）

## 5. Editor 与真机数据差异：哪些可信、哪些必须真机

| 指标 | Editor/Play mode | 真机 development 包 | 处理 |
| --- | --- | --- | --- |
| CPU 耗时 | 含 EditorLoop（归入 Others）、Editor-only 安全检查、Prefab 活动 | 仅 PlayerLoop | 相对排序可参考，绝对值必须真机 |
| GC.Alloc | Editor-only 样本折叠时不计入父样本 | 真实业务分配 | 真机为准；Editor 查分配忽略 Editor-only 标记 |
| 纹理内存 | Editor 视纹理为 read/write，保留 CPU 副本→**翻倍** | 实际 GPU/压缩后占用 | 纹理内存必须真机 |
| 总内存 | 含 Editor 自身+Profiler 自身内存，无法分离 | 引擎可监控部分 | 真机 + 进程级工具看总量 |
| Deep Profiling | 对 PlayerLoop+EditorLoop 所有调用都加开销 | 仅 player | 慎用，会显著拉低帧率 |

官方机制：Play mode 只采 PlayerLoop 样本，EditorLoop 归 Others；Memory 模块在目标为 Play mode/Editor 时顶部显示警告。结论：**Editor 用于快速迭代与相对定位，发布数值一律真机 development 包 + PerfDog/Instruments 复核**。（官方，[Play mode 与 Editor 样本](https://docs.unity.com/en-us/engine/6000.6/manual/analysis/profiler/play-edit-samples)、[Memory 模块介绍](https://docs.unity.com/en-us/engine/6000.6/manual/analysis/profiler/visualizing-data/memory/introduction)，核对 2026-11-12）

**iOS 测内存的特殊纪律**（微信/抖音官方一致）：iOS 测内存**务必不要开 development / profilingmem 模式**（工具自身内存会污染读数且加剧 WebContent 压力）；务必用代码分包+压缩纹理；用 PerfDog/Instruments 看 WebContent 进程。（官方，[微信优化内存 QA](https://developers.weixin.qq.com/minigame/dev/guide/game-engine/unity-webgl-transform/Design/OptimizationMemory.html)、[抖音内存与崩溃 QA](https://developer.open-douyin.com/docs/resource/zh-CN/mini-game/develop/guide/performance-optimization-both/unity/running/sc_webgl_memory_optimization)，核对 2026-11-12）

## 6. 内存红线与预算（硬约束，用前复核）

### 6.1 进程总内存峰值建议（官方）

| 平台 | 低档机 | 中高档机 | 来源（核对 2026-11-12） |
| --- | --- | --- | --- |
| 微信 Android | < 1.2G | < 1.5G | [微信优化内存](https://developers.weixin.qq.com/minigame/dev/guide/game-engine/unity-webgl-transform/Design/OptimizationMemory.html) |
| 微信 iOS | < 1G | < 1.4G | 同上 |
| 抖音 iOS | < 1G | < 1.4G | [抖音内存与崩溃](https://developer.open-douyin.com/docs/resource/zh-CN/mini-game/develop/guide/performance-optimization-both/unity/running/sc_webgl_memory_optimization) |

- 机型档：iOS 低档 = iPhone 6sp/7/8（RAM 2G）；中高档 = 7P/8P/X/11（RAM 3G+）。
- iOS 高性能模式：3G RAM 机型系统上限 **1.5G**，**安全峰值约 1.2–1.3G**；进程离 1.5G 还远就崩 → 查 UnityHeap 预留是否足够。
- iOS 高性能+模式：保留游戏独立进程 + 渲染挪回微信进程（GPU 内存移到平台进程微信 WebContent / 抖音进程），改善渲染效果与内存；建议 WebGL2、内存压力大的游戏开启，开启后验证进程内存/渲染兼容性/帧耗时。
- **高性能模式开通与判别**（iOSOptimization.md，镜像核实 round 39）：开通 = MP 后台 → 能力地图 → 研发能力 → 生产提效包 → 开通高性能模式，`game.json` 设 `iOSHighPerformance:true`（去掉开关回退普通模式对比）；判别 = 删本地小游戏重进开调试，vconsole「game start」日志 `render="h5"` 即高性能；系统要求 iOS≥14.0、基础库≥2.23.1（约 90% 用户），不满足回退普通。**坑**：高性能模式勿服务端设 Cookie（跨域读不到）；业务代码无需改、普通/高性能无缝切换；启动初始 CPU 高峰用于 WASM 编译优化，代码分包后回落。
- iOS 小游戏与客户端同进程（微信普通模式），总内存撞 OOM 阈值即被系统 kill；微信有 `wx.onMemoryWarning` 回调，连续多次告警会主动销毁小游戏。
- 进程名：微信 Android `WeChat AppBrand1/2`、iOS 普通 `WeChat`/高性能 `WebContent`；抖音 Android `minigame0–3`、iOS `WebContent`。PerfDog iOS 以紫色 XcodeMemory 为准。
- Cocos 侧同源红线（2G RAM→1G、3G RAM→1.4G、基础库常驻约 65M）见 [Cocos 场景切换后内存排查](./Cocos场景切换后内存排查.md)，与上表 iOS 数值一致（同为 iOS 系统级 OOM 限制）。

### 6.2 内存结构预算公式（官方）

`总内存 = 基础库 + Canvas + WASM编译内存 + UnityHeap + Gfx显存 + 音频 + JavaScript内存`
`UnityHeap = max(托管/Mono内存) + max(Native/Reserved内存 + C原生代码内存)`

典型量级（官方举例，核对 2026-11-12）：
- 基础库：微信约 **100–150MB**；抖音约 **70–90MB**。Canvas 与物理分辨率成正比（iPhone 11 Pro Max 约 **80MB**）。
- WASM 编译：iOS 上 **30MB 未压缩代码 ≈ 300MB 运行时编译内存**；团结给出「编译+JIT 总内存约为 WASM 文件的 **10 倍**」。代码分包可降编译内存 **50%+**。
- 例（iOS，30MB 代码）：基础库 100MB + Canvas 70MB + 编译 300MB + UnityHeap + Gfx + 音频 + JS(<100MB)。要支持低档机（<1G），**业务侧（UnityHeap+Gfx+音频+JS）需控制在约 500MB**。
- 来源：[微信优化内存](https://developers.weixin.qq.com/minigame/dev/guide/game-engine/unity-webgl-transform/Design/OptimizationMemory.html)、[抖音内存与崩溃](https://developer.open-douyin.com/docs/resource/zh-CN/mini-game/develop/guide/performance-optimization-both/unity/running/sc_webgl_memory_optimization)、[团结内存优化指引](https://docs.unity.cn/cn/tuanjiemanual/Manual/MemoryOptimizationOverview.html)。

### 6.3 UnityHeap 预留内存设置（官方）

- 设法：开性能面板跑一段时间，观察 `DynamicMemory` 峰值；`UnityHeap ≈ DynamicMemory + 少量静态内存(<10MB)`，预留设为略大于峰值（多留 50–100MB）。
- 微信建议值：超休闲 **256MB**、中度（模拟经营/卡牌）**496MB**、重度（SLG/MMO）**768MB**。
- 微信硬限制：`UnityHeap ≥ 1024MB` 大部分设备启动失败；`≥ 500MB` 32 位微信（约 5% 用户）与 iOS 普通模式大概率启动失败——中轻度游戏不超过 500MB。
- 抖音示例：测试中位 200MB → 设 256MB；`DynamicMemory` 峰值不要超过 500M。
- 来源：[微信优化内存](https://developers.weixin.qq.com/minigame/dev/guide/game-engine/unity-webgl-transform/Design/OptimizationMemory.html)、[抖音内存与崩溃](https://developer.open-douyin.com/docs/resource/zh-CN/mini-game/develop/guide/performance-optimization-both/unity/running/sc_webgl_memory_optimization)（核对 2026-11-12）。

### 6.4 抖音线上评测标准（官方，2026-06-01 版）

- **iOS 默认高性能模式**，上报的内存是抖音客户端主进程内存、与游戏本身无关 → iOS 用**内存异常退出率**考核，不用绝对内存值。休闲益智类达标线：高档 0.50% / 中档 1.62% / 低档 5.01%。
- **Android 用内存均值（MB）**，休闲益智类（优秀/平台建议/达标）：高档 1086/1307/1491、中档 988/1197/1371、低档 960/1166/1350；内存占用率高档 23%/29%/36%。
- 评测工具 **GamePerf**：记录上传取平均，每机型测 3 组再平均，内存峰值取最大值。机型按 CPU/GPU/RAM 分高/中/低档（覆盖 99% 用户）。
- 来源：[抖音 Unity 小游戏性能评测标准](https://developer.open-douyin.com/docs/resource/zh-CN/mini-game/develop/guide/performance-optimization-both/unity/performance-optimization/unity-minigame-scoring-standards)（页面更新 2026-06-23，核对 2026-11-12）。

### 6.5 音频内存（官方）

不用 fmod 播长音频（BGM）；同时存在音频数 ≤ 20；强制单声道（双声道 2 倍内存）；团结：加载方式 `Compressed In Memory` + `Force To Mono` + `Quality=1`（普通浏览器因安全保护不要用压缩音频，可能播放异常）。来源同上微信/抖音/团结文档（核对 2026-11-12）。

## 7. 复测方法（与 Cocos 条目同构）

1. **四同**：同设备、同模式（iOS 高性能/普通不混测；测内存不开 development/profilingmem）、同循环路径与轮数、同操作序列。
2. 修复前后各跑 N 轮（建议 ≥10）相同路径，记录**稳态内存**与**峰值内存**两条曲线；有效标志 = N 轮后稳态不再逐轮抬升。
3. 主动 GC 后读数（团结 Connect App `Request Force GC` / 抖音实时内存诊断垃圾桶按钮），排除「待回收」假象。
4. 进程级（PerfDog/Instruments）确认总内存低于 6.1 红线；引擎级（Profiler/Memory Profiler）确认 DynamicMemory 峰值低于 6.3 预留。
5. 每轮数字记进项目记录，建立回归基线。泄漏排查的「复现→证据→分支→复测」分支表见 [Cocos 场景切换后内存排查](./Cocos场景切换后内存排查.md)（Unity 机制同构：`Resources.Load`/AB 资源不随场景卸载自动释放）。

## 8. 本工作区项目对照（团结 + 微信/抖音模板）

- 模板 [projects/MiniGameTemplate](../projects/MiniGameTemplate) 的资源走 `IAssetProvider` 抽象（编辑器 Resources、生产 `RES_USE_BUNDLE` 切 AssetBundle），池用 `PrefabPool`（bundle 卸载前 `ClearIdle` 防空引用）。Profiling 时重点看：AB 加载是否触发团结增强模块的 2–3 倍膨胀、池是否 clear、`ResManager.RemoteBundleBaseUrl` 远程包是否误用 Unity 文件缓存。
- 模板未内置 Profiler 接入代码（grep 无 profiler 引用，2026-11-12 实测）；微信 SDK 的 `WXProfileStatsScript`（性能面板）与 `WX.InitSDK` 在装 SDK 后按第 2.1 节接入。
- 出包步骤见模板 `docs/build-wechat.md`、`docs/build-douyin.md`；架构与资源口径见 [Unity 小游戏项目架构](<./Unity 小游戏项目架构.md>)。
- **待项目实测**：模板首次出包后在微信开发者工具/抖音 IDE 实连 Profiler，记录实际端口与连接成功率；iOS 真机用 Instruments 记 WebContent 进程峰值，回填 6.1/6.3 的本项目基线。

## 相关条目

- [Cocos 场景切换后内存排查](./Cocos场景切换后内存排查.md) —— 泄漏分支表、引用计数释放机制、抖音 iOS 红线（Cocos 侧）。
- [Unity 小游戏项目架构](<./Unity 小游戏项目架构.md>) —— IAssetProvider/PrefabPool/出包口径。
- [微信与抖音小游戏发布检查](./微信与抖音小游戏发布检查.md) —— 包体/分包约束（与压缩纹理增包体权衡）。
- [对象池接入与复用](./对象池接入与复用.md) —— 降单帧分配、控 GC 尖峰。
- [游戏开发经验与优化资源汇总](<./游戏开发经验与优化资源汇总.md>) —— UWA/Unity 博客等来源导航。
- [小游戏启动优化](./小游戏启动优化.md) —— 启动链路各段耗时归因、首包/WASM 精简、抖音 TTI 阈值。