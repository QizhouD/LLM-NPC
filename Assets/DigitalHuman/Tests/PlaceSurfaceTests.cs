using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace DigitalHuman.Tests
{
    /// <summary>
    /// Where an item goes on a surface: next to whoever is placing it, wholly on top, and on
    /// top of an earlier item only when it would otherwise overlap it.
    /// </summary>
    public sealed class PlaceSurfaceTests
    {
        private const float Tolerance = 1e-3f;
        private const float Padding = 0.02f;
        private static readonly Vector3 ItemSize = Vector3.one * 0.4f;

        // 1.8 m square and 1.5 m tall, like the obstacle in the sample scene.
        private static readonly Vector3 SurfaceSize = new Vector3(1.8f, 1.5f, 1.8f);

        private readonly List<GameObject> created = new List<GameObject>();
        private PlaceSurface surface;

        [SetUp]
        public void SetUp()
        {
            GameObject table = Cube("Table", new Vector3(0f, SurfaceSize.y * 0.5f, 0f), SurfaceSize);
            surface = table.AddComponent<PlaceSurface>();
            surface.Configure("obstacle");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject item in created)
            {
                Object.DestroyImmediate(item);
            }

            created.Clear();
        }

        [Test]
        public void FromOneSide_PutsTheItemJustInsideTheNearEdge()
        {
            Vector3 spot = surface.ReservePlacePosition(Item("A"), ItemSize, new Vector3(0f, 0f, -3f));

            float insideEdge = -SurfaceSize.z * 0.5f + ItemSize.z * 0.5f + Padding;
            AssertNear(spot, new Vector3(0f, RestingHeight(), insideEdge));
        }

        [Test]
        public void PastACorner_KeepsTheWholeItemOnTop()
        {
            Vector3 spot = surface.ReservePlacePosition(Item("A"), ItemSize, new Vector3(5f, 0f, -3f));

            float inset = SurfaceSize.x * 0.5f - ItemSize.x * 0.5f - Padding;
            AssertNear(spot, new Vector3(inset, RestingHeight(), -inset));
        }

        [Test]
        public void WithoutAnApproach_UsesTheCentre()
        {
            Vector3 spot = surface.ReservePlacePosition(Item("A"), ItemSize);

            AssertNear(spot, new Vector3(0f, RestingHeight(), 0f));
        }

        [Test]
        public void StacksOnlyOnAnItemUnderTheSameFootprint()
        {
            Vector3 near = new Vector3(0f, 0f, -3f);
            GameObject first = Item("A");
            first.transform.position = surface.ReservePlacePosition(first, ItemSize, near);
            Physics.SyncTransforms();

            Vector3 farSide = surface.ReservePlacePosition(Item("B"), ItemSize, new Vector3(0f, 0f, 3f));
            Vector3 onTop = surface.ReservePlacePosition(Item("C"), ItemSize, near);

            Assert.That(
                farSide.y,
                Is.EqualTo(RestingHeight()).Within(Tolerance),
                "An item on the other side must not lift this one.");
            Assert.That(
                onTop.y,
                Is.EqualTo(RestingHeight() + ItemSize.y + Padding).Within(Tolerance),
                "An item in the same spot must be stacked on, not pushed into.");
        }

        private static float RestingHeight()
        {
            return SurfaceSize.y + ItemSize.y * 0.5f + Padding;
        }

        private GameObject Item(string name)
        {
            return Cube(name, new Vector3(10f, 0.2f, 10f), ItemSize);
        }

        private GameObject Cube(string name, Vector3 position, Vector3 size)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.position = position;
            cube.transform.localScale = size;
            created.Add(cube);
            Physics.SyncTransforms();
            return cube;
        }

        private static void AssertNear(Vector3 actual, Vector3 expected)
        {
            Assert.That(
                Vector3.Distance(actual, expected),
                Is.LessThan(Tolerance),
                $"Expected {expected:F3}, got {actual:F3}.");
        }
    }
}
