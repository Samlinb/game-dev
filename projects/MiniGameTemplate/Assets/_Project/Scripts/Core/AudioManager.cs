using System.Collections.Generic;
using MiniGame.Data;
using UnityEngine;

namespace MiniGame.Core
{
    /// <summary>
    /// 音乐/音效播放。两个 AudioSource(Music 常驻循环 / SFX OneShot)由一键初始化在 Boot 场景建好并引用。
    /// 音乐/音效开关由这里持久化(PlayerPrefs audio_* 键),不混进游戏存档。
    /// 出包要点:背景音乐走远程加载(ResManager 拉取),别打进首包。
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        private const string MusicPrefKey = "audio_music_on";
        private const string SfxPrefKey = "audio_sfx_on";

        [SerializeField] private AudioConfig config;
        [SerializeField] private AudioSource musicSource;
        [SerializeField] private AudioSource sfxSource;

        private Dictionary<string, AudioClip> sfxTable;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            BuildSfxTable();
        }

        public bool IsMusicOn => PlayerPrefs.GetInt(MusicPrefKey, 1) == 1;
        public bool IsSfxOn => PlayerPrefs.GetInt(SfxPrefKey, 1) == 1;

        /// <summary>音乐开关(持久化):关→停当前音乐;开→恢复播放。</summary>
        public void SetMusicOn(bool on)
        {
            PlayerPrefs.SetInt(MusicPrefKey, on ? 1 : 0);
            PlayerPrefs.Save();
            if (on) PlayMusic();
            else StopMusic();
        }

        /// <summary>音效开关(持久化),影响后续 PlaySfx。</summary>
        public void SetSfxOn(bool on)
        {
            PlayerPrefs.SetInt(SfxPrefKey, on ? 1 : 0);
            PlayerPrefs.Save();
        }

        public void PlayMusic()
        {
            if (!IsMusicOn) return;
            if (config == null || config.bgm == null || musicSource == null) return;
            musicSource.clip = config.bgm;
            musicSource.volume = config.musicVolume;
            musicSource.loop = true;
            musicSource.Play();
        }

        public void StopMusic()
        {
            if (musicSource != null) musicSource.Stop();
        }

        /// <summary>按 key 播音效,key 在 AudioConfig 的 sfx 表里配。</summary>
        public void PlaySfx(string key)
        {
            if (!IsSfxOn) return;
            if (sfxTable != null && sfxTable.TryGetValue(key, out var clip) && sfxSource != null)
            {
                sfxSource.volume = config.sfxVolume;
                sfxSource.PlayOneShot(clip);
            }
        }

        private void BuildSfxTable()
        {
            sfxTable = new Dictionary<string, AudioClip>();
            if (config == null) return;
            foreach (var entry in config.sfx)
                if (!string.IsNullOrEmpty(entry.key) && entry.clip != null)
                    sfxTable[entry.key] = entry.clip;
        }
    }
}
