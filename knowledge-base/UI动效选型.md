# UI 动效选型：节点动画 vs Spine

适用：Cocos Creator 3.x（按 3.8 官方手册核对）；决策框架同样适用于 Unity uGUI（本工作区项目引擎，见文末对照）。触发条件：给 UI 动效（按钮反馈、面板进出场、飘字、图标特效、角色展示）选实现方案。核对日期：2026-11-11。

## 结论（决策树）

1. **简单 UI 动效 → 节点动画（Animation 组件 / Tween），不用 Spine。** 位移、缩放、旋转、透明度、颜色、帧序列这类变化，节点动画零额外运行时成本；Spine 是「用 CPU 算力换美术资源」的方案，简单动画付不起这个账。
2. **角色级复杂动作、换装、多动作状态机 → Spine。** 骨骼驱动能用一套素材出多套动作，美术产能优势在动作数量多时才兑现。
3. **大量同骨架同动作的 Spine 同屏（场景装饰、特效、副本小怪、NPC）→ Spine 的 `SHARED_CACHE` 模式**，官方明确此场景「能极大提高帧率和降低内存」。
4. **未知量：目标机型上同屏骨架数上限。** 不要凭感觉定「能放多少个」，用文末压测法在真机测出来再定美术规格。

## 两种方案的账

| 维度 | 节点动画（Animation/Tween） | Spine（sp.Skeleton） |
| --- | --- | --- |
| 素材需求 | 切图即可；关键帧在 Creator 动画编辑器做，或纯代码 Tween | Spine 编辑器（商业授权）制作，导出 `.json/.skel` + `.atlas/.txt` + `.png` 三件套 |
| 版本约束 | 无 | **Creator v3.0+ 只支持 Spine v3.8 导出**（原生平台不支持 v3.8.75）；美术用 Spine 4.x 做的资产导入前必须核对引擎支持情况 |
| 运行时成本 | 关键帧插值，驱动节点属性，成本极低 | 每帧骨骼变换计算，成本 ∝ 激活骨架数 × 骨骼复杂度（CPU 侧） |
| 表现力 | 节点属性级：位移/旋转/缩放/颜色/透明度/spriteFrame 帧动画 | 骨骼级：网格变形、IK、换装、动作融合叠加（REALTIME 模式） |
| 合批 | UI 合批规则内 | 有 `Enable Batch` 开关；多 atlas 会拆 DrawCall |
| 适合 | 按钮/面板/飘字/图标等 UI 反馈动效 | 角色、宠物、复杂特效、需要换装的对象 |

