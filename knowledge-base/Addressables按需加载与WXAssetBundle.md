# Addressables 按需加载与 WXAssetBundle

适用：团结引擎 / Unity 2022.3 WebGL 微信+抖音小游戏，资源远程加载选 Addressables 路线（架构三条远程路线之一）。
触发：用 Addressables 按需加载场景/物件、接 WXAssetBundle 省 iOS 内存、消除 bundle 冗余、异步加载写法。
核对日期：2026-11-14（微信适配文档 Design/UsingAddressable.md，经镜像 BlackTom900131/minigame-unity-webgl-transform gh api 取得；原页待浏览器复核）。
前置：首包精简/预下载/缓存见 [小游戏启动优化](./小游戏启动优化.md)；模板 ResManager 三远程路线见 [Unity 小游戏项目架构](<./Unity 小游戏项目架构.md>)；本条目讲 Addressables 的具体用法。

## 结论速览

1. **物件引用用 `AssetReference` 而非 `GameObject`**：直接 `public GameObject` + `Instantiate` 会强制该 Prefab 及依赖在场景加载前全部就绪，极大拖慢初始化；改 `AssetReference.InstantiateAsync()` 才真正按需下载。
2. **iOS 内存压力用 WXAssetBundleProvider**：Addressables 默认 Provider 走 UnityWebRequest，替换为 WXAssetBundleProvider 直读微信文件缓存省内存（步骤见 §2）。
3. **多场景共享资源会冗余**：用 Addressables Analyze 的「Check Duplicate Bundle Dependencies」→「Fixed Selected Rules」自动把冗余项设为 Addressable。
4. **首包≤5M（zip 后 3M 最佳）/Resources 全进首包/字体不打包**等首包原则主存启动优化，本条目不重复。

## 1. 何时用 Addressables

Addressables 与 AssetBundle 在小游戏底层都走 XHR 远程访问 + 微信文件缓存。相比 AB 需手动标识/组织/Load/Unload/维护依赖，Addressables 自动管理依赖与引用计数，资源移动/改名仍能准确打包，本地→服务器几乎零代价。**已有 AB 项目迁移成本低**（Addressables Groups 提供 AB→Addressable 迁移）。模板默认 AB 路线（ResManager），Addressables 是可选远程路线之一。

## 2. 接入 WXAssetBundleProvider（省 iOS 内存）

来源：UsingAddressable.md §2.4（镜像核实 2026-11-14）；WXAssetBundle 原理见 [AssetBundle 按需加载与内存优化](./AssetBundle按需加载与内存优化.md) §3，团结兼容见 [Unity 小游戏项目架构](<./Unity 小游戏项目架构.md>)「待验证 B2」。

1. 下载 `WXAssetBundleProvider.cs`（原仓库 tools/ 下，仓库已封待复核）放到 `WX-WASM-SDK-V2/Runtime/`。
2. 导入后报缺依赖 → 给 `WX-WASM-SDK-V2/Runtime` 的 asmdef 增加 `Unity.ResourceManager` 引用。
3. Addressables 组设置里把 Provider 改为 WXAssetBundleProvider。
4. 重新导出 AA 包 + 小游戏。

> 团结引擎兼容性未实测（B2/B7 类待验证）；WXAssetBundle 直读微信文件缓存，避开 UnityWebRequest 的 iOS 内存膨胀。

## 3. 按需加载模式

### 3.1 场景动态加载

构建只留 splash/loading 首场景，主场景（大厅/战斗）**每个场景单独 Addressable 分组**，用到时才下载：

```csharp
var handle = Addressables.LoadSceneAsync("Assets/.../main.unity", LoadSceneMode.Single, true);
handle.Completed += obj => Debug.Log($"scene loaded {obj.Status}");
while (!handle.IsDone) { /* handle.PercentComplete 展示进度 */ yield return null; }
```

### 3.2 物件动态加载（AssetReference 坑）

**反例**（拖慢初始化）：

