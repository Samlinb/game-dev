# Unity 帧更新与物理更新（回调时序）

适用：Unity 2022.3 LTS（按该版本官方文档核对）；本工作区项目的团结引擎基于 Unity 2022.3，时序同样适用。其他大版本（如 Unity 6）结论基本一致，引用时换对应版本文档。触发条件：确定角色移动/输入/相机跟随该写进哪个回调，或排查「输入丢帧」「物理抖动」类问题。核对日期：2026-11-11。

## 结论（影响行动的部分）

- **输入读在 `Update`，物理写在 `FixedUpdate`，跟随相机放 `LateUpdate`。** 这是三个回调的分工底线。
- **`FixedUpdate` 不是每帧一次**：按固定时间间隔触发，一帧内可能 0 次也可能多次。把「每帧必然执行一次」的逻辑放进去会出错。
- **`Update` 里读边沿输入（GetKeyDown/GetButtonDown）是官方明确要求**：输入状态每帧重置，FixedUpdate 里读会丢信号或重复触发。
- **`FixedUpdate` 里算移动不要乘 `Time.deltaTime`**：它本身就是固定步长，帧率无关。

## 时序表（官方规定）

一帧内的主干顺序：

```
FixedUpdate（0..n 次，间隔 Time.fixedDeltaTime）
  └─ 每次 FixedUpdate 之后紧跟物理模拟（internal physics update）
Update（每帧 1 次）
协程恢复（yield null / WaitForSeconds：在所有 Update 之后）
LateUpdate（每帧 1 次，在所有 Update 完成之后）
渲染
```

| 回调 | 触发条件与频率 | 放什么 |
| --- | --- | --- |
| `FixedUpdate` | 固定间隔 `Time.fixedDeltaTime`，默认 **0.02s（50 次/秒）**；高帧率时某些帧 0 次，低帧率时一帧多次（官方例：25fps 约每帧 2 次，100fps 约两帧 1 次） | Rigidbody 施力/移动、物理相关逻辑 |
| 物理模拟 | 「All physics calculations and updates occur immediately after FixedUpdate」 | 引擎内部；碰撞/触发回调随之产生 |
| `Update` | 每帧 1 次，间隔随帧率波动 | 输入读取、非物理移动、计时器、一般游戏逻辑 |
| 协程（yield null） | 「Normal coroutine updates are run after the Update function returns」；`WaitForFixedUpdate` 则在所有 FixedUpdate 之后 | 跨帧流程 |
| `LateUpdate` | 每帧 1 次，所有 Update 完成后 | 跟随相机（官方典型用例：角色 Update 移动，相机 LateUpdate 跟踪，保证跟踪的是移动后的位置） |

来源：[Event Function Execution Order（2022.3）](https://docs.unity3d.com/2022.3/Documentation/Manual/ExecutionOrder.html) · [MonoBehaviour.FixedUpdate](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/MonoBehaviour.FixedUpdate.html)。核对 2026-11-11。

## 为什么输入必须在 Update 读

官方 [Input.GetKeyDown](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Input.GetKeyDown.html) 原文：「Call this function from the Update function, since the state gets reset each frame.」（核对 2026-11-11）

推演两种错法：

- **高帧率（100fps）**：约两帧才一次 FixedUpdate。按键按下发生在某个渲染帧，若该帧没有 FixedUpdate，边沿信号在下次 FixedUpdate 前已被重置 → **丢输入**。
- **低帧率（25fps）**：一帧约两次 FixedUpdate，同一次按键状态被读两遍 → 边沿输入**重复触发**（按住类 GetKey 则是重复执行，语义上可能没问题但次数不可控）。

正确姿势：Update 里读输入并缓存意图（方向向量、跳跃标志），FixedUpdate 里消费缓存做物理移动。

## 最小例子：输入驱动角色移动

### A. 非物理移动（本工作区模板的实际实现）

2D 休闲、无 Rigidbody 时直接在 Update 移动，乘 `Time.deltaTime` 保证帧率无关。模板 [PlayerMovement.cs](../projects/MiniGameTemplate/Assets/_Project/Scripts/Gameplay/PlayerMovement.cs)（模板现有代码，本轮读取核对、未运行）：

```csharp
private void Update()
{
    if (config == null) return;
    var dir = new Vector3(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"), 0f);
    transform.position += dir * (config.moveSpeed * Time.deltaTime);
}
```

速度从 ScriptableObject 读（模板约定：可调数值不写魔法数字），见 [Unity 小游戏项目架构](<./Unity 小游戏项目架构.md>)。

### B. 物理移动（Rigidbody 版，示例代码，未在模板运行）

```csharp
[SerializeField] private Rigidbody2D rb;
[SerializeField] private float moveSpeed = 5f;   // 实际项目从 Config SO 读
private Vector2 moveInput;

private void Update()
{
    // 输入只在 Update 读（状态每帧重置）
    moveInput = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")).normalized;
}

private void FixedUpdate()
{
    // 物理移动在 FixedUpdate；固定步长，不乘 deltaTime
    rb.MovePosition(rb.position + moveInput * (moveSpeed * Time.fixedDeltaTime));
}
```

官方依据：「Use FixedUpdate when using Rigidbody.」（[FixedUpdate 文档](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/MonoBehaviour.FixedUpdate.html)）

**预期完成结果**（示例的目标行为，非本轮实测）：A 版按键即移动、松手即停，不同帧率下移速一致；B 版角色与碰撞体正确交互（推箱子、撞墙停下），低帧率下移动依然平滑（物理步长恒定）。

## 项目上下文

- 模板目标帧率 30（`Application.targetFrameRate = 30`，低端机稳帧起点），默认 `fixedDeltaTime = 0.02` → 每帧约 1–2 次 FixedUpdate。30 帧下「FixedUpdate 比 Update 频繁」是常态，别假设两者次数相等。
- 模板是单场景 + 面板切换的 2D 休闲架构，角色移动走 A 版即可；引入 Rigidbody 玩法时才需要 B 版。

## 常见坑速查

| 症状 | 原因 | 修法 |
| --- | --- | --- |
| 按键偶尔没反应 | 边沿输入写在 FixedUpdate，高帧率帧漏读 | 输入挪到 Update，缓存意图 |
| 一次按键触发两次跳跃 | 低帧率一帧多次 FixedUpdate 重复读边沿 | 同上；或消费后清标志 |
| 物理移动速度随帧率变化 | FixedUpdate 里乘了 `Time.deltaTime` | 固定步长内不乘（或用 `Time.fixedDeltaTime`） |
| 跟随相机抖动 | 相机在 Update 跟踪，与角色移动先后不定 | 相机挪 LateUpdate（官方典型用例） |
| 每帧逻辑放 FixedUpdate 后时快时慢 | FixedUpdate 次数 ≠ 帧数 | 每帧逻辑放 Update |

## 相关条目

- [Unity 小游戏项目架构](<./Unity 小游戏项目架构.md>) —— 模板 30 帧策略、SO 数值约定、PlayerMovement 所在结构。
- [对象池接入与复用](./对象池接入与复用.md) —— 高频生成对象的清理与 Update 无关但同属运行时开销治理。
- [游戏开发经验与优化资源汇总](<./游戏开发经验与优化资源汇总.md>) —— Unity 官方博客「理解帧循环执行顺序」等来源导航。