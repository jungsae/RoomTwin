using System.Collections;
using NUnit.Framework;
using RoomTwin.Geometry;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

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
                Assert.IsNotNull(hud.UndoButtonComponent);
                Assert.IsNotNull(hud.ConfirmButtonComponent);
                Assert.IsFalse(hud.ConfirmButtonComponent.interactable);

                hud.Apply(ReadySnapshot(0), null, "debug");
                Assert.AreEqual("방향이 다른 벽이 있습니다", hud.Headline);

                int resets = 0;
                int undos = 0;
                int confirms = 0;
                hud.BindReset(() => resets++);
                hud.BindUndo(() => undos++);
                hud.BindConfirm(() => confirms++);
                hud.ResetButtonComponent.onClick.Invoke();
                hud.UndoButtonComponent.onClick.Invoke();
                hud.SetConfirmInteractable(true);
                hud.ConfirmButtonComponent.onClick.Invoke();
                hud.SetConfirmInteractable(false);
                Assert.AreEqual(1, resets);
                Assert.AreEqual(1, undos);
                Assert.AreEqual(1, confirms);
                Assert.IsFalse(hud.ConfirmButtonComponent.interactable);

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

        [UnityTest]
        public IEnumerator TrackingLoss_ClearsAnExistingPreview()
        {
            var root = new GameObject("preview-clear");
            try
            {
                var controller = root.AddComponent<ArFieldTestController>();
                yield return null;
                controller.RememberPreviewForTests(new[]
                {
                    Vector3.zero,
                    new Vector3(4f, 0f, 0f),
                    new Vector3(4f, 0f, 3f),
                    new Vector3(0f, 0f, 3f)
                });
                Assert.IsTrue(controller.HasValidPreview);

                controller.TickScanForTests();

                Assert.IsFalse(controller.HasValidPreview);
                Assert.IsFalse(controller.MeshBuilt);
            }
            finally
            {
                Object.Destroy(root);
            }
        }

        [UnityTest]
        public IEnumerator Rescan_KeepsPreviousPlaneIdsOut()
        {
            var root = new GameObject("rescan-gate");
            try
            {
                var controller = root.AddComponent<ArFieldTestController>();
                yield return null;
                controller.BeginRescanForTests(new[] { "old-plane" }, 0f);
                Assert.IsTrue(controller.IsRescanWaiting);
                Assert.IsFalse(controller.AllowsPlaneForTests("old-plane"));

                controller.ObserveRescanForTests(new[] { "old-plane" }, true, 1f);
                Assert.IsTrue(controller.IsRescanWaiting);

                controller.ObserveRescanForTests(new[] { "new-plane" }, true, 2f);
                Assert.IsFalse(controller.IsRescanWaiting);
                Assert.IsFalse(controller.AllowsPlaneForTests("old-plane"));
                Assert.IsTrue(controller.AllowsPlaneForTests("new-plane"));
            }
            finally
            {
                Object.Destroy(root);
            }
        }

        [UnityTest]
        public IEnumerator LogDeleteAndWriteFailure_DoNotStopTheController()
        {
            var root = new GameObject("log-safety");
            try
            {
                var controller = root.AddComponent<ArFieldTestController>();
                yield return null;
                Assert.DoesNotThrow(() => controller.DeleteLogsForTests());
                Assert.IsFalse(controller.LogIsRecording);
                Assert.DoesNotThrow(() => controller.ProbeLogForTests());
                Assert.IsTrue(controller.ControllerInitSucceeded);
                Assert.IsTrue(controller.enabled);
            }
            finally
            {
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
