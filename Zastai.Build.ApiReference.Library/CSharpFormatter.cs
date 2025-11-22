using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

using Mono.Cecil;

namespace Zastai.Build.ApiReference;

/// <summary>A class that will extract and format the public API for an assembly as plain C# code.</summary>
public class CSharpFormatter : CodeFormatter {

  /// <summary>
  /// A context for typename-related attribute lookups. These lookups include nullability (for nullable reference types),
  /// <c>dynamic</c> and native integers.
  /// </summary>
  /// <param name="main">The main source of custom attributes for this context.</param>
  /// <param name="method">The enclosing method for this context, if applicable.</param>
  /// <param name="type">The enclosing type for this context, if applicable.</param>
  /// <remarks>
  /// 3 things we care about are handled by attributes on the context:
  /// <list type="bullet">
  ///   <item>
  ///     <c>[Dynamic]</c>, to distinguish <c>dynamic</c> from <c>object</c>.
  ///     <list type="bullet">
  ///       <item>In simple/normal context this has no arguments.</item>
  ///       <item>
  ///         In a context with multiple types (<c>dynamic[]</c>, tuple, ...) it has an array with 1 <c>bool</c> argument per type.
  ///       </item>
  ///     </list>
  ///   </item>
  ///   <item>
  ///     <c>[NativeInteger]</c>, to distinguish <c>nint</c>/<c>nuint</c> from <c>IntPtr</c>/<c>UIntPtr</c>.
  ///     <list type="bullet">
  ///       <item>In simple/normal context this has no arguments.</item>
  ///       <item>In a context with multiple types it has an array with 1 bool argument per <c>IntPtr</c>/<c>UIntPtr</c>.</item>
  ///     </list>
  ///   </item>
  ///   <item>
  ///     <c>[Nullable]</c>, for nullable reference types.
  ///     <list type="bullet">
  ///       <item>Has an array with one byte argument per reference type or generic value type.</item>
  ///       <item>If all those bytes are the same, it can also have a single byte as value instead.</item>
  ///       <item>If not present, <c>[NullableContext]</c> is checked on enclosing method/types (always single value).</item>
  ///     </list>
  ///   </item>
  /// </list>
  /// Because the systems differ, we need to keep track of separate indexes for each case.
  /// </remarks>
  protected sealed class TypeNameContext(ICustomAttributeProvider? main, MethodDefinition? method = null,
                                         TypeDefinition? type = null) {

    /// <summary>The main source of custom attributes for this context.</summary>
    public readonly ICustomAttributeProvider? Main = main;

    /// <summary>The enclosing method for this context, if applicable.</summary>
    public readonly MethodDefinition? Method = method;

    /// <summary>The enclosing type for this context, if applicable.</summary>
    public readonly TypeDefinition? Type = type;

    /// <summary>The current value index for <c>[Dynamic]</c> attribute checks.</summary>
    public int DynamicIndex;

    /// <summary>The current value index for <c>[NativeInteger]</c> attribute checks.</summary>
    public int IntegerIndex;

    /// <summary>The current value index for <c>[Nullable]</c> attribute checks.</summary>
    public int NullableIndex;

  }

  /// <inheritdoc />
  protected override string AssemblyAttributeLine(string attribute) => $"[assembly: {attribute}]";

  private string Attributes(MethodDefinition md) {
    var sb = new StringBuilder();
    if (md.IsPublic) {
      sb.Append("public ");
    }
    else if (md.IsAssembly) {
      sb.Append("internal ");
    }
    else if (md.IsFamily) {
      sb.Append("protected ");
    }
    else if (md.IsFamilyAndAssembly) {
      sb.Append("private protected ");
    }
    else if (md.IsFamilyOrAssembly) {
      sb.Append("protected ");
      if (this.IncludeInternals) {
        sb.Append("internal ");
      }
    }
    else {
      sb.Append("/* unexpected accessibility */ ");
    }
    // FIXME: IsNewSlot is also set for the very first declaration in the hierarchy, so we can't usefully emit the 'new' specifier.
    if (md.IsStatic) {
      sb.Append("static ");
    }
    if (md.IsAbstract) {
      sb.Append("abstract ");
    }
    else if (md.IsFinal) {
      sb.Append("sealed ");
      if (md.IsVirtual) {
        sb.Append("override ");
      }
    }
    else if (md.IsVirtual) {
      // For some reason, static virtual methods in interfaces have IsReuseSlot set; that's currently the only situation where
      // static+virtual is valid, so we can just look at IsStatic to ignore the IsReuseSlot.
      var isOverride = md is { IsReuseSlot: true, IsStatic: false } || (md.IsNewSlot && md.HasCovariantReturn());
      sb.Append(isOverride ? "override " : "virtual ");
    }
    return sb.ToString();
  }

  /// <inheritdoc />
  protected override string Cast(TypeDefinition targetType, string value) {
    // FIXME: Does this need a context?
    return $"({this.TypeName(targetType)}) {value}";
  }

  /// <inheritdoc />
  protected override string CustomAttribute(CustomAttribute ca) {
    var sb = new StringBuilder();
    sb.Append(this.TypeName(ca.AttributeType));
    var arguments = new List<string>();
    if (ca.HasConstructorArguments) {
      arguments.AddRange(ca.ConstructorArguments.Select(this.CustomAttributeArgument));
    }
    arguments.AddRange(this.NamedCustomAttributeArguments(ca));
    if (arguments.Count > 0) {
      sb.Append('(').AppendJoin(", ", arguments).Append(')');
    }
    return sb.ToString();
  }

  /// <inheritdoc />
  protected override string CustomAttributeArgument(CustomAttributeArgument argument) {
    // Sometimes there's multiple levels of CustomAttributeArgument; mainly seems to be for cases where the argument is declared as
    // "object", where the outer CAA is of type System.Object and the inner one has the "real" argument type and value.
    while (argument.Value is CustomAttributeArgument caa) {
      argument = caa;
    }
    if (argument.Value is CustomAttributeArgument[] array) {
      var arguments = array.Select(this.CustomAttributeArgument);
      var sb = new StringBuilder();
      sb.Append("new ").Append(this.TypeName(argument.Type)).Append(" { ").AppendJoin(", ", arguments).Append(" }");
      return sb.ToString();
    }
    return this.Value(argument.Type, argument.Value);
  }

  /// <inheritdoc />
  protected override IEnumerable<string> CustomAttributes(ICustomAttributeProvider cap, int indent) {
    if (!cap.HasCustomAttributes) {
      yield break;
    }
    var sb = new StringBuilder();
    foreach (var attribute in this.CustomAttributes(cap.CustomAttributes)) {
      sb.Clear();
      sb.Append(' ', indent).Append('[').Append(attribute).Append(']');
      yield return sb.ToString();
    }
  }

  /// <summary>Formats custom attributes for inline use (like for method parameters).</summary>
  /// <param name="cap">The custom attribute provider.</param>
  /// <returns>The formatted custom attributes, all on one line (for inline use).</returns>
  protected virtual string CustomAttributesInline(ICustomAttributeProvider cap) {
    if (!cap.HasCustomAttributes) {
      return "";
    }
    var attributes = this.CustomAttributes(cap.CustomAttributes).ToList();
    if (attributes.Count == 0) {
      return "";
    }
    var sb = new StringBuilder();
    sb.Append('[').AppendJoin(", ", attributes).Append("] ");
    return sb.ToString();
  }

