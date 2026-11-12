using System;
using System.Collections;
using MiniGame.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MiniGame.Core
{
    /// <summary>
    /// 统一 Loading:自带 Overlay Canvas(排序最高),UI 代码构建、不依赖任何 Prefab,Boot 阶段即可用。
    /// 场景加载显示真实进度;任意异步流程用 Run(循环条);多步流程可 Show/SetProgress 手动驱动。
    /// </summary>
    public class LoadingManager : MonoBehaviour
    {
        public static LoadingManager Instance { get; private set; }

        private const float BarMin = 0.02f; // 0% 观感太秃,起点给一点

        private GameObject panel;
        private Image fillBar;
        private Text tipText;
        private Text percentText;
        private Coroutine indeterminateLoop;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            BuildUI();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>加载场景:0~90% 反映加载,90% 后激活场景到 100%(allowSceneActivation 细节见知识库)。</summary>
        public void LoadScene(string sceneName, string tip = null, Action onDone = null)
        {
            StartCoroutine(LoadSceneRoutine(sceneName, tip, onDone));
        }

        /// <summary>包装任意异步流程(协程):未知时长显示循环条。</summary>
        public void Run(IEnumerator op, string tip = null, Action onDone = null)
        {
            Show(tip);
            SetIndeterminate(true);
            StartCoroutine(WrapRoutine(op, onDone));
        }

        /// <summary>手动模式:自己控制显示与进度(多步下载逐段汇报时用)。</summary>
        public void Show(string tip = null)
        {
            panel.SetActive(true);
            SetTip(tip);
            SetIndeterminate(false);
            SetProgress(BarMin);
        }

        public void Hide()
        {
            if (panel != null) panel.SetActive(false);
            SetIndeterminate(false);
        }

        public void SetProgress(float v01)
        {
            SetIndeterminate(false);
            var v = Mathf.Clamp01(v01);
            fillBar.fillAmount = v;
            percentText.text = $"{Mathf.RoundToInt(v * 100)}%";
        }

        public void SetTip(string tip)
        {
            var has = !string.IsNullOrEmpty(tip);
            tipText.gameObject.SetActive(has);
            if (has) tipText.text = tip;
        }

        // ---------- 内部 ----------

        private IEnumerator LoadSceneRoutine(string sceneName, string tip, Action onDone)
        {
            Show(tip);
            var op = SceneManager.LoadSceneAsync(sceneName);
            if (op == null)
            {
                Debug.LogError($"[Loading] 场景不在 Build Settings: {sceneName}");
                Hide();
                yield break;
            }

            op.allowSceneActivation = false; // Unity 异步加载到 0.9 停住等激活许可;接管后进度走满再放行
            while (op.progress < 0.9f)
            {
                SetProgress(op.progress / 0.9f * 0.9f);
                yield return null;
            }
            SetProgress(0.9f);

            op.allowSceneActivation = true;
            while (!op.isDone) yield return null;
            SetProgress(1f);

            yield return new WaitForSecondsRealtime(0.2f); // 让 100% 被看到
            Hide();
            onDone?.Invoke();
        }

        private IEnumerator WrapRoutine(IEnumerator op, Action onDone)
        {
            yield return op;
            Hide();
            onDone?.Invoke();
        }

        private void SetIndeterminate(bool on)
        {
            if (on && indeterminateLoop == null && isActiveAndEnabled)
            {
                indeterminateLoop = StartCoroutine(IndeterminateRoutine());
            }
            else if (!on && indeterminateLoop != null)
            {
                StopCoroutine(indeterminateLoop);
                indeterminateLoop = null;
            }
        }

        private IEnumerator IndeterminateRoutine()
        {
            float t = 0f;
            while (true)
            {
                t += Time.unscaledDeltaTime * 0.9f;
                fillBar.fillAmount = Mathf.Clamp01(Mathf.PingPong(t, 1.2f));
                yield return null;
            }
        }

        /// <summary>工具型 UI 代码构建(不走 Prefab):启动期就要用,不能依赖资源加载本身。</summary>
        private void BuildUI()
        {
            var canvasGo = new GameObject("LoadingCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 2000; // 盖过所有 UI 层
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);

            panel = new GameObject("Panel", typeof(RectTransform));
            panel.transform.SetParent(canvasGo.transform, false);
            UGUIUtil.Stretch((RectTransform)panel.transform);
            panel.AddComponent<Image>().color = new Color(0.06f, 0.08f, 0.13f, 0.97f);

            var title = UGUIUtil.CreateText(panel.transform, "Title", "加载中...", 60);
            UGUIUtil.SetAnchor(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 140f), new Vector2(800f, 110f));

            tipText = UGUIUtil.CreateText(panel.transform, "Tip", "", 40);
            tipText.color = new Color(1f, 1f, 1f, 0.75f);
            UGUIUtil.SetAnchor(tipText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -80f), new Vector2(800f, 80f));

            var barBg = new GameObject("BarBg", typeof(RectTransform), typeof(Image));
            barBg.transform.SetParent(panel.transform, false);
            barBg.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.15f);
            UGUIUtil.SetAnchor((RectTransform)barBg.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, -20f), new Vector2(760f, 40f));

            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(barBg.transform, false);
            fillBar = fillGo.GetComponent<Image>();
            fillBar.type = Image.Type.Filled;
            fillBar.fillMethod = Image.FillMethod.Horizontal;
            fillBar.color = new Color(0.25f, 0.6f, 1f);
            var fillRt = (RectTransform)fillGo.transform;
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = new Vector2(5f, 5f);
            fillRt.offsetMax = new Vector2(-5f, -5f);

            percentText = UGUIUtil.CreateText(panel.transform, "Percent", "0%", 36);
            percentText.color = new Color(1f, 1f, 1f, 0.85f);
            UGUIUtil.SetAnchor(percentText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -90f), new Vector2(400f, 70f));

            panel.SetActive(false);
        }
    }
}
