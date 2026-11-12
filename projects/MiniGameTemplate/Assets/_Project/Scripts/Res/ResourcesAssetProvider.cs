using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MiniGame.Res
{
    /// <summary>
    /// Resources 实现:零依赖,编辑器/开发期默认。
    /// 注意:Resources 目录会全部打进首包,小游戏生产别放大资源,切 AssetBundleProvider。
    /// path 必须落在某个 Resources/ 目录内。
    /// </summary>
    public class ResourcesAssetProvider : IAssetProvider
    {
        public string Name => "Resources";

        public T Load<T>(string path) where T : Object
        {
            return Resources.Load<T>(path);
        }

        public void LoadAsync<T>(string path, Action<T> onDone) where T : Object
        {
            var request = Resources.LoadAsync<T>(path);
            if (request == null)
            {
                Debug.LogError($"[Res:Resources] 加载失败 {path}(确认资源在 Resources/ 目录内)");
                onDone?.Invoke(null);
                return;
            }

            if (request.isDone)
            {
                onDone?.Invoke(request.asset as T);
                return;
            }
            request.completed += _ => onDone?.Invoke(request.asset as T);
        }

        public bool Release(string path)
        {
            // Resources 的释放统一交给 ResManager.UnloadUnused()(场景切换时调),不按路径管理
            return false;
        }
    }
}
