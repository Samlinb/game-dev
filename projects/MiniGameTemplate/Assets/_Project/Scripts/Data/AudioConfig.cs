using System;
using System.Collections.Generic;
using UnityEngine;

namespace MiniGame.Data
{
    /// <summary>
    /// 音乐与音效配置。音效按 key 查表播放(AudioManager.PlaySfx(key))。
    /// </summary>
    [CreateAssetMenu(menuName = "MiniGame/AudioConfig")]
    public class AudioConfig : ScriptableObject
    {
        public AudioClip bgm;

        [Range(0f, 1f)] public float musicVolume = 0.7f;
        [Range(0f, 1f)] public float sfxVolume = 1f;

        public List<SfxEntry> sfx = new List<SfxEntry>();

        [Serializable]
        public class SfxEntry
        {
            public string key;
            public AudioClip clip;
        }
    }
}
