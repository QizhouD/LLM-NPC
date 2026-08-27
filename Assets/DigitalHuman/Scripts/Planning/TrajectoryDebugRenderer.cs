using System.Collections.Generic;
using UnityEngine;

namespace DigitalHuman.Planning
{
    /// <summary>
    /// Visualises the active plan: the path coloured per motion intent, the 10 Hz control
    /// points, segment boundaries and the obstacles the planner was given.
    /// </summary>
    /// <remarks>
    /// Uses <see cref="LineRenderer"/> rather than gizmos so the overlay appears in the Game
    /// view and therefore in recorded video, which is the point of having it.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class TrajectoryDebugRenderer : MonoBehaviour
    {
        [SerializeField] private TrajectoryPlanFollower follower;
        [SerializeField] private bool visible = true;
        [SerializeField, Min(0.005f)] private float lineWidth = 0.04f;
        [Tooltip("Lift the overlay above the floor so it does not z-fight with the ground.")]
        [SerializeField] private float groundOffset = 0.03f;

        [Header("Intent colours")]
        [SerializeField] private Color idleColor = new Color(0.6f, 0.6f, 0.6f);
        [SerializeField] private Color walkColor = new Color(0.25f, 0.85f, 0.35f);
        [SerializeField] private Color turnColor = new Color(0.25f, 0.75f, 0.95f);
        [SerializeField] private Color stopColor = new Color(0.95f, 0.8f, 0.25f);
        [SerializeField] private Color waveColor = new Color(0.9f, 0.4f, 0.85f);
        [SerializeField] private Color obstacleColor = new Color(0.95f, 0.3f, 0.3f);

        private readonly List<LineRenderer> segmentLines = new List<LineRenderer>();
        private readonly List<LineRenderer> obstacleLines = new List<LineRenderer>();
        private Transform overlayRoot;
        private Material sharedMaterial;
        private TrajectoryPlan renderedPlan;

        private void Awake()
        {
            if (follower == null)
            {
                follower = GetComponent<TrajectoryPlanFollower>();
            }

            overlayRoot = new GameObject("TrajectoryOverlay").transform;
            overlayRoot.SetParent(null);
            sharedMaterial = new Material(Shader.Find("Sprites/Default"));
        }

        private void OnEnable()
        {
            if (follower != null)
            {
                follower.PlanStarted += HandlePlanStarted;
                follower.FallbackPlanReceived += HandleFallback;
            }
        }

        private void OnDisable()
        {
            if (follower != null)
            {
                follower.PlanStarted -= HandlePlanStarted;
                follower.FallbackPlanReceived -= HandleFallback;
            }

            SetOverlayActive(false);
        }

        private void OnDestroy()
        {
            if (overlayRoot != null)
            {
                Destroy(overlayRoot.gameObject);
            }

            if (sharedMaterial != null)
            {
                Destroy(sharedMaterial);
            }
        }

        private void LateUpdate()
        {
            if (follower == null)
            {
                return;
            }

            bool shouldShow = visible && follower.IsFollowing && follower.ActivePlan != null;
            SetOverlayActive(shouldShow);
            if (!shouldShow)
            {
                return;
            }

            if (!ReferenceEquals(renderedPlan, follower.ActivePlan))
            {
                renderedPlan = follower.ActivePlan;
                RebuildPlanLines(renderedPlan);
                RebuildObstacleLines(follower.ActiveObstacles);
            }
        }

        public void SetVisible(bool value)
        {
            visible = value;
        }

        private void HandlePlanStarted(TrajectoryPlan plan)
        {
            renderedPlan = plan;
            RebuildPlanLines(plan);
            RebuildObstacleLines(follower.ActiveObstacles);
        }

        private void HandleFallback(TrajectoryPlan plan)
        {
            renderedPlan = null;
            SetOverlayActive(false);
        }

        private void RebuildPlanLines(TrajectoryPlan plan)
        {
            IReadOnlyList<MotionSegment> segments = plan.Segments;
            int lineIndex = 0;

            if (segments.Count == 0)
            {
                LineRenderer whole = GetSegmentLine(lineIndex++);
                FillLine(whole, plan, 0f, plan.Horizon, ColorFor(MotionIntent.Idle));
            }
            else
            {
                for (int i = 0; i < segments.Count; i++)
                {
                    LineRenderer line = GetSegmentLine(lineIndex++);
                    FillLine(
                        line,
                        plan,
                        segments[i].Start,
                        segments[i].End,
                        ColorFor(segments[i].Intent));
                }
            }

            for (int i = lineIndex; i < segmentLines.Count; i++)
            {
                segmentLines[i].gameObject.SetActive(false);
            }
        }

        private void FillLine(
            LineRenderer line,
            TrajectoryPlan plan,
            float startTime,
            float endTime,
            Color color)
        {
            List<Vector3> positions = new List<Vector3>();
            IReadOnlyList<TrajectoryPoint> points = plan.Points;
            for (int i = 0; i < points.Count; i++)
            {
                float t = points[i].Time;
                if (t < startTime || t > endTime)
                {
                    continue;
                }

                positions.Add(points[i].Position + Vector3.up * groundOffset);
            }

            // A one-point segment would draw nothing, so extend it to the sampled endpoint.
            if (positions.Count == 1)
            {
                positions.Add(plan.Sample(endTime).Position + Vector3.up * groundOffset);
            }

            line.gameObject.SetActive(positions.Count >= 2);
            if (positions.Count < 2)
            {
                return;
            }

            line.positionCount = positions.Count;
            line.SetPositions(positions.ToArray());
            line.startColor = color;
            line.endColor = color;
            line.widthMultiplier = lineWidth;
        }

        private void RebuildObstacleLines(IReadOnlyList<PlanObstacle> obstacles)
        {
            int index = 0;
            if (obstacles != null)
            {
                for (int i = 0; i < obstacles.Count; i++)
                {
                    LineRenderer line = GetObstacleLine(index++);
                    DrawCircle(line, obstacles[i].Center, obstacles[i].Radius, obstacleColor);
                }
            }

            for (int i = index; i < obstacleLines.Count; i++)
            {
                obstacleLines[i].gameObject.SetActive(false);
            }
        }

        private void DrawCircle(LineRenderer line, Vector3 center, float radius, Color color)
        {
            const int steps = 32;
            Vector3[] positions = new Vector3[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                float angle = i / (float)steps * Mathf.PI * 2f;
                positions[i] = new Vector3(
                    center.x + Mathf.Cos(angle) * radius,
                    center.y + groundOffset,
                    center.z + Mathf.Sin(angle) * radius);
            }

            line.gameObject.SetActive(true);
            line.positionCount = positions.Length;
            line.SetPositions(positions);
            line.startColor = color;
            line.endColor = color;
            line.widthMultiplier = lineWidth * 0.6f;
        }

        private LineRenderer GetSegmentLine(int index)
        {
            while (segmentLines.Count <= index)
            {
                segmentLines.Add(CreateLine($"Segment{segmentLines.Count}"));
            }

            segmentLines[index].gameObject.SetActive(true);
            return segmentLines[index];
        }

        private LineRenderer GetObstacleLine(int index)
        {
            while (obstacleLines.Count <= index)
            {
                obstacleLines.Add(CreateLine($"Obstacle{obstacleLines.Count}"));
            }

            obstacleLines[index].gameObject.SetActive(true);
            return obstacleLines[index];
        }

        private LineRenderer CreateLine(string name)
        {
            GameObject holder = new GameObject(name);
            holder.transform.SetParent(overlayRoot, false);
            LineRenderer line = holder.AddComponent<LineRenderer>();
            line.material = sharedMaterial;
            line.useWorldSpace = true;
            line.textureMode = LineTextureMode.Stretch;
            line.numCornerVertices = 2;
            line.numCapVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.widthMultiplier = lineWidth;
            return line;
        }

        private void SetOverlayActive(bool active)
        {
            if (overlayRoot != null && overlayRoot.gameObject.activeSelf != active)
            {
                overlayRoot.gameObject.SetActive(active);
            }
        }

        private Color ColorFor(MotionIntent intent)
        {
            switch (intent)
            {
                case MotionIntent.Walk:
                    return walkColor;
                case MotionIntent.Turn:
                    return turnColor;
                case MotionIntent.Stop:
                    return stopColor;
                case MotionIntent.Wave:
                    return waveColor;
                default:
                    return idleColor;
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (follower == null || follower.ActivePlan == null)
            {
                return;
            }

            TrajectoryPlan plan = follower.ActivePlan;
            IReadOnlyList<TrajectoryPoint> points = plan.Points;

            // Control points, so the 10 Hz discretisation is visible rather than implied.
            for (int i = 0; i < points.Count; i++)
            {
                Gizmos.color = ColorFor(plan.IntentAt(points[i].Time));
                Gizmos.DrawSphere(points[i].Position + Vector3.up * groundOffset, 0.03f);
            }

            IReadOnlyList<MotionSegment> segments = plan.Segments;
            for (int i = 0; i < segments.Count; i++)
            {
                Vector3 boundary = plan.Sample(segments[i].Start).Position;
                Gizmos.color = Color.white;
                Gizmos.DrawLine(boundary, boundary + Vector3.up * 0.6f);
                UnityEditor.Handles.Label(
                    boundary + Vector3.up * 0.65f,
                    $"{MotionIntents.ToName(segments[i].Intent)}  {segments[i].Start:F1}-{segments[i].End:F1}s");
            }

            TrajectoryPoint last = points[points.Count - 1];
            UnityEditor.Handles.Label(
                last.Position + Vector3.up * 0.4f,
                $"{plan.PlanId}  horizon {plan.Horizon:F1}s  score {plan.Score:F2}");
        }
#endif
    }
}
