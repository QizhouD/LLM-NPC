using System;
using System.Collections.Generic;
using UnityEngine;

namespace DigitalHuman.Planning
{
    /// <summary>
    /// Fixed-size ring of timestamped root poses. Plain class so it can be tested directly.
    /// </summary>
    public sealed class RootHistoryBuffer
    {
        private readonly RootSample[] samples;
        private readonly float[] times;
        private int next;

        public int Capacity => samples.Length;
        public int Count { get; private set; }

        public RootHistoryBuffer(int capacity)
        {
            if (capacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            samples = new RootSample[capacity];
            times = new float[capacity];
        }

        public void Add(RootSample sample, float time)
        {
            samples[next] = sample;
            times[next] = time;
            next = (next + 1) % samples.Length;
            Count = Mathf.Min(Count + 1, samples.Length);
        }

        public void Clear()
        {
            next = 0;
            Count = 0;
        }

        /// <summary>
        /// Writes <paramref name="steps"/> poses spaced exactly <paramref name="interval"/>
        /// seconds apart and ending at <paramref name="current"/>, oldest first.
        /// </summary>
        /// <remarks>
        /// The planner differentiates consecutive history samples to get the current
        /// velocity, so the spacing has to be exact: appending "now" to samples taken on
        /// a fixed clock would make the last gap shorter and the velocity too small.
        /// Times earlier than the oldest stored sample repeat that sample, which reads as
        /// "was standing there".
        /// </remarks>
        public void Resample(
            float now,
            RootSample current,
            float interval,
            int steps,
            List<RootSample> destination)
        {
            if (interval <= 0f || steps < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(steps));
            }

            destination.Clear();
            int oldest = (next - Count + samples.Length) % samples.Length;
            int cursor = 0;
            for (int step = steps - 1; step >= 0; step--)
            {
                float time = now - step * interval;
                if (Count == 0)
                {
                    destination.Add(current);
                    continue;
                }

                // Advance to the last stored sample at or before the requested time.
                while (cursor + 1 < Count && times[(oldest + cursor + 1) % samples.Length] <= time)
                {
                    cursor++;
                }

                int index = (oldest + cursor) % samples.Length;
                if (time <= times[index])
                {
                    destination.Add(samples[index]);
                    continue;
                }

                bool hasNext = cursor + 1 < Count;
                RootSample after = hasNext ? samples[(oldest + cursor + 1) % samples.Length] : current;
                float afterTime = hasNext ? times[(oldest + cursor + 1) % samples.Length] : now;
                float span = afterTime - times[index];
                float alpha = span <= 1e-6f ? 1f : Mathf.Clamp01((time - times[index]) / span);
                destination.Add(new RootSample(
                    Vector3.Lerp(samples[index].Position, after.Position, alpha),
                    samples[index].Yaw + SpringPathFollower.ShortestAngle(samples[index].Yaw, after.Yaw) * alpha));
            }
        }
    }

    /// <summary>
    /// Records the root pose at the planner's control rate. The planner conditions on the
    /// last two seconds of motion, which is how a new plan continues the speed and turn
    /// already under way instead of restarting from rest at every replan.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RootHistoryRecorder : MonoBehaviour
    {
        [Tooltip("Defaults to this component's transform.")]
        [SerializeField] private Transform root;
        [Tooltip("Samples per second. The planner contract specifies 10.")]
        [SerializeField, Min(1f)] private float controlHz = 10f;
        [Tooltip("Samples returned. The planner contract specifies 20, i.e. two seconds.")]
        [SerializeField, Min(1)] private int historySteps = 20;

        private RootHistoryBuffer buffer;
        private float accumulator;

        public float ControlHz => controlHz;
        public int HistorySteps => historySteps;

        private void Awake()
        {
            EnsureBuffer();
        }

        private void OnEnable()
        {
            EnsureBuffer();
            buffer.Clear();
            accumulator = 0f;
            buffer.Add(CurrentSample(), Time.time);
        }

        private void LateUpdate()
        {
            // LateUpdate, so the sample reflects whatever moved the root this frame.
            float interval = 1f / controlHz;
            accumulator += Time.deltaTime;
            if (accumulator >= interval)
            {
                accumulator %= interval;
                buffer.Add(CurrentSample(), Time.time);
            }
        }

        /// <summary>
        /// Fills <paramref name="destination"/> with evenly spaced history ending at the
        /// current pose, oldest first.
        /// </summary>
        public void Snapshot(List<RootSample> destination)
        {
            EnsureBuffer();
            buffer.Resample(Time.time, CurrentSample(), 1f / controlHz, historySteps, destination);
        }

        private RootSample CurrentSample()
        {
            Transform source = root != null ? root : transform;
            return new RootSample(source.position, source.rotation.eulerAngles.y * Mathf.Deg2Rad);
        }

        private void EnsureBuffer()
        {
            // Two spare slots, so interpolation still has a sample older than the window.
            int capacity = Mathf.Max(1, historySteps) + 2;
            if (buffer == null || buffer.Capacity != capacity)
            {
                buffer = new RootHistoryBuffer(capacity);
            }
        }
    }
}
