using System;
using MiniGame.Data;
using UnityEngine;

namespace MiniGame.Core
{
    public enum GameState
    {
        Menu,
        Playing,
        Paused,
        Settling,
    }

    /// <summary>
    /// 游戏状态机。挂在 Boot 场景 Managers 节点,DontDestroyOnLoad,全工程唯一。
    /// 跨系统通信走事件(OnStateChanged),不互相调方法链。
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [SerializeField] private GameConfig config;

        public GameState State { get; private set; } = GameState.Menu;

        /// <summary>状态变化事件:关心的系统自己订阅,别让 GameManager 逐个通知。</summary>
        public static event Action<GameState> OnStateChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            // 小游戏低端机发热明显,30 帧起步;数值在 GameConfig 里调
            Application.targetFrameRate = config != null ? config.targetFrameRate : 30;
        }

        public void StartGame()
        {
            SetState(GameState.Playing);
            GameEvents.RaiseGameStarted();
        }

        public void PauseGame() => SetState(GameState.Paused);

        public void ResumeGame() => SetState(GameState.Playing);

        public void EndGame()
        {
            SetState(GameState.Settling);
            GameEvents.RaiseGameEnded();
        }

        public void BackToMenu() => SetState(GameState.Menu);

        private void SetState(GameState next)
        {
            if (State == next) return;
            State = next;
            OnStateChanged?.Invoke(next);
        }
    }
}