  /// <inheritdoc />
  protected override string EnumField(FieldDefinition fd, int indent, EnumFieldValueMode mode, int highestBit) {
    var sb = new StringBuilder();
    sb.Append(' ', indent);
    if (!fd.IsPublic) {
      sb.Append("/* not public */ ");
    }
    sb.Append(fd.Name);
    if (fd.IsLiteral) {
      sb.Append(" = ");
      if (!fd.HasConstant) {
        sb.Append("/* constant value missing */");
      }
      else {
        switch (mode) {
          case EnumFieldValueMode.Binary: {
            var width = 4 * (1 + (highestBit / 4));
#if NET8_0_OR_GREATER
            var format = $"B{width}";
            var text = fd.Constant switch {
              byte u8 => u8.ToString(format),
              int i32 => i32.ToString(format),
              long i64 => i64.ToString(format),
              sbyte i8 => i8.ToString(format),
              short i16 => i16.ToString(format),
              uint u32 => u32.ToString(format),
              ulong u64 => u64.ToString(format),
              ushort u16 => u16.ToString(format),
              _ => ""
            };
#else
            var text = fd.Constant switch {
              byte u8 => Convert.ToString(u8, 2).PadLeft(width, '0'),
              int i32 => Convert.ToString(i32, 2).PadLeft(width, '0'),
              long i64 => Convert.ToString(i64, 2).PadLeft(width, '0'),
              sbyte i8 => Convert.ToString(i8, 2).PadLeft(width, '0'),
              short i16 => Convert.ToString(i16, 2).PadLeft(width, '0'),
              uint u32 => Convert.ToString(u32, 2).PadLeft(width, '0'),
              ulong u64 => Convert.ToString((long) u64, 2).PadLeft(width, '0'),
              ushort u16 => Convert.ToString(u16, 2).PadLeft(width, '0'),
              _ => ""
            };
#endif
            if (text.Length != 0) {
              sb.Append("0b");
              for (var pos = 0; pos < text.Length; pos += 4) {
                if (pos > 0) {
                  sb.Append('_');
                }
                sb.Append(text, pos, 4);
              }
            }
            else {
              // should never happen, but fall back on the "normal" processing
              goto case EnumFieldValueMode.Integer;
            }
            break;
          }
          case EnumFieldValueMode.Character:
            if (fd.Constant is ushort value) {
              sb.Append(this.Literal((char) value));
            }
            else {
              // should never happen, but fall back on the "normal" processing
              goto case EnumFieldValueMode.Integer;
            }
            break;
          case EnumFieldValueMode.Hexadecimal: {
            var format = $"X{1 + (highestBit / 4)}";
            var text = fd.Constant switch {
              byte u8 => u8.ToString(format),
              int i32 => i32.ToString(format),
              long i64 => i64.ToString(format),
              sbyte i8 => i8.ToString(format),
              short i16 => i16.ToString(format),
              uint u32 => u32.ToString(format),
              ulong u64 => u64.ToString(format),
              ushort u16 => u16.ToString(format),
              _ => ""
            };
            if (text.Length != 0) {
              sb.Append("0x").Append(text);
            }
            else {
              // should never happen, but fall back on the "normal" processing
              goto case EnumFieldValueMode.Integer;
            }
            break;
          }
          case EnumFieldValueMode.Integer:
            // We expect only fixed-size integral values, and we don't want any casts, or even any suffixes (because either they're
            // all int, or the specific integer type is listed on the enum as a base type, making the interpretation unambiguous).
            sb.Append(fd.Constant switch {
              byte u8 => u8.ToString(CultureInfo.InvariantCulture),
              int i32 => i32.ToString(CultureInfo.InvariantCulture),
              long i64 => i64.ToString(CultureInfo.InvariantCulture),
              sbyte i8 => i8.ToString(CultureInfo.InvariantCulture),
              short i16 => i16.ToString(CultureInfo.InvariantCulture),
              uint u32 => u32.ToString(CultureInfo.InvariantCulture),
              ulong u64 => u64.ToString(CultureInfo.InvariantCulture),
              ushort u16 => u16.ToString(CultureInfo.InvariantCulture),
              _ => "/* unexpected field type */ " + this.Value(null, fd.Constant)
            });
            break;
        }
      }
    }
    else {
      sb.Append(" /* not a literal */");
    }
    sb.Append(',');
    return sb.ToString();
  }

  /// <inheritdoc />
  protected override string EnumValue(TypeDefinition enumType, string name) => $"{this.TypeName(enumType)}.{name}";

  /// <inheritdoc />
  protected override string Event(EventDefinition ed, int indent) {
    var sb = new StringBuilder();
    sb.Append(' ', indent)
      .Append(ed.AddMethod is not null ? this.Attributes(ed.AddMethod) : "/* no add-on method */ ")
      .Append("event ").Append(this.TypeName(ed.EventType, ed)).Append(' ').Append(ed.Name).Append(';');
    return sb.ToString();
  }

  /// <inheritdoc />
  protected override IEnumerable<string?> ExportedTypes(SortedDictionary<string, IDictionary<string, ExportedType>> exportedTypes) {
    var sb = new StringBuilder();
    foreach (var scope in exportedTypes) {
      yield return null;
      yield return this.LineComment($"Exported Types - {scope.Key}");
      yield return null;
      foreach (var et in scope.Value.Values) {
        sb.Clear();
        sb.Append("[assembly: TypeForwardedTo(typeof(").Append(this.TypeName(et)).Append("))]");
        yield return sb.ToString();
      }
    }
  }

  /// <inheritdoc />
  protected override string Field(FieldDefinition fd, int indent) {
    var sb = new StringBuilder();
    sb.Append(' ', indent);
    if (fd.IsPublic) {
      sb.Append("public ");
    }
    else if (fd.IsAssembly) {
      sb.Append("internal ");
    }
    else if (fd.IsFamily) {
      sb.Append("protected ");
    }
    else if (fd.IsFamilyAndAssembly) {
      sb.Append("private protected ");
    }
    else if (fd.IsFamilyOrAssembly) {
      sb.Append("protected ");
      if (this.IncludeInternals) {
        sb.Append("internal ");
      }
    }
    else {
      sb.Append("/* unexpected accessibility */ ");
    }
    if (fd.IsRequired()) {
      sb.Append("required ");
    }
    var isDecimalConstant = false;
    decimal? decimalConstantValue = null;
    if (fd.IsLiteral) {
      sb.Append("const ");
    }
    else if (fd.IsDecimalConstant(out decimalConstantValue)) {
      sb.Append("const ");
      isDecimalConstant = true;
    }
    else {
      if (fd.IsStatic) {
        sb.Append("static ");
      }
      if (fd.IsInitOnly) {
        sb.Append("readonly ");
      }
    }
    sb.Append(this.TypeName(fd.FieldType, fd)).Append(' ').Append(fd.Name);
    if (fd.IsLiteral || isDecimalConstant) {
      sb.Append(" = ");
      if (fd.IsLiteral) {
        sb.Append(fd.HasConstant ? this.Value(fd.FieldType, fd.Constant) : "/* constant value missing */");
      }
      else if (decimalConstantValue.HasValue) {
        sb.Append(this.Literal(decimalConstantValue.Value));
      }
      else {
        sb.Append("/* could not decode decimal constant */");
      }
    }
    sb.Append(';');
    return sb.ToString();
  }

  private static string GenericParameter(GenericParameter gp) {
    var sb = new StringBuilder();
    if (gp.IsCovariant) {
      sb.Append("out ");
    }
    else if (gp.IsContravariant) {
      sb.Append("in ");
    }
    sb.Append(gp.Name);
    return sb.ToString();
  }

