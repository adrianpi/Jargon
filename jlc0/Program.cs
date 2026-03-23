using Jargon;
using System;
using System.Collections.Generic;
using System.IO;

namespace llc0
{
    internal class Program
    {
        static int Main(string[] args)
        {
            Console.WriteLine("Jargon Compiler 0.0");

            var c = new Compiler();
            var co = new CompilerOptions();

            co.DebugInfo = false;
            co.AdditionalFlags = "";
            bool op = false;

            List<string> files = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-g")
                {
                    co.DebugInfo = true;
                }
                else if (args[i] == "-k")
                {
                    co.KeepIntermediateFiles = true;
                }
                else if (args[i] == "-h" || args[i] == "--help")
                {
                    Console.WriteLine("Usage: jlc0 [options] <input files>");
                    Console.WriteLine("Options:");
                    Console.WriteLine("  -g                Generate debug information");
                    Console.WriteLine("  -k                Keep intermediate files");
                    Console.WriteLine("  -o <output file>  Specify output file name");
                    Console.WriteLine("  -O<level>         Set optimization level  (O0, O1, O2, Os, Ofast, Od, Ot, Ox)");
                    Console.WriteLine("  -I<dir>           Add directory to library search path");
                    Console.WriteLine("  -l<library>       Link with specified library");
                    return 0;
                }
                else if (args[i] == "-o")
                {
                    op = true;
                    i++;
                    if (i < args.Length)
                        co.OutputFileName = args[i];
                }
                else if (args[i].StartsWith("-O"))
                {
                    co.OptimizationLevel = args[i].Substring(1);
                }
                else if (args[i].StartsWith("-I"))
                {
                    co.LibraryDirectories.Add(args[i].Substring(2));
                }
                else if (args[i].StartsWith("-l"))
                {
                    co.AdditionalLibraries.Add(args[i].Substring(2));
                }
                else if (!op)
                {
                    if (args[i].EndsWith("."))
                    {
                        var dir = Path.GetFullPath(args[i]);
                        var fileList = Directory.GetFiles(dir, "*.cm");
                        files.AddRange(fileList);
                    }
                    else
                    {
                        files.Add(args[i]);
                    }
                }
                else
                {
                    co.AdditionalFlags += args[i] + " ";
                }
            }

            if (files.Count == 0)
            {
                Console.WriteLine("No input files.");
                return -1;
            }
            else if (co.OutputFileName == null)
            {
                Console.WriteLine("No output file (-o) specified.");
                return -2;
            }

            co.OutputDirectory = Path.GetDirectoryName(co.OutputFileName);
            co.OutputFileName = Path.GetFileName(co.OutputFileName);

            var output = c.Compile(files.ToArray(), co);
            foreach (var err in output.Errors)
            {
                Console.WriteLine(err.ToString() + "\r\n");
            }

            return output.ErrorCount;
        }
    }
}
