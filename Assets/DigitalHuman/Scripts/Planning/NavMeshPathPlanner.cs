using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace DigitalHuman.Planning
{
    /// <summary>
    /// The engine's half of the division of labour: the NavMesh decides the route, and
    /// later confirms that a plan produced along that route stays on walkable ground.
    /// </summary>
    public static class NavMeshPathPlanner
    {
        /// <summary>
        /// Computes path corners from <paramref name="from"/> to the goal, shortened by the
        /// goal's stop distance. Returns false when either end is off the NavMesh or no
        /// path exists.
        /// </summary>
        /// <param name="endsAtGoal">False for a partial path that stops short of the goal.</param>
        public static bool TryBuildPath(
            Vector3 from,
            PlanGoal goal,
            List<Vector3> corners,
            out bool endsAtGoal,
            float sampleDistance = 1.5f,
            int areaMask = NavMesh.AllAreas)
        {
            corners.Clear();
            endsAtGoal = false;
            if (!NavMesh.SamplePosition(from, out NavMeshHit start, sampleDistance, areaMask) ||
                !NavMesh.SamplePosition(goal.TargetPosition, out NavMeshHit end, sampleDistance, areaMask))
            {
                return false;
            }

            NavMeshPath path = new NavMeshPath();
            if (!NavMesh.CalculatePath(start.position, end.position, areaMask, path) ||
                path.status == NavMeshPathStatus.PathInvalid ||
                path.corners == null ||
                path.corners.Length == 0)
            {
                return false;
            }

            corners.AddRange(path.corners);
            endsAtGoal = path.status == NavMeshPathStatus.PathComplete;
            if (endsAtGoal)
            {
                PathGeometry.TrimEnd(corners, goal.StopDistance);
            }

            return true;
        }

        /// <summary>
        /// Exact swept check: true when every segment of the plan stays on the NavMesh.
        /// The planner bounds deviation from the path it was given; only the engine knows
        /// whether that corridor is actually walkable, so the final word is here.
        /// </summary>
        /// <param name="blockedIndex">Index of the first point that cannot be reached.</param>
        public static bool IsWalkable(
            TrajectoryPlan plan,
            out int blockedIndex,
            float sampleDistance = 1.5f,
            int areaMask = NavMesh.AllAreas)
        {
            blockedIndex = -1;
            if (plan == null || plan.PointCount == 0)
            {
                return false;
            }

            if (!NavMesh.SamplePosition(plan.Points[0].Position, out NavMeshHit previous, sampleDistance, areaMask))
            {
                blockedIndex = 0;
                return false;
            }

            for (int i = 1; i < plan.PointCount; i++)
            {
                if (!NavMesh.SamplePosition(plan.Points[i].Position, out NavMeshHit current, sampleDistance, areaMask) ||
                    PathGeometry.PlanarDistance(current.position, plan.Points[i].Position) > 0.05f ||
                    NavMesh.Raycast(previous.position, current.position, out NavMeshHit _, areaMask))
                {
                    blockedIndex = i;
                    return false;
                }

                previous = current;
            }

            return true;
        }
    }
}
