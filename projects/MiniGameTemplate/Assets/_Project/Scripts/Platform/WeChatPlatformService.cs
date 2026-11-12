using System;
using UnityEngine;

namespace MiniGame.Platform
{
    /// <summary>
    /// 微信小游戏实现。官方 SDK:https://github.com/wechat-miniprogram/minigame-tuanjie-transform-sdk
    /// (团结引擎专用包,替代旧 minigame-unity-webgl-transform)。
    ///
    /// 只在 PlayerSettings(WebGL)定义 WECHAT_SDK 后,才把 #if 里的 TODO 换成 SDK 真实现;
    /// 未定义时是空壳(Stub 语义),保证工程随时能编译运行。
    /// SDK 的 C# API 名以实际安装版本的 SDK README 为准,对不上时按编译报错对齐。
    /// 启用步骤与出包要点见 docs/build-wechat.md。
    /// </summary>
    public class WeChatPlatformService : IPlatformService
    {
#if WECHAT_SDK
        public string PlatformName => "WeChat";

        // 广告实例只创建一次并常驻复用(onClose/onError 监听挂实例上,重建才丢失)
        // TODO: 声明为 SDK 的激励视频类型,如 WX.CreateRewardedVideoAd(...) 的返回类型

        public void Login(Action<string> onCode)
        {
            // wx.login → code。code 交服务端换 openid,客户端不落 openid。
            // TODO: 调 SDK 封装,如 WX.Login(code => onCode(code));
            throw new NotImplementedException("接微信 SDK 后实现,见 docs/build-wechat.md");
        }

        public void ShowRewardedVideo(string adUnitId, Action<bool> onRewarded)
        {
            // 三要素(缺一不可),JS 侧参考实现见知识库条目「Unity 小游戏项目架构」:
            // 1. onClose 里判 res.isEnded == true 才回调 true;
            // 2. onError 里置空/销毁重建实例;
            // 3. show().catch(() => load().then(show)),仍失败才回调 false。
            // TODO: 用 SDK C# 封装落地上面的三步。
            throw new NotImplementedException("接微信 SDK 后实现,见 docs/build-wechat.md");
        }

        public void VibrateShort()
        {
            // TODO: 调 SDK 封装,如 WX.VibrateShort();
            throw new NotImplementedException("接微信 SDK 后实现");
        }

        public void ShareApp(string title, string imageUrl)
        {
            // TODO: 调 SDK 封装,如 WX.ShareAppMessage(...);
            throw new NotImplementedException("接微信 SDK 后实现");
        }
#else
        public string PlatformName => "WeChat(未接SDK)";

        public void Login(Action<string> onCode) => onCode?.Invoke("wechat_stub_code");

        public void ShowRewardedVideo(string adUnitId, Action<bool> onRewarded)
        {
            Debug.LogWarning($"[WeChat] 未定义 WECHAT_SDK,广告直接按「看完」处理。adUnit={adUnitId}");
            onRewarded?.Invoke(true);
        }

        public void VibrateShort() { }

        public void ShareApp(string title, string imageUrl)
            => Debug.Log($"[WeChat] 未接 SDK,分享仅打日志: {title}");
#endif
    }
}
