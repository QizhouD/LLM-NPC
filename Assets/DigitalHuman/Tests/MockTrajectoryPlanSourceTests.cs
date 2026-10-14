using System.Collections.Generic;
using DigitalHuman.Planning;
using NUnit.Framework;
using UnityEngine;

namespace DigitalHuman.Tests
{
    public sealed class MockTrajectoryPlanSourceTests
    {
        private GameObject host;
        private MockTrajectoryPlanSource source;

        [SetUp]
        public void SetUp()
        {
            host = new GameObject("MockPlanSource");
            source = host.AddComponent<MockTrajectoryPlanSource>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(host);
        }

        private TrajectoryPlan Plan(
            Vector3 start,
            PlanGoal goal,
            IReadOnlyList<PlanObstacle> obstacles = null)
        {
            PlanRequest request = new PlanRequest(
                start, 0f, Vector3.zero, 0f, MotionIntent.Idle, goal, obstacles);
            return source.RequestPlanAsync(request).Result;
        }

        [Test]
        public void Plan_HonoursContractShape()
        {
            TrajectoryPlan plan = Plan(
                Vector3.zero,
                new PlanGoal(MotionIntent.Walk, new Vector3(5f, 0f, 0f)));

            Assert.That(plan.IsFallback, Is.False, "the mock must never emit a fallback plan");
            Assert.That(plan.PointCount, Is.EqualTo(source.HorizonSteps));
            Assert.That(plan.Horizon,
                Is.EqualTo(source.HorizonSteps / source.ControlHz).Within(1e-4f));
            Assert.That(plan.Segments, Is.Not.Empty);
        }

        [Test]
        public void Plan_TimestampsAreMonotonicAtControlRate()
        {
            TrajectoryPlan plan = Plan(
                Vector3.zero,
                new PlanGoal(MotionIntent.Walk, new Vector3(5f, 0f, 0f)));

            float step = 1f / source.ControlHz;
            for (int i = 1; i < plan.PointCount; i++)
            {
                Assert.That(plan.Points[i].Time - plan.Points[i - 1].Time,
                    Is.EqualTo(step).Within(1e-4f), $"gap before point {i}");
            }
        }

        [Test]
        public void Plan_MakesProgressTowardsGoal()
        {
            Vector3 goalPosition = new Vector3(4f, 0f, 3f);
            TrajectoryPlan plan = Plan(
                Vector3.zero, new PlanGoal(MotionIntent.Walk, goalPosition));

            float startDistance = Vector3.Distance(plan.Points[0].Position, goalPosition);
            float endDistance = Vector3.Distance(
                plan.Points[plan.PointCount - 1].Position, goalPosition);

            Assert.That(endDistance, Is.LessThan(startDistance - 1f));
        }

        [Test]
        public void Plan_StopsShortByTheRequestedStandoff()
        {
            Vector3 goalPosition = new Vector3(3f, 0f, 0f);
            const float standoff = 1.2f;
            TrajectoryPlan plan = Plan(
                Vector3.zero,
                new PlanGoal(MotionIntent.Walk, goalPosition, stopDistance: standoff));

            Vector3 finalPosition = plan.Points[plan.PointCount - 1].Position;

            Assert.That(Vector3.Distance(finalPosition, goalPosition),
                Is.EqualTo(standoff).Within(0.25f));
        }

        [Test]
        public void Plan_RoutesAroundABlockingObstacle()
        {
            Vector3 goalPosition = new Vector3(6f, 0f, 0f);
            PlanObstacle obstacle = new PlanObstacle(new Vector3(3f, 0f, 0f), 0.8f);
            TrajectoryPlan plan = Plan(
                Vector3.zero,
                new PlanGoal(MotionIntent.Walk, goalPosition),
                new[] { obstacle });

            float minimumGap = float.MaxValue;
            for (int i = 0; i < plan.PointCount; i++)
            {
                Vector3 delta = plan.Points[i].Position - obstacle.Center;
                delta.y = 0f;
                minimumGap = Mathf.Min(minimumGap, delta.magnitude);
            }

            Assert.That(minimumGap, Is.GreaterThan(obstacle.Radius),
                "the planned path passes through the obstacle");
        }

