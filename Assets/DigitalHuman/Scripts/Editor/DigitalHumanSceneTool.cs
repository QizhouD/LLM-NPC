#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DigitalHuman.Editor
{
    /// <summary>
    /// The one maintained scene tool. Replaces the 27 single-use bootstrap scripts that
    /// now live in <c>Tools/LegacyEditorBootstrap/</c>.
    /// </summary>
    public static class DigitalHumanSceneTool
    {
        [MenuItem("Digital Human/Scene/Report Hierarchy")]
        public static string ReportHierarchy()
        {
            Scene scene = SceneManager.GetActiveScene();
            StringBuilder report = new StringBuilder();
            report.Append("scene: ").AppendLine(scene.name);
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                AppendObject(report, root.transform, 0);
            }

            string result = report.ToString().TrimEnd();
            Debug.Log(result);
            return result;
        }

        [MenuItem("Digital Human/Scene/Report Digital Human Components")]
        public static string ReportDigitalHumanComponents()
        {
            StringBuilder report = new StringBuilder();
            List<MonoBehaviour> owned = new List<MonoBehaviour>();
            foreach (MonoBehaviour behaviour in
                     Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (behaviour == null)
                {
                    continue;
                }

                string ns = behaviour.GetType().Namespace;
                if (ns != null && ns.StartsWith("DigitalHuman"))
                {
                    owned.Add(behaviour);
                }
            }

            owned.Sort((a, b) => string.Compare(
                Path(a.transform) + a.GetType().Name,
                Path(b.transform) + b.GetType().Name,
                System.StringComparison.Ordinal));

            foreach (MonoBehaviour behaviour in owned)
            {
                report.Append(Path(behaviour.transform))
                    .Append(" :: ")
                    .AppendLine(behaviour.GetType().Name);
            }

            string result = report.Length == 0
                ? "no DigitalHuman components found in the open scene"
                : report.ToString().TrimEnd();
            Debug.Log(result);
            return result;
        }

        [MenuItem("Digital Human/Scene/Report Animators")]
        public static string ReportAnimators()
        {
            StringBuilder report = new StringBuilder();
            foreach (Animator animator in
                     Object.FindObjectsByType<Animator>(FindObjectsSortMode.None))
            {
                report.Append(Path(animator.transform))
                    .Append(" controller=")
                    .Append(animator.runtimeAnimatorController != null
                        ? animator.runtimeAnimatorController.name
                        : "<none>")
                    .Append(" avatar=")
                    .Append(animator.avatar != null ? animator.avatar.name : "<none>")
                    .Append(" isHuman=")
                    .AppendLine((animator.avatar != null && animator.avatar.isHuman).ToString());
            }

            string result = report.Length == 0
                ? "no Animator in the open scene"
                : report.ToString().TrimEnd();
            Debug.Log(result);
            return result;
        }

        private const string BasePrefabPath = "Assets/DigitalHuman/Prefabs/DigitalHuman.prefab";
        private const string VexaPrefabPath = "Assets/Prefabs/Characters/Vexa.prefab";
        private const string VisualChildName = "Vexa_Visual";

        [MenuItem("Digital Human/Scene/Wire Animation Stack")]
        public static string WireAnimationStack()
        {
            StringBuilder report = new StringBuilder();
            report.AppendLine(WireBasePrefab());
            report.AppendLine(WireVexaPrefab());
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            string result = report.ToString().TrimEnd();
            Debug.Log(result);
            return result;
        }

        private static string WireBasePrefab()
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(BasePrefabPath);
            if (contents == null)
            {
                return $"base: FAILED to load {BasePrefabPath}";
            }

            StringBuilder report = new StringBuilder("base: ");
            try
            {
                MotionIntentAnimator intent = contents.GetComponent<MotionIntentAnimator>();
                if (intent == null)
                {
                    intent = contents.AddComponent<MotionIntentAnimator>();
                    report.Append("added MotionIntentAnimator; ");
                }
                else
                {
                    report.Append("MotionIntentAnimator present; ");
                }

                intent.Configure(
                    contents.GetComponentInChildren<Animator>(true),
                    contents.GetComponent<DigitalHumanController>());

                ProceduralHumanoidAnimator procedural =
                    contents.GetComponent<ProceduralHumanoidAnimator>();
                if (procedural != null)
                {
                    procedural.ConfigureIntentSource(
                        intent,
                        contents.GetComponentInChildren<Animator>(true));
                    report.Append("linked ProceduralHumanoidAnimator; ");
                }

                report.Append("ok");

                PrefabUtility.SaveAsPrefabAsset(contents, BasePrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            return report.ToString();
        }

        private static string WireVexaPrefab()
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(VexaPrefabPath);
            if (contents == null)
            {
                return $"vexa: FAILED to load {VexaPrefabPath}";
            }

            StringBuilder report = new StringBuilder("vexa: ");
            try
            {
                Transform visual = contents.transform.Find(VisualChildName);
                if (visual == null)
                {
                    return $"vexa: FAILED, no child named {VisualChildName}";
                }

                RuntimeAnimatorController controller =
                    AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                        AnimatorControllerBuilder.ControllerPath);

                // Vexa clips address bones as "Armature/Base Human...", relative to the
                // model root. The Animator therefore has to sit on Vexa_Visual, not on the
                // NPC root, or nothing binds.
                Animator visualAnimator = visual.GetComponent<Animator>();
                if (visualAnimator == null)
                {
                    visualAnimator = visual.gameObject.AddComponent<Animator>();
                    report.Append($"added Animator on {VisualChildName}; ");
                }
                else
                {
                    report.Append($"Animator on {VisualChildName} present; ");
                }

                visualAnimator.runtimeAnimatorController = controller;
                visualAnimator.applyRootMotion = false;
                visualAnimator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

                Animator rootAnimator = contents.GetComponent<Animator>();
                if (rootAnimator != null)
                {
                    rootAnimator.runtimeAnimatorController = null;
                    rootAnimator.enabled = false;
                    report.Append("disabled root Animator; ");
                }

                MotionIntentAnimator intent = contents.GetComponent<MotionIntentAnimator>();
                if (intent == null)
                {
                    intent = contents.AddComponent<MotionIntentAnimator>();
                    report.Append("added MotionIntentAnimator; ");
                }

                intent.Configure(visualAnimator, contents.GetComponent<DigitalHumanController>());

                ProceduralHumanoidAnimator procedural =
                    contents.GetComponent<ProceduralHumanoidAnimator>();
                if (procedural != null)
                {
                    procedural.ConfigureIntentSource(intent, visualAnimator);
                    report.Append("linked ProceduralHumanoidAnimator; ");
                }

                report.Append("ok");

                PrefabUtility.SaveAsPrefabAsset(contents, VexaPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            return report.ToString();
        }

        private static void AppendObject(StringBuilder report, Transform transform, int depth)
        {
            report.Append(new string(' ', depth * 2)).Append(transform.name);
            Component[] components = transform.GetComponents<Component>();
            if (components.Length > 1)
            {
                report.Append("  [");
                bool first = true;
                foreach (Component component in components)
                {
                    if (component is Transform)
                    {
                        continue;
                    }

                    if (!first)
                    {
                        report.Append(", ");
                    }

                    report.Append(component == null ? "MISSING_SCRIPT" : component.GetType().Name);
                    first = false;
                }

                report.Append(']');
            }

            report.AppendLine();

            // Bone hierarchies are long and not useful here.
            if (transform.name.StartsWith("Base Human") || transform.name == "Armature")
            {
                return;
            }

            for (int i = 0; i < transform.childCount; i++)
            {
                AppendObject(report, transform.GetChild(i), depth + 1);
            }
        }

        private static string Path(Transform transform)
        {
            if (transform.parent == null)
            {
                return transform.name;
            }

            return Path(transform.parent) + "/" + transform.name;
        }
    }
}
#endif
