using System.Text;

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

    var dependencyPath = new List<string>();
    for (var i = 2; i < args.Length; i += 2) {
      if (args[i] != "-r") {
        return Program.Usage(4);
      }
      dependencyPath.Add(args[i + 1]);
    }

    using var reference = referenceSource == "-" ? Console.Out : new StreamWriter(File.Create(referenceSource), Encoding.UTF8);
    using var ad = AssemblyDefinition.ReadAssembly(assembly, Program.CreateReaderParameters(assembly, dependencyPath));

    reference.WritePublicApi(ad);

    return 0;
  }

  private static int Usage(int rc = 1) {
    var programName = Assembly.GetExecutingAssembly().GetName().Name;
    Console.Out.WriteLine("Usage: {0} ASSEMBLY OUTPUT-FILE [-r DEPENDENCY-DIR]...", programName);
    return rc;
  }

}
