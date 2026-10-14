using NUnit.Framework;
using UnityEngine;

namespace DigitalHuman.Tests
{
    /// <summary>
    /// Picking an item up, putting it down and placing it must leave its size alone. It used
    /// to shrink to a fixed held scale and grow back to its first scale on release.
    /// </summary>
    public sealed class AgentInteractionTests
    {
        private const float Tolerance = 1e-4f;
        private static readonly Vector3 BoxSize = Vector3.one * 0.4f;

        // Far from the sample scene's NavMesh, so drops land at the agent's own height.
        private static readonly Vector3 Origin = new Vector3(500f, 0f, 500f);

        private GameObject agent;
        private GameObject box;
        private GameObject table;
        private Transform hand;
        private AgentInteraction interaction;

        [SetUp]
        public void SetUp()
        {
            agent = new GameObject("Agent");
            agent.transform.position = Origin;

            // Rig bones often carry a scale of their own; a held item must not take it on.
            hand = new GameObject("Hand").transform;
            hand.SetParent(agent.transform, false);
            hand.localPosition = new Vector3(0.2f, 1f, 0.3f);
            hand.localScale = Vector3.one * 0.01f;

            interaction = agent.AddComponent<AgentInteraction>();
            interaction.Configure(null, hand);

            box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "RedBox";
            box.transform.position = Origin + new Vector3(0f, 0.2f, 0.6f);
            box.transform.localScale = BoxSize;
            box.AddComponent<PickupItem>().Configure("Red Box", "red");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(box);
            Object.DestroyImmediate(agent);
            if (table != null)
            {
                Object.DestroyImmediate(table);
            }
        }

        [Test]
        public void Pickup_KeepsTheItemsSize()
        {
            Assert.That(interaction.Pickup(box), Is.True);

            Assert.That(box.transform.parent, Is.EqualTo(hand));
            AssertSize(box);
        }

        [Test]
        public void Drop_KeepsTheItemsSizeAndRestsItOnTheFloor()
        {
            interaction.Pickup(box);

            Assert.That(interaction.DropInFront(), Is.True);

            AssertSize(box);
            Assert.That(
                box.transform.position.y,
                Is.EqualTo(Origin.y + BoxSize.y * 0.5f).Within(Tolerance),
                "The bottom of the item, not its centre, belongs on the floor.");
            Assert.That(interaction.HeldItem, Is.Null);
        }

        [Test]
        public void PlaceOnSurface_KeepsTheItemsSize()
        {
            table = GameObject.CreatePrimitive(PrimitiveType.Cube);
            table.transform.position = Origin + new Vector3(0f, 0.75f, 2f);
            table.transform.localScale = new Vector3(1.8f, 1.5f, 1.8f);
            Physics.SyncTransforms();
            PlaceSurface surface = table.AddComponent<PlaceSurface>();
            surface.Configure("obstacle");
            interaction.Pickup(box);

            Assert.That(interaction.PlaceHeldItemOn(surface), Is.True);

            AssertSize(box);
            Assert.That(box.transform.position.y, Is.GreaterThan(1.5f), "The item belongs on top of the surface.");
        }

        [Test]
        public void PickupRoutine_WithoutAnIkRig_PicksUpAtOnce()
        {
            var routine = interaction.PickupRoutine(box);
            int steps = 0;
            while (routine.MoveNext() && steps++ < 1000)
            {
            }

            Assert.That(interaction.LastActionSucceeded, Is.True);
            Assert.That(interaction.HeldItem, Is.EqualTo(box));
            AssertSize(box);
        }

        private static void AssertSize(GameObject item)
        {
            Assert.That(
                Vector3.Distance(item.transform.lossyScale, BoxSize),
                Is.LessThan(Tolerance),
                $"Size changed to {item.transform.lossyScale:F3}.");
        }
    }
}