  /// <inheritdoc />
  protected override string? GenericParameterConstraints(GenericParameter gp) {
    // Some constraints (like "class" and "new()") are stored as attributes.
    // Similarly, a [Nullable] seems to be used for the "notnull" constraint, and to distinguish between "class" and "class?".
    if (gp is { HasConstraints: false, HasReferenceTypeConstraint: false, HasDefaultConstructorConstraint: false }) {
      return null;
    }
    var sb = new StringBuilder();
    sb.Append("where ").Append(gp.Name).Append(" : ");
    var first = true;
    var isValueType = gp.HasNotNullableValueTypeConstraint;
    var isUnmanaged = isValueType && gp.IsUnmanaged();
    if (gp.HasReferenceTypeConstraint) {
      sb.Append("class");
      first = false;
    }
    if (gp.HasConstraints) {
      foreach (var gpc in gp.Constraints) {
        var ct = gpc.ConstraintType;
        if (first && isValueType) {
          if (isUnmanaged) {
            // Expectation: First constraint is on ValueType modified by UnmanagedType; if so, write that as "unmanaged"
            // Note that UnmanagedType is neither a core library type nor a locally synthesized one.
            if (ct is RequiredModifierType rmt && rmt.ElementType.IsNamed("System", "ValueType") &&
                rmt.ModifierType.IsNamed("System.Runtime.InteropServices", "UnmanagedType")) {
              sb.Append(this.CustomAttributesInline(gpc)).Append("unmanaged");
              first = false;
              isValueType = false;
              continue;
            }
          }
          else {
            // Expectation: First constraint is on ValueType; if so, write that as "struct"
            if (ct.IsNamed("System", "ValueType")) {
              sb.Append(this.CustomAttributesInline(gpc)).Append("struct");
              first = false;
              isValueType = false;
              continue;
            }
          }
        }
        if (!first) {
          sb.Append(", ");
        }
        sb.Append(this.CustomAttributesInline(gpc)).Append(this.TypeName(ct, gp));
        // FIXME: Does the method/type count as context for this? If so, how is the index chosen?
        if (gpc.GetNullability() == Nullability.Nullable) {
          sb.Append('?');
        }
        first = false;
      }
    }
    // These should have been issued above, but make sure
    if (isValueType) {
      if (!first) {
        sb.Append(", ");
      }
      sb.Append(isUnmanaged ? "unmanaged" : "struct");
      first = false;
    }
    if (gp is { HasDefaultConstructorConstraint: true, HasNotNullableValueTypeConstraint: false }) {
      if (!first) {
        sb.Append(", ");
      }
      sb.Append("new()");
    }
    if (gp.AllowByRefLikeConstraint) {
      if (!first) {
        sb.Append(", ");
      }
      sb.Append("allows ref struct");
    }
    return sb.ToString();
  }

  /// <summary>Formats a set of generic type parameters (or arguments).</summary>
  /// <param name="provider">The provider for the generic parameters (or arguments).</param>
  /// <param name="tnc">The applicable type name context.</param>
  /// <returns>The formatted generic parameters (enclosed in angle brackets), or the empty string if there aren't any.</returns>
  protected virtual string GenericParameters(IGenericParameterProvider provider, TypeNameContext tnc) {
    var sb = new StringBuilder();
    if (provider is IGenericInstance gi) {
      // FIXME: Can this be false? If so, is it an error? Or should it produce <>?
      if (gi.HasGenericArguments) {
        var arguments = new List<string>();
        foreach (var argument in gi.GenericArguments) {
          if (argument is GenericParameter gp) {
            arguments.Add(CSharpFormatter.GenericParameter(gp));
          }
          else {
            arguments.Add(this.TypeName(argument, tnc));
          }
        }
        sb.Append('<').AppendJoin(", ", arguments).Append('>');
      }
    }
    else if (provider.HasGenericParameters) {
      sb.Append('<').AppendJoin(", ", provider.GenericParameters.Select(CSharpFormatter.GenericParameter)).Append('>');
    }
    return sb.ToString();
  }

  /// <inheritdoc />
  protected override bool IsHandledBySyntax(ICustomAttribute ca) {
    var at = ca.AttributeType;
    if (at is null) {
      return false;
    }
    switch (at.Namespace) {
      case "System":
        switch (at.Name) {
          case "ParamArrayAttribute":
            // Mapped to "params" keyword on parameters.
            return true;
          case "ObsoleteAttribute":
            // A few very specific forms are syntax-related:
            // - [Obsolete("Types with embedded references are not supported in this version of your compiler.", true)]
            // - [Obsolete("Constructors of types with required members are not supported in this version of your compiler.", true)]
            if (ca.HasConstructorArguments && ca.ConstructorArguments.Count == 2) {
              if (ca.ConstructorArguments[1].Value is true) {
                var message = ca.ConstructorArguments[0].Value as string;
                return message is "Types with embedded references are not supported in this version of your compiler."
                  or "Constructors of types with required members are not supported in this version of your compiler.";
              }
            }
            break;
        }
        break;
      case "System.Runtime.CompilerServices":
        switch (at.Name) {
          case "AsyncStateMachineAttribute":
          case "CompilerGeneratedAttribute":
          case "IteratorStateMachineAttribute":
          case "ReferenceAssemblyAttribute":
            // Guaranteed not to be relevant to the API.
            return true;
          case "DecimalConstantAttribute":
            // Mapped to "const" keyword + initial value.
            return true;
          case "DynamicAttribute":
            // Mapped to "dynamic" keyword.
            return true;
          case "ExtensionAttribute":
            // Not relevant at assembly/type level (just flags presence of extension methods).
            // Mapped to "this" keyword on parameters.
            return true;
          case "IsByRefLikeAttribute":
            // Mapped to "ref" keyword.
            return true;
          case "IsReadOnlyAttribute":
            // Mapped to "readonly" keyword.
            return true;
          case "IsUnmanagedAttribute":
            // Mapped to "unmanaged" keyword.
            return true;
          case "NativeIntegerAttribute":
            // Mapped to "nint"/"nuint" keywords.
            return true;
          case "NonNullTypesAttribute":
          case "NullableAttribute":
          case "NullableContextAttribute":
          case "NullablePublicOnlyAttribute":
            return true;
          case "PreserveBaseOverridesAttribute":
            // Used to detect covariant return types.
            return true;
          case "RequiredMemberAttribute":
            // Dropped (for types) or mapped to "required" keyword (for fields/properties).
            return true;
          case "ScopedRefAttribute":
            // Mapped to "scoped" keyword.
            return true;
          case "TupleElementNamesAttribute":
            // Names extracted for use in tuple syntax.
            return true;
        }
        break;
    }
    return false;
  }

  /// <inheritdoc />
  protected override string LineComment(string comment) => $"// {comment}".TrimEnd();

  /// <inheritdoc />
  protected override string Literal(bool value) => value ? "true" : "false";

  /// <inheritdoc />
  protected override string Literal(byte value) => "(byte) " + value.ToString(CultureInfo.InvariantCulture);

  /// <inheritdoc />
  protected override string Literal(char value) {
    switch (value) {
      case '\0':
        return "'\\0'";
      case '\'':
        return "'\\''";
      case '\a':
        return "'\\a'";
      case '\b':
        return "'\\b'";
      case '\u001f': // \e in C#13
        return "'\\e'";
      case '\f':
        return "'\\f'";
      case '\n':
        return "'\\n'";
      case '\r':
        return "'\\r'";
      case '\t':
        return "'\\t'";
      case '\v':
        return "'\\v'";
    }
    if (char.IsLetterOrDigit(value) || char.IsNumber(value) || char.IsPunctuation(value) || char.IsSymbol(value) || value == ' ') {
      return $"'{value}'";
    }
    // Anything else is "unprintable" and will get its hex form.
    var numericValue = (ushort) value;
    return numericValue < 0x100 ? $"'\\x{numericValue:X2}'" : $"'\\u{numericValue:X4}'";
  }

