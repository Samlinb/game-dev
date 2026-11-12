using UnityEngine;

namespace MiniGame.Core
{
    /// <summary>
    /// Boot 场景专用入口:管理器就绪后走统一 Loading 进 Game 场景(真实进度条)。
    /// Boot 场景只做初始化,不放游戏内容。
    /// </summary>
    public class BootLoader : MonoBehaviour
    {
        private void Start()
        {
            LoadingManager.Instance.LoadScene("Game", "进入游戏");
        }
    }
}
