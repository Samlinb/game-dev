using UnityEngine;
using UnityEngine.UI;

namespace MiniGame.UI
{
    /// <summary>
    /// 代码建 uGUI 的小工具:Loading/遮罩这类工具型 UI 用代码建(不依赖 Prefab、启动期就能用);
    /// 正式面板/弹窗仍走 Prefab(美术可改)。一键初始化也用它生成示例 Prefab。
    /// </summary>
    public static class UGUIUtil
    {
        public static Text CreateText(Transform parent, string name, string content, int fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.text = content;
            t.font = DefaultFont();
            t.fontSize = fontSize;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = Color.white;
            return t;
        }

        public static Font DefaultFont()
        {
            try
            {
                return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            catch
            {
                try
                {
                    return Resources.GetBuiltinResource<Font>("Arial.ttf");
                }
                catch
                {
                    return null; // 字体为空时 Text 会告警,按引擎文档补内置字体名
                }
            }
        }

        public static void SetAnchor(RectTransform rt, Vector2 anchor, Vector2 anchoredPos, Vector2 size)
        {
            SetAnchor(rt, anchor, new Vector2(0.5f, 0.5f), anchoredPos, size);
        }

        public static void SetAnchor(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 anchoredPos, Vector2 size)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
        }

        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
