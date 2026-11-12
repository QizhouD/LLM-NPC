using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace DigitalHuman.Planning
{
    /// <summary>
    /// Plans with the classical spring follower, no model involved.
    /// </summary>
    /// <remarks>
    /// Two jobs. It is the baseline the learned root-motion planner is compared against,
    /// running the same controller that is measured offline in the planner repo. And it is
    /// the safe fallback for <see cref="HttpTrajectoryPlanSource"/>: when the service is
    /// down or its plan leaves the NavMesh, the character still walks instead of freezing.
    /// </remarks>
    public sealed class SpringPathPlanSource : MonoBehaviour, ITrajectoryPlanSource
    {
        [Header("Contract")]
        [SerializeField, Min(1f)] private float controlHz = 10f;
        [SerializeField, Min(2)] private int horizonSteps = 30;

        [Header("Target speed per gait (m/s)")]
        [Tooltip("Defaults are the median moving speeds of the LAFAN1 training subjects.")]
        [SerializeField, Min(0.1f)] private float walkSpeed = 0.9f;
        [SerializeField, Min(0.1f)] private float runSpeed = 2.1f;
        [SerializeField, Min(0.1f)] private float sprintSpeed = 2.5f;

        [SerializeField] private SpringFollowerSettings spring = new SpringFollowerSettings();

        private readonly List<Vector3> path = new List<Vector3>();
        private int planCounter;

        public string SourceName => "baseline:spring";
        public float ControlHz => controlHz;
        public int HorizonSteps => horizonSteps;

        public float TargetSpeed(LocomotionGait gait)
        {
            switch (gait)
            {
                case LocomotionGait.Run:
                    return runSpeed;
                case LocomotionGait.Sprint:
                    return sprintSpeed;
                default:
                    return walkSpeed;
            }
        }

        public Task<TrajectoryPlan> RequestPlanAsync(
            PlanRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request == null)
            {
                return Task.FromException<TrajectoryPlan>(new ArgumentNullException(nameof(request)));
            }

            try
            {
                return Task.FromResult(Build(request));
            }
            catch (Exception exception)
            {
                return Task.FromException<TrajectoryPlan>(exception);
            }
        }

        private TrajectoryPlan Build(PlanRequest request)
        {
            bool endsAtGoal = true;
            path.Clear();
            path.Add(request.CurrentPosition);
            if (request.PathCorners.Count > 0)
            {
                // Corners start at the agent; keep the character's actual position as vertex 0.
                for (int i = 0; i < request.PathCorners.Count; i++)
                {
                    Vector3 corner = request.PathCorners[i];
                    if (PathGeometry.PlanarDistance(corner, path[path.Count - 1]) > 0.05f)
                    {
                        path.Add(corner);
                    }
                }

                endsAtGoal = request.PathEndsAtGoal;
            }
            else if (request.Goal.Behavior != MotionIntent.Turn)
            {
                // No NavMesh path: head straight for the goal, stopping at the standoff.
                path.Add(request.Goal.TargetPosition);
                PathGeometry.TrimEnd(path, request.Goal.StopDistance);
            }

            List<TrajectoryPoint> points = SpringPathFollower.Simulate(
                path,
                endsAtGoal,
                request.CurrentVelocity,
                request.CurrentYaw,
                TargetSpeed(request.Gait),
                spring,
                horizonSteps,
                controlHz,
                request.Goal.TargetYaw);

            planCounter++;
            return TrajectoryPlan.FromPoints(
                $"spring-{planCounter:D4}",
                controlHz,
                points,
                PlanSegmenter.Build(points, controlHz));
        }

        private void OnValidate()
        {
            controlHz = Mathf.Max(1f, controlHz);
            horizonSteps = Mathf.Max(2, horizonSteps);
        }
    }
}
