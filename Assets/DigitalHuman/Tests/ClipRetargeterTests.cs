using System.Collections.Generic;
using DigitalHuman.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DigitalHuman.Tests
{
    /// <summary>
    /// The Vexa animation files address the skeleton two different ways, and as shipped the
    /// idle, wave and jump clips bound nothing while IdleChill sank the feet into the floor.
    /// These pin the conversion that makes every clip drive the model.
    /// </summary>
    public sealed class ClipRetargeterTests
    {
        private const float Tolerance = 1e-4f;
        private const string RigPrefabPath =
            "Assets/Vefects/Stylized Female Character - Vexa/Prefabs/Vefects_Vexa.prefab";
        private const string RetargetFolder = "Assets/DigitalHuman/Animations/Retargeted";

        private GameObject model;

        [SetUp]
        public void SetUp()
        {
            model = new GameObject("Model");
            Transform armature = Child(model.transform, "Armature");
            Transform pelvis = Child(armature, "Pelvis");
            Child(pelvis, "Spine");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(model);
        }

        [Test]
        public void MapPath_KeepsAPathThatAlreadyResolves()
        {
            Assert.That(
                ClipRetargeter.MapPath("Armature/Pelvis/Spine", model.transform),
                Is.EqualTo("Armature/Pelvis/Spine"));
        }

        [Test]
        public void MapPath_RestoresTheNodeAFileLeftOut()
        {
            Assert.That(
                ClipRetargeter.MapPath("Pelvis/Spine", model.transform),
                Is.EqualTo("Armature/Pelvis/Spine"),
                "Clips exported without the Armature node must be re-rooted under it.");
        }

        [Test]
        public void MapPath_ReturnsNullForABoneTheModelLacks()
        {
            Assert.That(ClipRetargeter.MapPath("Tail/Tip", model.transform), Is.Null);
        }

        [Test]
        public void BakeParentIntoChild_LeavesTheChildAloneWhenTheParentIsAtItsDefault()
        {
            Vector3 childPosition = new Vector3(0.1f, 0.97f, -0.03f);
            Quaternion childRotation = Quaternion.Euler(10f, 20f, 30f);

            ClipRetargeter.BakeParentIntoChild(
                Vector3.zero,
                Quaternion.identity,
                Vector3.one,
                childPosition,
                childRotation,
                Vector3.one,
                Matrix4x4.identity,
                out Vector3 position,
                out Quaternion rotation,
                out Vector3 scale);

            Assert.That(Vector3.Distance(position, childPosition), Is.LessThan(Tolerance));
            Assert.That(Quaternion.Angle(rotation, childRotation), Is.LessThan(0.01f));
            Assert.That(Vector3.Distance(scale, Vector3.one), Is.LessThan(Tolerance));
        }

        [Test]
        public void BakeParentIntoChild_MovesTheParentsTurnOntoTheChild()
        {
            // IdleChill turns Armature -90 degrees about X, where the model keeps it at identity.
            Quaternion parentRotation = Quaternion.Euler(-90f, 0f, 0f);
            Vector3 childPosition = new Vector3(0f, 0.03f, 0.97f);
            Quaternion childRotation = Quaternion.Euler(0f, 0f, 90f);

            ClipRetargeter.BakeParentIntoChild(
                Vector3.zero,
                parentRotation,
                Vector3.one,
                childPosition,
                childRotation,
                Vector3.one,
                Matrix4x4.identity,
                out Vector3 position,
                out Quaternion rotation,
                out Vector3 scale);

            Assert.That(
                Vector3.Distance(position, parentRotation * childPosition),
                Is.LessThan(Tolerance),
                "The child must end up where parent and child together put it.");
            Assert.That(Quaternion.Angle(rotation, parentRotation * childRotation), Is.LessThan(0.01f));
            Assert.That(Vector3.Distance(scale, Vector3.one), Is.LessThan(Tolerance));
        }

        [TestCase("Idle")]
        [TestCase("IdleChill")]
        [TestCase("JumpComplete")]
        public void RetargetedClip_BindsEveryCurveOnTheModel(string clipName)
        {
            GameObject rig = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefabPath);
            AnimationClip clip = LoadRetargeted(clipName);
            Assert.That(rig, Is.Not.Null, RigPrefabPath);

            List<string> unbound = new List<string>();
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (AnimationUtility.GetAnimatedObject(rig, binding) == null)
                {
                    unbound.Add(binding.path);
                }
            }

            Assert.That(unbound, Is.Empty, "A curve that does not bind leaves its bone unanimated.");
        }

        [Test]
        public void IdleChill_KeepsTheHipsUpAndTheFeetOnTheFloor()
        {
            AnimationClip clip = LoadRetargeted("IdleChill");
            GameObject rig = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefabPath));
            try
            {
                Transform pelvis = Find(rig.transform, "Base HumanPelvis");
                Transform rightFoot = Find(rig.transform, "Base HumanRFoot");
                Transform leftFoot = Find(rig.transform, "Base HumanLFoot");
                float restPelvis = pelvis.position.y;
                float restFoot = Mathf.Min(rightFoot.position.y, leftFoot.position.y);

                for (int i = 0; i <= 12; i++)
                {
                    clip.SampleAnimation(rig, clip.length * i / 12f);
                    float foot = Mathf.Min(rightFoot.position.y, leftFoot.position.y);
                    Assert.That(
                        pelvis.position.y,
                        Is.EqualTo(restPelvis).Within(0.05f),
                        $"Hips collapsed at {i}/12 of the clip, as they did before retargeting.");
                    Assert.That(foot, Is.EqualTo(restFoot).Within(0.01f), $"Feet left the floor at {i}/12.");
                }
            }
            finally
            {
                Object.DestroyImmediate(rig);
            }
        }

        private static AnimationClip LoadRetargeted(string clipName)
        {
            string path = $"{RetargetFolder}/{clipName}.anim";
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            Assert.That(
                clip,
                Is.Not.Null,
                $"{path} is missing; run Digital Human/Animation/Rebuild Animator Controller.");
            return clip;
        }

        private static Transform Child(Transform parent, string name)
        {
            Transform child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        private static Transform Find(Transform root, string name)
        {
            foreach (Transform bone in root.GetComponentsInChildren<Transform>(true))
            {
                if (bone.name == name)
                {
                    return bone;
                }
            }

            Assert.Fail($"No bone named {name}.");
            return null;
        }
    }
}
