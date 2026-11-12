# Plugins

第三方 SDK 只放这里,与 `Assets/_Project/` 隔离:

- 微信:minigame-tuanjie-transform-sdk(https://github.com/wechat-miniprogram/minigame-tuanjie-transform-sdk)
- 抖音:StarkSDK / 团结引擎内置抖音转换插件

装好 SDK 后,在 PlayerSettings → Scripting Define Symbols(WebGL)加对应符号
(`WECHAT_SDK` / `DOUYIN_SDK`),`Scripts/Platform/` 下的真实现才会参与编译。
