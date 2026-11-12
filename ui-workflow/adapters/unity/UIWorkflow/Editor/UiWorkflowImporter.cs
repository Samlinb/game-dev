using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;

namespace UIWorkflow
{
    public static class UiWorkflowImporter
    {
        private const string AssetRoot = "Assets/UIWorkflow/";
        private static readonly Regex Id = new Regex("^[a-z][a-z0-9_]{0,63}$");
        private static readonly Regex Revision = new Regex("^[a-f0-9]{16}$");

        [Serializable] private sealed class Bundle
        {
            public string format, id, revision;
            public int version, width, height;
            public Asset[] assets;
            public Node[] nodes;
        }

        [Serializable] private sealed class Asset
        {
            public string id, file, sha256;
            public Border border;
        }

        [Serializable] private sealed class Border
        {
            public int left, top, right, bottom;
        }

        [Serializable] private sealed class Bounds
        {
            public float x, y, width, height;
        }

        [Serializable] private sealed class Node
        {
            public string id, parent, type, asset, anchor, text, color, align, action, pressed, disabled;
            public int fontSize;
            public Bounds rect;
        }

        [MenuItem("Tools/UI Workflow/Import Selected Bundle")]
        public static void ImportSelection()
        {
            Selection.activeObject = Import(AssetDatabase.GetAssetPath(Selection.activeObject));
        }

        [MenuItem("Tools/UI Workflow/Import Selected Bundle", true)]
        private static bool CanImportSelection()
        {
            return IsBundlePath(AssetDatabase.GetAssetPath(Selection.activeObject));
        }

        private static bool IsBundlePath(string path)
        {
            return path != null && path.StartsWith(AssetRoot, StringComparison.Ordinal) &&
                path.EndsWith("/bundle.json", StringComparison.Ordinal);
        }

