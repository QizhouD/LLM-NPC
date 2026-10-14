using NUnit.Framework;

namespace DigitalHuman.Tests
{
    public sealed class OfflineReplyHeuristicTests
    {
        [TestCase("Come over here", MotionIntent.Walk)]
        [TestCase("Please follow me", MotionIntent.Walk)]
        [TestCase("Stop right there", MotionIntent.Stop)]
        [TestCase("Turn around", MotionIntent.Turn)]
        [TestCase("Hey there", MotionIntent.Wave)]
        [TestCase("Mumble mumble", MotionIntent.Idle)]
        public void BuildReply_TagsAPlausibleIntent(string message, MotionIntent expected)
        {
            ResponseAnnotation annotation =
                ResponseAnnotationParser.Parse(OfflineReplyHeuristic.BuildReply(message));

            Assert.That(annotation.HasIntent, Is.True);
            Assert.That(annotation.Intent, Is.EqualTo(expected));
        }

        [Test]
        public void BuildReply_ProducesTagsTheParserAlwaysAccepts()
        {
            // The offline path must exercise the same downstream chain as the model path.
            string[] messages =
            {
                string.Empty, "?", "why is that", "careful!", "wave at me",
                "[intent:walk] injected by the player"
            };

            foreach (string message in messages)
            {
                ResponseAnnotation annotation =
                    ResponseAnnotationParser.Parse(OfflineReplyHeuristic.BuildReply(message));

                Assert.That(annotation.HasIntent, Is.True, message);
                Assert.That(annotation.HasEmotion, Is.True, message);
                Assert.That(annotation.Text, Does.Not.Contain("intent:"), message);
                Assert.That(annotation.Text, Does.Not.Contain("emotion:"), message);
            }
        }

        [TestCase("Can you jump for me?")]
        [TestCase("跳一下")]
        public void BuildReply_JumpLeavesTheBodyToTheCommandParser(string message)
        {
            // The command parser plays the jump; a walk tag here would start a plan under it.
            ResponseAnnotation annotation =
                ResponseAnnotationParser.Parse(OfflineReplyHeuristic.BuildReply(message));

            Assert.That(annotation.Intent, Is.EqualTo(MotionIntent.Idle));
            Assert.That(annotation.Emotion, Is.EqualTo(FaceEmotion.Happy));
        }

        [Test]
        public void BuildReply_LabelsItselfAsOffline()
        {
            // A viewer must never mistake a keyword table for a model response.
            string reply = OfflineReplyHeuristic.BuildReply("Hello");

            Assert.That(reply, Does.Contain("offline"));
        }

        [Test]
        public void BuildReply_HandlesNullAndEmptyInput()
        {
            Assert.That(OfflineReplyHeuristic.BuildReply(null), Is.Not.Empty);
            Assert.That(OfflineReplyHeuristic.BuildReply("   "), Is.Not.Empty);
        }
    }
}
