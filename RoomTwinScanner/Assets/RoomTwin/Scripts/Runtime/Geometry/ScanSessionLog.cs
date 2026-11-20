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

        readonly StreamWriter writer;
        bool recording;

        public string SessionId { get; }
        public string FilePath { get; }
        public bool IsRecording => recording;
        public int AcceptedCount { get; private set; }
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
                    ",\"startedUtc\":" + Quote(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)));
                return log;
            }
            catch (Exception)
            {
                return new ScanSessionLog(sessionId, Path.Combine(directory ?? string.Empty, name), null);
            }
        }

        public void Accept()
        {
            AcceptedCount++;
        }

        public void Reject()
        {
            RejectedCount++;
        }

        public void Write(string type, string fields)
        {
            if (writer == null)
            {
                return;
            }

            try
            {
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
                recording = false;
            }
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
                File.Delete(files[i]);
            }
        }

        public static void DeleteAll(string directory)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return;
            }

            string[] files = Directory.GetFiles(directory, "scan_*.jsonl");
            for (int i = 0; i < files.Length; i++)
            {
                File.Delete(files[i]);
            }
        }

        public static string ExportLatest(string directory)
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
    }
}
