using System;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace DigitalHuman.Planning
{
    /// <summary>
    /// Serializes a <see cref="PlanRequest"/> into the body of <c>POST /plan</c>, as read
    /// by <c>build_batch</c> in <c>src/gtp/rootmotion/request.py</c>.
    /// </summary>
    /// <remarks>
    /// Written by hand because <c>JsonUtility</c> cannot emit nested arrays, and with the
    /// invariant culture because a comma decimal separator would corrupt the payload on
    /// machines with a non-English locale.
    /// </remarks>
    public static class PlanRequestJson
    {
        public static string Build(PlanRequest request, int candidates)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            StringBuilder json = new StringBuilder(1024);
            json.Append("{\"history\":[");
            if (request.History.Count == 0)
            {
                AppendPose(json, request.CurrentPosition, request.CurrentYaw);
            }
            else
            {
                for (int i = 0; i < request.History.Count; i++)
                {
                    if (i > 0)
                    {
                        json.Append(',');
                    }

                    AppendPose(json, request.History[i].Position, request.History[i].Yaw);
                }
            }

            json.Append("],\"path\":[");
            if (request.PathCorners.Count == 0)
            {
                // No NavMesh path: ask for a straight line to the goal.
                AppendVector(json, request.CurrentPosition);
                json.Append(',');
                AppendVector(json, request.Goal.TargetPosition);
            }
            else
            {
                for (int i = 0; i < request.PathCorners.Count; i++)
                {
                    if (i > 0)
                    {
                        json.Append(',');
                    }

                    AppendVector(json, request.PathCorners[i]);
                }
            }

            json.Append("],\"path_ends_at_goal\":");
            json.Append(request.PathCorners.Count == 0 || request.PathEndsAtGoal ? "true" : "false");
            json.Append(",\"gait\":\"").Append(LocomotionGaits.ToName(request.Gait)).Append('"');
            json.Append(",\"corridor_half_width\":");
            AppendNumber(json, request.CorridorHalfWidth, "corridor_half_width");
            json.Append(",\"candidates\":").Append(Mathf.Max(1, candidates).ToString(CultureInfo.InvariantCulture));
            json.Append('}');
            return json.ToString();
        }

        private static void AppendPose(StringBuilder json, Vector3 position, float yaw)
        {
            json.Append("{\"position\":");
            AppendVector(json, position);
            json.Append(",\"yaw\":");
            AppendNumber(json, yaw, "yaw");
            json.Append('}');
        }

        private static void AppendVector(StringBuilder json, Vector3 value)
        {
            json.Append('[');
            AppendNumber(json, value.x, "x");
            json.Append(',');
            AppendNumber(json, value.y, "y");
            json.Append(',');
            AppendNumber(json, value.z, "z");
            json.Append(']');
        }

        private static void AppendNumber(StringBuilder json, float value, string field)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new ArgumentException($"Plan request field {field} is not finite.");
            }

            json.Append(value.ToString("R", CultureInfo.InvariantCulture));
        }
    }
}
