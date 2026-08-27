using System;

namespace DigitalHuman
{
    /// <summary>
    /// Mirrors <c>MOTION_INTENTS</c> in the generative-trajectory-planning repo
    /// (<c>src/gtp/schema.py</c>). The integer values are the wire format: the planner
    /// emits indices into that tuple, so the order here must not change.
    /// </summary>
    public enum MotionIntent
    {
        Idle = 0,
        Walk = 1,
        Turn = 2,
        Stop = 3,
        Wave = 4
    }

    public static class MotionIntents
    {
        /// <summary>Lowercase names exactly as the planner serializes them.</summary>
        public static readonly string[] Names = { "idle", "walk", "turn", "stop", "wave" };

        public const MotionIntent Fallback = MotionIntent.Idle;

        public static bool IsDefined(int index)
        {
            return index >= 0 && index < Names.Length;
        }

        /// <summary>
        /// Parses a planner intent name. Unknown or malformed input resolves to
        /// <see cref="Fallback"/> rather than throwing, because these strings arrive
        /// from a language model and a remote service.
        /// </summary>
        public static MotionIntent Parse(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return Fallback;
            }

            string trimmed = name.Trim();
            for (int i = 0; i < Names.Length; i++)
            {
                if (string.Equals(Names[i], trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    return (MotionIntent)i;
                }
            }

            return Fallback;
        }

        public static MotionIntent FromIndex(int index)
        {
            return IsDefined(index) ? (MotionIntent)index : Fallback;
        }

        public static string ToName(MotionIntent intent)
        {
            int index = (int)intent;
            return IsDefined(index) ? Names[index] : Names[(int)Fallback];
        }
    }
}
