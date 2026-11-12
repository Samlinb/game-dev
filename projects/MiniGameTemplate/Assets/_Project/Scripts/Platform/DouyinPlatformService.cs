using System;
using UnityEngine;

namespace MiniGame.Platform
{
    /// <summary>
    /// 抖音小游戏实现(StarkSDK)。官方文档:https://developer.open-douyin.com/docs/resource/zh-CN/mini-game/develop/guide/game-engine/rd-to-SCgame/unity-game-access/sc_stark_sdk
    /// 团结引擎另有内置抖音转换插件:https://docs.unity.cn/cn/tuanjiemanual/Manual/DouyinSDK.html
    ///
    /// 只在 PlayerSettings(WebGL)定义 DOUYIN_SDK 后,才把 #if 里的 TODO 换成 SDK 真实现;
    /// 未定义时是空壳(Stub 语义),保证工程随时能编译运行。
    /// StarkSDK 的 C# API 名以官方文档为准,对不上时按编译报错对齐。
    /// 启用步骤与出包要点见 docs/build-douyin.md。
    /// </summary>
    public class DouyinPlatformService : IPlatformService
    {
#if DOUYIN_SDK
        public string PlatformName => "Douyin";

        // 广告实例只创建一次并常驻复用(onClose/onError 监听挂实例上,重建才丢失)
        // TODO: 声明为 StarkSDK 的激励视频管理器/实例类型

        public void Login(Action<string> onCode)
        {
            // tt.login → code。code 交服务端换 openid,客户端不落 openid。
            // TODO: 调 StarkSDK 封装,如 StarkSDK.API.Login(...);
            throw new NotImplementedException("接 StarkSDK 后实现,见 docs/build-douyin.md");
        }

        public void ShowRewardedVideo(string adUnitId, Action<bool> onRewarded)
        {
            // 三要素(缺一不可),JS 侧参考实现见知识库条目「Unity 小游戏项目架构」:
            // 1. onClose 里判 res.isEnded == true 才回调 true;
            // 2. onError 里置空/销毁重建实例;
            // 3. show().catch(() => load().then(show)),仍失败才回调 false。
            // TODO: 用 StarkSDK C# 封装落地上面的三步。
            throw new NotImplementedException("接 StarkSDK 后实现,见 docs/build-douyin.md");
        }

        public void VibrateShort()
        {
            // TODO: 调 StarkSDK 封装(tt.vibrateShort)。
            throw new NotImplementedException("接 StarkSDK 后实现");
        }

        public void ShareApp(string title, string imageUrl)
        {
            // TODO: 调 StarkSDK 封装(tt.shareAppMessage)。
            // 抖音特色另可接 tt.getGameRecorderManager 录屏分享,通关/破纪录节点引导。
            throw new NotImplementedException("接 StarkSDK 后实现");
        }
#else
        public string PlatformName => "Douyin(未接SDK)";

        public void Login(Action<string> onCode) => onCode?.Invoke("douyin_stub_code");

        public void ShowRewardedVideo(string adUnitId, Action<bool> onRewarded)
        {
            Debug.LogWarning($"[Douyin] 未定义 DOUYIN_SDK,广告直接按「看完」处理。adUnit={adUnitId}");
            onRewarded?.Invoke(true);
        }

        public void VibrateShort() { }

        public void ShareApp(string title, string imageUrl)
            => Debug.Log($"[Douyin] 未接 SDK,分享仅打日志: {title}");
#endif
    }
}
