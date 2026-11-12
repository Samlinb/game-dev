using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MiniGame.Res
{
    /// <summary>
    /// 池化对象清理契约:OnDespawn 里自己退订事件/停协程/重置状态,OnSpawn 里初始化。
    /// 清理责任在对象自己,池只管存取——这是对象池复用不出 bug 的关键约定。
    /// </summary>
    public interface IPoolable
    {
        void OnSpawn();
        void OnDespawn();
    }

    /// <summary>
    /// Prefab 对象池:key = 资源路径。Spawn/Despawn 成对使用;闲置实例挂在 PoolRoot(DontDestroyOnLoad)下跨场景保留。
    /// bundle 环境先 PrewarmAsync(异步加载 bundle 后入池),再同步 Spawn。
    /// </summary>
    public class PrefabPool
    {
        private static PrefabPool instance;
        public static PrefabPool Instance => instance ??= new PrefabPool();

        private readonly Transform root;
        private readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();
        private readonly Dictionary<string, Stack<GameObject>> idle = new Dictionary<string, Stack<GameObject>>();
        private readonly Dictionary<GameObject, string> owner = new Dictionary<GameObject, string>();

        private PrefabPool()
        {
            var go = new GameObject("PoolRoot");
            Object.DontDestroyOnLoad(go);
            root = go.transform;
        }

        public GameObject Spawn(string path, Vector3 pos, Quaternion rot, Transform parent = null)
        {
            if (!prefabs.TryGetValue(path, out var prefab) || prefab == null)
            {
                prefab = ResManager.Load<GameObject>(path); // 走 bundle 时要求已预热,失败信息见 Provider
                if (prefab == null)
                {
                    Debug.LogError($"[PrefabPool] 加载失败: {path}");
                    return null;
                }
                prefabs[path] = prefab;
            }

            GameObject go = null;
            if (idle.TryGetValue(path, out var stack))
            {
                while (stack.Count > 0 && go == null)
                {
                    var candidate = stack.Pop();
                    if (candidate != null) go = candidate; // 剔除已随场景销毁的空引用
                }
            }

            if (go == null) go = Object.Instantiate(prefab);
            owner[go] = path;

            go.transform.SetParent(parent != null ? parent : root, false);
            go.transform.SetPositionAndRotation(pos, rot);
            go.SetActive(true);
            go.GetComponent<IPoolable>()?.OnSpawn();
            return go;
        }

        public void Despawn(GameObject go)
        {
            if (go == null) return;

            if (!owner.TryGetValue(go, out var path))
            {
                Debug.LogWarning($"[PrefabPool] 非池内对象,直接销毁: {go.name}");
                Object.Destroy(go);
                return;
            }

            go.GetComponent<IPoolable>()?.OnDespawn(); // 清理责任在对象自己(退订/停协程/重置)
            go.SetActive(false);
            go.transform.SetParent(root, false);

            if (!idle.TryGetValue(path, out var stack))
            {
                stack = new Stack<GameObject>();
                idle[path] = stack;
            }
            stack.Push(go);
        }

        /// <summary>同步预热:提前实例化 count 个进池(需 prefab 可同步加载,即 Resources 或已加载的 bundle)。</summary>
        public void Prewarm(string path, int count)
        {
            if (!prefabs.TryGetValue(path, out var prefab) || prefab == null)
            {
                prefab = ResManager.Load<GameObject>(path);
                if (prefab == null)
                {
                    Debug.LogError($"[PrefabPool] 预热失败: {path}");
                    return;
                }
                prefabs[path] = prefab;
            }

            for (var i = 0; i < count; i++)
            {
                var go = Object.Instantiate(prefab, root);
                go.SetActive(false);
                owner[go] = path;
                PushIdle(path, go);
            }
        }

        /// <summary>异步预热(bundle 需先异步下载):加载完成后入池,回调通知。</summary>
        public void PrewarmAsync(string path, int count, Action onDone = null)
        {
            ResManager.LoadAsync<GameObject>(path, prefab =>
            {
                if (prefab == null)
                {
                    onDone?.Invoke();
                    return;
                }
                prefabs[path] = prefab;
                for (var i = 0; i < count; i++)
                {
                    var go = Object.Instantiate(prefab, root);
                    go.SetActive(false);
                    owner[go] = path;
                    PushIdle(path, go);
                }
                onDone?.Invoke();
            });
        }

        /// <summary>清空闲置实例(bundle 卸载前调用,防空引用);path 为 null 清全部。</summary>
        public void ClearIdle(string path = null)
        {
            foreach (var kv in idle)
            {
                if (path != null && kv.Key != path) continue;
                foreach (var go in kv.Value)
                {
                    if (go == null) continue;
                    owner.Remove(go);
                    Object.Destroy(go);
                }
            }
            if (path == null) idle.Clear();
            else idle[path] = new Stack<GameObject>();
        }

        private void PushIdle(string path, GameObject go)
        {
            if (!idle.TryGetValue(path, out var stack))
            {
                stack = new Stack<GameObject>();
                idle[path] = stack;
            }
            stack.Push(go);
        }
    }
}
