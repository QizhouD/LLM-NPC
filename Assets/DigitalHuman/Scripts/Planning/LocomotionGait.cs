using System;

namespace DigitalHuman.Planning
{
    /// <summary>
    /// Mirrors <c>GAITS</c> in the planner repo (<c>src/gtp/rootmotion/__init__.py</c>).
    /// The lowercase names are the wire format of <c>POST /plan</c>.
    /// </summary>
    public enum LocomotionGait
    {
        Walk = 0,
        Run = 1,
        Sprint = 2
    }

    public static class LocomotionGaits
    {
        public static readonly string[] Names = { "walk", "run", "sprint" };

        public static string ToName(LocomotionGait gait)
        {
            int index = (int)gait;
            return index >= 0 && index < Names.Length ? Names[index] : Names[0];
        }

        public static LocomotionGait Parse(string name)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                string trimmed = name.Trim();
                for (int i = 0; i < Names.Length; i++)
                {
                    if (string.Equals(Names[i], trimmed, StringComparison.OrdinalIgnoreCase))
                    {
                        return (LocomotionGait)i;
                    }
                }
            }

            return LocomotionGait.Walk;
        }
    }
}
