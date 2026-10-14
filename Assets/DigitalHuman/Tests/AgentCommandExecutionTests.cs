using NUnit.Framework;
using UnityEngine;

namespace DigitalHuman.Tests
{
    public sealed class AgentCommandExecutionTests
    {
        private GameObject host;
        private AgentCommandController commands;

        [SetUp]
        public void SetUp()
        {
            host = new GameObject("AgentCommandController");
            commands = host.AddComponent<AgentCommandController>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(host);
        }

        [Test]
        public void DropWithEmptyHands_FailsWithoutBlockingTheNextCommand()
        {
            string failure = null;
            commands.CommandFailed += message => failure = message;

            Assert.That(commands.ExecuteCommand("Drop it."), Is.True);

            Assert.That(failure, Is.EqualTo("I'm not holding anything to drop."));
            Assert.That(commands.IsExecutingCommand, Is.False);
        }

        [Test]
        public void PlaceWithNothingToPlace_FailsWithoutBlockingTheNextCommand()
        {
            int failures = 0;
            commands.CommandFailed += _ => failures++;

            commands.ExecuteCommand("Put it on the obstacle.");
            commands.ExecuteCommand("Put it on the obstacle.");

            Assert.That(failures, Is.EqualTo(2));
            Assert.That(commands.IsExecutingCommand, Is.False);
        }
    }
}
