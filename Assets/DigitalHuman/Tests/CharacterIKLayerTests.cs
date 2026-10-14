using NUnit.Framework;
using UnityEngine;

namespace DigitalHuman.Tests
{
    /// <summary>
    /// The two-bone solver that plants the feet, puts the palms on a held box and raises the
    /// waving arm.
    /// </summary>
    public sealed class CharacterIKLayerTests
    {
        private const float Tolerance = 1e-3f;
        private const float UpperLength = 0.3f;
        private const float LowerLength = 0.25f;

        private GameObject limb;
        private Transform upper;
        private Transform lower;
        private Transform end;

        [SetUp]
        public void SetUp()
        {
            limb = new GameObject("Limb");
            upper = Bone("Upper", limb.transform, Vector3.zero);
            lower = Bone("Lower", upper, new Vector3(0f, -UpperLength, 0f));
            end = Bone("End", lower, new Vector3(0f, -LowerLength, 0f));
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(limb);
        }

        [Test]
        public void ReachableTarget_PutsTheEndOnTheTarget()
        {
            Vector3 target = new Vector3(0.2f, -0.35f, 0.15f);

            CharacterIKLayer.SolveTwoBone(upper, lower, end, target, new Vector3(0f, -0.3f, 1f));

            Assert.That(
                Vector3.Distance(end.position, target),
                Is.LessThan(Tolerance),
                "A target within reach must be met exactly, or the palms float off the box.");
        }

        [Test]
        public void Solving_KeepsTheBoneLengths()
        {
            CharacterIKLayer.SolveTwoBone(
                upper,
                lower,
                end,
                new Vector3(-0.1f, -0.2f, 0.3f),
                new Vector3(0f, 0f, 1f));

            Assert.That(
                Vector3.Distance(upper.position, lower.position),
                Is.EqualTo(UpperLength).Within(Tolerance),
                "Only rotations may change; a stretched bone would tear the skin.");
            Assert.That(
                Vector3.Distance(lower.position, end.position),
                Is.EqualTo(LowerLength).Within(Tolerance));
        }

        [Test]
        public void OutOfReachTarget_StraightensTheLimbTowardsIt()
        {
            Vector3 target = new Vector3(2f, 0f, 0f);

            CharacterIKLayer.SolveTwoBone(upper, lower, end, target, new Vector3(0f, 0f, 1f));

            Vector3 reach = end.position - upper.position;
            Assert.That(
                Vector3.Dot(reach.normalized, target.normalized),
                Is.GreaterThan(0.9999f),
                "An unreachable target should still be pointed at.");
            Assert.That(reach.magnitude, Is.EqualTo(UpperLength + LowerLength).Within(Tolerance));
        }

        [TestCase(1f)]
        [TestCase(-1f)]
        public void Joint_BendsTowardsThePole(float side)
        {
            Vector3 pole = new Vector3(0f, -0.2f, side);

            CharacterIKLayer.SolveTwoBone(upper, lower, end, new Vector3(0f, -0.4f, 0f), pole);

            Assert.That(
                lower.position.z * side,
                Is.GreaterThan(0.05f),
                "Knees must bend forward and elbows out, towards the pole given for each.");
        }

        private static Transform Bone(string name, Transform parent, Vector3 localPosition)
        {
            Transform bone = new GameObject(name).transform;
            bone.SetParent(parent, false);
            bone.localPosition = localPosition;
            return bone;
        }
    }
}
