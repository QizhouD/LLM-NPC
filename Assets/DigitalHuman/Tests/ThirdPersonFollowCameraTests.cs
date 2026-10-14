using NUnit.Framework;
using UnityEngine;

namespace DigitalHuman.Tests
{
    public sealed class ThirdPersonFollowCameraTests
    {
        private const float Tolerance = 1e-3f;

        /// <summary>
        /// The overview shot is the camera's scene placement re-expressed as an orbit. If the
        /// round trip drifts, switching to the overview no longer lands where it started.
        /// </summary>
        [Test]
        public void FromPlacement_RoundTripsTheScenePlacement()
        {
            Vector3 position = new Vector3(0f, 16f, -15f);
            Quaternion rotation = Quaternion.Euler(46f, 0f, 0f);

            OrbitPose pose = OrbitPose.FromPlacement(position, rotation, 0f);

            Assert.That(Vector3.Distance(pose.Position, position), Is.LessThan(Tolerance));
            Assert.That(Quaternion.Angle(pose.Rotation, rotation), Is.LessThan(0.01f));
            Assert.That(pose.Pivot.y, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(pose.Pitch, Is.EqualTo(46f).Within(Tolerance));
        }

        [Test]
        public void FromPlacement_FallsBackWhenTheCameraLooksAtTheHorizon()
        {
            OrbitPose pose = OrbitPose.FromPlacement(
                new Vector3(0f, 2f, 0f), Quaternion.Euler(-5f, 30f, 0f), 0f, 12f);

            Assert.That(pose.Distance, Is.EqualTo(12f));
            Assert.That(Vector3.Distance(pose.Position, new Vector3(0f, 2f, 0f)), Is.LessThan(Tolerance));
        }

        [TestCase(NpcCameraAngle.Front, 180f - 30f)]
        [TestCase(NpcCameraAngle.Side, 90f)]
        [TestCase(NpcCameraAngle.Back, 0f)]
        public void YawFor_PlacesTheCameraRelativeToWhereSheFaces(
            NpcCameraAngle angle,
            float offsetFromFacing)
        {
            const float facing = 145f;

            float yaw = ThirdPersonFollowCamera.YawFor(angle, facing, 30f);

            Assert.That(Mathf.DeltaAngle(facing + offsetFromFacing, yaw),
                Is.EqualTo(0f).Within(Tolerance));
        }

        [Test]
        public void YawFor_FrontShotLooksBackAtHerFace()
        {
            const float facing = 145f;
            Vector3 faceDirection = Quaternion.Euler(0f, facing, 0f) * Vector3.forward;

            float yaw = ThirdPersonFollowCamera.YawFor(NpcCameraAngle.Front, facing, 0f);
            Vector3 viewDirection = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;

            Assert.That(Vector3.Dot(faceDirection, viewDirection), Is.EqualTo(-1f).Within(Tolerance));
        }
    }
}
