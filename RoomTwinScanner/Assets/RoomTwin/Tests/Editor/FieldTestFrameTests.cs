using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace RoomTwin.Geometry.Tests
{
    public sealed class FieldTestFrameTests
    {
        [Test]
        public void WorldSquare_MapsToLocalXz_AndReportsEdgeLengths()
        {
            Vector3[] world =
            {
                new Vector3(10f, 1.2f, -2f),
                new Vector3(14f, 1.25f, -2f),
                new Vector3(14f, 1.15f, 1f),
                new Vector3(10f, 1.2f, 1f)
            };

            bool mapped = RoomLocalFrame.TryMap(
                world,
                2.4f,
                out RoomGeometryData data,
                out Vector3 anchor,
                out float floorY,
                out float[] edges,
                out string error);

            Assert.IsTrue(mapped, error);
            Assert.AreEqual(0f, data.floorCornersXz[0].x, 0.0001f);
            Assert.AreEqual(0f, data.floorCornersXz[0].y, 0.0001f);
            Assert.AreEqual(4f, data.floorCornersXz[1].x, 0.0001f);
            Assert.AreEqual(0f, data.floorCornersXz[1].y, 0.0001f);
            Assert.AreEqual(10f, anchor.x, 0.0001f);
            Assert.AreEqual(-2f, anchor.z, 0.0001f);
            Assert.AreEqual(1.2f, floorY, 0.0001f);
            Assert.AreEqual(floorY, anchor.y, 0.0001f);
            Assert.AreEqual(4f, edges[0], 0.0001f);
            Assert.AreEqual(3f, edges[1], 0.0001f);
            Assert.AreEqual(4f, edges[2], 0.0001f);
            Assert.AreEqual(3f, edges[3], 0.0001f);
            Assert.AreEqual(2.4f, data.wallHeightMeters, 0.0001f);
        }

        [Test]
        public void ClockwiseWorldOrder_StillValidates()
        {
            Vector3[] world =
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(0f, 0f, 3f),
                new Vector3(4f, 0f, 3f),
                new Vector3(4f, 0f, 0f)
            };

            bool mapped = RoomLocalFrame.TryMap(world, 2.4f, out RoomGeometryData data, out _, out _, out _, out string error);

            Assert.IsTrue(mapped, error);
            Assert.Greater(RoomGeometryValidator.SignedArea(data.floorCornersXz), 0f);
        }

        [Test]
        public void CrossedWorldOrder_FailsWithoutAMeshPose()
        {
            Vector3[] world =
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(4f, 0f, 3f),
                new Vector3(4f, 0f, 0f),
                new Vector3(0f, 0f, 3f)
            };

            bool mapped = RoomLocalFrame.TryMap(world, 2.4f, out _, out _, out _, out float[] edges, out string error);

            Assert.IsFalse(mapped);
            StringAssert.Contains("convex", error);
            Assert.AreEqual(4, edges.Length);
        }

        [Test]
        public void CloseCorners_AreRejectedBeforeCompletion()
        {
            var capture = new FloorCornerCapture();

            Assert.IsTrue(capture.TryAdd(new Vector3(0f, 0f, 0f), out _));
            bool added = capture.TryAdd(new Vector3(0.02f, 0.1f, 0.01f), out string rejection);

            Assert.IsFalse(added);
            StringAssert.Contains("closer", rejection);
            Assert.AreEqual(1, capture.Count);
        }

        [Test]
        public void Reset_AllowsANewMeasurement()
        {
            var capture = new FloorCornerCapture();
            Vector3[] first =
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(4f, 0f, 0f),
                new Vector3(4f, 0f, 3f),
                new Vector3(0f, 0f, 3f)
            };
            for (int i = 0; i < first.Length; i++)
            {
                Assert.IsTrue(capture.TryAdd(first[i], out string rejection), rejection);
            }

            capture.Reset();
            Assert.AreEqual(0, capture.Count);
            Assert.IsTrue(capture.TryAdd(new Vector3(1f, 0.5f, 1f), out _));
            Assert.AreEqual(1, capture.Count);
        }

        [Test]
        public void FourDistinctCorners_CanBeAddedInOrder()
        {
            var capture = new FloorCornerCapture();
            IReadOnlyList<Vector3> corners = new[]
            {
                new Vector3(0f, 1f, 0f),
                new Vector3(3f, 1f, 0f),
                new Vector3(3f, 1f, 2f),
                new Vector3(0f, 1f, 2f)
            };

            for (int i = 0; i < corners.Count; i++)
            {
                Assert.IsTrue(capture.TryAdd(corners[i], out string rejection), rejection);
            }

            Assert.IsTrue(capture.IsComplete);
            Assert.IsFalse(capture.TryAdd(new Vector3(8f, 1f, 8f), out string full));
            StringAssert.Contains("Reset", full);
        }
    }
}
