using MiniGame.Data;
using UnityEngine;

namespace MiniGame.Gameplay
{
    /// <summary>
    /// 玩法示例:键盘移动,演示 PlayerConfig 数值的接入方式。
    /// 真实项目的输入与移动按玩法重写,唯一要求:可调数值一律从 Config SO 读,不写魔法数字。
    /// </summary>
    public class PlayerMovement : MonoBehaviour
    {
        [SerializeField] private PlayerConfig config;

        private void Update()
        {
            if (config == null) return;
            var dir = new Vector3(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"), 0f);
            transform.position += dir * (config.moveSpeed * Time.deltaTime);
        }
    }
}
