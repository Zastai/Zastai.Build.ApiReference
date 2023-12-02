﻿ namespace Zastai.Build.ApiReference;

internal abstract partial class CodeFormatter : IComparer<MethodDefinition> {

  public int Compare(MethodDefinition? x, MethodDefinition? y) {
    if (object.ReferenceEquals(x, y)) {
      return 0;
    }
    if (x is null) {
      return -1;
    }
    if (y is null) {
      return +1;
    }
    // Level 0: Put the constructors at the top.
    if (x.IsRuntimeSpecialName != y.IsRuntimeSpecialName) {
      return x.IsRuntimeSpecialName ? -1 : +1;
    }
    // Level 1: The method name.
    string xReturnType;
    string yReturnType;
    {
      var name1 = this.MethodName(x, out xReturnType);
      var name2 = this.MethodName(y, out yReturnType);
      // FIXME: Or should this use the invariant culture?
      var cmp = string.Compare(name1, name2, StringComparison.Ordinal);
      if (cmp != 0) {
        return cmp;
      }
    }
    // Level 2: Generic types.
    if (x.HasGenericParameters && y.HasGenericParameters) {
      using var walker1 = x.GenericParameters.GetEnumerator();
      using var walker2 = y.GenericParameters.GetEnumerator();
      while (walker1.MoveNext()) {
        if (!walker2.MoveNext()) {
          return +1;
        }
        // Assumption: the full name of the generic parameter is suitable as comparison/sort form.
        // FIXME: Or should this also use the stringification implemented by the formatter?
        var name1 = walker1.Current?.FullName;
        var name2 = walker2.Current?.FullName;
        // FIXME: Or should this use the invariant culture?
        var cmp = string.Compare(name1, name2, StringComparison.Ordinal);
        if (cmp != 0) {
          return cmp;
        }
      }
      if (walker2.MoveNext()) {
        return -1;
      }
    }
    else if (x.HasGenericParameters) {
      return +1;
    }
    else if (y.HasGenericParameters) {
      return -1;
    }
    // Level Three: Parameter types.
    if (x.HasParameters && y.HasParameters) {
      using var walker1 = x.Parameters.GetEnumerator();
      using var walker2 = y.Parameters.GetEnumerator();
      while (walker1.MoveNext()) {
        if (!walker2.MoveNext()) {
          return +1;
        }
        // This uses the formatter-specific idea of a type's string form. That also means that Int16 sorts after Int32 for the C#
        // formatter (because "short" follows "int").
        var type1 = walker1.Current?.ParameterType;
        var name1 = type1 is null ? null : this.TypeName(type1, x);
        var type2 = walker2.Current?.ParameterType;
        var name2 = type2 is null ? null : this.TypeName(type2, y);
        // FIXME: Or should this use the invariant culture?
        var cmp = string.Compare(name1, name2, StringComparison.Ordinal);
        if (cmp != 0) {
          return cmp;
        }
      }
      if (walker2.MoveNext()) {
        return -1;
      }
    }
    else if (x.HasParameters) {
      return +1;
    }
    else if (y.HasParameters) {
      return -1;
    }
    // FIXME: Are there other things to compare?
    // The return type _should not_ matter, but let's include that in the sequence just in case.
    // FIXME: Or should this use the invariant culture?
    return string.Compare(xReturnType, yReturnType, StringComparison.Ordinal);
  }

}