```csharp
public GameObject somePrefab;          // 直接引用
void Start() { Instantiate(somePrefab); }  // 资源被迫在场景加载前就绪
```

**正解**（按需下载）：

```csharp
public AssetReference somePrefab;      // 改 AssetReference
IEnumerator Spawn() {
    var handle = somePrefab.InstantiateAsync();
    while (!handle.IsDone) yield return null;
    var go = handle.Result;            // 加载完成回调
}
```

Prefab 在 Editor 设为 Addressable 并重新给 somePrefab 赋值。

## 4. 消除多场景冗余

仅把场景设分组时，场景内静态物件会一并打进场景 bundle；两个场景用同一资源就**产生冗余**。消除：Addressables 面板 **Tools → Analyze → Check Duplicate Bundle Dependencies → Fixed Selected Rules**，工具自动把冗余项逐个设为 Addressable（独立 bundle 共享）。

## 5. 异步加载三式 + 释放

```csharp
// 回调
var h = Addressables.LoadAssetAsync<Texture2D>("mytexture");
h.Completed += op => { if (op.Status == AsyncOperationStatus.Succeeded) Use(op.Result); };

// 协程（IsDone 检查可省一帧等待）
var h2 = Addressables.LoadAssetAsync<Texture2D>("mytexture");
if (!h2.IsDone) yield return h2;
if (h2.Status == AsyncOperationStatus.Succeeded) { Use(h2.Result); Addressables.Release(h2); }  // 用完释放

// await
var h3 = Addressables.LoadAssetAsync<Texture2D>("mytexture");
await h3.Task;   // 用前检查 Status
```

**引用计数**：Addressables 自动引用计数，用完 `Addressables.Release(handle)` 释放（内存管理主存 [Unity 真机 Profiling 与内存分析](./Unity真机Profiling与内存分析.md)）。

## 6. 首包与部署要点（详见启动优化）

- 首包只含首屏（Splash+文案）；导出场景不勾其他场景；**不打包字体**（压缩率低，走 WXFont 见启动优化 §2.1）；**Resources 目录无条件进首包**（改造：设 Addressable，Unity 自动移 Resources_Moved，加载改 `Addressables.LoadAssetAsync`）。
- 首资源包 zip 后 3M 最佳、≤5M；CDN 部署对 .txt 开 Brotli/gzip；小游戏分包底层自动压缩。
- AA 编译输出 `Library/com.unity.addressables/`，转小游戏时自动拷贝 Bundle，StreamingAssets 传 CDN；预下载见启动优化 §五（UsingPreload）。

## 来源汇总

- 微信适配文档 Design/UsingAddressable.md（官方文档内容·镜像取得 BlackTom900131/minigame-unity-webgl-transform，gh api 核实 2026-11-14；原页待浏览器复核）。
- [Unity Addressables 官方样例](https://github.com/Unity-Technologies/Addressables-Sample)（官方仓库）；[Addressables 手册](https://docs.unity3d.com/Packages/com.unity.addressables@1.1/manual/index.html)。
- WXAssetBundleProvider.cs / 官方 Demo（wechat-miniprogram 仓库已封，待复核）。
- [UWA Addressables 介绍](https://blog.uwa4d.com/archives/USparkle_Addressable4.html)（外部案例）。

## 相关条目

- [Unity 小游戏项目架构](<./Unity 小游戏项目架构.md>) — ResManager 三远程路线（Addressables/AB+WXAssetBundle/AutoStreaming）、B2 待验证
- [AssetBundle 按需加载与内存优化](./AssetBundle按需加载与内存优化.md) — AB 路线 + WXAssetBundle 原理/API（本条目 §2 Provider 共用）
- [小游戏启动优化](./小游戏启动优化.md) — 首包精简、WXFont、预下载、FileCache 缓存
- [Unity 真机 Profiling 与内存分析](./Unity真机Profiling与内存分析.md) — AB/Addressables 内存统计与释放
- [外部库选型评估](./外部库选型评估.md) — Addressables 包版本/依赖核对