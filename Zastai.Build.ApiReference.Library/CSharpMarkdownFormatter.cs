using System.Collections.Generic;
using System.Text;

using Mono.Cecil;

namespace Zastai.Build.ApiReference;

/// <summary>A class that will extract and format the public API for an assembly as Markdown with C# code blocks.</summary>
public class CSharpMarkdownFormatter : CSharpFormatter {

  private static readonly string?[] CodeFooter = [
    "```"
  ];

  /// <inheritdoc />
  protected override IEnumerable<string?> AssemblyAttributeFooter(AssemblyDefinition ad) => CSharpMarkdownFormatter.CodeFooter;

  /// <inheritdoc />
  protected override IEnumerable<string?> AssemblyAttributeHeader(AssemblyDefinition ad) {
    yield return null;
    yield return "## Assembly Attributes";
    yield return null;
    yield return "```cs";
  }

  /// <inheritdoc />
  protected override IEnumerable<string?> ExportedTypes(SortedDictionary<string, IDictionary<string, ExportedType>> exportedTypes) {
    yield return null;
    yield return "## Exported Types";
    var sb = new StringBuilder();
    foreach (var scope in exportedTypes) {
      yield return null;
      yield return $"Exported to {scope.Key}:";
      yield return null;
      foreach (var et in scope.Value.Values) {
        sb.Clear();
        sb.Append("- ").Append(this.TypeName(et));
        yield return sb.ToString();
      }
    }
  }

  /// <inheritdoc />
  protected override IEnumerable<string?> FileHeader(AssemblyDefinition ad) {
    yield return $"# API Reference: {ad.Name.Name}";
  }

  /// <inheritdoc />
  protected override IEnumerable<string?> ModuleAttributeFooter(ModuleDefinition md) => CSharpMarkdownFormatter.CodeFooter;

  /// <inheritdoc />
  protected override IEnumerable<string?> ModuleAttributeHeader(ModuleDefinition md) {
    yield return null;
    yield return $"## Module Attributes: {md.Name}";
    yield return null;
    yield return "```cs";
  }

  /// <inheritdoc />
  protected override IEnumerable<string?> NamespaceHeader() {
    yield return null;
    yield return string.IsNullOrEmpty(this.CurrentNamespace) ? "## Unnamed Namespace" : $"## Namespace: {this.CurrentNamespace}";
  }

  /// <inheritdoc />
  protected override IEnumerable<string?> TypeFooter(TypeDefinition td) => CSharpMarkdownFormatter.CodeFooter;

  /// <inheritdoc />
  protected override IEnumerable<string?> TypeHeader(TypeDefinition td) {
    yield return null;
    yield return $"### Type: {this.TypeName(td).Replace("<", "\\<")}";
    yield return null;
    yield return "```cs";
  }

}
