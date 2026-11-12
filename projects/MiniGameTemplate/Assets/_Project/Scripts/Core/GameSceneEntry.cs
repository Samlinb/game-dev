using MiniGame.UI;
using UnityEngine;

namespace MiniGame.Core
{
    /// <summary>
    /// Game 场景入口:进场景后显示首页面板。面板流转全部经 UIManager,不在场景里散放 UI。
    /// </summary>
    public class GameSceneEntry : MonoBehaviour
    {
        private void Start()
        {
            UIManager.Instance?.ShowOnly(nameof(HomePanel));
        }
    }
}
