using UnityEngine;

namespace MiniGame.Data
{
    /// <summary>
    /// 玩家数值示例。用法见 Gameplay/PlayerMovement.cs:
    /// 使用方 [SerializeField] private PlayerConfig config; 在 Inspector 拖入资产。
    /// </summary>
    [CreateAssetMenu(menuName = "MiniGame/PlayerConfig")]
    public class PlayerConfig : ScriptableObject
    {
        public float moveSpeed = 5f;
        public int maxHealth = 3;
        public float reviveAdCooldown = 30f; // 复活广告冷却(秒)
    }
}
