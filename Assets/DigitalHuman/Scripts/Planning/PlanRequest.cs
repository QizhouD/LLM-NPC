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

    /// <summary>
    /// Everything the planner needs for one <c>POST /plan</c> call. Assembled by the
    /// embodiment layer and consumed by an <see cref="ITrajectoryPlanSource"/>.
    /// </summary>
    public sealed class PlanRequest
    {
        private static readonly PlanObstacle[] NoObstacles = Array.Empty<PlanObstacle>();

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

        public PlanRequest(
            Vector3 currentPosition,
            float currentYaw,
            Vector3 currentVelocity,
            float currentAngularVelocity,
            MotionIntent currentIntent,
            PlanGoal goal,
            IReadOnlyList<PlanObstacle> obstacles = null,
            string instruction = null)
        {
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
