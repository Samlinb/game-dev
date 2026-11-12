# 出包:微信小游戏(团结引擎路径)

官方 SDK:[wechat-miniprogram/minigame-tuanjie-transform-sdk](https://github.com/wechat-miniprogram/minigame-tuanjie-transform-sdk)
适配文档:[微信小游戏 Unity/团结引擎技术适配方案](https://developers.weixin.qq.com/minigame/dev/guide/game-engine/unity-webgl-transform.html)

> 包体上限、类目资质等数值类要求以官方最新文档为准,本文不写死数字(核对日期:2026-11-11)。

## 步骤

1. 注册微信小游戏开发者账号,创建小游戏拿 AppID;下载安装「微信开发者工具」。
2. 按 SDK 仓库 README 把转换 SDK 装进本工程(落在 `Assets/Plugins/`,确认许可证后再入库)。
3. PlayerSettings → Scripting Define Symbols(WebGL 构建)加 `WECHAT_SDK`,激活 `WeChatPlatformService` 真实现;按编译报错把文件里 TODO 处的 API 名对齐到 SDK 实际封装。
4. 落地四个 TODO:`Login`(code 交服务端换 openid)/ `ShowRewardedVideo`(三要素缺一不可)/ `VibrateShort` / `ShareApp`。
5. `Assets/_Project/Data/AdConfig.asset` 填微信广告位 ID(流量主开通后才可创建广告位)。
6. 构建前自查(菜单 `MiniGame → 3.应用小游戏 PlayerSettings` 已设大部分):
   - 纹理 ASTC;音频 AAC 低码率;背景音乐走远程 CDN 不进首包;
   - Managed Stripping Level ≥ Low,反射用到的类型写进 `link.xml`;
   - 色彩空间 Gamma(模板已设,美术验收时注意观感)。
7. Build 出 WebGL → 转换工具生成小游戏工程 → 微信开发者工具打开预览/上传。

## 验收

- 工具内不出真实广告,**广告必须真机验证**:看完(isEnded)才发奖、中途关闭不发、断网重试路径可走通。
- 真机看首屏加载时长、内存、DrawCall;低端机以 30 帧为目标。
