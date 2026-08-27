#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DigitalHuman.Editor
{
    public static class DigitalHumanStep3Bootstrap
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string RootName = "DigitalHuman";
        private const string SmokeStateKey = "DigitalHuman.Step3.SmokeState";

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

        public static void CreateNpcBase()
        {
            try
            {
                Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                GameObject existing = GameObject.Find(RootName);
                if (existing != null)
                {
                    UnityEngine.Object.DestroyImmediate(existing);
                }

                GameObject root = new GameObject(RootName);
                root.transform.position = new Vector3(0f, 1f, 3f);

                Animator animator = root.AddComponent<Animator>();
                animator.applyRootMotion = false;

                AudioSource audioSource = root.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 1f;

                Rigidbody rigidbody = root.AddComponent<Rigidbody>();
                rigidbody.isKinematic = true;
                rigidbody.useGravity = false;

                GameObject model = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                model.name = "Model";
                model.transform.SetParent(root.transform, false);
                Collider modelCollider = model.GetComponent<Collider>();
                if (modelCollider != null)
                {
                    UnityEngine.Object.DestroyImmediate(modelCollider);
                }

                GameObject interactionTrigger = new GameObject("InteractionTrigger");
                interactionTrigger.transform.SetParent(root.transform, false);
                SphereCollider triggerCollider = interactionTrigger.AddComponent<SphereCollider>();
                triggerCollider.isTrigger = true;
                triggerCollider.radius = 3f;

                GameObject lookTarget = new GameObject("LookTarget");
                lookTarget.transform.SetParent(root.transform, false);
                lookTarget.transform.localPosition = new Vector3(0f, 1.6f, 0f);

                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene, ScenePath))
                {
                    throw new InvalidOperationException($"Failed to save scene: {ScenePath}");
                }

                AssetDatabase.SaveAssets();
                Debug.Log("[DigitalHuman] STEP 3 NPC base created and SampleScene saved.");
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
                ValidateNpcBase();
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
                    ValidateNpcBase();
                    SessionState.SetInt(SmokeStateKey, 2);
                    Debug.Log("[DigitalHuman] STEP 3 Play Mode smoke test passed.");
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    SessionState.SetInt(SmokeStateKey, -1);
                }
                finally
                {
                    EditorApplication.ExitPlaymode();
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

        private static void ValidateNpcBase()
        {
            GameObject root = GameObject.Find(RootName);
            Require(root != null, "DigitalHuman root is missing.");
            Require(root.GetComponent<Animator>() != null, "Animator is missing.");
            Require(root.GetComponent<AudioSource>() != null, "AudioSource is missing.");

            Rigidbody rigidbody = root.GetComponent<Rigidbody>();
            Require(rigidbody != null && rigidbody.isKinematic && !rigidbody.useGravity,
                "Kinematic Rigidbody is not configured.");

            Transform model = root.transform.Find("Model");
            Transform interactionTrigger = root.transform.Find("InteractionTrigger");
            Transform lookTarget = root.transform.Find("LookTarget");
            Require(model != null, "Model child is missing.");
            Require(interactionTrigger != null, "InteractionTrigger child is missing.");
            Require(lookTarget != null, "LookTarget child is missing.");

            SphereCollider trigger = interactionTrigger.GetComponent<SphereCollider>();
            Require(trigger != null && trigger.isTrigger && Mathf.Approximately(trigger.radius, 3f),
                "Interaction trigger is not configured.");
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
