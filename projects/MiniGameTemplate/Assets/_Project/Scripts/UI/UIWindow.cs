using System;
using UnityEngine;

namespace MiniGame.UI
{
    /// <summary>
    /// 弹窗基类:遮罩与关闭回调由 UIManager 托管,业务只调 Close()。
    /// destroyOnClose=false 的窗关闭后被缓存,下次 ShowWindow 直接复用。
    /// OnHide 里退订事件/停协程,与 OnShow 成对。
    /// </summary>
    public abstract class UIWindow : MonoBehaviour
    {
        [Tooltip("关闭即销毁;false 则缓存复用")]
        public bool destroyOnClose = true;

        private UIManager owner;
        private GameObject mask;
        private Action onClosed;

        /// <summary>窗口显示时调用;arg 为 ShowWindow 传入的参数。</summary>
        public virtual void OnShow(object arg) { }

        /// <summary>窗口关闭时调用。</summary>
        public virtual void OnHide() { }

        /// <summary>业务主动关闭(按钮回调里调);onClosed 回调由 UIManager 在清理完成后触发。</summary>
        public void Close()
        {
            OnHide();
            if (owner != null) owner.OnWindowClosed(this);
        }

        internal void Bind(UIManager owner, string key, GameObject mask, Action onClosed)
        {
            this.owner = owner;
            name = key;
            this.mask = mask;
            this.onClosed = onClosed;
        }

        internal void Rebind(Action onClosed) => this.onClosed = onClosed;

        internal void AttachMask(GameObject mask) => this.mask = mask;

        internal void CleanupMask()
        {
            if (mask != null) Destroy(mask);
            mask = null;
        }

        internal void InvokeClosed()
        {
            var cb = onClosed;
            onClosed = null;
            cb?.Invoke();
        }
    }
}
