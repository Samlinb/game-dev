using System;
using UnityEngine;

namespace MiniGame.Core
{
    /// <summary>
    /// 示例存档结构:按项目需要加字段,只加不改(老档兼容)。
    /// 音乐/音效开关不在这里(音频偏好由 AudioManager 自己持久化 audio_* 键);
    /// 平台托管存储(云存档)走 IPlatformService,别在这里直接加。
    /// </summary>
    [Serializable]
    public class GameSaveData
    {
        public int highestLevel = 1;
        public int coins = 0;
    }

    /// <summary>
    /// 本地存档:PlayerPrefs 存 JSON。WebGL/小游戏构建下 PlayerPrefs 由引擎/SDK 映射到本地存储。
    /// </summary>
    public static class SaveManager
    {
        private const string Key = "save_v1";

        public static GameSaveData Data { get; private set; } = Load();

        public static void Save()
        {
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(Data));
            PlayerPrefs.Save();
        }

        private static GameSaveData Load()
        {
            var json = PlayerPrefs.GetString(Key, string.Empty);
            if (string.IsNullOrEmpty(json)) return new GameSaveData();
            try
            {
                return JsonUtility.FromJson<GameSaveData>(json) ?? new GameSaveData();
            }
            catch
            {
                return new GameSaveData();
            }
        }
    }
}
