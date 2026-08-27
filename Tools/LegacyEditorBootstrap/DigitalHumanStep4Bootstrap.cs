#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DigitalHuman.Editor
{
    public static class DigitalHumanStep4Bootstrap
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string SmokeStateKey = "DigitalHuman.Step4.SmokeState";

        private static PlayerInteractionDetector detector;
        private static GameObject player;
        private static int runtimeStage;
        private static double deadline;
        private static bool interactionEventReceived;

        [InitializeOnLoadMethod]
        private static void ResumePendingSmokeTest()
        {
            if (SessionState.GetInt(SmokeStateKey, 0) == 0)
            {
                return;
            }

            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        public static void ConfigureScene()
        {
            try
            {
                Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                GameObject digitalHuman = GameObject.Find("DigitalHuman");
                Require(digitalHuman != null, "DigitalHuman root is missing.");

                Transform triggerTransform = digitalHuman.transform.Find("InteractionTrigger");
                Require(triggerTransform != null, "InteractionTrigger is missing.");
                PlayerInteractionDetector interactionDetector =
                    triggerTransform.GetComponent<PlayerInteractionDetector>();
                if (interactionDetector == null)
                {
                    interactionDetector = triggerTransform.gameObject.AddComponent<PlayerInteractionDetector>();
                }

                GameObject scenePlayer = GameObject.FindWithTag("Player");
                if (scenePlayer == null)
                {
                    scenePlayer = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    scenePlayer.name = "Player";
                    scenePlayer.tag = "Player";
                    scenePlayer.transform.position = new Vector3(0f, 1f, -3f);

                    CapsuleCollider capsuleCollider = scenePlayer.GetComponent<CapsuleCollider>();
                    if (capsuleCollider != null)
                    {
                        UnityEngine.Object.DestroyImmediate(capsuleCollider);
                    }

                    CharacterController characterController = scenePlayer.AddComponent<CharacterController>();
                    characterController.center = Vector3.zero;
                    characterController.height = 2f;
                    characterController.radius = 0.5f;
                }

                DemoPlayerController playerController = scenePlayer.GetComponent<DemoPlayerController>();
                if (playerController == null)
                {
                    playerController = scenePlayer.AddComponent<DemoPlayerController>();
                }

                Camera mainCamera = Camera.main;
                playerController.Configure(mainCamera != null ? mainCamera.transform : null, 3f);

                EditorSceneManager.MarkSceneDirty(scene);
                Require(EditorSceneManager.SaveScene(scene, ScenePath), "Failed to save SampleScene.");
                AssetDatabase.SaveAssets();
                Debug.Log("[DigitalHuman] STEP 4 Player interaction scene setup completed.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
                throw;
            }
        }

        public static void StartPlayModeSmokeTest()
        {
            try
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                ValidateEditModeSetup();
                SessionState.SetInt(SmokeStateKey, 1);
                EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
                EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
                EditorApplication.EnterPlaymode();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                SessionState.EraseInt(SmokeStateKey);
                EditorApplication.Exit(1);
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (SessionState.GetInt(SmokeStateKey, 0) == 0)
            {
                return;
            }

            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                try
                {
                    detector = UnityEngine.Object.FindFirstObjectByType<PlayerInteractionDetector>();
                    player = GameObject.FindWithTag("Player");
                    Require(detector != null, "PlayerInteractionDetector is missing in Play Mode.");
                    Require(player != null, "Player is missing in Play Mode.");

                    interactionEventReceived = false;
                    detector.InteractionRequested += OnInteractionRequested;
                    player.transform.position = new Vector3(0f, 1f, -3f);
                    Physics.SyncTransforms();
                    runtimeStage = 1;
                    deadline = EditorApplication.timeSinceStartup + 0.4d;
                    EditorApplication.update -= RunSmokeTestUpdate;
                    EditorApplication.update += RunSmokeTestUpdate;
                }
                catch (Exception exception)
                {
                    FailSmokeTest(exception);
                }
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                int exitCode = SessionState.GetInt(SmokeStateKey, -1) == 2 ? 0 : 1;
                SessionState.EraseInt(SmokeStateKey);
                EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
                EditorApplication.update -= RunSmokeTestUpdate;
                EditorApplication.delayCall += () => EditorApplication.Exit(exitCode);
            }
        }

        private static void RunSmokeTestUpdate()
        {
            if (EditorApplication.timeSinceStartup < deadline)
            {
                return;
            }

            try
            {
                switch (runtimeStage)
                {
                    case 1:
                        Require(!detector.CanInteract, "Detector should be inactive while Player is outside.");
                        player.transform.position = new Vector3(0f, 1f, 1.5f);
                        Physics.SyncTransforms();
                        runtimeStage = 2;
                        deadline = EditorApplication.timeSinceStartup + 0.6d;
                        break;

                    case 2:
                        Require(detector.CanInteract, "Detector did not register Player entering range.");
                        Require(detector.CurrentPlayer == player.transform, "Detector stored the wrong Player.");
                        Require(detector.TryInteract(), "TryInteract returned false while Player was in range.");
                        Require(interactionEventReceived, "InteractionRequested event was not raised.");
                        player.transform.position = new Vector3(0f, 1f, -3f);
                        Physics.SyncTransforms();
                        runtimeStage = 3;
                        deadline = EditorApplication.timeSinceStartup + 0.6d;
                        break;

                    case 3:
                        Require(!detector.CanInteract, "Detector did not register Player leaving range.");
                        SessionState.SetInt(SmokeStateKey, 2);
                        Debug.Log("[DigitalHuman] STEP 4 Play Mode interaction test passed.");
                        EditorApplication.update -= RunSmokeTestUpdate;
                        EditorApplication.ExitPlaymode();
                        break;
                }
            }
            catch (Exception exception)
            {
                FailSmokeTest(exception);
            }
        }

        private static void OnInteractionRequested(Transform playerTransform)
        {
            interactionEventReceived = playerTransform == player.transform;
        }

        private static void FailSmokeTest(Exception exception)
        {
            Debug.LogException(exception);
            SessionState.SetInt(SmokeStateKey, -1);
            EditorApplication.update -= RunSmokeTestUpdate;
            if (EditorApplication.isPlaying)
            {
                EditorApplication.ExitPlaymode();
            }
            else
            {
                EditorApplication.Exit(1);
            }
        }

        private static void ValidateEditModeSetup()
        {
            GameObject digitalHuman = GameObject.Find("DigitalHuman");
            GameObject scenePlayer = GameObject.FindWithTag("Player");
            Require(digitalHuman != null, "DigitalHuman root is missing.");
            Require(scenePlayer != null, "Player is missing.");
            Require(scenePlayer.GetComponent<CharacterController>() != null,
                "Player CharacterController is missing.");
            Require(scenePlayer.GetComponent<DemoPlayerController>() != null,
                "DemoPlayerController is missing.");

            Transform trigger = digitalHuman.transform.Find("InteractionTrigger");
            Require(trigger != null && trigger.GetComponent<PlayerInteractionDetector>() != null,
                "PlayerInteractionDetector is missing.");
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
