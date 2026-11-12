using System;

namespace MiniGame.Core
{
    /// <summary>
    /// 跨系统事件通道:发事件的一方不关心谁监听。
    /// 例如 AdsManager 发 RewardGranted,HomePanel 监听加金币——两者互相不知道对方存在。
    /// </summary>
    public static class GameEvents
    {
        public static event Action GameStarted;
        public static event Action GameEnded;

        /// <summary>激励视频确认看完(isEnded == true)后才发;发奖逻辑统一监听这里,别散在业务回调里。</summary>
        public static event Action<string> RewardGranted;

        public static void RaiseGameStarted() => GameStarted?.Invoke();
        public static void RaiseGameEnded() => GameEnded?.Invoke();
        public static void RaiseRewardGranted(string placement) => RewardGranted?.Invoke(placement);
    }
}
