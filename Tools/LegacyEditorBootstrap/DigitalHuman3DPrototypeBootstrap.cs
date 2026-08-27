#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DigitalHuman.Editor
{
    public static class DigitalHuman3DPrototypeBootstrap
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string PrefabPath =
            "Assets/DigitalHuman/Prefabs/DigitalHuman.prefab";
        private const string SkinMaterialPath =
            "Assets/DigitalHuman/Materials/DigitalHumanSkin.mat";
        private const string SuitMaterialPath =
            "Assets/DigitalHuman/Materials/DigitalHumanSuit.mat";
        private const string AccentMaterialPath =
            "Assets/DigitalHuman/Materials/DigitalHumanAccent.mat";

        public static void CreateAndValidate()
        {
            try
            {
                Material skin = GetOrCreateMaterial(
                    SkinMaterialPath,
                    "DigitalHumanSkin",
                    new Color(0.63f, 0.39f, 0.29f, 1f),
                    0.05f,
                    0.48f,
                    false);
                Material suit = GetOrCreateMaterial(
                    SuitMaterialPath,
                    "DigitalHumanSuit",
                    new Color(0.025f, 0.075f, 0.12f, 1f),
                    0.52f,
                    0.78f,
                    false);
                Material accent = GetOrCreateMaterial(
                    AccentMaterialPath,
                    "DigitalHumanAccent",
                    new Color(0.04f, 0.72f, 0.95f, 1f),
                    0.25f,
                    0.82f,
                    true);

                BuildPrefab(skin, suit, accent);
                ConfigureScene();
                ValidatePrefabAndScene();
                AssetDatabase.SaveAssets();
                Debug.Log(
                    "[DigitalHuman] 3D Humanoid Prototype created and connected. " +
                    "Height=1.76m; complete volumetric body; Idle/Talking/FacePlayer enabled.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
                throw;
            }
        }

        private static void BuildPrefab(
            Material skin,
            Material suit,
            Material accent)
        {
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                Transform model = prefabRoot.transform.Find("Model");
                Require(model != null, "Prefab Model container is missing.");

                RemoveComponent<MeshRenderer>(model.gameObject);
                RemoveComponent<MeshFilter>(model.gameObject);
                RemoveComponent<Collider>(model.gameObject);
                Transform existing = model.Find("DigitalHuman_3D");
                if (existing != null)
                {
                    UnityEngine.Object.DestroyImmediate(existing.gameObject);
                }

                Transform characterRoot = CreateJoint(
                    "DigitalHuman_3D", model, Vector3.zero);
                Transform rig = CreateJoint("Rig", characterRoot, Vector3.zero);
                Transform pelvis = CreateJoint(
                    "Pelvis", rig, new Vector3(0f, 0.90f, 0f));
                CreatePart("PelvisMesh", PrimitiveType.Capsule, pelvis,
                    Vector3.zero, new Vector3(0.40f, 0.10f, 0.26f), suit);

                Transform torso = CreateJoint(
                    "Torso", pelvis, new Vector3(0f, 0.31f, 0f));
                CreatePart("TorsoMesh", PrimitiveType.Capsule, torso,
                    Vector3.zero, new Vector3(0.48f, 0.25f, 0.30f), suit);
                CreatePart("ChestCore", PrimitiveType.Sphere, torso,
                    new Vector3(0f, 0.055f, 0.155f),
                    new Vector3(0.12f, 0.12f, 0.045f), accent);
                CreatePart("Neck", PrimitiveType.Capsule, torso,
                    new Vector3(0f, 0.27f, 0f),
                    new Vector3(0.12f, 0.07f, 0.12f), skin);

                Transform head = CreateJoint(
                    "Head", torso, new Vector3(0f, 0.40f, 0f));
                CreatePart("HeadMesh", PrimitiveType.Sphere, head,
                    Vector3.zero, new Vector3(0.34f, 0.36f, 0.34f), skin);
                CreatePart("LeftEye", PrimitiveType.Sphere, head,
                    new Vector3(-0.070f, 0.035f, 0.161f),
                    new Vector3(0.045f, 0.035f, 0.025f), accent);
                CreatePart("RightEye", PrimitiveType.Sphere, head,
                    new Vector3(0.070f, 0.035f, 0.161f),
                    new Vector3(0.045f, 0.035f, 0.025f), accent);

                Transform leftUpperArm = CreateArm(
                    "Left", torso, -0.32f, suit, skin);
                Transform rightUpperArm = CreateArm(
                    "Right", torso, 0.32f, suit, skin);
                CreateLeg("Left", pelvis, -0.14f, suit);
                CreateLeg("Right", pelvis, 0.14f, suit);

                DigitalHumanController controller =
                    prefabRoot.GetComponent<DigitalHumanController>();
                Require(controller != null,
                    "DigitalHumanController is missing from Prefab root.");
                ProceduralHumanoidAnimator proceduralAnimator =
                    prefabRoot.GetComponent<ProceduralHumanoidAnimator>();
                if (proceduralAnimator == null)
                {
                    proceduralAnimator =
                        prefabRoot.AddComponent<ProceduralHumanoidAnimator>();
                }

                proceduralAnimator.Configure(
                    controller,
                    characterRoot,
                    torso,
                    head,
                    leftUpperArm,
                    rightUpperArm);
                EditorUtility.SetDirty(proceduralAnimator);
                ValidateModel(characterRoot.gameObject);
                Require(PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath) != null,
                    "Failed to save DigitalHuman Prefab.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        private static Transform CreateArm(
            string side,
            Transform torso,
            float shoulderX,
            Material suit,
            Material skin)
        {
            Transform upperArm = CreateJoint(
                side + "UpperArm",
                torso,
                new Vector3(shoulderX, 0.18f, 0f));
            CreatePart(side + "UpperArmMesh", PrimitiveType.Capsule, upperArm,
                new Vector3(0f, -0.18f, 0f),
                new Vector3(0.13f, 0.18f, 0.13f), suit);
            Transform forearm = CreateJoint(
                side + "Forearm",
                upperArm,
                new Vector3(0f, -0.36f, 0f));
            CreatePart(side + "ForearmMesh", PrimitiveType.Capsule, forearm,
                new Vector3(0f, -0.16f, 0f),
                new Vector3(0.12f, 0.16f, 0.12f), suit);
            Transform hand = CreateJoint(
                side + "Hand",
                forearm,
                new Vector3(0f, -0.32f, 0f));
            CreatePart(side + "HandMesh", PrimitiveType.Capsule, hand,
                new Vector3(0f, -0.07f, 0f),
                new Vector3(0.13f, 0.09f, 0.12f), skin);
            return upperArm;
        }

        private static void CreateLeg(
            string side,
            Transform pelvis,
            float hipX,
            Material suit)
        {
            Transform upperLeg = CreateJoint(
                side + "UpperLeg",
                pelvis,
                new Vector3(hipX, -0.08f, 0f));
            CreatePart(side + "Thigh", PrimitiveType.Capsule, upperLeg,
                new Vector3(0f, -0.20f, 0f),
                new Vector3(0.18f, 0.20f, 0.19f), suit);
            Transform lowerLeg = CreateJoint(
                side + "LowerLeg",
                upperLeg,
                new Vector3(0f, -0.40f, 0f));
            CreatePart(side + "Shin", PrimitiveType.Capsule, lowerLeg,
                new Vector3(0f, -0.18f, 0f),
                new Vector3(0.15f, 0.19f, 0.16f), suit);
            Transform foot = CreateJoint(
                side + "Foot",
                lowerLeg,
                new Vector3(0f, -0.36f, 0f));
            CreatePart(side + "FootMesh", PrimitiveType.Cube, foot,
                new Vector3(0f, 0f, 0.085f),
                new Vector3(0.19f, 0.12f, 0.34f), suit);
        }

        private static Transform CreateJoint(
            string name,
            Transform parent,
            Vector3 localPosition)
        {
            GameObject joint = new GameObject(name);
            joint.transform.SetParent(parent, false);
            joint.transform.localPosition = localPosition;
            joint.transform.localRotation = Quaternion.identity;
            joint.transform.localScale = Vector3.one;
            return joint.transform;
        }

        private static GameObject CreatePart(
            string name,
            PrimitiveType type,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Material material)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localRotation = Quaternion.identity;
            part.transform.localScale = localScale;
            Collider collider = part.GetComponent<Collider>();
            if (collider != null)
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }

            MeshRenderer renderer = part.GetComponent<MeshRenderer>();
            Require(renderer != null, $"{name} MeshRenderer is missing.");
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;
            return part;
        }

        private static void ConfigureScene()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject digitalHuman = GameObject.Find("DigitalHuman");
            Require(digitalHuman != null, "Scene DigitalHuman instance is missing.");
            Require(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(digitalHuman) ==
                    PrefabPath,
                "Scene DigitalHuman is not connected to the expected Prefab.");

            GameObject canvas = GameObject.Find("DialogueCanvas");
            Require(canvas != null, "DialogueCanvas is missing.");
            Transform panel = canvas.transform.Find("DialoguePanel");
            Require(panel != null, "DialoguePanel is missing.");
            Transform portrait = panel.Find("EVA_Portrait");
            if (portrait != null)
            {
                UnityEngine.Object.DestroyImmediate(portrait.gameObject);
            }

            SetChildStretch(panel, "NPCNameText",
                new Vector2(0.05f, 0.82f), new Vector2(0.93f, 0.96f));
            SetChildStretch(panel, "DialogueText",
                new Vector2(0.05f, 0.34f), new Vector2(0.96f, 0.81f));
            SetChildStretch(panel, "PlayerInputField",
                new Vector2(0.05f, 0.08f), new Vector2(0.75f, 0.28f));

            EditorSceneManager.MarkSceneDirty(scene);
            Require(EditorSceneManager.SaveScene(scene, ScenePath),
                "Failed to save SampleScene.");
        }

        private static void ValidatePrefabAndScene()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Require(prefab != null, "DigitalHuman Prefab is missing.");
            Transform character = prefab.transform.Find("Model/DigitalHuman_3D");
            Require(character != null, "Prefab DigitalHuman_3D is missing.");
            ValidateModel(character.gameObject);
            Require(prefab.GetComponent<ProceduralHumanoidAnimator>() != null,
                "ProceduralHumanoidAnimator is missing from Prefab.");
            Require(prefab.GetComponent<AIConversationManager>() != null &&
                    prefab.GetComponent<OpenAILLMService>() != null &&
                    prefab.GetComponent<VoiceController>() != null &&
                    prefab.GetComponent<DialogueController>() != null &&
                    prefab.GetComponent<LookAtController>() != null,
                "Existing AI, voice, dialogue, or look-at logic was lost.");

            GameObject sceneCharacter = GameObject.Find("DigitalHuman");
            Require(sceneCharacter != null &&
                    sceneCharacter.transform.Find("Model/DigitalHuman_3D") != null,
                "Scene 3D humanoid instance is missing.");
            GameObject canvas = GameObject.Find("DialogueCanvas");
            Require(canvas != null &&
                    canvas.transform.Find("DialoguePanel/EVA_Portrait") == null,
                "The old 2D EVA portrait is still present.");
            ValidateNoMissingScripts(sceneCharacter, "DigitalHuman scene instance");
            ValidateNoMissingScripts(canvas, "DialogueCanvas");
        }

        private static void ValidateModel(GameObject character)
        {
            string[] requiredParts =
            {
                "Head", "Torso", "Pelvis",
                "LeftUpperArm", "RightUpperArm",
                "LeftHand", "RightHand",
                "LeftUpperLeg", "RightUpperLeg",
                "LeftLowerLeg", "RightLowerLeg",
                "LeftFoot", "RightFoot"
            };
            for (int i = 0; i < requiredParts.Length; i++)
            {
                Require(FindRecursive(character.transform, requiredParts[i]) != null,
                    $"3D humanoid part is missing: {requiredParts[i]}");
            }

            Require(character.GetComponentsInChildren<SpriteRenderer>(true).Length == 0,
                "3D character contains a SpriteRenderer.");
            MeshRenderer[] renderers =
                character.GetComponentsInChildren<MeshRenderer>(true);
            Require(renderers.Length >= 18,
                $"3D humanoid has too few volumetric renderers: {renderers.Length}");

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            Require(bounds.size.y >= 1.70f && bounds.size.y <= 1.82f,
                $"3D humanoid height is outside target range: {bounds.size.y:F3}");
            Require(bounds.size.x >= 0.70f && bounds.size.z >= 0.25f,
                $"3D humanoid lacks full volume: size={bounds.size}");
            MeshFilter[] filters = character.GetComponentsInChildren<MeshFilter>(true);
            Require(filters.Length == renderers.Length,
                "One or more 3D body parts has no MeshFilter.");
            for (int i = 0; i < filters.Length; i++)
            {
                Require(filters[i].sharedMesh != null &&
                        filters[i].sharedMesh.vertexCount >= 8,
                    $"Invalid volumetric mesh on {filters[i].name}.");
            }

            ValidateNoMissingScripts(character, "DigitalHuman_3D");
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

        private static Material GetOrCreateMaterial(
            string path,
            string name,
            Color color,
            float metallic,
            float smoothness,
            bool emission)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                Require(shader != null, "URP Lit shader is unavailable.");
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            if (emission)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * 1.8f);
            }
            else
            {
                material.DisableKeyword("_EMISSION");
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        private static void SetChildStretch(
            Transform parent,
            string childName,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            Transform child = parent.Find(childName);
            Require(child != null, $"{childName} is missing.");
            RectTransform rect = (RectTransform)child;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            EditorUtility.SetDirty(rect);
        }

        private static void RemoveComponent<T>(GameObject gameObject)
            where T : Component
        {
            T component = gameObject.GetComponent<T>();
            if (component != null)
            {
                UnityEngine.Object.DestroyImmediate(component);
            }
        }

        private static void ValidateNoMissingScripts(GameObject root, string label)
        {
            int missing = 0;
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                missing += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
                    transforms[i].gameObject);
            }

            Require(missing == 0,
                $"{label} contains {missing} missing script reference(s).");
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
