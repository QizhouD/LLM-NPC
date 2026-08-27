#if UNITY_EDITOR
using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DigitalHuman.Editor
{
    public static class DigitalHumanRemoteUiCameraBootstrap
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string PrefabPath =
            "Assets/DigitalHuman/Prefabs/DigitalHuman.prefab";

        public static void ConfigureAndValidate()
        {
            try
            {
                ConfigurePrefabDialogueAccess();
                Scene scene = EditorSceneManager.OpenScene(
                    ScenePath,
                    OpenSceneMode.Single);
                ConfigureTopDownCamera();
                ConfigureRemotePlayerPosition();
                ConfigureResponsiveChatUi();
                ValidateScene();

                EditorSceneManager.MarkSceneDirty(scene);
                Require(EditorSceneManager.SaveScene(scene, ScenePath),
                    "Failed to save SampleScene.");
                AssetDatabase.SaveAssets();
                Debug.Log(
                    "[AgentNPC] Overhead camera, always-available remote chat, " +
                    "and responsive interaction-safe UI configured.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
                throw;
            }
        }

        private static void ConfigurePrefabDialogueAccess()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                DialogueController dialogue = root.GetComponent<DialogueController>();
                Require(dialogue != null,
                    "DialogueController is missing from DigitalHuman Prefab.");
                dialogue.ConfigureAgentDemoAccess(true, true);
                EditorUtility.SetDirty(dialogue);
                Require(PrefabUtility.SaveAsPrefabAsset(root, PrefabPath) != null,
                    "Failed to save DigitalHuman Prefab.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void ConfigureTopDownCamera()
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                camera = UnityEngine.Object.FindFirstObjectByType<Camera>();
            }

            Require(camera != null, "Main Camera is missing.");
            camera.gameObject.tag = "MainCamera";
            camera.orthographic = false;
            camera.fieldOfView = 55f;
            camera.nearClipPlane = 0.2f;
            camera.farClipPlane = 120f;

            Vector3 position = new Vector3(0f, 16f, -15f);
            Vector3 focus = new Vector3(0f, 0.45f, 0f);
            camera.transform.SetPositionAndRotation(
                position,
                Quaternion.LookRotation(focus - position, Vector3.up));
            EditorUtility.SetDirty(camera);
            EditorUtility.SetDirty(camera.transform);

            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
        }

        private static void ConfigureRemotePlayerPosition()
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player == null)
            {
                return;
            }

            player.transform.position = new Vector3(8f, 1.08f, -6f);
            EditorUtility.SetDirty(player.transform);
        }

        private static void ConfigureResponsiveChatUi()
        {
            DialogueUI ui = UnityEngine.Object.FindFirstObjectByType<DialogueUI>();
            Require(ui != null, "DialogueUI is missing.");
            ui.ConfigurePersistentPanel(true, 8);

            Canvas canvas = ui.GetComponent<Canvas>();
            CanvasScaler scaler = ui.GetComponent<CanvasScaler>();
            GraphicRaycaster raycaster = ui.GetComponent<GraphicRaycaster>();
            Require(canvas != null && scaler != null && raycaster != null,
                "DialogueCanvas rendering components are incomplete.");
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = false;
            raycaster.enabled = true;
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            scaler.referencePixelsPerUnit = 100f;

            CommandChatLayout.EnsureConfigured(ui);
            RectTransform panel = FindRect(ui.transform, "DialoguePanel");
            Require(panel != null && panel.gameObject.activeSelf,
                "DialoguePanel must stay active for command input.");
            Require(ui.PlayerInputField != null && ui.PlayerInputField.interactable,
                "Chat UI controls are incomplete.");

            EventSystem eventSystem = UnityEngine.Object.FindFirstObjectByType<EventSystem>();
            Require(eventSystem != null, "EventSystem is missing.");
            EditorUtility.SetDirty(ui);
            EditorUtility.SetDirty(canvas);
            EditorUtility.SetDirty(scaler);
            EditorUtility.SetDirty(panel);
        }

        private static void ValidateScene()
        {
            Camera camera = Camera.main;
            Require(camera != null && camera.transform.position.y >= 12f,
                "Overhead Main Camera was not configured.");
            Require(Vector3.Dot(camera.transform.forward, Vector3.down) >= 0.65f,
                "Main Camera angle is not sufficiently top-down.");

            Vector3[] platformCorners =
            {
                new Vector3(-11f, 0f, -8f),
                new Vector3(11f, 0f, -8f),
                new Vector3(-11f, 0f, 8f),
                new Vector3(11f, 0f, 8f)
            };
            for (int i = 0; i < platformCorners.Length; i++)
            {
                Vector3 viewport = camera.WorldToViewportPoint(platformCorners[i]);
                Require(viewport.z > 0f &&
                        viewport.x >= -0.08f && viewport.x <= 1.08f &&
                        viewport.y >= -0.08f && viewport.y <= 1.08f,
                    $"AgentPlatform is outside the overhead view at corner {i}: {viewport}");
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            DialogueController prefabDialogue =
                prefab != null ? prefab.GetComponent<DialogueController>() : null;
            Require(prefabDialogue != null &&
                    prefabDialogue.AutoStartDialogue &&
                    prefabDialogue.AllowRemoteDialogue,
                "DialogueController is not configured for direct remote access.");

            DialogueUI ui = UnityEngine.Object.FindFirstObjectByType<DialogueUI>();
            Require(ui != null && ui.IsPersistent,
                "DialogueUI is not persistent.");
            RectTransform panel = FindRect(ui.transform, "DialoguePanel");
            Vector2 anchorSize = panel.anchorMax - panel.anchorMin;
            Require(ui.GetComponent<CommandChatLayout>() != null,
                "CommandChatLayout is missing from DialogueCanvas.");
            Require(anchorSize.x >= 0.70f && anchorSize.x <= 0.90f &&
                    anchorSize.y >= 0.22f && anchorSize.y <= 0.32f,
                $"Command chat panel size is invalid: {anchorSize}");
            Require(panel.anchorMin.x >= 0f && panel.anchorMin.y >= 0f &&
                    panel.anchorMax.x <= 1f && panel.anchorMax.y <= 1f,
                "Chat panel anchors extend beyond the screen.");

            TMP_InputField input = FindRect(panel, "PlayerInputField")
                .GetComponent<TMP_InputField>();
            Button send = FindRect(panel, "SendButton").GetComponent<Button>();
            Require(input != null && input.interactable &&
                    send != null && send.interactable,
                "Chat input or Send button is not interactable.");
            Require(ui.GetComponent<CanvasScaler>().referenceResolution ==
                    new Vector2(1920f, 1080f),
                "CanvasScaler reference resolution is incorrect.");
        }

        private static RectTransform FindRect(Transform parent, string path)
        {
            Transform child = parent.Find(path);
            Require(child != null, $"UI object is missing: {path}");
            return child as RectTransform;
        }

        private static void SetStretch(
            RectTransform rect,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            Require(rect != null, "RectTransform is missing.");
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
#endif