  /// <inheritdoc />
  protected override string Literal(decimal value) => value switch {
    decimal.MaxValue => "decimal.MaxValue",
    decimal.MinValue => "decimal.MinValue",
    _ => value.ToString(CultureInfo.InvariantCulture) + 'M'
  };

  /// <inheritdoc />
  protected override string Literal(double value) => value switch {
    Math.E => "Math.E",
    Math.PI => "Math.PI",
#if NET
    Math.Tau => "Math.Tau",
#else
    6.283185307179586476925 => "Math.Tau",
#endif
    double.Epsilon => "double.Epsilon",
    double.MaxValue => "double.MaxValue",
    double.MinValue => "double.MinValue",
    double.NaN => "double.NaN",
    double.NegativeInfinity => "double.NegativeInfinity",
    double.PositiveInfinity => "double.PositiveInfinity",
    _ => value.ToString("G17", CultureInfo.InvariantCulture) + 'D'
  };

  /// <inheritdoc />
  protected override string Literal(float value) => value switch {
#if NET
    MathF.E => "MathF.E",
    MathF.PI => "MathF.PI",
    MathF.Tau => "MathF.Tau",
#else
    2.71828183F => "MathF.E",
    3.14159265F => "MathF.PI",
    6.283185307F => "MathF.Tau",
#endif
    float.Epsilon => "float.Epsilon",
    float.MaxValue => "float.MaxValue",
    float.MinValue => "float.MinValue",
    float.NaN => "float.NaN",
    float.NegativeInfinity => "float.NegativeInfinity",
    float.PositiveInfinity => "float.PositiveInfinity",
    _ => value.ToString("G9", CultureInfo.InvariantCulture) + 'F'
  };

  /// <inheritdoc />
  protected override string Literal(int value) => value.ToString(CultureInfo.InvariantCulture);

  /// <inheritdoc />
  protected override string Literal(long value) => value.ToString(CultureInfo.InvariantCulture) + "L";

  /// <inheritdoc />
  protected override string Literal(sbyte value) => "(sbyte) " + value.ToString(CultureInfo.InvariantCulture);

  /// <inheritdoc />
  protected override string Literal(short value) => "(short) " + value.ToString(CultureInfo.InvariantCulture);

  /// <inheritdoc />
  protected override string Literal(string value) {
    var sb = new StringBuilder();
    sb.Append('"');
    foreach (var c in value) {
      switch (c) {
        case '\0':
          sb.Append("\\0");
          break;
        case '\\':
          sb.Append(@"\\");
          break;
        case '\"':
          sb.Append("\\\"");
          break;
        case '\a':
          sb.Append("\\a");
          break;
        case '\b':
          sb.Append("\\b");
          break;
        case '\u001f': // \e in C#13
          sb.Append("\\e");
          break;
        case '\f':
          sb.Append("\\f");
          break;
        case '\n':
          sb.Append("\\n");
          break;
        case '\r':
          sb.Append("\\r");
          break;
        case '\v':
          sb.Append("\\v");
          break;
        default:
          if (char.IsControl(c)) {
            var codePoint = (int) c;
            sb.Append($"\\u{codePoint:X4}");
          }
          else {
            sb.Append(c);
          }
          break;
      }
    }
    sb.Append('"');
    return sb.ToString();
  }

  /// <inheritdoc />
  protected override string Literal(uint value) => value.ToString(CultureInfo.InvariantCulture) + "U";

  /// <inheritdoc />
  protected override string Literal(ulong value) => value.ToString(CultureInfo.InvariantCulture) + "UL";

  /// <inheritdoc />
  protected override string Literal(ushort value) => "(ushort) " + value.ToString(CultureInfo.InvariantCulture);

  /// <inheritdoc />
  protected override IEnumerable<string?> Method(MethodDefinition md, int indent) {
    foreach (var customAttribute in this.CustomAttributes(md, indent)) {
      yield return customAttribute;
    }
    var sb = new StringBuilder();
    if (md.MethodReturnType.HasCustomAttributes) {
      foreach (var attribute in this.CustomAttributes(md.MethodReturnType.CustomAttributes)) {
        sb.Append(' ', indent).Append("[return: ").Append(attribute).Append(']');
        yield return sb.ToString();
        sb.Clear();
      }
    }
    if (md.ImplAttributes != 0) {
      var flags = new List<string>();
      // Mono.Cecil's MethodImplAttributes does not currently match MethodImplOptions - some values missing, some added.
      // Similarly, we can't rely on reflecting over MethodImplOptions, because then this tool may not be aware of flags added by
      // later framework versions. So we need to maintain a hardcoded list of "known" flags here, using nameof() for all flags
      // that exist in all our supported target frameworks.
      var numericFlags = (uint) md.ImplAttributes;
      if ((numericFlags & 0x0004) != 0) {
        flags.Add("Unmanaged");
      }
      if ((numericFlags & 0x0008) != 0) {
        flags.Add("NoInlining");
      }
      if ((numericFlags & 0x0010) != 0) {
        flags.Add("ForwardRef");
      }
      if ((numericFlags & 0x0020) != 0) {
        flags.Add("Synchronized");
      }
      if ((numericFlags & 0x0040) != 0) {
        flags.Add("NoOptimization");
      }
      if ((numericFlags & 0x0080) != 0) {
        flags.Add("PreserveSig");
      }
      if ((numericFlags & 0x0100) != 0) {
        flags.Add("AggressiveInlining");
      }
      if ((numericFlags & 0x0200) != 0) {
        flags.Add("AggressiveOptimization");
      }
      if ((numericFlags & 0x1000) != 0) {
        flags.Add("InternalCall");
      }
      if (flags.Count > 0) {
        var options = typeof(MethodImplOptions).FullName + '.';
        sb.Append(' ', indent).Append('[')
          .Append(typeof(MethodImplAttribute).FullName).Append('(')
          .Append(options).AppendJoin(", " + options, flags)
          .Append(")]");
        yield return sb.ToString();
        sb.Clear();
      }
    }
    sb.Append(' ', indent).Append(this.Attributes(md));
    if (md.IsReadOnly()) {
      sb.Append("readonly ");
    }
    var methodName = this.MethodName(md, out var returnTypeName);
    if (returnTypeName.Length > 0) {
      sb.Append(returnTypeName).Append(' ');
    }
    sb.Append(methodName)
      .Append(this.GenericParameters(md, new TypeNameContext(md, md)))
      .Append(this.Parameters(md));
    var constraints = this.GenericParameterConstraints(md, indent + 2).ToList();
    if (constraints.Count == 0) {
      sb.Append(';');
    }
    else {
      constraints[constraints.Count - 1] += ';';
    }
    yield return sb.ToString();
    foreach (var constraint in constraints) {
      yield return constraint;
    }
  }

