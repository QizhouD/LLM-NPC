using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NUnit.Framework;

namespace DigitalHuman.Tests
{
    public sealed class ChatDemoScriptTests
    {
        private const string ShippedScriptPath = "Assets/DigitalHuman/Demo/FullActionDemo.txt";

        [Test]
        public void Parse_TurnsPlainRowsIntoChatLinesAndSkipsCommentsAndBlanks()
        {
            List<DemoStep> steps = ChatDemoScript.Parse(
                "# a comment\n\n  Hello there  \r\nPick up the red box.\n");

            Assert.That(steps.Count, Is.EqualTo(2));
            Assert.That(steps[0].Kind, Is.EqualTo(DemoStepKind.Say));
            Assert.That(steps[0].Text, Is.EqualTo("Hello there"));
            Assert.That(steps[0].Line, Is.EqualTo(3));
            Assert.That(steps[1].Text, Is.EqualTo("Pick up the red box."));
        }

        [Test]
        public void Parse_ReadsEveryDirection()
        {
            List<string> errors = new List<string>();
            List<DemoStep> steps = ChatDemoScript.Parse(
                "@wait 2.5\n@hold 0\n@typing 20\n@nowait\n" +
                "@camera overview\n@camera player\n@camera npc\n@camera npc side\n" +
                "@CAMERA EVA Back\n@camera npc free",
                errors);

            Assert.That(errors, Is.Empty);
            Assert.That(steps.Count, Is.EqualTo(10));
            AssertStep(steps[0], DemoStepKind.Wait, 2.5f);
            AssertStep(steps[1], DemoStepKind.Hold, 0f);
            AssertStep(steps[2], DemoStepKind.Typing, 20f);
            Assert.That(steps[3].Kind, Is.EqualTo(DemoStepKind.NoWait));
            AssertCamera(steps[4], CameraShot.Overview);
            AssertCamera(steps[5], CameraShot.FollowPlayer);
            AssertCamera(steps[6], CameraShot.FollowNpc, NpcCameraAngle.Front);
            AssertCamera(steps[7], CameraShot.FollowNpc, NpcCameraAngle.Side);
            AssertCamera(steps[8], CameraShot.FollowNpc, NpcCameraAngle.Back);
            AssertCamera(steps[9], CameraShot.FollowNpc, NpcCameraAngle.Free);
        }

        [TestCase("@wait")]
        [TestCase("@wait soon")]
        [TestCase("@wait -1")]
        [TestCase("@typing 0")]
        [TestCase("@nowait please")]
        [TestCase("@camera")]
        [TestCase("@camera npc sideways")]
        [TestCase("@camera player front")]
        [TestCase("@dance")]
        public void Parse_ReportsAMalformedDirectionAndCarriesOn(string direction)
        {
            List<string> errors = new List<string>();
            List<DemoStep> steps = ChatDemoScript.Parse(
                "Hello\n" + direction + "\nGoodbye", errors);

            Assert.That(errors.Count, Is.EqualTo(1));
            Assert.That(errors[0], Does.StartWith("line 2:"));
            Assert.That(steps.Count, Is.EqualTo(2));
            Assert.That(steps[1].Text, Is.EqualTo("Goodbye"));
        }

        [Test]
        public void Parse_ReadsDecimalPointsWhateverTheLocale()
        {
            CultureInfo previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");

                List<DemoStep> steps = ChatDemoScript.Parse("@wait 1.5");

                AssertStep(steps[0], DemoStepKind.Wait, 1.5f);
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [Test]
        public void ShippedScript_ParsesWithoutErrors()
        {
            List<string> errors = new List<string>();
            List<DemoStep> steps = ChatDemoScript.Parse(
                File.ReadAllText(ShippedScriptPath), errors);

            Assert.That(errors, Is.Empty, string.Join("\n", errors));
            Assert.That(steps.Exists(step => step.Kind == DemoStepKind.Say), Is.True);
        }

        /// <summary>
        /// The demo is meant to show everything the character can do, so a capability without
        /// a line in the script is missing from the recording.
        /// </summary>
        [TestCase("wave")]
        [TestCase("jump")]
        [TestCase("pick up")]
        [TestCase("drop")]
        [TestCase("on the obstacle")]
        [TestCase("turn")]
        [TestCase("come")]
        [TestCase("stop")]
        [TestCase("careful")]
        public void ShippedScript_ExercisesEveryAction(string phrase)
        {
            List<DemoStep> steps = ChatDemoScript.Parse(File.ReadAllText(ShippedScriptPath));

            bool found = steps.Exists(step =>
                step.Kind == DemoStepKind.Say &&
                step.Text.IndexOf(phrase, StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.That(found, Is.True, $"no chat line containing '{phrase}'");
        }

        private static void AssertStep(DemoStep step, DemoStepKind kind, float value)
        {
            Assert.That(step.Kind, Is.EqualTo(kind));
            Assert.That(step.Value, Is.EqualTo(value));
        }

        private static void AssertCamera(
            DemoStep step,
            CameraShot shot,
            NpcCameraAngle angle = NpcCameraAngle.Front)
        {
            Assert.That(step.Kind, Is.EqualTo(DemoStepKind.Camera));
            Assert.That(step.Shot, Is.EqualTo(shot));
            if (shot == CameraShot.FollowNpc)
            {
                Assert.That(step.Angle, Is.EqualTo(angle));
            }
        }
    }
}
