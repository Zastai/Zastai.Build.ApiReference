// This defines some of the special attributes used for nullability analysis that exist in .NET Core but not .NET Framework.

#if NETFRAMEWORK

namespace System.Diagnostics.CodeAnalysis;

[AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
[ExcludeFromCodeCoverage, DebuggerNonUserCode]
internal sealed class NotNullWhenAttribute : Attribute {

  public NotNullWhenAttribute(bool returnValue) {
    this.ReturnValue = returnValue;
  }

  public bool ReturnValue { get; }

}

#endif
