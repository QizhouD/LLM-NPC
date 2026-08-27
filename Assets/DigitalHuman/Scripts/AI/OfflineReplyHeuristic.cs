using System;

namespace DigitalHuman
{
    /// <summary>
    /// Produces a tagged reply without a language model, so the embodiment layer can be
    /// evaluated with no API key.
    /// </summary>
    /// <remarks>
    /// This is a keyword table, and it is not pretending otherwise: the reply text says
    /// "offline mode" so nobody watching mistakes it for a model output. Its only job is to
    /// emit a plausible <c>[intent:x][emotion:y]</c> pair, because without one the body has
    /// nothing to act on and a keyless run would show only blinking and gaze.
    ///
    /// The intent vocabulary is the same bounded set the model is asked for, so the downstream
    /// path — parser, plan bridge, animator, face — is exercised identically either way.
    /// </remarks>
    public static class OfflineReplyHeuristic
    {
        private static readonly (string[] Keywords, MotionIntent Intent, FaceEmotion Emotion, string Reply)[]
            Rules =
            {
                (new[] { "stop", "wait", "stay", "hold" },
                    MotionIntent.Stop, FaceEmotion.Neutral, "Holding here"),
                (new[] { "come", "follow", "walk", "over here", "approach", "closer" },
                    MotionIntent.Walk, FaceEmotion.Curious, "On my way"),
                (new[] { "turn", "face", "look at me", "around" },
                    MotionIntent.Turn, FaceEmotion.Neutral, "Turning to face you"),
                (new[] { "hello", "hi ", "hey", "greet", "wave" },
                    MotionIntent.Wave, FaceEmotion.Happy, "Hello there"),
                (new[] { "what", "why", "how", "explain", "?" },
                    MotionIntent.Idle, FaceEmotion.Curious, "Good question"),
                (new[] { "careful", "danger", "wrong", "broken", "problem" },
                    MotionIntent.Idle, FaceEmotion.Concerned, "That does sound concerning")
            };

        private const MotionIntent DefaultIntent = MotionIntent.Idle;
        private const FaceEmotion DefaultEmotion = FaceEmotion.Neutral;
        private const string DefaultReply = "I hear you";

        /// <summary>
        /// Builds a reply carrying behaviour tags in the same format the model is asked to
        /// produce, so <see cref="ResponseAnnotationParser"/> handles both identically.
        /// </summary>
        public static string BuildReply(string playerMessage)
        {
            string message = playerMessage != null ? playerMessage.Trim() : string.Empty;
            string lower = message.ToLowerInvariant();

            MotionIntent intent = DefaultIntent;
            FaceEmotion emotion = DefaultEmotion;
            string reply = DefaultReply;

            for (int i = 0; i < Rules.Length; i++)
            {
                if (!ContainsAny(lower, Rules[i].Keywords))
                {
                    continue;
                }

                intent = Rules[i].Intent;
                emotion = Rules[i].Emotion;
                reply = Rules[i].Reply;
                break;
            }

            string quoted = message.Length == 0 ? string.Empty : $" — you said \"{message}\"";
            return $"[offline mode] {reply}{quoted}. " +
                   $"[intent:{MotionIntents.ToName(intent)}]" +
                   $"[emotion:{FaceEmotions.ToName(emotion)}]";
        }

        private static bool ContainsAny(string haystack, string[] needles)
        {
            for (int i = 0; i < needles.Length; i++)
            {
                if (haystack.IndexOf(needles[i], StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
