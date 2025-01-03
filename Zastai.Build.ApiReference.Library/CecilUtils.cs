﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

using Mono.Cecil;

namespace Zastai.Build.ApiReference;

/// <summary>Utility methods for working with Mono.Cecil elements.</summary>
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

  public static HashSet<string> GetRuntimeFeatures(this AssemblyDefinition ad) {
    var coreLib = ad.MainModule.TypeSystem.CoreLibrary;
    // FIXME: What this _should_ do is look up System.Runtime.CompilerServices.RuntimeFeature (in the context of coreLib), and
    //        enumerate all its fields, returning a set containing the names of any that are string constants.
    //        However, I have not been able to get that initial type lookup to work, and we currently only care about numeric IntPtr
    //        support, so we just check for .NET 7 or later.
    if (coreLib is AssemblyNameReference { Name: "System.Runtime" } anr && anr.Version >= Version.Parse("7.0")) {
      return ["NumericIntPtr"];
    }
    return [];
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

  public static bool IsDelegate(this TypeDefinition td, [NotNullWhen(true)] out MethodDefinition? invoke) {
    invoke = null;
    // They have no contents other than methods.
    if (td.HasEvents || td.HasFields || td.HasInterfaces || !td.HasMethods || td.HasNestedTypes || td.HasProperties) {
      return false;
    }
    // Base is always System.MulticastDelegate
    if (td.BaseType is null || !td.BaseType.IsCoreLibraryType(nameof(System), nameof(MulticastDelegate))) {
      return false;
    }
    // Further checks that _could_ be added, if it turns out there's cases where built assemblies contain subclasses of
    // MulticastDelegate that are not actually delegate types:
    // - class size seems to always be -1
    // - class attributes are sealed + visibility
    // - exactly 4 methods:
    //   - constructor with 2 arguments: an object called "object" and a nint/IntPtr called "method"
    //   - Invoke() with the delegate's signature (return type and arguments)
    //   - BeginInvoke() taking the in/ref/out arguments of Invoke() plus an AsyncCallback called "callback" and an object called
    //     "object"; returns IAsyncResult.
    //   - EndInvoke() returning Invoke()'s return type and taking its ref/out arguments plus an IAsyncResult called "result".
    //   - fixed attributes: Public+HideBySig+SpecialName+RTSpecialName for the constructor;
    //                       Public+MethodAttributes.Virtual+MethodAttributes.HideBySig+MethodAttributes.NewSlot for the others
    invoke = td.Methods.FirstOrDefault(md => md.Name == "Invoke");
    return invoke is not null;
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

  // FIXME: Can the remove method differ? Does it matter?
  public static bool IsInternalApi(this EventDefinition ed) => ed.AddMethod.IsInternalApi();

  public static bool IsInternalApi(this FieldDefinition fd) => fd.IsAssembly || fd.IsFamilyAndAssembly;

  public static bool IsInternalApi(this MethodDefinition md) => md.IsAssembly || md.IsFamilyAndAssembly;

  public static bool IsInternalApi(this TypeDefinition td) => td.IsNestedAssembly || td.IsNestedFamilyAndAssembly || td.IsNotPublic;

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
