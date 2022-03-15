using System.Diagnostics.CodeAnalysis;
using System.Linq;

using JetBrains.Annotations;

namespace Zastai.Build.ApiReference;

/// <summary>Utility methods for working with Mono.Cecil elements.</summary>
[PublicAPI]
internal static class CecilUtils {

  public static string?[]? GetTupleElementNames(this ICustomAttributeProvider? cap) {
    if (cap is not null && cap.HasCustomAttributes) {
      foreach (var ca in cap.CustomAttributes) {
        if (ca.AttributeType.IsNamed("System.Runtime.CompilerServices", "TupleElementNamesAttribute")) {
          if (ca.HasConstructorArguments && ca.ConstructorArguments.Count == 1) {
            var arg = ca.ConstructorArguments[0];
            if (arg.Value is CustomAttributeArgument[] values) {
              var names = new string?[values.Length];
              var idx = 0;
              foreach (var value in values) {
                if (value.Type == value.Type.Module.TypeSystem.String && value.Value is string name) {
                  names[idx] = name;
                }
                ++idx;
              }
              return names;
            }
          }
          return null;
        }
      }
    }
    return null;
  }

  public static bool HasCovariantReturn(this MethodDefinition md) {
    if (md.HasCustomAttributes) {
      foreach (var ca in md.CustomAttributes) {
        if (ca.AttributeType.IsNamed("System.Runtime.CompilerServices", "PreserveBaseOverridesAttribute")) {
          return true;
        }
      }
    }
    return false;
  }

  private static bool HasNullabilityFlag(this ICustomAttributeProvider? cap, string attribute, byte value, int idx) {
    if (cap is not null && cap.HasCustomAttributes) {
      foreach (var ca in cap.CustomAttributes) {
        if (ca.AttributeType.IsNamed("System.Runtime.CompilerServices", attribute)) {
          if (ca.HasConstructorArguments && ca.ConstructorArguments.Count == 1) {
            var arg = ca.ConstructorArguments[0];
            if (arg.Value is CustomAttributeArgument[] values && values.Length > idx) {
              arg = values[idx];
            }
            else if (idx != 0) {
              return false;
            }
            return arg.Type == arg.Type.Module.TypeSystem.Byte && arg.Value is byte b && b == value;
          }
          return false;
        }
      }
    }
    return false;
  }

  private static bool HasNullableFlag(this ICustomAttributeProvider? cap, byte value, int idx)
    => cap.HasNullabilityFlag("NullableAttribute", value, idx);

  public static MethodDefinition? IfPublicApi(this MethodDefinition? method) {
    if (method is null) {
      return null;
    }
    return method.IsPublicApi() ? method : null;
  }

  public static bool IsByRefLike(this TypeDefinition td) {
    if (td.HasCustomAttributes) {
      foreach (var ca in td.CustomAttributes) {
        if (ca.AttributeType.IsNamed("System.Runtime.CompilerServices", "IsByRefLikeAttribute")) {
          return true;
        }
      }
    }
    return false;
  }

  public static bool IsCompilerGenerated(this TypeReference tr) {
    var td = tr.Resolve();
    if (td is not null && td.HasCustomAttributes) {
      foreach (var ca in td.CustomAttributes) {
        if (ca.AttributeType.IsNamed("System.Runtime.CompilerServices", "CompilerGeneratedAttribute")) {
          return true;
        }
      }
      return false;
    }
    return false;
  }

  public static bool IsCoreLibraryType(this TypeReference tr) => tr.Scope == tr.Module.TypeSystem.CoreLibrary;

  public static bool IsCoreLibraryType(this TypeReference tr, string? ns) => tr.IsCoreLibraryType() && tr.IsNamed(ns);

  public static bool IsCoreLibraryType(this TypeReference tr, string? ns, string name)
    => tr.IsCoreLibraryType() && tr.IsNamed(ns, name);

