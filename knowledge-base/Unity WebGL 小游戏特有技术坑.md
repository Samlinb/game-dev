# Unity WebGL 小游戏特有技术坑

> 适用范围：**团结引擎 / Unity 2022.3+ WebGL 构建**导出的**微信 + 抖音小游戏**。
> 不适用于：Unity 导出的浏览器 WebGL 站（不走小游戏壳，部分 API 行为不同）、原生小游戏（非 Unity）、Cocos 小游戏。
> 写作思路：从选型决策（HybridCLR vs IL2CPP）→ WASM 编译坑 → 平台 API 差异 → Shader 兼容 → 团结引擎 unity-webgl-transform-sdk 链路坑，每个坑点给出现象、根因、应对和来源。
> 互链约定：白屏/wasm 编译报错/Shader 报错 → [真机白屏与资源加载失败排查](./真机白屏与资源加载失败排查.md)；WASM 体积/分包/首包精简 → [小游戏启动优化](./小游戏启动优化.md)；发布包体上限 → [微信与抖音小游戏发布检查](./微信与抖音小游戏发布检查.md)。

---

## 1. 脚本后端选型：HybridCLR vs 纯 IL2CPP

### 1.1 两个选项是什么

| 方案 | 本质 | 产出 |
|------|------|------|
| **纯 IL2CPP**（Mono + IL2CPP 编译） | Unity 官方后端：AOT 编译，所有托管代码编成 WASM 机器码，无运行时 JIT | 单个 `.wasm` + `.framework.js`，所有业务代码固定在包体里 |
| **HybridCLR + IL2CPP** | HybridCLR 在 IL2CPP 之上加一层「热更 DLL 解释器」：AOT 部分（引擎 + 基础框架）编成 WASM，热更业务代码以 `.dll` 形式运行时加载 | 首包 wasm（不含热更业务代码） + 远程/本地热更 dll 列表 |

### 1.2 边界条件

**纯 IL2CPP 是 Unity WebGL 的唯一 Scripting Backend**——Mono（非 IL2CPP）在团结引擎小游戏链路里**直接 Loading 卡死**（KB-004 §2.4），原因是 Mono 需要 `blazor.boot.json` 配置文件，而微信 unity-webgl-transform 工具链不产出它。

**HybridCLR 官方声称支持 WebGL + 微信 + 抖音小游戏**（来源：HybridCLR 支持平台表，核对日期 2026-11-12）。但注意以下硬约束：

| 约束 | 详情 | 来源 |
|------|------|------|
| 必须 IL2CPP 后端 | HybridCLR 依赖 IL2CPP 的 `il2cpp.dll` + 额外的桥接代码 | hybridclr.cn 官方文档 |
| 额外包体 | HybridCLR 运行时：196KB + MethodBridge.cpp * 0.049（典型项目约 +250KB~500KB） | hybridclr.cn/docs/basic/impactonappsize |
| 热更 dll 加载内存 | 社区版：dll 大小 × 4.7；商业化版：dll 大小 × 2.9 | hybridclr.cn/docs/basic/memory |
| iOS 15.4 BUG | iOS 15.4 系统上 HybridCLR 小游戏报 `Not implemented` / `Class::FromIl2CppType` 卡死 | hybridclr.cn Common Errors；解法：微信 wasm 代码分包工具 |

### 1.3 什么时候选哪个

**选纯 IL2CPP + 微信 wasm 代码分包**（推荐）：
- 业务代码体量 ≤ 30MB（典型轻量级小游戏，热更带来的内存收益不明显）
- 团队不想维护 HybridCLR 额外工具链（link.xml + AOT 补充元数据 + 自动收集泛型）
- iOS 15.4 必须稳定运行（HybridCLR 有已知系统级 BUG）
- 模板工程 `projects/MiniGameTemplate/` 就是这个方案——**没有 HybridCLRSettings.asset**，说明当前项目架构默认纯 IL2CPP

**选 HybridCLR + IL2CPP**：
- 业务代码 > 30MB，热更后能把 wasm 体积从 ~30MB 减到 ~20MB（Brotli 后减少 ~2MB 首包下载）
- 有强热更需求（活动开关、数值表远程更新、紧急线上 Bug 修复）
- 预算了热更 dll 加载内存（×3~5 倍膨胀）

**不选 Mono（非 IL2CPP）**：团结引擎 unity-webgl-transform 链路根本不支持。

**来源**：HybridCLR 官方支持平台（2026-11-12 WebFetch）、HybridCLR 包体影响（同日期）、KB-004 §2.4（Scripting Backend 非 IL2CPP Loading 卡死）、团结引擎 WebGLPlatform 页（纯 IL2CPP 是唯一官方后端）、模板工程 MiniGameTemplate 无 HybridCLRSettings.asset（项目实测 Glob）。

