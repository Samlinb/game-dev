using System.IO;
using UnityEditor;
using UnityEngine;

namespace MiniGame.EditorTools
{
    /// <summary>
    /// AssetBundle 打包流水线(配合 RES_USE_BUNDLE + AssetBundleProvider,资源远程加载文档:
    /// https://developers.weixin.qq.com/minigame/dev/guide/game-engine/unity-webgl-transform/Design/Transform.html ):
    /// 1. 按目录分配 bundle 名:Prefabs/Art 下每个子目录一个 bundle(目录路径 → 小写下划线,与 AssetBundleProvider.Split 对应);
    /// 2. WebGL 构建 LZ4(ChunkBasedCompression),输出到工程根 Bundles/WebGL(不进 Assets,不导入);
    /// 3. 上传 CDN → Boot 初始化处 ResManager.RemoteBundleBaseUrl 赋值 → PlayerSettings 定义 RES_USE_BUNDLE。
    /// 依赖资源(图集、贴图)没有自己的 bundle 名时,自动并入引用它的 bundle,无需手动处理。
    /// </summary>
    public static class AssetBundleBuilder
    {
        private const string SrcRoot = "Assets/_Project";
        private const string OutputDir = "Bundles/WebGL";

        [MenuItem("MiniGame/资源/1.按目录分配 Bundle 名")]
        public static void AssignBundleNames()
        {
            AssignUnder($"{SrcRoot}/Prefabs");
            AssignUnder($"{SrcRoot}/Art");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[AB] Bundle 名分配完成:每个子目录一个 bundle(目录路径小写下划线)。");
        }

        [MenuItem("MiniGame/资源/2.打包 AssetBundles(WebGL)")]
        public static void BuildBundles()
        {
            Directory.CreateDirectory(OutputDir);
            var manifest = BuildPipeline.BuildAssetBundles(
                OutputDir,
                BuildAssetBundleOptions.ChunkBasedCompression, // LZ4:小游戏加载解压快,包体与 LZMA 差距可接受
                BuildTarget.WebGL);

            if (manifest == null)
            {
                Debug.LogError("[AB] 打包失败,看上方报错。");
                return;
            }

            var bundles = manifest.GetAllAssetBundles();
            Debug.Log($"[AB] 完成 → {Path.GetFullPath(OutputDir)},共 {bundles.Length} 个 bundle。");
            Debug.Log("[AB] 下一步:①上传 CDN;②Boot 初始化处 ResManager.RemoteBundleBaseUrl = \"https://你的CDN/bundles/webgl\";③PlayerSettings 定义 RES_USE_BUNDLE。");
        }

        private static void AssignUnder(string dir)
        {
            if (!Directory.Exists(dir)) return;

            var rootRel = dir.Substring(SrcRoot.Length + 1).Replace('\\', '/'); // 如 "Prefabs"
            var rootBundle = rootRel.ToLower().Replace('/', '_');

            // 根目录直属文件 → 目录自身的 bundle(约定:资源放子目录,根目录尽量别放散文件)
            foreach (var file in Directory.GetFiles(dir))
            {
                if (file.EndsWith(".meta")) continue;
                Assign(file.Replace('\\', '/'), rootBundle);
            }

            // 每个子目录一个 bundle
            foreach (var sub in Directory.GetDirectories(dir, "*", SearchOption.AllDirectories))
            {
                var rel = sub.Substring(SrcRoot.Length + 1).Replace('\\', '/');
                var bundleName = rel.ToLower().Replace('/', '_');
                foreach (var file in Directory.GetFiles(sub))
                {
                    if (file.EndsWith(".meta")) continue;
                    Assign(file.Replace('\\', '/'), bundleName);
                }
            }
        }

        private static void Assign(string assetPath, string bundleName)
        {
            var importer = AssetImporter.GetAtPath(assetPath);
            if (importer == null || importer.assetBundleName == bundleName) return;
            importer.assetBundleName = bundleName;
            Debug.Log($"[AB] {assetPath} → {bundleName}");
        }
    }
}
