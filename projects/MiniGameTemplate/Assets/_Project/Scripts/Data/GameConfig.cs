using UnityEngine;

namespace MiniGame.Data
{
    /// <summary>
    /// 全局可调数值。禁止用静态常量存可调数值,改数值来这里,不动代码。
    /// </summary>
    [CreateAssetMenu(menuName = "MiniGame/GameConfig")]
    public class GameConfig : ScriptableObject
    {
        [Header("小游戏低端机 30 帧起步,发热/掉帧再权衡")]
        public int targetFrameRate = 30;
    }
}