  /// <inheritdoc />
  protected override string MethodName(MethodDefinition md, out string returnTypeName) {
    returnTypeName = this.TypeName(md.ReturnType, md.MethodReturnType);
    if (md.IsRuntimeSpecialName) {
      // Runtime-Special Names
      if (md.Name is ".ctor" or ".cctor") {
        returnTypeName = "";
        return md.DeclaringType.NonGenericName();
      }
      return $"/* Unhandled RunTime-Special Name */ {md.Name}";
    }
    if (md.IsSpecialName) {
      // Other Special Names - these will probably look/work fine if not treated specially
      if (md.Name.StartsWith("op_")) {
        var sb = new StringBuilder();
        var op = md.Name.Substring(3);
        var fsharp = false;
        string prefix;
        string name;
        var infix = "";
        var suffix = "";
        // Also seen on regular methods; not entirely sure what it means, so there is no special handling for it as such. We just
        // need to make sure it's ignored in the context of this name lookup.
        if (op.EndsWith("$W")) {
          suffix = "$W";
          fsharp = true;
          op = op.Remove(op.Length - 2);
        }
        if (op.StartsWith("Checked")) {
          infix = "checked";
          op = op.Substring(7);
        }
        if (op is "Explicit" or "Implicit") {
          prefix = op is "Implicit" ? "implicit" : "explicit";
          name = returnTypeName;
          returnTypeName = "";
        }
        else {
          prefix = "";
          switch (op) {
            case "Addition":
            case "UnaryPlus":
              name = "+";
              break;
            case "AdditionAssignment":
              name = "+=";
              break;
            case "AddressOf":
              fsharp = true;
              name = "~&";
              break;
            case "Append":
              fsharp = true;
              name = "@";
              break;
            case "Assign":
              name = "=";
              break;
            case "BitwiseAnd":
              name = "&";
              break;
            case "BitwiseAndAssignment":
              name = "&=";
              break;
            case "BitwiseOr":
              name = "|";
              break;
            case "BitwiseOrAssignment":
              name = "|=";
              break;
            case "BooleanAnd":
              fsharp = true;
              name = "&&";
              break;
            case "BooleanOr":
              fsharp = true;
              name = "||";
              break;
            case "ColonEquals":
              fsharp = true;
              name = ":=";
              break;
            case "Comma":
              name = ",";
              break;
            case "ComposeLeft":
              fsharp = true;
              name = "<<";
              break;
            case "ComposeRight":
              fsharp = true;
              name = ">>";
              break;
            case "Concatenate":
              fsharp = true;
              name = "^";
              break;
            case "Cons":
              fsharp = true;
              name = "::";
              break;
            case "Decrement":
            case "DecrementAssignment":
              name = "--";
              break;
            case "Dereference":
              fsharp = true;
              name = "!";
              break;
            case "Division":
              name = "/";
              break;
            case "DivisionAssignment":
              name = "/=";
              break;
            case "Dynamic":
              fsharp = true;
              name = "?";
              break;
            case "DynamicAssignment":
              fsharp = true;
              name = "?<-";
              break;
            case "Equality":
              name = "==";
              break;
            case "ExclusiveOr":
              name = "^";
              break;
            case "ExclusiveOrAssignment":
              name = "^=";
              break;
            case "Exponent": // ECMA-335
            case "Exponentiation": // F#
              name = "**";
              break;
            case "False":
              name = "false";
              break;
            case "GreaterThan":
              name = ">";
              break;
            case "GreaterThanOrEqual":
              name = ">=";
              break;
            case "Increment":
            case "IncrementAssignment":
              name = "++";
              break;
            case "Inequality":
              name = "!=";
              break;
            case "IntegerAddressOf":
              fsharp = true;
              name = "~&&";
              break;
            case "LeftShift":
              name = "<<";
              break;
            case "LeftShiftAssignment":
              name = "<<=";
              break;
            case "LessThan":
              name = "<";
              break;
            case "LessThanOrEqual":
              name = "<=";
              break;
            case "LogicalAnd":
              name = "&&";
              break;
            case "LogicalNot":
              name = "!";
              break;
            case "LogicalOr":
              name = "||";
              break;
            case "MemberAccess":
              name = "->";
              break;
            case "Modulus":
              name = "%";
              break;
            case "ModulusAssignment":
              name = "%=";
              break;
            case "MultiplicationAssignment": // ECMA-335
            case "MultiplyAssignment": // F#
              name = "*=";
              break;
            case "Multiply":
              name = "*";
              break;
            case "Nil":
              fsharp = true;
              name = "[]";
              break;
            case "OnesComplement":
              name = "~";
              break;
            case "PipeLeft":
              fsharp = true;
              name = "<|";
              break;
            case "PipeLeft2":
              fsharp = true;
              name = "<||";
              break;
            case "PipeLeft3":
              fsharp = true;
              name = "<|||";
              break;
            case "PipeRight":
              fsharp = true;
              name = "|>";
              break;
            case "PipeRight2":
              fsharp = true;
              name = "||>";
              break;
            case "PipeRight3":
              fsharp = true;
              name = "|||>";
              break;
            case "PointerToMemberSelection":
              name = "->*";
              break;
            case "Quotation":
              fsharp = true;
              name = "<@ @>";
              break;
            case "QuotationUntyped":
              fsharp = true;
              name = "<@@ @@>";
              break;
            case "Range":
              fsharp = true;
              name = "..";
              break;
            case "RangeStep":
              fsharp = true;
              name = ".. ..";
              break;
            case "RightShift":
              name = ">>";
              break;
            case "RightShiftAssignment":
              name = ">>=";
              break;
            case "SignedRightShift":
              name = "/* signed */ >>";
              break;
            case "Subtraction":
            case "UnaryNegation":
              name = "-";
              break;
            case "SubtractionAssignment":
              name = "-=";
              break;
            case "True":
              name = "true";
              break;
            case "UnsignedRightShift":
              name = ">>>";
              break;
            case "UnsignedRightShiftAssignment":
              name = ">>>=";
              break;
            default:
              if (Constants.FSharpCustomOperatorPattern().IsMatch(op)) {
                fsharp = true;
                name = op.Replace("Amp", "&")
                         .Replace("At", "@")
                         .Replace("Bang", "!")
                         .Replace("Bar", "|")
                         .Replace("Comma", ",")
                         .Replace("Divide", "/")
                         .Replace("Dollar", "$")
                         .Replace("Dot", ".")
                         .Replace("Equals", "=")
                         .Replace("Greater", ">")
                         .Replace("Hat", "^")
                         .Replace("LBrack", "[")
                         .Replace("LParen", "(")
                         .Replace("Less", "<")
                         .Replace("Multiply", "*")
                         .Replace("Minus", "-")
                         .Replace("Percent", "%")
                         .Replace("Plus", "+")
                         .Replace("Qmark", "?")
                         .Replace("RBrack", "]")
                         .Replace("RParen", ")")
                         .Replace("Twiddle", "~");
              }
              else {
                name = $"/* TODO: Map Operator Correctly */ {op}";
              }
              break;
          }
        }
        if (prefix.Length > 0) {
          sb.Append(prefix).Append(' ');
        }
        sb.Append("operator ");
        if (infix.Length > 0) {
          sb.Append(infix).Append(' ');
        }
        if (fsharp) {
          sb.Append("F# { ").Append(name).Append(" }");
        }
        else {
          sb.Append(name);
        }
        sb.Append(suffix);
        return sb.ToString();
      }
      if (md.Name.StartsWith("|") && md.Name.EndsWith("|")) {
        // F# Active Pattern
        return $"F# match {md.Name}";
      }
      return $"/* Special Name */ {md.Name}";
    }
    return md.Name;
  }

  /// <inheritdoc />
  protected override string ModuleAttributeLine(string attribute) => $"[module: {attribute}]";

  /// <inheritdoc />
  protected override IEnumerable<string?> NamespaceHeader() {
    if (string.IsNullOrEmpty(this.CurrentNamespace)) {
      yield break;
    }
    yield return null;
    yield return $"namespace {this.CurrentNamespace};";
  }

  /// <inheritdoc />
  protected override string Null() => "null";

  /// <inheritdoc />
  protected override string Or() => "|";

