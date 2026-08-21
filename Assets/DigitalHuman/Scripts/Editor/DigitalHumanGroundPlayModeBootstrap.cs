#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DigitalHuman.Editor
{
    public static class DigitalHumanGroundPlayModeBootstrap
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string StateKey = "DigitalHuman.GroundTest.State";

        private static CharacterController playerController;
        private static bool groundCollisionObserved;
        private static double deadline;

        [InitializeOnLoadMethod]
        private static void ResumePendingTest()
        {
            if (SessionState.GetInt(StateKey, 0) == 0)
            {
                return;
            }

            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        public static void StartTest()
        {
            try
            {
                EditorSceneManager.OpenScene(ScenePath);
                Require(GameObject.Find("Environment/GroundPlatform") != null,
                    "GroundPlatform is missing before Play Mode.");
                Require(GameObject.FindWithTag("Player") != null,
                    "Player is missing before Play Mode.");
                SessionState.SetInt(StateKey, 1);
                EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
                EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
                EditorApplication.EnterPlaymode();
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (SessionState.GetInt(StateKey, 0) == 0)
            {
                return;
            }

            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                try
                {
                    GameObject player = GameObject.FindWithTag("Player");
                    Require(player != null, "Player is missing in Play Mode.");
                    playerController = player.GetComponent<CharacterController>();
                    Require(playerController != null,
                        "Player CharacterController is missing in Play Mode.");
                    GameObject platform = GameObject.Find("Environment/GroundPlatform");
                    Require(platform != null &&
                            platform.GetComponent<BoxCollider>() != null,
                        "GroundPlatform collider is missing in Play Mode.");

                    player.transform.position = new Vector3(0f, 4f, -3f);
                    Physics.SyncTransforms();
                    groundCollisionObserved = false;
                    deadline = EditorApplication.timeSinceStartup + 2d;
                    EditorApplication.update -= RunGroundTest;
                    EditorApplication.update += RunGroundTest;
                }
                catch (Exception exception)
                {
                    Fail(exception);
                }
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                int exitCode = SessionState.GetInt(StateKey, -1) == 2 ? 0 : 1;
                SessionState.EraseInt(StateKey);
                EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
                EditorApplication.update -= RunGroundTest;
                EditorApplication.delayCall += () => EditorApplication.Exit(exitCode);
            }
        }

        private static void RunGroundTest()
        {
            try
            {
                CollisionFlags flags = playerController.Move(Vector3.down * 0.15f);
                groundCollisionObserved |= (flags & CollisionFlags.Below) != 0;
                if (EditorApplication.timeSinceStartup < deadline)
                {
                    return;
                }

                float playerY = playerController.transform.position.y;
                Require(groundCollisionObserved,
                    "Player never collided with GroundPlatform while moving downward.");
                Require(playerY >= 0.98f && playerY <= 1.12f,
                    $"Player did not settle on the platform. Y={playerY:F3}");
                Debug.Log(
                    $"[DigitalHuman] Ground Play Mode test passed. " +
                    $"Player settled at Y={playerY:F3}.");
                SessionState.SetInt(StateKey, 2);
                EditorApplication.update -= RunGroundTest;
                EditorApplication.ExitPlaymode();
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        }

        private static void Fail(Exception exception)
        {
            Debug.LogException(exception);
            SessionState.SetInt(StateKey, -1);
            EditorApplication.update -= RunGroundTest;
            if (EditorApplication.isPlaying)
            {
                EditorApplication.ExitPlaymode();
            }
            else
            {
                EditorApplication.Exit(1);
            }
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
