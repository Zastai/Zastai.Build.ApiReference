// This provides bits of API present in .NET Core but not in .NET Framework.

#if NETFRAMEWORK

namespace Zastai.Build.ApiReference {

  internal static class MissingApi {

    public static StringBuilder AppendJoin<T>(this StringBuilder sb, string? separator, IEnumerable<T> values)
      => sb.Append(string.Join(separator, values));

  }

}

// Special attributes used for nullability analysis.
namespace System.Diagnostics.CodeAnalysis {

  [AttributeUsage(AttributeTargets.Parameter)]
  [ExcludeFromCodeCoverage, DebuggerNonUserCode]
  internal sealed class NotNullWhenAttribute : Attribute {

    public NotNullWhenAttribute(bool returnValue) {
      this.ReturnValue = returnValue;
    }

    public bool ReturnValue { get; }

  }

}

#endif
