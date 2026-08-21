#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DigitalHuman.Editor
{
    public static class DigitalHumanStep5Bootstrap
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string SmokeStateKey = "DigitalHuman.Step5.SmokeState";

        private static GameObject digitalHuman;
        private static GameObject player;
        private static LookAtController lookAtController;
        private static int runtimeStage;
        private static double deadline;
        private static Quaternion rotationAfterExit;

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
                GameObject npc = GameObject.Find("DigitalHuman");
                Require(npc != null, "DigitalHuman root is missing.");

                Transform trigger = npc.transform.Find("InteractionTrigger");
                Require(trigger != null, "InteractionTrigger is missing.");
                PlayerInteractionDetector detector = trigger.GetComponent<PlayerInteractionDetector>();
                Require(detector != null, "PlayerInteractionDetector is missing.");

                LookAtController controller = npc.GetComponent<LookAtController>();
                if (controller == null)
                {
                    controller = npc.AddComponent<LookAtController>();
                }

                controller.Configure(detector, npc.transform, 6f);
                EditorSceneManager.MarkSceneDirty(scene);
                Require(EditorSceneManager.SaveScene(scene, ScenePath), "Failed to save SampleScene.");
                AssetDatabase.SaveAssets();
                Debug.Log("[DigitalHuman] STEP 5 Look At scene setup completed.");
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
                    digitalHuman = GameObject.Find("DigitalHuman");
                    player = GameObject.FindWithTag("Player");
                    lookAtController = UnityEngine.Object.FindFirstObjectByType<LookAtController>();
                    Require(digitalHuman != null, "DigitalHuman is missing in Play Mode.");
                    Require(player != null, "Player is missing in Play Mode.");
                    Require(lookAtController != null, "LookAtController is missing in Play Mode.");

                    digitalHuman.transform.rotation = Quaternion.identity;
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
                        Require(lookAtController.CurrentTarget == null,
                            "Look At target should be empty while Player is outside.");
                        player.transform.position = new Vector3(2f, 1f, 3f);
                        Physics.SyncTransforms();
                        runtimeStage = 2;
                        deadline = EditorApplication.timeSinceStartup + 1.3d;
                        break;

                    case 2:
                        Require(lookAtController.CurrentTarget == player.transform,
                            "Look At target was not assigned after Player entered range.");
                        Vector3 direction = player.transform.position - digitalHuman.transform.position;
                        direction.y = 0f;
                        float facingAngle = Vector3.Angle(digitalHuman.transform.forward, direction.normalized);
                        Require(facingAngle < 3f,
                            $"DigitalHuman did not smoothly face Player. Angle: {facingAngle:F2}");
                        Vector3 euler = digitalHuman.transform.eulerAngles;
                        Require(Mathf.Abs(Mathf.DeltaAngle(0f, euler.x)) < 0.1f &&
                                Mathf.Abs(Mathf.DeltaAngle(0f, euler.z)) < 0.1f,
                            "Look At rotation changed a non-Y axis.");

                        player.transform.position = new Vector3(0f, 1f, -3f);
                        Physics.SyncTransforms();
                        runtimeStage = 3;
                        deadline = EditorApplication.timeSinceStartup + 0.6d;
                        break;

                    case 3:
                        Require(lookAtController.CurrentTarget == null,
                            "Look At target was not cleared after Player exited range.");
                        rotationAfterExit = digitalHuman.transform.rotation;
                        runtimeStage = 4;
                        deadline = EditorApplication.timeSinceStartup + 0.5d;
                        break;

                    case 4:
                        Require(Quaternion.Angle(rotationAfterExit, digitalHuman.transform.rotation) < 0.1f,
                            "DigitalHuman kept rotating after Player exited range.");
                        SessionState.SetInt(SmokeStateKey, 2);
                        Debug.Log("[DigitalHuman] STEP 5 Play Mode Look At test passed.");
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
            GameObject npc = GameObject.Find("DigitalHuman");
            Require(npc != null, "DigitalHuman root is missing.");
            Require(npc.GetComponent<LookAtController>() != null, "LookAtController is missing.");
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