---

## 2. WASM 编译阶段坑

### 2.1 体积 → 编译耗时 → 内存消耗的连锁关系

团结引擎实测数据（来源：WebGLPlatform 页，核对 2026-11-12）：

| 环节 | 体积/耗时/内存 | 说明 |
|------|--------------|------|
| WASM 文件本身 | 典型 33.8MB | Brotli 压缩后 ~6MB 下载量 |
| 浏览器编译阶段内存 | **约是文件大小的 10 倍** | 33.8MB wasm → 编译多占 ~340MB，是原生 App 没有的额外消耗 |
| WASM Heap 未分配 | +90MB | Unity WebGL Heap 自动扩容上限 PlayerSettings 里可调 |
| JS File System copy | +60MB | Emscripten 模拟的文件系统 JS 层始终持有一份文件副本 |

iOS 小游戏整机内存红线：低档机 ≤ 1GB、高档机 ≤ 1.4GB。**任何一段超了就会被操作系统 OOM 杀掉**（非 WebGL 专属，iOS 机制，但 WebGL 的额外 ~490MB 消耗让这条线特别紧）。

### 2.2 wasm 编译失败典型原因

| 原因 | 现象（vConsole） | 诊断 | 修复 |
|------|------------------|------|------|
| **内存不足** | `WebAssembly.Memory allocation failed` / `Out of Memory` / Loading 50-80% 卡住 | 查 Player Settings → Web → Configuration → **WebGL Memory Size**（默认 256MB，iOS 建议 ≤ 128MB）；查 Player Settings → Web → Other Settings → Managed Stripping Level 是否 High | Memory Size 降到真机实测能跑的最低值；配合代码裁剪 + wasm 分包 |
| **代码裁剪过度** | 启动时报 `Missing method` / `MethodAccessException` / 反射调用空引用 | 用 dotPeek 拆 `ManagedStripped` 目录下的 dll 看关键方法是否被裁；微信 wasm 分包工具首次导出会提示"需要首次拉取代码分包" | 加 `link.xml` 保留反射/序列化/动态调用的类型；更新分包工具后重新导出 |
| **IL2CPP 版本不匹配** | Loading 条直接卡死无报错，或 Console 报 `Invalid opcode` | 团结引擎版本 vs Unity 基础版本 vs 微信 unity-webgl-transform SDK 版本是否对齐 | 保持三者同主版本号；用团结引擎自带的 unity-webgl-transform 插件 |
| **iOS 15.4 HybridCLR BUG** | `Not implemented` / `Class::FromIl2CppType` | HybridCLR + iOS 15.4（含 15.4.1）组合 | 这是 Unity + iOS 15.4 的 WebKit/il2cpp 已知 BUG，HybridCLR 官方给两个方案：①微信 wasm 代码分包（推荐，步骤见 §2.4）；②对 `GenericMetadata::InflateIfNeeded` 关闭 clang 优化（步骤见 §2.4，2026-11-13 全文核实）。用户端升级 iOS 也可绕过 |

### 2.3 诊断步骤

1. **先看 wasm 压缩后体积**：从 CDN/导出目录拿到 `.wasm.br` 或 `.wasm.gz`，压缩后 > 6MB 需要分包
2. **再看 Player Settings 三个关键配置**：
   - Scripting Backend = **IL2CPP**
   - Managed Stripping Level = **High**
   - Strip Engine Code = **开**
   - WebGL Memory Size = 先 128MB 验证，再往上调
3. **用微信 wasm 代码分包工具**（wasmCodeSplit，Unity 导出插件）把首包 wasm 压到 3-5MB，子包 7-15MB
4. **真机 Profiler**：查 `Total Memory / Used Heap`，iOS 低档机 Heap 峰值 ≤ 600MB 比较安全

**来源**：团结引擎 WebGLPlatform 页（2026-11-12 WebFetch）、Unity Manual Memory in Unity Web（2026-11-12 WebFetch）、KB-004 §2.5（wasm 编译失败 → 白屏）、微信 wasm 代码分包工具 WasmSplit.html（2026-11-13 WebFetch 正文）。

### 2.4 iOS 15.4 的 wasm 分包解法步骤（HybridCLR 官方推荐方案①）

