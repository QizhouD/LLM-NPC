#if UNITY_EDITOR
using System;
using TMPro;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace DigitalHuman.Editor
{
    public static class DigitalHumanAgentDemoBootstrap
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string PrefabPath =
            "Assets/DigitalHuman/Prefabs/DigitalHuman.prefab";
        private const string NavMeshPath =
            "Assets/DigitalHuman/Scenes/AgentPlatformNavMesh.asset";
        private const string PlatformMaterialPath =
            "Assets/DigitalHuman/Materials/AgentPlatform.mat";
        private const string ObstacleMaterialPath =
            "Assets/DigitalHuman/Materials/AgentObstacle.mat";
        private const string ChineseFontAssetPath =
            "Assets/DigitalHuman/Fonts/SimHeiDynamicSDF.asset";

        public static void ConfigureAndValidate()
        {
            try
            {
                EnsurePickupTag();
                ConfigurePrefab();

                Scene scene = EditorSceneManager.OpenScene(
                    ScenePath,
                    OpenSceneMode.Single);
                Material platformMaterial = GetOrCreateMaterial(
                    PlatformMaterialPath,
                    new Color(0.12f, 0.18f, 0.22f, 1f),
                    0.18f,
                    0.58f);
                Material obstacleMaterial = GetOrCreateMaterial(
                    ObstacleMaterialPath,
                    new Color(0.24f, 0.28f, 0.34f, 1f),
                    0.45f,
                    0.68f);
                TMP_FontAsset chineseFont = GetOrCreateChineseFont();

                NavMeshSurface surface = CreateAgentPlatform(
                    platformMaterial,
                    obstacleMaterial);
                CreatePickupProps();
                ConfigureSceneCharacter();
                ConfigureCompactChatUi(chineseFont);
                BakeAndSaveNavMesh(surface);
                ValidateScene(surface);

                EditorSceneManager.MarkSceneDirty(scene);
                Require(EditorSceneManager.SaveScene(scene, ScenePath),
                    "Failed to save SampleScene.");
                AssetDatabase.SaveAssets();
                Debug.Log(
                    "[AgentNPC] 3D Agent NPC Demo configured: compact chat, " +
                    "AgentPlatform, baked NavMesh, pickups, movement and commands.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
                throw;
            }
        }

        private static void ConfigurePrefab()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                NavMeshAgent agent = root.GetComponent<NavMeshAgent>();
                if (agent == null)
                {
                    agent = root.AddComponent<NavMeshAgent>();
                }

                agent.agentTypeID = 0;
                agent.radius = 0.28f;
                agent.height = 1.76f;
                agent.baseOffset = 0f;
                agent.speed = 2.6f;
                agent.angularSpeed = 540f;
                agent.acceleration = 10f;
                agent.stoppingDistance = 0.72f;
                agent.autoBraking = true;
                agent.autoRepath = true;
                agent.updatePosition = true;
                agent.updateRotation = true;
                agent.updateUpAxis = true;
                agent.obstacleAvoidanceType =
                    ObstacleAvoidanceType.HighQualityObstacleAvoidance;

                LookAtController lookAt = root.GetComponent<LookAtController>();
                DialogueController dialogue = root.GetComponent<DialogueController>();
                Require(lookAt != null && dialogue != null,
                    "Existing LookAtController or DialogueController is missing.");

                AgentMovement movement = root.GetComponent<AgentMovement>();
                if (movement == null)
                {
                    movement = root.AddComponent<AgentMovement>();
                }
                movement.Configure(agent, lookAt);

                Transform rig = root.transform.Find(
                    "Model/DigitalHuman_3D/Rig/Pelvis");
                Transform rightHand = root.transform.Find(
                    "Model/DigitalHuman_3D/Rig/Pelvis/Torso/" +
                    "RightUpperArm/RightForearm/RightHand");
                Transform leftLeg = rig != null
                    ? rig.Find("LeftUpperLeg")
                    : null;
                Transform rightLeg = rig != null
                    ? rig.Find("RightUpperLeg")
                    : null;
                Require(rightHand != null && leftLeg != null && rightLeg != null,
                    "3D humanoid hand or leg joints are missing.");

                AgentInteraction interaction = root.GetComponent<AgentInteraction>();
                if (interaction == null)
                {
                    interaction = root.AddComponent<AgentInteraction>();
                }
                interaction.Configure(movement, rightHand);

                AgentCommandController commands =
                    root.GetComponent<AgentCommandController>();
                if (commands == null)
                {
                    commands = root.AddComponent<AgentCommandController>();
                }
                commands.Configure(dialogue, movement, interaction);
                dialogue.ConfigureAgentCommands(commands);

                ProceduralHumanoidAnimator animator =
                    root.GetComponent<ProceduralHumanoidAnimator>();
                Require(animator != null,
                    "ProceduralHumanoidAnimator is missing.");
                animator.ConfigureMovement(movement, leftLeg, rightLeg);

                EditorUtility.SetDirty(agent);
                EditorUtility.SetDirty(movement);
                EditorUtility.SetDirty(interaction);
                EditorUtility.SetDirty(commands);
                EditorUtility.SetDirty(dialogue);
                EditorUtility.SetDirty(animator);
                Require(PrefabUtility.SaveAsPrefabAsset(root, PrefabPath) != null,
                    "Failed to save DigitalHuman Prefab.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static NavMeshSurface CreateAgentPlatform(
            Material platformMaterial,
            Material obstacleMaterial)
        {
            DestroySceneObject("AgentPlatform");
            DestroySceneObject("GroundPlatform");

            GameObject platform = new GameObject("AgentPlatform");
            platform.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            BoxCollider platformCollider = platform.AddComponent<BoxCollider>();
            platformCollider.center = new Vector3(0f, -0.25f, 0f);
            platformCollider.size = new Vector3(22f, 0.5f, 16f);

            NavMeshSurface surface = platform.AddComponent<NavMeshSurface>();
            surface.agentTypeID = 0;
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = ~0;
            surface.ignoreNavMeshAgent = true;
            surface.ignoreNavMeshObstacle = true;
            surface.overrideVoxelSize = true;
            surface.voxelSize = 0.08f;
            surface.overrideTileSize = true;
            surface.tileSize = 128;
            surface.minRegionArea = 0.5f;

            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "PlatformVisual";
            visual.transform.SetParent(platform.transform, false);
            visual.transform.localPosition = new Vector3(0f, -0.25f, 0f);
            visual.transform.localScale = new Vector3(22f, 0.5f, 16f);
            UnityEngine.Object.DestroyImmediate(visual.GetComponent<Collider>());
            visual.GetComponent<MeshRenderer>().sharedMaterial = platformMaterial;

            GameObject obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obstacle.name = "NavigationObstacle";
            obstacle.transform.SetParent(platform.transform, false);
            obstacle.transform.localPosition = new Vector3(0f, 0.75f, 0f);
            obstacle.transform.localScale = new Vector3(1.8f, 1.5f, 1.8f);
            obstacle.GetComponent<MeshRenderer>().sharedMaterial = obstacleMaterial;
            PlaceSurface placeSurface = obstacle.GetComponent<PlaceSurface>();
            if (placeSurface == null)
            {
                placeSurface = obstacle.AddComponent<PlaceSurface>();
            }

            placeSurface.Configure("obstacle");

            StaticEditorFlags navigationFlags =
                StaticEditorFlags.BatchingStatic |
                StaticEditorFlags.OccluderStatic |
                StaticEditorFlags.OccludeeStatic;
            GameObjectUtility.SetStaticEditorFlags(platform, navigationFlags);
            GameObjectUtility.SetStaticEditorFlags(visual, navigationFlags);
            GameObjectUtility.SetStaticEditorFlags(obstacle, navigationFlags);
            return surface;
        }

        private static void CreatePickupProps()
        {
            DestroySceneObject("AgentProps");
            GameObject root = new GameObject("AgentProps");
            CreatePickup(
                root.transform,
                "RedBox",
                "red box",
                "Red",
                new Color(0.88f, 0.07f, 0.06f, 1f),
                new Vector3(4f, 0.35f, 0f));
            CreatePickup(
                root.transform,
                "BlueBox",
                "blue box",
                "Blue",
                new Color(0.04f, 0.24f, 0.92f, 1f),
                new Vector3(3.2f, 0.35f, -3.2f));
            CreatePickup(
                root.transform,
                "GreenBox",
                "green box",
                "Green",
                new Color(0.05f, 0.72f, 0.16f, 1f),
                new Vector3(-1.3f, 0.35f, 3.7f));
        }

        private static void CreatePickup(
            Transform parent,
            string objectName,
            string displayName,
            string colorName,
            Color color,
            Vector3 position)
        {
            GameObject item = GameObject.CreatePrimitive(PrimitiveType.Cube);
            item.name = objectName;
            item.tag = "Pickup";
            item.transform.SetParent(parent, true);
            item.transform.position = position;
            item.transform.localScale = Vector3.one * 0.7f;
            Material material = GetOrCreateMaterial(
                $"Assets/DigitalHuman/Materials/{objectName}.mat",
                color,
                0.12f,
                0.48f);
            item.GetComponent<MeshRenderer>().sharedMaterial = material;
            PickupItem pickup = item.AddComponent<PickupItem>();
            pickup.Configure(displayName, colorName);
            EditorUtility.SetDirty(pickup);
        }

        private static void ConfigureSceneCharacter()
        {
            GameObject npc = GameObject.Find("DigitalHuman");
            Require(npc != null, "DigitalHuman scene instance is missing.");
            npc.transform.SetPositionAndRotation(
                new Vector3(-4f, 0f, 0f),
                Quaternion.LookRotation(Vector3.right, Vector3.up));
            PrefabUtility.RecordPrefabInstancePropertyModifications(npc.transform);

            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                player.transform.position = new Vector3(-4f, 1.08f, -2.4f);
                PrefabUtility.RecordPrefabInstancePropertyModifications(player.transform);
            }
        }

        private static void ConfigureCompactChatUi(TMP_FontAsset chineseFont)
        {
            DialogueUI dialogueUI = UnityEngine.Object.FindFirstObjectByType<DialogueUI>();
            Require(dialogueUI != null, "DialogueUI is missing.");
            CommandChatLayout.EnsureConfigured(dialogueUI, chineseFont);
        }

        private static void BakeAndSaveNavMesh(NavMeshSurface surface)
        {
            Require(surface != null, "NavMeshSurface is missing.");
            surface.RemoveData();
            surface.navMeshData = null;
            if (AssetDatabase.LoadAssetAtPath<NavMeshData>(NavMeshPath) != null)
            {
                Require(AssetDatabase.DeleteAsset(NavMeshPath),
                    "Failed to replace the previous NavMesh asset.");
            }

            surface.BuildNavMesh();
            NavMeshData data = surface.navMeshData;
            Require(data != null, "NavMesh baking returned no data.");
            AssetDatabase.CreateAsset(data, NavMeshPath);
            AssetDatabase.SaveAssets();
            surface.navMeshData = AssetDatabase.LoadAssetAtPath<NavMeshData>(NavMeshPath);
            surface.RemoveData();
            surface.AddData();
            EditorUtility.SetDirty(surface);
        }

        private static void ValidateScene(NavMeshSurface surface)
        {
            GameObject npc = GameObject.Find("DigitalHuman");
            Require(npc != null, "DigitalHuman is missing.");
            Require(npc.GetComponent<NavMeshAgent>() != null &&
                    npc.GetComponent<AgentMovement>() != null &&
                    npc.GetComponent<AgentInteraction>() != null &&
                    npc.GetComponent<AgentCommandController>() != null,
                "Agent navigation or command components are missing.");
            Require(surface.navMeshData != null &&
                    AssetDatabase.Contains(surface.navMeshData),
                "Baked NavMesh data was not saved as an asset.");
            Require(NavMesh.SamplePosition(
                    npc.transform.position,
                    out NavMeshHit startHit,
                    0.8f,
                    NavMesh.AllAreas),
                "DigitalHuman is not on the baked NavMesh.");

            string[] itemNames = { "RedBox", "BlueBox", "GreenBox" };
            for (int i = 0; i < itemNames.Length; i++)
            {
                GameObject item = GameObject.Find(itemNames[i]);
                Require(item != null && item.CompareTag("Pickup") &&
                        item.GetComponent<PickupItem>() != null &&
                        item.GetComponent<Collider>() != null,
                    $"Pickup item is invalid: {itemNames[i]}");
            }

            GameObject redBox = GameObject.Find("RedBox");
            AgentInteraction interaction = npc.GetComponent<AgentInteraction>();
            Vector3 approach = interaction.CalculateApproachPoint(redBox);
            NavMeshPath path = new NavMeshPath();
            Require(NavMesh.CalculatePath(
                    startHit.position,
                    approach,
                    NavMesh.AllAreas,
                    path) &&
                    path.status == NavMeshPathStatus.PathComplete,
                "No complete NavMesh path exists from the NPC to RedBox.");
            Require(path.corners.Length >= 3,
                "The RedBox route does not navigate around the test obstacle.");

            DialogueUI ui = UnityEngine.Object.FindFirstObjectByType<DialogueUI>();
            RectTransform panel = (RectTransform)ui.transform.Find("DialoguePanel");
            Vector2 anchorSize = panel.anchorMax - panel.anchorMin;
            Require(ui.GetComponent<CommandChatLayout>() != null,
                "CommandChatLayout is missing from DialogueCanvas.");
            Require(anchorSize.x >= 0.70f && anchorSize.x <= 0.90f &&
                    anchorSize.y >= 0.22f && anchorSize.y <= 0.32f,
                $"Command chat panel is not usable: anchor size={anchorSize}.");
            CanvasScaler scaler = ui.GetComponent<CanvasScaler>();
            Require(scaler.referenceResolution == new Vector2(1920f, 1080f),
                "CanvasScaler reference resolution is not 1920x1080.");
            TMP_FontAsset chineseFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                ChineseFontAssetPath);
            Require(chineseFont != null,
                "Dynamic Chinese TMP font asset is missing.");
            TMP_Text[] uiText = ui.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < uiText.Length; i++)
            {
                Require(uiText[i].font == chineseFont,
                    $"Chinese TMP font is not assigned to {uiText[i].name}.");
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Require(prefab != null &&
                    prefab.GetComponent<AIConversationManager>() != null &&
                    prefab.GetComponent<OpenAILLMService>() != null &&
                    prefab.GetComponent<VoiceController>() != null &&
                    prefab.GetComponent<DialogueController>() != null,
                "Existing AI, LLM, voice or dialogue components were lost.");
            ValidateNoMissingScripts(npc);
            ValidateNoMissingScripts(ui.gameObject);
        }

        private static Material GetOrCreateMaterial(
            string path,
            Color color,
            float metallic,
            float smoothness)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                Require(shader != null, "URP Lit shader is unavailable.");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static TMP_FontAsset GetOrCreateChineseFont()
        {
            TMP_FontAsset fontAsset =
                AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(ChineseFontAssetPath);
            if (fontAsset != null)
            {
                return fontAsset;
            }

            if (!AssetDatabase.IsValidFolder("Assets/DigitalHuman/Fonts"))
            {
                AssetDatabase.CreateFolder("Assets/DigitalHuman", "Fonts");
            }

            const string systemFontPath = @"C:\Windows\Fonts\simhei.ttf";
            Require(System.IO.File.Exists(systemFontPath),
                "A Chinese system font was not found at C:/Windows/Fonts/simhei.ttf.");
            fontAsset = TMP_FontAsset.CreateFontAsset(
                systemFontPath,
                0,
                90,
                9,
                GlyphRenderMode.SDFAA,
                1024,
                1024);
            Require(fontAsset != null,
                "Failed to create the dynamic Chinese TMP font asset.");
            fontAsset.name = "SimHei Dynamic SDF";
            AssetDatabase.CreateAsset(fontAsset, ChineseFontAssetPath);

            Texture2D[] atlases = fontAsset.atlasTextures;
            for (int i = 0; i < atlases.Length; i++)
            {
                if (atlases[i] != null && !AssetDatabase.Contains(atlases[i]))
                {
                    atlases[i].name = $"SimHei Atlas {i}";
                    AssetDatabase.AddObjectToAsset(atlases[i], fontAsset);
                }
            }

            if (fontAsset.material != null &&
                !AssetDatabase.Contains(fontAsset.material))
            {
                fontAsset.material.name = "SimHei Atlas Material";
                AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            }

            EditorUtility.SetDirty(fontAsset);
            AssetDatabase.SaveAssets();
            return fontAsset;
        }

        private static void EnsurePickupTag()
        {
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(
                "ProjectSettings/TagManager.asset");
            Require(assets.Length > 0, "TagManager.asset is unavailable.");
            SerializedObject tagManager = new SerializedObject(assets[0]);
            SerializedProperty tags = tagManager.FindProperty("tags");
            for (int i = 0; i < tags.arraySize; i++)
            {
                if (tags.GetArrayElementAtIndex(i).stringValue == "Pickup")
                {
                    return;
                }
            }

            int index = tags.arraySize;
            tags.InsertArrayElementAtIndex(index);
            tags.GetArrayElementAtIndex(index).stringValue = "Pickup";
            tagManager.ApplyModifiedProperties();
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

        private static void DestroySceneObject(string name)
        {
            GameObject existing = GameObject.Find(name);
            if (existing != null)
            {
                UnityEngine.Object.DestroyImmediate(existing);
            }
        }

        private static void ValidateNoMissingScripts(GameObject root)
        {
            int missing = 0;
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                missing += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
                    transforms[i].gameObject);
            }

            Require(missing == 0,
                $"{root.name} contains {missing} missing scripts.");
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
