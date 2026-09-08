using System;
using System.IO;
using System.Text;
using System.Threading;
using WaywardGamers.KParser;
using WaywardGamers.KParser.Monitoring;

namespace WaywardGamers.KParser.Cli
{
    internal static class Program
    {
        static int Main(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                PrintUsage();
                return 0;
            }

            if (string.Equals(args[0], "capture", StringComparison.OrdinalIgnoreCase))
                return Capture(args);

            if (args.Length < 2 ||
                !string.Equals(args[0], "snapshot", StringComparison.OrdinalIgnoreCase))
            {
                PrintUsage();
                return 1;
            }

            string path = args[1];
            bool asJson = false;
            bool parityChat = false;
            string output = null;

            for (int i = 2; i < args.Length; i++)
            {
                string arg = args[i];
                if (string.Equals(arg, "--json", StringComparison.OrdinalIgnoreCase))
                {
                    asJson = true;
                }
                else if (string.Equals(arg, "--parity-chat", StringComparison.OrdinalIgnoreCase))
                {
                    parityChat = true;
                }
                else if (string.Equals(arg, "-o", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(arg, "--output", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("-o/--output requires a path");
                        return 1;
                    }
                    i++;
                    output = args[i];
                }
                else
                {
                    Console.Error.WriteLine("Unknown argument: {0}", arg);
                    PrintUsage();
                    return 1;
                }
            }

            if (!File.Exists(path))
            {
                Console.Error.WriteLine("File not found: {0}", path);
                return 1;
            }

            ParseSnapshotResult result = ParseSnapshot.FromChatLineFile(path);
            string json = parityChat
                ? ParseSnapshot.ToParityChatJson(result)
                : ParseSnapshot.ToJson(result);

            if (output != null)
                File.WriteAllText(output, json, new UTF8Encoding(false));

            if (parityChat || asJson)
                Console.WriteLine(json);
            else
                Console.Write(ParseSnapshot.FormatSummary(result));

            return result.Errors.Count > 0 ? 2 : 0;
        }

        static int Capture(string[] args)
        {
            if (args.Length < 2)
            {
                PrintUsage();
                return 1;
            }

            string path = args[1];
            int durationMs = 600000;
            int checkpointMs = 120000;

            for (int i = 2; i < args.Length; i++)
            {
                string arg = args[i];
                if (string.Equals(arg, "--duration-ms", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryReadInt(args, ref i, out durationMs) || durationMs <= 0)
                    {
                        Console.Error.WriteLine("--duration-ms must be a positive integer");
                        return 1;
                    }
                }
                else if (string.Equals(arg, "--checkpoint-ms", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryReadInt(args, ref i, out checkpointMs) || checkpointMs < 0)
                    {
                        Console.Error.WriteLine("--checkpoint-ms must be a non-negative integer");
                        return 1;
                    }
                }
                else
                {
                    Console.Error.WriteLine("Unknown argument: {0}", arg);
                    PrintUsage();
                    return 1;
                }
            }

            ManualResetEvent stop = new ManualResetEvent(false);
            ConsoleCancelEventHandler cancel = delegate(object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                stop.Set();
            };

            try
            {
                ChatLineCapture capture = ChatLineCapture.Start(path);
                try
                {
                    Console.CancelKeyPress += cancel;
                    Console.WriteLine(
                        "Capturing kparser ChatLines to {0} for {1}ms",
                        path,
                        durationMs);

                    DateTime deadline = DateTime.UtcNow.AddMilliseconds(durationMs);
                    DateTime nextCheckpoint = DateTime.UtcNow.AddMilliseconds(checkpointMs);
                    while (!stop.WaitOne(100) && DateTime.UtcNow < deadline)
                    {
                        if (checkpointMs > 0 && DateTime.UtcNow >= nextCheckpoint)
                        {
                            Console.WriteLine(
                                "capture checkpoint: chatlines={0}",
                                capture.LineCount);
                            nextCheckpoint = DateTime.UtcNow.AddMilliseconds(checkpointMs);
                        }
                    }

                    Console.WriteLine(
                        "Captured {0} ChatLines to {1}",
                        capture.LineCount,
                        path);
                }
                finally
                {
                    capture.Dispose();
                }

                foreach (string status in capture.StatusMessages)
                {
                    Console.WriteLine("capture status: {0}", status);
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Capture failed: {0}", ex.Message);
                return 2;
            }
            finally
            {
                Console.CancelKeyPress -= cancel;
                stop.Close();
            }
        }

        static bool TryReadInt(string[] args, ref int index, out int value)
        {
            value = 0;
            if (index + 1 >= args.Length)
                return false;

            index++;
            return Int32.TryParse(args[index], out value);
        }

        static void PrintUsage()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  kparser.cli snapshot <chatlines.txt> [--json] [--parity-chat] [-o|--output out.json]");
            Console.WriteLine("  kparser.cli capture <chatlines.txt> [--duration-ms ms] [--checkpoint-ms ms]");
        }
    }
}
