using System;

namespace MiniGame.Platform
{
    /// <summary>
    /// 平台能力抽象:游戏逻辑只依赖这个接口,微信/抖音的 SDK 细节封在各自实现里。
    /// 要加平台能力时:先在这里加方法,再分别补齐三个实现(微信/抖音/编辑器 Stub)。
    /// </summary>
    public interface IPlatformService
    {
        string PlatformName { get; }

        /// <summary>登录:拿到 code 后交给自家服务端换 openid,客户端不落 openid。</summary>
        void Login(Action<string> onCode);

        /// <summary>
        /// 激励视频。实现必须保证三件事,缺一不可:
        /// 1. onClose 里判 isEnded == true 才回调 true(中途关闭不发奖);
        /// 2. onError 里销毁重建实例(坏实例不会再恢复);
        /// 3. show 失败先 load 再 show,仍失败才回调 false(不抛异常,回调必须到达)。
        /// </summary>
        void ShowRewardedVideo(string adUnitId, Action<bool> onRewarded);

        void VibrateShort();

        void ShareApp(string title, string imageUrl);
    }
}
