using UnityEngine;

namespace DigitalHuman
{
    /// <summary>
    /// Stride geometry for a straight-legged walk cycle, free of scene state so the
    /// no-skate property can be tested directly.
    /// </summary>
    /// <remarks>
    /// Phase is measured in cycles: 0 is left heel strike and 0.5 is right heel strike.
    /// Each leg spends the first half of its own cycle in stance and the second in swing.
    /// </remarks>
    public static class StrideGait
    {
        /// <summary>
        /// Ground covered in one full cycle, which is two steps. During one stance the foot
        /// goes from <c>L·sin A</c> ahead of the hip to <c>L·sin A</c> behind it.
        /// </summary>
        public static float StrideLength(float legLength, float hipAmplitudeDegrees)
        {
            return 4f * legLength * Mathf.Sin(hipAmplitudeDegrees * Mathf.Deg2Rad);
        }

        /// <summary>
        /// Fore-aft foot position relative to the hip, from +1 at heel strike to -1 at toe
        /// off. Stance moves it at constant speed, which is what lets a planted foot stay
        /// still while the body passes over it. Swing returns along a cubic whose end slopes
        /// match stance, so the leg does not snap at heel strike or toe off.
        /// </summary>
        public static float FootTravel(float phase)
        {
            float p = Mathf.Repeat(phase, 1f);
            if (p < 0.5f)
            {
                return 1f - 4f * p;
            }

            float s = (p - 0.5f) * 2f;
            return ((-8f * s + 12f) * s - 2f) * s - 1f;
        }

        /// <summary>
        /// Hip pitch in degrees that places a straight leg's foot at
        /// <paramref name="footTravel"/>. Positive swings the leg forward.
        /// </summary>
        public static float HipAngle(float footTravel, float hipAmplitudeDegrees)
        {
            float reach = footTravel * Mathf.Sin(hipAmplitudeDegrees * Mathf.Deg2Rad);
            return Mathf.Asin(Mathf.Clamp(reach, -1f, 1f)) * Mathf.Rad2Deg;
        }

        /// <summary>Zero throughout stance, rising to 1 at mid-swing and back to 0 at heel strike.</summary>
        public static float SwingLift(float phase)
        {
            float p = Mathf.Repeat(phase, 1f);
            return p < 0.5f ? 0f : Mathf.Sin((p - 0.5f) * 2f * Mathf.PI);
        }

        public static bool IsInStance(float phase)
        {
            return Mathf.Repeat(phase, 1f) < 0.5f;
        }
    }
}
