using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

using Mono.Cecil;

namespace Zastai.Build.ApiReference.Tool;

public static class Program {

  private static ReaderParameters CreateReaderParameters(string assembly, IEnumerable<string> dependencyPath) {
    // Set up a resolver that looks in
    // 1. The directory containing the assembly being processed
    // 2. Any directories specified on the task
    // 3. The directory containing this assembly
    // Note: a new resolver each time, to avoid caching a referenced assembly that applied to a previous assembly but not the
    //       current one.
    var resolver = new DefaultAssemblyResolver();
    foreach (var dir in resolver.GetSearchDirectories()) {
      resolver.RemoveSearchDirectory(dir);
    }
    resolver.AddSearchDirectory(Path.GetDirectoryName(assembly));
    foreach (var dir in dependencyPath) {
      if (Directory.Exists(dir)) {
        resolver.AddSearchDirectory(dir);
      }
      else {
        Console.Error.WriteLine($"Ignoring non-existent dependency folder: {dir}");
      }
    }
    return new ReaderParameters {
      AssemblyResolver = resolver,
      ReadingMode = ReadingMode.Immediate,
      ReadSymbols = false,
      ReadWrite = false,
    };
  }

  public static int Main(string[] args) {
    if (args.Length < 2 || args.Length % 2 != 0) {
      return Program.Usage();
    }
    var assembly = args[0];
    if (!File.Exists(assembly)) {
      Console.Error.WriteLine($"Assembly not found: {assembly}");
      return 2;
    }
    var referenceSource = args[1];
    var outputDir = Path.GetDirectoryName(Path.GetFullPath(referenceSource));
    if (!Directory.Exists(outputDir)) {
      Console.Error.WriteLine($"Output folder does not exist: {outputDir}");
      return 3;
    }
    CodeFormatter? formatter = null;
    var handleBinaryEnums = false;
    var handleCharEnums = false;
    var handleHexEnums = false;
    var includeInternals = false;
    var dependencyPath = new List<string>();
    var includedAttributes = new List<string>();
    var excludedAttributes = new List<string>();
    for (var i = 2; i < args.Length; i += 2) {
      switch (args[i]) {
        case "-ea":
          excludedAttributes.Add(args[i + 1]);
          break;
        case "-eh":
          foreach (var handling in args[i + 1].Split(',', ';')) {
            switch (handling.Trim().ToLowerInvariant()) {
              case "binary":
                handleBinaryEnums = true;
                break;
              case "char":
                handleCharEnums = true;
                break;
              case "hex":
                handleHexEnums = true;
                break;
              case "":
                // ignore
                break;
              default:
                Console.Error.WriteLine($"Unsupported enum handling: {handling}");
                return 4;
            }
          }
          break;
        case "-f": {
          var format = args[i + 1];
          switch (format.Trim().ToLowerInvariant()) {
            case "cs":
            case "csharp":
              formatter = new CSharpFormatter();
              break;
            case "cs-md":
            case "csharp-markdown":
              formatter = new CSharpMarkdownFormatter();
              break;
            default:
              Console.Error.WriteLine($"Unsupported output format: {format}");
              return 4;
          }
          break;
        }
        case "-ia":
          includedAttributes.Add(args[i + 1]);
          break;
        case "-r":
          dependencyPath.Add(args[i + 1]);
          break;
        case "-v": {
          var visibility = args[i + 1];
          switch (visibility.Trim().ToLowerInvariant()) {
            case "internal":
              includeInternals = true;
              break;
            case "public":
              includeInternals = false;
              break;
            default:
              Console.Error.WriteLine($"Unsupported visibility ({visibility}) specified; should be either 'public' or 'internal'.");
              return 4;
          }
          break;
        }
        default:
          return Program.Usage(4);
      }
    }
    // Default to C#
    formatter ??= new CSharpFormatter();
    formatter.EnableBinaryEnums(handleBinaryEnums);
    formatter.EnableCharEnums(handleCharEnums);
    formatter.EnableHexEnums(handleHexEnums);
    formatter.ExcludeCustomAttributes(excludedAttributes);
    formatter.IncludeCustomAttributes(includedAttributes);
    formatter.IncludeInternals = includeInternals;
    try {
      using var reference = referenceSource == "-" ? Console.Out : new StreamWriter(File.Create(referenceSource), Encoding.UTF8);
      foreach (var line in formatter.FormatPublicApi(assembly, Program.CreateReaderParameters(assembly, dependencyPath))) {
        if (line is null) {
          reference.WriteLine();
        }
        else {
          reference.WriteLine(line);
        }
      }
    }
    catch (AssemblyResolutionException are) {
      var fg = Console.ForegroundColor;
      try {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Write("ERROR: ");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("There was a problem resolving a dependency (");
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write(are.AssemblyReference);
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("); please provide it using the -r option.");
      }
      finally {
        Console.ForegroundColor = fg;
      }
      return 16;
    }
    return 0;
  }

  private static int Usage(int rc = 1) {
    Console.WriteLine("Usage: {0} ASSEMBLY OUTPUT-FILE [OPTIONS]", Assembly.GetExecutingAssembly().GetName().Name);
    Console.WriteLine();
    Console.WriteLine("Generate a reference source named OUTPUT-FILE containing the public API surface");
    Console.WriteLine("for ASSEMBLY.");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  -ea ATTRIBUTE-TYPE-NAME   Exclude a particular attribute");
    Console.WriteLine("  -eh ENUM-HANDLING         Activate specific enum handling (comma-separated)");
    Console.WriteLine("                            - binary: use binary literals for [Flags] enums");
    Console.WriteLine("                            - char: try to use character literals (ushort only)");
    Console.WriteLine("                            - hex: use hexadecimal literals for [Flags] enums");
    Console.WriteLine("  -f  FORMAT                Specify the output format");
    Console.WriteLine("                            - csharp / cs: plain C# syntax");
    Console.WriteLine("                            - csharp-markdown / cs-md: Markdown with C# code");
    Console.WriteLine("                            (default: csharp)");
    Console.WriteLine("  -ia ATTRIBUTE-TYPE-NAME   Include a particular attribute");
    Console.WriteLine("  -r  DEPENDENCY-DIR        Add a location to search for required references");
    Console.WriteLine("  -v  VISIBILITY            What visibility to include ('public' or 'internal')");
    Console.WriteLine("                            (default: public)");
    return rc;
  }

}
