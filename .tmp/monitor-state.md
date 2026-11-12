# 监控-关机 运行状态与判定标准

更新时间：2026-11-12 —— 架构修订 v2：移除 Cron 自愈，监控只检测+关机

## 架构（2026-11-12 v2）

- **主任务**：`每天晚上11点知识库维护（kb-maintainer）`，automation-e571dff4-7548-4489-bbd3-1a195855b9ce
- **Agent 定义**：`D:\game\game-dev\.zcode\agents\kb-maintainer.md`
- **持久状态**：`D:\game\game-dev\knowledge-base\.agent-state.md`（唯一待办源）
- **调度恢复**：由用户交互式会话的 AGENTS.md 启动检查负责。cron 轮和子代理均无 Cron 工具。
- **监控职责**：只检测运行状态 + 完成后关机。不重建调度、不派发子智能体。

## 判定流程

1. 看日志 mtime + knowledge-base/ 文件写入判断是否在跑。
2. 正在跑 → 不动，记录观察时间。
3. 不在跑 → 读 .agent-state.md 的 round 和「进行中」区判断完成/卡住。
4. 已完成 → 记录 → shutdown /s /t 60。
5. 疑似卡住 → 记录「需用户交互会话检查」，不尝试修复。
6. 到 06:50 仍未完成 → 只记录，不关机。

## 重建用 prompt（交互式会话启动检查用，一字不改）

你是 kb-maintainer agent，执行游戏开发知识库的一轮维护循环。工作区：D:\game\game-dev。

严格按以下顺序执行：

1. 读取 agent 定义 D:\game\game-dev\.zcode\agents\kb-maintainer.md，这是你的完整工作流程和约束。
2. 读取持久状态 D:\game\game-dev\knowledge-base\.agent-state.md，接续上轮进度。检查「进行中」区是否有未完成任务，有则先补完；无则从队列取下一个 pending 任务。
3. 读取任务书 D:\game\game-dev\知识库收集与整理任务.md，这是条目写法的唯一标准。
4. 读取 knowledge-base/README.md 索引，确认覆盖度。
5. 按 agent 定义的五阶段循环执行：收集→登记→验收查漏→找出问题→解决问题。每轮只处理一个任务。
6. 收尾：更新 .agent-state.md（清除进行中、轮次+1、历史摘要、下一轮计划、经验教训复盘）。
7. 输出简短交付报告。

禁止：不创建/修改/删除任何自动化；不派发子智能体；不删除已有条目；不编造来源；不跳过验收；不修改 agent 定义文件。

## 已知平台限制

- cron 派发轮禁用 CronCreate/CronUpdate/CronDelete（automationToolPolicy.ts）。
- 子代理也无 Cron 工具、不能嵌套派发、不读项目 Memory。
- recurring=true 派发后变 completed/disabled，需交互式会话重建。
- 自动化记录状态不可信，以日志 mtime + 文件写入为准。

## 历史

- 2026-11-11：首批 5 条目完成。旧 automation 603d7da5 因工具循环故障未能自愈。
- 2026-11-12：架构升级 v1（kb-maintainer agent + 自愈）。首轮验证跑通 KB-001。
- 2026-11-12：架构修订 v2。源码证实 cron 轮禁 Cron 三件套、子代理无 Cron。移除自愈，改为 AGENTS.md 启动检查。监控简化为只检测+关机。