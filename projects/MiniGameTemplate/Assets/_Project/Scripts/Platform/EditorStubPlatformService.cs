using System;
using System.Collections;
using UnityEngine;

namespace MiniGame.Platform
{
    /// <summary>
    /// 编辑器/未知平台兜底实现:全流程可跑,广告延迟 1 秒按「看完」回调,
    /// 方便先在编辑器验证业务逻辑,再接真 SDK。
    /// </summary>
    public class EditorStubPlatformService : IPlatformService
    {
        public string PlatformName => "Editor";

        public void Login(Action<string> onCode)
        {
            Debug.Log("[Platform:Stub] Login → 返回假 code");
            onCode?.Invoke("stub_code");
        }

        public void ShowRewardedVideo(string adUnitId, Action<bool> onRewarded)
        {
            Debug.Log($"[Platform:Stub] 激励视频 adUnit={adUnitId} → 1 秒后按「看完」回调(模拟真机异步)");
            var runner = new GameObject("StubAdRunner").AddComponent<StubRunner>();
            runner.Run(() =>
            {
                onRewarded?.Invoke(true);
                Destroy(runner.gameObject);
            });
        }

        public void VibrateShort() { }

        public void ShareApp(string title, string imageUrl)
            => Debug.Log($"[Platform:Stub] 分享: {title} {imageUrl}");

        /// <summary>延迟回调用的临时宿主(纯类不能 StartCoroutine,借一个临时 GameObject)。</summary>
        private class StubRunner : MonoBehaviour
        {
            public void Run(Action callback)
            {
                StartCoroutine(WaitThen(callback));
            }

            private IEnumerator WaitThen(Action callback)
            {
                yield return new WaitForSeconds(1f);
                callback();
            }
        }
    }
}