        public static GameObject Import(string path)
        {
            if (!IsBundlePath(path)) throw new ArgumentException("Select bundle.json under " + AssetRoot);
            var bundle = JsonUtility.FromJson<Bundle>(File.ReadAllText(path));
            if (bundle == null || bundle.format != "ui-workflow" || bundle.version != 1 ||
                !Id.IsMatch(bundle.id ?? "") || !Revision.IsMatch(bundle.revision ?? "") ||
                bundle.width <= 0 || bundle.height <= 0 || bundle.assets == null || bundle.nodes == null ||
                path != AssetRoot + bundle.id + "_" + bundle.revision + "/bundle.json")
                throw new InvalidDataException("Expected an intact UI Workflow v1 bundle in its revision folder.");

            var folder = Path.GetDirectoryName(path).Replace('\\', '/');
            var prefabPath = folder + "/" + bundle.id + ".prefab";
            var images = new Dictionary<string, Asset>();
            foreach (var image in bundle.assets)
            {
                if (image == null || !Id.IsMatch(image.id ?? "") || image.file != "images/" + image.id + ".png")
                    throw new InvalidDataException("Invalid bundle image path.");
                var png = File.ReadAllBytes(folder + "/" + image.file);
                using (var sha = SHA256.Create())
                {
                    var hash = BitConverter.ToString(sha.ComputeHash(png)).Replace("-", "").ToLowerInvariant();
                    if (hash != image.sha256) throw new InvalidDataException("PNG hash differs: " + image.id);
                }
                images.Add(image.id, image);
            }

            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (existing != null) return existing;
            TMP_FontAsset font = null;
            if (Array.Exists(bundle.nodes, node => node.type == "text"))
            {
                font = TMP_Settings.defaultFontAsset;
                if (font == null) throw new InvalidOperationException("Configure the project TMP default font before import.");
            }
            var sprites = new Dictionary<string, Sprite>();
            foreach (var image in bundle.assets)
                sprites.Add(image.id, ImportSprite(folder + "/" + image.file, image.border));

            var root = new GameObject(bundle.id, typeof(RectTransform));
            try
            {
                var rect = (RectTransform)root.transform;
                root.layer = LayerMask.NameToLayer("UI");
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
                rect.sizeDelta = new Vector2(bundle.width, bundle.height);
                var parents = new Dictionary<string, RectTransform> { { "$root", rect } };
                var sizes = new Dictionary<string, Vector2> { { "$root", rect.sizeDelta } };
                foreach (var node in bundle.nodes)
                {
                    parents.Add(node.id, CreateNode(node, parents[node.parent], sizes[node.parent], sprites, font));
                    sizes.Add(node.id, new Vector2(node.rect.width, node.rect.height));
                }
                var result = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                if (result == null) throw new IOException("Unity failed to save " + prefabPath);
                Debug.Log("UI Workflow created " + prefabPath);
                return result;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static Sprite ImportSprite(string path, Border border)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new IOException("Unity has not imported " + path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 16384;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteBorder = border == null ? Vector4.zero :
                new Vector4(border.left, border.bottom, border.right, border.top);
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path) ?? throw new IOException("Missing Sprite at " + path);
        }

        private static RectTransform CreateNode(Node node, RectTransform parent, Vector2 size,
            Dictionary<string, Sprite> sprites, TMP_FontAsset font)
        {
            if (node == null || node.rect == null || !Id.IsMatch(node.id ?? ""))
                throw new InvalidDataException("Invalid UI node.");
            var gameObject = new GameObject(node.id, typeof(RectTransform));
            var rect = (RectTransform)gameObject.transform;
            rect.SetParent(parent, false);
            gameObject.layer = LayerMask.NameToLayer("UI");
            SetRect(rect, node, size);

            switch (node.type)
            {
                case "group":
                    break;
                case "image":
                case "button":
                    var image = gameObject.AddComponent<UnityEngine.UI.Image>();
                    image.sprite = sprites[node.asset];
                    image.type = image.sprite.border == Vector4.zero ?
                        UnityEngine.UI.Image.Type.Simple : UnityEngine.UI.Image.Type.Sliced;
                    image.raycastTarget = node.type == "button";
                    if (node.type == "button")
                    {
                        var button = gameObject.AddComponent<UnityEngine.UI.Button>();
                        button.targetGraphic = image;
                        if (!string.IsNullOrEmpty(node.pressed) || !string.IsNullOrEmpty(node.disabled))
                        {
                            button.transition = UnityEngine.UI.Selectable.Transition.SpriteSwap;
                            button.spriteState = new UnityEngine.UI.SpriteState
                            {
                                pressedSprite = string.IsNullOrEmpty(node.pressed) ? image.sprite : sprites[node.pressed],
                                disabledSprite = string.IsNullOrEmpty(node.disabled) ? image.sprite : sprites[node.disabled]
                            };
                        }
                        var action = gameObject.AddComponent<UiWorkflowAction>();
                        action.actionId = node.action;
                        UnityEventTools.AddPersistentListener(button.onClick, action.Dispatch);
                    }
                    break;
                case "text":
                    var label = gameObject.AddComponent<TextMeshProUGUI>();
                    label.font = font;
                    label.text = node.text;
                    label.fontSize = node.fontSize;
                    label.enableWordWrapping = false;
                    label.richText = false;
                    label.raycastTarget = false;
                    label.overflowMode = TextOverflowModes.Truncate;
                    label.alignment = node.align == "left" ? TextAlignmentOptions.MidlineLeft :
                        node.align == "right" ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.Midline;
                    if (ColorUtility.TryParseHtmlString(node.color, out var color)) label.color = color;
                    break;
                default:
                    throw new InvalidDataException("Unsupported UI node type: " + node.type);
            }

            return rect;
        }

        private static void SetRect(RectTransform rect, Node node, Vector2 parentSize)
        {
            var bounds = node.rect;
            rect.pivot = new Vector2(0, 1);
            if (node.anchor == "stretch")
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = new Vector2(bounds.x, parentSize.y - bounds.y - bounds.height);
                rect.offsetMax = new Vector2(bounds.x + bounds.width - parentSize.x, -bounds.y);
                return;
            }

            var anchor = node.anchor == "topRight" ? Vector2.one :
                node.anchor == "center" ? new Vector2(.5f, .5f) :
                node.anchor == "bottomCenter" ? new Vector2(.5f, 0) : new Vector2(0, 1);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.sizeDelta = new Vector2(bounds.width, bounds.height);
            rect.anchoredPosition = new Vector2(bounds.x - parentSize.x * anchor.x,
                parentSize.y * (1 - anchor.y) - bounds.y);
        }
    }
}
