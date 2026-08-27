#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DigitalHuman.Editor
{
    public static class DigitalHumanStep18Bootstrap
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string PrefabPath =
            "Assets/DigitalHuman/Prefabs/DigitalHuman.prefab";

        public static void CreateAndValidatePrefab()
        {
            try
            {
                Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                GameObject digitalHuman = GameObject.Find("DigitalHuman");
                GameObject player = GameObject.FindWithTag("Player");
                DialogueUI dialogueUI =
                    UnityEngine.Object.FindFirstObjectByType<DialogueUI>(
                        FindObjectsInactive.Include);
                Require(digitalHuman != null, "DigitalHuman root is missing.");
                Require(player != null, "Player is missing.");
                Require(dialogueUI != null, "DialogueUI is missing.");

                string currentPrefabPath =
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(digitalHuman);
                if (string.IsNullOrEmpty(currentPrefabPath))
                {
                    SaveAndConnectNewPrefab(
                        digitalHuman,
                        player,
                        dialogueUI);
                }
                else
                {
                    Require(currentPrefabPath == PrefabPath,
                        $"DigitalHuman is connected to an unexpected Prefab: " +
                        currentPrefabPath);
                    RestoreSceneReferences(digitalHuman, player, dialogueUI);
                }

                ValidatePrefabAsset();
                ValidateSceneInstance(digitalHuman, player, dialogueUI);
                EditorSceneManager.MarkSceneDirty(scene);
                Require(EditorSceneManager.SaveScene(scene, ScenePath),
                    "Failed to save SampleScene.");
                AssetDatabase.SaveAssets();
                Debug.Log(
                    "[DigitalHuman] STEP 18 DigitalHuman Prefab creation completed.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
                throw;
            }
        }

        private static void SaveAndConnectNewPrefab(
            GameObject digitalHuman,
            GameObject player,
            DialogueUI dialogueUI)
        {
            DialogueController dialogueController =
                digitalHuman.GetComponent<DialogueController>();
            AIConversationManager conversationManager =
                digitalHuman.GetComponent<AIConversationManager>();
            GameContextProvider contextProvider =
                digitalHuman.GetComponent<GameContextProvider>();
            PlayerInteractionDetector detector =
                digitalHuman.transform.Find("InteractionTrigger")
                    .GetComponent<PlayerInteractionDetector>();
            Require(dialogueController != null, "DialogueController is missing.");
            Require(conversationManager != null, "AIConversationManager is missing.");
            Require(contextProvider != null, "GameContextProvider is missing.");
            Require(detector != null, "PlayerInteractionDetector is missing.");

            bool saveSucceeded = false;
            try
            {
                dialogueController.Configure(detector, null, conversationManager);
                contextProvider.Configure(
                    null,
                    contextProvider.CurrentLocation,
                    contextProvider.PlayerHealth,
                    contextProvider.CurrentObjective,
                    true,
                    true);

                PrefabUtility.SaveAsPrefabAssetAndConnect(
                    digitalHuman,
                    PrefabPath,
                    InteractionMode.AutomatedAction,
                    out saveSucceeded);
            }
            finally
            {
                RestoreSceneReferences(digitalHuman, player, dialogueUI);
            }

            Require(saveSucceeded, "Failed to create DigitalHuman Prefab.");
        }

        private static void RestoreSceneReferences(
            GameObject digitalHuman,
            GameObject player,
            DialogueUI dialogueUI)
        {
            PlayerInteractionDetector detector =
                digitalHuman.transform.Find("InteractionTrigger")
                    .GetComponent<PlayerInteractionDetector>();
            DialogueController dialogueController =
                digitalHuman.GetComponent<DialogueController>();
            AIConversationManager conversationManager =
                digitalHuman.GetComponent<AIConversationManager>();
            OpenAILLMService llmService =
                digitalHuman.GetComponent<OpenAILLMService>();
            GameContextProvider contextProvider =
                digitalHuman.GetComponent<GameContextProvider>();
            DigitalHumanController stateController =
                digitalHuman.GetComponent<DigitalHumanController>();
            DigitalHumanAnimator animationController =
                digitalHuman.GetComponent<DigitalHumanAnimator>();
            VoiceController voiceController =
                digitalHuman.GetComponent<VoiceController>();
            MockVoiceService voiceService =
                digitalHuman.GetComponent<MockVoiceService>();
            AudioSource audioSource = digitalHuman.GetComponent<AudioSource>();
            Animator animator = digitalHuman.GetComponent<Animator>();
            LookAtController lookAtController =
                digitalHuman.GetComponent<LookAtController>();

            Require(detector != null, "PlayerInteractionDetector is missing.");
            Require(dialogueController != null, "DialogueController is missing.");
            Require(conversationManager != null, "AIConversationManager is missing.");
            Require(llmService != null, "OpenAILLMService is missing.");
            Require(contextProvider != null, "GameContextProvider is missing.");
            Require(stateController != null, "DigitalHumanController is missing.");
            Require(animationController != null, "DigitalHumanAnimator is missing.");
            Require(voiceController != null, "VoiceController is missing.");
            Require(voiceService != null, "MockVoiceService is missing.");
            Require(audioSource != null, "AudioSource is missing.");
            Require(animator != null, "Animator is missing.");
            Require(lookAtController != null, "LookAtController is missing.");

            conversationManager.Configure(
                conversationManager.Persona,
                llmService,
                16);
            contextProvider.Configure(
                player.transform,
                contextProvider.CurrentLocation,
                contextProvider.PlayerHealth,
                contextProvider.CurrentObjective,
                true,
                true);
            conversationManager.SetGameContextProvider(contextProvider);
            dialogueController.Configure(detector, dialogueUI, conversationManager);
            stateController.Configure(detector, dialogueController, 0.65f);
            animationController.Configure(stateController, animator);
            voiceService.Configure(0.5f);
            voiceController.Configure(
                dialogueController,
                stateController,
                audioSource,
                voiceService);
            lookAtController.Configure(detector, digitalHuman.transform, 5f);

            EditorUtility.SetDirty(dialogueController);
            EditorUtility.SetDirty(conversationManager);
            EditorUtility.SetDirty(contextProvider);
            EditorUtility.SetDirty(stateController);
            EditorUtility.SetDirty(animationController);
            EditorUtility.SetDirty(voiceController);
            EditorUtility.SetDirty(lookAtController);
        }

        private static void ValidatePrefabAsset()
        {
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Require(prefab != null, "DigitalHuman Prefab asset is missing.");
            Require(prefab.name == "DigitalHuman", "Prefab root name is incorrect.");
            Require(prefab.transform.Find("Model") != null,
                "Prefab Model child is missing.");
            Require(prefab.transform.Find("InteractionTrigger") != null,
                "Prefab InteractionTrigger child is missing.");
            Require(prefab.transform.Find("LookTarget") != null,
                "Prefab LookTarget child is missing.");

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
                Require(prefab.GetComponent(requiredComponents[i]) != null,
                    $"Prefab is missing {requiredComponents[i].Name}.");
            }

            DialogueController dialogueController =
                prefab.GetComponent<DialogueController>();
            SerializedObject dialogueObject = new SerializedObject(dialogueController);
            Require(
                dialogueObject.FindProperty("interactionDetector").objectReferenceValue != null,
                "Prefab DialogueController lost its internal detector reference.");
            Require(
                dialogueObject.FindProperty("conversationServiceComponent")
                    .objectReferenceValue != null,
                "Prefab DialogueController lost its AI manager reference.");
            Require(
                dialogueObject.FindProperty("dialogueUI").objectReferenceValue == null,
                "Prefab must not contain a scene DialogueUI reference.");

            GameContextProvider contextProvider =
                prefab.GetComponent<GameContextProvider>();
            SerializedObject contextObject = new SerializedObject(contextProvider);
            Require(
                contextObject.FindProperty("playerTransform").objectReferenceValue == null,
                "Prefab must not contain a scene Player reference.");

            AIConversationManager manager =
                prefab.GetComponent<AIConversationManager>();
            SerializedObject managerObject = new SerializedObject(manager);
            Require(managerObject.FindProperty("persona").objectReferenceValue != null,
                "Prefab lost its Persona reference.");
            Require(
                managerObject.FindProperty("responseProviderComponent")
                    .objectReferenceValue != null,
                "Prefab lost its LLM Service reference.");
            Require(
                managerObject.FindProperty("gameContextProviderComponent")
                    .objectReferenceValue != null,
                "Prefab lost its Game Context reference.");

            ValidateNoMissingScripts(prefab, "Prefab");
        }

        private static void ValidateSceneInstance(
            GameObject digitalHuman,
            GameObject player,
            DialogueUI dialogueUI)
        {
            Require(PrefabUtility.IsPartOfPrefabInstance(digitalHuman),
                "Scene DigitalHuman is not connected to its Prefab.");
            Require(
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(digitalHuman) ==
                PrefabPath,
                "Scene DigitalHuman is connected to the wrong Prefab.");

            DialogueController dialogueController =
                digitalHuman.GetComponent<DialogueController>();
            SerializedObject dialogueObject = new SerializedObject(dialogueController);
            Require(
                dialogueObject.FindProperty("dialogueUI").objectReferenceValue ==
                dialogueUI,
                "Scene instance lost its DialogueUI override.");

            GameContextProvider contextProvider =
                digitalHuman.GetComponent<GameContextProvider>();
            SerializedObject contextObject = new SerializedObject(contextProvider);
            Require(
                contextObject.FindProperty("playerTransform").objectReferenceValue ==
                player.transform,
                "Scene instance lost its Player override.");
            ValidateNoMissingScripts(digitalHuman, "Scene DigitalHuman instance");
        }

        private static void ValidateNoMissingScripts(GameObject root, string label)
        {
            int missingScriptCount = 0;
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                missingScriptCount +=
                    GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
                        transforms[i].gameObject);
            }

            Require(missingScriptCount == 0,
                $"{label} contains {missingScriptCount} missing script reference(s).");
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
