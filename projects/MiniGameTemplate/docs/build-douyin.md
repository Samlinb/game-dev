# 出包:抖音小游戏(团结引擎路径)

官方文档:[StarkSDK C# 接口](https://developer.open-douyin.com/docs/resource/zh-CN/mini-game/develop/guide/game-engine/rd-to-SCgame/unity-game-access/sc_stark_sdk)
团结引擎:[内置抖音小游戏转换 SDK 插件说明](https://docs.unity.cn/cn/tuanjiemanual/Manual/DouyinSDK.html)

> 包体上限、类目资质等数值类要求以抖音开放平台最新文档为准(核对日期:2026-11-11)。

## 步骤

1. 注册抖音小游戏开发者账号,创建小游戏拿 AppID;下载安装「抖音开发者工具」。
2. 两条路径二选一:
   - **团结引擎内置插件**:引擎自带抖音小游戏转换 SDK 插件,按手册启用,免装外部包;
   - **StarkSDK-Unity Tools**:按官方文档安装,配合 bgdt 工具一键构建运行到抖音。
3. PlayerSettings → Scripting Define Symbols(WebGL 构建)加 `DOUYIN_SDK`,激活 `DouyinPlatformService` 真实现;按编译报错把 TODO 处 API 名对齐到 StarkSDK 实际封装。
4. 落地四个 TODO:`Login`(code 交服务端换 openid)/ `ShowRewardedVideo`(三要素缺一不可)/ `VibrateShort` / `ShareApp`。
5. `Assets/_Project/Data/AdConfig.asset` 填抖音广告位 ID。
6. 构建前自查:纹理 ASTC、音频 AAC 低码率、Managed Stripping Level ≥ Low + `link.xml`、背景音乐远程加载。
7. 构建转换生成小游戏工程 → 抖音开发者工具打开预览/上传。

## 抖音特色

- 录屏分享:`tt.getGameRecorderManager` 录游戏高光发布,通关/破纪录节点主动引导。
- 分享:`tt.shareAppMessage`,合成/过关节点引导分享。
- 震动:`tt.vibrateShort` 配合关键操作反馈(已抽象在 IPlatformService.VibrateShort)。

## 验收

- 工具内不出真实广告,**广告必须真机验证**:看完(isEnded)才发奖、onError 重建、show 失败 load 重试三条路径都要走一遍。
- 抖音开发者工具开 vConsole/远程调试;关注首屏时长、内存(超限直接闪退)、DrawCall;低端机 30 帧目标。
