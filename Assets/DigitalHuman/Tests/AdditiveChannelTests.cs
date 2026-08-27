using NUnit.Framework;
using UnityEngine;

namespace DigitalHuman.Tests
{
    /// <summary>
    /// Locks in the fix for the drift that lifted the character off the ground: the
    /// procedural layer added its offset to its own previous output every frame, so a
    /// non-negative offset such as the walk bounce climbed without bound.
    /// </summary>
    public sealed class AdditiveChannelTests
    {
        private const float Tolerance = 1e-4f;

        [Test]
        public void Position_HoldsSteadyWhileNothingElseWritesTheValue()
        {
            AdditivePositionChannel channel = new AdditivePositionChannel();
            Vector3 offset = new Vector3(0f, 0.02f, 0f);
            Vector3 value = Vector3.zero;

            for (int frame = 0; frame < 600; frame++)
            {
                value = channel.Apply(value, offset);
            }

            Assert.That(
                value.y,
                Is.EqualTo(offset.y).Within(Tolerance),
                "A constant offset must stay a constant offset, not accumulate per frame.");
        }

        [Test]
        public void Position_TracksTheLowerLayerWhenItWritesTheValue()
        {
            AdditivePositionChannel channel = new AdditivePositionChannel();
            Vector3 offset = new Vector3(0f, 0.02f, 0f);

            for (int frame = 0; frame < 100; frame++)
            {
                // Stands in for an Animator writing a fresh pose every frame.
                Vector3 poseFromBelow = new Vector3(0f, frame, 0f);
                Vector3 result = channel.Apply(poseFromBelow, offset);
                Assert.That(
                    result.y,
                    Is.EqualTo(frame + offset.y).Within(Tolerance),
                    "The offset must land on the pose the lower layer just wrote.");
            }
        }

        [Test]
        public void Position_ReturnsToTheBaseWhenTheOffsetGoesAway()
        {
            AdditivePositionChannel channel = new AdditivePositionChannel();
            Vector3 value = new Vector3(0f, 1f, 0f);

            for (int frame = 0; frame < 200; frame++)
            {
                value = channel.Apply(value, new Vector3(0f, 0.05f, 0f));
            }

            value = channel.Apply(value, Vector3.zero);

            Assert.That(
                value.y,
                Is.EqualTo(1f).Within(Tolerance),
                "Dropping the offset must restore the original pose exactly.");
        }

        [Test]
        public void Rotation_HoldsSteadyWhileNothingElseWritesTheValue()
        {
            AdditiveRotationChannel channel = new AdditiveRotationChannel();
            Quaternion offset = Quaternion.Euler(0f, 5f, 0f);
            Quaternion value = Quaternion.identity;

            for (int frame = 0; frame < 600; frame++)
            {
                value = channel.Apply(value, offset);
            }

            Assert.That(
                Quaternion.Angle(value, offset),
                Is.LessThan(0.05f),
                "600 frames of a 5 degree offset must still read as 5 degrees.");
        }

        [Test]
        public void Rotation_TracksTheLowerLayerWhenItWritesTheValue()
        {
            AdditiveRotationChannel channel = new AdditiveRotationChannel();
            Quaternion offset = Quaternion.Euler(0f, 5f, 0f);

            for (int frame = 0; frame < 100; frame++)
            {
                Quaternion poseFromBelow = Quaternion.Euler(0f, frame * 3f, 0f);
                Quaternion result = channel.Apply(poseFromBelow, offset);
                Assert.That(
                    Quaternion.Angle(result, poseFromBelow * offset),
                    Is.LessThan(0.05f));
            }
        }

        [Test]
        public void Rotation_ApplyToBaseIgnoresWhateverIsCurrentlyOnTheBone()
        {
            AdditiveRotationChannel channel = new AdditiveRotationChannel();
            Quaternion rest = Quaternion.Euler(0f, 90f, 0f);
            Quaternion offset = Quaternion.Euler(0f, 5f, 0f);

            channel.Apply(Quaternion.Euler(0f, 200f, 0f), offset);
            Quaternion result = channel.ApplyToBase(rest, offset);

            Assert.That(
                Quaternion.Angle(result, rest * offset),
                Is.LessThan(0.05f),
                "With no lower layer the rest pose is the base, whatever the bone holds.");
        }

        [Test]
        public void Reset_ForgetsTheStoredBase()
        {
            AdditiveRotationChannel channel = new AdditiveRotationChannel();
            Quaternion offset = Quaternion.Euler(0f, 5f, 0f);

            Quaternion value = channel.Apply(Quaternion.identity, offset);
            channel.Reset();

            // After a reset the current value is adopted as a fresh base, which is what
            // rebinding to a different skeleton needs.
            Quaternion afterReset = channel.Apply(value, offset);

            Assert.That(
                Quaternion.Angle(afterReset, offset * offset),
                Is.LessThan(0.05f));
        }
    }
}