  public static bool IsDynamic(this ICustomAttributeProvider? cap, int idx) {
    if (cap is not null && cap.HasCustomAttributes) {
      foreach (var ca in cap.CustomAttributes) {
        if (ca.AttributeType.FullName == "System.Runtime.CompilerServices.DynamicAttribute") {
          if (idx == 0 && !ca.HasConstructorArguments) {
            return true;
          }
          if (ca.HasConstructorArguments && ca.ConstructorArguments.Count == 1) {
            var arg = ca.ConstructorArguments[0];
            if (arg.Value is CustomAttributeArgument[] values && values.Length > idx) {
              arg = values[idx];
            }
            else if (idx != 0) {
              return false;
            }
            return arg.Type == arg.Type.Module.TypeSystem.Boolean && arg.Value is true;
          }
          return false;
        }
      }
    }
    return false;
  }

  public static bool IsLocalType(this TypeReference tr) => tr.Scope == tr.Module;

  public static bool IsLocalType(this TypeReference tr, string? ns) => tr.IsLocalType() && tr.IsNamed(ns);

  public static bool IsLocalType(this TypeReference tr, string? ns, string name) => tr.IsLocalType() && tr.IsNamed(ns, name);

  public static bool IsNamed(this TypeReference tr, string? ns) => tr.Namespace == ns;

  public static bool IsNamed(this TypeReference tr, string? ns, string name) => tr.Namespace == ns && tr.Name == name;

  public static bool IsNativeInteger(this ICustomAttributeProvider? cap, int idx) {
    if (cap is not null && cap.HasCustomAttributes) {
      foreach (var ca in cap.CustomAttributes) {
        if (ca.AttributeType.IsNamed("System.Runtime.CompilerServices", "NativeIntegerAttribute")) {
          if (idx == 0 && !ca.HasConstructorArguments) {
            return true;
          }
          if (ca.HasConstructorArguments && ca.ConstructorArguments.Count == 1) {
            var arg = ca.ConstructorArguments[0];
            if (arg.Value is CustomAttributeArgument[] values && values.Length > idx) {
              arg = values[idx];
            }
            else if (idx != 0) {
              return false;
            }
            return arg.Type == arg.Type.Module.TypeSystem.Boolean && arg.Value is true;
          }
          return false;
        }
      }
    }
    return false;
  }

  public static bool IsNullable(this ICustomAttributeProvider? cap, int idx = 0) => cap.HasNullableFlag(2, idx);

  public static bool IsParamArray(this ParameterDefinition pd)
    => pd.HasCustomAttributes && pd.CustomAttributes.Any(ca => ca.AttributeType.IsNamed("System", "ParamArrayAttribute"));

  // FIXME: Can the remove method differ? Does it matter?
  public static bool IsPublicApi(this EventDefinition ed) => ed.AddMethod.IsPublicApi();

  public static bool IsPublicApi(this FieldDefinition fd) => fd.IsPublic || fd.IsFamily || fd.IsFamilyOrAssembly;

