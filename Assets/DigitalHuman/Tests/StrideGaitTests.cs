using NUnit.Framework;
using UnityEngine;

namespace DigitalHuman.Tests
{
    /// <summary>
    /// Pins what the procedural walk is for: a foot in stance stays where it landed while
    /// the body moves over it, whatever the speed. The stride used to run at a fixed
    /// cadence with no link to ground speed, so the feet could only ever skate.
    /// </summary>
    public sealed class StrideGaitTests
    {
        private const float LegLength = 0.93f;
        private const float Amplitude = 27f;
        private const float ReferenceSpeed = 1.4f;

        [TestCase(1.4f)]
        [TestCase(0.7f)]
        [TestCase(0.35f)]
        public void PlantedFoot_StaysPutWhileTheBodyWalksOverIt(float speed)
        {
            float hipAmplitude = Amplitude * Mathf.Sqrt(speed / ReferenceSpeed);
            float strideLength = StrideGait.StrideLength(LegLength, hipAmplitude);
            const float dt = 1f / 240f;

            float phase = 0f;
            float body = 0f;
            float plantedAt = 0f;
            float worstSlip = 0f;
            bool wasInStance = false;
            for (int frame = 0; frame < 240 * 4; frame++)
            {
                float hip = StrideGait.HipAngle(StrideGait.FootTravel(phase), hipAmplitude);
                float foot = body + LegLength * Mathf.Sin(hip * Mathf.Deg2Rad);
                bool inStance = StrideGait.IsInStance(phase);
                if (inStance && !wasInStance)
                {
                    plantedAt = foot;
                }

                if (inStance)
                {
                    worstSlip = Mathf.Max(worstSlip, Mathf.Abs(foot - plantedAt));
                }

                wasInStance = inStance;
                body += speed * dt;
                phase = Mathf.Repeat(phase + speed * dt / strideLength, 1f);
            }

            Assert.That(worstSlip, Is.LessThan(0.01f), "the stance foot skated along the floor");
        }

        [TestCase(0.5f)]
        [TestCase(1f)]
        public void FootTravel_HasNoJumpOrKinkAtHeelStrikeOrToeOff(float boundary)
        {
            const float h = 1e-3f;
            float before = StrideGait.FootTravel(boundary - h);
            float at = StrideGait.FootTravel(boundary);
            float after = StrideGait.FootTravel(boundary + h);

            Assert.That(Mathf.Abs(after - before), Is.LessThan(0.02f), "the foot jumps");
            Assert.That(after - at, Is.EqualTo(at - before).Within(1e-3f),
                "the foot changes speed abruptly");
        }

        [Test]
        public void SwingLift_StaysDownThroughStanceAndPeaksMidSwing()
        {
            for (int i = 0; i < 50; i++)
            {
                float phase = i / 100f;
                Assert.That(StrideGait.SwingLift(phase), Is.EqualTo(0f),
                    $"the knee bends during stance at phase {phase}");
            }

            Assert.That(StrideGait.SwingLift(0.75f), Is.EqualTo(1f).Within(1e-4f));
        }
    }
}
