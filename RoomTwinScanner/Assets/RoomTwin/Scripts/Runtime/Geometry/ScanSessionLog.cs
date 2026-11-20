using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace RoomTwin.Geometry
{
    /// <summary>
    /// Append-only JSONL scan log. Recording failures must not stop AR.
    /// </summary>
    public sealed class ScanSessionLog
    {
        public const int MaxFiles = 12;
        public const long MaxFileBytes = 2L * 1024L * 1024L;

        readonly StreamWriter writer;
        bool recording;
        long byteLimit = MaxFileBytes;

        public string SessionId { get; }
        public string FilePath { get; }
        public bool IsRecording => recording;

        /// <summary>A tapped plane was accepted as a wall selection.</summary>
        public int WallIntentCount { get; private set; }

        /// <summary>A closed room outline was estimated and shown.</summary>
        public int OutlineCount { get; private set; }

        /// <summary>An input was rejected. This is not an outline failure by itself.</summary>
        public int RejectedCount { get; private set; }

        ScanSessionLog(string sessionId, string filePath, StreamWriter writer)
        {
            SessionId = sessionId;
            FilePath = filePath;
            this.writer = writer;
            recording = writer != null;
        }

        public static ScanSessionLog Start(string directory, string version, string gitSha, string deviceModel)
        {
            string sessionId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)
                + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string name = "scan_" + sessionId + ".jsonl";
            try
            {
                Directory.CreateDirectory(directory);
                Trim(directory, MaxFiles - 1);
                string path = Path.Combine(directory, name);
                var writer = new StreamWriter(path, false, new UTF8Encoding(false)) { AutoFlush = true };
                var log = new ScanSessionLog(sessionId, path, writer);
                log.Write("session-start",
                    "\"version\":" + Quote(version) +
                    ",\"git\":" + Quote(gitSha) +
                    ",\"device\":" + Quote(deviceModel) +
                    ",\"startedUtc\":" + Quote(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)) +
                    ",\"metrics\":\"wallIntent=wall selection,outline=closed preview,rejected=input rejected\"");
                return log;
            }
            catch (Exception)
            {
                return new ScanSessionLog(sessionId, Path.Combine(directory ?? string.Empty, name), null);
            }
        }

        public void AcceptWallIntent()
        {
            WallIntentCount++;
        }

        public void AcceptOutline()
        {
            OutlineCount++;
        }

        public void Reject()
        {
            RejectedCount++;
        }

        public void SetByteLimit(long bytes)
        {
            byteLimit = bytes > 0L ? bytes : MaxFileBytes;
        }

        public void Write(string type, string fields)
        {
            if (!recording || writer == null)
            {
                return;
            }

            try
            {
                if (writer.BaseStream != null && writer.BaseStream.Length >= byteLimit)
                {
                    StopRecording();
                    return;
                }

                writer.Write("{\"type\":");
                writer.Write(Quote(type));
                writer.Write(",\"session\":");
                writer.Write(Quote(SessionId));
                writer.Write(",\"t\":");
                writer.Write(DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));
                if (!string.IsNullOrEmpty(fields))
                {
                    writer.Write(',');
                    writer.Write(fields);
                }

                writer.Write("}\n");
            }
            catch (Exception)
            {
                StopRecording();
            }
        }

        public void Flush()
        {
            if (!recording || writer == null)
            {
                return;
            }

            try
            {
                writer.Flush();
            }
            catch (Exception)
            {
                StopRecording();
            }
        }

        public void StopRecording()
        {
            recording = false;
            DisposeWriter();
        }

        public void DisposeWriter()
        {
            try
            {
                writer?.Dispose();
            }
            catch (Exception)
            {
            }
        }

        public static string Quote(string value)
        {
            if (value == null)
            {
                return "null";
            }

            var builder = new StringBuilder(value.Length + 2);
            builder.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '\\' || c == '"')
                {
                    builder.Append('\\');
                }

                if (c == '\n' || c == '\r')
                {
                    builder.Append(' ');
                    continue;
                }

                builder.Append(c);
            }

            builder.Append('"');
            return builder.ToString();
        }

        public static void Trim(string directory, int maxFiles)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return;
            }

            string[] files = Directory.GetFiles(directory, "scan_*.jsonl");
            Array.Sort(files, StringComparer.Ordinal);
            int extra = files.Length - Math.Max(0, maxFiles);
            for (int i = 0; i < extra; i++)
            {
                TryDelete(files[i]);
            }
        }

        public static void DeleteAll(string directory)
        {
            try
            {
                if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                {
                    return;
                }

                string[] files = Directory.GetFiles(directory, "scan_*.jsonl");
                for (int i = 0; i < files.Length; i++)
                {
                    TryDelete(files[i]);
                }

                TryDelete(Path.Combine(directory, "latest-scan.jsonl"));
            }
            catch (Exception)
            {
            }
        }

        public static string ExportLatest(string directory)
        {
            try
            {
                if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                {
                    return null;
                }

                string[] files = Directory.GetFiles(directory, "scan_*.jsonl");
                if (files.Length == 0)
                {
                    return null;
                }

                Array.Sort(files, StringComparer.Ordinal);
                string latest = files[files.Length - 1];
                string destination = Path.Combine(directory, "latest-scan.jsonl");
                File.Copy(latest, destination, true);
                return destination;
            }
            catch (Exception)
            {
                return null;
            }
        }

        static void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
