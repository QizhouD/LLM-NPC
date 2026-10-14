using NUnit.Framework;
using UnityEngine;

namespace DigitalHuman.Tests
{
    public sealed class AgentCommandParsingTests
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

        [TestCase("Jump!")]
        [TestCase("Can you jump for me?")]
        [TestCase("跳一下")]
        [TestCase("Pick up the green box.")]
        [TestCase("Put it on the obstacle.")]
        [TestCase("Drop it.")]
        [TestCase("拿起蓝色箱子")]
        public void CanHandle_RecognisesActionsTheBodyPerforms(string message)
        {
            Assert.That(commands.CanHandle(message), Is.True);
        }

        /// <summary>
        /// These are driven by the model's intent tag. If the parser claimed them too, the
        /// reply would also be treated as a command turn and its walk or turn tag dropped.
        /// </summary>
        [TestCase("Come over here!")]
        [TestCase("Turn around and face me.")]
        [TestCase("Hi there! Can you wave hello?")]
        [TestCase("What can you do?")]
        public void CanHandle_LeavesTagDrivenRequestsToTheModel(string message)
        {
            Assert.That(commands.CanHandle(message), Is.False);
        }
    }
}
