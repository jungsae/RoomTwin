using System.IO;
using NUnit.Framework;

namespace RoomTwin.Geometry.Tests
{
    public sealed class ScanSessionLogTests
    {
        string directory;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "roomtwin-scan-log-" + Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public void Start_WritesANewSessionFile()
        {
            ScanSessionLog log = ScanSessionLog.Start(directory, "0.1.0", "c9a5169", "test-device");
            log.Accept();
            log.Reject();
            log.Write("corner", "\"index\":1");
            log.DisposeWriter();

            Assert.IsTrue(File.Exists(log.FilePath));
            string body = File.ReadAllText(log.FilePath);
            StringAssert.Contains("session-start", body);
            StringAssert.Contains("\"type\":\"corner\"", body);
            Assert.AreEqual(1, log.AcceptedCount);
            Assert.AreEqual(1, log.RejectedCount);
        }

        [Test]
        public void FailedDirectory_DoesNotThrow()
        {
            string blockingFile = Path.Combine(directory, "not-a-directory");
            File.WriteAllText(blockingFile, "x");

            ScanSessionLog log = ScanSessionLog.Start(blockingFile, "0.1.0", "c9a5169", "test-device");

            Assert.IsFalse(log.IsRecording);
            Assert.DoesNotThrow(() => log.Write("snapshot", "\"cameraPos\":[0,0,0]"));
        }

        [Test]
        public void DeleteAll_RemovesSessionFiles()
        {
            ScanSessionLog first = ScanSessionLog.Start(directory, "0.1.0", "abc", "phone");
            first.DisposeWriter();
            ScanSessionLog.DeleteAll(directory);

            Assert.AreEqual(0, Directory.GetFiles(directory, "scan_*.jsonl").Length);
        }

        [Test]
        public void UndoLast_RemovesOnlyTheLatestCorner()
        {
            var capture = new FloorCornerCapture();
            Assert.IsTrue(capture.TryAdd(new UnityEngine.Vector3(0f, 0f, 0f), out _));
            Assert.IsTrue(capture.TryAdd(new UnityEngine.Vector3(2f, 0.05f, 0f), out _));

            Assert.IsTrue(capture.UndoLast());
            Assert.AreEqual(1, capture.Count);
            Assert.IsTrue(capture.HasReferenceFloorY);
            capture.Reset();
            Assert.IsFalse(capture.HasReferenceFloorY);
        }
    }
}