  private string Parameter(ParameterDefinition pd) {
    var sb = new StringBuilder();
    sb.Append(this.CustomAttributesInline(pd));
    if (pd.IsScopedRef()) {
      sb.Append("scoped ");
    }
    if (pd.IsIn) {
      sb.Append("in ");
    }
    if (pd.IsOut) {
      sb.Append("out ");
    }
    if (pd.IsParamArray()) {
      sb.Append("params ");
    }
    sb.Append(this.TypeName(pd.ParameterType, pd)).Append(' ').Append(pd.Name);
    if (pd.HasDefault) {
      sb.Append(" = ");
      // FIXME: How to tell the difference between `default` and `new()`?
      if (!pd.HasConstant) {
        sb.Append("/* constant value missing */");
      }
      else if (pd.Constant is null && pd.ParameterType.IsValueType) {
        sb.Append("default");
      }
      else {
        sb.Append(this.Value(pd.ParameterType, pd.Constant));
      }
    }
    return sb.ToString();
  }

  private string Parameters(MethodDefinition md) {
    var sb = new StringBuilder();
    sb.Append('(');
    if (md.HasParameters) {
      // Detect extension methods
      if (md.HasCustomAttributes) {
        foreach (var ca in md.CustomAttributes) {
          if (ca.AttributeType.IsNamed("System.Runtime.CompilerServices", "ExtensionAttribute")) {
            sb.Append("this ");
            break;
          }
        }
      }
      sb.AppendJoin(", ", md.Parameters.Select(this.Parameter));
    }
    sb.Append(')');
    return sb.ToString();
  }

  private string Parameters(PropertyDefinition pd) {
    if (!pd.HasParameters) {
      return "";
    }
    var sb = new StringBuilder();
    // Assumption: only indexers have parameters, and they use []
    sb.Append('[').AppendJoin(", ", pd.Parameters.Select(this.Parameter)).Append(']');
    return sb.ToString();
  }

  /// <inheritdoc />
  protected override IEnumerable<string?> Property(PropertyDefinition pd, int indent) {
    foreach (var line in this.CustomAttributes(pd, indent)) {
      yield return line;
    }
    {
      var sb = new StringBuilder();
      sb.Append(' ', indent);
      if (pd.IsRequired()) {
        sb.Append("required ");
      }
      sb.Append(this.TypeName(pd.PropertyType, pd)).Append(' ');
      sb.Append(this.PropertyName(pd));
      sb.Append(this.Parameters(pd)).Append(" {");
      yield return sb.ToString();
    }
    foreach (var line in this.PropertyAccessor(pd.GetMethod.IfPublicApi(), indent + 2)) {
      yield return line;
    }
    foreach (var line in this.PropertyAccessor(pd.SetMethod.IfPublicApi(), indent + 2)) {
      yield return line;
    }
    if (pd.HasOtherMethods) {
      var sb = new StringBuilder();
      sb.Append(' ', indent + 2).Append("// unsupported: \"other methods\"");
      yield return sb.ToString();
    }
    {
      var sb = new StringBuilder();
      sb.Append(' ', indent).Append('}');
      yield return sb.ToString();
    }
  }

  private IEnumerable<string?> PropertyAccessor(MethodDefinition? method, int indent) {
    if (method is null) {
      yield break;
    }
    foreach (var line in this.CustomAttributes(method, indent)) {
      yield return line;
    }
    var sb = new StringBuilder();
    sb.Append(' ', indent).Append(this.Attributes(method));
    if (method.IsReadOnly()) {
      sb.Append("readonly ");
    }
    if (method.IsGetter) {
      sb.Append("get");
    }
    else if (method.IsSetter) {
      // For `init`, it's not an attribute on the setter method, but rather a "required modifier" on its return type (void).
      // A bit weird, but whatever.
      if (method.ReturnType is RequiredModifierType rmt && rmt.ElementType == rmt.Module.TypeSystem.Void &&
          rmt.ModifierType.IsNamed("System.Runtime.CompilerServices", "IsExternalInit")) {
        sb.Append("init");
      }
      else {
        sb.Append("set");
      }
    }
    sb.Append(';');
    yield return sb.ToString();
  }

  /// <inheritdoc />
  protected override string PropertyName(PropertyDefinition pd) {
    // FIXME: Or should this only be done when the type has [System.Reflection.DefaultMemberAttribute("Item")]?
    return pd is { HasParameters: true, Name: "Item" } ? "this" : pd.Name;
  }

  /// <inheritdoc />
  protected override IEnumerable<string?> Type(TypeDefinition td, int indent) {
    foreach (var line in this.CustomAttributes(td, indent)) {
      yield return line;
    }
    var sb = new StringBuilder();
    // This IL flag is written as an attribute in C# code. Do the same.
    if (td.IsSerializable) {
      sb.Append(' ', indent).Append('[').Append(typeof(SerializableAttribute).FullName).Append(']');
      yield return sb.ToString();
      sb.Clear();
    }
    sb.Append(' ', indent);
    if (td.IsPublic || td.IsNestedPublic) {
      sb.Append("public ");
    }
    else if (td.IsNestedAssembly || td.IsNotPublic) {
      sb.Append("internal ");
    }
    else if (td.IsNestedFamily) {
      sb.Append("protected ");
    }
    else if (td.IsNestedFamilyAndAssembly) {
      sb.Append("private protected ");
    }
    else if (td.IsNestedFamilyOrAssembly) {
      sb.Append("protected ");
      if (this.IncludeInternals) {
        sb.Append("internal ");
      }
    }
    else {
      sb.Append("/* unexpected accessibility */ ");
    }
    if (td.IsDelegate(out var invoke)) {
      sb.Append("delegate ");
      this.MethodName(invoke, out var returnTypeName);
      if (returnTypeName.Length > 0) {
        sb.Append(returnTypeName).Append(' ');
      }
      sb.Append(this.TypeName(td)).Append(this.Parameters(invoke));
      var constraints = this.GenericParameterConstraints(td, indent + 2).ToList();
      if (constraints.Count == 0) {
        sb.Append(';');
      }
      else {
        constraints[constraints.Count - 1] += ';';
      }
      yield return sb.ToString();
      sb.Clear();
      foreach (var constraint in constraints) {
        yield return constraint;
      }
      yield break;
    }
    if (td is { IsClass: true, IsAbstract: true, IsSealed: true }) {
      sb.Append("static ");
    }
    else if (td is { IsAbstract: true, IsInterface: false }) {
      sb.Append("abstract ");
    }
    else if (td is { IsSealed: true, IsValueType: false }) {
      sb.Append("sealed ");
    }
    if (td.IsEnum) {
      sb.Append("enum");
    }
    else if (td.IsInterface) {
      sb.Append("interface");
    }
    else if (td.IsValueType) {
      if (td.IsReadOnly()) {
        sb.Append("readonly ");
      }
      if (td.IsByRefLike()) {
        sb.Append("ref ");
      }
      sb.Append("struct");
    }
    else if (td.IsClass) {
      // TODO: Maybe detect delegates; but then what of explicitly written classes deriving from MultiCastDelegate?
      sb.Append("class");
    }
    else { // What else can it be?
      sb.Append($"/* type with unsupported classification: {td.Attributes} */");
    }
    // Note: The definition is NOT passed as context here (otherwise [NullableContext(2)] causes "public class Foo?").
    sb.Append(' ').Append(this.TypeName(td));
    {
      var baseType = td.BaseType;
      if (baseType is not null) {
        if (td.IsClass && baseType.IsCoreLibraryType("System", "Object")) {
          baseType = null;
        }
        else if (td.IsEnum && baseType.IsCoreLibraryType("System", "Enum")) {
          baseType = null;
        }
        else if (td.IsValueType && baseType.IsNamed("System", "ValueType")) {
          baseType = null;
        }
      }
      // For an enum, look for the special-named 'value__' field and use its type as the base type.
      if (baseType is null && td is { IsEnum: true, HasFields: true }) {
        foreach (var fd in td.Fields) {
          if (fd.IsSpecialName && fd.Name == "value__") {
            // If it's Int32, leave it off
            if (fd.FieldType != fd.Module.TypeSystem.Int32) {
              baseType = fd.FieldType;
            }
            break;
          }
        }
      }
      if (baseType is not null || td.HasInterfaces) {
        sb.Append(" : ");
      }
      if (baseType is not null) {
        // FIXME: Does this need a context?
        sb.Append(this.TypeName(baseType, td));
        if (td.HasInterfaces) {
          sb.Append(", ");
        }
      }
      if (td.HasInterfaces) {
        // Ensure these are emitted sorted
        var interfaces = new SortedDictionary<string, string>();
        foreach (var implementation in td.Interfaces) {
          var type = this.TypeName(implementation.InterfaceType, implementation, typeContext: td);
          interfaces.Add(type, this.CustomAttributesInline(implementation) + type);
        }
        sb.AppendJoin(", ", interfaces.Values);
      }
    }
    {
      var constraints = this.GenericParameterConstraints(td, indent + 2).ToList();
      if (constraints.Count == 0) {
        sb.Append(" {");
      }
      else {
        constraints[constraints.Count - 1] += " {";
      }
      yield return sb.ToString();
      sb.Clear();
      foreach (var constraint in constraints) {
        yield return constraint;
      }
    }
    foreach (var line in this.Fields(td, indent + 2)) {
      yield return line;
    }
    foreach (var line in this.Properties(td, indent + 2)) {
      yield return line;
    }
    foreach (var line in this.Events(td, indent + 2)) {
      yield return line;
    }
    foreach (var line in this.Methods(td, indent + 2)) {
      yield return line;
    }
    foreach (var line in this.NestedTypes(td, indent + 2)) {
      yield return line;
    }
    yield return null;
    sb.Append(' ', indent).Append('}');
    yield return sb.ToString();
  }

