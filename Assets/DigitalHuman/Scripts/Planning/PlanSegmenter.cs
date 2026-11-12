using System.Collections.Generic;
using UnityEngine;

namespace DigitalHuman.Planning
{
    /// <summary>
    /// Labels a sampled trajectory with motion intents using the same thresholds as the
    /// planner service (<c>src/gtp/rootmotion/request.py</c>).
    /// </summary>
    public static class PlanSegmenter
    {
        public const float WalkSpeedThreshold = 0.15f;
        public const float TurnRateThreshold = 0.35f;

        public static List<MotionSegment> Build(IReadOnlyList<TrajectoryPoint> points, float controlHz)
        {
            List<MotionSegment> segments = new List<MotionSegment>();
            if (points == null || points.Count == 0)
            {
                return segments;
            }

            bool moved = false;
            MotionIntent run = Classify(points[0], moved);
            moved = points[0].Speed > WalkSpeedThreshold;
            float runStart = points[0].Time;
            for (int i = 1; i < points.Count; i++)
            {
                MotionIntent intent = Classify(points[i], moved);
                moved = moved || points[i].Speed > WalkSpeedThreshold;
                if (intent == run)
                {
                    continue;
                }

                segments.Add(new MotionSegment(runStart, points[i].Time, run, PromptFor(run)));
                run = intent;
                runStart = points[i].Time;
            }

            float end = points[points.Count - 1].Time + 1f / controlHz;
            segments.Add(new MotionSegment(runStart, end, run, PromptFor(run)));
            return segments;
        }

        private static MotionIntent Classify(TrajectoryPoint point, bool movedBefore)
        {
            if (point.Speed > WalkSpeedThreshold)
            {
                return MotionIntent.Walk;
            }

            if (Mathf.Abs(point.AngularVelocity) > TurnRateThreshold)
            {
                return MotionIntent.Turn;
            }

            return movedBefore ? MotionIntent.Stop : MotionIntent.Idle;
        }

        private static string PromptFor(MotionIntent intent)
        {
            switch (intent)
            {
                case MotionIntent.Walk:
                    return "A person moves along the path.";
                case MotionIntent.Turn:
                    return "A person turns in place.";
                case MotionIntent.Stop:
                    return "A person slows down and comes to a settled stop.";
                default:
                    return "A person stands naturally and remains attentive.";
            }
        }
    }
}
