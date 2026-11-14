# AssetBundle 按需加载与内存优化

适用：团结引擎 / Unity 2022.3 WebGL 微信+抖音小游戏，资源远程加载选 AssetBundle 路线（模板 ResManager 默认）。
触发：AB 打包参数/远程下载、接 WXAssetBundle 省 iOS 内存、AB 内存优化、避免 WWW 缓存内存坑。
核对日期：2026-11-14（微信适配文档 Design/UsingAssetBundle.md，经镜像 BlackTom900131/minigame-unity-webgl-transform gh api 取得；原页待浏览器复核）。
前置：Addressables 路线见 [Addressables 按需加载与 WXAssetBundle](./Addressables按需加载与WXAssetBundle.md)；缓存规则见 [小游戏启动优化](./小游戏启动优化.md) §4.4；模板 ResManager 见 [Unity 小游戏项目架构](<./Unity 小游戏项目架构.md>)。

## 结论速览

1. **小游戏不支持 AB 本地加载**（`LoadFromFile` 不可用），只能远程异步下载；底层自动缓存（业务按「无缓存」写，见启动优化 §4.4）。
2. **切忌 `WWW.LoadFromCacheOrDownload` / WWW 带 cache 接口**：WebGL 用 JS 模拟文件系统，会额外存一份 UnityCache 内存（实测加载后内存增加整个 bundle 大小）。
3. **内存敏感的重度游戏用 WXAssetBundle**：桥接微信文件系统省内存，但读磁盘有性能代价、默认 5 秒未用清出内存，**只推荐内存要求高的重度游戏**；仅微信环境（多版本导出加编译宏）。
4. **尽早 `ab.Unload(false)`** + 自维护 Asset 引用计数；打包用 LZ4 + AppendHash + DisableWriteTypeTree。

## 1. AB 打包参数

```csharp
BuildPipeline.BuildAssetBundles(dst,
    BuildAssetBundleOptions.AppendHashToAssetBundleName   // 带 hash，缓存淘汰依据（启动优化 §4.4）
    | BuildAssetBundleOptions.ChunkBasedCompression        // LZ4，加载速度/包体均衡
    | BuildAssetBundleOptions.DisableWriteTypeTree,        // 非新老引擎兼容时：提速降内存
    BuildTarget.WebGL);
```

- **AppendHashToAssetBundleName**（重要）：bundle 带 hash，底层缓存及淘汰以 hash 为依据。
- **ChunkBasedCompression**（LZ4）：加载速度与包体更均衡（模板默认）。
- **DisableWriteTypeTree**：无需新老 Unity 版本兼容时开启，提升加载速度、降低内存。

## 2. AB 远程下载

**小游戏不支持 AB 本地加载**，两种远程方式：

```csharp
// 方式一：UnityWebRequestAssetBundle
var req = UnityWebRequestAssetBundle.GetAssetBundle(url);
yield return req.SendWebRequest();
if (!req.isHttpError) {
    var ab = (req.downloadHandler as DownloadHandlerAssetBundle).assetBundle;
    // ab.LoadAsset...
    ab.Unload(false);
}
req.Dispose();
```

方式二：`UnityWebRequest` + `DownloadHandlerAssetBundle`（同理）。

> **切忌 `WWW.LoadFromCacheOrDownload` 或 WWW 带 cache 接口**——WebGL 下用 JS 模拟文件系统带来额外内存（§4 实测）。

## 3. WXAssetBundle 省内存（重度游戏）

**原理**：Unity `AssetBundle.LoadFromFile()` 原只存包头/查找表（按 fd 读对象）省内存；但 **WebGL 文件系统在 JS 内存维护**（[emscripten](https://emscripten.org/docs/porting/files/file_systems_overview.html)），不写磁盘，LoadFromFile 无法省内存。微信把文件系统接口桥接到微信缓存目录，提供 WXAssetBundle 包装。

**API**（仅微信小游戏环境，异步）：

```csharp
using WeChatWasm;
var req = WXAssetBundle.GetAssetBundle(url);              // 替代 UnityWebRequestAssetBundle
yield return req.SendWebRequest();
if (!req.isHttpError) {
    var bundle = (req.downloadHandler as DownloadHandlerWXAssetBundle).assetBundle;
    // bundle.LoadAsset...
    bundle.WXUnload(false);   // 仍是 AssetBundle 类型，但须调扩展方法 WXUnload 才真正卸载
}
```

**注意**：
- **仅微信小游戏环境**；需同时导出 WebGL 版本的，加编译宏控制。
- JS 内存仍维护完整 bundle 缓存，用到已清理的 bundle 才一次性从磁盘读入；**默认 5 秒未使用的 bundle 从内存清理**。
- 读磁盘影响性能，**只推荐内存要求高的重度游戏**。
- Addressables 项目改用 WXAssetBundleProvider，见 [Addressables 条目](./Addressables按需加载与WXAssetBundle.md) §2。

## 4. AB 内存优化

**多次内存分配**：浏览器 HTTP 对象 → 拷贝到 WASM 临时内存 → Unity ab 内存文件（略大于 ab 本身，相对 APP 的 LoadFromFile 这是 WebGL 额外开销）。HTTP 对象 dispose 后释放临时内存；`ab.Unload` 释放 Unity ab 内存文件。

**最佳实践**：尽早 `ab.Unload(false)`，自行维护 Asset 引用计数管理资源。

**WWW 缓存内存坑（实测）**：bundle 5403162 字节，`UnityWebRequestAssetBundle`/`UnityWebRequest`/`WWW` 加载前后内存无变化；但 **`WWW.LoadFromCacheOrDownload` 加载后内存增加 5403162 字节**（除存 IndexDB 外还存内存 UnityCache 双份）。bundle 多时浪费显著——**不要用已淘汰的 WWW 类**。

## 来源汇总

- 微信适配文档 Design/UsingAssetBundle.md（官方文档内容·镜像取得 BlackTom900131/minigame-unity-webgl-transform，gh api 核实 2026-11-14；原页待浏览器复核）。
- [emscripten 文件系统](https://emscripten.org/docs/porting/files/file_systems_overview.html)（WebGL 文件系统在 JS 内存）；[Unity AssetBundle API](https://docs.unity3d.com/2019.4/Documentation/ScriptReference/AssetBundle.html)。
- [Unity WebGL 内存优化](https://blog.unity.com/technology/understanding-memory-in-unity-webgl)（外部参考）。

## 相关条目

- [Addressables 按需加载与 WXAssetBundle](./Addressables按需加载与WXAssetBundle.md) — Addressables 路线 + WXAssetBundleProvider
- [Unity 小游戏项目架构](<./Unity 小游戏项目架构.md>) — 模板 ResManager AB 路线、AB 打包两步走、B2 网络层待验证
- [小游戏启动优化](./小游戏启动优化.md) §4.4 — FileCache 缓存规则/淘汰/hash
- [Unity 真机 Profiling 与内存分析](./Unity真机Profiling与内存分析.md) — AB 内存统计（AssetBundles/Memory Storages）
- [小游戏网络通信与安全域名](./小游戏网络通信与安全域名.md) — UnityWebRequest 远程下载