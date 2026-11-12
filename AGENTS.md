# 项目指令

## 知识库维护调度启动检查

每次在本工作区开启新的交互式会话时，执行以下检查：

1. CronList 查看标题含「知识库维护」的自动化状态。
2. 若 lifecycleStatus 为 completed 或 enabled=false：
   - CronDelete 该 ID
   - CronCreate 重建：title=每天晚上11点知识库维护（kb-maintainer），cron=`0 23 * * *`，recurring=true，prompt 从 `.tmp/monitor-state.md`「重建用 prompt」节读取。
3. 若仍为 active：不动。
4. 检查监控自动化（标题含「监控-关机」）是否 active，若 completed/disabled 同样重建。

此检查只在交互式会话执行。cron 派发轮和子代理中 Cron 工具被禁用，不要尝试。

## 知识库维护

- 知识库位于 `knowledge-base/`，任务索引是 `knowledge-base/README.md`。
- 维护 agent 定义：`.zcode/agents/kb-maintainer.md`。
- 持久状态（唯一待办源）：`knowledge-base/.agent-state.md`。
- 手动触发一轮：Agent(subagent_type: "kb-maintainer") 或 general-purpose + 读定义文件。
- 条目写法标准：`知识库收集与整理任务.md`。
- 用户给出新规则或纠正时，深挖背后逻辑写入知识库并更新索引。

## 全局规则

写新功能代码前，先 GitHub 搜索有没有相关代码，不要重复造轮子。