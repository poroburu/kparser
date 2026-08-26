using System;
using System.IO;
using System.Text;
using WaywardGamers.KParser;

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

        static void PrintUsage()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  kparser.cli snapshot <chatlines.txt> [--json] [--parity-chat] [-o|--output out.json]");
        }
    }
}
