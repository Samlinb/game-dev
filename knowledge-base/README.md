# 知识库任务索引

开发时先查本索引命中条目，读完条目正文再按需打开项目证据或外部原文。条目组织约定见[知识库收集与整理任务](../知识库收集与整理任务.md)。

| 用户要做什么 | 对应条目 | 适用 |
| --- | --- | --- |
| 写新功能前决定要不要先找现成实现 | [写新功能前先找现成实现](./写新功能前先找现成实现.md) | 通用；含 GitHub 搜索语法与例外边界 |
| 判断一个外部库能不能接入 | [外部库选型评估](./外部库选型评估.md) | 通用；含许可证对比与供应链风险 |
| 用 AI 设计或还原 uGUI 界面并交付 Prefab | [Unity uGUI 美术资源与 AI 拼接标准](<./Unity uGUI 美术资源与 AI 拼接标准.md>) | Unity uGUI |
| Tooltip、下拉菜单被公共资源栏挡住 / 浮层点击穿透 | [Unity uGUI 浮层遮挡与点击穿透](<./Unity uGUI 浮层遮挡与点击穿透.md>) | Unity 2021.3 uGUI；Canvas 排序、射线命中与 Ninja Boot 回归 |
| 从零搭 Unity 小游戏工程结构(微信/抖音双平台) | [Unity 小游戏项目架构](<./Unity 小游戏项目架构.md>) | 团结引擎/Unity WebGL;含模板工程 projects/MiniGameTemplate |
| 给高频创建销毁的节点接入对象池 | [对象池接入与复用](./对象池接入与复用.md) | Cocos Creator 3.6–3.8；含 Unity 模板 PrefabPool 对照 |
| 给 UI 动效选实现（按钮/面板/飘字/角色特效） | [UI 动效选型](./UI动效选型.md) | Cocos 3.x 决策树 + Unity uGUI 对照；Spine 缓存模式 |
| 确定角色移动/输入/相机跟随的更新入口 | [Unity 帧更新与物理更新](./Unity帧更新与物理更新.md) | Unity 2022.3 / 团结引擎；回调时序与输入时机 |
| 检查小游戏发布包（包体/分包/资质/流程） | [微信与抖音小游戏发布检查](./微信与抖音小游戏发布检查.md) | 微信+抖音；数值核对 2026-11-11，用前复核 |
| 切场景后内存上涨 / iOS 内存闪退 | [Cocos 场景切换后内存排查](./Cocos场景切换后内存排查.md) | Cocos 3.x；抖音 iOS 内存红线与诊断工具 |
| Unity 真机连 Profiler / 定位 GC 尖峰与纹理内存 / 查内存红线 | [Unity 真机 Profiling 与内存分析](./Unity真机Profiling与内存分析.md) | 团结/Unity WebGL 微信+抖音；含 Editor 与真机差异、内存预算 |
| 优化小游戏首屏启动时间 / 启动评测不达标 / 首包精简 | [小游戏启动优化](./小游戏启动优化.md) | 团结/Unity WebGL 微信+抖音；启动链路、WASM 分包、抖音 FP80/TTI80 阈值 |
| 判断是否需要版号/软著 / 准备发布资质材料 / 选游戏类目 | [小游戏资质与类目](./小游戏资质与类目.md) | 微信 IAA/IAP 分流 + 备案流程；抖音待核对 |
| 真机白屏/黑屏/资源加载失败/wasm 编译报错 | [真机白屏与资源加载失败排查](./真机白屏与资源加载失败排查.md) | 微信+抖音 Unity WebGL；含 vConsole 开启、压缩头、CDN、Shader、PlayerPref |
| 给用户提供免广告权益不损失收入 / 设计免广告券 / 插屏降频与点位改造 | [小游戏免广告方案与IAA变现优化](./小游戏免广告方案与IAA变现优化.md) | 微信+抖音 IAA；机制+频控+风控清单；eCPM 倍数为经验值 |
| Unity WebGL 小游戏坑点 / HybridCLR / WASM 编译 / API 行为差异 | [Unity WebGL 小游戏特有技术坑](<./Unity WebGL 小游戏特有技术坑.md>) | 微信+抖音 团结引擎；含 IL2CPP vs HybridCLR 选型、PlayerPrefs/FileIO/System.Net 差异、Shader 兼容、unity-webgl-transform 链路 |
| 降低 DrawCall / 合批优化 / OverDraw 治理 / 图集策略 / 纹理压缩 | [DrawCall 与渲染批处理优化](./DrawCall与渲染批处理优化.md) | 微信+抖音 团结引擎 WebGL；含 Static Batching、SRP Batcher WebGL 可用性澄清、GPU Instancing 互斥、Shader 变体裁剪、ASTC/ETC2 覆盖、Canvas 合批规则 |
| 接入平台 SDK / 开通广告流量主 / 能力自查 / 真机广告验收 | [平台能力开通与接入检查](./平台能力开通与接入检查.md) | 微信+抖音 团结引擎；SDK 安装、能力矩阵、端到端 checklist、广告三路径验收 |
| UI 被刘海遮挡 / 屏幕比例布局错乱 / 低端机降帧降分辨率 | [小游戏分辨率适配与降级策略](./小游戏分辨率适配与降级策略.md) | 微信+抖音 团结引擎 uGUI；Canvas Scaler 选型、SafeArea、胶囊避让、设备分档降级 |
| Spine 同屏卡顿 / DrawCall 高 / 实例化掉帧 / 分帧更新 / atlas 合并 | [Spine 运行时优化](./Spine运行时优化.md) | spine-unity 微信+抖音 WebGL；Freeze/round-robin/atlas 共享/对象池/GPU 动画/SkeletonGraphic 约束 |
| 导入 Spine 资产到 Unity / 理解 SkeletonDataAsset / PMA 设置 / 版本匹配 | [Spine Unity 资产工作流](./SpineUnity资产工作流.md) | spine-unity 4.2/4.3；导入流程、无 SHARED_CACHE 等效、PMA vs Straight、组件选择 |
| 判断项目范围是否过大 / 决定砍什么功能 / 独立项目立项自查 | [独立项目范围管理](./独立项目范围管理.md) | 方法论；overscoping 信号清单、核心循环裁剪判据、PC 来源与小游戏语境边界 |
| 查各主题的外部资料来源 | [游戏开发经验与优化资源汇总](<./游戏开发经验与优化资源汇总.md>) | 来源导航 |

## 维护状态

待办队列、轮次进度、经验教训统一维护在 [.agent-state.md](./.agent-state.md)（唯一待办源）。
本索引只保留开发检索入口，不维护待办。维护 agent 定义见 [kb-maintainer](../.zcode/agents/kb-maintainer.md)。
