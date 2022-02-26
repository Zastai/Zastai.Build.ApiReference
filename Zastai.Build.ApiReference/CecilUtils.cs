﻿using System.Diagnostics.CodeAnalysis;
using System.Linq;

using JetBrains.Annotations;

namespace Zastai.Build.ApiReference;

/// <summary>Utility methods for working with Mono.Cecil elements.</summary>
[PublicAPI]
internal static class CecilUtils {

  private static bool HasNullabilityFlag(this ICustomAttributeProvider cap, string attribute, byte value) {
    if (!cap.HasCustomAttributes) {
      return false;
    }
    foreach (var ca in cap.CustomAttributes) {
      // These attributes are emitted in the assembly itself
      if (ca.AttributeType.IsLocalType("System.Runtime.CompilerServices", attribute)) {
        if (ca.HasConstructorArguments && ca.ConstructorArguments.Count == 1) {
          var arg = ca.ConstructorArguments[0];
          if (arg.Type == arg.Type.Module.TypeSystem.Byte) {
            return arg.Value is byte b && b == value;
          }
        }
        return false;
      }
    }
    return false;
  }

  private static bool HasNullableContextFlag(this ICustomAttributeProvider cap, byte value)
    => cap.HasNullabilityFlag("NullableContextAttribute", value);

  private static bool HasNullableFlag(this ICustomAttributeProvider cap, byte value)
    => cap.HasNullabilityFlag("NullableAttribute", value);

  public static MethodDefinition? IfPublicApi(this MethodDefinition? method) {
    if (method is null) {
      return null;
    }
    return method.IsPublicApi() ? method : null;
  }

  public static bool IsByRefLike(this TypeDefinition td) {
    if (!td.HasCustomAttributes) {
      return false;
    }
    foreach (var ca in td.CustomAttributes) {
      if (ca.AttributeType.IsCoreLibraryType("System.Runtime.CompilerServices", "IsByRefLikeAttribute")) {
        return true;
      }
    }
    return false;
  }

  public static bool IsCompilerGenerated(this TypeReference tr) {
    var td = tr.Resolve();
    if (td is null) {
      return false;
    }
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

  public static bool IsDynamic(this ICustomAttributeProvider? cap) {
    if (cap is null || !cap.HasCustomAttributes) {
      return false;
    }
    foreach (var ca in cap.CustomAttributes) {
      // This is also not a core library type; it's in System.Linq.Expressions, of all places.
      if (ca.AttributeType.FullName == "System.Runtime.CompilerServices.DynamicAttribute") {
        return true;
      }
    }
    return false;
  }

  public static bool IsLocalType(this TypeReference tr) => tr.Scope == tr.Module;

  public static bool IsLocalType(this TypeReference tr, string? ns, string name)
    => tr.IsLocalType() && tr.Namespace == ns && tr.Name == name;

  public static bool IsNativeInteger(this ICustomAttributeProvider? cap) {
    if (cap is null || !cap.HasCustomAttributes) {
      return false;
    }
    foreach (var ca in cap.CustomAttributes) {
      // These attributes are emitted in the assembly itself
      if (ca.AttributeType.IsLocalType("System.Runtime.CompilerServices", "NativeIntegerAttribute")) {
        return true;
      }
    }
    return false;
  }

  public static bool IsNotNull(this ICustomAttributeProvider cap) => cap.HasNullableFlag(1);

  public static bool IsNotNullContext(this ICustomAttributeProvider cap) => cap.HasNullableContextFlag(1);

  public static bool IsNullable(this ICustomAttributeProvider cap) => cap.HasNullableFlag(2);

  public static bool IsNullableContext(this ICustomAttributeProvider cap) => cap.HasNullableContextFlag(2);

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
            case "ObsoleteAttribute":
              // This is normally relevant to public API. But the compiler sometimes introduces one when particular language
              // features are used, and we don't care about those.
              if (ca.HasConstructorArguments) {
                var arg = ca.ConstructorArguments[0];
                if (arg.Type == arg.Type.Module.TypeSystem.String) {
                  switch ((string) arg.Value) {
                    case Obsolete.RefStructs:
                      return false;
                  }
                }
              }
              return true;
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
            case "IsByRefLikeAttribute":
              // This is handled as part of struct definition handling.
              return false;
            case "IsReadOnlyAttribute":
              // This is handled as part of relevant handling:
              // - type definitions (for readonly struct)
              // - method & property definitions (for readonly struct members)
              // - return types
              return false;
          }
          break;
      }
    }
    else if (attributeType.Namespace == "System.Runtime.CompilerServices") {
      // Some of these live outside the core library
      switch (attributeType.Name) {
        case "IsUnmanagedAttribute" when attributeType.IsLocalType():
          // This is handled as part of generic type constraint handling.
          return false;
        case "DynamicAttribute":
        case "NativeIntegerAttribute" when attributeType.IsLocalType():
          // These are handled as part of type name handling.
          return false;
        case "NullableAttribute" when attributeType.IsLocalType():
        case "NullableContextAttribute" when attributeType.IsLocalType():
          // These are handled as part of nullable reference type support.
          return false;
      }
    }
    // Assume public API by default.
    return true;
  }

  public static bool IsPublicApi(this MethodDefinition md) => md.IsPublic || md.IsFamily || md.IsFamilyOrAssembly;

  public static bool IsPublicApi(this TypeDefinition td)
    => td.IsPublic || td.IsNestedPublic || td.IsNestedFamily || td.IsNestedFamilyOrAssembly;

  private static bool IsReadOnlyInternal(ICustomAttributeProvider provider) {
    if (!provider.HasCustomAttributes) {
      return false;
    }
    foreach (var ca in provider.CustomAttributes) {
      if (ca.AttributeType.IsCoreLibraryType("System.Runtime.CompilerServices", "IsReadOnlyAttribute")) {
        return true;
      }
    }
    return false;
  }

  public static bool IsReadOnly(this MethodDefinition md) => CecilUtils.IsReadOnlyInternal(md);

  public static bool IsReadOnly(this PropertyDefinition pd) => CecilUtils.IsReadOnlyInternal(pd);

  public static bool IsReadOnly(this TypeDefinition td) => CecilUtils.IsReadOnlyInternal(td);

  public static bool IsUnmanaged(this GenericParameter gp) {
    if (!gp.HasCustomAttributes) {
      return false;
    }
    foreach (var ca in gp.CustomAttributes) {
      // These attributes are emitted in the assembly itself
      if (ca.AttributeType.IsLocalType("System.Runtime.CompilerServices", "IsUnmanagedAttribute")) {
        return true;
      }
    }
    return false;
  }

  private static class Obsolete {

    public const string RefStructs = "Types with embedded references are not supported in this version of your compiler.";

  }

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
