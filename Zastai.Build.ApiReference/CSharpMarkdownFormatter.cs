using System.Linq;

namespace Zastai.Build.ApiReference;

internal class CSharpMarkdownFormatter : CSharpFormatter {

  protected override int TopLevelTypeIndent => 0;

  private static readonly string?[] CodeFooter = { "```" };

  protected override IEnumerable<string?> AssemblyAttributeFooter(AssemblyDefinition ad) => CSharpMarkdownFormatter.CodeFooter;

  protected override IEnumerable<string?> AssemblyAttributeHeader(AssemblyDefinition ad) {
    yield return null;
    yield return "## Assembly Attributes";
    yield return null;
    yield return "```cs";
  }

  protected override IEnumerable<string?> FileHeader(AssemblyDefinition ad) {
    yield return $"# API Reference: {ad.Name.Name}";
  }

  protected override IEnumerable<string?> ModuleAttributeFooter(ModuleDefinition md) => CSharpMarkdownFormatter.CodeFooter;

  protected override IEnumerable<string?>  ModuleAttributeHeader(ModuleDefinition md) {
    yield return null;
    yield return $"## Module Attributes: {md.Name}";
    yield return null;
    yield return "```cs";
  }

  protected override IEnumerable<string?> NamespaceFooter() => Enumerable.Empty<string?>();

  protected override IEnumerable<string?> NamespaceHeader() {
    yield return null;
    yield return string.IsNullOrEmpty(this.CurrentNamespace) ? "## Unnamed Namespace" : $"## Namespace: {this.CurrentNamespace}";
  }

  protected override IEnumerable<string?> TypeFooter(TypeDefinition td) => CSharpMarkdownFormatter.CodeFooter;

  protected override IEnumerable<string?> TypeHeader(TypeDefinition td) {
    yield return null;
    yield return $"### Type: {this.TypeName(td).Replace("<", "\\<")}";
    yield return null;
    yield return "```cs";
  }

}
