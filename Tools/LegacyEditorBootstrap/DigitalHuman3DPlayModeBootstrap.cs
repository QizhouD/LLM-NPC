#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DigitalHuman.Editor
{
    public static class DigitalHuman3DPlayModeBootstrap
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string StateKey = "DigitalHuman.3DPlayMode.State";

        private static GameObject digitalHuman;
        private static GameObject player;
        private static Transform characterRoot;
        private static Transform torso;
        private static Transform leftUpperArm;
        private static PlayerInteractionDetector detector;
        private static DialogueController dialogue;
        private static DigitalHumanController stateController;
        private static LookAtController lookAtController;
        private static ProceduralHumanoidAnimator proceduralAnimator;
        private static Quaternion idleArmRotation;
        private static Quaternion idleTorsoRotation;
        private static int stage;
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
                    digitalHuman = GameObject.Find("DigitalHuman");
                    player = GameObject.FindWithTag("Player");
                    Require(digitalHuman != null && player != null,
                        "DigitalHuman or Player is missing in Play Mode.");
                    characterRoot =
                        digitalHuman.transform.Find("Model/DigitalHuman_3D");
                    Require(characterRoot != null,
                        "DigitalHuman_3D is missing in Play Mode.");
                    torso = FindRecursive(characterRoot, "Torso");
                    leftUpperArm = FindRecursive(characterRoot, "LeftUpperArm");
                    Require(torso != null && leftUpperArm != null,
                        "Required procedural animation joints are missing.");

                    detector = digitalHuman.transform.Find("InteractionTrigger")
                        .GetComponent<PlayerInteractionDetector>();
                    dialogue = digitalHuman.GetComponent<DialogueController>();
                    stateController = digitalHuman.GetComponent<DigitalHumanController>();
                    lookAtController = digitalHuman.GetComponent<LookAtController>();
                    proceduralAnimator =
                        digitalHuman.GetComponent<ProceduralHumanoidAnimator>();
                    Require(detector != null && dialogue != null &&
                            stateController != null && lookAtController != null &&
                            proceduralAnimator != null,
                        "Existing interaction or 3D animation logic is incomplete.");

                    ValidateVolumetricBodyAtFourAngles();
                    Require(characterRoot.GetComponentsInChildren<SpriteRenderer>(true).Length == 0,
                        "DigitalHuman_3D contains a SpriteRenderer.");
                    player.transform.position = new Vector3(0f, 1f, -3f);
                    digitalHuman.transform.rotation = Quaternion.identity;
                    characterRoot.localRotation = Quaternion.identity;
                    Physics.SyncTransforms();
                    idleArmRotation = leftUpperArm.localRotation;
                    idleTorsoRotation = torso.localRotation;
                    stage = 1;
                    deadline = EditorApplication.timeSinceStartup + 0.45d;
                    EditorApplication.update -= RunTest;
                    EditorApplication.update += RunTest;
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
                EditorApplication.update -= RunTest;
                EditorApplication.delayCall += () => EditorApplication.Exit(exitCode);
            }
        }

        private static void RunTest()
        {
            if (EditorApplication.timeSinceStartup < deadline)
            {
                return;
            }

            try
            {
                switch (stage)
                {
                    case 1:
                        Require(stateController.CurrentState == DigitalHumanState.Idle,
                            "3D humanoid did not start in Idle.");
                        Require(proceduralAnimator.MotionMagnitude > 0.1f,
                            "Idle procedural motion is inactive.");
                        Require(Quaternion.Angle(idleTorsoRotation, torso.localRotation) > 0.03f,
                            "Idle torso breathing/sway was not applied.");
                        player.transform.position = new Vector3(2f, 1f, 2.5f);
                        Physics.SyncTransforms();
                        stage = 2;
                        deadline = EditorApplication.timeSinceStartup + 0.8d;
                        break;

                    case 2:
                        Require(detector.CanInteract,
                            "Player was not detected near the 3D humanoid.");
                        Require(lookAtController.CurrentTarget == player.transform,
                            "FacePlayer target was not acquired.");
                        Vector3 planarDirection =
                            player.transform.position - digitalHuman.transform.position;
                        planarDirection.y = 0f;
                        float faceAngle = Vector3.Angle(
                            digitalHuman.transform.forward,
                            planarDirection.normalized);
                        Require(faceAngle < 5f,
                            $"3D humanoid did not smoothly face Player. Angle={faceAngle:F2}");
                        idleArmRotation = leftUpperArm.localRotation;
                        Require(detector.TryInteract(),
                            "Interaction request failed for 3D humanoid.");
                        Require(dialogue.IsDialogueActive,
                            "Dialogue did not start for 3D humanoid.");
                        dialogue.ReceiveNpcResponse("3D talking animation test.");
                        stage = 3;
                        deadline = EditorApplication.timeSinceStartup + 0.28d;
                        break;

                    case 3:
                        Require(stateController.CurrentState == DigitalHumanState.Speaking,
                            "Dialogue response did not enter Speaking state.");
                        Require(proceduralAnimator.IsTalking &&
                                proceduralAnimator.TalkingBlend > 0.5f,
                            "Procedural Talking blend did not activate.");
                        Require(Quaternion.Angle(
                                idleArmRotation,
                                leftUpperArm.localRotation) > 2f,
                            "Talking arm gesture was not applied.");
                        dialogue.EndDialogue();
                        stage = 4;
                        deadline = EditorApplication.timeSinceStartup + 0.65d;
                        break;

                    case 4:
                        Require(!proceduralAnimator.IsTalking &&
                                proceduralAnimator.TalkingBlend < 0.05f,
                            "3D humanoid did not return to visual Idle after dialogue.");
                        Require(Quaternion.Angle(
                                idleArmRotation,
                                leftUpperArm.localRotation) < 0.5f,
                            "Talking gesture did not return to Idle pose.");
                        player.transform.position = new Vector3(0f, 1f, -3f);
                        Physics.SyncTransforms();
                        stage = 5;
                        deadline = EditorApplication.timeSinceStartup + 0.6d;
                        break;

                    case 5:
                        Require(!detector.CanInteract &&
                                stateController.CurrentState == DigitalHumanState.Idle,
                            "3D humanoid did not return to Idle after Player exit.");
                        Debug.Log(
                            "[DigitalHuman] 3D Play Mode acceptance test passed: " +
                            "volumetric 360 body, Idle, Talking, FacePlayer, interaction.");
                        SessionState.SetInt(StateKey, 2);
                        EditorApplication.update -= RunTest;
                        EditorApplication.ExitPlaymode();
                        break;
                }
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        }

        private static void ValidateVolumetricBodyAtFourAngles()
        {
            MeshRenderer[] renderers =
                characterRoot.GetComponentsInChildren<MeshRenderer>(true);
            Require(renderers.Length >= 18,
                "3D humanoid does not have enough mesh body parts.");
            float[] angles = { 0f, 90f, 180f, 270f };
            for (int angleIndex = 0; angleIndex < angles.Length; angleIndex++)
            {
                characterRoot.localRotation = Quaternion.Euler(
                    0f, angles[angleIndex], 0f);
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }

                Require(bounds.size.y >= 1.70f &&
                        bounds.size.x >= 0.25f && bounds.size.z >= 0.25f,
                    $"3D volume failed at {angles[angleIndex]} degrees: {bounds.size}");
            }

            characterRoot.localRotation = Quaternion.identity;
        }

        private static Transform FindRecursive(Transform root, string name)
        {
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                if (transforms[i].name == name)
                {
                    return transforms[i];
                }
            }

            return null;
        }

        private static void Fail(Exception exception)
        {
            Debug.LogException(exception);
            SessionState.SetInt(StateKey, -1);
            EditorApplication.update -= RunTest;
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
