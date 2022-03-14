using System.Reflection;
using System.Runtime.Serialization;

namespace Zastai.Build.ApiReference;

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
        Console.Out.WriteLine("Ignoring non-existent dependency folder: {0}", dir);
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
      Console.Error.WriteLine("Assembly not found: {0}", assembly);
      return 2;
    }

    var referenceSource = args[1];
    var outputDir = Path.GetDirectoryName(Path.GetFullPath(referenceSource));
    if (!Directory.Exists(outputDir)) {
      Console.Error.WriteLine("Output folder does not exist: {0}", outputDir);
      return 3;
    }

    CodeFormatter? formatter = null;
    var dependencyPath = new List<string>();
    for (var i = 2; i < args.Length; i += 2) {
      switch (args[i]) {
        case "-f":
          switch (args[i + 1].ToLowerInvariant()) {
            case "c#":
            case "cs":
            case "csharp":
              formatter = new CSharpFormatter();
              break;
            case "c#-markdown":
            case "c#-md":
            case "cs-markdown":
            case "cs-md":
            case "csharp-markdown":
            case "csharp-md":
              formatter = new CSharpMarkdownFormatter();
              break;
            default:
              Console.Error.WriteLine("Unsupported output format: {0}", args[i + 1]);
              return 4;
          }
          break;
        case "-r":
          dependencyPath.Add(args[i + 1]);
          break;
        default:
          return Program.Usage(4);
      }
    }

    // Default to C#
    formatter ??= new CSharpFormatter();

    try {
      using var ad = AssemblyDefinition.ReadAssembly(assembly, Program.CreateReaderParameters(assembly, dependencyPath));

      using var reference = referenceSource == "-" ? Console.Out : new StreamWriter(File.Create(referenceSource), Encoding.UTF8);

      foreach (var line in formatter.FormatPublicApi(ad)) {
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
        Console.Out.WriteLine("); please provide it using the -r option.");
      }
      finally {
        Console.ForegroundColor = fg;
      }
      return 16;
    }

    return 0;
  }

  private static int Usage(int rc = 1) {
    var programName = Assembly.GetExecutingAssembly().GetName().Name;
    Console.Out.WriteLine("Usage: {0} ASSEMBLY OUTPUT-FILE [-f FORMAT] [-r DEPENDENCY-DIR]...", programName);
    return rc;
  }

}
