using System.IO;
using MiniGame.Ads;
using MiniGame.Core;
using MiniGame.Data;
using MiniGame.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MiniGame.EditorTools
{
    /// <summary>
    /// 一键把模板工程初始化成可运行状态:配置资产 → Boot/Game 场景(含三层 UI/Loading)→ PlayerSettings。
    /// 菜单:MiniGame → 一键初始化。分项菜单在只想重建某部分时用。
    /// </summary>
    public static class ProjectBootstrap
    {
        private const string DataDir = "Assets/_Project/Data";
        private const string ScenesDir = "Assets/_Project/Scenes";
        private const string UiPrefabDir = "Assets/_Project/Prefabs/UI";

        [MenuItem("MiniGame/一键初始化")]
        public static void BootstrapAll()
        {
            CreateConfigAssets();
            CreateScenes();
            ApplyMiniGamePlayerSettings();
            AssetDatabase.Refresh();

            var bootPath = $"{ScenesDir}/Boot.unity";
            if (File.Exists(bootPath))
                EditorSceneManager.OpenScene(bootPath);

            Debug.Log("[ProjectBootstrap] 完成。Boot 场景已打开,按 Play 即可跑通主流程(广告为 Stub 模拟,Loading 真实进度)。");
        }

        // ---------- 1. 配置资产 ----------

        [MenuItem("MiniGame/1.创建配置资产")]
        public static void CreateConfigAssets()
        {
            Directory.CreateDirectory(DataDir);

            CreateAsset<GameConfig>($"{DataDir}/GameConfig.asset");
            CreateAsset<PlayerConfig>($"{DataDir}/PlayerConfig.asset");
            CreateAsset<AudioConfig>($"{DataDir}/AudioConfig.asset");

            var adConfig = CreateAsset<AdConfig>($"{DataDir}/AdConfig.asset");
            if (adConfig != null && adConfig.placements.Count == 0)
            {
                // 预置一个示例场景位;广告位 ID 上线前在 Inspector 里填
                var so = new SerializedObject(adConfig);
                var arr = so.FindProperty("placements");
                arr.arraySize = 1;
                arr.GetArrayElementAtIndex(0).FindPropertyRelative("placement").stringValue = "double_coins";
                so.ApplyModifiedProperties();
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[ProjectBootstrap] 配置资产就绪(已存在的不覆盖)。");
        }

        private static T CreateAsset<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        // ---------- 2. 场景 ----------

        [MenuItem("MiniGame/2.创建场景(Boot+Game)")]
        public static void CreateScenes()
        {
            Directory.CreateDirectory(ScenesDir);
            Directory.CreateDirectory(UiPrefabDir);
            CreateBootScene();
            CreateGameScene();
        }

        private static void CreateBootScene()
        {
            var path = $"{ScenesDir}/Boot.unity";
            if (File.Exists(path) && !ConfirmOverwrite(path)) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var managers = new GameObject("Managers");

            // GameManager:状态机 + 目标帧率
            var gm = managers.AddComponent<GameManager>();
            SetField(gm, "config", LoadAsset<GameConfig>($"{DataDir}/GameConfig.asset"));

            // AudioManager:Music 常驻循环 / SFX OneShot 两个音源
            var am = managers.AddComponent<AudioManager>();
            var musicGo = new GameObject("MusicSource");
            musicGo.transform.SetParent(managers.transform, false);
            var musicSrc = musicGo.AddComponent<AudioSource>();
            musicSrc.playOnAwake = false;
            var sfxGo = new GameObject("SfxSource");
            sfxGo.transform.SetParent(managers.transform, false);
            var sfxSrc = sfxGo.AddComponent<AudioSource>();
            sfxSrc.playOnAwake = false;
            SetField(am, "config", LoadAsset<AudioConfig>($"{DataDir}/AudioConfig.asset"));
            SetField(am, "musicSource", musicSrc);
            SetField(am, "sfxSource", sfxSrc);

            // AdsManager
            var ads = managers.AddComponent<AdsManager>();
            SetField(ads, "config", LoadAsset<AdConfig>($"{DataDir}/AdConfig.asset"));

            // 统一 Loading(自带 Overlay Canvas,Boot 阶段即可用)
            managers.AddComponent<LoadingManager>();

            // Boot 场景入口:走统一 Loading 进 Game
            new GameObject("BootLoader").AddComponent<BootLoader>();

            EditorSceneManager.SaveScene(scene, path);
            Debug.Log($"[ProjectBootstrap] 已生成 {path}");
        }

        private static void CreateGameScene()
        {
            var path = $"{ScenesDir}/Game.unity";
            if (File.Exists(path) && !ConfirmOverwrite(path)) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            new GameObject("GameRoot").AddComponent<GameSceneEntry>();

            // UIRoot:竖屏休闲模板,参考分辨率 1080x1920,按高适配
            var uiRoot = new GameObject("UIRoot", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            uiRoot.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = uiRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 1f;

            // 三层:面板 < 弹窗 < Toast(子 Canvas sortingOrder,不靠实例化顺序)
            var panelRoot = CreateLayer(uiRoot.transform, "Normal", (int)UILayer.Normal);
            var popupRoot = CreateLayer(uiRoot.transform, "Popup", (int)UILayer.Popup);
            var toastRoot = CreateLayer(uiRoot.transform, "Top", (int)UILayer.Top);

            var uim = uiRoot.AddComponent<UIManager>();
            SetField(uim, "panelRoot", panelRoot);
            SetField(uim, "popupRoot", popupRoot);
            SetField(uim, "toastRoot", toastRoot);

            // 注册 Prefab:首页面板 + Toast + 通用确认框(无则现场生成)
            var homePrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{UiPrefabDir}/HomePanel.prefab")
                             ?? CreateHomePanelPrefab($"{UiPrefabDir}/HomePanel.prefab");
            var toastPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{UiPrefabDir}/Toast.prefab")
                              ?? CreateToastPrefab($"{UiPrefabDir}/Toast.prefab");
            var dialogPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{UiPrefabDir}/DialogWindow.prefab")
                               ?? CreateDialogPrefab($"{UiPrefabDir}/DialogWindow.prefab");

            var so = new SerializedObject(uim);
            var list = so.FindProperty("panels");
            AddEntry(list, nameof(HomePanel), homePrefab);
            AddEntry(list, "Toast", toastPrefab);
            AddEntry(list, nameof(DialogWindow), dialogPrefab);
            so.ApplyModifiedProperties();

            // EventSystem(UI 点击必需)
            if (FindObjectOfType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem));
                es.AddComponent<StandaloneInputModule>(); // 换 Input System 包后改 InputSystemUIInputModule
            }

            EditorSceneManager.SaveScene(scene, path);

            // Build Settings:Boot 在前
            var bootPath = $"{ScenesDir}/Boot.unity";
            if (File.Exists(bootPath))
            {
                EditorBuildSettings.scenes = new[]
                {
                    new EditorBuildSettingsScene(bootPath, true),
                    new EditorBuildSettingsScene(path, true),
                };
            }

            Debug.Log($"[ProjectBootstrap] 已生成 {path},并写入 Build Settings(Boot 在前)。");
        }

        /// <summary>UI 层级:独立子 Canvas,按 sortingOrder 叠放,不靠实例化顺序。</summary>
        private static RectTransform CreateLayer(Transform parent, string name, int sortingOrder)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            UGUIUtil.Stretch(rt);
            var canvas = go.GetComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = sortingOrder;
            return rt;
        }

        private static void AddEntry(SerializedProperty list, string key, GameObject prefab)
        {
            list.arraySize++;
            var el = list.GetArrayElementAtIndex(list.arraySize - 1);
            el.FindPropertyRelative("key").stringValue = key;
            el.FindPropertyRelative("prefab").objectReferenceValue = prefab;
        }

        // ---------- 3. PlayerSettings ----------

        [MenuItem("MiniGame/3.应用小游戏 PlayerSettings")]
        public static void ApplyMiniGamePlayerSettings()
        {
            PlayerSettings.colorSpace = ColorSpace.Gamma; // WebGL 小游戏开销更小;美术验收时注意色彩观感
            PlayerSettings.stripEngineCode = true;
            PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.WebGL, ManagedStrippingLevel.Low);
            PlayerSettings.bundleVersion = "0.1.0";

            Debug.Log("[ProjectSettings] 已设:Gamma 色彩空间 / 引擎代码剥离 / WebGL 托管代码剥离 Low。");
            Debug.Log("[ProjectSettings] 出包前自查:纹理 ASTC、音频 AAC 低码率、背景音乐走远程、反射类型写 link.xml。包体限额以平台最新文档为准。");
        }

        // ---------- 示例/通用 Prefab ----------

        private static GameObject CreateHomePanelPrefab(string path)
        {
            var root = new GameObject(nameof(HomePanel), typeof(RectTransform));
            UGUIUtil.Stretch(root.GetComponent<RectTransform>());
            root.AddComponent<Image>().color = new Color(0.09f, 0.11f, 0.18f); // 深色底
            root.AddComponent<HomePanel>();

            var title = UGUIUtil.CreateText(root.transform, "Title", "小游戏模板", 72);
            UGUIUtil.SetAnchor(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -220f), new Vector2(800f, 160f));

            var coins = UGUIUtil.CreateText(root.transform, "Coins", "金币 0", 48);
            UGUIUtil.SetAnchor(coins.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -430f), new Vector2(600f, 90f));

            var startBtn = CreateButton(root.transform, "StartButton", "开始游戏", new Color(0.2f, 0.55f, 0.95f),
                new Vector2(0f, -120f), new Vector2(600f, 180f));
            var adBtn = CreateButton(root.transform, "AdButton", "看广告 +100", new Color(0.95f, 0.55f, 0.2f),
                new Vector2(0f, -330f), new Vector2(600f, 140f));

            var panel = root.GetComponent<HomePanel>();
            SetField(panel, "coinsText", coins);
            SetField(panel, "startButton", startBtn);
            SetField(panel, "adButton", adBtn);

            return SavePrefab(root, path);
        }

        private static GameObject CreateToastPrefab(string path)
        {
            var root = new GameObject("Toast", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            var rt = root.GetComponent<RectTransform>();
            UGUIUtil.SetAnchor(rt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -260f), new Vector2(760f, 130f));
            root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.78f);
            var toast = root.AddComponent<Toast>();

            var msg = UGUIUtil.CreateText(root.transform, "Message", "", 42);
            UGUIUtil.Stretch(msg.rectTransform);
            msg.rectTransform.offsetMin = new Vector2(30f, 10f);
            msg.rectTransform.offsetMax = new Vector2(-30f, -10f);

            SetField(toast, "messageText", msg);
            SetField(toast, "group", root.GetComponent<CanvasGroup>());
            return SavePrefab(root, path);
        }

        private static GameObject CreateDialogPrefab(string path)
        {
            var root = new GameObject(nameof(DialogWindow), typeof(RectTransform));
            UGUIUtil.SetAnchor(root.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820f, 560f));
            root.AddComponent<Image>().color = new Color(0.13f, 0.16f, 0.24f);
            var win = root.AddComponent<DialogWindow>();
            win.destroyOnClose = true; // 确认框一次性

            var title = UGUIUtil.CreateText(root.transform, "Title", "标题", 48);
            UGUIUtil.SetAnchor(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -95f), new Vector2(700f, 90f));

            var body = UGUIUtil.CreateText(root.transform, "Body", "内容", 40);
            UGUIUtil.SetAnchor(body.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(700f, 280f));

            var confirm = CreateButton(root.transform, "ConfirmButton", "确认", new Color(0.2f, 0.55f, 0.95f),
                new Vector2(-180f, -195f), new Vector2(300f, 110f));
            var cancel = CreateButton(root.transform, "CancelButton", "取消", new Color(0.35f, 0.38f, 0.45f),
                new Vector2(180f, -195f), new Vector2(300f, 110f));

            SetField(win, "titleText", title);
            SetField(win, "bodyText", body);
            SetField(win, "confirmButton", confirm);
            SetField(win, "cancelButton", cancel);
            SetField(win, "confirmText", confirm.GetComponentInChildren<Text>());
            SetField(win, "cancelText", cancel.GetComponentInChildren<Text>());
            return SavePrefab(root, path);
        }

        private static GameObject SavePrefab(GameObject root, string path)
        {
            Directory.CreateDirectory(UiPrefabDir);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            DestroyImmediate(root);
            Debug.Log($"[ProjectBootstrap] 已生成 {path}");
            return prefab;
        }

        private static Button CreateButton(Transform parent, string name, string label, Color color,
            Vector2 anchoredPos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            UGUIUtil.SetAnchor(go.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), anchoredPos, size);

            var text = UGUIUtil.CreateText(go.transform, "Label", label, 44);
            UGUIUtil.Stretch(text.rectTransform);
            return go.GetComponent<Button>();
        }

        // ---------- 通用工具 ----------

        private static void SetField(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogError($"[ProjectBootstrap] 找不到字段 {field}({target.GetType().Name}),检查脚本字段名。");
                return;
            }
            prop.objectReferenceValue = value;
            so.ApplyModifiedProperties();
        }

        private static T LoadAsset<T>(string path) where T : Object
            => AssetDatabase.LoadAssetAtPath<T>(path);

        private static bool ConfirmOverwrite(string path)
            => EditorUtility.DisplayDialog("覆盖确认", $"{path}\n已存在,覆盖重建?", "覆盖", "跳过");
    }
}
