using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;

namespace UIWorkflow
{
    public static class UiWorkflowVerify
    {
        [Serializable] private sealed class ButtonResult
        {
            public string node;
            public string action;
            public string raycastTarget;
            public bool clicked;
        }

        [Serializable] private sealed class Report
        {
            public string prefab;
            public int width, height;
            public ButtonResult[] buttons;
        }

        [MenuItem("Tools/UI Workflow/Verify Selected Bundle")]
        public static void VerifySelection()
        {
            var path = AssetDatabase.GetAssetPath(Selection.activeObject);
            var output = EditorUtility.OpenFolderPanel("UI Workflow verification output", "", "");
            if (!string.IsNullOrEmpty(output)) Verify(path, output);
        }

        public static void RunBatch()
        {
            var arguments = Environment.GetCommandLineArgs();
            var bundle = Array.IndexOf(arguments, "-uiBundle");
            var output = Array.IndexOf(arguments, "-uiOutput");
            if (bundle < 0 || output < 0 || bundle + 1 >= arguments.Length || output + 1 >= arguments.Length)
                throw new ArgumentException("Pass -uiBundle Assets/UIWorkflow/.../bundle.json -uiOutput <directory>");
            Verify(arguments[bundle + 1], arguments[output + 1]);
        }

        public static void Verify(string input, string output)
        {
            var prefab = UiWorkflowImporter.Import(input);
            var sourceSize = ((RectTransform)prefab.transform).rect.size;
            var width = Mathf.RoundToInt(sourceSize.x);
            var height = Mathf.RoundToInt(sourceSize.y);
            Directory.CreateDirectory(output);

            var scene = EditorSceneManager.NewPreviewScene();
            var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            var previousTexture = RenderTexture.active;
            Texture2D snapshot = null;
            try
            {
                var cameraObject = EditorUtility.CreateGameObjectWithHideFlags("UI Workflow Camera", HideFlags.None, typeof(Camera));
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, scene);
                var camera = cameraObject.GetComponent<Camera>();
                camera.scene = scene;
                camera.orthographic = true;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.clear;
                camera.targetTexture = target;

                var canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.GraphicRaycaster));
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(canvasObject, scene);
                var canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1;
                var raycaster = canvasObject.GetComponent<UnityEngine.UI.GraphicRaycaster>();
                var eventObject = new GameObject("EventSystem", typeof(EventSystem));
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(eventObject, scene);
                var events = eventObject.GetComponent<EventSystem>();

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                instance.transform.SetParent(canvasObject.transform, false);
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = target;
                snapshot = new Texture2D(width, height, TextureFormat.RGBA32, false);
                snapshot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                snapshot.Apply();
                File.WriteAllBytes(Path.Combine(output, "unity-preview.png"), snapshot.EncodeToPNG());

                var results = new List<ButtonResult>();
                foreach (var action in instance.GetComponentsInChildren<UiWorkflowAction>(true))
                {
                    var rect = (RectTransform)action.transform;
                    var pointer = new PointerEventData(events)
                    {
                        button = PointerEventData.InputButton.Left,
                        position = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.center))
                    };
                    var hits = new List<RaycastResult>();
                    raycaster.Raycast(pointer, hits);
                    var clicked = false;
                    action.invoked.AddListener(() => clicked = true);
                    var hit = hits.Count > 0 ? hits[0].gameObject : null;
                    if (hit != null && hit.GetComponentInParent<UiWorkflowAction>() == action)
                    {
                        var button = action.GetComponent<UnityEngine.UI.Button>();
                        for (var index = 0; index < button.onClick.GetPersistentEventCount(); index++)
                        {
                            if (button.onClick.GetPersistentTarget(index) == action &&
                                button.onClick.GetPersistentMethodName(index) == nameof(UiWorkflowAction.Dispatch))
                                button.onClick.SetPersistentListenerState(index, UnityEventCallState.EditorAndRuntime);
                        }
                        ExecuteEvents.ExecuteHierarchy(hit, pointer, ExecuteEvents.pointerClickHandler);
                    }
                    results.Add(new ButtonResult { node = action.name, action = action.actionId,
                        raycastTarget = hit == null ? "" : hit.name, clicked = clicked });
                }

                var report = new Report { prefab = AssetDatabase.GetAssetPath(prefab), width = width, height = height, buttons = results.ToArray() };
                File.WriteAllText(Path.Combine(output, "unity-verification.json"), JsonUtility.ToJson(report, true));
                var failures = results.FindAll(result => !result.clicked);
                if (failures.Count != 0)
                    throw new InvalidOperationException("Button hit or dispatch failed: " + string.Join(", ", failures.ConvertAll(result => result.node)));
                Debug.Log("UI Workflow verified " + results.Count + " button clicks. " + output);
            }
            finally
            {
                RenderTexture.active = previousTexture;
                if (snapshot != null) UnityEngine.Object.DestroyImmediate(snapshot);
                EditorSceneManager.ClosePreviewScene(scene);
                RenderTexture.ReleaseTemporary(target);
            }
        }
    }
}
