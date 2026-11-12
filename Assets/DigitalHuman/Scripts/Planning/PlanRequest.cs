using System;
using System.Collections.Generic;
using UnityEngine;

namespace DigitalHuman.Planning
{
    /// <summary>
    /// Mirrors <c>gtp.schema.Obstacle</c>. The planner works in the XZ plane, so the
    /// vertical component of <paramref name="center"/> is dropped on serialization.
    /// </summary>
    public readonly struct PlanObstacle
    {
        public readonly Vector3 Center;
        public readonly float Radius;
        public readonly int Kind;

        public PlanObstacle(Vector3 center, float radius, int kind = 0)
        {
            if (radius <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(radius), "Obstacle radius must be positive.");
            }

            Center = center;
            Radius = radius;
            Kind = kind;
        }
    }

    /// <summary>Mirrors <c>gtp.schema.PlanningGoal</c>.</summary>
    public readonly struct PlanGoal
    {
        public readonly MotionIntent Behavior;
        public readonly Vector3 TargetPosition;
        /// <summary>Desired final heading in radians about the Y axis.</summary>
        public readonly float TargetYaw;
        public readonly float StopDistance;
        public readonly string Instruction;

        public PlanGoal(
            MotionIntent behavior,
            Vector3 targetPosition,
            float targetYaw = 0f,
            float stopDistance = 0f,
            string instruction = null)
        {
            if (stopDistance < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(stopDistance), "Stop distance cannot be negative.");
            }

            Behavior = behavior;
            TargetPosition = targetPosition;
            TargetYaw = targetYaw;
            StopDistance = stopDistance;
            Instruction = instruction ?? string.Empty;
        }
    }

    /// <summary>One recorded root pose. The planner reads the last two seconds of these.</summary>
    public readonly struct RootSample
    {
        public readonly Vector3 Position;
        /// <summary>Root heading in radians about the Y axis.</summary>
        public readonly float Yaw;

        public RootSample(Vector3 position, float yaw)
        {
            Position = position;
            Yaw = yaw;
        }
    }

    /// <summary>
    /// Everything the planner needs for one <c>POST /plan</c> call. Assembled by the
    /// embodiment layer and consumed by an <see cref="ITrajectoryPlanSource"/>.
    /// </summary>
    public sealed class PlanRequest
    {
        public const float DefaultCorridorHalfWidth = 0.5f;

        private static readonly PlanObstacle[] NoObstacles = Array.Empty<PlanObstacle>();
        private static readonly Vector3[] NoPath = Array.Empty<Vector3>();
        private static readonly RootSample[] NoHistory = Array.Empty<RootSample>();

        public Vector3 CurrentPosition { get; }
        /// <summary>Current root heading in radians about the Y axis.</summary>
        public float CurrentYaw { get; }
        public Vector3 CurrentVelocity { get; }
        public float CurrentAngularVelocity { get; }
        public MotionIntent CurrentIntent { get; }
        public PlanGoal Goal { get; }
        public IReadOnlyList<PlanObstacle> Obstacles { get; }
        /// <summary>Free-form instruction forwarded to the text-to-motion adapter.</summary>
        public string Instruction { get; }

        /// <summary>
        /// Where to go, as the engine's pathfinder sees it: world-space corners starting at
        /// the current position. Empty when no path was computed. The root-motion planner
        /// decides how to move along this; it never decides the route.
        /// </summary>
        public IReadOnlyList<Vector3> PathCorners { get; }
        /// <summary>False when <see cref="PathCorners"/> stops short of the goal.</summary>
        public bool PathEndsAtGoal { get; }
        /// <summary>Recent root poses at the control rate, oldest first.</summary>
        public IReadOnlyList<RootSample> History { get; }
        public LocomotionGait Gait { get; }
        /// <summary>How far, in metres, a plan may stray from <see cref="PathCorners"/>.</summary>
        public float CorridorHalfWidth { get; }

        public PlanRequest(
            Vector3 currentPosition,
            float currentYaw,
            Vector3 currentVelocity,
            float currentAngularVelocity,
            MotionIntent currentIntent,
            PlanGoal goal,
            IReadOnlyList<PlanObstacle> obstacles = null,
            string instruction = null,
            IReadOnlyList<Vector3> pathCorners = null,
            bool pathEndsAtGoal = true,
            IReadOnlyList<RootSample> history = null,
            LocomotionGait gait = LocomotionGait.Walk,
            float corridorHalfWidth = DefaultCorridorHalfWidth)
        {
            if (corridorHalfWidth <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(corridorHalfWidth), "Corridor half width must be positive.");
            }

            PathCorners = pathCorners ?? NoPath;
            PathEndsAtGoal = pathEndsAtGoal;
            History = history ?? NoHistory;
            Gait = gait;
            CorridorHalfWidth = corridorHalfWidth;
            CurrentPosition = currentPosition;
            CurrentYaw = currentYaw;
            CurrentVelocity = currentVelocity;
            CurrentAngularVelocity = currentAngularVelocity;
            CurrentIntent = currentIntent;
            Goal = goal;
            Obstacles = obstacles ?? NoObstacles;
            Instruction = instruction ?? goal.Instruction;
        }
    }
}
