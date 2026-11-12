using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace MiniGame.UI
{
    /// <summary>
    /// 轻提示:淡入 → 停留 → 淡出自毁(Prefab 由一键初始化生成并注册)。
    /// 经 UIManager.ShowToast(message) 使用,不要直接实例化。
    /// </summary>
    public class Toast : MonoBehaviour
    {
        [SerializeField] private Text messageText;
        [SerializeField] private CanvasGroup group;

        private const float FadeIn = 0.2f;
        private const float FadeOut = 0.3f;

        public void Play(string message, float duration)
        {
            if (messageText != null) messageText.text = message;
            StartCoroutine(FadeRoutine(duration));
        }

        private IEnumerator FadeRoutine(float duration)
        {
            if (group == null)
            {
                yield return new WaitForSecondsRealtime(duration);
                Destroy(gameObject);
                yield break;
            }

            for (var t = 0f; t < FadeIn; t += Time.unscaledDeltaTime)
            {
                group.alpha = t / FadeIn;
                yield return null;
            }
            group.alpha = 1f;

            yield return new WaitForSecondsRealtime(duration);

            for (var t = 0f; t < FadeOut; t += Time.unscaledDeltaTime)
            {
                group.alpha = 1f - t / FadeOut;
                yield return null;
            }
            Destroy(gameObject);
        }
    }
}
