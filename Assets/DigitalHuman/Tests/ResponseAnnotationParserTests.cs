using NUnit.Framework;

namespace DigitalHuman.Tests
{
    public sealed class ResponseAnnotationParserTests
    {
        [Test]
        public void Parse_ReadsBothTagsAndRemovesThem()
        {
            ResponseAnnotation result = ResponseAnnotationParser.Parse(
                "Of course, follow me. [intent:walk][emotion:curious]");

            Assert.That(result.Text, Is.EqualTo("Of course, follow me."));
            Assert.That(result.Intent, Is.EqualTo(MotionIntent.Walk));
            Assert.That(result.Emotion, Is.EqualTo(FaceEmotion.Curious));
            Assert.That(result.HasIntent, Is.True);
            Assert.That(result.HasEmotion, Is.True);
        }

        [Test]
        public void Parse_TreatsReplyWithoutTagsAsUnannotated()
        {
            ResponseAnnotation result = ResponseAnnotationParser.Parse("Just talking here.");

            Assert.That(result.Text, Is.EqualTo("Just talking here."));
            Assert.That(result.HasIntent, Is.False);
            Assert.That(result.HasEmotion, Is.False);
            Assert.That(result.Intent, Is.EqualTo(MotionIntent.Idle));
            Assert.That(result.Emotion, Is.EqualTo(FaceEmotion.Neutral));
        }

        [TestCase("[intent:moonwalk]")]
        [TestCase("[intent:]")]
        [TestCase("[intent: ]")]
        [TestCase("[intent:walk walk walk]")]
        [TestCase("[intent:DROP TABLE users]")]
        public void Parse_DegradesIllegalIntentToIdleAndStillStripsIt(string tag)
        {
            ResponseAnnotation result = ResponseAnnotationParser.Parse("Hello. " + tag);

            Assert.That(result.Intent, Is.EqualTo(MotionIntent.Idle));
            Assert.That(result.HasIntent, Is.False,
                "an out-of-vocabulary tag must not count as an intent");
            Assert.That(result.Text, Is.EqualTo("Hello."),
                "the tag must be stripped even when its value is rejected");
        }

        [TestCase("[emotion:ecstatic]")]
        [TestCase("[emotion:]")]
        [TestCase("[emotion:<one of neutral | curious | happy>]")]
        [TestCase("[emotion:\"happy\"]")]
        public void Parse_DegradesIllegalEmotionToNeutralAndStillStripsIt(string tag)
        {
            ResponseAnnotation result = ResponseAnnotationParser.Parse("Hello. " + tag);

            Assert.That(result.Emotion, Is.EqualTo(FaceEmotion.Neutral));
            Assert.That(result.HasEmotion, Is.False);
            Assert.That(result.Text, Does.Not.Contain("emotion"));
        }

        [Test]
        public void Parse_AcceptsCaseAndSpacingVariation()
        {
            ResponseAnnotation result = ResponseAnnotationParser.Parse(
                "Hi. [ Intent : WAVE ][EMOTION: Happy ]");

            Assert.That(result.Intent, Is.EqualTo(MotionIntent.Wave));
            Assert.That(result.Emotion, Is.EqualTo(FaceEmotion.Happy));
            Assert.That(result.Text, Is.EqualTo("Hi."));
        }

        [Test]
        public void Parse_AcceptsEqualsAsSeparator()
        {
            ResponseAnnotation result = ResponseAnnotationParser.Parse("Hi. [intent=stop]");

            Assert.That(result.Intent, Is.EqualTo(MotionIntent.Stop));
            Assert.That(result.HasIntent, Is.True);
        }

        [Test]
        public void Parse_LastTagWinsWhenTheModelRepeatsItself()
        {
            ResponseAnnotation result = ResponseAnnotationParser.Parse(
                "Wait. [intent:walk] Actually, no. [intent:stop]");

            Assert.That(result.Intent, Is.EqualTo(MotionIntent.Stop));
            Assert.That(result.Text, Is.EqualTo("Wait. Actually, no."));
        }

        [Test]
        public void Parse_IgnoresIllegalValueButKeepsAnEarlierLegalOne()
        {
            ResponseAnnotation result = ResponseAnnotationParser.Parse(
                "Hi. [intent:wave][intent:pirouette]");

            Assert.That(result.Intent, Is.EqualTo(MotionIntent.Wave));
            Assert.That(result.HasIntent, Is.True);
            Assert.That(result.Text, Is.EqualTo("Hi."));
        }

        [Test]
        public void Parse_StripsTagsFromTheMiddleOfProse()
        {
            ResponseAnnotation result = ResponseAnnotationParser.Parse(
                "Let me [intent:turn] check that for you.");

            Assert.That(result.Text, Is.EqualTo("Let me check that for you."));
            Assert.That(result.Intent, Is.EqualTo(MotionIntent.Turn));
        }

        [Test]
        public void Parse_LeavesUnrelatedBracketsAlone()
        {
            ResponseAnnotation result = ResponseAnnotationParser.Parse(
                "The sign reads [RESTRICTED AREA]. [emotion:concerned]");

            Assert.That(result.Text, Is.EqualTo("The sign reads [RESTRICTED AREA]."));
            Assert.That(result.Emotion, Is.EqualTo(FaceEmotion.Concerned));
        }

        [TestCase(null)]
        [TestCase("")]
        public void Parse_HandlesEmptyInput(string input)
        {
            ResponseAnnotation result = ResponseAnnotationParser.Parse(input);

            Assert.That(result.Text, Is.Empty);
            Assert.That(result.Intent, Is.EqualTo(MotionIntent.Idle));
            Assert.That(result.Emotion, Is.EqualTo(FaceEmotion.Neutral));
        }

        [Test]
        public void Parse_ReturnsEmptyTextWhenTheReplyIsOnlyTags()
        {
            ResponseAnnotation result =
                ResponseAnnotationParser.Parse("[intent:idle][emotion:neutral]");

            Assert.That(result.Text, Is.Empty);
            Assert.That(result.HasIntent, Is.True);
            Assert.That(result.HasEmotion, Is.True);
        }

        [Test]
        public void Parse_PreservesNewlines()
        {
            ResponseAnnotation result = ResponseAnnotationParser.Parse(
                "First line.\nSecond line. [emotion:happy]");

            Assert.That(result.Text, Is.EqualTo("First line.\nSecond line."));
        }

        [Test]
        public void StripTags_RemovesTagsWithoutInterpretingThem()
        {
            Assert.That(
                ResponseAnnotationParser.StripTags("Hello. [intent:nonsense][emotion:nonsense]"),
                Is.EqualTo("Hello."));
        }
    }
}
