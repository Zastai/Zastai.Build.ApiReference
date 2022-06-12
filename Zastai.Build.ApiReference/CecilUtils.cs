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

  public static Nullability? GetNullability(this ICustomAttributeProvider cap, MethodDefinition? context, int idx = 0)
    => cap.GetNullability(idx) ?? context.GetNullabilityContext();

  public static Nullability? GetNullability(this ICustomAttributeProvider cap, MethodDefinition? methodContext,
                                            TypeDefinition? typeContext, int idx = 0)
    => cap.GetNullability(idx) ?? methodContext.GetNullabilityContext() ?? typeContext.GetNullabilityContext();

  public static Nullability? GetNullability(this ICustomAttributeProvider cap, TypeDefinition? context, int idx = 0)
    => cap.GetNullability(idx) ?? context.GetNullabilityContext();

  public static Nullability? GetNullability(this ICustomAttributeProvider cap, int idx = 0) {
    if (cap.HasCustomAttributes) {
      foreach (var ca in cap.CustomAttributes) {
        if (ca.AttributeType.IsNamed("System.Runtime.CompilerServices", "NullableAttribute")) {
          if (ca.HasConstructorArguments && ca.ConstructorArguments.Count == 1) {
            var arg = ca.ConstructorArguments[0];
            if (arg.Value is CustomAttributeArgument[] values) {
              if (values.Length > idx) {
                arg = values[idx];
              }
              else {
                // There is a array of values but not for this index: should really be an error, but assume null-oblivious.
                return Nullability.Oblivious;
              }
            }
            if (arg.Type == arg.Type.Module.TypeSystem.Byte && arg.Value is byte b) {
              return (Nullability) b;
            }
          }
          // Attribute present: assume null-oblivious when we can't get a definite value
          return Nullability.Oblivious;
        }
      }
    }
    // No attribute: unknown - context needed
    return null;
  }

  private static Nullability? GetNullabilityContext(this MethodDefinition? md) {
    if (md is not null) {
      var nullability = md.GetNullabilityContextInternal();
      if (nullability.HasValue) {
        return nullability;
      }
      // No attribute: get from parent context (i.e. enclosing type).
      return md.DeclaringType.GetNullabilityContext();
    }
    return null;
  }

  private static Nullability? GetNullabilityContext(this TypeDefinition? td) {
    while (td is not null) {
      var nullability = td.GetNullabilityContextInternal();
      if (nullability.HasValue) {
        return nullability;
      }
      // No attribute: get from parent context (i.e. enclosing type).
      td = td.DeclaringType;
    }
    return null;
  }

  private static Nullability? GetNullabilityContextInternal(this ICustomAttributeProvider? cap) {
    if (cap is not null && cap.HasCustomAttributes) {
      foreach (var ca in cap.CustomAttributes) {
        if (ca.AttributeType.IsNamed("System.Runtime.CompilerServices", "NullableContextAttribute")) {
          if (ca.HasConstructorArguments && ca.ConstructorArguments.Count == 1) {
            var arg = ca.ConstructorArguments[0];
            if (arg.Type == arg.Type.Module.TypeSystem.Byte && arg.Value is byte b) {
              return (Nullability) b;
            }
          }
          // Attribute present: assume 0 when we can't get a definite value
          return Nullability.Oblivious;
        }
      }
    }
    // No attribute: unknown - check parent context
    return null;
  }

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

  public static bool IsParamArray(this ParameterDefinition pd)
    => pd.HasCustomAttributes && pd.CustomAttributes.Any(ca => ca.AttributeType.IsNamed("System", "ParamArrayAttribute"));

  // FIXME: Can the remove method differ? Does it matter?
  public static bool IsPublicApi(this EventDefinition ed) => ed.AddMethod.IsPublicApi();

  public static bool IsPublicApi(this FieldDefinition fd) => fd.IsPublic || fd.IsFamily || fd.IsFamilyOrAssembly;

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
