using System.Collections;
using NUnit.Framework;
using RoomTwin.Geometry;
using UnityEngine;
using UnityEngine.TestTools;

namespace RoomTwin.AR.PlayModeTests
{
    public sealed class ArScanHudPlayModeTests
    {
        [UnityTest]
        public IEnumerator Initialize_CreatesStatusResetAndProgress()
        {
            ArScanHud.LoadFontOverride = null;
            var root = new GameObject("hud-smoke");
            var hud = root.AddComponent<ArScanHud>();
            try
            {
                Assert.IsTrue(hud.TryInitialize(out string error), error);
                Assert.IsNotNull(hud.ResetButtonComponent);
                Assert.IsFalse(string.IsNullOrEmpty(hud.Headline));

                int resets = 0;
                hud.BindReset(() => resets++);
                hud.ResetButtonComponent.onClick.Invoke();
                Assert.AreEqual(1, resets);

                hud.SetDeveloperVisible(true);
                Assert.IsTrue(hud.IsDeveloperPanelActive);
                hud.SetDeveloperVisible(false);
                Assert.IsFalse(hud.IsDeveloperPanelActive);

                hud.Apply(ReadySnapshot(0), null, "debug");
                Assert.AreEqual("0/4", hud.Progress);
                hud.Apply(ReadySnapshot(3), null, "debug");
                Assert.AreEqual("3/4", hud.Progress);
                yield return null;
            }
            finally
            {
                Object.Destroy(root);
            }
        }

        [UnityTest]
        public IEnumerator HudFailure_DoesNotStopControllerInitialization()
        {
            ArScanHud.LoadFontOverride = () => null;
            LogAssert.Expect(LogType.Error, "[RoomTwin][FieldTest] font-load=failure path=Fonts/NotoSansKR-Regular");
            LogAssert.Expect(LogType.Error, "[RoomTwin][FieldTest] hud-init=failure Korean font was not included in the player.");
            var root = new GameObject("controller-smoke");
            try
            {
                var controller = root.AddComponent<ArFieldTestController>();
                yield return null;
                Assert.IsTrue(controller.ControllerInitSucceeded);
                Assert.IsFalse(controller.HudReady);
                Assert.IsTrue(controller.UsingEmergencyHud);
            }
            finally
            {
                ArScanHud.LoadFontOverride = null;
                Object.Destroy(root);
            }
        }

        static ScanSnapshot ReadySnapshot(int corners)
        {
            return new ScanSnapshot
            {
                IsTracking = true,
                FloorLocked = true,
                VerticalWallCount = 2,
                HasIntersectablePair = true,
                CornerCount = corners
            };
        }
    }
}
