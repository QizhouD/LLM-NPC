#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DigitalHuman.Editor
{
    /// <summary>
    /// Copies an imported clip into an .anim whose curves bind on the rig the Animator drives.
    /// </summary>
    /// <remarks>
    /// The Vexa animation FBXs were exported two ways. Most have no Armature node, so their
    /// curves start at "Base HumanPelvis" and resolve to nothing under Vexa_Visual, where the
    /// pelvis sits at "Armature/Base HumanPelvis": those clips leave the skeleton untouched.
    /// IdleChill keeps the node but animates it at -90 degrees about X, while the model's
    /// Armature is identity, and blending the two conventions folds the body down by about
    /// 30 cm for the length of every transition. Retargeting re-roots the first kind and
    /// bakes the node's transform into its children for the second, so every clip agrees
    /// with the model.
    /// </remarks>
    public static class ClipRetargeter
    {
        private const string PositionProperty = "m_LocalPosition";
        private const string RotationProperty = "m_LocalRotation";
        private const string ScaleProperty = "m_LocalScale";

        /// <summary>
        /// Path of <paramref name="path"/> relative to <paramref name="root"/>, re-rooted at the
        /// descendant named like its first segment when it does not resolve as given.
        /// Null when no such bone exists.
        /// </summary>
        public static string MapPath(string path, Transform root)
        {
            if (root == null)
            {
                return null;
            }

            if (string.IsNullOrEmpty(path) || root.Find(path) != null)
            {
                return path ?? string.Empty;
            }

            int slash = path.IndexOf('/');
            string head = slash < 0 ? path : path.Substring(0, slash);
            Transform match = FindDescendant(root, head);
            if (match == null)
            {
                return null;
            }

            string prefix = AnimationUtility.CalculateTransformPath(match, root);
            string mapped = slash < 0 ? prefix : prefix + path.Substring(slash);
            return root.Find(mapped) != null ? mapped : null;
        }

        /// <summary>
        /// Local pose a child needs under its parent's default transform to land where the
        /// clip's animated parent and child put it.
        /// </summary>
        public static void BakeParentIntoChild(
            Vector3 parentPosition,
            Quaternion parentRotation,
            Vector3 parentScale,
            Vector3 childPosition,
            Quaternion childRotation,
            Vector3 childScale,
            Matrix4x4 parentDefault,
            out Vector3 position,
            out Quaternion rotation,
            out Vector3 scale)
        {
            Matrix4x4 baked = parentDefault.inverse *
                              Matrix4x4.TRS(parentPosition, parentRotation, parentScale) *
                              Matrix4x4.TRS(childPosition, childRotation, childScale);
            position = baked.GetColumn(3);
            rotation = baked.rotation;
            scale = baked.lossyScale;
        }

        /// <summary>
        /// Writes the retargeted copy of <paramref name="source"/> to
        /// <paramref name="assetPath"/>, reusing the asset there so references survive.
        /// </summary>
        /// <param name="bakeNodes">
        /// Paths, as they resolve on <paramref name="targetRoot"/>, of nodes whose animated
        /// transform is folded into their children so the node itself keeps its default.
        /// </param>
        public static AnimationClip Retarget(
            AnimationClip source,
            Transform targetRoot,
            string assetPath,
            IReadOnlyCollection<string> bakeNodes,
            out string report)
        {
            AnimationClip clip = new AnimationClip
            {
                name = Path.GetFileNameWithoutExtension(assetPath),
                frameRate = source.frameRate,
                wrapMode = source.wrapMode,
                legacy = false
            };
            AnimationUtility.SetAnimationClipSettings(
                clip,
                AnimationUtility.GetAnimationClipSettings(source));

            HashSet<string> baked = new HashSet<string>(bakeNodes ?? new string[0]);
            Dictionary<string, Dictionary<string, AnimationCurve>> transformCurves =
                new Dictionary<string, Dictionary<string, AnimationCurve>>();
            List<EditorCurveBinding> bindings = new List<EditorCurveBinding>();
            List<AnimationCurve> curves = new List<AnimationCurve>();
            int dropped = 0;

            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(source))
            {
                string mapped = MapPath(binding.path, targetRoot);
                if (mapped == null)
                {
                    dropped++;
                    continue;
                }

                AnimationCurve curve = AnimationUtility.GetEditorCurve(source, binding);
                if (binding.type == typeof(Transform))
                {
                    if (!transformCurves.TryGetValue(mapped, out Dictionary<string, AnimationCurve> byProperty))
                    {
                        byProperty = new Dictionary<string, AnimationCurve>();
                        transformCurves.Add(mapped, byProperty);
                    }

                    byProperty[binding.propertyName] = curve;
                    continue;
                }

                EditorCurveBinding copy = binding;
                copy.path = mapped;
                bindings.Add(copy);
                curves.Add(curve);
            }

            int bakedChildren = 0;
            foreach (string node in baked)
            {
                Transform nodeTransform = targetRoot.Find(node);
                if (nodeTransform == null ||
                    !transformCurves.TryGetValue(node, out Dictionary<string, AnimationCurve> nodeCurves))
                {
                    continue;
                }

                for (int i = 0; i < nodeTransform.childCount; i++)
                {
                    Transform child = nodeTransform.GetChild(i);
                    string childPath = AnimationUtility.CalculateTransformPath(child, targetRoot);
                    transformCurves.TryGetValue(childPath, out Dictionary<string, AnimationCurve> childCurves);
                    transformCurves[childPath] = BakeChild(
                        source,
                        nodeCurves,
                        nodeTransform,
                        childCurves,
                        child);
                    bakedChildren++;
                }

                transformCurves.Remove(node);
            }

            foreach (KeyValuePair<string, Dictionary<string, AnimationCurve>> entry in transformCurves)
            {
                foreach (KeyValuePair<string, AnimationCurve> property in entry.Value)
                {
                    bindings.Add(EditorCurveBinding.FloatCurve(
                        entry.Key,
                        typeof(Transform),
                        property.Key));
                    curves.Add(property.Value);
                }
            }

            AnimationUtility.SetEditorCurves(clip, bindings.ToArray(), curves.ToArray());

            AnimationClip existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(clip, existing);
                Object.DestroyImmediate(clip);
                clip = existing;
                EditorUtility.SetDirty(clip);
            }
            else
            {
                string folder = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
                EnsureFolder(folder);
                AssetDatabase.CreateAsset(clip, assetPath);
            }

            report = $"{source.name} -> {assetPath}: {bindings.Count} curves" +
                     (dropped > 0 ? $", {dropped} unresolvable dropped" : string.Empty) +
                     (bakedChildren > 0 ? $", {bakedChildren} child(ren) baked" : string.Empty);
            return clip;
        }

        /// <summary>
        /// Resamples the child's transform once per frame with the node's animated transform
        /// folded in, relative to the node's default.
        /// </summary>
        private static Dictionary<string, AnimationCurve> BakeChild(
            AnimationClip source,
            Dictionary<string, AnimationCurve> nodeCurves,
            Transform node,
            Dictionary<string, AnimationCurve> childCurves,
            Transform child)
        {
            float frameRate = source.frameRate > 0f ? source.frameRate : 30f;
            int frameCount = Mathf.Max(2, Mathf.RoundToInt(source.length * frameRate) + 1);
            Matrix4x4 nodeDefault = Matrix4x4.TRS(node.localPosition, node.localRotation, node.localScale);

            string[] names =
            {
                PositionProperty + ".x", PositionProperty + ".y", PositionProperty + ".z",
                RotationProperty + ".x", RotationProperty + ".y", RotationProperty + ".z", RotationProperty + ".w",
                ScaleProperty + ".x", ScaleProperty + ".y", ScaleProperty + ".z"
            };
            Keyframe[][] keys = new Keyframe[names.Length][];
            for (int i = 0; i < names.Length; i++)
            {
                keys[i] = new Keyframe[frameCount];
            }

            Quaternion previous = Quaternion.identity;
            for (int frame = 0; frame < frameCount; frame++)
            {
                float time = Mathf.Min(frame / frameRate, source.length);
                BakeParentIntoChild(
                    SampleVector(nodeCurves, PositionProperty, time, node.localPosition),
                    SampleRotation(nodeCurves, time, node.localRotation),
                    SampleVector(nodeCurves, ScaleProperty, time, node.localScale),
                    SampleVector(childCurves, PositionProperty, time, child.localPosition),
                    SampleRotation(childCurves, time, child.localRotation),
                    SampleVector(childCurves, ScaleProperty, time, child.localScale),
                    nodeDefault,
                    out Vector3 position,
                    out Quaternion rotation,
                    out Vector3 scale);

                // q and -q are the same rotation, but the curves interpolate componentwise.
                if (frame > 0 && Quaternion.Dot(previous, rotation) < 0f)
                {
                    rotation = new Quaternion(-rotation.x, -rotation.y, -rotation.z, -rotation.w);
                }

                previous = rotation;
                float[] values =
                {
                    position.x, position.y, position.z,
                    rotation.x, rotation.y, rotation.z, rotation.w,
                    scale.x, scale.y, scale.z
                };
                for (int i = 0; i < names.Length; i++)
                {
                    keys[i][frame] = new Keyframe(time, values[i]);
                }
            }

            Dictionary<string, AnimationCurve> result = new Dictionary<string, AnimationCurve>();
            for (int i = 0; i < names.Length; i++)
            {
                result[names[i]] = SmoothCurve(keys[i]);
            }

            return result;
        }

        private static AnimationCurve SmoothCurve(Keyframe[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                int before = Mathf.Max(0, i - 1);
                int after = Mathf.Min(keys.Length - 1, i + 1);
                float span = keys[after].time - keys[before].time;
                float slope = span > 0f ? (keys[after].value - keys[before].value) / span : 0f;
                keys[i].inTangent = slope;
                keys[i].outTangent = slope;
            }

            return new AnimationCurve(keys);
        }

        private static Vector3 SampleVector(
            Dictionary<string, AnimationCurve> curves,
            string property,
            float time,
            Vector3 fallback)
        {
            return new Vector3(
                Sample(curves, property + ".x", time, fallback.x),
                Sample(curves, property + ".y", time, fallback.y),
                Sample(curves, property + ".z", time, fallback.z));
        }

        private static Quaternion SampleRotation(
            Dictionary<string, AnimationCurve> curves,
            float time,
            Quaternion fallback)
        {
            Quaternion q = new Quaternion(
                Sample(curves, RotationProperty + ".x", time, fallback.x),
                Sample(curves, RotationProperty + ".y", time, fallback.y),
                Sample(curves, RotationProperty + ".z", time, fallback.z),
                Sample(curves, RotationProperty + ".w", time, fallback.w));
            float magnitude = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return magnitude > 1e-6f
                ? new Quaternion(q.x / magnitude, q.y / magnitude, q.z / magnitude, q.w / magnitude)
                : fallback;
        }

        private static float Sample(
            Dictionary<string, AnimationCurve> curves,
            string property,
            float time,
            float fallback)
        {
            return curves != null && curves.TryGetValue(property, out AnimationCurve curve) && curve != null
                ? curve.Evaluate(time)
                : fallback;
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.name == name)
                {
                    return child;
                }

                Transform found = FindDescendant(child, name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static void EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
#endif
