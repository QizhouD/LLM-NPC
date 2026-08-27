using NUnit.Framework;
using UnityEngine;

namespace DigitalHuman.Tests
{
    public sealed class DigitalHumanPersonaTests
    {
        private DigitalHumanPersona persona;

        [SetUp]
        public void SetUp()
        {
            persona = ScriptableObject.CreateInstance<DigitalHumanPersona>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(persona);
        }

        [Test]
        public void BuildSystemPrompt_IncludesEveryPersonaField()
        {
            persona.Configure(
                "Vexa",
                "field guide",
                "wry and observant",
                "raised on the outer platforms",
                "clipped sentences",
                "knows the platform layout",
                "keep the visitor safe",
                "cautious but warming up");

            string prompt = persona.BuildSystemPrompt();

            Assert.That(prompt, Does.Contain("Vexa"));
            Assert.That(prompt, Does.Contain("field guide"));
            Assert.That(prompt, Does.Contain("wry and observant"));
            Assert.That(prompt, Does.Contain("raised on the outer platforms"));
            Assert.That(prompt, Does.Contain("clipped sentences"));
            Assert.That(prompt, Does.Contain("knows the platform layout"));
            Assert.That(prompt, Does.Contain("keep the visitor safe"));
            Assert.That(prompt, Does.Contain("cautious but warming up"));
        }

        [Test]
        public void BuildSystemPrompt_AppendsGameContextWhenProvided()
        {
            string prompt = persona.BuildSystemPrompt("Player is holding the red box.");

            Assert.That(prompt, Does.Contain("Current game context:"));
            Assert.That(prompt, Does.Contain("Player is holding the red box."));
        }

        [Test]
        public void BuildSystemPrompt_OmitsContextSectionWhenBlank()
        {
            Assert.That(persona.BuildSystemPrompt("   "), Does.Not.Contain("Current game context:"));
            Assert.That(persona.BuildSystemPrompt(null), Does.Not.Contain("Current game context:"));
        }

        [Test]
        public void Configure_FallsBackForBlankFields()
        {
            persona.Configure("  ", null, "", "  ", null, "", "  ", null);

            string prompt = persona.BuildSystemPrompt();

            Assert.That(prompt, Does.Contain("EVA"));
            Assert.That(prompt, Does.Not.Contain("Role: \n"));
        }

        [Test]
        public void BuildSystemPrompt_StatesTheEmbodiedFraming()
        {
            string prompt = persona.BuildSystemPrompt();

            Assert.That(prompt, Does.Contain("Stay in character."));
            Assert.That(prompt, Does.Contain("Do not describe yourself as a chatbot"));
        }

        /// <summary>
        /// The prompt enumerates the vocabularies from the enums, so this guards against the
        /// prompt and the parser drifting apart.
        /// </summary>
        [Test]
        public void BuildSystemPrompt_EnumeratesEveryIntentAndEmotion()
        {
            string prompt = persona.BuildSystemPrompt();

            Assert.That(prompt, Does.Contain("[intent:"));
            Assert.That(prompt, Does.Contain("[emotion:"));
            foreach (string name in MotionIntents.Names)
            {
                Assert.That(prompt, Does.Contain(name), $"intent '{name}' missing from prompt");
            }

            foreach (string name in FaceEmotions.Names)
            {
                Assert.That(prompt, Does.Contain(name), $"emotion '{name}' missing from prompt");
            }
        }

        /// <summary>
        /// The example the prompt shows the model must survive the parser it is teaching the
        /// model to satisfy.
        /// </summary>
        [Test]
        public void BuildSystemPrompt_ExampleParsesCleanly()
        {
            ResponseAnnotation parsed = ResponseAnnotationParser.Parse(
                "Of course, follow me. [intent:walk][emotion:curious]");

            Assert.That(persona.BuildSystemPrompt(),
                Does.Contain("Of course, follow me. [intent:walk][emotion:curious]"));
            Assert.That(parsed.HasIntent, Is.True);
            Assert.That(parsed.HasEmotion, Is.True);
            Assert.That(parsed.Text, Is.EqualTo("Of course, follow me."));
        }

        /// <summary>
        /// The prose is spoken aloud and the tags already move the body, so narrated actions
        /// like "*walks over, smiling*" are both redundant and unspeakable. Observed coming
        /// back from a live model, hence the explicit instruction and this guard.
        /// </summary>
        [Test]
        public void BuildSystemPrompt_ForbidsNarratedActions()
        {
            string prompt = persona.BuildSystemPrompt();

            Assert.That(prompt, Does.Contain("never narrate your own actions"));
            Assert.That(prompt, Does.Contain("asterisks"));
            Assert.That(prompt, Does.Contain("Wrong:"));
            Assert.That(prompt, Does.Contain("Right:"));
        }
    }
}
