using System;
using DigitalHuman.Planning;
using NUnit.Framework;
using UnityEngine;

namespace DigitalHuman.Tests
{
    public sealed class TrajectoryPlanTests
    {
        /// <summary>
        /// Shaped exactly like the planner's export, including the snake_case keys, so this
        /// test fails if the DTO drifts from the Python contract.
        /// </summary>
        private const string ValidPlanJson = @"{
            ""plan_id"": ""plan-0007"",
            ""horizon"": 3.0,
            ""trajectory"": [
                {""t"": 0.0, ""position"": [0.0, 0.0, 0.0], ""yaw"": 0.0,
                 ""velocity"": [0.0, 0.0, 1.0], ""angular_velocity"": 0.0, ""speed"": 1.0},
                {""t"": 0.1, ""position"": [0.0, 0.0, 0.1], ""yaw"": 0.0,
                 ""velocity"": [0.0, 0.0, 1.0], ""angular_velocity"": 0.0, ""speed"": 1.0},
                {""t"": 0.2, ""position"": [0.0, 0.0, 0.2], ""yaw"": 0.5,
                 ""velocity"": [0.0, 0.0, 1.0], ""angular_velocity"": 5.0, ""speed"": 1.0}
            ],
            ""segments"": [
                {""start"": 0.0, ""end"": 0.2, ""action"": ""walk"", ""prompt"": ""walk forward""},
                {""start"": 0.2, ""end"": 0.3, ""action"": ""wave"", ""prompt"": ""wave hello""}
            ],
            ""score"": 12.5,
            ""fallback"": false
        }";

        [Test]
        public void FromJson_ParsesPlannerExportShape()
        {
            TrajectoryPlan plan = TrajectoryPlan.FromJson(ValidPlanJson);

            Assert.That(plan.PlanId, Is.EqualTo("plan-0007"));
            Assert.That(plan.Horizon, Is.EqualTo(3.0f).Within(1e-4f));
            Assert.That(plan.Score, Is.EqualTo(12.5f).Within(1e-4f));
            Assert.That(plan.IsFallback, Is.False);
            Assert.That(plan.PointCount, Is.EqualTo(3));
            Assert.That(plan.Points[2].Position, Is.EqualTo(new Vector3(0f, 0f, 0.2f)));
            Assert.That(plan.Points[2].AngularVelocity, Is.EqualTo(5.0f).Within(1e-4f));
            Assert.That(plan.Segments.Count, Is.EqualTo(2));
            Assert.That(plan.Segments[0].Intent, Is.EqualTo(MotionIntent.Walk));
            Assert.That(plan.Segments[1].Intent, Is.EqualTo(MotionIntent.Wave));
            Assert.That(plan.Segments[1].Prompt, Is.EqualTo("wave hello"));
        }

        [Test]
        public void FromJson_ReadsFallbackFlag()
        {
            string json = ValidPlanJson.Replace(@"""fallback"": false", @"""fallback"": true");

            Assert.That(TrajectoryPlan.FromJson(json).IsFallback, Is.True);
        }

        [Test]
        public void FromJson_RejectsPlanWithoutTrajectory()
        {
            string json = @"{""plan_id"": ""p"", ""horizon"": 3.0, ""trajectory"": [],
                             ""segments"": [], ""score"": 0.0, ""fallback"": false}";

            Assert.Throws<FormatException>(() => TrajectoryPlan.FromJson(json));
        }

        [Test]
        public void FromJson_RejectsNonFinitePosition()
        {
            string json = ValidPlanJson.Replace(
                @"""position"": [0.0, 0.0, 0.1]", @"""position"": [0.0, 0.0, Infinity]");

            Assert.Throws<FormatException>(() => TrajectoryPlan.FromJson(json));
        }

        [Test]
        public void FromJson_RejectsWrongPositionArity()
        {
            string json = ValidPlanJson.Replace(
                @"""position"": [0.0, 0.0, 0.1]", @"""position"": [0.0, 0.1]");

            Assert.Throws<FormatException>(() => TrajectoryPlan.FromJson(json));
        }

        [Test]
        public void FromJson_DegradesUnknownActionToIdle()
        {
            string json = ValidPlanJson.Replace(@"""action"": ""walk""", @"""action"": ""moonwalk""");

            TrajectoryPlan plan = TrajectoryPlan.FromJson(json);

            Assert.That(plan.Segments[0].Intent, Is.EqualTo(MotionIntent.Idle));
        }

        [Test]
        public void Sample_InterpolatesBetweenControlPoints()
        {
            TrajectoryPlan plan = TrajectoryPlan.FromJson(ValidPlanJson);

            TrajectoryPoint midpoint = plan.Sample(0.05f);

            Assert.That(midpoint.Position.z, Is.EqualTo(0.05f).Within(1e-4f));
        }

        [Test]
        public void Sample_ClampsOutsideHorizon()
        {
            TrajectoryPlan plan = TrajectoryPlan.FromJson(ValidPlanJson);

            Assert.That(plan.Sample(-5f).Position.z, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(plan.Sample(99f).Position.z, Is.EqualTo(0.2f).Within(1e-4f));
        }

        [Test]
        public void Sample_TakesShortestArcAcrossYawWrap()
        {
            string json = ValidPlanJson
                .Replace(@"{""t"": 0.0, ""position"": [0.0, 0.0, 0.0], ""yaw"": 0.0",
                    @"{""t"": 0.0, ""position"": [0.0, 0.0, 0.0], ""yaw"": 3.0")
                .Replace(@"{""t"": 0.1, ""position"": [0.0, 0.0, 0.1], ""yaw"": 0.0",
                    @"{""t"": 0.1, ""position"": [0.0, 0.0, 0.1], ""yaw"": -3.0");

            TrajectoryPlan plan = TrajectoryPlan.FromJson(json);
            float yaw = plan.Sample(0.05f).Yaw;

            // Going the short way from 3.0 to -3.0 passes through +/-pi, not through zero.
            Assert.That(Mathf.Abs(yaw), Is.GreaterThan(3.0f));
        }

        [Test]
        public void IntentAt_ResolvesSegmentBoundaries()
        {
            TrajectoryPlan plan = TrajectoryPlan.FromJson(ValidPlanJson);

            Assert.That(plan.IntentAt(0.0f), Is.EqualTo(MotionIntent.Walk));
            Assert.That(plan.IntentAt(0.19f), Is.EqualTo(MotionIntent.Walk));
            Assert.That(plan.IntentAt(0.2f), Is.EqualTo(MotionIntent.Wave));
        }

        [Test]
        public void FromJson_RejectsEmptyInput()
        {
            Assert.Throws<ArgumentException>(() => TrajectoryPlan.FromJson("   "));
        }
    }
}
