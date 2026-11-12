using System;
using System.Collections.Generic;
using UnityEngine;

namespace MiniGame.UI
{
    [Serializable]
    public class PanelEntry
    {
        public string key;        // 约定:与面板类名同名,如 HomePanel;Toast/DialogWindow 等固定 key 除外
        public GameObject prefab;
    }

    /// <summary>
    /// UI 统一入口,三层结构(UILayer,由一键初始化生成子 Canvas):
    /// Normal 普通面板(单场景切屏)/ Popup 弹窗(自动遮罩)/ Top 轻提示。
    /// 面板/弹窗/Toast 全是 Prefab 并在此注册,场景里不散放 UI。
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        [Header("层级根节点(一键初始化生成并接线)")]
        [SerializeField] private RectTransform panelRoot; // Normal
        [SerializeField] private RectTransform popupRoot; // Popup
        [SerializeField] private RectTransform toastRoot; // Top

        [SerializeField] private List<PanelEntry> panels = new List<PanelEntry>();

        private readonly Dictionary<string, GameObject> loaded = new Dictionary<string, GameObject>();
        private readonly Dictionary<string, UIWindow> windows = new Dictionary<string, UIWindow>();

        private void Awake()
        {
            Instance = this; // 随 Game 场景创建销毁,不 DontDestroyOnLoad
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ---------- 普通面板(Normal 层) ----------

        /// <summary>显示面板(未加载则实例化,已加载复用),返回面板脚本;纯展示面板可以不挂 UIPanel 子类。</summary>
        public UIPanel Show(string key)
        {
            var go = GetOrInstantiate(key);
            if (go == null) return null;

            var panel = go.GetComponent<UIPanel>();
            if (go.activeSelf)
            {
                panel?.OnRefresh();
                return panel;
            }

            go.SetActive(true);
            panel?.OnShow();
            return panel;
        }

        /// <summary>只显示一个面板,其余隐藏。休闲游戏最常用的切换方式。</summary>
        public void ShowOnly(string key)
        {
            foreach (var k in new List<string>(loaded.Keys))
                if (k != key)
                    Hide(k);
            Show(key);
        }

        public void Hide(string key)
        {
            if (!loaded.TryGetValue(key, out var go) || !go.activeSelf) return;
            go.GetComponent<UIPanel>()?.OnHide();
            go.SetActive(false);
        }

        public void HideAll()
        {
            foreach (var key in new List<string>(loaded.Keys))
                Hide(key);
        }

        // ---------- 弹窗(Popup 层) ----------

        /// <summary>
        /// 弹窗:自动加全屏遮罩挡下层点击,onClosed 在窗关闭清理后回调。
        /// destroyOnClose=false 的窗(Prefab 上改)会被缓存复用。
        /// </summary>
        public UIWindow ShowWindow(string key, Action onClosed = null, object arg = null)
        {
            if (popupRoot == null)
            {
                Debug.LogError("[UIManager] popupRoot 未接线(一键初始化会生成)。");
                return null;
            }

            if (windows.TryGetValue(key, out var cached) && cached != null)
            {
                cached.Rebind(onClosed);
                cached.AttachMask(CreateMask());
                cached.gameObject.SetActive(true);
                cached.OnShow(arg);
                return cached;
            }

            var prefab = GetPrefab(key);
            if (prefab == null) return null;

            var go = Instantiate(prefab, popupRoot);
            go.name = key;
            var win = go.GetComponent<UIWindow>();
            if (win == null)
            {
                Debug.LogError($"[UIManager] 弹窗 Prefab 根没挂 UIWindow 子类: {key}");
                Destroy(go);
                return null;
            }

            win.Bind(this, key, CreateMask(), onClosed);
            windows[key] = win;
            go.SetActive(true);
            win.OnShow(arg);
            return win;
        }

        /// <summary>通用确认框(DialogWindow,一键初始化生成);cancelLabel 传空则当纯提示框。</summary>
        public void ShowConfirm(string title, string body, Action onConfirm = null,
            string confirmLabel = "确认", string cancelLabel = "取消", Action onCancel = null)
        {
            var win = ShowWindow(nameof(DialogWindow));
            if (win == null) return;
            ((DialogWindow)win).Setup(title, body, confirmLabel, cancelLabel, onConfirm, onCancel);
        }

        internal void OnWindowClosed(UIWindow win)
        {
            win.CleanupMask();
            win.gameObject.SetActive(false);
            if (win.destroyOnClose)
            {
                windows.Remove(win.name);
                Destroy(win.gameObject);
            }
            win.InvokeClosed();
        }

        // ---------- 轻提示(Top 层) ----------

        /// <summary>Toast:Top 层,自动消失自毁;没生成 Toast.prefab 时退化为日志。</summary>
        public void ShowToast(string message, float duration = 1.5f)
        {
            if (toastRoot == null || GetPrefab("Toast") == null)
            {
                Debug.Log($"[Toast] {message}");
                return;
            }

            var go = Instantiate(GetPrefab("Toast"), toastRoot);
            var toast = go.GetComponent<Toast>();
            if (toast == null)
            {
                Destroy(go, duration);
                return;
            }
            go.SetActive(true);
            toast.Play(message, duration);
        }

        // ---------- 内部 ----------

        /// <summary>弹窗遮罩:挡下层点击 + 视觉聚焦。</summary>
        private GameObject CreateMask()
        {
            var go = new GameObject("Mask", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(popupRoot, false);
            UGUIUtil.Stretch((RectTransform)go.transform);
            go.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
            return go;
        }

        private GameObject GetPrefab(string key)
        {
            var entry = panels.Find(p => p.key == key);
            if (entry == null || entry.prefab == null)
            {
                Debug.LogError($"[UIManager] 未注册 Prefab: {key}。到 Game 场景 UIRoot 的 UIManager 面板列表里加,或用一键初始化生成。");
                return null;
            }
            return entry.prefab;
        }

        private GameObject GetOrInstantiate(string key)
        {
            if (loaded.TryGetValue(key, out var go))
            {
                if (go != null) return go;
                loaded.Remove(key); // 场景重建后的悬空引用,清掉重实例化
            }

            var prefab = GetPrefab(key);
            if (prefab == null) return null;

            if (panelRoot == null)
            {
                Debug.LogError("[UIManager] panelRoot 未接线(一键初始化会生成)。");
                return null;
            }

            go = Instantiate(prefab, panelRoot);
            go.name = key; // 保持与 key 同名,便于排查
            loaded[key] = go;
            if (go.activeSelf) go.SetActive(false); // 统一由 Show 驱动显示与生命周期
            return go;
        }
    }
}
