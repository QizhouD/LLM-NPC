using System.Text;
using System.Text.RegularExpressions;

namespace DigitalHuman
{
    /// <summary>
    /// The result of stripping behaviour tags off a model reply.
    /// </summary>
    public readonly struct ResponseAnnotation
    {
        /// <summary>Reply text with all tags removed. Safe to show and to speak.</summary>
        public readonly string Text;
        public readonly MotionIntent Intent;
        public readonly FaceEmotion Emotion;
        /// <summary>True when a well-formed, in-vocabulary intent tag was present.</summary>
        public readonly bool HasIntent;
        /// <summary>True when a well-formed, in-vocabulary emotion tag was present.</summary>
        public readonly bool HasEmotion;

        public ResponseAnnotation(
            string text,
            MotionIntent intent,
            FaceEmotion emotion,
            bool hasIntent,
            bool hasEmotion)
        {
            Text = text;
            Intent = intent;
            Emotion = emotion;
            HasIntent = hasIntent;
            HasEmotion = hasEmotion;
        }

        /// <summary>
        /// A copy without a <c>walk</c> or <c>turn</c> intent. Text, emotion and any other
        /// intent are kept.
        /// </summary>
        public ResponseAnnotation WithoutLocomotion()
        {
            bool locomotion = HasIntent &&
                              (Intent == MotionIntent.Walk || Intent == MotionIntent.Turn);
            return locomotion
                ? new ResponseAnnotation(Text, MotionIntents.Fallback, Emotion, false, HasEmotion)
                : this;
        }
    }

    /// <summary>
    /// Extracts <c>[intent:walk]</c> and <c>[emotion:curious]</c> tags from a language model
    /// reply and turns them into bounded enum values.
    /// </summary>
    /// <remarks>
    /// This is the seam where language becomes behaviour, and it is also untrusted input: the
    /// tag content is whatever the model decided to emit. Every failure mode therefore
    /// degrades to <c>idle</c> and <c>neutral</c> rather than throwing, and the tag text is
    /// always removed from the reply so it can never leak into the UI or the voice service.
    /// </remarks>
    public static class ResponseAnnotationParser
    {
        /// <summary>
        /// Matches tags whose value could be a vocabulary word. Bounded on purpose: the value
        /// is length-capped and restricted to letters so a malformed or adversarial reply
        /// cannot produce pathological matching.
        /// </summary>
        private static readonly Regex TagPattern = new Regex(
            @"\[\s*(?<key>intent|emotion)\s*[:=]\s*(?<value>[A-Za-z_\- ]{0,24})\s*\]",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>
        /// Matches any tag-shaped run, whatever its contents, and is used only for removal.
        /// A model that echoes the prompt's own placeholder verbatim, for example
        /// <c>[emotion:&lt;one of neutral | curious&gt;]</c>, produces a value the strict
        /// pattern rejects; without this the raw placeholder would leak into the dialogue box.
        /// Deliberately does not cross a newline or swallow an unrelated closing bracket.
        /// </summary>
        private static readonly Regex StripPattern = new Regex(
            @"\[\s*(?:intent|emotion)\s*[:=][^\]\r\n]{0,48}\]",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static ResponseAnnotation Parse(string response)
        {
            if (string.IsNullOrEmpty(response))
            {
                return new ResponseAnnotation(
                    string.Empty, MotionIntents.Fallback, FaceEmotions.Fallback, false, false);
            }

            MotionIntent intent = MotionIntents.Fallback;
            FaceEmotion emotion = FaceEmotions.Fallback;
            bool hasIntent = false;
            bool hasEmotion = false;

            foreach (Match match in TagPattern.Matches(response))
            {
                string key = match.Groups["key"].Value.ToLowerInvariant();
                string value = match.Groups["value"].Value;

                // A later tag wins, so a model that corrects itself mid-reply is honoured.
                if (key == "intent")
                {
                    if (IsInVocabulary(MotionIntents.Names, value))
                    {
                        intent = MotionIntents.Parse(value);
                        hasIntent = true;
                    }
                }
                else if (IsInVocabulary(FaceEmotions.Names, value))
                {
                    emotion = FaceEmotions.Parse(value);
                    hasEmotion = true;
                }
            }

            return new ResponseAnnotation(
                CleanText(response), intent, emotion, hasIntent, hasEmotion);
        }

        /// <summary>Removes tags without interpreting them.</summary>
        public static string StripTags(string response)
        {
            return string.IsNullOrEmpty(response) ? string.Empty : CleanText(response);
        }

        private static bool IsInVocabulary(string[] vocabulary, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string trimmed = value.Trim();
            for (int i = 0; i < vocabulary.Length; i++)
            {
                if (string.Equals(
                        vocabulary[i], trimmed, System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Strips every tag, including malformed and out-of-vocabulary ones, then tidies the
        /// whitespace the removal leaves behind.
        /// </summary>
        private static string CleanText(string response)
        {
            string stripped = StripPattern.Replace(response, string.Empty);

            StringBuilder builder = new StringBuilder(stripped.Length);
            bool previousWasSpace = false;
            for (int i = 0; i < stripped.Length; i++)
            {
                char character = stripped[i];
                if (character == ' ' || character == '\t')
                {
                    previousWasSpace = true;
                    continue;
                }

                if (previousWasSpace && builder.Length > 0 && character != '\n' && character != '\r')
                {
                    builder.Append(' ');
                }

                previousWasSpace = false;
                builder.Append(character);
            }

            return builder.ToString().Trim();
        }
    }
}