        [Test]
        public void Plan_SpeedStaysWithinTheKinematicBudget()
        {
            TrajectoryPlan plan = Plan(
                Vector3.zero, new PlanGoal(MotionIntent.Walk, new Vector3(8f, 0f, 0f)));

            for (int i = 0; i < plan.PointCount; i++)
            {
                Assert.That(plan.Points[i].Speed, Is.GreaterThanOrEqualTo(0f),
                    $"point {i} has negative speed");
                Assert.That(plan.Points[i].Speed, Is.LessThanOrEqualTo(3f),
                    $"point {i} exceeds the planner speed limit");
            }
        }

        [Test]
        public void Plan_ReplanMidWalkKeepsTheCurrentSpeed()
        {
            const float cruise = 1.4f;
            PlanRequest request = new PlanRequest(
                Vector3.zero,
                Mathf.PI * 0.5f,
                new Vector3(cruise, 0f, 0f),
                0f,
                MotionIntent.Walk,
                new PlanGoal(MotionIntent.Walk, new Vector3(10f, 0f, 0f)));

            TrajectoryPlan plan = source.RequestPlanAsync(request).Result;

            for (int i = 0; i < plan.PointCount; i++)
            {
                Assert.That(plan.Points[i].Speed, Is.EqualTo(cruise).Within(1e-3f),
                    $"point {i} fell below the speed the body arrived with");
            }
        }

        [Test]
        public void Plan_SpeedMatchesTheDistanceActuallyTravelled()
        {
            TrajectoryPlan plan = Plan(
                Vector3.zero, new PlanGoal(MotionIntent.Walk, new Vector3(8f, 0f, 0f)));

            float step = 1f / source.ControlHz;
            for (int i = 1; i < plan.PointCount; i++)
            {
                float travelled = Vector3.Distance(
                    plan.Points[i - 1].Position, plan.Points[i].Position);
                float implied = travelled / step;

                Assert.That(implied, Is.LessThanOrEqualTo(3f),
                    $"implied speed at point {i} exceeds the planner limit");
            }
        }

        [Test]
        public void Plan_SegmentsTileTheHorizonWithoutGaps()
        {
            TrajectoryPlan plan = Plan(
                Vector3.zero, new PlanGoal(MotionIntent.Wave, new Vector3(2f, 0f, 0f)));

            Assert.That(plan.Segments[0].Start, Is.EqualTo(0f).Within(1e-4f));
            for (int i = 1; i < plan.Segments.Count; i++)
            {
                Assert.That(plan.Segments[i].Start,
                    Is.EqualTo(plan.Segments[i - 1].End).Within(1e-4f),
                    $"gap between segment {i - 1} and {i}");
            }

            Assert.That(plan.Segments[plan.Segments.Count - 1].End,
                Is.GreaterThanOrEqualTo(plan.Points[plan.PointCount - 1].Time));
        }

        [Test]
        public void Plan_EndsWithWaveWhenTheGoalIsAGreeting()
        {
            TrajectoryPlan plan = Plan(
                Vector3.zero,
                new PlanGoal(MotionIntent.Wave, new Vector3(1.5f, 0f, 0f), stopDistance: 0.5f));

            Assert.That(plan.Segments[plan.Segments.Count - 1].Intent,
                Is.EqualTo(MotionIntent.Wave));
        }

        [Test]
        public void Plan_YawFollowsTheDirectionOfTravel()
        {
            // Straight along +X, so the expected Unity yaw is +90 degrees.
            TrajectoryPlan plan = Plan(
                Vector3.zero, new PlanGoal(MotionIntent.Walk, new Vector3(6f, 0f, 0f)));

            float midYaw = plan.Points[plan.PointCount / 2].Yaw;

            Assert.That(midYaw * Mathf.Rad2Deg, Is.EqualTo(90f).Within(5f));
        }

        [Test]
        public void Plan_HoldsStillWhenAlreadyAtTheGoal()
        {
            TrajectoryPlan plan = Plan(
                Vector3.zero, new PlanGoal(MotionIntent.Idle, Vector3.zero));

            for (int i = 0; i < plan.PointCount; i++)
            {
                Assert.That(plan.Points[i].Speed, Is.EqualTo(0f).Within(1e-3f));
            }
        }

        [Test]
        public void Plan_IdsAreUnique()
        {
            PlanGoal goal = new PlanGoal(MotionIntent.Walk, new Vector3(3f, 0f, 0f));

            string first = Plan(Vector3.zero, goal).PlanId;
            string second = Plan(Vector3.zero, goal).PlanId;

            Assert.That(second, Is.Not.EqualTo(first));
        }
    }
}
