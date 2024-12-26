using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Microsoft.Build.Framework;

using Mono.Cecil;

namespace Zastai.Build.ApiReference;

/// <summary>A build task that generates an API reference source for the assemblies produced by the build.</summary>
public sealed class GenerateApiReference : ITask {

  #region ITask Properties

  public IBuildEngine? BuildEngine { get; set; }

  public ITaskHost? HostObject { get; set; }

  #endregion

  #region Task Parameters

  [Required]
  public string Assembly { get; set; } = string.Empty;

  public string EnumHandling { get; set; } = string.Empty;

  public ITaskItem[] ExcludeAttributes { get; set; } = [];

  public string Format { get; set; } = string.Empty;

  public ITaskItem[] IncludeAttributes { get; set; } = [];

  public ITaskItem[] LibraryPath { get; set; } = [];

  [Required]
  public string OutputPath { get; set; } = string.Empty;

  public string Visibility { get; set; } = string.Empty;

  #endregion

  private ReaderParameters CreateReaderParameters(string assembly, IEnumerable<string> libraryPath) {
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
    foreach (var dir in libraryPath) {
      if (Directory.Exists(dir)) {
        resolver.AddSearchDirectory(dir);
      }
      else {
        this.LogWarning($"Ignoring non-existent dependency folder: {dir}");
      }
    }
    return new ReaderParameters() {
      AssemblyResolver = resolver,
      ReadingMode = ReadingMode.Immediate,
      ReadSymbols = false,
      ReadWrite = false,
    };
  }

  public bool Execute() {
    var assembly = this.Assembly;
    if (!File.Exists(assembly)) {
      this.LogError($"Assembly not found: {assembly}");
      return false;
    }
    var referenceSource = this.OutputPath;
    var outputDir = Path.GetDirectoryName(Path.GetFullPath(referenceSource));
    if (!Directory.Exists(outputDir)) {
      this.LogError($"Output folder does not exist: {outputDir}");
      return false;
    }
    CodeFormatter? formatter = null;
    if (!string.IsNullOrWhiteSpace(this.Format)) {
      switch (this.Format.Trim().ToLowerInvariant()) {
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
          this.LogError($"Unsupported output format: {this.Format}");
          return false;
      }
    }
    var handleBinaryEnums = false;
    var handleCharEnums = false;
    var handleHexEnums = false;
    if (!string.IsNullOrWhiteSpace(this.EnumHandling)) {
      foreach (var handling in this.EnumHandling.Split(',', ';')) {
        switch (handling.Trim().ToLowerInvariant()) {
          case "binary":
            handleBinaryEnums = true;
            break;
          case "char":
            handleCharEnums = true;
            break;
          case "hex":
          case "hex-flags":
            handleHexEnums = true;
            break;
          case "":
            // ignore
            break;
          default:
            this.LogError($"Unsupported enum handling: {handling}");
            return false;
        }
      }
    }
    var includeInternals = false;
    if (!string.IsNullOrWhiteSpace(this.Visibility)) {
      switch (this.Visibility.Trim().ToLowerInvariant()) {
        case "internal":
          includeInternals = true;
          break;
        case "public":
          includeInternals = false;
          break;
        default:
          this.LogError($"Unsupported visibility ({this.Visibility}) specified; should be either 'public' or 'internal'.");
          return false;
      }
    }
    var includedAttributes = this.IncludeAttributes.Select(item => item.ItemSpec).ToArray();
    var excludedAttributes = this.ExcludeAttributes.Select(item => item.ItemSpec).ToArray();
    // Assumption: The identity is fine, no need to go for the FullPath metadata.
    var libraryPath = this.LibraryPath.Select(item => item.ItemSpec).ToArray();
    // Default to C#
    formatter ??= new CSharpFormatter();
    formatter.EnableBinaryEnums(handleBinaryEnums);
    formatter.EnableCharEnums(handleCharEnums);
    formatter.EnableHexEnums(handleHexEnums);
    formatter.ExcludeCustomAttributes(excludedAttributes);
    formatter.IncludeCustomAttributes(includedAttributes);
    formatter.IncludeInternals = includeInternals;
    // TODO: Once the formatter has a TraceSource, configure it to output build events.
    try {
      using var ad = AssemblyDefinition.ReadAssembly(assembly, this.CreateReaderParameters(assembly, libraryPath));
      using var reference = new StreamWriter(File.Create(referenceSource), Encoding.UTF8);
      foreach (var line in formatter.FormatPublicApi(ad)) {
        reference.WriteLine(line);
      }
    }
    catch (AssemblyResolutionException are) {
      this.LogError($"There was a problem resolving a dependency ({are.AssemblyReference}); you may need to set " +
                    $"$(ApiReferenceLibraryPath) to include its location.");
      return false;
    }
    return true;
  }

  private void LogError(string message) {
    if (this.BuildEngine is null) {
      return;
    }
    var args = new BuildErrorEventArgs(null, null, null, 0, 0, 0, 0, message, null, nameof(GenerateApiReference), DateTime.UtcNow);
    this.BuildEngine.LogErrorEvent(args);
  }

  private void LogMessage(string message, MessageImportance importance = MessageImportance.Normal) {
    if (this.BuildEngine is null) {
      return;
    }
    var args = new BuildMessageEventArgs(null, null, null, 0, 0, 0, 0, message, null, nameof(GenerateApiReference), importance);
    this.BuildEngine.LogMessageEvent(args);
  }

  private void LogWarning(string message) {
    if (this.BuildEngine is null) {
      return;
    }
    var args = new BuildWarningEventArgs(null, null, null, 0, 0, 0, 0, message, null, nameof(GenerateApiReference));
    this.BuildEngine.LogWarningEvent(args);
  }

}
