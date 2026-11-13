using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using DigitalHuman.Planning;
using NUnit.Framework;
using UnityEngine;

namespace DigitalHuman.Tests
{
    public sealed class RootMotionPlanningTests
    {
        private static readonly Vector3[] Corner =
        {
            new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 2f), new Vector3(1.5f, 0f, 2f)
        };

        private static PlanRequest Request(
            IReadOnlyList<Vector3> corners = null,
            bool endsAtGoal = true,
            IReadOnlyList<RootSample> history = null,
            LocomotionGait gait = LocomotionGait.Walk,
            MotionIntent behavior = MotionIntent.Walk,
            float goalYaw = 0f)
        {
            PlanGoal goal = new PlanGoal(behavior, new Vector3(1.5f, 0f, 2f), goalYaw);
            return new PlanRequest(
                Vector3.zero, 0f, Vector3.zero, 0f, MotionIntent.Idle, goal,
                null, null, corners, endsAtGoal, history, gait);
        }

        [Test]
        public void PathGeometry_MeasuresAndSamplesAlongThePath()
        {
            List<float> cumulative = new List<float>();
            PathGeometry.BuildArcLengths(Corner, cumulative);

            Assert.That(PathGeometry.Length(Corner), Is.EqualTo(3.5f).Within(1e-5f));
            Assert.That(cumulative, Is.EqualTo(new[] { 0f, 2f, 3.5f }).Within(1e-5f));
            Assert.That(PathGeometry.PointAt(Corner, cumulative, 2.5f),
                Is.EqualTo(new Vector3(0.5f, 0f, 2f)).Using(Vector3EqualityComparer.Instance));
            Assert.That(PathGeometry.PointAt(Corner, cumulative, 99f),
                Is.EqualTo(Corner[2]).Using(Vector3EqualityComparer.Instance));
            Assert.That(PathGeometry.DistanceToPath(Corner, new Vector3(1f, 5f, 1f)),
                Is.EqualTo(1f).Within(1e-5f), "height must not count");
        }

        [Test]
        public void PathGeometry_ProjectNeverMovesBackwards()
        {
            List<float> cumulative = new List<float>();
            PathGeometry.BuildArcLengths(Corner, cumulative);

            float ahead = PathGeometry.Project(Corner, cumulative, new Vector3(0.2f, 0f, 1f), 0f, 5f);
            Assert.That(ahead, Is.EqualTo(1f).Within(1e-4f));

            // The same query, once progress is already past it, stays at the progress made.
            float held = PathGeometry.Project(Corner, cumulative, new Vector3(0.2f, 0f, 1f), 2.5f, 5f);
            Assert.That(held, Is.EqualTo(2.5f).Within(1e-4f));
        }

        [Test]
        public void PathGeometry_TrimEndShortensAcrossVertices()
        {
            List<Vector3> path = new List<Vector3>(Corner);
            PathGeometry.TrimEnd(path, 0.5f);
            Assert.That(path.Count, Is.EqualTo(3));
            Assert.That(path[2], Is.EqualTo(new Vector3(1f, 0f, 2f)).Using(Vector3EqualityComparer.Instance));

            PathGeometry.TrimEnd(path, 1.5f);
            Assert.That(path.Count, Is.EqualTo(2), "the emptied last segment is removed");
            Assert.That(path[1], Is.EqualTo(new Vector3(0f, 0f, 1.5f)).Using(Vector3EqualityComparer.Instance));
        }

        [Test]
        public void SpringFollower_RoundsTheCornerAndSettlesOnTheGoal()
        {
            List<TrajectoryPoint> points = SpringPathFollower.Simulate(
                Corner, true, Vector3.zero, 0f, 1.2f, new SpringFollowerSettings(), 80);

            Assert.That(points.Count, Is.EqualTo(81));
            Assert.That(points[0].Time, Is.EqualTo(0f));
            Assert.That(points[0].Position, Is.EqualTo(Vector3.zero).Using(Vector3EqualityComparer.Instance));

            TrajectoryPoint last = points[points.Count - 1];
            Assert.That(PathGeometry.PlanarDistance(last.Position, Corner[2]), Is.LessThan(0.1f));
            Assert.That(last.Speed, Is.LessThan(0.1f));

            float peak = 0f;
            float worstDeviation = 0f;
            for (int i = 0; i < points.Count; i++)
            {
                peak = Mathf.Max(peak, points[i].Speed);
                worstDeviation = Mathf.Max(
                    worstDeviation, PathGeometry.DistanceToPath(Corner, points[i].Position));
                if (i > 0)
                {
                    Assert.That(points[i].Time - points[i - 1].Time, Is.EqualTo(0.1f).Within(1e-4f));
                }
            }

            Assert.That(points[1].Speed, Is.LessThan(0.6f), "a standing start must ease in");
            Assert.That(peak, Is.LessThanOrEqualTo(1.2f + 1e-3f));
            Assert.That(worstDeviation, Is.LessThan(0.5f), "must stay inside the default corridor");
        }

