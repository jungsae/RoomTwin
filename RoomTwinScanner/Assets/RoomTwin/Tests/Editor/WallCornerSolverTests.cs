using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace RoomTwin.Geometry.Tests
{
    public sealed class WallCornerSolverTests
    {
        [Test]
        public void PerpendicularWalls_MeetTheFloorAtTheExpectedCorner()
        {
            DetectedWall east = Wall(id: "east", point: new Vector3(2f, 1f, 1f), normal: Vector3.right, center: new Vector3(2f, 1f, 1f), half: new Vector2(1.5f, 1.2f));
            DetectedWall south = Wall(id: "south", point: new Vector3(1f, 1f, 0f), normal: Vector3.forward, center: new Vector3(1f, 1f, 0f), half: new Vector2(1.5f, 1.2f));

            bool solved = WallCornerSolver.TryPair(east, south, 0.4f, out WallCornerEstimate estimate);

            Assert.IsTrue(solved, estimate.Rejection);
            Assert.AreEqual(new Vector3(2f, 0.4f, 0f), estimate.Corner);
            Assert.Less(estimate.PlaneResidualMeters, 0.001f);
            Assert.AreEqual(0f, estimate.BoundaryOverflowMeters, 0.001f);
        }

        [Test]
        public void ParallelWalls_AreRejected()
        {
            DetectedWall a = Wall("a", new Vector3(0f, 1f, 0f), Vector3.right, new Vector3(0f, 1f, 0f), new Vector2(2f, 1f));
            DetectedWall b = Wall("b", new Vector3(3f, 1f, 0f), new Vector3(0.99f, 0f, 0.05f), new Vector3(3f, 1f, 0f), new Vector2(2f, 1f));

            bool solved = WallCornerSolver.TryPair(a, b, 0f, out WallCornerEstimate estimate);

            Assert.IsFalse(solved);
            StringAssert.Contains("parallel", estimate.Rejection);
        }

        [Test]
        public void DistantIntersection_IsRejected()
        {
            DetectedWall a = Wall("a", Vector3.zero, Vector3.right, new Vector3(0f, 1f, 12f), new Vector2(0.4f, 0.4f));
            DetectedWall b = Wall("b", Vector3.zero, Vector3.forward, new Vector3(0f, 1f, 0f), new Vector2(0.4f, 0.4f));

            bool solved = WallCornerSolver.TryPair(a, b, 0f, out WallCornerEstimate estimate);

            Assert.IsFalse(solved);
            StringAssert.Contains("far", estimate.Rejection);
        }

        [Test]
        public void UncertainFloorHeights_DoNotLockACorner()
        {
            bool chosen = WallCornerSolver.TryChooseFloorY(new[] { 0f, 0.8f }, 0.12f, out _, out string error);

            Assert.IsFalse(chosen);
            StringAssert.Contains("uncertain", error);
        }

        [Test]
        public void SimilarFloorHeights_LockTheAverage()
        {
            bool chosen = WallCornerSolver.TryChooseFloorY(new[] { 0.10f, 0.16f }, 0.12f, out float floorY, out string error);

            Assert.IsTrue(chosen, error);
            Assert.AreEqual(0.13f, floorY, 0.0001f);
        }

        [Test]
        public void MissingFloor_DoesNotLockACorner()
        {
            bool chosen = WallCornerSolver.TryChooseFloorY((IReadOnlyList<float>)null, 0.12f, out _, out string error);

            Assert.IsFalse(chosen);
            StringAssert.Contains("floor", error);
        }

        [Test]
        public void CloserAdjacentWall_WinsOverAFartherCandidate()
        {
            DetectedWall selected = Wall("selected", new Vector3(0f, 1f, 0f), Vector3.forward, new Vector3(1f, 1f, 0f), new Vector2(3f, 1.2f));
            DetectedWall near = Wall("near", new Vector3(0f, 1f, 1f), Vector3.right, new Vector3(0f, 1f, 1f), new Vector2(2f, 1.2f));
            DetectedWall far = Wall("far", new Vector3(5f, 1f, 1f), Vector3.right, new Vector3(5f, 1f, 4f), new Vector2(0.3f, 0.3f));

            bool solved = WallCornerSolver.TryEstimate(selected, new[] { far, near }, 0f, out WallCornerEstimate estimate);

            Assert.IsTrue(solved, estimate.Rejection);
            Assert.AreEqual("near", estimate.OtherWallId);
            Assert.AreEqual(new Vector3(0f, 0f, 0f), estimate.Corner);
        }

        [Test]
        public void NoAdjacentWall_AsksForAnotherScan()
        {
            DetectedWall selected = Wall("selected", Vector3.zero, Vector3.forward, Vector3.zero, new Vector2(1f, 1f));

            bool solved = WallCornerSolver.TryEstimate(selected, new DetectedWall[0], 0f, out WallCornerEstimate estimate);

            Assert.IsFalse(solved);
            StringAssert.Contains("adjacent wall", estimate.Rejection);
        }

        [Test]
        public void FourEstimatedCorners_BuildTheExistingRoomMesh()
        {
            var capture = new FloorCornerCapture();
            Vector3[] corners =
            {
                Estimate(0f, 0f),
                Estimate(4f, 0f),
                Estimate(4f, 3f),
                Estimate(0f, 3f)
            };
            for (int i = 0; i < corners.Length; i++)
            {
                Assert.IsTrue(capture.TryAdd(corners[i], out string rejection), rejection);
            }

            bool mapped = RoomLocalFrame.TryMap(
                capture.WorldCorners,
                2.4f,
                out RoomGeometryData data,
                out _,
                out _,
                out _,
                out string error);

            Assert.IsTrue(mapped, error);
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

        static Vector3 Estimate(float x, float z)
        {
            DetectedWall wallX = Wall("x", new Vector3(x, 1f, z + 1f), Vector3.right, new Vector3(x, 1f, z + 1f), new Vector2(2f, 1.2f));
            DetectedWall wallZ = Wall("z", new Vector3(x + 1f, 1f, z), Vector3.forward, new Vector3(x + 1f, 1f, z), new Vector2(2f, 1.2f));
            Assert.IsTrue(WallCornerSolver.TryPair(wallX, wallZ, 0f, out WallCornerEstimate estimate), estimate.Rejection);
            return estimate.Corner;
        }

        static DetectedWall Wall(string id, Vector3 point, Vector3 normal, Vector3 center, Vector2 half)
        {
            Vector3 n = normal.normalized;
            Vector3 axisU = Vector3.Cross(Vector3.up, n).normalized;
            if (axisU.sqrMagnitude < 0.001f)
            {
                axisU = Vector3.right;
            }

            return new DetectedWall
            {
                Id = id,
                Point = point,
                Normal = n,
                Center = center,
                AxisU = axisU,
                AxisV = Vector3.Cross(n, axisU).normalized,
                HalfExtents = half
            };
        }
    }
}
