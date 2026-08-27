using System;

namespace DigitalHuman.Planning
{
    /// <summary>
    /// Literal mirror of the JSON produced by
    /// <c>gtp.integrations.plan_export.build_trajectory_plan</c>. Field names are the wire
    /// format and must match the Python <c>TrajectoryPlan.to_dict()</c> output exactly, so
    /// they intentionally use snake_case instead of C# conventions.
    /// </summary>
    [Serializable]
    public sealed class TrajectoryPlanDto
    {
        public string plan_id;
        public float horizon;
        public TrajectoryPointDto[] trajectory;
        public MotionSegmentDto[] segments;
        public float score;
        public bool fallback;
    }

    [Serializable]
    public sealed class TrajectoryPointDto
    {
        public float t;
        public float[] position;
        public float yaw;
        public float[] velocity;
        public float angular_velocity;
        public float speed;
    }

    [Serializable]
    public sealed class MotionSegmentDto
    {
        public float start;
        public float end;
        public string action;
        public string prompt;
    }
}
