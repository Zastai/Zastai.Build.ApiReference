using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

using JetBrains.Annotations;

namespace Zastai.Build.ApiReference;

/// <summary>Utility methods for working with Mono.Cecil elements.</summary>
[PublicAPI]
internal static class CecilUtils {

  public static bool IsCompilerGenerated(this TypeReference tr) {
    var td = tr.Resolve();
    if (!td.HasCustomAttributes) {
      return false;
    }
    foreach (var ca in td.CustomAttributes) {
      if (ca.AttributeType.IsCoreLibraryType("System.Runtime.CompilerServices", "CompilerGeneratedAttribute")) {
        return true;
      }
    }
    return false;
  }

  public static bool IsCoreLibraryType(this TypeReference tr) => tr.Scope == tr.Module.TypeSystem.CoreLibrary;

  public static bool IsCoreLibraryType(this TypeReference tr, string? ns, string name)
    => tr.IsCoreLibraryType() && tr.Namespace == ns && tr.Name == name;

  public static bool IsParamArray(this ParameterDefinition pd)
    => pd.HasCustomAttributes && pd.CustomAttributes.Any(ca => ca.AttributeType.IsCoreLibraryType("System", "ParamArrayAttribute"));

  // FIXME: Can the remove method differ? Does it matter?
  public static bool IsPublicApi(this EventDefinition ed) => ed.AddMethod.IsPublicApi();

  public static bool IsPublicApi(this FieldDefinition fd) => fd.IsPublic || fd.IsFamily || fd.IsFamilyOrAssembly;

  public static bool IsPublicApi(this ICustomAttribute ca) {
    var attributeType = ca.AttributeType;
    if (attributeType.Scope == attributeType.Module.TypeSystem.CoreLibrary) {
      switch (attributeType.Namespace) {
        case "System":
          switch (attributeType.Name) {
            case "ParamArrayAttribute":
              // This is handled as part of parameter attribute handling
              return false;
          }
          break;
        case "System.Diagnostics":
          switch (attributeType.Name) {
            case "DebuggableAttribute":
            case "DebuggerStepThroughAttribute":
              // Not relevant to API
              return false;
          }
          break;
        case "System.Reflection":
          if (attributeType.Name.StartsWith("Assembly")) {
            return false;
          }
          break;
        case "System.Runtime.CompilerServices":
          switch (attributeType.Name) {
            case "AsyncStateMachineAttribute":
            case "CompilationRelaxationsAttribute":
            case "CompilerGeneratedAttribute":
            case "RuntimeCompatibilityAttribute":
              // Not relevant to API
              return false;
            case "ExtensionAttribute":
              // This is handled as part of method signature handling; we don't care about its presence on assemblies/classes
              return false;
          }
          break;
      }
    }
    else if (attributeType.IsCompilerGenerated()) {
      // Assume compiler-generated attributes are never relevant to API.
      // This includes [Nullable], [NullableContext] and [NullablePublicOnly]; the compiler emits those inside the assembly - it is
      // not a framework type. (Which allows it to work for assemblies targeting older framework versions.)
      return false;
    }
    // Assume public API by default.
    return true;
  }

  public static bool IsPublicApi(this MethodDefinition md) => md.IsPublic || md.IsFamily || md.IsFamilyOrAssembly;

  public static bool IsPublicApi(this TypeDefinition td)
    => td.IsPublic || td.IsNestedPublic || td.IsNestedFamily || td.IsNestedFamilyOrAssembly;

  public static bool TryUnwrapNullable(this TypeReference tr, [NotNullWhen(true)] out TypeReference? unwrapped) {
    if (tr.Scope == tr.Module.TypeSystem.CoreLibrary) {
      if (tr.IsGenericInstance && tr.Namespace == "System" && tr.Name == "Nullable`1") {
        var gi = (IGenericInstance) tr;
        Trace.Assert(gi.HasGenericArguments && gi.GenericArguments.Count == 1,
                     "Nullable type instance does not have exactly one generic argument.");
        unwrapped = gi.GenericArguments[0];
        return true;
      }
    }
    unwrapped = null;
    return false;
  }

}
