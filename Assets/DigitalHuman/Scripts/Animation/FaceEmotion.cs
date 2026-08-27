using System;

namespace DigitalHuman
{
    /// <summary>
    /// The bounded emotion vocabulary the language model is allowed to emit. Kept small on
    /// purpose: every value must map to a pose the Vexa rig can actually reach with eyebrow
    /// and eyelid bones alone, since the model has no blend shapes.
    /// </summary>
    public enum FaceEmotion
    {
        Neutral = 0,
        Curious = 1,
        Happy = 2,
        Concerned = 3,
        Surprised = 4
    }

    public static class FaceEmotions
    {
        public static readonly string[] Names =
            { "neutral", "curious", "happy", "concerned", "surprised" };

        public const FaceEmotion Fallback = FaceEmotion.Neutral;

        public static bool IsDefined(int index)
        {
            return index >= 0 && index < Names.Length;
        }

        /// <summary>
        /// Parses a model-authored emotion name. Anything unrecognised becomes
        /// <see cref="Fallback"/>, because this string comes straight out of an LLM.
        /// </summary>
        public static FaceEmotion Parse(string name)
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
                    return (FaceEmotion)i;
                }
            }

            return Fallback;
        }

        public static string ToName(FaceEmotion emotion)
        {
            int index = (int)emotion;
            return IsDefined(index) ? Names[index] : Names[(int)Fallback];
        }
    }
}
