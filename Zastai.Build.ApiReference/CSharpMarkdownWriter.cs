namespace Zastai.Build.ApiReference;

internal class CSharpMarkdownWriter : CSharpWriter {

  public CSharpMarkdownWriter(TextWriter writer) : base(writer) {
  }

  protected override int TopLevelTypeIndent => 0;

  protected override void WriteAssemblyAttributeFooter(AssemblyDefinition ad) => this.Writer.WriteLine("```");

  protected override void WriteAssemblyAttributeHeader(AssemblyDefinition ad) {
    this.Writer.WriteLine();
    this.Writer.WriteLine("## Assembly Attributes");
    this.Writer.WriteLine();
    this.Writer.WriteLine("```cs");
  }

  protected override void WriteFileHeader(AssemblyDefinition ad) {
    this.Writer.WriteLine($"# API Reference: {ad.Name.Name}");
  }

  protected override void WriteModuleAttributeFooter(ModuleDefinition md) => this.Writer.WriteLine("```");

  protected override void WriteModuleAttributeHeader(ModuleDefinition md) {
    this.Writer.WriteLine();
    this.Writer.WriteLine($"## Module Attributes: {md.Name}");
    this.Writer.WriteLine();
    this.Writer.WriteLine("```cs");
  }

  protected override void WriteNamespaceFooter() {
  }

  protected override void WriteNamespaceHeader() {
    this.Writer.WriteLine();
    if (string.IsNullOrEmpty(this.CurrentNamespace)) {
      this.Writer.WriteLine("## Unnamed Namespace");
    }
    else {
      this.Writer.WriteLine($"## Namespace: {this.CurrentNamespace}");
    }
  }

  protected override void WriteTypeFooter(TypeDefinition td) => this.Writer.WriteLine("```");

  protected override void WriteTypeHeader(TypeDefinition td) {
    this.Writer.WriteLine();
    this.Writer.Write("### Type: ");
    this.WriteTypeName(td);
    this.Writer.WriteLine();
    this.Writer.WriteLine();
    this.Writer.WriteLine("```cs");
  }

}
