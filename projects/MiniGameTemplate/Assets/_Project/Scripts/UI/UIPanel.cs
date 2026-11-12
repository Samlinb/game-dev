using UnityEngine;

namespace MiniGame.UI
{
    /// <summary>
    /// 面板基类:所有面板 Prefab 根节点挂 UIPanel 子类,生命周期由 UIManager 驱动。
    /// OnShow/OnHide 必须成对订阅/退订事件,防止重复订阅和悬空回调。
    /// </summary>
    public abstract class UIPanel : MonoBehaviour
    {
        /// <summary>面板被显示(含从隐藏到显示)时调用。</summary>
        public virtual void OnShow() { }

        /// <summary>面板被隐藏时调用。</summary>
        public virtual void OnHide() { }

        /// <summary>面板已显示但再次 Show 时调用(数据刷新,不重复订阅)。</summary>
        public virtual void OnRefresh() { }
    }
}
