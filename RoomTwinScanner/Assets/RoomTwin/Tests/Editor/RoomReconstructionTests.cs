using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace RoomTwin.Geometry.Tests
{
    public sealed class RoomReconstructionTests
    {
        [Test]
        public void CoplanarFragments_MergeIntoOneWall()
        {
            WallClusterBuilder builder = Build(
                Obs("a", new Vector3(2f, 1f, 0f), Vector3.right, new Vector3(2f, 1f, 0f), new Vector2(1f, 1.2f)),
                Obs("b", new Vector3(2f, 1f, 1.6f), Vector3.right, new Vector3(2f, 1f, 1.6f), new Vector2(1f, 1.2f)));

            Assert.AreEqual(1, builder.Clusters.Count);
            Assert.AreEqual(2, builder.Clusters[0].MemberIds.Count);
        }

        [Test]
        public void OppositeNormals_OnTheSamePlane_Merge()
        {
            WallClusterBuilder builder = Build(
                Obs("front", new Vector3(2f, 1f, 1f), Vector3.right, new Vector3(2f, 1f, 1f), new Vector2(1.5f, 1.2f)),
                Obs("back", new Vector3(2f, 1f, 1f), Vector3.left, new Vector3(2f, 1f, 1.2f), new Vector2(1.5f, 1.2f)));

            Assert.AreEqual(1, builder.Clusters.Count);
            Assert.AreEqual(2, builder.Clusters[0].MemberIds.Count);
        }

        [Test]
        public void ParallelWalls_WithALargeOffset_StaySeparate()
        {
            WallClusterBuilder builder = Build(
                Obs("near", new Vector3(0f, 1f, 1f), Vector3.right, new Vector3(0f, 1f, 1f), new Vector2(1.5f, 1.2f)),
                Obs("far", new Vector3(1.5f, 1f, 1f), Vector3.right, new Vector3(1.5f, 1f, 1f), new Vector2(1.5f, 1.2f)));

            Assert.AreEqual(2, builder.Clusters.Count);
        }

        [Test]
        public void ClosetFace_DoesNotMergeWithTheRearWall()
        {
            WallClusterBuilder builder = Build(
                Obs("wall", new Vector3(0f, 1f, 0f), Vector3.forward, new Vector3(1f, 1f, 0f), new Vector2(1.2f, 1.2f)),
                Obs("closet", new Vector3(1f, 1f, 0.4f), Vector3.forward, new Vector3(1f, 1f, 0.4f), new Vector2(0.6f, 1f)));

            Assert.AreEqual(2, builder.Clusters.Count);
        }

        [Test]
        public void SubsumedPlane_IsDiscarded()
        {
            WallObservation subsumed = Obs("child", new Vector3(2f, 1f, 0f), Vector3.right, new Vector3(2f, 1f, 0f), new Vector2(1f, 1f));
            subsumed.Subsumed = true;
            WallClusterBuilder builder = Build(
                subsumed,
                Obs("parent", new Vector3(2f, 1f, 0f), Vector3.right, new Vector3(2f, 1f, 0.4f), new Vector2(1.4f, 1.2f)));

            Assert.AreEqual(1, builder.Clusters.Count);
            Assert.AreEqual(1, builder.Discarded.Count);
            StringAssert.Contains("subsumed", builder.Discarded[0].DiscardReason);
            CollectionAssert.DoesNotContain(builder.Clusters[0].MemberIds, "child");
        }

        [Test]
        public void LostTracking_IsDiscarded_AndLimitedStaysLessConfident()
        {
            WallObservation lost = Obs("lost", new Vector3(0f, 1f, 0f), Vector3.forward, new Vector3(1f, 1f, 0f), new Vector2(1f, 1f));
            lost.Tracking = WallTrackingQuality.None;
            WallObservation limited = Obs("limited", new Vector3(0f, 1f, 3f), Vector3.forward, new Vector3(1f, 1f, 3f), new Vector2(1.5f, 1.2f));
            limited.Tracking = WallTrackingQuality.Limited;
            WallClusterBuilder builder = Build(
                lost,
                limited,
                Obs("stable", new Vector3(4f, 1f, 1f), Vector3.right, new Vector3(4f, 1f, 1f), new Vector2(1.5f, 1.2f)));

            Assert.AreEqual(2, builder.Clusters.Count);
            Assert.AreEqual(1, builder.Discarded.Count);
            StringAssert.Contains("tracking", builder.Discarded[0].DiscardReason);
            WallCluster limitedCluster = builder.FindByMember("limited");
            WallCluster stableCluster = builder.FindByMember("stable");
            Assert.Less(limitedCluster.Confidence, stableCluster.Confidence);
        }

        [Test]
        public void RectangleRoom_ClosesOnObservedWalls()
        {
            bool built = Solve(out RoomReconstruction result, Rectangle());

            Assert.IsTrue(built, result.Rejection);
            Assert.AreEqual(4, result.Corners.Length);
            Assert.AreEqual(4, result.EdgeLengths.Length);
            float opposite = result.EdgeLengths[0] + result.EdgeLengths[2];
            Assert.IsTrue(Mathf.Abs(opposite - 8f) < 0.08f || Mathf.Abs(opposite - 6f) < 0.08f, opposite.ToString("0.000"));
        }

        [Test]
        public void PartialWalls_DoNotInventTheMissingSide()
        {
            List<WallObservation> walls = Rectangle();
            walls.RemoveAt(walls.Count - 1);

            bool built = Solve(out RoomReconstruction result, walls);

            Assert.IsFalse(built);
            StringAssert.Contains("not enough", result.Rejection.ToLowerInvariant());
        }

        [Test]
        public void FurnitureFace_IsNotRequiredToReplaceTheRearWall()
        {
            List<WallObservation> walls = Rectangle();
            walls.Add(Obs("closet", new Vector3(1f, 1f, 0.45f), Vector3.forward, new Vector3(1f, 1f, 0.45f), new Vector2(0.4f, 1.2f), 3f, 3f));

            bool built = Solve(out RoomReconstruction result, walls);

            Assert.IsTrue(built, result.Rejection);
            CollectionAssert.DoesNotContain(result.WallIds, "wall-closet");
        }

        [Test]
        public void InconsistentOppositeWalls_AreWithheld()
        {
            List<WallObservation> walls = Rectangle();
            walls.Add(Obs("north-b", new Vector3(2f, 1f, 3.4f), Vector3.back, new Vector3(2f, 1f, 3.4f), new Vector2(2.2f, 1.2f)));

            bool built = Solve(out RoomReconstruction result, walls);

            Assert.IsFalse(built);
            StringAssert.Contains("inconsistent", result.Rejection);
        }

        [Test]
        public void CornersOutsideObservedBoundaries_AreRejected()
        {
            bool built = Solve(
                out RoomReconstruction result,
                Obs("south", new Vector3(1f, 1f, 0f), Vector3.forward, new Vector3(1f, 1f, 0f), new Vector2(0.2f, 0.4f)),
                Obs("north", new Vector3(1f, 1f, 3f), Vector3.back, new Vector3(1f, 1f, 3f), new Vector2(0.2f, 0.4f)),
                Obs("west", new Vector3(0f, 1f, 1f), Vector3.right, new Vector3(0f, 1f, 1f), new Vector2(0.2f, 0.4f)),
                Obs("east", new Vector3(4f, 1f, 1f), Vector3.left, new Vector3(4f, 1f, 1f), new Vector2(0.2f, 0.4f)));

            Assert.IsFalse(built);
            Assert.IsNull(result.Corners);
        }

        [Test]
        public void NonOrthogonalRoom_IsNotForcedIntoARectangle()
        {
            Vector3 westNormal = new Vector3(1f, 0f, 0.35f).normalized;
            bool built = Solve(
                out RoomReconstruction result,
                Obs("south", new Vector3(2f, 1f, 0f), Vector3.forward, new Vector3(2f, 1f, 0f), new Vector2(3f, 1.5f)),
                Obs("north", new Vector3(1f, 1f, 3f), Vector3.back, new Vector3(1f, 1f, 3f), new Vector2(3f, 1.5f)),
                Obs("west", Vector3.zero, westNormal, new Vector3(-0.52f, 1f, 1.5f), new Vector2(2.5f, 1.5f)),
                Obs("east", new Vector3(4f, 1f, 0f), -westNormal, new Vector3(3.47f, 1f, 1.5f), new Vector2(2.5f, 1.5f)));

            Assert.IsTrue(built, result.Rejection);
            bool skewed = false;
            for (int i = 0; i < result.Corners.Length; i++)
            {
                if (result.Corners[i].z > 2.5f && result.Corners[i].x < -0.5f)
                {
                    skewed = true;
                }
            }

            Assert.IsTrue(skewed);
        }

        [Test]
        public void AmbiguousSelectedWall_DoesNotCreateARoom()
        {
            var samples = new List<WallObservation>
            {
                Obs("tiny", new Vector3(1f, 1.2f, 1f), Vector3.right, new Vector3(1f, 1.2f, 1f), new Vector2(0.2f, 0.15f), 0.1f, 0.2f)
            };
            samples.AddRange(Rectangle());
            WallClusterBuilder builder = new WallClusterBuilder();
            builder.Rebuild(samples, WallClusterSettings.CreateDefault(), 0f, true);
            WallCluster tiny = builder.FindByMember("tiny");
            Assert.IsTrue(tiny.LowConfidence);

            var required = new List<string> { tiny.Id };
            bool built = new RoomReconstructionSolver().TryBuild(
                builder.Clusters, 0f, required, WallClusterSettings.CreateDefault(), out RoomReconstruction result);

            Assert.IsFalse(built);
            StringAssert.Contains("ambiguous", result.Rejection);
        }

        [Test]
        public void FloorSupportedByWalls_BeatsALargerTable()
        {
            var samples = new List<FloorObservation>
            {
                new FloorObservation { Id = "table", Height = 0.75f, AreaSquareMeters = 3f, ObservedSeconds = 2f },
                new FloorObservation { Id = "floor", Height = 0.02f, AreaSquareMeters = 1.5f, ObservedSeconds = 2f }
            };
            var bottoms = new List<float> { 0.02f, 0.05f };

            bool chosen = new FloorHeightEstimator().TryChoose(samples, 1.45f, 0.12f, bottoms, out float floorY, out string reason);

            Assert.IsTrue(chosen, reason);
            Assert.AreEqual(0.02f, floorY, 0.05f);
        }

        [Test]
        public void TwoLargeFloors_WithoutWallSupport_StayUncertain()
        {
            var samples = new List<FloorObservation>
            {
                new FloorObservation { Id = "a", Height = 0f, AreaSquareMeters = 2f, ObservedSeconds = 2f },
                new FloorObservation { Id = "b", Height = 0.75f, AreaSquareMeters = 1.7f, ObservedSeconds = 2f }
            };

            bool chosen = new FloorHeightEstimator().TryChoose(samples, 1.5f, 0.12f, null, out _, out string reason);

            Assert.IsFalse(chosen);
            StringAssert.Contains("uncertain", reason);
        }

        [Test]
        public void LowestTinySurface_IsNotTheFloor()
        {
            var samples = new List<FloorObservation>
            {
                new FloorObservation { Id = "noise", Height = -0.7f, AreaSquareMeters = 0.3f, ObservedSeconds = 1f },
                new FloorObservation { Id = "floor", Height = 0.05f, AreaSquareMeters = 1.6f, ObservedSeconds = 2f }
            };

            bool chosen = new FloorHeightEstimator().TryChoose(samples, 1.5f, 0.12f, null, out float floorY, out string reason);

            Assert.IsTrue(chosen, reason);
            Assert.Greater(floorY, -0.2f);
        }

        static bool Solve(out RoomReconstruction result, params WallObservation[] walls)
        {
            return Solve(out result, new List<WallObservation>(walls));
        }

        static bool Solve(out RoomReconstruction result, List<WallObservation> walls)
        {
            var builder = new WallClusterBuilder();
            builder.Rebuild(walls, WallClusterSettings.CreateDefault(), 0f, true);
            return new RoomReconstructionSolver().TryBuild(
                builder.Clusters,
                0f,
                null,
                WallClusterSettings.CreateDefault(),
                out result);
        }

        static WallClusterBuilder Build(params WallObservation[] walls)
        {
            var builder = new WallClusterBuilder();
            builder.Rebuild(walls, WallClusterSettings.CreateDefault(), 0f, false);
            return builder;
        }

        static List<WallObservation> Rectangle()
        {
            return new List<WallObservation>
            {
                Obs("south", new Vector3(2f, 1f, 0f), Vector3.forward, new Vector3(2f, 1f, 0f), new Vector2(2.2f, 1.2f)),
                Obs("north", new Vector3(2f, 1f, 3f), Vector3.back, new Vector3(2f, 1f, 3f), new Vector2(2.2f, 1.2f)),
                Obs("west", new Vector3(0f, 1f, 1.5f), Vector3.right, new Vector3(0f, 1f, 1.5f), new Vector2(2.2f, 1.2f)),
                Obs("east", new Vector3(4f, 1f, 1.5f), Vector3.left, new Vector3(4f, 1f, 1.5f), new Vector2(2.2f, 1.2f))
            };
        }

        static WallObservation Obs(
            string id,
            Vector3 point,
            Vector3 normal,
            Vector3 center,
            Vector2 half,
            float seconds = 2f,
            float area = 4f)
        {
            Vector3 n = normal.normalized;
            Vector3 axisU = Vector3.Cross(Vector3.up, n);
            if (axisU.sqrMagnitude < 0.001f)
            {
                axisU = Vector3.right;
            }
            else
            {
                axisU.Normalize();
            }

            return new WallObservation
            {
                Wall = new DetectedWall
                {
                    Id = id,
                    Point = point,
                    Normal = n,
                    Center = center,
                    AxisU = axisU,
                    AxisV = Vector3.Cross(n, axisU).normalized,
                    HalfExtents = half
                },
                AreaSquareMeters = area,
                ObservedSeconds = seconds,
                Tracking = WallTrackingQuality.Tracking
            };
        }
    }
}
