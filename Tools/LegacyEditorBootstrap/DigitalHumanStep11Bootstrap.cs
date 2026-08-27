#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DigitalHuman.Editor
{
    public static class DigitalHumanStep11Bootstrap
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string PersonaPath =
            "Assets/DigitalHuman/ScriptableObjects/EVA Persona.asset";
        private const string SmokeStateKey = "DigitalHuman.Step11.SmokeState";

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

        public static void ConfigureProject()
        {
            try
            {
                DigitalHumanPersona persona =
                    AssetDatabase.LoadAssetAtPath<DigitalHumanPersona>(PersonaPath);
                if (persona == null)
                {
                    persona = ScriptableObject.CreateInstance<DigitalHumanPersona>();
                    AssetDatabase.CreateAsset(persona, PersonaPath);
                }

                persona.Configure(
                    "EVA",
                    "AI research assistant",
                    "Calm, intelligent, curious, and empathetic.",
                    "An AI-powered digital human living inside a virtual research facility.",
                    "Use short, natural, conversational sentences.",
                    "You understand the research facility and can discuss what the player observes.",
                    "Help the player understand the environment and feel welcome.",
                    "Treat the player as a trusted visitor you are meeting in person.");
                EditorUtility.SetDirty(persona);
                AssetDatabase.SaveAssets();

                Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                Require(EditorSceneManager.SaveScene(scene, ScenePath),
                    "Failed to save SampleScene.");
                Debug.Log("[DigitalHuman] STEP 11 NPC Persona asset setup completed.");
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
                Require(
                    AssetDatabase.LoadAssetAtPath<DigitalHumanPersona>(PersonaPath) != null,
                    "EVA Persona asset is missing.");
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
                    DigitalHumanPersona persona =
                        AssetDatabase.LoadAssetAtPath<DigitalHumanPersona>(PersonaPath);
                    Require(persona != null, "EVA Persona asset is unavailable in Play Mode.");
                    Require(persona.NpcName == "EVA", "Persona NPC name is incorrect.");
                    Require(persona.NpcRole == "AI research assistant",
                        "Persona role is incorrect.");

                    string prompt = persona.BuildSystemPrompt();
                    Require(prompt.Contains("You are EVA"), "Prompt omitted the NPC identity.");
                    Require(prompt.Contains("Calm, intelligent"),
                        "Prompt omitted personality.");
                    Require(prompt.Contains("Stay in character"),
                        "Prompt omitted character constraints.");
                    Require(prompt.Contains("Do not describe yourself as a chatbot"),
                        "Prompt omitted chatbot identity protection.");
                    Require(prompt.Contains("Keep responses concise"),
                        "Prompt omitted speaking guidance.");

                    SessionState.SetInt(SmokeStateKey, 2);
                    Debug.Log("[DigitalHuman] STEP 11 Play Mode NPC Persona test passed.");
                    EditorApplication.ExitPlaymode();
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
                EditorApplication.delayCall += () => EditorApplication.Exit(exitCode);
            }
        }

        private static void FailSmokeTest(Exception exception)
        {
            Debug.LogException(exception);
            SessionState.SetInt(SmokeStateKey, -1);
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
