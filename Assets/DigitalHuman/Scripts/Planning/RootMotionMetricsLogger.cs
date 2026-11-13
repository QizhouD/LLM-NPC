using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace DigitalHuman.Planning
{
    /// <summary>
    /// Writes what the root actually did, at the control rate, to a CSV file.
    /// </summary>
    /// <remarks>
    /// This is the measurement half of using the scene as an evaluation platform: run the
    /// same scripted route with different plan sources and compare the logs. Speed,
    /// acceleration and turn rate are computed from the executed transform, not copied
    /// from the plan, so they include the effects of replanning and NavMesh snapping.
    /// Columns match the quantities the planner repo's evaluator reports.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class RootMotionMetricsLogger : MonoBehaviour
    {
        public const string Header =
            "time,x,z,yaw,speed,acceleration,turn_rate,following,source,plan_id,path_deviation";

        [SerializeField] private TrajectoryPlanFollower follower;
        [Tooltip("Defaults to the follower's transform.")]
        [SerializeField] private Transform root;
        [SerializeField, Min(1f)] private float controlHz = 10f;
        [Tooltip("Folder under Application.persistentDataPath.")]
        [SerializeField] private string folder = "rootmotion-logs";
        [Tooltip("Label written into the file name, e.g. the plan source under test.")]
        [SerializeField] private string runLabel = "run";

        private StreamWriter writer;
        // Kept from the last sample: when Play mode ends, the follower may be destroyed
        // before this logger writes its footer, and Unity then reports it as null.
        private HttpTrajectoryPlanSource httpSource;
        private int stalePlansDiscarded;
        private float accumulator;
        private float elapsed;
        private Vector3 previousPosition;
        private float previousYaw;
        private float previousSpeed;
        private bool hasPrevious;

        public string LogPath { get; private set; } = string.Empty;
        public int RowCount { get; private set; }
        public int FallbackPlanCount { get; private set; }
        public int FailedRequestCount { get; private set; }
        public int GoalReachedCount { get; private set; }

        private void Awake()
        {
            if (follower == null)
            {
                follower = GetComponent<TrajectoryPlanFollower>();
            }
        }

        private void OnEnable()
        {
            if (follower != null)
            {
                follower.FallbackPlanReceived += HandleFallback;
                follower.PlanRequestFailed += HandleFailure;
                follower.GoalReached += HandleGoalReached;
            }

            string directory = Path.Combine(Application.persistentDataPath, folder);
            Directory.CreateDirectory(directory);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            LogPath = Path.Combine(directory, $"{SanitizeLabel(runLabel)}-{stamp}.csv");
            writer = new StreamWriter(LogPath, false, new UTF8Encoding(false));
            writer.WriteLine(Header);
            accumulator = 0f;
            elapsed = 0f;
            hasPrevious = false;
            RowCount = 0;
        }

        private void OnDisable()
        {
            if (follower != null)
            {
                follower.FallbackPlanReceived -= HandleFallback;
                follower.PlanRequestFailed -= HandleFailure;
                follower.GoalReached -= HandleGoalReached;
            }

            if (writer != null)
            {
                CacheSourceCounters();
                writer.WriteLine(FormatFooter(
                    FallbackPlanCount, FailedRequestCount, GoalReachedCount,
                    stalePlansDiscarded, httpSource));
                writer.Dispose();
                writer = null;
                Debug.Log($"[Planning] Root-motion log written to {LogPath}", this);
            }
        }

        private void LateUpdate()
        {
            float interval = 1f / controlHz;
            accumulator += Time.deltaTime;
            while (accumulator >= interval)
            {
                accumulator -= interval;
                elapsed += interval;
                Sample(interval);
            }
        }

        private void Sample(float interval)
        {
            Transform source = root != null ? root : (follower != null ? follower.transform : transform);
            Vector3 position = source.position;
            float yaw = source.rotation.eulerAngles.y * Mathf.Deg2Rad;

            float speed = 0f;
            float acceleration = 0f;
            float turnRate = 0f;
            if (hasPrevious)
            {
                speed = PathGeometry.PlanarDistance(position, previousPosition) / interval;
                acceleration = (speed - previousSpeed) / interval;
                turnRate = SpringPathFollower.ShortestAngle(previousYaw, yaw) / interval;
            }

            CacheSourceCounters();
            bool following = follower != null && follower.IsFollowing;
            string sourceName = follower != null ? follower.SourceName : string.Empty;
            string planId = following && follower.ActivePlan != null ? follower.ActivePlan.PlanId : string.Empty;
            float deviation = following && follower.LastPathCorners.Count > 0
                ? PathGeometry.DistanceToPath(follower.LastPathCorners, position)
                : 0f;

            writer.WriteLine(FormatRow(
                elapsed, position, yaw, speed, acceleration, turnRate, following, sourceName, planId, deviation));
            RowCount++;

            previousPosition = position;
            previousYaw = yaw;
            previousSpeed = speed;
            hasPrevious = true;
        }

        private void CacheSourceCounters()
        {
            if (follower == null)
            {
                return;
            }

            stalePlansDiscarded = follower.StalePlanDiscardCount;
            if (follower.PlanSource is HttpTrajectoryPlanSource http)
            {
                httpSource = http;
            }
        }

        /// <summary>
        /// Footer comment line. The first three counters are follower events: a fallback plan
        /// the follower refused to execute, a request that surfaced an exception, and goals
        /// reached. An HTTP source handles its own fallbacks without raising either follower
        /// event, so when it is the plan source its counters are appended as well.
        /// </summary>
        public static string FormatFooter(
            int fallbackPlans,
            int failedRequests,
            int goalsReached,
            int stalePlansDiscarded,
            HttpTrajectoryPlanSource http)
        {
            // Integers only, so the current culture cannot change the text.
            string footer =
                $"# fallback_plans={fallbackPlans},failed_requests={failedRequests}," +
                $"goals_reached={goalsReached},stale_plans_discarded={stalePlansDiscarded}";
            // A reference check, not Unity's null: the source may already be destroyed when
            // Play mode ends, and its counters are plain properties that remain readable.
            if (!ReferenceEquals(http, null))
            {
                footer +=
                    $",http_requests={http.RequestCount},http_model={http.ModelPlanCount}," +
                    $"http_service_fallback={http.ServiceFallbackCount}," +
                    $"http_error_fallback={http.ErrorFallbackCount}," +
                    $"http_skipped_after_error={http.SkippedRequestCount}," +
                    $"http_navmesh_reject={http.NavMeshRejectCount}";
            }

            return footer;
        }

        /// <summary>One CSV row in the invariant culture. Public so the format is testable.</summary>
        public static string FormatRow(
            float time,
            Vector3 position,
            float yaw,
            float speed,
            float acceleration,
            float turnRate,
            bool following,
            string sourceName,
            string planId,
            float pathDeviation)
        {
            CultureInfo culture = CultureInfo.InvariantCulture;
            return string.Join(
                ",",
                time.ToString("F3", culture),
                position.x.ToString("F4", culture),
                position.z.ToString("F4", culture),
                yaw.ToString("F4", culture),
                speed.ToString("F4", culture),
                acceleration.ToString("F4", culture),
                turnRate.ToString("F4", culture),
                following ? "1" : "0",
                SanitizeCell(sourceName),
                SanitizeCell(planId),
                pathDeviation.ToString("F4", culture));
        }

        private static string SanitizeCell(string value)
        {
            return string.IsNullOrEmpty(value)
                ? string.Empty
                : value.Replace(',', ';').Replace('\n', ' ').Replace('\r', ' ');
        }

        private static string SanitizeLabel(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "run";
            }

            StringBuilder cleaned = new StringBuilder(value.Length);
            foreach (char character in value.Trim())
            {
                cleaned.Append(char.IsLetterOrDigit(character) || character == '-' || character == '_'
                    ? character
                    : '_');
            }

            return cleaned.ToString();
        }

        private void HandleFallback(TrajectoryPlan plan)
        {
            FallbackPlanCount++;
        }

        private void HandleFailure(string message)
        {
            FailedRequestCount++;
        }

        private void HandleGoalReached()
        {
            GoalReachedCount++;
        }
    }
}