  /// <summary>Formats the type name for an exported type.</summary>
  /// <param name="et">The exported type.</param>
  /// <returns>The formatted type name.</returns>
  /// <remarks>
  /// There is no provision for generic parameters on <see cref="ExportedType"/>, so this will have the "ugly name" (<c>Foo`3</c>).
  /// </remarks>
  protected virtual string TypeName(ExportedType et) {
    var sb = new StringBuilder();
    if (et.DeclaringType is not null) {
      sb.Append(this.TypeName(et.DeclaringType)).Append('.');
    }
    else if (!string.IsNullOrEmpty(et.Namespace)) {
      sb.Append(et.Namespace).Append('.');
    }
    // FIXME: We could detect Foo`4 here and emit Foo<T1, T2, T3, T4>.
    sb.Append(et.Name);
    return sb.ToString();
  }

  /// <inheritdoc />
  protected override string TypeName(TypeReference tr, ICustomAttributeProvider? context = null,
                                     MethodDefinition? methodContext = null, TypeDefinition? typeContext = null) {
    var prefix = "";
    // ref X can only occur on outer types; can't have an array of `ref int` or a `Func<ref int>`, so handle that here
    if (tr is ByReferenceType brt) { // => ref T
      // omit the "ref" for "out" parameters - it's covered by the "out"
      if (context is not ParameterDefinition { IsOut: true }) {
        prefix = context.IsReadOnly() ? "ref readonly " : "ref ";
      }
      tr = brt.ElementType;
    }
    if (context is not null && methodContext is null && typeContext is null) {
      switch (context) {
        case EventDefinition ed:
          typeContext = ed.DeclaringType;
          break;
        case FieldDefinition fd:
          typeContext = fd.DeclaringType;
          break;
        case GenericParameter gp:
          methodContext = gp.DeclaringMethod?.Resolve();
          typeContext = gp.DeclaringType?.Resolve();
          break;
        case MethodDefinition md:
          methodContext = md;
          break;
        case MethodReturnType mrt:
          methodContext = mrt.Method as MethodDefinition;
          break;
        case ParameterDefinition pd:
          methodContext = pd.Method as MethodDefinition;
          break;
        case PropertyDefinition pd:
          typeContext = pd.DeclaringType;
          break;
        case TypeDefinition td:
          typeContext = td;
          break;
        default:
          prefix += $"/* FIXME: unhandled nullability context: {context.GetType().Name} */ ";
          break;
      }
    }
    return prefix + this.TypeName(tr, new TypeNameContext(context, methodContext, typeContext));
  }

