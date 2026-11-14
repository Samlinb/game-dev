# Unity 小游戏项目架构(微信 + 抖音,2D 休闲)

适用:团结引擎(Tuanjie)/ Unity WebGL 构建转微信、抖音小游戏;1-3 人小团队做 2D 休闲游戏。
核对日期:2026-11-11。直接可用的模板工程:[projects/MiniGameTemplate](../projects/MiniGameTemplate)(团结引擎打开 → 菜单 `MiniGame → 一键初始化`)。

## 目录结构

> 仓库本体是代码骨架（无 ProjectSettings、无 .asset/.unity 资产）。下图是跑完菜单 `MiniGame → 一键初始化` 后的完整结构；`Data/`（含 AdConfig.asset 等配置）、`Scenes/`（Boot/Game）由 ProjectBootstrap 编辑器菜单生成（round 13 复核确认）。

```
Assets/
  _Project/            # 自己的全部内容
    Art/               # 图片、动画、音频,按功能分子目录
    Prefabs/           # Actors / UI / Effects
    Scenes/            # Boot.unity(初始化) + Game.unity(单场景主游戏)
    Scripts/           # Core / Gameplay / UI / Platform / Ads / Data / Editor
    Data/              # ScriptableObject 配置资产
    Settings/          # 渲染、物理设置
  Plugins/             # 第三方 SDK(微信/抖音官方转换 SDK)
```

## 关键决策与背后逻辑

### 为什么 `_Project/` 用下划线前缀

Unity Project 窗口按字母排序,`_` 排最前;自己的内容置顶一眼可见。更重要的是**隔离边界**:第三方 SDK(微信/抖音转换包动辄上千文件)进 `Plugins/`,自己的内容永远只在 `_Project/` 里,升级/替换 SDK 不会污染业务目录,搜索也不会被 SDK 文件淹没。

### 为什么可调数值全走 ScriptableObject,禁止静态常量

- 调数值不用重编 WebGL 包:小游戏包体构建链路慢,SO 资产独立序列化,Inspector 改完即生效。
- 策划/自己调参不碰代码,避免「改个移速顺手把逻辑改坏」。
- SO 是资源,天然进 Unity 序列化与版本管理,数值变更有 diff。
- 接法固定:使用方 `[SerializeField] private XxxConfig config;` Inspector 拖入,运行期只读。示例见模板 `Data/PlayerConfig.cs` + `Gameplay/PlayerMovement.cs`。

### 为什么 Boot 场景 + DontDestroyOnLoad 单例,而不是纯静态类

- 管理器要挂 Inspector 配置(SO 引用、AudioSource),MonoBehaviour 才能拖引用;纯静态类只能代码 new 配置,回到「数值进代码」的老路。
- `Awake` 里判重再 `DontDestroyOnLoad`(见模板 GameManager),配合「Boot 场景只进一次」= 全工程唯一;直接 `Destroy(gameObject)` 处理重复实例,Play 模式反复加载 Boot 不产生双管理器。
- Boot 场景只做初始化 → `SceneManager.LoadSceneAsync("Game")`,初始化代码与游戏内容解耦,主场景可以随便重建调试。

### 为什么休闲小游戏用单场景 + 面板切换,不做多场景跳转

- WebGL 小游戏场景加载即重新反序列化 + 资源加载,低端机卡顿明显;单场景内切面板(Instantiate/SetActive)开销小一个量级。
- 场景间传状态(金币、关卡进度)容易丢;单场景 + `SaveManager`(PlayerPrefs 存 JSON)状态不丢。
- 大世界/分关类游戏才值得 additive 多场景;休闲游戏别用。

### 为什么面板必须是 Prefab + UIManager 统一实例化

- UI 散在场景里 = 多人协作必冲突(场景文件二进制化合并困难);面板 Prefab 化后各自改各自的。
- UIManager 按 key(约定 = 面板类名)注册/复用/隐藏,`ShowOnly(key)` 一句话切屏;`UIPanel.OnShow/OnHide` 成对订阅/退订事件,防重复订阅与悬空回调。
- Prefab 嵌套不超过 2 层,不在 Prefab 里放场景专属逻辑。

