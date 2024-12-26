// This provides bits of API present in .NET but not in .NET Framework/Standard.

#if !NET

using System.Diagnostics.CodeAnalysis;

namespace Zastai.Build.ApiReference {

  internal static class MissingApi {

    public static StringBuilder AppendJoin<T>(this StringBuilder sb, string? separator, IEnumerable<T> values)
      => sb.Append(string.Join(separator, values));

    public static bool TryGetValue<T>(this SortedSet<T> set, T equalValue, [MaybeNullWhen(false)] out T actualValue) {
      foreach (var item in set.GetViewBetween(equalValue, equalValue)) {
        actualValue = item;
        return true;
      }
      actualValue = default;
      return false;
    }

  }

}

// Special attributes used for nullability analysis.
namespace System.Diagnostics.CodeAnalysis {

  [AttributeUsage(AttributeTargets.Parameter)]
  [ExcludeFromCodeCoverage, DebuggerNonUserCode]
  internal sealed class MaybeNullWhenAttribute(bool returnValue) : Attribute {

    /// <summary>Gets the return value condition.</summary>
    public bool ReturnValue { get; } = returnValue;

  }

  [AttributeUsage(AttributeTargets.Parameter)]
  [ExcludeFromCodeCoverage, DebuggerNonUserCode]
  internal sealed class NotNullWhenAttribute(bool returnValue) : Attribute {

    public bool ReturnValue { get; } = returnValue;

  }

}

#endif
