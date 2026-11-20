using NUnit.Framework;

namespace RoomTwin.Geometry.Tests
{
    public sealed class ScanFeedbackTests
    {
        [Test]
        public void TrackingNotReady_StaysOnTracking()
        {
            ScanPhase phase = ScanFeedback.Resolve(new ScanSnapshot());

            Assert.AreEqual(ScanPhase.Tracking, phase);
            Assert.AreEqual("추적 준비 중", ScanFeedback.Headline(phase));
        }

        [Test]
        public void OneWall_AsksForAnAdjacentWall()
        {
            var snapshot = new ScanSnapshot
            {
                IsTracking = true,
                FloorLocked = true,
                VerticalWallCount = 1
            };

            Assert.AreEqual(ScanPhase.NeedAdjacentWall, ScanFeedback.Resolve(snapshot));
            Assert.AreEqual("인접한 벽을 더 스캔하세요", ScanFeedback.Headline(ScanPhase.NeedAdjacentWall));
        }

        [Test]
        public void TwoParallelWalls_DoNotLookReady()
        {
            var snapshot = new ScanSnapshot
            {
                IsTracking = true,
                FloorLocked = true,
                VerticalWallCount = 2,
                HasIntersectablePair = false
            };

            Assert.AreEqual(ScanPhase.NeedAdjacentWall, ScanFeedback.Resolve(snapshot));
        }

        [Test]
        public void IntersectableWalls_AreReady_AndProgressCountsCorners()
        {
            var snapshot = new ScanSnapshot
            {
                IsTracking = true,
                FloorLocked = true,
                VerticalWallCount = 2,
                HasIntersectablePair = true,
                CornerCount = 2
            };

            Assert.AreEqual(ScanPhase.ReadyToEstimate, ScanFeedback.Resolve(snapshot));
            Assert.AreEqual("2/4", ScanFeedback.ProgressLabel(snapshot.CornerCount));
            string ready = ScanFeedback.Guidance(ScanPhase.ReadyToEstimate, 0, null);
            StringAssert.Contains("생기지 않습니다", ready);
            StringAssert.Contains("2/4", ScanFeedback.Guidance(ScanPhase.ReadyToEstimate, 2, null));
        }

        [Test]
        public void FailureText_IsShownUntilCleared()
        {
            string guidance = ScanFeedback.Guidance(
                ScanPhase.ReadyToEstimate,
                1,
                ScanFeedback.ToKorean("The walls are parallel or nearly parallel."));

            StringAssert.Contains("평행", guidance);
        }

        [Test]
        public void ResetSnapshot_ClearsProgressAndFailure()
        {
            var reset = new ScanSnapshot { IsTracking = true, FloorLocked = false, CornerCount = 0 };

            Assert.AreEqual(ScanPhase.NeedFloor, ScanFeedback.Resolve(reset));
            Assert.AreEqual("0/4", ScanFeedback.ProgressLabel(reset.CornerCount));
            Assert.IsFalse(ScanFeedback.Guidance(ScanPhase.NeedFloor, 0, null).Contains("평행"));
        }

        [Test]
        public void LargeHigherSurface_CanWinWithoutChoosingTheLowest()
        {
            var samples = new[]
            {
                new WallCornerSolver.FloorSample { Height = 0f, AreaSquareMeters = 0.4f },
                new WallCornerSolver.FloorSample { Height = 0.8f, AreaSquareMeters = 3f }
            };

            bool chosen = WallCornerSolver.TryChooseFloorY(samples, 0.12f, out float floorY, out string error);

            Assert.IsTrue(chosen, error);
            Assert.AreEqual(0.8f, floorY, 0.0001f);
        }

        [Test]
        public void TwoLargeSurfaces_StayUncertain()
        {
            var samples = new[]
            {
                new WallCornerSolver.FloorSample { Height = 0f, AreaSquareMeters = 2f },
                new WallCornerSolver.FloorSample { Height = 0.75f, AreaSquareMeters = 1.5f }
            };

            bool chosen = WallCornerSolver.TryChooseFloorY(samples, 0.12f, out _, out string error);

            Assert.IsFalse(chosen);
            StringAssert.Contains("uncertain", error);
        }
    }
}
