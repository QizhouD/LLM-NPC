#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DigitalHuman.Editor
{
    public static class DigitalHumanEnvironmentBootstrap
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string MaterialPath =
            "Assets/DigitalHuman/Materials/DigitalHumanPlatform.mat";

        public static void ConfigureAndValidate()
        {
            try
            {
                Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                ConfigureLoadedScene(scene);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(1);
                }

                throw;
            }
        }

        private static void ConfigureLoadedScene(Scene scene)
        {
            GameObject environment = GameObject.Find("Environment");
            if (environment == null)
            {
                environment = new GameObject("Environment");
            }

            Transform existingPlatform = environment.transform.Find("GroundPlatform");
            GameObject platform;
            if (existingPlatform == null)
            {
                platform = GameObject.CreatePrimitive(PrimitiveType.Cube);
                platform.name = "GroundPlatform";
                platform.transform.SetParent(environment.transform, false);
            }
            else
            {
                platform = existingPlatform.gameObject;
            }

            platform.transform.position = new Vector3(0f, -0.25f, 0f);
            platform.transform.rotation = Quaternion.identity;
            platform.transform.localScale = new Vector3(18f, 0.5f, 18f);
            platform.isStatic = true;

            BoxCollider collider = platform.GetComponent<BoxCollider>();
            if (collider == null)
            {
                collider = platform.AddComponent<BoxCollider>();
            }

            collider.enabled = true;
            collider.isTrigger = false;

            MeshRenderer renderer = platform.GetComponent<MeshRenderer>();
            Require(renderer != null, "GroundPlatform MeshRenderer is missing.");
            renderer.sharedMaterial = GetOrCreatePlatformMaterial();

            GameObject player = GameObject.FindWithTag("Player");
            GameObject digitalHuman = GameObject.Find("DigitalHuman");
            Require(player != null, "Player is missing.");
            Require(digitalHuman != null, "DigitalHuman is missing.");
            player.transform.position = new Vector3(0f, 1f, -3f);

            Physics.SyncTransforms();
            Bounds bounds = collider.bounds;
            Require(bounds.min.y <= 0f && bounds.max.y >= -0.001f,
                "GroundPlatform top surface is not at world Y=0.");
            Require(ContainsXZ(bounds, player.transform.position),
                "GroundPlatform does not cover the Player spawn point.");
            Require(ContainsXZ(bounds, digitalHuman.transform.position),
                "GroundPlatform does not cover DigitalHuman.");

            EditorUtility.SetDirty(environment);
            EditorUtility.SetDirty(platform);
            EditorUtility.SetDirty(player.transform);
            EditorSceneManager.MarkSceneDirty(scene);
            Require(EditorSceneManager.SaveScene(scene, ScenePath),
                "Failed to save SampleScene with GroundPlatform.");
            AssetDatabase.SaveAssets();
            Debug.Log(
                "[DigitalHuman] GroundPlatform created and validated: " +
                "18x18, top Y=0, solid BoxCollider, URP material.");
        }

        private static Material GetOrCreatePlatformMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                Require(shader != null, "URP Lit shader is unavailable.");
                material = new Material(shader)
                {
                    name = "DigitalHumanPlatform"
                };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }

            Color baseColor = new Color(0.035f, 0.09f, 0.14f, 1f);
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", baseColor);
            }

            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", 0.35f);
            }

            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", 0.72f);
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        private static bool ContainsXZ(Bounds bounds, Vector3 point)
        {
            return point.x >= bounds.min.x && point.x <= bounds.max.x &&
                   point.z >= bounds.min.z && point.z <= bounds.max.z;
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
