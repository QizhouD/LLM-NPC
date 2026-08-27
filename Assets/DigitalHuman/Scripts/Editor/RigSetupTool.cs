#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DigitalHuman.Editor
{
    /// <summary>
    /// Reproducible rig setup for the Vexa character.
    /// The shipped asset is a Generic rig with "Base Human*" bone names, so
    /// Humanoid conversion is scripted rather than clicked through the Inspector.
    /// </summary>
    public static class RigSetupTool
    {
        public const string CharacterModelPath =
            "Assets/Vefects/Stylized Female Character - Vexa/Meshes/Vefects_Vexa.fbx";

        private const string AnimationFolder =
            "Assets/Vefects/Stylized Female Character - Vexa/Animations";

        [MenuItem("Digital Human/Rig/Report Character Rig")]
        public static string ReportCharacterRig()
        {
            string report = BuildRigReport(CharacterModelPath);
            Debug.Log(report);
            return report;
        }

        [MenuItem("Digital Human/Rig/Convert Character To Humanoid")]
        public static string ConvertCharacterToHumanoid()
        {
            ModelImporter importer = LoadImporter(CharacterModelPath);
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.SaveAndReimport();

            string report = BuildRigReport(CharacterModelPath);
            Debug.Log(report);
            return report;
        }

        [MenuItem("Digital Human/Rig/Revert Character To Generic")]
        public static string RevertCharacterToGeneric()
        {
            ModelImporter importer = LoadImporter(CharacterModelPath);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.SaveAndReimport();
            return BuildRigReport(CharacterModelPath);
        }

        /// <summary>
        /// Converts up to <paramref name="batchSize"/> animation models that are not yet
        /// Humanoid. The Unity CLI aborts a main-thread eval after five seconds, so the
        /// conversion is resumable instead of a single blocking loop over 25 files.
        /// </summary>
        public static string ConvertNextAnimationBatch(int batchSize = 2)
        {
            Avatar characterAvatar = FindAvatar(CharacterModelPath);
            if (characterAvatar == null || !characterAvatar.isHuman)
            {
                return "FAILED: the character avatar is not a valid Humanoid avatar. " +
                       "Run Convert Character To Humanoid first.";
            }

            List<string> pending = new List<string>();
            int alreadyDone = 0;
            foreach (string path in EnumerateAnimationModels())
            {
                ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null)
                {
                    continue;
                }

                if (importer.animationType == ModelImporterAnimationType.Human)
                {
                    alreadyDone++;
                }
                else
                {
                    pending.Add(path);
                }
            }

            StringBuilder report = new StringBuilder();
            int processed = 0;
            for (int i = 0; i < pending.Count && processed < Mathf.Max(1, batchSize); i++)
            {
                string path = pending[i];
                ModelImporter importer = (ModelImporter)AssetImporter.GetAtPath(path);
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                importer.sourceAvatar = characterAvatar;
                importer.SaveAndReimport();
                processed++;
                if (!HasClip(path))
                {
                    report.AppendLine($"  WARNING no clip after reimport: {Path.GetFileName(path)}");
                }
            }

            int remaining = pending.Count - processed;
            report.Insert(
                0,
                $"converted={processed} alreadyHuman={alreadyDone} remaining={remaining}\n");
            string result = report.ToString().TrimEnd();
            Debug.Log(result);
            return result;
        }

        [MenuItem("Digital Human/Rig/List Animation Clip Names")]
        public static string ListAnimationClipNames()
        {
            StringBuilder report = new StringBuilder();
            foreach (string path in EnumerateAnimationModels())
            {
                report.Append(Path.GetFileNameWithoutExtension(path)).Append(": ");
                List<string> names = new List<string>();
                foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset is AnimationClip clip)
                    {
                        names.Add(clip.name.StartsWith("__preview__") ? "[preview]" : clip.name);
                    }
                }

                report.AppendLine(names.Count == 0 ? "NONE" : string.Join(",", names));
            }

            string result = report.ToString().TrimEnd();
            Debug.Log(result);
            return result;
        }

        [MenuItem("Digital Human/Rig/Describe Animation Models")]
        public static string DescribeAnimationModels()
        {
            StringBuilder report = new StringBuilder();
            foreach (string path in EnumerateAnimationModels())
            {
                report.Append(Path.GetFileNameWithoutExtension(path)).Append(" -> ");
                foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    report.Append(asset.GetType().Name).Append('(').Append(asset.name).Append(") ");
                }

                report.AppendLine();
            }

            string result = report.ToString().TrimEnd();
            Debug.Log(result);
            return result;
        }

        [MenuItem("Digital Human/Rig/Report Animation Rigs")]
        public static string ReportAnimationRigs()
        {
            int human = 0;
            int other = 0;
            int withClips = 0;
            foreach (string path in EnumerateAnimationModels())
            {
                ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null)
                {
                    continue;
                }

                if (importer.animationType == ModelImporterAnimationType.Human)
                {
                    human++;
                }
                else
                {
                    other++;
                }

                if (HasClip(path))
                {
                    withClips++;
                }
            }

            string report = $"animation models: human={human} nonHuman={other} withClips={withClips}";
            Debug.Log(report);
            return report;
        }

        private static IEnumerable<string> EnumerateAnimationModels()
        {
            string[] guids = AssetDatabase.FindAssets("t:Model", new[] { AnimationFolder });
            foreach (string guid in guids)
            {
                yield return AssetDatabase.GUIDToAssetPath(guid);
            }
        }

        private static bool HasClip(string path)
        {
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                {
                    return true;
                }
            }

            return false;
        }

        public static Avatar FindAvatar(string modelPath)
        {
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(modelPath))
            {
                if (asset is Avatar avatar)
                {
                    return avatar;
                }
            }

            return null;
        }

        private static ModelImporter LoadImporter(string path)
        {
            ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                throw new InvalidOperationException($"No ModelImporter at {path}");
            }

            return importer;
        }

        private static string BuildRigReport(string modelPath)
        {
            ModelImporter importer = LoadImporter(modelPath);
            Avatar avatar = FindAvatar(modelPath);
            StringBuilder report = new StringBuilder();
            report.AppendLine($"Rig report for {Path.GetFileName(modelPath)}");
            report.AppendLine($"  animationType: {importer.animationType}");
            report.AppendLine($"  avatar found: {avatar != null}");
            if (avatar != null)
            {
                report.AppendLine($"  isValid: {avatar.isValid}");
                report.AppendLine($"  isHuman: {avatar.isHuman}");
            }

            HumanDescription description = importer.humanDescription;
            report.AppendLine($"  human bone mappings: {description.human?.Length ?? 0}");
            if (description.human != null && description.human.Length > 0)
            {
                List<string> unmapped = new List<string>();
                foreach (HumanBone bone in description.human)
                {
                    if (string.IsNullOrEmpty(bone.boneName))
                    {
                        unmapped.Add(bone.humanName);
                    }
                }

                report.AppendLine(unmapped.Count == 0
                    ? "  unmapped required bones: none"
                    : $"  unmapped: {string.Join(", ", unmapped)}");
            }

            return report.ToString().TrimEnd();
        }
    }
}
#endif
