using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using Object = UnityEngine.Object;

namespace MiniGame.Res
{
    /// <summary>
    /// AssetBundle 实现:bundle 名 = 资源目录名(小写下划线,由 AssetBundleBuilder 生成)。
    /// 优先远端(ResManager.RemoteBundleBaseUrl),未配置则读 StreamingAssets(首包内置/本地验证)。
    /// WebGL 下 UnityWebRequest 由微信/抖音转换 SDK 适配网络层;
    /// 官方推荐 WXAssetBundle 直读可进一步省内存,接 SDK 后替换 GetBundleRequest 即可(TODO 标注)。
    /// 文档:https://developers.weixin.qq.com/minigame/dev/guide/game-engine/unity-webgl-transform/Design/UsingAssetBundle.html
    /// </summary>
    public class AssetBundleProvider : IAssetProvider
    {
        private readonly string remoteBase;
        private readonly Dictionary<string, AssetBundle> loaded = new Dictionary<string, AssetBundle>();

        public AssetBundleProvider(string remoteBase)
        {
            this.remoteBase = string.IsNullOrEmpty(remoteBase) ? null : remoteBase.TrimEnd('/');
        }

        public string Name => "AssetBundle";

        public T Load<T>(string path) where T : Object
        {
            var (bundleName, assetName) = Split(path);
            if (!loaded.TryGetValue(bundleName, out var bundle) || bundle == null)
            {
                Debug.LogError($"[Res:AB] 同步加载要求 bundle 已异步加载过(先 PrewarmAsync/LoadAsync)。path={path}");
                return null;
            }
            return bundle.LoadAsset<T>(assetName);
        }

        public void LoadAsync<T>(string path, Action<T> onDone) where T : Object
        {
            ResManager.RunCoroutine(LoadAsyncRoutine(path, onDone));
        }

        private IEnumerator LoadAsyncRoutine<T>(string path, Action<T> onDone) where T : Object
        {
            var (bundleName, assetName) = Split(path);

            if (!loaded.TryGetValue(bundleName, out var bundle) || bundle == null)
            {
                var url = GetBundleUrl(bundleName);
                using var request = UnityWebRequestAssetBundle.GetAssetBundle(url);
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[Res:AB] bundle 下载失败 {url}: {request.error}");
                    onDone?.Invoke(null);
                    yield break;
                }

                bundle = DownloadHandlerAssetBundle.GetContent(request);
                if (bundle == null)
                {
                    Debug.LogError($"[Res:AB] bundle 内容无效 {url}");
                    onDone?.Invoke(null);
                    yield break;
                }
                loaded[bundleName] = bundle;
            }

            var assetRequest = bundle.LoadAssetAsync<T>(assetName);
            yield return assetRequest;
            onDone?.Invoke(assetRequest.asset as T);
        }

        public bool Release(string path)
        {
            var (bundleName, _) = Split(path);
            if (!loaded.TryGetValue(bundleName, out var bundle) || bundle == null) return false;

            // false:已实例化对象保留资源引用;小游戏内存紧张时改 true(实例必须已全部销毁/归还池)
            bundle.Unload(false);
            loaded.Remove(bundleName);
            return true;
        }

        private string GetBundleUrl(string bundleName)
        {
            if (remoteBase != null) return $"{remoteBase}/{bundleName}";
            return $"{Application.streamingAssetsPath}/{bundleName}";
        }

        /// <summary>"Prefabs/UI/Toast" → bundle "prefabs_ui" + 资源名 "Toast"(与 AssetBundleBuilder 的目录规则对应,资源别放根目录)。</summary>
        private static (string bundleName, string assetName) Split(string path)
        {
            var idx = path.LastIndexOf('/');
            var dir = idx >= 0 ? path.Substring(0, idx) : string.Empty;
            var name = idx >= 0 ? path.Substring(idx + 1) : path;
            var bundleName = dir.ToLower().Replace('/', '_');
            if (string.IsNullOrEmpty(bundleName)) bundleName = "root";
            return (bundleName, name);
        }
    }
}
