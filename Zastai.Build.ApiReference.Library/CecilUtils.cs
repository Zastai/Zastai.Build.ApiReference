using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

using Mono.Cecil;

namespace Zastai.Build.ApiReference;

/// <summary>Utility methods for working with <c>Mono.Cecil</c> elements.</summary>
internal static class CecilUtils {

  extension(AssemblyDefinition ad) {

    public HashSet<string> GetRuntimeFeatures() {
      var coreLib = ad.MainModule.TypeSystem.CoreLibrary;
      // FIXME: What this _should_ do is look up System.Runtime.CompilerServices.RuntimeFeature (in the context of coreLib), and
      //        enumerate all its fields, returning a set containing the names of any that are string constants.
      //        However, I have not been able to get that initial type lookup to work, and we currently only care about numeric
      //        IntPtr support, so we just check for .NET 7 or later.
      if (coreLib is AssemblyNameReference { Name: "System.Runtime" } anr && anr.Version >= Version.Parse("7.0")) {
        return ["NumericIntPtr"];
      }
      return [];
    }

  }

  extension(EventDefinition ed) {

    // FIXME: Can the remove method differ? Does it matter?
    public bool IsInternalApi => ed.AddMethod.IsInternalApi;

    // FIXME: Can the remove method differ? Does it matter?
    public bool IsPublicApi => ed.AddMethod.IsPublicApi;

  }

  extension(FieldDefinition fd) {

    public bool IsDecimalConstant(out decimal? value) {
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

    public bool IsInternalApi => fd.IsAssembly || fd.IsFamilyAndAssembly;

    public bool IsPublicApi => fd.IsPublic || fd.IsFamily || fd.IsFamilyOrAssembly;

  }

  extension(GenericParameter? gp) {

    public bool IsUnmanaged => gp.HasAttribute("System.Runtime.CompilerServices", "IsUnmanagedAttribute");

  }

  extension(ICustomAttributeProvider? cap) {

    public Nullability? GetNullability(MethodDefinition? context, int idx = 0)
      => cap?.GetNullability(idx) ?? context.GetNullabilityContext();

    public Nullability? GetNullability(MethodDefinition? methodContext, TypeDefinition? typeContext, int idx = 0)
      => cap?.GetNullability(idx) ?? methodContext.GetNullabilityContext() ?? typeContext.GetNullabilityContext();

    public Nullability? GetNullability(TypeDefinition? context, int idx = 0)
      => cap?.GetNullability(idx) ?? context.GetNullabilityContext();

    public Nullability? GetNullability(int idx = 0) {
      if (cap is not null && cap.HasCustomAttributes) {
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

    private Nullability? GetNullabilityContextInternal() {
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

    public string?[]? GetTupleElementNames() {
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

    public bool HasAttribute(string ns, string name)
      => cap is not null && cap.HasCustomAttributes && cap.CustomAttributes.Any(ca => ca.AttributeType.IsNamed(ns, name));

    public bool IsDynamic(int idx) {
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

    public bool IsMarkedAsExtension => cap.HasAttribute("System.Runtime.CompilerServices", "ExtensionAttribute");

    public bool IsNativeInteger(int idx) {
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

    public bool IsReadOnly => cap.HasAttribute("System.Runtime.CompilerServices", "IsReadOnlyAttribute");

  }

  extension(IMemberDefinition? md) {

    public bool IsRequired => md.HasAttribute("System.Runtime.CompilerServices", "RequiredMemberAttribute");

  }

  extension(MethodDefinition? md) {

    private Nullability? GetNullabilityContext() {
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

    public bool HasCovariantReturn => md.HasAttribute("System.Runtime.CompilerServices", "PreserveBaseOverridesAttribute");

    public MethodDefinition? IfPublicApi() => md.IsPublicApi ? md : null;

    public bool IsInternalApi => md is not null && (md.IsAssembly || md.IsFamilyAndAssembly);

    public bool IsPublicApi => md is not null && (md.IsPublic || md.IsFamily || md.IsFamilyOrAssembly);

  }

  extension(ParameterDefinition? pd) {

    public bool IsParamArray => pd.HasAttribute("System", "ParamArrayAttribute");

    public bool IsScopedRef => pd.HasAttribute("System.Runtime.CompilerServices", "ScopedRefAttribute");

  }

  extension(TypeDefinition? td) {

    private Nullability? GetNullabilityContext() {
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

    public bool IsByRefLike => td.HasAttribute("System.Runtime.CompilerServices", "IsByRefLikeAttribute");

    private bool IsCompilerGenerated => td.HasAttribute("System.Runtime.CompilerServices", "CompilerGeneratedAttribute");

    public bool IsDelegate([NotNullWhen(true)] out MethodDefinition? invoke) {
      invoke = null;
      if (td is null) {
        return false;
      }
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

    public bool IsExtensionBlock => td is { IsSpecialName: true, IsMarkedAsExtension: true } && td.Name.StartsWith("<G>");

    public bool IsInternalApi => td is not null && (td.IsNestedAssembly || td.IsNestedFamilyAndAssembly || td.IsNotPublic);

    public bool IsPublicApi
      => td is not null && (td.IsPublic || td.IsNestedPublic || td.IsNestedFamily || td.IsNestedFamilyOrAssembly);

  }

  extension(TypeReference tr) {

    public bool IsCompilerGenerated {
      get {
        return tr.Resolve().IsCompilerGenerated;
      }
    }

    public bool IsCoreLibraryType() => tr.Scope == tr.Module.TypeSystem.CoreLibrary;

    public bool IsCoreLibraryType(string? ns) => tr.IsCoreLibraryType() && tr.IsNamed(ns);

    public bool IsCoreLibraryType(string? ns, string name) => tr.IsCoreLibraryType() && tr.IsNamed(ns, name);

    public bool IsLocalType() => tr.Scope == tr.Module;

    public bool IsLocalType(string? ns) => tr.IsLocalType() && tr.IsNamed(ns);

    public bool IsLocalType(string? ns, string name) => tr.IsLocalType() && tr.IsNamed(ns, name);

    public bool IsModifiedType(TypeReference elementType, string? ns, string modifier)
      => tr is RequiredModifierType rmt && rmt.ElementType == elementType && rmt.ModifierType.IsNamed(ns, modifier);

    public bool IsModifiedType(string? ns1, string element, string? ns2, string modifier)
      => tr is RequiredModifierType rmt && rmt.ElementType.IsNamed(ns1, element) && rmt.ModifierType.IsNamed(ns2, modifier);

    public bool IsNamed(string? ns) => tr.Namespace == ns;

    public bool IsNamed(string? ns, string name) => tr.Namespace == ns && tr.Name == name;

    public bool IsVoid => tr == tr.Module.TypeSystem.Void;

    public string NonGenericName() {
      var name = tr.Name;
      var backTick = name.IndexOf('`');
      return backTick >= 0 ? name.Substring(0, backTick) : name;
    }

    public bool TryUnwrapNullable([NotNullWhen(true)] out TypeReference? unwrapped) {
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

}
