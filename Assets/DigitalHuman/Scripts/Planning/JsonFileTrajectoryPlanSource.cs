using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace DigitalHuman.Planning
{
    /// <summary>
    /// Reads a plan exported by the planner repo (<c>results/*.json</c>). This is the
    /// offline stand-in for the not-yet-existing <c>POST /plan</c> service.
    /// </summary>
    /// <remarks>
    /// The exported plans currently in that repo are all <c>fallback: true</c> stationary
    /// plans, so this source is useful for verifying the parse path and the fallback branch,
    /// not for driving a demo. Use <see cref="MockTrajectoryPlanSource"/> for that.
    /// </remarks>
    public sealed class JsonFileTrajectoryPlanSource : MonoBehaviour, ITrajectoryPlanSource
    {
        [Tooltip("Absolute path, or a path relative to the project folder (the parent of Assets).")]
        [SerializeField] private string planFilePath = "PlanSamples/greet_plan.json";
        [Tooltip("Re-read the file on every request so edits are picked up without a restart.")]
        [SerializeField] private bool reloadEveryRequest = true;

        private TrajectoryPlan cached;
        private string cachedFrom;

        public string SourceName => $"json:{Path.GetFileName(ResolvePath())}";

        public string PlanFilePath
        {
            get => planFilePath;
            set
            {
                planFilePath = value;
                cached = null;
            }
        }

        public Task<TrajectoryPlan> RequestPlanAsync(
            PlanRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string resolved = ResolvePath();
            if (!reloadEveryRequest && cached != null && cachedFrom == resolved)
            {
                return Task.FromResult(cached);
            }

            if (!File.Exists(resolved))
            {
                return Task.FromException<TrajectoryPlan>(
                    new FileNotFoundException($"No plan file at {resolved}", resolved));
            }

            try
            {
                TrajectoryPlan plan = TrajectoryPlan.FromJson(File.ReadAllText(resolved));
                cached = plan;
                cachedFrom = resolved;
                return Task.FromResult(plan);
            }
            catch (Exception exception)
            {
                return Task.FromException<TrajectoryPlan>(exception);
            }
        }

        private string ResolvePath()
        {
            if (string.IsNullOrWhiteSpace(planFilePath))
            {
                return string.Empty;
            }

            if (Path.IsPathRooted(planFilePath))
            {
                return planFilePath;
            }

            string projectFolder = Directory.GetParent(Application.dataPath)?.FullName
                                   ?? Application.dataPath;
            return Path.GetFullPath(Path.Combine(projectFolder, planFilePath));
        }
    }
}
