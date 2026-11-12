namespace MiniGame.UI
{
    /// <summary>
    /// UI 层级:各层是独立子 Canvas,按 sortingOrder 数值叠放。
    /// 数值表达「面板 &lt; 弹窗 &lt; Toast」,不靠实例化顺序(Sibling Index 排序脆弱,实例化顺序一变层级就乱)。
    /// </summary>
    public enum UILayer
    {
        Normal = 0,
        Popup = 100,
        Top = 1000,
    }
}
