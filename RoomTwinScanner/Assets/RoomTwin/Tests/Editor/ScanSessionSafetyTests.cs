using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace RoomTwin.Geometry.Tests
{
    public sealed class ScanSessionSafetyTests
    {
        [Test]
        public void RetiredPlanes_StayOutUntilTheyDisappear()
        {
            var gate = new RescanGate();
            gate.Begin(new[] { "old-a", "old-b" }, 0f);

            gate.Observe(new[] { "old-a", "new-c" }, true, 1f);

            Assert.IsTrue(gate.Waiting);
            Assert.IsTrue(gate.IsRetired("old-a"));
            Assert.IsFalse(gate.IsRetired("new-c"));

            gate.Observe(new[] { "new-c" }, true, 2f);

            Assert.IsFalse(gate.Waiting);
            Assert.IsTrue(gate.PlanesCleared);
            Assert.IsTrue(gate.IsRetired("old-b"));
            Assert.IsFalse(gate.IsRetired("new-c"));
        }

        [Test]
        public void Timeout_EndsTheWait_AndStillBlocksRetiredIds()
        {
            var gate = new RescanGate();
            gate.Begin(new[] { "old-a" }, 0f);

            gate.Observe(new[] { "old-a" }, false, RescanGate.DefaultTimeoutSeconds + 0.1f);

            Assert.IsTrue(gate.TimedOut);
            Assert.IsFalse(gate.Waiting);
            Assert.IsTrue(gate.IsRetired("old-a"));
        }

        [Test]
        public void SubsumedSelection_MovesToTheParent_AndAMissingWallIsLost()
        {
            var intended = new List<string> { "child", "gone" };
            var live = new List<string> { "parent" };
            var subsumed = new Dictionary<string, string> { { "child", "parent" } };

            WallSelectionTransfer.Resolve(intended, live, subsumed, out bool transferred, out bool lost);

            Assert.IsTrue(transferred);
            Assert.IsTrue(lost);
            CollectionAssert.Contains(intended, "parent");
            CollectionAssert.DoesNotContain(intended, "gone");
            CollectionAssert.DoesNotContain(intended, "child");
        }

        [Test]
        public void FloorFailure_ClearsAPreview_AndALargeMoveBlocksConfirm()
        {
            Assert.IsTrue(PreviewConfirmation.ShouldClear(false, true, false, true, true));
            Assert.IsFalse(PreviewConfirmation.CanConfirm(true, false, false, false));
            Assert.IsFalse(PreviewConfirmation.CanConfirm(true, true, true, false));
            Assert.IsFalse(PreviewConfirmation.CanConfirm(true, true, false, true));

            Vector3[] shown =
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(4f, 0f, 0f),
                new Vector3(4f, 0f, 3f),
                new Vector3(0f, 0f, 3f)
            };
            Vector3[] shifted =
            {
                new Vector3(0.4f, 0f, 0f),
                new Vector3(4.4f, 0f, 0f),
                new Vector3(4.4f, 0f, 3f),
                new Vector3(0.4f, 0f, 3f)
            };

            Assert.IsTrue(PreviewConfirmation.CornersMoved(shown, shifted, PreviewConfirmation.CornerChangeMeters));
            Assert.IsFalse(PreviewConfirmation.CornersMoved(shown, new[] { shown[2], shown[0], shown[3], shown[1] }, PreviewConfirmation.CornerChangeMeters));
        }
    }
}