  public static bool IsPublicApi(this ICustomAttribute ca) {
    var attributeType = ca.AttributeType;
    // FIXME: Perhaps this should be a list of which one(s) to retain instead...
    switch (attributeType.Namespace) {
      case "":
        switch (attributeType.Name) {
          case "__DynamicallyInvokableAttribute":
            // Not relevant to API
            return false;
        }
        break;
      case "System":
        switch (attributeType.Name) {
          case "CLSCompliantAttribute":
            // Not relevant to API
            return false;
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
      case "System.CodeDom.Compiler":
        switch (attributeType.Name) {
          case "GeneratedCodeAttribute":
            // Not relevant to API
            return false;
        }
        break;
      case "System.ComponentModel":
        switch (attributeType.Name) {
          case "BrowsableAttribute":
          case "DesignOnlyAttribute":
          case "DesignTimeVisibleAttribute":
          case "DesignerSerializationVisibilityAttribute":
          case "EditorBrowsableAttribute":
            // Not relevant to API
            return false;
        }
        break;
      case "System.Diagnostics":
        switch (attributeType.Name) {
          case "DebuggableAttribute":
          case "DebuggerBrowsableAttribute":
          case "DebuggerDisplayAttribute":
          case "DebuggerHiddenAttribute":
          case "DebuggerNonUserCodeAttribute":
          case "DebuggerStepThroughAttribute":
          case "DebuggerTypeProxyAttribute":
          case "StackTraceHiddenAttribute":
            // Not relevant to API
            return false;
        }
        break;
      case "System.Diagnostics.CodeAnalysis":
        switch (attributeType.Name) {
          case "AllowNullAttribute":
          case "DisallowNullAttribute":
          case "DoesNotReturnAttribute":
          case "DoesNotReturnIfAttribute":
          case "DynamicallyAccessedMembersAttribute":
          case "DynamicDependencyAttribute":
          case "MaybeNullAttribute":
          case "MaybeNullWhenAttribute":
          case "MemberNotNullAttribute":
          case "MemberNotNullWhenAttribute":
          case "NotNullAttribute":
          case "NotNullIfNotNullAttribute":
          case "NotNullWhenAttribute":
            // Not really relevant to API (yet; maybe become so if full nullable reference type support is added)
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
          case "__BlockReflectionAttribute":
          case "AsyncIteratorStateMachineAttribute":
          case "AsyncMethodBuilderAttribute":
          case "AsyncStateMachineAttribute":
          case "CompilationRelaxationsAttribute":
          case "CompilerGeneratedAttribute":
          case "IntrinsicAttribute":
          case "IteratorStateMachineAttribute":
          case "RuntimeCompatibilityAttribute":
          case "SkipLocalsInitAttribute":
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
          case "IsUnmanagedAttribute":
            // This is handled as part of generic type constraint handling.
            return false;
          case "NullableAttribute":
          case "NullableContextAttribute":
          case "NullablePublicOnlyAttribute":
            // These are handled as part of nullable reference type support.
            return false;
          case "PreserveBaseOverridesAttribute":
            // This is handled as part of method attribute handling.
            return false;
          case "DynamicAttribute":
          case "NativeIntegerAttribute":
          case "TupleElementNamesAttribute":
            // This is handled as part of type name handling.
            return false;
        }
        break;
      case "System.Runtime.Versioning":
        switch (attributeType.Name) {
          case "NonVersionableAttribute":
          case "RequiresPreviewFeaturesAttribute":
            // Not relevant to API
            return false;
        }
        break;
      case "System.Security":
        switch (attributeType.Name) {
          case "AllowPartiallyTrustedCallersAttribute":
          case "SecurityCriticalAttribute":
          case "SecuritySafeCriticalAttribute":
          case "SecurityTransparentAttribute":
          case "UnverifiableCodeAttribute":
            // Not relevant to API
            return false;
        }
        break;
      case "Xunit.Sdk":
        // Assume this is all unit tests and therefore irrelevant to API
        return false;
    }
    // Assume public API by default.
    return true;
  }

  public static bool IsPublicApi(this MethodDefinition md) => md.IsPublic || md.IsFamily || md.IsFamilyOrAssembly;

  public static bool IsPublicApi(this TypeDefinition td)
    => td.IsPublic || td.IsNestedPublic || td.IsNestedFamily || td.IsNestedFamilyOrAssembly;

  public static bool IsReadOnly(this ICustomAttributeProvider provider) {
    if (provider.HasCustomAttributes) {
      foreach (var ca in provider.CustomAttributes) {
        if (ca.AttributeType.IsNamed("System.Runtime.CompilerServices", "IsReadOnlyAttribute")) {
          return true;
        }
      }
    }
    return false;
  }

  public static bool IsUnmanaged(this GenericParameter gp) {
    if (gp.HasCustomAttributes) {
      foreach (var ca in gp.CustomAttributes) {
        if (ca.AttributeType.IsNamed("System.Runtime.CompilerServices", "IsUnmanagedAttribute")) {
          return true;
        }
      }
    }
    return false;
  }

  public static string NonGenericName(this TypeReference tr) {
    var name = tr.Name;
    var backTick = name.IndexOf('`');
    return backTick >= 0 ? name.Substring(0, backTick) : name;
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
