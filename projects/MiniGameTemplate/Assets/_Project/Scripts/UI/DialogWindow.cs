using System;
using UnityEngine;
using UnityEngine.UI;

namespace MiniGame.UI
{
    /// <summary>
    /// 通用确认/提示对话框(Prefab 由一键初始化生成并注册)。
    /// 用 UIManager.ShowConfirm(...) 弹出,不要直接实例化;按钮回调先执行再关窗由 Close 顺序保证。
    /// </summary>
    public class DialogWindow : UIWindow
    {
        [SerializeField] private Text titleText;
        [SerializeField] private Text bodyText;
        [SerializeField] private Button confirmButton;
        [SerializeField] private Button cancelButton;
        [SerializeField] private Text confirmText;
        [SerializeField] private Text cancelText;

        private Action onConfirm;
        private Action onCancel;

        private void Awake()
        {
            if (confirmButton != null)
                confirmButton.onClick.AddListener(OnConfirmClicked);
            if (cancelButton != null)
                cancelButton.onClick.AddListener(OnCancelClicked);
        }

        /// <summary>填充内容;cancelLabel 传空且 onCancel 为 null 时隐藏取消按钮(当纯提示框用)。</summary>
        public void Setup(string title, string body, string confirmLabel, string cancelLabel, Action onConfirm, Action onCancel)
        {
            this.onConfirm = onConfirm;
            this.onCancel = onCancel;

            if (titleText != null) titleText.text = title ?? string.Empty;
            if (bodyText != null) bodyText.text = body ?? string.Empty;
            if (confirmText != null) confirmText.text = string.IsNullOrEmpty(confirmLabel) ? "确认" : confirmLabel;
            if (cancelText != null) cancelText.text = string.IsNullOrEmpty(cancelLabel) ? "取消" : cancelLabel;

            var showCancel = !string.IsNullOrEmpty(cancelLabel) || onCancel != null;
            if (cancelButton != null) cancelButton.gameObject.SetActive(showCancel);
        }

        private void OnConfirmClicked()
        {
            var cb = onConfirm;
            Close();
            cb?.Invoke();
        }

        private void OnCancelClicked()
        {
            var cb = onCancel;
            Close();
            cb?.Invoke();
        }
    }
}
