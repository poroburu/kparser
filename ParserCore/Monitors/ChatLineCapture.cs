using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace WaywardGamers.KParser.Monitoring
{
    /// <summary>
    /// Captures the raw ChatLine stream raised by the active reader without
    /// creating a database or invoking the parser.
    /// </summary>
    public sealed class ChatLineCapture : IDisposable
    {
        private readonly object sync = new object();
        private readonly StreamWriter writer;
        private readonly List<string> statusMessages = new List<string>();
        private bool disposed;
        private bool readerStarted;
        private int lineCount;

        private ChatLineCapture(string path)
        {
            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath);
            if (!String.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            writer = new StreamWriter(fullPath, false, new UTF8Encoding(false));
            writer.AutoFlush = true;
            writer.WriteLine("# kparser chatline capture schema_version=1");
            writer.WriteLine(
                "# started_utc=" +
                DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        }

        public int LineCount
        {
            get
            {
                lock (sync)
                {
                    return lineCount;
                }
            }
        }

        public string[] StatusMessages
        {
            get
            {
                lock (sync)
                {
                    return statusMessages.ToArray();
                }
            }
        }

        public static ChatLineCapture Start(string path)
        {
            if (String.IsNullOrEmpty(path))
                throw new ArgumentException("A capture path is required.", "path");

            ChatLineCapture capture = new ChatLineCapture(path);
            Monitor.Instance.ReaderDataChanged += capture.ReaderDataChanged;
            Monitor.Instance.ReaderStatusChanged += capture.ReaderStatusChanged;

            try
            {
                Monitor.Instance.StartRawCapture();
                capture.readerStarted = true;
                return capture;
            }
            catch
            {
                Monitor.Instance.ReaderDataChanged -= capture.ReaderDataChanged;
                Monitor.Instance.ReaderStatusChanged -= capture.ReaderStatusChanged;
                capture.DisposeWriter();
                throw;
            }
        }

        private void ReaderDataChanged(object sender, ReaderDataEventArgs e)
        {
            if (e == null || e.ChatLines == null)
                return;

            lock (sync)
            {
                if (disposed)
                    return;

                foreach (ChatLine line in e.ChatLines)
                {
                    if (line == null)
                        continue;

                    // ChatLine.ChatText is the original byte-preserving
                    // character stream. FFXI ChatLines do not contain line
                    // breaks, so UTF-8 preserves the stream for snapshot.
                    writer.WriteLine(line.ChatText);
                    lineCount++;
                }
            }
        }

        private void ReaderStatusChanged(object sender, ReaderStatusEventArgs e)
        {
            if (e == null || String.IsNullOrEmpty(e.StatusMessage))
                return;

            lock (sync)
            {
                if (disposed)
                    return;

                if (statusMessages.Count == 0 ||
                    !String.Equals(statusMessages[statusMessages.Count - 1],
                        e.StatusMessage,
                        StringComparison.Ordinal))
                {
                    statusMessages.Add(e.StatusMessage);
                }

                if (statusMessages.Count > 32)
                    statusMessages.RemoveAt(0);
            }
        }

        public void Stop()
        {
            lock (sync)
            {
                if (disposed)
                    return;
            }

            Monitor.Instance.ReaderDataChanged -= ReaderDataChanged;

            try
            {
                if (readerStarted)
                {
                    Monitor.Instance.StopRawCapture();
                    readerStarted = false;
                }
            }
            finally
            {
                lock (sync)
                {
                    disposed = true;
                }

                Monitor.Instance.ReaderStatusChanged -= ReaderStatusChanged;
                DisposeWriter();
            }
        }

        private void DisposeWriter()
        {
            lock (sync)
            {
                writer.Flush();
                writer.Dispose();
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