### 为什么加 IPlatformService 平台抽象层

微信、抖音 API 形状几乎相同但命名空间、SDK、包体链路完全不同;业务代码直接调 SDK 会导致「出抖音包要删微信代码」的灾难。抽象后业务只传场景位(placement),平台细节封在实现里:

| 能力 | 微信 | 抖音 |
| --- | --- | --- |
| 登录 | `wx.login` → code | `tt.login` → code |
| 激励视频 | `wx.createRewardedVideoAd` | `tt.createRewardedVideoAd` |
| 判断看完 | `res.isEnded` | `res.isEnded` |
| 分享 | `wx.shareAppMessage` | `tt.shareAppMessage` |
| 录屏分享 | — | `tt.getGameRecorderManager`(抖音特色) |

实现切换用编译符号:`WECHAT_SDK` / `DOUYIN_SDK`(PlayerSettings 的 Scripting Define Symbols,一个构建目标只定义一个),编辑器恒走 Stub——不装 SDK 也能在编辑器跑通全部业务流程,接 SDK 的工作被压缩到「填空三个实现类」。

**现成工具参考**（D 方向挖掘 round 47）：[WanNengDeXiangWang/UnityMinigameSDKTool](https://github.com/WanNengDeXiangWang/UnityMinigameSDKTool)（0★，**无 LICENSE**，pushed 2026-10-29）是编辑器工具：同工程保留微信+抖音 SDK，用三模式宏 `WECHAT`/`BYTEDANCE`/`WEB_BROWSER`（互斥）+ SDK 存在标记 `MGF_WECHAT_SDK`/`MGF_DOUYIN_SDK`（检测目录有 SDK ≠ 启用，微信代码需 `WECHAT && MGF_WECHAT_SDK` 同时成立）+ 给 SDK 的 `.asmdef`/插件导入加平台编译约束，SDK 导入/更新/删除后自动补齐隔离。切换模式只调编译环境，不改构建目标/模板/压缩/场景。比本项目单纯 `WECHAT_SDK`/`DOUYIN_SDK` 宏多了「普通网页 WebGL 模式」与 **asmdef 级隔离**（防 SDK 程序集/JS 桥接混入网页包）。**仅作思路参考**：无 LICENSE 代码不可直接复用（默认保留所有权利）；面向标准 Unity 2022.3，团结引擎兼容性未验证（round 28 已知抖音 SDK asmdef 与团结冲突）；接入前走 [外部库选型评估](./外部库选型评估.md)。

### 激励视频为什么必须三件事(onClose 判 isEnded / onError 重建 / show 失败 load 重试)

1. **onClose 判 `isEnded`**:不判 = 用户点关闭也拿奖励,广告变现直接失效,还会触发平台审核风控。
2. **onError 销毁重建**:广告实例坏掉后监听和内部状态不会自愈,必须丢弃重建,否则后续永远失败。
3. **show 失败 → load → 再 show**:广告缓存经常为空(首次调用/库存不足),一次失败就放弃 = 大量可看广告流失;重试仍失败才回调 false。

JS 参考实现:

```javascript
let rewardedAd = null;

function createRewardedAd(adUnitId) {
  rewardedAd = tt.createRewardedVideoAd({ adUnitId }); // 微信换 wx.*
  rewardedAd.onError(() => { rewardedAd = null; });    // 坏了下次重建
}

function showRewardedAd(adUnitId) {
  return new Promise((resolve) => {
    if (!rewardedAd) createRewardedAd(adUnitId);
    const onClose = (res) => {
      rewardedAd.offClose(onClose);
      resolve(res && res.isEnded);                     // 看完才 true
    };
    rewardedAd.onClose(onClose);
    rewardedAd.show().catch(() =>
      rewardedAd.load().then(() => rewardedAd.show())
        .catch(() => { rewardedAd.offClose(onClose); resolve(false); })
    );
  });
}
```

架构上,`AdsManager` 收到 true 后统一广播 `GameEvents.RewardGranted(placement)`,奖励逻辑只在监听方做——防止「回调里散着发奖」导致看一次广告发两次或漏发。

### 代码剥离与 link.xml

小游戏构建默认开 Managed Stripping(裁剪未引用代码减包体),但裁剪按「静态引用」判断,**反射、JsonUtility 序列化、SDK 内部反射的类型可能被误裁**,真机上才炸。对策:Stripping Level ≥ Low,把反射/序列化用到的类型写进 `Assets/link.xml` 保留。

### 为什么 30 帧起步

小游戏跑在低端安卓机 WebView/容器里,60 帧下 CPU/GPU 满载 → 发热 → 系统降频反而掉帧。`Application.targetFrameRate = 30`(GameConfig 可调)是稳帧起点,确认有余量再上 60。

### PlayerPrefs 在小游戏的存储

WebGL 构建下 PlayerPrefs 落 IndexedDB;微信/抖音 SDK 会映射到平台本地文件存储。业务侧继续用 PlayerPrefs + JsonUtility(模板 SaveManager),不要自己封装平台存储,云端存档等需求再加到 IPlatformService。

### 渲染与资源出包口径

- 2D 休闲用内置管线 + Gamma 色彩空间最省;切 URP 只在需要后处理/多光源时做。
- 纹理 ASTC(移动端 WebGL 标准),音频 AAC 低码率,BGM 走远程 CDN 不进首包。
- 首包/总包体上限是平台政策数值,会变,用前查官方文档(不要凭记忆)。
- 真机性能/内存验收(连 Profiler、GC 尖峰、纹理内存、iOS 内存红线与 UnityHeap 预留)见 [Unity 真机 Profiling 与内存分析](./Unity真机Profiling与内存分析.md)。

## SDK 选型(用官方轮子)

| 平台 | 方案 | 来源 |
| --- | --- | --- |
| 微信 | minigame-tuanjie-transform-sdk(团结引擎专用,替代旧 minigame-unity-webgl-transform) | https://github.com/wechat-miniprogram/minigame-tuanjie-transform-sdk |
| 微信 | 适配文档 | https://developers.weixin.qq.com/minigame/dev/guide/game-engine/unity-webgl-transform.html |
| 抖音 | StarkSDK(官方 C# SDK) | https://developer.open-douyin.com/docs/resource/zh-CN/mini-game/develop/guide/game-engine/rd-to-SCgame/unity-game-access/sc_stark_sdk |
| 抖音 | 团结引擎内置抖音转换插件 | https://docs.unity.cn/cn/tuanjiemanual/Manual/DouyinSDK.html |
| 参考 | VastStarryRiver/MiniGame(社区,团结引擎双平台框架) | https://github.com/VastStarryRiver/MiniGame |
| 参考 | GameFrameX 抖音广告适配层(StarkSDK 封装示例) | https://github.com/GameFrameX/com.gameframex.unity.advertisement.douyinminigame |

出包步骤见模板 `docs/build-wechat.md`、`docs/build-douyin.md`;发布检查(包体/分包/类目)待补条目「微信与抖音小游戏发布检查」。

## 模板内置通用系统(资源/池化/Loading/弹窗/音频偏好)

### 资源加载为什么抽象 IAssetProvider,而不是直接 Resources.Load

- **Resources 全进首包**,小游戏首包有限额——这是硬约束,所以生产环境必须走 AssetBundle(远程 CDN)或 Addressables;开发期又要零配置跑通。抽象后编辑器恒走 Resources、生产定义 `RES_USE_BUNDLE` 切 AssetBundleProvider,与平台 SDK 符号同一套路,业务代码一行不改。
- 微信官方对资源远程加载的三条路线:[Addressables](https://developers.weixin.qq.com/minigame/dev/guide/game-engine/unity-webgl-transform/Design/UsingAddressable.html)(自动化最高,官方推荐)、[AssetBundle + WXAssetBundle](https://developers.weixin.qq.com/minigame/dev/guide/game-engine/unity-webgl-transform/Design/UsingAssetBundle.html)(手动管理但省内存)、[AutoStreaming](https://developers.weixin.qq.com/minigame/dev/guide/game-engine/unity-webgl-transform/Design/InstantGameGuide.html)。模板的 Provider 抽象保证三条路都能接:换 Provider,不改业务。
- bundle 命名用「目录名小写下划线」(`Prefabs/UI/Toast` → `prefabs_ui`):打包脚本按目录自动分配,加载侧按路径推导,两边规则一致就不需要人工维护映射表。

### 对象池的清理责任归谁

归**对象自己**:实现 `IPoolable`,OnDespawn 里退订事件/停协程/重置状态,OnSpawn 里初始化。池只管存取,不理解业务。原因:池不知道对象订阅了什么,代清理必然漏;「申请时清理」也不成立——池化对象生命周期归池,状态污染是复用 bug 的头号来源。同场景高频对象(子弹/飘字/格子)才池化,低频对象直接 Instantiate,别为池化而池化。

### Loading 为什么接管 allowSceneActivation

Unity 异步加载场景时 `progress` 到 **0.9 就停住**(等待激活许可),进度条会卡在 90% 很久,观感像死机。LoadingManager 设 `allowSceneActivation=false` 主动接管:0→90% 走真实加载进度,90% 后放行激活补到 100%,再停留 0.2s 让用户看到完成。未知时长的流程(网络请求组合)用循环条,别伪造假进度。

### 弹窗为什么用三层子 Canvas 而不是靠实例化顺序

同 Canvas 内层级靠 Sibling Index(实例化顺序)决定,弹窗一多、顺序一变层级就乱且难排查。三层子 Canvas(`Normal=0 / Popup=100 / Top=1000`)用 sortingOrder 数值稳定表达「面板<弹窗<Toast」,Loading 再盖在最上(2000)。遮罩由管理器统一创建(挡下层点击),窗口不用每个都自带遮罩;`destroyOnClose=false` 的窗缓存复用,避免反复实例化。

### 音频偏好为什么不进游戏存档

音乐/音效开关是**系统偏好**,不是游戏进度:存档结构按「只加字段」演进,混入偏好字段会污染存档版本;且设置页可能独立重置偏好。AudioManager 用自己的 `audio_*` PlayerPrefs 键持久化,互不干扰。

### AssetBundle 打包两步走(菜单 MiniGame/资源)

按目录分配 bundle 名 → WebGL LZ4(`ChunkBasedCompression`,解压快,小游戏加载体验优先)构建到工程根 `Bundles/WebGL` → 传 CDN → `ResManager.RemoteBundleBaseUrl` 赋值 + 定义 `RES_USE_BUNDLE`。依赖资源(图集/贴图)无 bundle 名时自动并入引用方,不用手动处理。



## 待验证

- AssetBundle 在微信/抖音转换 SDK 下的网络层适配(UnityWebRequest 映射、WXAssetBundle 直读)未实测,接 SDK 后按官方文档验证。
- 两个官方 SDK 的 C# API 具体签名未在本工程实测:模板用 `#if WECHAT_SDK / DOUYIN_SDK` 隔离 + TODO 占位,装 SDK 后按编译报错对齐即可,不影响默认工程编译。
- StarkSDK 与团结引擎内置插件二选一的取舍(是否功能重叠)需装包后确认。

## 相关条目

- [平台能力开通与接入检查](./平台能力开通与接入检查.md) — SDK 安装步骤、能力矩阵、端到端 checklist、广告真机验收。
- [小游戏资质与类目](./小游戏资质与类目.md) — 开通前置：IAA/IAP 分流、备案、类目。
- [微信与抖音小游戏发布检查](./微信与抖音小游戏发布检查.md) — 包体/分包硬约束、发布流程。
- [小游戏音频管理与音视频适配](./小游戏音频管理与音视频适配.md) — AudioManager 实现、UnityAudio 适配、音频格式与 iOS 音频坑（音频偏好决策见本条目「音频偏好为什么不进游戏存档」）。