        [Test]
        public void SpringFollower_KeepsGoingWhenThePathContinues()
        {
            Vector3[] shortPath = { Vector3.zero, new Vector3(0f, 0f, 1f) };
            List<TrajectoryPoint> points = SpringPathFollower.Simulate(
                shortPath, false, new Vector3(0f, 0f, 1.2f), 0f, 1.2f, new SpringFollowerSettings());

            Assert.That(points[points.Count - 1].Position.z, Is.GreaterThan(3f));
        }

        [Test]
        public void SpringFollower_TurnsInPlaceTowardsTheArrivalYaw()
        {
            Vector3[] standing = { new Vector3(2f, 0f, 3f) };
            List<TrajectoryPoint> points = SpringPathFollower.Simulate(
                standing, true, Vector3.zero, 0f, 1.2f, new SpringFollowerSettings(), 30, 10f, 1.2f);

            TrajectoryPoint last = points[points.Count - 1];
            Assert.That(PathGeometry.PlanarDistance(last.Position, standing[0]), Is.LessThan(1e-3f));
            Assert.That(last.Yaw, Is.EqualTo(1.2f).Within(0.02f));
        }

        [Test]
        public void SpringSource_ProducesAPlanInTheContractShape()
        {
            GameObject host = new GameObject("SpringSource");
            try
            {
                SpringPathPlanSource source = host.AddComponent<SpringPathPlanSource>();
                TrajectoryPlan plan = source.RequestPlanAsync(Request(Corner)).Result;

                Assert.That(plan.IsFallback, Is.False);
                Assert.That(plan.PointCount, Is.EqualTo(source.HorizonSteps + 1));
                Assert.That(plan.Segments, Is.Not.Empty);
                Assert.That(plan.Segments[0].Start, Is.EqualTo(0f));
                for (int i = 1; i < plan.Segments.Count; i++)
                {
                    Assert.That(plan.Segments[i].Start,
                        Is.EqualTo(plan.Segments[i - 1].End).Within(1e-4f), "segments must tile");
                }

                // Without a NavMesh path the source heads straight for the goal.
                TrajectoryPlan direct = source.RequestPlanAsync(Request()).Result;
                Vector3 end = direct.Points[direct.PointCount - 1].Position;
                Assert.That(Vector3.Dot(end, new Vector3(1.5f, 0f, 2f).normalized), Is.GreaterThan(0.5f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void RootHistoryBuffer_ResamplesToExactSpacingEndingNow()
        {
            // Walking +z at 2 m/s, recorded on a 10 Hz clock; "now" falls between ticks.
            RootHistoryBuffer buffer = new RootHistoryBuffer(4);
            for (int i = 0; i < 6; i++)
            {
                float time = 0.1f * i;
                buffer.Add(new RootSample(new Vector3(0f, 0f, 2f * time), 0f), time);
            }

            const float now = 0.53f;
            List<RootSample> history = new List<RootSample>();
            buffer.Resample(now, new RootSample(new Vector3(0f, 0f, 2f * now), 0f), 0.1f, 3, history);

            Assert.That(history.Count, Is.EqualTo(3));
            Assert.That(history[2].Position.z, Is.EqualTo(1.06f).Within(1e-4f));
            // Equal spacing is what makes the planner's velocity estimate correct.
            Assert.That(history[2].Position.z - history[1].Position.z, Is.EqualTo(0.2f).Within(1e-4f));
            Assert.That(history[1].Position.z - history[0].Position.z, Is.EqualTo(0.2f).Within(1e-4f));
        }

        [Test]
        public void RootHistoryBuffer_PadsWithTheOldestPoseAndHandlesAnEmptyBuffer()
        {
            RootHistoryBuffer buffer = new RootHistoryBuffer(4);
            List<RootSample> history = new List<RootSample>();
            RootSample here = new RootSample(new Vector3(1f, 0f, 1f), 0.5f);

            buffer.Resample(3f, here, 0.1f, 3, history);
            Assert.That(history.Count, Is.EqualTo(3));
            Assert.That(history[0].Yaw, Is.EqualTo(0.5f));

            buffer.Add(new RootSample(Vector3.zero, 0f), 2.95f);
            buffer.Resample(3f, here, 0.1f, 3, history);
            Assert.That(history[0].Position, Is.EqualTo(Vector3.zero).Using(Vector3EqualityComparer.Instance),
                "before the first recording the character is taken to have stood still");
            Assert.That(history[2].Position, Is.EqualTo(here.Position).Using(Vector3EqualityComparer.Instance));
        }

        [Test]
        public void RequestJson_MatchesTheServiceContract()
        {
            RootSample[] history =
            {
                new RootSample(new Vector3(0f, 0f, -0.1f), 0f),
                new RootSample(Vector3.zero, 0.25f)
            };
            string json = PlanRequestJson.Build(
                Request(Corner, false, history, LocomotionGait.Run), 8);

            Assert.That(json, Is.EqualTo(
                "{\"history\":[{\"position\":[0,0,-0.1],\"yaw\":0},{\"position\":[0,0,0],\"yaw\":0.25}]," +
                "\"path\":[[0,0,0],[0,0,2],[1.5,0,2]],\"path_ends_at_goal\":false," +
                "\"gait\":\"run\",\"corridor_half_width\":0.5,\"candidates\":8}"));
        }

        [Test]
        public void RequestJson_IsLocaleIndependentAndFillsDefaults()
        {
            CultureInfo previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                // German formats 0.5 as "0,5", which would silently corrupt the payload.
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                string json = PlanRequestJson.Build(Request(), 16);

                Assert.That(json, Does.Contain("\"corridor_half_width\":0.5"));
                // No history: the current pose. No path: a straight line to the goal.
                Assert.That(json, Does.StartWith("{\"history\":[{\"position\":[0,0,0],\"yaw\":0}],"));
                Assert.That(json, Does.Contain("\"path\":[[0,0,0],[1.5,0,2]],\"path_ends_at_goal\":true"));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        [Test]
        public void RequestJson_RejectsNonFiniteValues()
        {
            Vector3[] broken = { Vector3.zero, new Vector3(float.NaN, 0f, 1f) };
            Assert.Throws<ArgumentException>(() => PlanRequestJson.Build(Request(broken), 16));
        }

        [Test]
        public void HttpSource_ReturnsTheServicePlanAndRecordsItsOrigin()
        {
            const string body =
                "{\"plan_id\":\"plan-abc\",\"horizon\":0.1,\"trajectory\":[" +
                "{\"t\":0.0,\"position\":[0,0,0],\"yaw\":0,\"velocity\":[0,0,1],\"angular_velocity\":0,\"speed\":1}," +
                "{\"t\":0.1,\"position\":[0,0,0.1],\"yaw\":0,\"velocity\":[0,0,1],\"angular_velocity\":0,\"speed\":1}]," +
                "\"segments\":[{\"start\":0.0,\"end\":0.2,\"action\":\"walk\",\"prompt\":\"p\"}]," +
                "\"score\":3.0,\"fallback\":false,\"source\":\"model\"," +
                "\"rejected_candidates\":3,\"candidates\":16,\"latency_ms\":41.5}";
            GameObject host = new GameObject("HttpSource");
            try
            {
                FakeTransport transport = new FakeTransport(body);
                HttpTrajectoryPlanSource source = host.AddComponent<HttpTrajectoryPlanSource>();
                source.Configure("http://planner.test:8787/", transport, null, false);

                TrajectoryPlan plan = source.RequestPlanAsync(Request(Corner)).Result;

                Assert.That(plan.PlanId, Is.EqualTo("plan-abc"));
                Assert.That(plan.PointCount, Is.EqualTo(2));
                Assert.That(transport.LastUrl, Is.EqualTo("http://planner.test:8787/plan"));
                Assert.That(transport.LastBody, Does.Contain("\"path\":[[0,0,0],[0,0,2],[1.5,0,2]]"));
                Assert.That(source.LastOrigin, Is.EqualTo(PlanOrigin.Model));
                Assert.That(source.ModelPlanCount, Is.EqualTo(1));
                Assert.That(source.LastServiceLatencyMs, Is.EqualTo(41.5f).Within(1e-3f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void HttpSource_FallsBackWhenTheServiceIsDown()
        {
            GameObject host = new GameObject("HttpSource");
            try
            {
                SpringPathPlanSource spring = host.AddComponent<SpringPathPlanSource>();
                HttpTrajectoryPlanSource source = host.AddComponent<HttpTrajectoryPlanSource>();
                source.Configure(
                    "http://planner.test:8787", new FakeTransport(new PlanServiceException("refused")),
                    spring, false);

                TrajectoryPlan plan = source.RequestPlanAsync(Request(Corner)).Result;

                Assert.That(plan.PlanId, Does.StartWith("spring-"));
                Assert.That(source.LastOrigin, Is.EqualTo(PlanOrigin.LocalFallbackAfterError));
                Assert.That(source.ErrorFallbackCount, Is.EqualTo(1));
                Assert.That(source.LastError, Does.Contain("refused"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void HttpSource_SurfacesTheFailureWhenThereIsNoFallback()
        {
            GameObject host = new GameObject("HttpSource");
            try
            {
                HttpTrajectoryPlanSource source = host.AddComponent<HttpTrajectoryPlanSource>();
                source.Configure("http://planner.test:8787", new FakeTransport("not json"), null, false);

                AggregateException error = Assert.Throws<AggregateException>(
                    () => { TrajectoryPlan unused = source.RequestPlanAsync(Request(Corner)).Result; });
                Assert.That(error.InnerException, Is.InstanceOf<PlanServiceException>());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void MetricsLogger_RowsUseTheInvariantCultureAndStayOneCellPerField()
        {
            CultureInfo previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                string row = RootMotionMetricsLogger.FormatRow(
                    1.5f, new Vector3(0.25f, 9f, -2f), 0.5f, 1.2f, -0.3f, 0.1f, true,
                    "http:Model,odd", "plan-1", 0.125f);

                Assert.That(row, Is.EqualTo(
                    "1.500,0.2500,-2.0000,0.5000,1.2000,-0.3000,0.1000,1,http:Model;odd,plan-1,0.1250"));
                Assert.That(row.Split(',').Length,
                    Is.EqualTo(RootMotionMetricsLogger.Header.Split(',').Length));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        [Test]
        public void HttpSource_DefaultsToAHalfSecondTimeoutAndPassesItInMilliseconds()
        {
            GameObject host = new GameObject("HttpSource");
            try
            {
                FakeTransport transport = new FakeTransport(ModelPlanJson("plan-t", 0f, 0.2f, 0.2f));
                HttpTrajectoryPlanSource source = host.AddComponent<HttpTrajectoryPlanSource>();
                source.Configure("http://planner.test:8787", transport, null, false);

                Assert.That(source.TimeoutMilliseconds, Is.EqualTo(500));
                Assert.That(source.RetryAfterSeconds, Is.EqualTo(5f));
                TrajectoryPlan unused = source.RequestPlanAsync(Request(Corner)).Result;
                Assert.That(transport.LastTimeoutMilliseconds, Is.EqualTo(500));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void HttpSource_SkipsTheServiceForTheRetryWindowAfterATransportFailure()
        {
            GameObject host = new GameObject("HttpSource");
            try
            {
                double now = 100.0;
                SpringPathPlanSource spring = host.AddComponent<SpringPathPlanSource>();
                FakeTransport transport = new FakeTransport(
                    new PlanServiceException("timed out after 500 ms", 0, true));
                HttpTrajectoryPlanSource source = host.AddComponent<HttpTrajectoryPlanSource>();
                source.Configure("http://planner.test:8787", transport, spring, false);
                source.ConfigureResilience(500, 5f);
                source.SetClock(() => now);

                TrajectoryPlan first = source.RequestPlanAsync(Request(Corner)).Result;
                Assert.That(first.PlanId, Does.StartWith("spring-"));
                Assert.That(transport.CallCount, Is.EqualTo(1));
                Assert.That(source.ErrorFallbackCount, Is.EqualTo(1));
                Assert.That(source.IsServiceSkipped, Is.True);

                // Inside the window: answered by the fallback, the service is not contacted.
                now = 104.9;
                transport.Respond(ModelPlanJson("plan-ok", 0f, 0.2f, 0.2f));
                TrajectoryPlan skipped = source.RequestPlanAsync(Request(Corner)).Result;
                Assert.That(skipped.PlanId, Does.StartWith("spring-"));
                Assert.That(transport.CallCount, Is.EqualTo(1));
                Assert.That(source.SkippedRequestCount, Is.EqualTo(1));
                Assert.That(source.LastOrigin, Is.EqualTo(PlanOrigin.LocalFallbackAfterError));

                // After the window: one more try, and the recovered service is used again.
                now = 105.1;
                TrajectoryPlan recovered = source.RequestPlanAsync(Request(Corner)).Result;
                Assert.That(recovered.PlanId, Is.EqualTo("plan-ok"));
                Assert.That(transport.CallCount, Is.EqualTo(2));
                Assert.That(source.LastOrigin, Is.EqualTo(PlanOrigin.Model));
                Assert.That(source.IsServiceSkipped, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void HttpSource_FailingAgainAfterTheWindowReopensIt()
        {
            GameObject host = new GameObject("HttpSource");
            try
            {
                double now = 0.0;
                SpringPathPlanSource spring = host.AddComponent<SpringPathPlanSource>();
                FakeTransport transport = new FakeTransport(new PlanServiceException("refused"));
                HttpTrajectoryPlanSource source = host.AddComponent<HttpTrajectoryPlanSource>();
                source.Configure("http://planner.test:8787", transport, spring, false);
                source.ConfigureResilience(500, 5f);
                source.SetClock(() => now);

                TrajectoryPlan unused = source.RequestPlanAsync(Request(Corner)).Result;
                now = 5.5;
                unused = source.RequestPlanAsync(Request(Corner)).Result;
                Assert.That(transport.CallCount, Is.EqualTo(2), "one retry once the window has passed");
                now = 6.0;
                unused = source.RequestPlanAsync(Request(Corner)).Result;
                Assert.That(transport.CallCount, Is.EqualTo(2), "the failed retry starts a new window");
                Assert.That(source.SkippedRequestCount, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void HttpSource_NavMeshRejectDoesNotSkipTheService()
        {
            GameObject host = new GameObject("HttpSource");
            try
            {
                double now = 0.0;
                SpringPathPlanSource spring = host.AddComponent<SpringPathPlanSource>();
                // Far from any NavMesh, so the walkability check must reject it.
                FakeTransport transport = new FakeTransport(ModelPlanJson("plan-off", 1000f, 1000.2f, 0.2f));
                HttpTrajectoryPlanSource source = host.AddComponent<HttpTrajectoryPlanSource>();
                source.Configure("http://planner.test:8787", transport, spring, true);
                source.SetClock(() => now);

                TrajectoryPlan first = source.RequestPlanAsync(Request(Corner)).Result;
                Assert.That(first.PlanId, Does.StartWith("spring-"));
                Assert.That(source.NavMeshRejectCount, Is.EqualTo(1));
                Assert.That(source.IsServiceSkipped, Is.False);

                now = 0.1;
                TrajectoryPlan unused = source.RequestPlanAsync(Request(Corner)).Result;
                Assert.That(transport.CallCount, Is.EqualTo(2));
                Assert.That(source.SkippedRequestCount, Is.EqualTo(0));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void Follower_DiscardsAStalePlanAndRequestsAgainImmediately()
        {
            using (FollowerRig rig = new FollowerRig())
            {
                rig.Follower.BeginPlanTo(new PlanGoal(MotionIntent.Walk, new Vector3(10f, 0f, 0f)));
                Assert.That(rig.Source.Pending.Count, Is.EqualTo(1));

                // Horizon 2 s, so the latency allowance is 1 s; this answer is 2 s old.
                rig.Now = 2f;
                rig.Source.Complete(0, LinePlan("p1", 0f, 2f, 2f));
                Assert.That(rig.Follower.IsFollowing, Is.False, "a stale plan must not be executed");
                Assert.That(rig.Follower.StalePlanDiscardCount, Is.EqualTo(1));
                Assert.That(rig.Source.Pending.Count, Is.EqualTo(2), "re-requested at once");

                rig.Now = 2.05f;
                rig.Source.Complete(1, LinePlan("p2", 0f, 2f, 2f));
                Assert.That(rig.Follower.IsFollowing, Is.True);
                Assert.That(rig.Follower.ActivePlan.PlanId, Is.EqualTo("p2"));
                Assert.That(rig.Follower.PlanElapsed, Is.EqualTo(0.05f).Within(1e-4f));
            }
        }

        [Test]
        public void Follower_DiscardsAtMostOnceInARow()
        {
            using (FollowerRig rig = new FollowerRig())
            {
                rig.Follower.BeginPlanTo(new PlanGoal(MotionIntent.Walk, new Vector3(10f, 0f, 0f)));
                rig.Now = 2f;
                rig.Source.Complete(0, LinePlan("p1", 0f, 2f, 2f));

                // The re-request is slow too: adopted anyway, starting at the allowance.
                rig.Now = 3.5f;
                rig.Source.Complete(1, LinePlan("p2", 0f, 2f, 2f));
                Assert.That(rig.Follower.IsFollowing, Is.True);
                Assert.That(rig.Follower.ActivePlan.PlanId, Is.EqualTo("p2"));
                Assert.That(rig.Follower.PlanElapsed, Is.EqualTo(1f).Within(1e-4f));
                Assert.That(rig.Source.Pending.Count, Is.EqualTo(2), "no third request");
                Assert.That(rig.Follower.StalePlanDiscardCount, Is.EqualTo(1));
            }
        }

        [Test]
        public void Follower_IgnoresAPlanThatArrivesAfterTheGoalIsReached()
        {
            using (FollowerRig rig = new FollowerRig(replanInterval: 0.2f))
            {
                int goalsReached = 0;
                rig.Follower.GoalReached += () => goalsReached++;
                rig.Follower.BeginPlanTo(new PlanGoal(MotionIntent.Walk, new Vector3(1f, 0f, 0f)));
                rig.Source.Complete(0, LinePlan("p1", 0f, 2f, 2f));

                // Walk at 1 m/s; the 0.2 s replan leaves a request in flight at arrival.
                for (int i = 0; i < 12 && rig.Follower.IsFollowing; i++)
                {
                    rig.Now += 0.1f;
                    rig.Follower.Advance(0.1f);
                }

                Assert.That(goalsReached, Is.EqualTo(1));
                Assert.That(rig.Source.Pending.Count, Is.EqualTo(2), "a replan was in flight");
                Assert.That(rig.Source.Tokens[1].IsCancellationRequested, Is.True,
                    "arrival must cancel the pending request");
                Vector3 arrived = rig.Host.transform.position;

                rig.Source.Complete(1, LinePlan("late", arrived.x, arrived.x + 3f, 2f));
                for (int i = 0; i < 5; i++)
                {
                    rig.Now += 0.1f;
                    rig.Follower.Advance(0.1f);
                }

                Assert.That(rig.Follower.IsFollowing, Is.False);
                Assert.That(goalsReached, Is.EqualTo(1));
                Assert.That(rig.Host.transform.position, Is.EqualTo(arrived).Using(Vector3EqualityComparer.Instance),
                    "a late plan must not move the character");
            }
        }

        /// <summary>A service response: a straight walk along +x from x0 to x1 over the horizon.</summary>
        private static string ModelPlanJson(string id, float x0, float x1, float horizon)
        {
            return LinePlanJson(id, x0, x1, horizon)
                .Replace("\"fallback\":false", "\"fallback\":false,\"source\":\"model\"");
        }

        private static TrajectoryPlan LinePlan(string id, float x0, float x1, float horizon)
        {
            return TrajectoryPlan.FromJson(LinePlanJson(id, x0, x1, horizon));
        }

        private static string LinePlanJson(string id, float x0, float x1, float horizon)
        {
            int steps = Mathf.Max(1, Mathf.RoundToInt(horizon / 0.1f));
            float speed = (x1 - x0) / horizon;
            System.Text.StringBuilder points = new System.Text.StringBuilder();
            for (int i = 0; i <= steps; i++)
            {
                float t = horizon * i / steps;
                float x = Mathf.Lerp(x0, x1, (float)i / steps);
                if (i > 0)
                {
                    points.Append(',');
                }

                points.Append(string.Format(CultureInfo.InvariantCulture,
                    "{{\"t\":{0},\"position\":[{1},0,0],\"yaw\":1.5707964,\"velocity\":[{2},0,0]," +
                    "\"angular_velocity\":0,\"speed\":{3}}}", t, x, speed, Mathf.Abs(speed)));
            }

            return string.Format(CultureInfo.InvariantCulture,
                "{{\"plan_id\":\"{0}\",\"horizon\":{1},\"trajectory\":[{2}]," +
                "\"segments\":[{{\"start\":0.0,\"end\":{1},\"action\":\"walk\",\"prompt\":\"p\"}}]," +
                "\"score\":1.0,\"fallback\":false}}", id, horizon, points);
        }

        /// <summary>
        /// A follower on a fresh object with a source whose answers the test releases by hand,
        /// an injected clock, and no NavMesh involvement. Continuations run inline so a released
        /// answer is adopted before the next line of the test.
        /// </summary>
        private sealed class FollowerRig : IDisposable
        {
            private readonly SynchronizationContext previousContext;

            public GameObject Host { get; }
            public TrajectoryPlanFollower Follower { get; }
            public ControlledSource Source { get; } = new ControlledSource();
            public float Now;

            public FollowerRig(float replanInterval = 1f)
            {
                previousContext = SynchronizationContext.Current;
                SynchronizationContext.SetSynchronizationContext(null);

                Host = new GameObject("Follower");
                Follower = Host.AddComponent<TrajectoryPlanFollower>();
                UnityEditor.SerializedObject serialized = new UnityEditor.SerializedObject(Follower);
                serialized.FindProperty("snapToNavMesh").boolValue = false;
                serialized.FindProperty("sendNavMeshPath").boolValue = false;
                serialized.FindProperty("replanInterval").floatValue = replanInterval;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Follower.Configure(null, null, null);
                Follower.SetPlanSource(Source);
                Follower.SetClock(() => Now);
            }

            public void Dispose()
            {
                UnityEngine.Object.DestroyImmediate(Host);
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
        }

        private sealed class ControlledSource : ITrajectoryPlanSource
        {
            public readonly List<TaskCompletionSource<TrajectoryPlan>> Pending =
                new List<TaskCompletionSource<TrajectoryPlan>>();
            public readonly List<CancellationToken> Tokens = new List<CancellationToken>();

            public string SourceName => "controlled";

            public Task<TrajectoryPlan> RequestPlanAsync(
                PlanRequest request,
                CancellationToken cancellationToken = default)
            {
                TaskCompletionSource<TrajectoryPlan> answer = new TaskCompletionSource<TrajectoryPlan>();
                Pending.Add(answer);
                Tokens.Add(cancellationToken);
                return answer.Task;
            }

            public void Complete(int index, TrajectoryPlan plan)
            {
                Pending[index].SetResult(plan);
            }
        }

        private sealed class FakeTransport : IPlanHttpTransport
        {
            private string body;
            private Exception failure;

            public string LastUrl { get; private set; }
            public string LastBody { get; private set; }
            public int LastTimeoutMilliseconds { get; private set; }
            public int CallCount { get; private set; }

            public FakeTransport(string responseBody)
            {
                body = responseBody;
            }

            public FakeTransport(Exception error)
            {
                failure = error;
            }

            /// <summary>From now on, answer with <paramref name="responseBody"/>.</summary>
            public void Respond(string responseBody)
            {
                body = responseBody;
                failure = null;
            }

            public Task<string> PostJsonAsync(
                string url,
                string jsonBody,
                int timeoutMilliseconds,
                CancellationToken cancellationToken = default)
            {
                CallCount++;
                LastUrl = url;
                LastBody = jsonBody;
                LastTimeoutMilliseconds = timeoutMilliseconds;
                return failure != null
                    ? Task.FromException<string>(failure)
                    : Task.FromResult(body);
            }
        }

        private sealed class Vector3EqualityComparer : IEqualityComparer<Vector3>
        {
            public static readonly Vector3EqualityComparer Instance = new Vector3EqualityComparer();

            public bool Equals(Vector3 a, Vector3 b)
            {
                return (a - b).sqrMagnitude < 1e-8f;
            }

            public int GetHashCode(Vector3 value)
            {
                return 0;
            }
        }
    }
}
