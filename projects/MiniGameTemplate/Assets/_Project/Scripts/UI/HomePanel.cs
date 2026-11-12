using MiniGame.Ads;
using MiniGame.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MiniGame.UI
{
    /// <summary>
    /// 示例面板:开始游戏 + 看广告领金币完整链路。
    /// 新面板照抄这个套路:继承 UIPanel → OnShow 读数据/订阅事件 → 按钮回调改状态。
    /// </summary>
    public class HomePanel : UIPanel
    {
        [SerializeField] private Text coinsText;
        [SerializeField] private Button startButton;
        [SerializeField] private Button adButton;

        private void Awake()
        {
            if (startButton != null) startButton.onClick.AddListener(OnStartClicked);
            if (adButton != null) adButton.onClick.AddListener(OnAdClicked);
        }

        public override void OnShow()
        {
            RefreshCoins();
            GameEvents.RewardGranted += OnRewardGranted;
            AudioManager.Instance?.PlayMusic();
        }

        public override void OnHide()
        {
            GameEvents.RewardGranted -= OnRewardGranted; // 与 OnShow 成对,防重复订阅
        }

        private void OnStartClicked()
        {
            UIManager.Instance.Hide(nameof(HomePanel));
            GameManager.Instance.StartGame();
        }

        private void OnAdClicked()
        {
            if (AdsManager.Instance == null) return;
            adButton.interactable = false; // 防连点,回调里恢复
            AdsManager.Instance.ShowRewarded("double_coins", ok =>
            {
                adButton.interactable = true;
                if (!ok) UIManager.Instance?.ShowToast("未看完广告,不发奖励");
            });
        }

        private void OnRewardGranted(string placement)
        {
            if (placement != "double_coins") return;
            SaveManager.Data.coins += 100;
            SaveManager.Save();
            RefreshCoins();
        }

        private void RefreshCoins()
        {
            if (coinsText != null) coinsText.text = $"金币 {SaveManager.Data.coins}";
        }
    }
}
