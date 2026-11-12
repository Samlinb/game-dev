using System;
using MiniGame.Core;
using MiniGame.Data;
using MiniGame.Platform;
using UnityEngine;

namespace MiniGame.Ads
{
    /// <summary>
    /// 激励视频统一入口:业务只传场景位(placement,如 double_coins / revive),
    /// 广告位 ID 由 AdConfig 按平台配置。发奖统一走 GameEvents.RewardGranted,业务回调只处理失败提示。
    /// 复活/翻倍等具体奖励逻辑在监听方实现,这里不做任何数值改动。
    /// </summary>
    public class AdsManager : MonoBehaviour
    {
        public static AdsManager Instance { get; private set; }

        [SerializeField] private AdConfig config;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public void ShowRewarded(string placement, Action<bool> onResult)
        {
            var platform = PlatformService.Instance.PlatformName;
            var adUnitId = config != null ? config.GetAdUnitId(platform, placement) : null;

            // 编辑器允许空广告位(Stub 不校验);真机必须配好,否则按失败处理
            if (string.IsNullOrEmpty(adUnitId) && platform != "Editor")
            {
                Debug.LogError($"[AdsManager] 场景位 {placement} 没配 {platform} 的广告位 ID,去 AdConfig 资产里填。");
                onResult?.Invoke(false);
                return;
            }

            PlatformService.Instance.ShowRewardedVideo(adUnitId, ok =>
            {
                if (ok) GameEvents.RaiseRewardGranted(placement); // 确认看完才广播发奖
                onResult?.Invoke(ok);
            });
        }
    }
}