  private string TypeName(TypeReference tr, TypeNameContext tnc) {
    var sb = new StringBuilder();
    Nullability? nullability = null;
    // Note: these checks used to use things like `IsArray` and then `GetElementType()` to get at the contents. However,
    // `GetElementType()` gets the _innermost_ element type (https://github.com/jbevain/cecil/issues/841). So given we need casts
    // anyway to access the `ElementType` property, and this isn't very performance-critical code, we just use pattern matching to
    // keep things readable.
    switch (tr) {
      case ArrayType at: { // => T[]
        // Any reference type, including an array, gets an entry in [Dynamic]
        ++tnc.DynamicIndex;
        nullability = tnc.Main?.GetNullability(tnc.Method, tnc.Type, tnc.NullableIndex++);
        var elementTypeName = this.TypeName(at.ElementType, tnc);
        sb.Append(elementTypeName).Append("[]");
        if (nullability == Nullability.Nullable) {
          sb.Append('?');
        }
        return sb.ToString();
      }
      case OptionalModifierType omt: {
        var type = omt.ElementType;
        var modifier = omt.ModifierType;
        // Actual meanings to be determined - put the modifier in a comment for now
        var mainType = this.TypeName(type, tnc);
        // FIXME: Does this need a context? Should it affect the indexes?
        var modifierType = this.TypeName(modifier);
        return $"{mainType} /* optionally modified by: {modifierType} */";
      }
      case PointerType pt: { // => T*
        // Assumption: a pointer cannot be nullable so does not take up a slot in the nullability info.
        var targetTypeName = this.TypeName(pt.ElementType, tnc);
        sb.Append(targetTypeName).Append('*');
        return sb.ToString();
      }
      case RequiredModifierType rmt: {
        var type = rmt.ElementType;
        var modifier = rmt.ModifierType;
        if (modifier.IsNamed("System.Runtime.InteropServices", "InAttribute") && type is ByReferenceType brt) {
          // This signals a `ref readonly xxx` return type
          sb.Append("ref readonly ");
          tr = brt.ElementType;
          break;
        }
        // Actual meanings to be determined - put the modifier in a comment for now
        var mainType = this.TypeName(type, tnc);
        // FIXME: Does this need a context? Should it affect the indexes?
        var modifierType = this.TypeName(modifier);
        return $"{mainType} /* modified by: {modifierType} */";
      }
    }
    // A nullability slot is used for every reference type and every generic value type.
    // Special Case: System.Void is not considered to be a value type, but also does not participate in nullability.
    if (tr.IsValueType) {
      // Assumption: both apply
      if (tr.IsGenericInstance || tr.HasGenericParameters) {
        ++tnc.NullableIndex;
      }
    }
    else if (!tr.IsVoid()) {
      nullability = tnc.Main?.GetNullability(tnc.Method, tnc.Type, tnc.NullableIndex++);
    }
    // Check for System.Nullable<T> and make it T?
    if (tr.TryUnwrapNullable(out var unwrapped)) {
      sb.Append(this.TypeName(unwrapped, tnc)).Append('?');
      return sb.ToString();
    }
    { // Check for specific framework types that have a keyword form
      var isBuiltinType = true;
      var ts = tr.Module.TypeSystem;
      if (tr == ts.Boolean) {
        sb.Append("bool");
      }
      else if (tr == ts.Byte) {
        sb.Append("byte");
      }
      else if (tr == ts.Char) {
        sb.Append("char");
      }
      else if (tr == ts.Double) {
        sb.Append("double");
      }
      else if (tr == ts.Int16) {
        sb.Append("short");
      }
      else if (tr == ts.Int32) {
        sb.Append("int");
      }
      else if (tr == ts.Int64) {
        sb.Append("long");
      }
      else if (tr == ts.IntPtr && (this.HasRuntimeFeature("NumericIntPtr") || tnc.Main.IsNativeInteger(tnc.IntegerIndex++))) {
        sb.Append("nint");
      }
      else if (tr == ts.SByte) {
        sb.Append("sbyte");
      }
      else if (tr == ts.Single) {
        sb.Append("float");
      }
      else if (tr == ts.String) {
        sb.Append("string");
      }
      else if (tr == ts.Object) {
        sb.Append(tnc.Main.IsDynamic(tnc.DynamicIndex) ? "dynamic" : "object");
      }
      else if (tr == ts.UInt16) {
        sb.Append("ushort");
      }
      else if (tr == ts.UInt32) {
        sb.Append("uint");
      }
      else if (tr == ts.UInt64) {
        sb.Append("ulong");
      }
      else if (tr == ts.UIntPtr && (this.HasRuntimeFeature("NumericIntPtr") || tnc.Main.IsNativeInteger(tnc.IntegerIndex++))) {
        sb.Append("nuint");
      }
      else if (tr == ts.Void) {
        sb.Append("void");
      }
      else if (tr.IsCoreLibraryType("System", "Decimal")) {
        sb.Append("decimal");
      }
      else {
        isBuiltinType = false;
      }
      if (isBuiltinType) {
        ++tnc.DynamicIndex;
        if (nullability == Nullability.Nullable) {
          sb.Append('?');
        }
        return sb.ToString();
      }
    }
    // Any type gets an entry in [Dynamic]
    ++tnc.DynamicIndex;
    // Check for C# tuples (i.e. System.ValueTuple)
    if (tr.IsGenericInstance && tr.IsNamed("System") && tr.Name.StartsWith("ValueTuple`")) {
      sb.Append('(');
      var element = 0;
      var elementNames = tnc.Main.GetTupleElementNames();
    moreGenericArguments:
      var genericArguments = ((GenericInstanceType) tr).GenericArguments;
      var item = 0;
      foreach (var ga in genericArguments) {
        if (++item == 8 && tr.Name == "ValueTuple`8") {
          // a 10-element tuple is an 8-element tuple where the 8th element is a 3-element tuple
          if (ga.IsGenericInstance && ga.IsNamed("System") && ga.Name.StartsWith("ValueTuple`")) {
            // skip this type
            ++tnc.DynamicIndex;
            ++tnc.NullableIndex;
            // switch to this one and continue processing
            tr = ga;
            goto moreGenericArguments;
          }
        }
        if (element > 0) {
          sb.Append(", ");
        }
        sb.Append(this.TypeName(ga, tnc));
        if (elementNames is not null) {
          if (element >= elementNames.Length) {
            sb.Append(" /* name missing */");
          }
          else {
            var elementName = elementNames[element];
            if (elementName is not null) {
              sb.Append(' ').Append(elementName);
            }
          }
        }
        ++element;
      }
      sb.Append(')');
      return sb.ToString();
    }
    if (tr is FunctionPointerType fpt) {
      // Formats:
      // - plain: delegate*<type, ..., return-type>
      // - unmanaged: delegate* unmanaged<type, ..., return-type>
      // - explicitly managed/unmanaged with calling convention modifiers: delegate* (un)managed[x, ...] <type, ..., return-type>
      // (the syntax also allows "managed" instead of "unmanaged", but that's not a separate flag, just the default).
      // Annoyingly, the return type is considered first for dynamic/integer/... index purposes, but the C# syntax has it at the
      // end in order to match Func<>. So for now we use slightly different syntax:
      // - delegate* <return-type> (un)managed[x, ...] <type, ...>
      sb.Append("delegate* ");
      var returnType = fpt.ReturnType;
      var callingConventions = new List<string>();
      if (fpt.CallingConvention == MethodCallingConvention.Unmanaged) {
        // look for (and drop) modopt(.CallConvXXX) on the return type, keeping the XXXs
        while (returnType is OptionalModifierType omt) {
          var modifier = omt.ModifierType;
          if (modifier.IsNamed("System.Runtime.CompilerServices") && modifier.Name.StartsWith("CallConv")) {
            callingConventions.Add(modifier.Name.Substring(8));
            returnType = omt.ElementType;
            continue;
          }
          break;
        }
      }
      var returnTypeAttributes = this.CustomAttributesInline(fpt.MethodReturnType);
      // This needs to be done right away to use the correct indexes, even though it appears near the end in the syntax
      var returnTypeName = this.TypeName(returnType, tnc);
      switch (fpt.CallingConvention) {
        case MethodCallingConvention.Default:
          sb.Append("managed");
          break;
        case MethodCallingConvention.C:
          sb.Append("unmanaged[Cdecl] ");
          break;
        case MethodCallingConvention.FastCall:
          sb.Append("unmanaged[Fastcall] ");
          break;
        case MethodCallingConvention.StdCall:
          sb.Append("unmanaged[Stdcall] ");
          break;
        case MethodCallingConvention.ThisCall:
          sb.Append("unmanaged[Thiscall] ");
          break;
        case MethodCallingConvention.Unmanaged:
          sb.Append("unmanaged");
          if (callingConventions.Count > 0) {
            sb.Append('[').AppendJoin(", ", callingConventions).Append("] ");
          }
          break;
        default:
          sb.Append($"unmanaged /* unhandled calling convention: {(int) fpt.CallingConvention} */ ");
          break;
      }
      sb.Append('<');
      if (fpt.HasParameters) {
        foreach (var parameter in fpt.Parameters) {
          var parameterType = parameter.ParameterType;
          var parameterTypeName = this.TypeName(parameterType, tnc);
          sb.Append(this.CustomAttributesInline(parameter)).Append(parameterTypeName).Append(", ");
        }
      }
      sb.Append(returnTypeAttributes).Append(returnTypeName).Append('>');
      // FIXME: Does nullability apply here?
      if (nullability == Nullability.Nullable) {
        sb.Append('?');
      }
      return sb.ToString();
    }
    // Otherwise, full stringification.
    if (tr.IsNested) {
      var declaringType = tr.DeclaringType;
      // Ignore enclosing types for qualification purposes
      for (var currentType = this.CurrentType; currentType is not null; currentType = currentType.DeclaringType) {
        if (declaringType == currentType) {
          declaringType = null;
          break;
        }
      }
      if (declaringType is not null) {
        // FIXME: Does this need a context? Should it affect the indexes?
        sb.Append(this.TypeName(tr.DeclaringType)).Append('.');
      }
    }
    else if (!string.IsNullOrEmpty(tr.Namespace) && tr.Namespace != this.CurrentNamespace) {
      sb.Append(tr.Namespace).Append('.');
    }
    sb.Append(tr.NonGenericName()).Append(this.GenericParameters(tr, tnc));
    if (nullability == Nullability.Nullable) {
      sb.Append('?');
    }
    return sb.ToString();
  }

  /// <inheritdoc />
  protected override string TypeOf(TypeReference tr) {
    // FIXME: Does this need a context?
    return $"typeof({this.TypeName(tr)})";
  }

}
