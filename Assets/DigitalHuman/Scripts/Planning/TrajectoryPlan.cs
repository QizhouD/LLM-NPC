using System;
using System.Collections.Generic;
using UnityEngine;

namespace DigitalHuman.Planning
{
    /// <summary>One planner sample: world-space root pose and kinematics at time <see cref="Time"/>.</summary>
    public readonly struct TrajectoryPoint
    {
        public readonly float Time;
        public readonly Vector3 Position;
        /// <summary>Root heading in radians about the Y axis.</summary>
        public readonly float Yaw;
        public readonly Vector3 Velocity;
        /// <summary>Radians per second about the Y axis.</summary>
        public readonly float AngularVelocity;
        /// <summary>Metres per second along the path.</summary>
        public readonly float Speed;

        public TrajectoryPoint(
            float time,
            Vector3 position,
            float yaw,
            Vector3 velocity,
            float angularVelocity,
            float speed)
        {
            Time = time;
            Position = position;
            Yaw = yaw;
            Velocity = velocity;
            AngularVelocity = angularVelocity;
            Speed = speed;
        }

        public Quaternion Rotation => Quaternion.Euler(0f, Yaw * Mathf.Rad2Deg, 0f);
    }

    /// <summary>A contiguous run of one motion intent, with the text-to-motion prompt.</summary>
    public readonly struct MotionSegment
    {
        public readonly float Start;
        public readonly float End;
        public readonly MotionIntent Intent;
        public readonly string Prompt;

        public MotionSegment(float start, float end, MotionIntent intent, string prompt)
        {
            Start = start;
            End = end;
            Intent = intent;
            Prompt = prompt;
        }

        public bool Contains(float time)
        {
            return time >= Start && time < End;
        }
    }

    /// <summary>
    /// Runtime form of a planner result. Construct through <see cref="FromJson"/> or
    /// <see cref="FromDto"/> so validation happens in one place.
    /// </summary>
    public sealed class TrajectoryPlan
    {
        private readonly TrajectoryPoint[] points;
        private readonly MotionSegment[] segments;

        public string PlanId { get; }
        public float Horizon { get; }
        public float Score { get; }

        /// <summary>
        /// True when the planner rejected every sampled candidate and returned a stationary
        /// plan. Consumers must hold idle rather than execute this.
        /// </summary>
        public bool IsFallback { get; }

        public IReadOnlyList<TrajectoryPoint> Points => points;
        public IReadOnlyList<MotionSegment> Segments => segments;
        public int PointCount => points.Length;

        private TrajectoryPlan(
            string planId,
            float horizon,
            float score,
            bool isFallback,
            TrajectoryPoint[] trajectoryPoints,
            MotionSegment[] motionSegments)
        {
            PlanId = planId;
            Horizon = horizon;
            Score = score;
            IsFallback = isFallback;
            points = trajectoryPoints;
            segments = motionSegments;
        }

        public static TrajectoryPlan FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ArgumentException("Plan JSON is empty.", nameof(json));
            }

            TrajectoryPlanDto dto;
            try
            {
                dto = JsonUtility.FromJson<TrajectoryPlanDto>(json);
            }
            catch (Exception exception)
            {
                throw new FormatException($"Plan JSON could not be parsed: {exception.Message}", exception);
            }

            if (dto == null)
            {
                throw new FormatException("Plan JSON did not deserialize into an object.");
            }

