# MiniGameTemplate:微信 + 抖音小游戏 Unity 模板工程

团结引擎(Tuanjie)· 2D 休闲 · 单场景 + 面板切换 · 双平台抽象层 · 内置资源/池化/Loading/弹窗/音频通用系统。
设计决策与背后逻辑见知识库条目:[Unity 小游戏项目架构](../../knowledge-base/Unity%20小游戏项目架构.md)。

## 快速开始

1. 用团结引擎打开本目录(首次打开由引擎生成 `Packages/manifest.json` 与 `ProjectSettings`,稍慢属正常)。
2. 菜单 `MiniGame → 一键初始化`:生成配置资产、Boot/Game 场景(含三层 UI)、通用 Prefab(HomePanel/Toast/DialogWindow),并应用小游戏 PlayerSettings。
3. Boot 场景按 Play:Loading 真实进度 → 进首页 → 「开始游戏」/「看广告 +100」(编辑器下广告走 Stub,1 秒后模拟「看完」)。
4. 出包:微信见 [docs/build-wechat.md](docs/build-wechat.md),抖音见 [docs/build-douyin.md](docs/build-douyin.md);资源远程加载见下方「资源与打包」。

## 目录结构

```
Assets/
  _Project/                  # 自己的全部内容(下划线前缀置顶,与第三方隔离)
    Art/                     # 图片、动画、音频(按功能分子目录;子目录即一个 AssetBundle)
    Prefabs/
      UI/                    # 面板/弹窗/Toast Prefab(一键初始化生成 HomePanel/Toast/DialogWindow)
    Scenes/
      Boot.unity             # 启动场景:创建管理器 → 统一 Loading 进 Game
      Game.unity             # 主场景:UIRoot 三层(Normal/Popup/Top)+ 面板实例化
    Scripts/
      Core/                  # GameManager 状态机、AudioManager、SaveManager、LoadingManager、事件、场景入口
      Res/                   # ResManager + IAssetProvider(Resources/AssetBundle)+ PrefabPool 对象池
      Gameplay/              # 玩法脚本(PlayerMovement 示例:数值从 SO 读)
      UI/                    # UIManager 三层、UIPanel/UIWindow、Toast、DialogWindow、UGUIUtil
      Platform/              # IPlatformService 抽象 + 微信/抖音/Stub 三个实现
      Ads/                   # AdsManager:激励视频统一入口
      Data/                  # ScriptableObject 定义
      Editor/                # ProjectBootstrap 一键初始化 + AssetBundleBuilder 打包
    Data/                    # SO 配置资产实例(一键初始化生成)
    Settings/                # 渲染/物理等设置资产
  Plugins/                   # 微信/抖音官方 SDK 安装位置
```

## 脚本职责(一句话)

