using NUnit.Framework;

namespace DigitalHuman.Tests
{
    public sealed class MotionIntentTests
    {
        /// <summary>
        /// The integer values are the wire format shared with <c>MOTION_INTENTS</c> in
        /// <c>src/gtp/schema.py</c>. If this test fails, plans will be misinterpreted
        /// silently rather than loudly, so it is pinned deliberately.
        /// </summary>
        [Test]
        public void Indices_MatchPlannerVocabularyOrder()
        {
            Assert.That((int)MotionIntent.Idle, Is.EqualTo(0));
            Assert.That((int)MotionIntent.Walk, Is.EqualTo(1));
            Assert.That((int)MotionIntent.Turn, Is.EqualTo(2));
            Assert.That((int)MotionIntent.Stop, Is.EqualTo(3));
            Assert.That((int)MotionIntent.Wave, Is.EqualTo(4));
            Assert.That(MotionIntents.Names.Length, Is.EqualTo(5));
        }

        [Test]
        public void Names_MatchPlannerSpelling()
        {
            Assert.That(MotionIntents.Names,
                Is.EqualTo(new[] { "idle", "walk", "turn", "stop", "wave" }));
        }

        [TestCase("idle", MotionIntent.Idle)]
        [TestCase("walk", MotionIntent.Walk)]
        [TestCase("turn", MotionIntent.Turn)]
        [TestCase("stop", MotionIntent.Stop)]
        [TestCase("wave", MotionIntent.Wave)]
        [TestCase("WALK", MotionIntent.Walk)]
        [TestCase("  wave  ", MotionIntent.Wave)]
        public void Parse_AcceptsVocabularyAndTolerantFormatting(string input, MotionIntent expected)
        {
            Assert.That(MotionIntents.Parse(input), Is.EqualTo(expected));
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase(null)]
        [TestCase("sprint")]
        [TestCase("walk;DROP TABLE")]
        [TestCase("0")]
        public void Parse_DegradesUnknownInputToIdle(string input)
        {
            Assert.That(MotionIntents.Parse(input), Is.EqualTo(MotionIntent.Idle));
        }

        [TestCase(-1, MotionIntent.Idle)]
        [TestCase(0, MotionIntent.Idle)]
        [TestCase(4, MotionIntent.Wave)]
        [TestCase(5, MotionIntent.Idle)]
        [TestCase(9999, MotionIntent.Idle)]
        public void FromIndex_ClampsOutOfRangeToFallback(int index, MotionIntent expected)
        {
            Assert.That(MotionIntents.FromIndex(index), Is.EqualTo(expected));
        }

        [Test]
        public void ToName_RoundTripsThroughParse()
        {
            for (int i = 0; i < MotionIntents.Names.Length; i++)
            {
                MotionIntent intent = (MotionIntent)i;
                Assert.That(MotionIntents.Parse(MotionIntents.ToName(intent)), Is.EqualTo(intent));
            }
        }
    }
}
