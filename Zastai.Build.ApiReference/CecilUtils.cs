using System.Diagnostics.CodeAnalysis;
using System.Linq;

using JetBrains.Annotations;

namespace Zastai.Build.ApiReference;

/// <summary>Utility methods for working with Mono.Cecil elements.</summary>
[PublicAPI]
internal static class CecilUtils {

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

  // FIXME: IReadOnlySet would be better, but is not available on .NET Framework.
  public static ISet<string> GetRuntimeFeatures(this AssemblyDefinition ad) {
    var coreLib = ad.MainModule.TypeSystem.CoreLibrary;
    // FIXME: What this _should_ do is look up System.Runtime.CompilerServices.RuntimeFeature (in the context of coreLib), and
    //        enumerate all its fields, returning a set containing the names of any that are string constants.
    //        However, I have not been able to get that initial type lookup to work, and we currently only care about numeric IntPtr
    //        support, so we just check for .NET 7 or later.
    if (coreLib is AssemblyNameReference { Name: "System.Runtime" } anr && anr.Version >= Version.Parse("7.0")) {
      return new HashSet<string> {
        "NumericIntPtr"
      };
    }
    // FIXME: When we drop .NET Framework support, this could be ImmutableHashSet<string>.Empty (not worth the extra dependency now)
    return new HashSet<string>();
  }

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

  public static bool HasCovariantReturn(this MethodDefinition md)
    => md.HasCustomAttributes &&
       md.CustomAttributes.Any(ca => ca.AttributeType.IsNamed("System.Runtime.CompilerServices", "PreserveBaseOverridesAttribute"));

  public static MethodDefinition? IfPublicApi(this MethodDefinition? method) {
    if (method is null) {
      return null;
    }
    return method.IsPublicApi() ? method : null;
  }

  public static bool IsByRefLike(this TypeDefinition td)
    => td.HasCustomAttributes &&
       td.CustomAttributes.Any(ca => ca.AttributeType.IsNamed("System.Runtime.CompilerServices", "IsByRefLikeAttribute"));

  private static bool IsCompilerGenerated(this TypeDefinition? td)
    => td is not null && td.HasCustomAttributes &&
       td.CustomAttributes.Any(ca => ca.AttributeType.IsNamed("System.Runtime.CompilerServices", "CompilerGeneratedAttribute"));

  public static bool IsCompilerGenerated(this TypeReference tr) => tr.Resolve().IsCompilerGenerated();

  public static bool IsCoreLibraryType(this TypeReference tr) => tr.Scope == tr.Module.TypeSystem.CoreLibrary;

  public static bool IsCoreLibraryType(this TypeReference tr, string? ns) => tr.IsCoreLibraryType() && tr.IsNamed(ns);

  public static bool IsCoreLibraryType(this TypeReference tr, string? ns, string name)
    => tr.IsCoreLibraryType() && tr.IsNamed(ns, name);

  public static bool IsDecimalConstant(this FieldDefinition fd, out decimal? value) {
    if (fd is { IsStatic: true, IsInitOnly: true, HasCustomAttributes: true }) {
      foreach (var ca in fd.CustomAttributes) {
        if (ca.AttributeType.FullName == "System.Runtime.CompilerServices.DecimalConstantAttribute") {
          var valid = false;
          byte scale = 0;
          var negative = false;
          var lo = 0;
          var mid = 0;
          var hi = 0;
          if (ca.HasConstructorArguments && ca.ConstructorArguments.Count == 5) {
            var ts = fd.Module.TypeSystem;
            // 2 forms: one has int for lo/mid/hi, the other has uint
            if (ca.ConstructorArguments[0].Type == ts.Byte) {
              scale = (byte) ca.ConstructorArguments[0].Value;
              if (ca.ConstructorArguments[1].Type == ts.Byte) {
                negative = 0 != (byte) ca.ConstructorArguments[1].Value;
                if (ca.ConstructorArguments[2].Type == ts.Int32) {
                  hi = (int) ca.ConstructorArguments[2].Value;
                  if (ca.ConstructorArguments[3].Type == ts.Int32) {
                    mid = (int) ca.ConstructorArguments[3].Value;
                    if (ca.ConstructorArguments[4].Type == ts.Int32) {
                      lo = (int) ca.ConstructorArguments[4].Value;
                      valid = true;
                    }
                  }
                }
                else if (ca.ConstructorArguments[2].Type == ts.UInt32) {
                  hi = (int) (uint) ca.ConstructorArguments[2].Value;
                  if (ca.ConstructorArguments[3].Type == ts.UInt32) {
                    mid = (int) (uint) ca.ConstructorArguments[3].Value;
                    if (ca.ConstructorArguments[4].Type == ts.UInt32) {
                      lo = (int) (uint) ca.ConstructorArguments[4].Value;
                      valid = true;
                    }
                  }
                }
              }
            }
          }
          value = valid ? new decimal(lo, mid, hi, negative, scale) : null;
          return true;
        }
      }
    }
    value = null;
    return false;
  }

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

  public static bool IsReadOnly(this ICustomAttributeProvider? provider)
    => provider is not null && provider.HasCustomAttributes &&
       provider.CustomAttributes.Any(ca => ca.AttributeType.IsNamed("System.Runtime.CompilerServices", "IsReadOnlyAttribute"));

  public static bool IsRequired(this IMemberDefinition? md)
    => md is not null && md.HasCustomAttributes &&
       md.CustomAttributes.Any(ca => ca.AttributeType.IsNamed("System.Runtime.CompilerServices", "RequiredMemberAttribute"));

  public static bool IsScopedRef(this ParameterDefinition? pd)
    => pd is not null && pd.HasCustomAttributes &&
       pd.CustomAttributes.Any(ca => ca.AttributeType.IsNamed("System.Runtime.CompilerServices", "ScopedRefAttribute"));

  public static bool IsUnmanaged(this GenericParameter? gp)
    => gp is not null && gp.HasCustomAttributes &&
       gp.CustomAttributes.Any(ca => ca.AttributeType.IsNamed("System.Runtime.CompilerServices", "IsUnmanagedAttribute"));

  public static bool IsVoid(this TypeReference tr) => tr == tr.Module.TypeSystem.Void;

  public static string NonGenericName(this TypeReference tr) {
    var name = tr.Name;
    var backTick = name.IndexOf('`');
    return backTick >= 0 ? name.Substring(0, backTick) : name;
  }

  public static bool TryUnwrapNullable(this TypeReference tr, [NotNullWhen(true)] out TypeReference? unwrapped) {
    if (tr.Scope == tr.Module.TypeSystem.CoreLibrary) {
      if (tr is { IsGenericInstance: true, Namespace: "System", Name: "Nullable`1" }) {
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
