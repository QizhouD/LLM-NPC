#if UNITY_EDITOR
using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

namespace DigitalHuman.Editor
{
    public static class DigitalHumanStep17AuditBootstrap
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string PersonaPath =
            "Assets/DigitalHuman/ScriptableObjects/EVA Persona.asset";
        private const string AnimatorControllerPath =
            "Assets/DigitalHuman/Animations/DigitalHuman.controller";

        public static void RunAudit()
        {
            try
            {
                Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                Require(scene.IsValid() && scene.isLoaded, "SampleScene did not load.");

                GameObject digitalHuman = GameObject.Find("DigitalHuman");
                GameObject player = GameObject.FindWithTag("Player");
                GameObject canvas = GameObject.Find("DialogueCanvas");
                Require(digitalHuman != null, "DigitalHuman root is missing.");
                Require(player != null, "Player is missing.");
                Require(canvas != null, "DialogueCanvas is missing.");

                ValidateHierarchy(digitalHuman);
                ValidateCoreComponents(digitalHuman);
                ValidatePlayer(player);
                ValidateUi(canvas);
                ValidateAssets(digitalHuman);
                ValidateNoMissingScripts(scene);

                GameContextProvider contextProvider =
                    digitalHuman.GetComponent<GameContextProvider>();
                AIConversationManager manager =
                    digitalHuman.GetComponent<AIConversationManager>();
                manager.SetGameContextProvider(contextProvider);
                EditorUtility.SetDirty(manager);
                EditorSceneManager.MarkSceneDirty(scene);
                Require(EditorSceneManager.SaveScene(scene, ScenePath),
                    "Failed to save SampleScene.");
                AssetDatabase.SaveAssets();
                Debug.Log(
                    "[DigitalHuman] STEP 17 Final error and regression audit passed.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
                throw;
            }
        }

        private static void ValidateHierarchy(GameObject digitalHuman)
        {
            Require(digitalHuman.transform.Find("Model") != null,
                "DigitalHuman/Model is missing.");
            Transform trigger = digitalHuman.transform.Find("InteractionTrigger");
            Require(trigger != null, "DigitalHuman/InteractionTrigger is missing.");
            Require(digitalHuman.transform.Find("LookTarget") != null,
                "DigitalHuman/LookTarget is missing.");

            Collider triggerCollider = trigger.GetComponent<Collider>();
            Require(triggerCollider != null && triggerCollider.isTrigger,
                "InteractionTrigger must have a trigger Collider.");
            Require(trigger.GetComponent<PlayerInteractionDetector>() != null,
                "PlayerInteractionDetector is missing from InteractionTrigger.");
        }

        private static void ValidateCoreComponents(GameObject digitalHuman)
        {
            Type[] requiredComponents =
            {
                typeof(Animator),
                typeof(AudioSource),
                typeof(Rigidbody),
                typeof(LookAtController),
                typeof(DialogueController),
                typeof(DigitalHumanController),
                typeof(DigitalHumanAnimator),
                typeof(AIConversationManager),
                typeof(OpenAILLMService),
                typeof(VoiceController),
                typeof(MockVoiceService),
                typeof(GameContextProvider)
            };

            for (int i = 0; i < requiredComponents.Length; i++)
            {
                Require(digitalHuman.GetComponent(requiredComponents[i]) != null,
                    $"{requiredComponents[i].Name} is missing from DigitalHuman.");
            }

            Rigidbody rigidbody = digitalHuman.GetComponent<Rigidbody>();
            Require(rigidbody.isKinematic, "DigitalHuman Rigidbody must be kinematic.");

            AudioSource audioSource = digitalHuman.GetComponent<AudioSource>();
            Require(!audioSource.playOnAwake && !audioSource.loop,
                "AudioSource playback flags are incorrect.");

            Animator animator = digitalHuman.GetComponent<Animator>();
            Require(animator.runtimeAnimatorController != null,
                "Animator Controller is missing.");
            Require(HasBoolParameter(animator, "IsTalking"),
                "Animator IsTalking Bool is missing.");
            Require(HasBoolParameter(animator, "IsListening"),
                "Animator IsListening Bool is missing.");
            Require(HasBoolParameter(animator, "IsThinking"),
                "Animator IsThinking Bool is missing.");

            AIConversationManager manager =
                digitalHuman.GetComponent<AIConversationManager>();
            Require(manager.Persona != null, "AIConversationManager Persona is missing.");
            Require(manager.MaxHistoryMessages >= 10 &&
                    manager.MaxHistoryMessages <= 20,
                "Conversation history limit is outside the intended range.");
        }

        private static void ValidatePlayer(GameObject player)
        {
            Require(player.CompareTag("Player"), "Player tag is incorrect.");
            Require(player.GetComponent<CharacterController>() != null,
                "Player CharacterController is missing.");
            Require(player.GetComponent<DemoPlayerController>() != null,
                "DemoPlayerController is missing.");
        }

        private static void ValidateUi(GameObject canvas)
        {
            DialogueUI dialogueUI = canvas.GetComponent<DialogueUI>();
            Require(dialogueUI != null, "DialogueUI is missing.");
            Require(canvas.GetComponent<Canvas>() != null, "Canvas component is missing.");
            Require(dialogueUI.PlayerInputField != null,
                "Dialogue input field is missing.");
            Require(dialogueUI.SendButton != null, "Dialogue send button is missing.");
            Require(dialogueUI.CloseButton != null, "Dialogue close button is missing.");
            Require(canvas.GetComponentInChildren<TMP_Text>(true) != null,
                "Dialogue text components are missing.");

            EventSystem eventSystem =
                UnityEngine.Object.FindFirstObjectByType<EventSystem>(
                    FindObjectsInactive.Include);
            Require(eventSystem != null, "EventSystem is missing.");
            Require(eventSystem.GetComponent<InputSystemUIInputModule>() != null,
                "InputSystemUIInputModule is missing.");
        }

        private static void ValidateAssets(GameObject digitalHuman)
        {
            DigitalHumanPersona persona =
                AssetDatabase.LoadAssetAtPath<DigitalHumanPersona>(PersonaPath);
            Require(persona != null, "EVA Persona asset is missing.");
            Require(persona.NpcName == "EVA", "EVA Persona identity is incorrect.");
            Require(!string.IsNullOrWhiteSpace(persona.BuildSystemPrompt()),
                "Persona prompt is empty.");
            Require(AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                        AnimatorControllerPath) != null,
                "DigitalHuman Animator Controller asset is missing.");

            OpenAILLMService service =
                digitalHuman.GetComponent<OpenAILLMService>();
            Require(service.ApiKeyEnvironmentVariable == "OPENAI_API_KEY",
                "OpenAILLMService must use the OPENAI_API_KEY environment variable.");
            Require(service.Endpoint == "https://api.openai.com/v1/responses",
                "OpenAILLMService endpoint is incorrect.");
            Require(service.Model == "gpt-5.6-terra",
                "OpenAILLMService model is incorrect.");
        }

        private static void ValidateNoMissingScripts(Scene scene)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            int missingScriptCount = 0;
            for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                Transform[] transforms =
                    roots[rootIndex].GetComponentsInChildren<Transform>(true);
                for (int transformIndex = 0;
                     transformIndex < transforms.Length;
                     transformIndex++)
                {
                    missingScriptCount +=
                        GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
                            transforms[transformIndex].gameObject);
                }
            }

            Require(missingScriptCount == 0,
                $"Scene contains {missingScriptCount} missing script reference(s).");
        }

        private static bool HasBoolParameter(Animator animator, string name)
        {
            AnimatorControllerParameter[] parameters = animator.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].name == name &&
                    parameters[i].type == AnimatorControllerParameterType.Bool)
                {
                    return true;
                }
            }

            return false;
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
