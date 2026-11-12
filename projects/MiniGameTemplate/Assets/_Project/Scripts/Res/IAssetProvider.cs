using System;
using Object = UnityEngine.Object;

namespace MiniGame.Res
{
    /// <summary>
    /// 资源加载提供者抽象:ResManager 只认接口,具体实现按平台/构建切换。
    /// path 约定:不带扩展名,相对 Assets/_Project 的逻辑路径,如 "Prefabs/UI/Toast"。
    /// 小游戏生产环境(Resources 全进首包,WebGL 首包有限额)→ 用 AssetBundleProvider;
    /// 微信官方另有 Addressables 适配(https://developers.weixin.qq.com/minigame/dev/guide/game-engine/unity-webgl-transform/Design/UsingAddressable.html),
    /// 要切 Addressables 时实现一个 Provider 即可,业务代码不动。
    /// </summary>
    public interface IAssetProvider
    {
        string Name { get; }

        /// <summary>同步加载。AssetBundleProvider 要求 bundle 已被异步加载过,否则报错返回 null。</summary>
        T Load<T>(string path) where T : Object;

        /// <summary>异步加载;回调保证主线程,失败回调 null 并打日志,不抛异常。</summary>
        void LoadAsync<T>(string path, Action<T> onDone) where T : Object;

        /// <summary>释放该路径的引用/缓存(实现自定语义)。返回是否处理了该路径。</summary>
        bool Release(string path);
    }
}