来源：[HybridCLR Common Errors（中文）](https://www.hybridclr.cn/docs/help/commonerrors)（iOS 15.4 条目，2026-11-13 WebFetch 完整正文，round 17）、[微信 使用代码分包工具](https://developers.weixin.qq.com/minigame/dev/guide/game-engine/unity-webgl-transform/Design/WasmSplit.html)（2026-06-29 更新，2026-11-13 WebFetch 正文）。官方规定。

**适用**：Unity/团结引擎导出的微信小游戏（`game.json` 的 `plugins` 为 `UnityPlugin`）。通用适配线（`WXGameKit`）用「Wasm代码分包V2」，两条线的分包插件不可混用。

操作步骤：

1. 微信开发者工具「扩展」视图安装并启用 `wasmCodeSplit` 分包插件（Unity 专用线）。
2. 项目初始化 → 启用代码分包。
3. 真机收集：用开发者工具的「预览」（**不是真机调试**）在 Android、iOS 上分别跑，覆盖启动后最先进入的场景/关卡；新增函数个数稳定后点继续。
4. 重复收集/生成：每次新增函数趋稳后生成分包；上线前生成 **release** 版。
5. 上线前检查：首包函数占原始包函数总数 **≥ 30%**（Unity 专用线口径）、新增收集函数个数清零；关注 `wait for func`（普通模式子包过早加载）和 `fetch js`（iOS 高性能按函数拉取）日志，有则继续收集。
6. 上线后：保留小游戏包，持续关注「新增收集函数个数」，出现新增就重新生成分包并提审。

**结果判据**：分包后 `wasmcode`（首包）约 3–5MB、子包 7–15MB、三者合计 10–25MB；iOS 高性能模式下子包不整体加载，转为按函数粒度按需加载。

**边界**：本步骤解决「单文件 wasm 过大触发 iOS 15.4 WebKit 编译缺陷」。

**方案②（Unity WebGL 官方论坛临时修复，HybridCLR 官方文档转述）**：对 `GenericMetadata::InflateIfNeeded` 函数关闭 clang 优化——

1. 打开 `Library/HybridCLR/LocalIl2CppData-xxx/il2cpp/libil2cpp/metadata/GenericMetadata.cpp`。
2. 在 `GenericMetadata::InflateIfNeeded` 函数定义前加一行 `#pragma clang optimize off`。
3. 在该函数结束后加 `#pragma clang optimize on`。

来源：[HybridCLR Common Errors（中文）](https://www.hybridclr.cn/docs/help/commonerrors) iOS 15.4 条目，2026-11-13 WebFetch 取到完整正文（round 17；round 15 的「页面截断」问题本轮未复现）。官方规定。

---

## 3. 平台 API 行为差异

Unity WebGL 平台在浏览器沙盒里跑 WASM，底层 JS 封装了各种原生 API 调用。小游戏壳又在浏览器之上加了一层微信/抖音 Runtime API（`wx.*` / `tt.*`），导致很多 Unity API 行为和你想的不一样。

### 3.1 PlayerPrefs → IndexedDB + 1MB 硬限制 + 小游戏切断持久化

**现象**：存档读不回来、Unity 控制台/真机 vConsole 报 `PlayerPrefsException`。

**根因**：
- Unity WebGL PlayerPrefs 底层用浏览器 IndexedDB，**单域名总上限 1MB**（Unity Scripting API 明确写了 "limited to 1 megabyte"，超出抛 `PlayerPrefsException`）
- 小游戏环境下：微信 unity-webgl-transform **切断了 JS File System 和 IndexedDB 的同步**（团结引擎 FAQ + WebGLPlatform 页），即使 Unity 往 IndexedDB 写了，小游戏退出时清进程也可能丢

**替代方案**（按推荐度排序）：

| 方案 | 容量 | 特点 | 接入 |
|------|------|------|------|
| **云开发 / 自建服务器** | 无上限 | 玩家删除小游戏不丢存档 | WX Cloud / 自建后端 + UnityWebRequest |
| **微信 SDK 文件 API** | 文件系统 200MB | 异步，需用 C# WX SDK 包装 | `WXApi.GetFileSystemManager()` |
| **微信 SDK Storage** | 10MB | 同步接口（会阻塞主线程） | `WXApi.SetStorage` / `WXApi.GetStorage` |
| **Unity PlayerPrefs** | 1MB + 不可靠 | 仅临时调试用 | Unity 原生 API |

**关键**：项目架构里应该有一个 `IPlatformStorage` 抽象层，WebGL/Editor stub 走 PlayerPrefs，微信/抖音走各自 SDK。模板工程 `SaveManager.cs` **没有**这个抽象（round 14 全工程 grep 确认，直接用 PlayerPrefs + JsonUtility，见 §7/§9）。

**来源**：Unity Scripting API PlayerPrefs（WebSearch 摘要 + Unity Manual webgl-debugging）、团结引擎 WebGLPlatform 页（JS File System 切断 IndexedDB 同步）、团结引擎 FAQ 存档说明、微信 Unity 适配 QA §8。核对日期 2026-11-12。

### 3.2 Application.persistentDataPath → 根本不存在

**现象**：代码里 `File.WriteAllText(Application.persistentDataPath + "save.json", ...)` 跑完无异常，但下次进游戏文件没了。

**根因**：
- Unity WebGL 走 Emscripten JS + IndexedDB 模拟文件系统，`persistentDataPath` 指向这个模拟路径
- **小游戏切断了 IndexedDB 同步**（团结引擎明确写了"不会有卡顿问题，但额外内存消耗依旧存在，并且游戏退出后文件不会保存下来"）
- streamingAssetsPath 在 WebGL 下指向远程 CDN 地址，不能 `File.WriteAllText`

**修复**：和 PlayerPrefs 同一个根因。**小游戏里用任何文件 IO 前，先查 `Application.platform` 是不是 `RuntimePlatform.WeChatMiniProgram` 或 `RuntimePlatform.DouyinMiniProgram`**，然后走对应 SDK 文件 API。

**来源**：团结引擎 WebGLPlatform 页文件系统节（2026-11-12 WebFetch）、团结引擎 FAQ（存档节）。

### 3.3 Application.isFocused → 不完全可靠

**现象**：Unity 代码 `if (Application.isFocused) ...` 判断游戏是否在前台，但小游戏切后台回来这个标志有时候不触发。

**根因**：小游戏宿主 Runtime（微信/抖音）有自己的前后台管理，切后台时不是 WebGL 标准的 `visibilitychange` 事件，而是 Runtime 层先切。Unity WebGL 监听的是浏览器层事件，可能比 Runtime 慢一拍。

**修复**：同时监听 Unity 的 `Application.focusChanged` + 微信/抖音 SDK 的 `wx.onShow` / `wx.onHide` / `tt.onShow` / `tt.onHide`，两个事件任一触发都更新前台状态。

**来源**：微信 Unity 适配 QA 平台适配节（WebSearch 摘要）、项目经验。

### 3.4 Input → 触控映射、Unity Input System 在 WebGL 的限制

**Unity Input System 在 WebGL 下**：

| 输入类型 | 原生 Unity Input | Unity Input System (new) | 小游戏特殊 |
|----------|-----------------|-------------------------|-----------|
| 触控 | `Input.touches` | `Touchscreen.current` + `Pointer` | 基本都能用，走浏览器 TouchEvents |
| 键盘 | `Input.GetKey` / `Input.GetAxis` | `Keyboard.current` | 小游戏默认没键盘，开发工具模拟器有，真机只有外接蓝牙键盘（极少数用户） |
| 鼠标 | `Input.mousePosition` | `Mouse.current` | 手机小游戏没鼠标，模拟为触控（Pointer device） |
| 加速计 | `Input.gyro` | `Gyroscope.current` | 需要 `wx.onDeviceMotionChange` 或 `wx.startDeviceMotionListening`，**Unity WebGL 的 `Input.gyro` 在小游戏里不工作** |
| 摇杆/游戏手柄 | `Input.GetJoystickNames` | `Gamepad.current` | 依赖浏览器 Gamepad API + 用户先按键激活安全检查 |

**关键坑点**：
- Unity WebGL Input System 在小游戏里不触发「激活」——用户触摸屏幕不等于激活了 Input System，需手动在第一个 Touch 回调里调 `InputSystem.EnableDevice(Touchscreen.current)` 或设置 `Player Settings → Input → Active Input Handling` 为 Both
- 小游戏 Accelerometer/Gyroscope 需要显式开启：`wx.startAccelerometer({ interval: 'game' })`，Unity 的 `SystemInfo.supportsGyroscope` 在小游戏里**返回 true 但实际读不到数据**

**来源**：Unity Manual Input in Web（2026-11-12 WebFetch，完整章节保存）、团结引擎 MiniGamePlatform 页（设备 API 适配）、微信 Unity 适配 QA（Sensor 节，WebSearch 摘要）。

### 3.5 System.Net / System.Net.Sockets / System.Net.Http → 直接不支持

**现象**：打包时 IL2CPP 报错 `Unable to load DLL`，或游戏启动后网络请求一直 timeout。

**根因**：
- 浏览器沙盒不暴露 IP Socket，Unity WebGL 禁止加载非源码编译的 DLL
- `System.Net.Sockets`（TCP/UDP）、`System.Net.Http`（HttpClient）、`System.Net.WebSockets` 在 Unity WebGL 构建时**直接报错**
- UnityEngine.Networking（旧版 Networking 包）也不支持

**替代**：
- HTTP 请求 → **UnityWebRequest**（底层浏览器 Fetch API，自动走 CORS）
- TCP → **WebSocket**（UnityWebSocket 社区库，底层浏览器 WebSocket API）
- 小游戏环境下 → UnityWebRequest 会被 unity-webgl-transform 工具自动替换成 `wx.request` / `tt.request`

**关键**：UnityWebRequest 必须用协程 `yield return uwr.SendWebRequest()`，**不能 while(!isDone) 阻塞**——WebGL 单线程，阻塞会卡死游戏（团结引擎明确给出了反例代码）。

**来源**：Unity Manual Web networking（2026-11-12 WebFetch，明确列出不支持的 .NET 类）、团结引擎 WebGLPlatform 页网络节（反例代码）、微信 Unity 适配 QA Unity WebGL §1-§2。

### 3.6 Texture 2D 格式在小游戏的坑（与 KB-004 §3.5 互补）

Unity Manual Texture compression in Web 详细说明：
- Desktop 浏览器支持 DXT/S3TC，移动浏览器支持 ASTC/ETC2
- 如果设备不支持某个格式，Unity 会**软件回退解压**——能跑但吃内存 + 慢
- 微信小游戏 Android 真机的 WebGL 渲染管线对 ASTC/ETC2 支持不一致，紫色块 / 全透明通常是这个原因

**修复**：
1. 打包前 Texture Import Settings → Compression 先设 **RGBA32（未压缩）** 验证所有机型正常
2. 逐步尝试格式：ASTC（微信 Android）→ ETC2（iOS）→ DXT（桌面浏览器）
3. 微信 Unity 适配工具的「多包融合」功能（MultiPackage）可以自动出两套 texture 格式的包

**来源**：Unity Manual Texture compression in Web（2026-11-12 WebFetch）、KB-004 §3.5。

---

## 4. Shader 兼容性

### 4.1 WebGL 1.0 vs 2.0 硬差异

| 特性 | WebGL 1.0（OpenGLES 2.0） | WebGL 2.0（OpenGLES 3.0） |
|------|--------------------------|--------------------------|
| 最低 Shader Language | GLSL ES 1.00 | GLSL ES 3.00 |
| half 精度 | **不支持**（Shader Precision Model 必须 Float） | 支持 |
| Compute Shader | ❌ | ❌（小游戏 GPU 不支持） |
| Transform Feedback | ❌ | ❌ |
| 延迟渲染 | ❌ | ✅ |
| 线性色彩空间 | ❌ | ✅ |
| GPU Skinning | 弱（CPU fallback） | 支持 |
| URP 后处理 | ❌（CoreBlit 等不支持） | ✅ |

**微信 Unity 适配默认导出 WebGL 1.0**。如果项目里用了 WebGL 2.0 专属特性（比如 URP 完整后处理），在微信真机上会**黑屏 + Shader 编译报错**（KB-004 §2.3 已覆盖）。

**修复路径**：
1. Player Settings → Web → Other Settings → Rendering → **强制只勾 WebGL 2** 或 **强制只勾 WebGL 1**（不要让 Unity Auto 选择，否则会为两个 API 各编一套 Shader 变体 + 首包膨胀）
2. 用 URP 必须走 WebGL 2，并确认 iOS 基础库版本 ≥ 目标要求
3. 不用 URP 的项目（Unity 内置管线 / BRP）基本不会踩这个坑

### 4.2 URP 隐藏 Shader 的 WebGL 1.0 黑名单

微信 Unity 适配 QA 明确列出在 WebGL 1.0 下编译失败的 URP 隐藏 Shader：
- `Hidden/Universal/CoreBlit`（URP Blitter Pass 1）
- `Hidden/Universal Render Pipeline/LutBuilderLdr`（颜色分级）

**修复**（两个选一个）：
1. 切 WebGL 2.0
2. URP Asset 里禁用 Color Adjustments，CoreBlit.shader 只保留第一个 Pass（KB-004 §2.3 详细步骤）

### 4.3 Shader 变体裁剪与 Keyword 管理

Shader 变体膨胀是首包 + 编译耗时的隐形杀手。KB-002 §二已经给出了「取消 Auto Graphics API、清理 Always Included Shaders、Quality 只保留一个等级」的操作清单。补充：

- **`#pragma multi_compile`** 会为每个 Keyword 组合编一套变体，一个 Shader 里 `multi_compile A B` + `multi_compile X Y` = 4 套变体；`multi_compile_local` 对其他 Shader 不可见，减少变体传播
- **Shader Variant Collection**（Unity 6+）可以显式声明「这个 Shader 需要哪些变体」，但 WebGL 1.0/2.0 各需要一套 Collection
- **动态 Keyword**（`Shader.EnableKeyword` / `Shader.DisableKeyword`）比 multi_compile 更省变体，但需要运行时手动管理——WebGL 下 keyword 解析是主线程阻塞的

### 4.4 Shader Precision Model

Player Settings → Web → Other Settings → **Shader Precision Model**：
- **Float**：最高精度，WebGL 1.0 必须用，性能最好但变体更多
- **Half**：中精度，WebGL 2.0 可以用，减少变体但 WebGL 1.0 不兼容（Unity 会自动 fallback，但 fallback 本身有开销）
- **Default**：Unity 根据 API 自动选——WebGL 1.0 默认 Float，WebGL 2.0 默认 Half

**调试 Shader 精度问题的标准操作**：先切到 Float 验证功能正确，再逐步降精度看真机性能收益。

**来源**：Unity Manual WebGL2 页（2026-11-12 WebFetch）、Unity Manual Web graphics APIs（同上）、微信 Unity 适配 QA §8 + §11（URP CoreBlit）、KB-002 §二（首包精简 + Shader 变体）、KB-004 §2.3（Shader 编译错误黑屏）。

---

## 5. 团结引擎 unity-webgl-transform-sdk 链路坑

Unity WebGL 要跑到微信/抖音小游戏壳里，必须经过「Unity 导出 WebGL → unity-webgl-transform-sdk 转成小游戏目录结构 → 开发者工具上传」这条链路。这条链路的坑是上述所有问题的交汇点。

### 5.1 包体结构

微信 unity-webgl-transform 导出后的典型目录结构：

```
<export-root>/
├── game.js                  # 小游戏入口（自动从 Unity loader.js 改造）
├── game.json                # 小游戏配置
├── project.config.json      # 开发者工具项目配置
├── check-version.js         # 基础库版本检查
├── events.js                # 生命周期/焦点事件桥接
├── unitySDK/                # UnityRuntime adapter
│   ├── loader.js
│   ├── framework.js         # Unity WASM 的 JS 封装
│   └── ...
├── wasmcode/                # 首包 wasm 代码（brotli 后 3-5MB）
│   └── <guid>.wasm.br
├── wasmcode1/               # 延迟分包 wasm（按需下载，7-15MB）
├── wasmcode2/
├── Build/                   # 首资源包：场景、metadata、全局设置
│   ├── data.unityweb
│   ├── il2cpp_*.dll
│   └── global-metadata.dat
└── StreamingAssets/         # 远程资源目录（CDN）
```

### 5.2 首包/分包硬约束

**微信**：总包体 ≤ 20MB（game.js + game.json + unitySDK + wasmcode + Build + wasmcode1 + ... + wasmcodeN 全算）。Brotli 压缩后的 wasmcode 典型 3-5MB，wasmcode1...N 典型 7-15MB，总包 10-25MB 区间。

- 如果 wasmcode + Build（首资源包）< 20MB：可以**包内加载**（全部进微信游戏包体）
- 如果超了：必须**CDN 加载**（unity-webgl-transform 导出面板的「游戏资源 CDN」填对 URL，末尾 `/` 不能少）
- Unity 包体里的 `.data` / `.wasm` / `.framework.js` 必须走 **CDN + 正确的 Content-Encoding 头**（KB-004 §2.1-§2.2 详细步骤）

**抖音**：硬约束在开发者工具上传时报错，阈值由平台后台动态调整。

### 5.3 远程资源加载

UnityWebRequest 在小游戏里被 unity-webgl-transform 工具自动替换：
- `UnityWebRequest.Get(url)` → `wx.request({ url, method: 'GET' })` / `tt.request({ ... })`
- `AssetBundle.LoadFromFile(url)` → `UnityWebRequestAssetBundle.GetAssetBundle(url)`（同上）
- CORS 问题：小游戏环境下**不走浏览器同源策略**，但 CDN 还是应该正确返回 `Access-Control-Allow-Origin: *` 以防开发工具模拟器报错

### 5.4 unity-webgl-transform 工具链对齐

**微信 unity-webgl-transform 原始仓库 `github.com/wechat-miniprogram/minigame-unity-webgl-transform` 已 403 封掉**（2026-11-12 验证），现在统一用团结引擎自带的 unity-webgl-transform 插件：

| 版本对齐要求 | 说明 |
|-------------|------|
| 团结引擎版本 | Unity 基础版本 + unity-webgl-transform 插件版本 + 基础库最低版本 设置 三者主版本号必须一致 |
| Unity 导出设置 | Scripting Backend = IL2CPP；Mono 直接 Loading 卡死 |
| 压缩格式 | Unity Player Settings 压缩选项**保持不压缩**——微信转换工具自动对代码包做 Brotli 压缩 |
| 游戏资源 CDN | 路径末尾 `/` 不能少；CDN 必须返回 `Access-Control-Allow-Origin: *` |

### 5.5 多包融合工具（Unity 专用）

微信提供了「多包融合工具」（MultiPackage）：把单线程包 build-st/、多线程包 build-mt/、Debug 包等融合成一个统一目录结构，开发者直接上传即可。融合后：
```
build-merged/
├── game.js, game.json, project.config.json  ...
└── unitySDK/  (同时含 st + mt 两个 loader，运行时自动选择)
```

**来源**：微信 unity-webgl-transform 导出包体结构（WebFetch DataCDN.html 摘要）、微信 wasm 代码分包（WasmSplit.html 摘要）、团结引擎 WebGLPlatform 页 + FAQ（Unity 版本 + 插件 + 基础库对齐要求）、unity-webgl-transform 仓库封掉 403（2026-11-12 Glob/Grep 验证）、KB-004 §2.1-§2.2（CDN 配置）。

---

## 6. 与已有条目的互链（强制）

| 本条目章节 | 指向条目 | 被指向的章节 |
|-----------|----------|-------------|
| §2.1-§2.2 WASM 编译坑 | [真机白屏与资源加载失败排查](./真机白屏与资源加载失败排查.md) | §2.5（内存不足 → wasm 编译失败白屏） |
| §3.1 PlayerPrefs | 同上 | §3.1（PlayerPrefs 别直接用） |
| §3.5 System.Net | 同上 | §3.2（System.Net / System.File 不能用） |
| §3.6 Texture 格式 | 同上 | §3.5（Texture 2D 格式限制） |
| §4.1-§4.2 Shader 兼容 | 同上 | §2.3（Shader 编译错误 → 黑屏） |
| §5.2 首包/分包 | 同上 | §2.1-§2.2（CDN 配置 / 压缩头） |
| §1.2 Mono 卡死 | 同上 | §2.4（Scripting Backend 非 IL2CPP → Loading 卡死） |
| §2 WASM 体积 | [小游戏启动优化](./小游戏启动优化.md) | §一（启动链路三阶段）、§三（WASM 代码精简 + 分包）、§五（平台评测标准） |
| §4.3-§4.4 Shader 变体 | 同上 | §二（首包精简：清理 Always Included Shaders、取消 Auto Graphics API） |
| §5.2 包体硬约束 | [微信与抖音小游戏发布检查](./微信与抖音小游戏发布检查.md) | 包体/分包/资质硬约束 |
| §4.1-§4.4 Shader 兼容/后处理 | [Unity 小游戏特效实现与性能](./Unity小游戏特效实现与性能.md) | §1（VFX Graph 不可用证据链）、§5（Bloom WebGL1/2 可用性与降级） |

**被反向引用**：KB-004 §5 最后一行 `Unity WebGL 引擎级坑（HybridCLR / IL2CPP / WASM 编译）→ KB-014 待补` 已完成。KB-002 目前没指向 KB-014，后续 round 扫到 KB-002 时补上。

---

## 7. 模板工程对照

路径：`projects/MiniGameTemplate/`（纯代码骨架，**不是完整 Unity 工程，无 ProjectSettings 目录**）。

| 条目 | 模板里的现状 | 含义 |
|------|-------------|------|
| HybridCLR | ❌ 无 `HybridCLRSettings.asset` | 架构默认纯 IL2CPP + wasm 代码分包 |
| IL2CPP | UnityPlayer / ProjectSettings 不在仓库 | 打包时用户自己在 Unity Editor 里设 Scripting Backend = IL2CPP |
| PlayerPrefs 替代 | `SaveManager.cs` 待读 | 可能已有 IPlatformStorage 抽象层，也可能直接用 PlayerPrefs |
| Network | `IPlatformService.cs` / `WeChatPlatformService.cs` / `DouyinPlatformService.cs` | 已有平台抽象层，网络请求统一走平台服务接口 |
| GraphicsSettings | ❌ 无（无 ProjectSettings） | 打包时 Unity Player Settings 默认 WebGL 1.0 + 单 Graphics API |

**待验证**：模板工程实际打出来的 wasm 体积、Shader 变体数、PlayerPrefs 在小游戏里是否真的不工作——需要首次出包后真机实测。

**来源**：项目 Glob + LS（2026-11-12）。

---

## 8. 所有来源与核对日期（2026-11-12）

| 来源 | URL / 路径 | 核对方式 |
|------|-----------|----------|
| Unity Manual Web 开发目录 | https://docs.unity3d.com/Manual/webgl-develop.html | WebFetch 跟随重定向成功 |
| Unity Manual Web memory | https://docs.unity3d.com/Manual/webgl-memory.html | WebFetch 跟随重定向成功（11.8KB 整页） |
| Unity Manual Web graphics APIs | https://docs.unity3d.com/Manual/webgl-graphics.html | WebFetch 跟随重定向成功 |
| Unity Manual WebGL2 | https://docs.unity3d.com/Manual/WebGL2.html | WebFetch 跟随重定向成功（547 行完整页） |
| Unity Manual Web texture compression | https://docs.unity3d.com/Manual/webgl-texture-compression.html | WebFetch 跟随重定向成功 |
| Unity Manual Web networking | https://docs.unity3d.com/Manual/webgl-networking.html | WebFetch 跟随重定向成功（整页明确列出不支持的 .NET 类） |
| Unity Manual Web input | https://docs.unity3d.com/Manual/webgl-input.html | WebFetch 跟随重定向成功（11.3KB 整页） |
| Unity Manual PlayerPrefs WebGL IndexedDB | https://docs.unity3d.com/ScriptReference/PlayerPrefs.html | WebFetch 失败 → WebSearch 摘要命中 "limited to 1 megabyte" |
| Unity Manual Debug WebGL（persistentDataPath flush） | https://docs.unity3d.com/Manual/webgl-debugging.html | WebSearch 摘要命中（direct WebFetch 失败） |
| 团结引擎 WebGL 平台特征简介 | https://docs.unity.cn/cn/tuanjiemanual/Manual/WebGLPlatform.html | WebFetch 直接可达（整页，含 WASM 10 倍内存数据） |
| 团结引擎 FAQ | https://docs.unity.cn/cn/tuanjiemanual/1.6/Manual/FAQ.html | WebSearch 摘要命中（WebFetch 待试） |
| HybridCLR 支持平台 | https://www.hybridclr.cn/docs/7.10.0/basic/supportedplatformanduniyversion | WebFetch 直接可达 |
| HybridCLR 包体影响 | https://www.hybridclr.cn/docs/basic/impactonappsize | WebSearch 摘要命中 |
| HybridCLR 内存与 GC | https://www.hybridclr.cn/docs/basic/memory | WebSearch 摘要命中 |
| HybridCLR Common Errors | https://www.hybridclr.cn/en/docs/help/commonerrors | WebFetch 直接可达但正文被截断（Runtime Errors 段未取到）；iOS 15.4 条目由 WebSearch 摘要命中（2026-11-13） |
| 微信 wasm 代码分包 WasmSplit（Unity 专用线） | https://developers.weixin.qq.com/minigame/dev/guide/game-engine/unity-webgl-transform/Design/WasmSplit.html | **WebFetch 正文直接可达**（页面更新 2026-06-29，2026-11-13 验证） |
| 微信 代码分包 V2（通用适配线） | https://developers.weixin.qq.com/minigame/dev/guide/game-engine/WasmSplitV2.html | **WebFetch 正文直接可达**（页面更新 2026-09-22，2026-11-13 验证） |
| 微信 unity-webgl-transform SDKInstaller | https://developers.weixin.qq.com/minigame/dev/guide/game-engine/unity-webgl-transform/Design/SDKInstaller.html | WebSearch 摘要命中 |
| 模板工程 MiniGameTemplate 无 HybridCLRSettings.asset | `projects/MiniGameTemplate/` Glob | 项目内 Glob 验证 |
| unity-webgl-transform 仓库已 403 | github.com/wechat-miniprogram/minigame-unity-webgl-transform | 状态文件 round 1/4 已验证 |
| KB-004 真机白屏与资源加载失败排查 | `knowledge-base/真机白屏与资源加载失败排查.md` | 项目 Read |
| KB-002 小游戏启动优化 | `knowledge-base/小游戏启动优化.md` | 项目 Read |

## 9. 待复核项

| 待复核内容 | 条件 | 原因 |
|-----------|------|------|
| ~~HybridCLR iOS 15.4 官方页面给出的「第二个解决方案」~~ | **已解决（round 17 WebFetch 完整正文）** | 方案②（对 GenericMetadata::InflateIfNeeded 关闭 clang 优化）步骤已写入 §2.4；来源 hybridclr.cn/docs/help/commonerrors 中文页，round 15 的截断问题本轮未复现 |
| 抖音小游戏 Application API 行为细节 | IAB 浏览器渲染 | 抖音 Unity 适配文档 JS 渲染，WebFetch 只得壳 |
| ~~模板工程 SaveManager.cs 是否已有 IPlatformStorage 抽象~~ | **已解决（round 14 本地 grep）** | 全工程无 IPlatformStorage；SaveManager 直接用 PlayerPrefs+JsonUtility，与架构条目「不要自己封装平台存储」决策一致 |
| Unity Input System 在微信小游戏 Accelerometer/Gyroscope 具体行为 | 有真机 | 需真机 setEnableDebug + vConsole 验证 |