| 脚本 | 职责 |
| --- | --- |
| Core/GameManager | 游戏状态机(菜单/游玩/暂停/结算),启动时设目标帧率 |
| Core/AudioManager | BGM 循环 + SFX OneShot;音乐/音效开关持久化(audio_* 键) |
| Core/SaveManager | PlayerPrefs 存 JSON 存档,读写 GameSaveData |
| Core/LoadingManager | 统一 Loading:场景真实进度 / 任意协程循环条 / 手动模式;UI 代码构建不依赖资源 |
| Core/BootLoader | Boot 场景入口:管理器就绪后走 LoadingManager 进 Game |
| Core/GameSceneEntry | Game 场景入口:显示首页面板 |
| Core/GameEvents | 跨系统静态事件(开局/结算/发奖),替代管理器互调 |
| Res/ResManager | 资源加载统一入口:按构建选 Provider(编辑器 Resources / 生产 AssetBundle) |
| Res/IAssetProvider | Provider 抽象;切 Addressables(微信官方推荐)时新增实现即可,业务不动 |
| Res/ResourcesAssetProvider | Resources 实现,零依赖(注意:Resources 全进首包) |
| Res/AssetBundleProvider | AssetBundle 实现:远端 CDN / StreamingAssets,按目录名找 bundle |
| Res/PrefabPool | Prefab 对象池:Spawn/Despawn/PrewarmAsync;清理责任在对象自己(IPoolable) |
| UI/UIManager | UI 统一入口:三层(Normal 面板 / Popup 弹窗+遮罩 / Top Toast) |
| UI/UIPanel | 普通面板基类:OnShow/OnHide/OnRefresh 生命周期 |
| UI/UIWindow | 弹窗基类:遮罩/关闭回调托管,destroyOnClose 可复用 |
| UI/DialogWindow | 通用确认框(UIManager.ShowConfirm 一行弹出) |
| UI/Toast | 轻提示:UIManager.ShowToast,淡入淡出自毁 |
| UI/UGUIUtil | 代码建 uGUI 工具(Loading/遮罩等工具型 UI 不依赖 Prefab) |
| UI/HomePanel | 示例面板:开始游戏 + 看广告领金币 + Toast 完整链路 |
| Platform/IPlatformService | 平台能力抽象:登录/激励视频/震动/分享 |
| Platform/PlatformService | 按编译符号选实现(编辑器恒 Stub) |
| Platform/WeChatPlatformService | 微信实现(#if WECHAT_SDK) |
| Platform/DouyinPlatformService | 抖音实现(#if DOUYIN_SDK) |
| Platform/EditorStubPlatformService | 编辑器/兜底实现,全流程可跑 |
| Ads/AdsManager | 业务只传场景位(placement),查 AdConfig 调平台服务,确认看完才发奖 |
| Data/GameConfig | 全局数值(目标帧率等) |
| Data/PlayerConfig | 玩家数值示例(移速/血量/复活广告冷却) |
| Data/AudioConfig | BGM、音量表、音量 |
| Data/AdConfig | 场景位 → 各平台广告位 ID 映射 |
| Gameplay/PlayerMovement | 玩法示例:演示 PlayerConfig 的接入方式 |
| Editor/ProjectBootstrap | 菜单一键初始化:资产/场景/Prefab/三层 UI/PlayerSettings |
| Editor/AssetBundleBuilder | 资源打包:按目录分配 bundle 名 + WebGL LZ4 构建 |

## 数据流

- **数值**:Data/ 下 SO → 管理器与业务脚本 Inspector 引用,运行期只读。改数值不动代码。
- **状态**:UI → `GameManager.StartGame/EndGame`(状态机)→ `OnStateChanged` 事件 → 关心的系统自行响应。
- **资源**:`ResManager.LoadAsync/InstantiatePrefab`(一次性)/ `PrefabPool.Spawn/Despawn`(高频复用)→ Provider 按构建解析(Resources / AssetBundle)。
- **加载**:`LoadingManager.LoadScene`(场景)/ `.Run(协程)`(任意流程)/ `Show+SetProgress`(手动),UI 排序最高、Boot 阶段即可用。
- **发奖**:业务调 `AdsManager.ShowRewarded(placement)` → 平台实现 → 确认看完(isEnded)→ `GameEvents.RaiseRewardGranted` → 监听方加金币并存档。
- **UI**:面板 `UIManager.ShowOnly(key)`;弹窗 `ShowWindow/ShowConfirm`(自动遮罩,onClosed 回调);提示 `ShowToast`(Top 层自毁)。

## 资源与打包(生产小游戏必读)

Resources 目录**全部进首包**——小游戏首包有限额,所以:

1. **开发期**:直接用 Resources Provider(默认),零配置跑通。
2. **生产**:`MiniGame/资源/1.按目录分配 Bundle 名` → `2.打包 AssetBundles(WebGL)` → bundle 上传 CDN → Boot 初始化处 `ResManager.RemoteBundleBaseUrl = "https://你的CDN/bundles/webgl"` → PlayerSettings(WebGL)定义 `RES_USE_BUNDLE`。
3. 代码侧统一走 `ResManager` / `PrefabPool`,切 Provider 业务零改动;要上 Addressables(微信官方推荐,自动化更高)时实现一个新 Provider 即可。
4. bundle 命名规则:资源子目录 → 小写下划线(`Prefabs/UI/x.prefab` → bundle `prefabs_ui`),依赖资源自动并入引用方。

## 关键约定

- 面板 key 与面板类名同名(`nameof(HomePanel)`);Toast/DialogWindow 是固定 key,经 `ShowToast/ShowConfirm` 使用,别直接实例化。
- 池化对象实现 `IPoolable`:OnDespawn 里自己退订事件/停协程/重置状态;Spawn/Despawn 必须成对。
- 平台能力只加在 `IPlatformService`,三个实现各补一份;SDK 装好后在 PlayerSettings(WebGL)定义 `WECHAT_SDK` / `DOUYIN_SDK`。
- 可调数值禁止写静态常量,一律进 `Data/` 下 SO;音频偏好归 AudioManager(audio_* 键),不混游戏存档。
- 管理器只在 Boot 场景创建,DontDestroyOnLoad;UI 随 Game 场景走,不跨场景。

## SDK 选型(用官方轮子,不自研平台 API)

- 微信:[minigame-tuanjie-transform-sdk](https://github.com/wechat-miniprogram/minigame-tuanjie-transform-sdk)(官方团结引擎 SDK);资源加载官方推荐 [Addressables](https://developers.weixin.qq.com/minigame/dev/guide/game-engine/unity-webgl-transform/Design/UsingAddressable.html) 或 [WXAssetBundle](https://developers.weixin.qq.com/minigame/dev/guide/game-engine/unity-webgl-transform/Design/UsingAssetBundle.html)
- 抖音:[StarkSDK 官方文档](https://developer.open-douyin.com/docs/resource/zh-CN/mini-game/develop/guide/game-engine/rd-to-SCgame/unity-game-access/sc_stark_sdk),团结引擎另有[内置抖音转换插件](https://docs.unity.cn/cn/tuanjiemanual/Manual/DouyinSDK.html)
