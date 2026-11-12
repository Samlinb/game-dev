using UnityEngine;

namespace MiniGame.Platform
{
    /// <summary>
    /// 运行时按编译符号选平台实现。编辑器下恒为 Stub,不装 SDK 也能跑通全部逻辑。
    /// 出微信包用 WECHAT_SDK,出抖音包换 DOUYIN_SDK(两者不同时定义;若都定义了,微信优先并告警)。
    /// </summary>
    public static class PlatformService
    {
        private static IPlatformService instance;

        public static IPlatformService Instance => instance ??= Create();

        private static IPlatformService Create()
        {
#if UNITY_EDITOR
            return new EditorStubPlatformService();
#elif WECHAT_SDK && DOUYIN_SDK
            Debug.LogWarning("[PlatformService] WECHAT_SDK 与 DOUYIN_SDK 同时定义了,一个构建目标只该定义一个。本次按微信处理。");
            return new WeChatPlatformService();
#elif WECHAT_SDK
            return new WeChatPlatformService();
#elif DOUYIN_SDK
            return new DouyinPlatformService();
#else
            Debug.LogWarning("[PlatformService] 未定义平台 SDK 符号,用 Stub 兜底。真机出包请检查 PlayerSettings 的 Scripting Define Symbols。");
            return new EditorStubPlatformService();
#endif
        }
    }
}
