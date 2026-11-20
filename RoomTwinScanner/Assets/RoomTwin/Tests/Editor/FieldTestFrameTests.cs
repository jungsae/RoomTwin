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

        [Test]
        public void DifferentFloorHeights_AreRejected_AndCountStays()
        {
            var capture = new FloorCornerCapture();
            Assert.IsTrue(capture.TryAdd(new Vector3(1.109f, -0.693f, -1.138f), out _));

            bool added = capture.TryAdd(new Vector3(-0.051f, 0.110f, 0.005f), out string rejection);

            Assert.IsFalse(added);
            StringAssert.Contains("Height differs", rejection);
            Assert.AreEqual(1, capture.Count);
            Assert.AreEqual(-0.693f, capture.ReferenceFloorY, 0.0001f);
        }

        [Test]
        public void HeightWithinTolerance_IsAccepted()
        {
            var capture = new FloorCornerCapture();
            Assert.IsTrue(capture.TryAdd(new Vector3(0f, 1f, 0f), out _));

            bool added = capture.TryAdd(new Vector3(2f, 1.10f, 0f), out string rejection);

            Assert.IsTrue(added, rejection);
            Assert.AreEqual(2, capture.Count);
        }

        [Test]
        public void Reset_ClearsReferenceFloorHeight()
        {
            var capture = new FloorCornerCapture();
            Assert.IsTrue(capture.TryAdd(new Vector3(0f, -0.693f, 0f), out _));
            Assert.IsTrue(capture.HasReferenceFloorY);

            capture.Reset();

            Assert.IsFalse(capture.HasReferenceFloorY);
            Assert.AreEqual(0, capture.Count);
            Assert.IsTrue(capture.TryAdd(new Vector3(0f, 0.8f, 0f), out string rejection), rejection);
            Assert.AreEqual(0.8f, capture.ReferenceFloorY, 0.0001f);
        }

        [Test]
        public void SameHeightFourCorners_MapToAValidRoom()
        {
            var capture = new FloorCornerCapture();
            Vector3[] world =
            {
                new Vector3(0f, 0.2f, 0f),
                new Vector3(4f, 0.24f, 0f),
                new Vector3(4f, 0.16f, 3f),
                new Vector3(0f, 0.22f, 3f)
            };
            for (int i = 0; i < world.Length; i++)
            {
                Assert.IsTrue(capture.TryAdd(world[i], out string rejection), rejection);
            }

            bool mapped = RoomLocalFrame.TryMap(
                capture.WorldCorners,
                2.4f,
                out RoomGeometryData data,
                out Vector3 anchor,
                out float floorY,
                out float[] edges,
                out string error);

            Assert.IsTrue(mapped, error);
            Assert.AreEqual(0.2f, floorY, 0.0001f);
            Assert.AreEqual(0.2f, anchor.y, 0.0001f);
            Assert.AreEqual(4, edges.Length);
            Assert.Greater(RoomGeometryValidator.SignedArea(data.floorCornersXz), 0f);
            Mesh floor = RoomMeshGenerator.BuildFloor(data.floorCornersXz);
            Mesh[] walls = RoomMeshGenerator.BuildWalls(data.floorCornersXz, data.wallHeightMeters);
            try
            {
                Assert.AreEqual(4, floor.vertexCount);
                Assert.AreEqual(4, walls.Length);
            }
            finally
            {
                Object.DestroyImmediate(floor);
                for (int i = 0; i < walls.Length; i++)
                {
                    Object.DestroyImmediate(walls[i]);
                }
            }
        }
    }
}
