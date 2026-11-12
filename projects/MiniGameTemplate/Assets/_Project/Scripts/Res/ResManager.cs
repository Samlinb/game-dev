using System;
using System.Collections;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MiniGame.Res
{
    /// <summary>
    /// 资源加载统一入口。选择策略:
    /// 编辑器 → Resources(零依赖);定义 RES_USE_BUNDLE → AssetBundleProvider(生产小游戏)。
    /// 业务只调 ResManager,以后切 Addressables(微信官方推荐)时加一个 Provider 即可,业务不动。
    /// </summary>
    public static class ResManager
    {
        /// <summary>远端 bundle 根地址(结尾不带 /),如 https://cdn.xxx.com/bundles/webgl。Boot 初始化处赋值。</summary>
        public static string RemoteBundleBaseUrl = string.Empty;

        private static IAssetProvider provider;
        private static CoroutineRunner runner;

        public static IAssetProvider Provider => provider ??= CreateProvider();

        public static T Load<T>(string path) where T : Object => Provider.Load<T>(path);

        public static void LoadAsync<T>(string path, Action<T> onDone) where T : Object
            => Provider.LoadAsync(path, onDone);

        /// <summary>加载并实例化 Prefab(不入池;复用对象用 PrefabPool.Spawn)。</summary>
        public static void InstantiatePrefab(string path, Transform parent, Action<GameObject> onDone)
        {
            LoadAsync<GameObject>(path, prefab =>
            {
                if (prefab == null)
                {
                    onDone?.Invoke(null);
                    return;
                }
                onDone?.Invoke(Object.Instantiate(prefab, parent));
            });
        }

        /// <summary>场景切换后统一释放未引用资源(配合 GC;对象池先 ClearIdle)。</summary>
        public static void UnloadUnused() => Resources.UnloadUnusedAssets();

        /// <summary>给无 MonoBehaviour 的类跑协程的内部入口(Provider 用)。</summary>
        public static void RunCoroutine(IEnumerator routine)
        {
            if (runner == null)
            {
                var go = new GameObject("ResRunner");
                Object.DontDestroyOnLoad(go);
                runner = go.AddComponent<CoroutineRunner>();
            }
            runner.StartCoroutine(routine);
        }

        private static IAssetProvider CreateProvider()
        {
#if UNITY_EDITOR
            return new ResourcesAssetProvider(); // 编辑器恒走 Resources 保零依赖;联调 bundle 临时改这里
#elif RES_USE_BUNDLE
            return new AssetBundleProvider(RemoteBundleBaseUrl);
#else
            Debug.LogWarning("[ResManager] 未定义 RES_USE_BUNDLE,走 Resources 兜底。Resources 全进首包,小游戏生产请定义 RES_USE_BUNDLE + 打包 AssetBundle(菜单 MiniGame/资源)。");
            return new ResourcesAssetProvider();
#endif
        }

        private class CoroutineRunner : MonoBehaviour { }
    }
}