            return FromDto(dto);
        }

        public static TrajectoryPlan FromDto(TrajectoryPlanDto dto)
        {
            if (dto == null)
            {
                throw new ArgumentNullException(nameof(dto));
            }

            if (dto.trajectory == null || dto.trajectory.Length == 0)
            {
                throw new FormatException("Plan contains no trajectory points.");
            }

            TrajectoryPoint[] parsedPoints = new TrajectoryPoint[dto.trajectory.Length];
            for (int i = 0; i < dto.trajectory.Length; i++)
            {
                TrajectoryPointDto point = dto.trajectory[i];
                if (point == null)
                {
                    throw new FormatException($"Trajectory point {i} is null.");
                }

                parsedPoints[i] = new TrajectoryPoint(
                    point.t,
                    ToVector3(point.position, $"trajectory[{i}].position", 3),
                    point.yaw,
                    ToVector3(point.velocity, $"trajectory[{i}].velocity", 3),
                    point.angular_velocity,
                    point.speed);
            }

            MotionSegmentDto[] segmentDtos = dto.segments ?? Array.Empty<MotionSegmentDto>();
            MotionSegment[] parsedSegments = new MotionSegment[segmentDtos.Length];
            for (int i = 0; i < segmentDtos.Length; i++)
            {
                MotionSegmentDto segment = segmentDtos[i];
                if (segment == null)
                {
                    throw new FormatException($"Motion segment {i} is null.");
                }

                // Unknown intent names degrade to idle instead of throwing: the vocabulary
                // may grow on the planner side before this client is updated.
                parsedSegments[i] = new MotionSegment(
                    segment.start,
                    segment.end,
                    MotionIntents.Parse(segment.action),
                    segment.prompt ?? string.Empty);
            }

            float horizon = dto.horizon > 0f
                ? dto.horizon
                : parsedPoints[parsedPoints.Length - 1].Time;

            return new TrajectoryPlan(
                string.IsNullOrWhiteSpace(dto.plan_id) ? "plan-unnamed" : dto.plan_id,
                horizon,
                dto.score,
                dto.fallback,
                parsedPoints,
                parsedSegments);
        }

        public static TrajectoryPlan FromPoints(
            string planId,
            float controlHz,
            IReadOnlyList<TrajectoryPoint> trajectoryPoints,
            IReadOnlyList<MotionSegment> motionSegments,
            float score = 0f,
            bool isFallback = false)
        {
            if (trajectoryPoints == null || trajectoryPoints.Count == 0)
            {
                throw new ArgumentException("At least one point is required.", nameof(trajectoryPoints));
            }

            if (controlHz <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(controlHz));
            }

            TrajectoryPoint[] copiedPoints = new TrajectoryPoint[trajectoryPoints.Count];
            for (int i = 0; i < trajectoryPoints.Count; i++)
            {
                copiedPoints[i] = trajectoryPoints[i];
            }

            MotionSegment[] copiedSegments = motionSegments == null
                ? Array.Empty<MotionSegment>()
                : new MotionSegment[motionSegments.Count];
            for (int i = 0; i < copiedSegments.Length; i++)
            {
                copiedSegments[i] = motionSegments[i];
            }

            return new TrajectoryPlan(
                planId,
                copiedPoints.Length / controlHz,
                score,
                isFallback,
                copiedPoints,
                copiedSegments);
        }

        /// <summary>
        /// Samples the plan at <paramref name="time"/> seconds from plan start, linearly
        /// interpolating position and shortest-arc interpolating yaw.
        /// </summary>
        public TrajectoryPoint Sample(float time)
        {
            if (points.Length == 1 || time <= points[0].Time)
            {
                return points[0];
            }

            TrajectoryPoint last = points[points.Length - 1];
            if (time >= last.Time)
            {
                return last;
            }

            for (int i = 1; i < points.Length; i++)
            {
                if (points[i].Time < time)
                {
                    continue;
                }

                TrajectoryPoint previous = points[i - 1];
                TrajectoryPoint next = points[i];
                float span = next.Time - previous.Time;
                float alpha = span <= Mathf.Epsilon ? 0f : (time - previous.Time) / span;
                return new TrajectoryPoint(
                    time,
                    Vector3.Lerp(previous.Position, next.Position, alpha),
                    LerpAngleRadians(previous.Yaw, next.Yaw, alpha),
                    Vector3.Lerp(previous.Velocity, next.Velocity, alpha),
                    Mathf.Lerp(previous.AngularVelocity, next.AngularVelocity, alpha),
                    Mathf.Lerp(previous.Speed, next.Speed, alpha));
            }

            return last;
        }

        public MotionIntent IntentAt(float time)
        {
            for (int i = 0; i < segments.Length; i++)
            {
                if (segments[i].Contains(time))
                {
                    return segments[i].Intent;
                }
            }

            return segments.Length > 0
                ? segments[segments.Length - 1].Intent
                : MotionIntents.Fallback;
        }

        private static float LerpAngleRadians(float from, float to, float alpha)
        {
            float difference = Mathf.Repeat(to - from, Mathf.PI * 2f);
            if (difference > Mathf.PI)
            {
                difference -= Mathf.PI * 2f;
            }

            return from + difference * alpha;
        }

        private static Vector3 ToVector3(float[] values, string field, int expectedLength)
        {
            if (values == null || values.Length != expectedLength)
            {
                throw new FormatException(
                    $"{field} must contain {expectedLength} components.");
            }

            for (int i = 0; i < values.Length; i++)
            {
                if (float.IsNaN(values[i]) || float.IsInfinity(values[i]))
                {
                    throw new FormatException($"{field}[{i}] is not finite.");
                }
            }

            return new Vector3(values[0], values[1], values[2]);
        }
    }
}