来源：[Spine 骨骼动画资源](https://github.com/cocos/cocos-docs/blob/master/versions/3.8/zh/asset/spine.md)（版本支持表、三件套）· [Spine Skeleton 组件参考](https://github.com/cocos/cocos-docs/blob/master/versions/3.8/zh/editor/components/spine.md)（缓存模式、合批、UI 渲染约束）· [程序化控制动画](https://github.com/cocos/cocos-docs/blob/master/versions/3.8/zh/animation/animation-component.md)（play/crossFade/帧事件）。均为官方手册，核对 2026-11-11。

## Spine 三种缓存模式（官方规定，选型关键）

`Animation Cache Mode` 属性（[组件参考](https://github.com/cocos/cocos-docs/blob/master/versions/3.8/zh/editor/components/spine.md)，核对 2026-11-11）：

| 模式 | 机制 | 代价 | 适用 |
| --- | --- | --- | --- |
| `REALTIME`（默认） | 实时运算 | CPU 最高 | 需要动作融合/叠加、换装、完整事件的主角 |
| `SHARED_CACHE` | 预烘焙骨骼动画，多实例**共享**动画与贴图数据 | 不支持动作融合/叠加，只支持动作开始/结束事件 | 同骨架同动作 **N≥3** 个实例起显现内存优势，N 越大越划算；场景动画、特效、副本怪物、NPC |
| `PRIVATE_CACHE` | 预烘焙但**不共享**，额外占内存 | 只有性能优势，大量使用可能卡顿 | 要缓存性能但需换装（不能共享贴图）的对象 |

**选型含义**：「同屏很多 Spine」不等于「必须放弃 Spine」——先问是不是同骨架同动作。是，则 SHARED_CACHE 把每实例骨骼计算变成查表；不是（每个都要独立换装/融合），才回到「减数量、减骨骼、分帧」的优化路径。

另注意官方组件约束：Spine Skeleton 是 UI 渲染组件，节点必须在 Canvas（或 RenderRoot2D）下；Node 的 Anchor/Size 对它无效。

## 同屏规模怎样影响选择（外部案例，保留测试条件）

- **Cocos 论坛实测**（外部案例：Cocos Creator **2.4.7**，2D 像素 MMO，集体拍照场景同屏约 **400** 个 Spine）：DrawCall 已合批仍然卡，「骨骼数量减少，卡顿现象明显缓解」——瓶颈在骨骼计算而非渲染。回复中的压测法：只放一种 spine 排除 DC 干扰，从目标数量往下调，找出该机型不掉帧的上限；远低于目标说明 CPU 算力不够，优化方向只剩减骨骼/减数量/LOD。[帖子](https://forum.cocos.org/t/topic/164105)（核对 2026-11-11）
- **UWA 三消游戏案例**（外部案例：**Unity**，GOT Online 线上数据）：进场景后 `SkeletonGraphic.Update/LateUpdate` 调用 50+ 次 = 激活 Spine 组件数；格子里变化幅度不大的小图标特效「确实没有太大必要用 Spine 来做」。屏幕外的 Spine 用 Freeze 冻结更新，且 UI 组件优先 Freeze 而非 disable（激活/隐藏可能触发网格重建额外开销）。文章未给出耗时毫秒数与机型。[UWA：Spine 动画更新耗时治理](https://blog.uwa4d.com/archives/TechSharing_471.html)（核对 2026-11-11）

两个案例共同指向：**Spine 的预算单位是「激活骨架数 × 骨骼复杂度」，不是 DrawCall**。合批解决不了骨骼计算。

## 目标设备测量怎样影响选择

1. **先定机型再定方案**：低端机 CPU 预算小，同样 20 个 Spine 在旗舰机无感、低端机掉帧。美术规格（骨骼数、同屏数）应在项目前期按目标低端机立标准（与[资源汇总](<./游戏开发经验与优化资源汇总.md>)避坑清单「美术标准前期确立」一致）。
2. **压测步骤**（来自上述论坛帖，实践建议）：真机 → 只放一种 Spine → 从目标同屏数递减 → 记录不掉帧上限 → 上限远低于需求时，依次尝试 SHARED_CACHE、减骨骼、视区外冻结、降逻辑帧率，仍不够则换节点动画/帧动画方案。
3. **分清渲染帧还是逻辑帧**：骨骼计算吃逻辑帧，atlas 过多/半透明叠加吃渲染帧，两者优化手段不同（同上帖）。
4. 视区外冻结：屏幕外的 Spine 停更新，快回视区再恢复（UWA 案例做法）。

## 最小接入示例

节点动画（官方接口，示例代码）：

```typescript
import { Animation } from 'cc';
const anim = this.node.getComponent(Animation);
anim.play('idle');                 // 播放；playOnLoad + defaultClip 可自动播
anim.crossFade('run', 0.3);        // 0.3s 淡入淡出切换
```

Spine（官方接口，示例代码）：

```typescript
import { sp } from 'cc';
const sk = this.node.getComponent(sp.Skeleton);
sk.setAnimation(0, 'walk', true);  // 轨道0、动画名、循环
// 缓存模式在属性检查器设 Animation Cache Mode；
// 代码侧对应 sp.Skeleton.AnimationCacheMode 枚举（SHARED_CACHE / PRIVATE_CACHE / REALTIME）
```

Spine 支持远程加载三件套（`assetManager.loadAny` + `loadRemote` 组装 `sp.SkeletonData`），小游戏远程资源方案可用，完整代码见[官方资源文档](https://github.com/cocos/cocos-docs/blob/master/versions/3.8/zh/asset/spine.md)「从服务器远程加载 Spine」节。

## 本工作区项目对照（Unity uGUI）

项目是团结引擎 + uGUI：同一决策树成立，且 UWA 案例本身就是 Unity 侧证据。简单 UI 动效用 Animation/Tween 类方案；Spine 走 SkeletonGraphic（Spine-Unity 运行时），屏幕外 Freeze 的建议直接适用。uGUI 素材与拼接规范见 [Unity uGUI 美术资源与 AI 拼接标准](<./Unity uGUI 美术资源与 AI 拼接标准.md>)。Unity 侧 Spine 缓存模式对应 Spine-Unity 的 `SkeletonDataAsset` 烘焙工作流，与 Cocos 的三模式枚举不同，接入时按 Spine-Unity 版本文档核对（待补项）。

## 相关条目

- [对象池接入与复用](./对象池接入与复用.md) —— 大量动效对象（飘字、特效）的创建销毁走池化。
- [Cocos 场景切换后内存排查](./Cocos场景切换后内存排查.md) —— Spine 贴图/骨骼数据是动态资源大头，切场景释放策略见该条。
- [Unity uGUI 美术资源与 AI 拼接标准](<./Unity uGUI 美术资源与 AI 拼接标准.md>) —— 项目 UI 素材规范。
- [游戏开发经验与优化资源汇总](<./游戏开发经验与优化资源汇总.md>) —— Spine 优化来源导航（官方论坛、10 万 Spine 同屏 GPU Skinning 方案等）。