using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

using Mono.Cecil;

namespace Zastai.Build.ApiReference;

/// <summary>A class that will extract and format the public API for an assembly.</summary>
public abstract partial class CodeFormatter {

  /// <summary>The name of the namespace currently being processed.</summary>
  protected string? CurrentNamespace { get; private set; }

  /// <summary>The type definition currently being processed.</summary>
  protected TypeDefinition? CurrentType { get; private set; }

  /// <summary>The number of spaces to be used to indent a top-level type.</summary>
  protected virtual int TopLevelTypeIndent => 0;

  private readonly HashSet<string> _attributesToExclude = [];

  private readonly HashSet<string> _attributesToInclude = [];

  private bool _binaryEnumsEnabled;

  private bool _charEnumsEnabled;

  private bool _hexEnumsEnabled;

  private HashSet<string>? _runtimeFeatures;

  /// <summary>Produces the lines to use as a footer for the section containing assembly-level attributes.</summary>
  /// <param name="ad">The assembly definition whose attributes have just been formatted.</param>
  /// <returns>The lines to use as a footer for the section containing assembly-level attributes.</returns>
  protected virtual IEnumerable<string?> AssemblyAttributeFooter(AssemblyDefinition ad) => [];

  /// <summary>Produces the lines to use as a header for the section containing assembly-level attributes.</summary>
  /// <param name="ad">The assembly definition whose attributes are about to be formatted.</param>
  /// <returns>The lines to use as a header for the section containing assembly-level attributes.</returns>
  protected virtual IEnumerable<string?> AssemblyAttributeHeader(AssemblyDefinition ad) {
    yield return null;
    yield return this.LineComment("Assembly Attributes");
    yield return null;
  }

  /// <summary>Produces a single line for the section containing assembly-level attributes.</summary>
  /// <param name="attribute">The formatted attribute name plus constructor arguments and properties, if specified.</param>
  /// <returns>A single line for the section containing assembly-level attributes.</returns>
  protected abstract string AssemblyAttributeLine(string attribute);

  /// <summary>Formats a cast of a value to a particular type.</summary>
  /// <param name="targetType">The target type for the case.</param>
  /// <param name="value">The formatted value being cast to <paramref name="targetType"/>.</param>
  /// <returns>The formatted cast.</returns>
  protected abstract string Cast(TypeDefinition targetType, string value);

  /// <summary>
  /// Clears any attribute exclusion/inclusion patterns previously set up via <see cref="ExcludeCustomAttributes"/> and/or
  /// <see cref="IncludeCustomAttributes"/>.
  /// </summary>
  public void ClearCustomAttributePatterns() {
    this._attributesToInclude.Clear();
    this._attributesToExclude.Clear();
  }

  /// <summary>Formats a custom attribute.</summary>
  /// <param name="ca">The custom attribute to format.</param>
  /// <returns>The formatted custom attribute (its name, plus constructor arguments and properties, if specified).</returns>
  protected abstract string CustomAttribute(CustomAttribute ca);

  /// <summary>Formats an unnamed custom attribute argument (i.e. a constructor argument).</summary>
  /// <param name="value">The attribute argument to format.</param>
  /// <returns>The formatted custom attribute argument.</returns>
  protected abstract string CustomAttributeArgument(CustomAttributeArgument value);

  private IEnumerable<string?> CustomAttributes(AssemblyDefinition ad) {
    if (!ad.HasCustomAttributes) {
      yield break;
    }
    var attributes = this.CustomAttributes(ad.CustomAttributes).ToList();
    if (attributes.Count > 0) {
      foreach (var line in this.AssemblyAttributeHeader(ad)) {
        yield return line;
      }
      foreach (var attribute in attributes) {
        yield return this.AssemblyAttributeLine(attribute);
      }
      foreach (var line in this.AssemblyAttributeFooter(ad)) {
        yield return line;
      }
    }
  }

  /// <summary>Formats a set of custom attributes.</summary>
  /// <param name="cap">The source of the custom attributes.</param>
  /// <param name="indent">The number of spaces of indentation to use.</param>
  /// <returns>The formatted custom attributes (if any were retained), one per line.</returns>
  protected abstract IEnumerable<string> CustomAttributes(ICustomAttributeProvider cap, int indent);

  /// <summary>Formats a set of custom attributes.</summary>
  /// <param name="attributes">The custom attributes to format.</param>
  /// <returns>
  /// The formatted custom attributes (if any were retained), one per line. Note that these lines contain the result of
  /// <see cref="CustomAttribute"/>, so just the attribute name and its arguments, without any language-specific syntax to mark that
  /// as an attribute declaration.
  /// </returns>
  protected IEnumerable<string> CustomAttributes(IEnumerable<CustomAttribute> attributes) {
    // Sort by the (full) type name; unfortunately, I'm not sure how to sort duplicates in a stable way.
    var sortedAttributes = new SortedDictionary<string, List<CustomAttribute>>();
    foreach (var ca in attributes) {
      if (!this.Retain(ca)) {
        continue;
      }
      var attributeType = ca.AttributeType.FullName;
      if (!sortedAttributes.TryGetValue(attributeType, out var list)) {
        sortedAttributes.Add(attributeType, list = []);
      }
      list.Add(ca);
    }
    if (sortedAttributes.Count == 0) {
      yield break;
    }
    // Now process them
    foreach (var list in sortedAttributes.Values) {
      foreach (var attribute in list) {
        yield return this.CustomAttribute(attribute);
      }
    }
  }

  private IEnumerable<string?> CustomAttributes(ModuleDefinition md) {
    if (!md.HasCustomAttributes) {
      yield break;
    }
    var attributes = this.CustomAttributes(md.CustomAttributes).ToList();
    if (attributes.Count > 0) {
      foreach (var line in this.ModuleAttributeHeader(md)) {
        yield return line;
      }
      foreach (var attribute in attributes) {
        yield return this.ModuleAttributeLine(attribute);
      }
      foreach (var line in this.ModuleAttributeFooter(md)) {
        yield return line;
      }
    }
  }

  /// <summary>
  /// Configures the use of binary literals for the values of enums marked with <see cref="FlagsAttribute"/> (instead of
  /// regular integer literals). If hex literals are enabled as well (via <see cref="EnableHexEnums"/>), binary literals will still
  /// be used.
  /// </summary>
  /// <param name="yes">
  /// Indicates whether binary literals should be used for the values of enums marked with <see cref="FlagsAttribute"/>.
  /// </param>
  public void EnableBinaryEnums(bool yes) => this._binaryEnumsEnabled = yes;

  /// <summary>
  /// Configures the use of character literals for the values of enums based on <see cref="UInt16"/> (instead of
  /// regular integer literals), when all values are suitable (digits, letters, punctuation, symbols, or a subset of whitespace).
  /// </summary>
  /// <param name="yes">
  /// Indicates whether character literals should be used for the values of enums based on <see cref="UInt16"/>, when all values
  /// are suitable (digits, letters, punctuation, symbols, or a subset of whitespace).
  /// </param>
  public void EnableCharEnums(bool yes) => this._charEnumsEnabled = yes;

  /// <summary>
  /// Configures the use of hexadecimal literals for the values of enums marked with <see cref="FlagsAttribute"/> (instead of
  /// regular integer literals). If binary literals are enabled as well (via <see cref="EnableBinaryEnums"/>), binary literals will
  /// be used.
  /// </summary>
  /// <param name="yes">
  /// Indicates whether hexadecimal literals should be used for the values of enums marked with <see cref="FlagsAttribute"/>.
  /// </param>
  public void EnableHexEnums(bool yes) => this._hexEnumsEnabled = yes;

  /// <summary>Formats a field that is declared by an enum.</summary>
  /// <param name="fd">The field to format.</param>
  /// <param name="indent">The number of spaces of indentation to use.</param>
  /// <param name="mode">The applicable formatting mode.</param>
  /// <param name="highestBit">The highest bit (zero-based) set in the values for all fields in the enum.</param>
  /// <returns></returns>
  protected abstract string EnumField(FieldDefinition fd, int indent, EnumFieldValueMode mode, int highestBit);

  /// <summary>Formats a (named) value of an enum type.</summary>
  /// <param name="enumType">The enum type.</param>
  /// <param name="name">The name of the enum value.</param>
  /// <returns>The formatted enum value.</returns>
  protected abstract string EnumValue(TypeDefinition enumType, string name);

  /// <summary>Formats an event.</summary>
  /// <param name="ed">The event to format.</param>
  /// <param name="indent">The number of spaces of indentation to use.</param>
  /// <returns>The formatted event (one line).</returns>
  protected abstract string Event(EventDefinition ed, int indent);

  /// <summary>Formats the events declared by a type.</summary>
  /// <param name="td">The type to process.</param>
  /// <param name="indent">The number of spaces of indentation to use.</param>
  /// <returns>The formatted events (one per line).</returns>
  protected IEnumerable<string?> Events(TypeDefinition td, int indent) {
    if (!td.HasEvents) {
      yield break;
    }
    var events = new SortedDictionary<string, EventDefinition>();
    foreach (var ed in td.Events) {
      if (!this.ShouldInclude(ed)) {
        continue;
      }
      // Assumption: no overloads for these
      if (events.TryGetValue(ed.Name, out var previousEvent)) {
        Trace.Fail(ed.ToString(), $"Multiply defined event in {td}; previous was {previousEvent}.");
      }
      events.Add(ed.Name, ed);
    }
    if (events.Count == 0) {
      yield break;
    }
    foreach (var ed in events.Values) {
      yield return null;
      foreach (var line in this.CustomAttributes(ed, indent)) {
        yield return line;
      }
      yield return this.Event(ed, indent);
    }
  }

  private IEnumerable<string?> ExportedTypes(AssemblyDefinition ad) {
    var exportedTypes = new SortedDictionary<string, IDictionary<string, ExportedType>>();
    foreach (var md in ad.Modules) {
      if (!md.HasExportedTypes) {
        continue;
      }
      foreach (var et in md.ExportedTypes) {
        string key;
        if (et.Scope is AssemblyNameReference anr) {
          key = $"{anr.Name} (v{anr.Version})";
        }
        else {
          key = et.Scope.Name;
        }
        if (!exportedTypes.TryGetValue(key, out var types)) {
          exportedTypes.Add(key, types = new SortedDictionary<string, ExportedType>());
        }
        types.Add(et.FullName, et);
      }
    }
    return exportedTypes.Count == 0 ? [] : this.ExportedTypes(exportedTypes);
  }

  /// <summary>Adds patterns indicating attributes that should be excluded from the output.</summary>
  /// <param name="patterns">
  /// The patterns to exclude (using simple shell wildcards: <c>*</c> or <c>?</c>). Note that matches are against the internal name
  /// of the attribute, so <c>Foo/BarAttribute`1</c> for a <c>BarAttribute&lt;T&gt;</c> attribute type nested in a <c>Foo</c> class.
  /// </param>
  public void ExcludeCustomAttributes(IEnumerable<string> patterns) {
    foreach (var pattern in patterns) {
      if (string.IsNullOrWhiteSpace(pattern)) {
        continue;
      }
      this._attributesToExclude.Add(pattern.Trim());
    }
  }

  /// <summary>Formats the types exported by an assembly.</summary>
  /// <param name="exportedTypes">The exported types (grouped by their scope).</param>
  /// <returns>The formatted exported types.</returns>
  protected abstract IEnumerable<string?> ExportedTypes(SortedDictionary<string, IDictionary<string, ExportedType>> exportedTypes);

  /// <summary>Formats a field.</summary>
  /// <param name="fd">The field to format.</param>
  /// <param name="indent">The number of spaces of indentation to use.</param>
  /// <returns>The formatted field (one line).</returns>
  protected abstract string Field(FieldDefinition fd, int indent);

  /// <summary>Formats the fields declared by a type.</summary>
  /// <param name="td">The type to process.</param>
  /// <param name="indent">The number of spaces of indentation to use.</param>
  /// <returns>The formatted fields (one per line).</returns>
  protected IEnumerable<string?> Fields(TypeDefinition td, int indent) {
    if (!td.HasFields) {
      yield break;
    }
    var fields = new SortedDictionary<string, FieldDefinition>();
    foreach (var field in td.Fields) {
      if (!this.ShouldInclude(field)) {
        continue;
      }
      if (fields.TryGetValue(field.Name, out var previousField)) {
        Trace.Fail(field.ToString(), $"Multiply defined field in {td}; previous was {previousField}.");
      }
      fields.Add(field.Name, field);
    }
    if (fields.Count == 0) {
      yield break;
    }
    if (td.IsEnum) {
      yield return null;
      // First pass: determine the processing mode for the values.
      var canUseBinary = false;
      var canUseCharacters = this._charEnumsEnabled;
      var canUseHex = false;
      var highestBit = 0;
      // Currently, only use binary or hex mode for [Flags] enums.
      if ((this._binaryEnumsEnabled || this._hexEnumsEnabled) && td.HasCustomAttributes) {
        foreach (var ca in td.CustomAttributes) {
          var at = ca.AttributeType;
          if (at.Scope == at.Module.TypeSystem.CoreLibrary && at is { Namespace: "System", Name: "FlagsAttribute" }) {
            // Character values never makes sense for [Flags] enums.
            canUseCharacters = false;
            canUseBinary = this._binaryEnumsEnabled;
            canUseHex = this._hexEnumsEnabled;
            break;
          }
        }
      }
      // If both binary and hex are possible and requested, choose binary.
      if (canUseBinary && canUseHex) {
        canUseHex = false;
      }
      foreach (var fd in fields.Values) {
        // Skip anything that is not an actual enum constant field.
        if (fd.IsSpecialName || !fd.HasConstant || !fd.IsLiteral) {
          continue;
        }
        if (canUseCharacters) {
          // Only enums with 'ushort' as base type are currently considered candidates for character interpretation.
          if (fd.Constant is not ushort constant) {
            canUseCharacters = false;
          }
          else {
            var c = (char) constant;
            // FIXME: Do we want to include the Number category too?
            if (!char.IsLetterOrDigit(c) && !char.IsPunctuation(c) && !char.IsSymbol(c)) {
              // Specific other values we allow. This specifically does not include "uncommon" escapes (\a, \b, \f and \v).
              if (" \0\n\r\t".IndexOf(c) < 0) {
                // Anything else is Bad(tm).
                canUseCharacters = false;
              }
            }
          }
        }
        if (canUseBinary || canUseHex) {
          try {
            var binary = fd.Constant switch {
              byte u8 => Convert.ToString(u8, 2),
              int i32 => Convert.ToString(i32, 2),
              long i64 => Convert.ToString(i64, 2),
              sbyte i8 => Convert.ToString(i8, 2),
              short i16 => Convert.ToString(i16, 2),
              uint u32 => Convert.ToString(u32, 2),
              ulong u64 => Convert.ToString(unchecked((long) u64), 2),
              ushort u16 => Convert.ToString(u16, 2),
              _ => ""
            };
            highestBit = Math.Max(highestBit, binary.Length - 1);
            if (binary.Length == 0) {
              canUseBinary = canUseHex = false;
            }
            else if (canUseHex) {
              // We want to allow zero, plus either values with a single bit set:
              //   0x0001, 0x0200, 0x8000
              // Or with a contiguous set of bits set (i.e. a "mask"):
              //   0x00FF, 0x03F0, 0x0F80
              // It seems like the easiest (if not the fastest) way to detect these, is to format as binary, trim trailing zeroes
              // and then check whether any zeroes remain.
              if (binary.TrimEnd('0').Contains('0')) {
                canUseHex = false;
              }
            }
          }
          catch {
            canUseBinary = canUseHex = false;
          }
        }
      }
      var mode = EnumFieldValueMode.Integer;
      if (canUseBinary) {
        mode = EnumFieldValueMode.Binary;
      }
      else if (canUseCharacters) {
        mode = EnumFieldValueMode.Character;
      }
      else if (canUseHex) {
        mode = EnumFieldValueMode.Hexadecimal;
      }
      // Second pass
      foreach (var fd in fields.Values) {
        if (fd.IsSpecialName) {
          // skip value__
          continue;
        }
        foreach (var line in this.CustomAttributes(fd, indent)) {
          yield return line;
        }
        yield return this.EnumField(fd, indent, mode, highestBit);
      }
    }
    else {
      foreach (var fd in fields.Values) {
        yield return null;
        foreach (var line in this.CustomAttributes(fd, indent)) {
          yield return line;
        }
        yield return this.Field(fd, indent);
      }
    }
  }

  /// <summary>Produces the lines to use as a footer for the whole output.</summary>
  /// <param name="ad">The assembly whose public API has just been formatted.</param>
  /// <returns>The lines to use as a footer for the whole output.</returns>
  protected virtual IEnumerable<string?> FileFooter(AssemblyDefinition ad) => [];

  /// <summary>Produces the lines to use as a header for the whole output.</summary>
  /// <param name="ad">The assembly whose public API is about to be formatted.</param>
  /// <returns>The lines to use as a header for the whole output.</returns>
  protected virtual IEnumerable<string?> FileHeader(AssemblyDefinition ad) {
    yield return this.LineComment("=== Generated API Reference === DO NOT EDIT BY HAND ===");
  }

  /// <summary>Formats the public API for an assembly.</summary>
  /// <param name="ad">The assembly to process.</param>
  /// <returns>The formatted public API for the assembly, line by line.</returns>
  public IEnumerable<string?> FormatPublicApi(AssemblyDefinition ad) {
    this._runtimeFeatures = ad.GetRuntimeFeatures();
    foreach (var line in this.FileHeader(ad)) {
      yield return line;
    }
    foreach (var line in this.TopLevelAttributes(ad)) {
      yield return line;
    }
    foreach (var line in this.ExportedTypes(ad)) {
      yield return line;
    }
    foreach (var line in this.TopLevelTypes(ad)) {
      yield return line;
    }
    foreach (var line in this.FileFooter(ad)) {
      yield return line;
    }
    this._runtimeFeatures = null;
  }

  /// <summary>Formats the public API for an assembly.</summary>
  /// <param name="assembly">A stream containing the assembly to process.</param>
  /// <returns>The formatted public API for the assembly, line by line.</returns>
  public IEnumerable<string?> FormatPublicApi(Stream assembly) => this.FormatPublicApi(assembly, new ReaderParameters());

  /// <summary>Formats the public API for an assembly.</summary>
  /// <param name="assembly">A stream containing the assembly to process.</param>
  /// <param name="parameters">The parameters to apply when reading the assembly.</param>
  /// <returns>The formatted public API for the assembly, line by line.</returns>
  public IEnumerable<string?> FormatPublicApi(Stream assembly, ReaderParameters parameters) {
    using var ad = AssemblyDefinition.ReadAssembly(assembly, parameters);
    return this.FormatPublicApi(ad);
  }

  /// <summary>Formats the public API for an assembly.</summary>
  /// <param name="assemblyPath">The path to the assembly to process.</param>
  /// <returns>The formatted public API for the assembly, line by line.</returns>
  public IEnumerable<string?> FormatPublicApi(string assemblyPath) => this.FormatPublicApi(assemblyPath, new ReaderParameters());

  /// <summary>Formats the public API for an assembly.</summary>
  /// <param name="assemblyPath">The path to the assembly to process.</param>
  /// <param name="parameters">The parameters to apply when reading the assembly.</param>
  /// <returns>The formatted public API for the assembly, line by line.</returns>
  public IEnumerable<string?> FormatPublicApi(string assemblyPath, ReaderParameters parameters) {
    using var ad = AssemblyDefinition.ReadAssembly(assemblyPath, parameters);
    return this.FormatPublicApi(ad);
  }

  /// <summary>Formats any constraints applied to a generic parameter.</summary>
  /// <param name="gp">The generic parameter.</param>
  /// <returns>The formatted constrains.</returns>
  protected abstract string? GenericParameterConstraints(GenericParameter gp);

  /// <summary>Formats all generic parameter constraints that apply to something that can have generic parameters.</summary>
  /// <param name="provider">The source of the generic parameters.</param>
  /// <param name="indent">The number of spaces of indentation to use.</param>
  /// <returns>The formatted generic parameter constraints.</returns>
  protected IEnumerable<string> GenericParameterConstraints(IGenericParameterProvider provider, int indent) {
    if (!provider.HasGenericParameters) {
      yield break;
    }
    var sb = new StringBuilder();
    foreach (var parameter in provider.GenericParameters) {
      var constraints = this.GenericParameterConstraints(parameter);
      if (constraints is not null) {
        sb.Clear();
        sb.Append(' ', indent).Append(constraints);
        yield return sb.ToString();
      }
    }
  }

  /// <summary>Determines whether the assembly being processed includes a particular runtime feature.</summary>
  /// <param name="feature">
  /// The name of the feature to check for. This should match one of the field names documented
  /// <a href="https://learn.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.runtimefeature">here</a>.
  /// </param>
  /// <returns></returns>
  protected bool HasRuntimeFeature(string feature) => this._runtimeFeatures?.Contains(feature) ?? false;

  /// <summary>
  /// Adds patterns indicating attributes that should be included in the output. When inclusion patterns are set up, those will be
  /// the <em>only</em> attributes that are included (further filtered by any exclusion patterns set up via
  /// <see cref="ExcludeCustomAttributes"/>).
  /// </summary>
  /// <param name="patterns">
  /// The patterns to include (using simple shell wildcards: <c>*</c> or <c>?</c>). Note that matches are against the internal name
  /// of the attribute, so <c>Foo/BarAttribute`1</c> for a <c>BarAttribute&lt;T&gt;</c> attribute type nested in a <c>Foo</c> class.
  /// </param>
  public void IncludeCustomAttributes(IEnumerable<string> patterns) {
    foreach (var pattern in patterns) {
      if (string.IsNullOrWhiteSpace(pattern)) {
        continue;
      }
      this._attributesToInclude.Add(pattern.Trim());
    }
  }

  /// <summary>
  /// Indicates whether the public API should be considered to include <code>internal</code> and <code>private protected</code>
  /// items instead of only <code>public</code> and <code>protected</code> ones.
  /// </summary>
  public bool IncludeInternals { get; set; }

  /// <summary>
  /// Determines whether a particular custom attribute will be represented by syntax in the formatted output (implying that there is
  /// no need to format it as an attribute).
  /// </summary>
  /// <param name="ca">The custom attribute.</param>
  /// <returns><see langword="true"/> when the attribute gets formatted as syntax, <see langword="false"/> otherwise.</returns>
  protected abstract bool IsHandledBySyntax(ICustomAttribute ca);

  /// <summary>Formats a single-line comment.</summary>
  /// <param name="comment">The comment text.</param>
  /// <returns>The formatted comment.</returns>
  protected abstract string LineComment(string comment);

  /// <summary>Formats a boolean literal.</summary>
  /// <param name="value">The value for the literal.</param>
  /// <returns>The formatted literal.</returns>
  protected abstract string Literal(bool value);

  /// <summary>Formats an unsigned 8-bit integer literal.</summary>
  /// <param name="value">The value for the literal.</param>
  /// <returns>The formatted literal.</returns>
  protected abstract string Literal(byte value);

  /// <summary>Formats a character literal.</summary>
  /// <param name="value">The value for the literal.</param>
  /// <returns>The formatted literal.</returns>
  protected abstract string Literal(char value);

  /// <summary>Formats a decimal literal.</summary>
  /// <param name="value">The value for the literal.</param>
  /// <returns>The formatted literal.</returns>
  protected abstract string Literal(decimal value);

  /// <summary>Formats a 64-bit floating-point literal.</summary>
  /// <param name="value">The value for the literal.</param>
  /// <returns>The formatted literal.</returns>
  protected abstract string Literal(double value);

  /// <summary>Formats a 32-bit floating-point literal.</summary>
  /// <param name="value">The value for the literal.</param>
  /// <returns>The formatted literal.</returns>
  protected abstract string Literal(float value);

  /// <summary>Formats a signed 32-bit integer literal.</summary>
  /// <param name="value">The value for the literal.</param>
  /// <returns>The formatted literal.</returns>
  protected abstract string Literal(int value);

  /// <summary>Formats a signed 64-bit integer literal.</summary>
  /// <param name="value">The value for the literal.</param>
  /// <returns>The formatted literal.</returns>
  protected abstract string Literal(long value);

  /// <summary>Formats a signed 8-bit integer literal.</summary>
  /// <param name="value">The value for the literal.</param>
  /// <returns>The formatted literal.</returns>
  protected abstract string Literal(sbyte value);

  /// <summary>Formats a signed 16-bit integer literal.</summary>
  /// <param name="value">The value for the literal.</param>
  /// <returns>The formatted literal.</returns>
  protected abstract string Literal(short value);

  /// <summary>Formats a string literal.</summary>
  /// <param name="value">The value for the literal.</param>
  /// <returns>The formatted literal.</returns>
  protected abstract string Literal(string value);

  /// <summary>Formats an unsigned 32-bit integer literal.</summary>
  /// <param name="value">The value for the literal.</param>
  /// <returns>The formatted literal.</returns>
  protected abstract string Literal(uint value);

  /// <summary>Formats an unsigned 64-bit integer literal.</summary>
  /// <param name="value">The value for the literal.</param>
  /// <returns>The formatted literal.</returns>
  protected abstract string Literal(ulong value);

  /// <summary>Formats an unsigned 16-bit integer literal.</summary>
  /// <param name="value">The value for the literal.</param>
  /// <returns>The formatted literal.</returns>
  protected abstract string Literal(ushort value);

  /// <summary>Formats a method.</summary>
  /// <param name="md">The method to format.</param>
  /// <param name="indent">The number of spaces of indentation to use.</param>
  /// <returns>The formatted method (including any attributes attached to it).</returns>
  protected abstract IEnumerable<string?> Method(MethodDefinition md, int indent);

  /// <summary>Formats the name of a method, and determine its (formatted) return type.</summary>
  /// <param name="md">The method.</param>
  /// <param name="returnTypeName">
  /// The (formatted) return type for the method. This can be the empty string in cases where the return type was used in the method
  /// name (as is typically the case for constructors and conversion operators).
  /// </param>
  /// <returns>The formatted method name.</returns>
  protected abstract string MethodName(MethodDefinition md, out string returnTypeName);

  /// <summary>Formats the methods declared by a type.</summary>
  /// <param name="td">The type to process.</param>
  /// <param name="indent">The number of spaces of indentation to use.</param>
  /// <returns>The formatted methods, as produced by <see cref="Method"/>, separated by blank lines.</returns>
  protected IEnumerable<string?> Methods(TypeDefinition td, int indent) {
    if (!td.HasMethods) {
      yield break;
    }
    var methods = new SortedSet<MethodDefinition>(this);
    foreach (var method in td.Methods) {
      if (!this.ShouldInclude(method) || method.IsAddOn || method.IsGetter || method.IsRemoveOn || method.IsSetter) {
        continue;
      }
      if (methods.Add(method)) {
        continue;
      }
      if (methods.TryGetValue(method, out var previousMethod)) {
        Trace.Fail(method.ToString(), $"Multiply defined method; previous was {previousMethod}.");
      }
      else {
        Trace.Fail(method.ToString(), "Multiply defined method; could not determine original definition.");
      }
    }
    if (methods.Count == 0) {
      yield break;
    }
    foreach (var method in methods) {
      yield return null;
      foreach (var line in this.Method(method, indent)) {
        yield return line;
      }
    }
  }

  /// <summary>Produces the lines to use as a footer for the section containing module-level attributes.</summary>
  /// <param name="md">The module definition whose attributes have just been formatted.</param>
  /// <returns>The lines to use as a footer for the section containing module-level attributes.</returns>
  protected virtual IEnumerable<string?> ModuleAttributeFooter(ModuleDefinition md) => [];

  /// <summary>Produces the lines to use as a header for the section containing module-level attributes.</summary>
  /// <param name="md">The module definition whose attributes are about to be formatted.</param>
  /// <returns>The lines to use as a header for the section containing module-level attributes.</returns>
  protected virtual IEnumerable<string?> ModuleAttributeHeader(ModuleDefinition md) {
    yield return null;
    yield return this.LineComment($"Module Attributes ({md.Name})");
    yield return null;
  }

  /// <summary>Produces a single line for the section containing module-level attributes.</summary>
  /// <param name="attribute">The formatted attribute name plus constructor arguments and properties, if specified.</param>
  /// <returns>A single line for the section containing module-level attributes.</returns>
  protected abstract string ModuleAttributeLine(string attribute);

  /// <summary>Formats a custom attribute's named arguments (i.e. its property assignments).</summary>
  /// <param name="ca">The custom attribute to process.</param>
  /// <returns>The named arguments, formatted using <see cref="NamedCustomAttributeArgument"/>.</returns>
  protected IEnumerable<string> NamedCustomAttributeArguments(CustomAttribute ca) {
    var namedArguments = new SortedDictionary<string, CustomAttributeArgument>();
    if (ca.HasFields) {
      foreach (var arg in ca.Fields) {
        namedArguments.Add(arg.Name, arg.Argument);
      }
    }
    if (ca.HasProperties) {
      foreach (var arg in ca.Properties) {
        namedArguments.Add(arg.Name, arg.Argument);
      }
    }
    return namedArguments.Select(item => this.NamedCustomAttributeArgument(item.Key, item.Value));
  }

  /// <summary>Formats a named custom attribute argument (i.e. a property assignment).</summary>
  /// <param name="name">The name of the argument.</param>
  /// <param name="value">The attribute argument to format.</param>
  /// <returns>The formatted custom attribute argument.</returns>
  protected virtual string NamedCustomAttributeArgument(string name, CustomAttributeArgument value)
    => name + " = " + this.CustomAttributeArgument(value);

  /// <summary>Produces the lines to use as a footer for the formatted contents of the current namespace.</summary>
  /// <returns>The lines to use as a footer for the formatted contents of the current namespace.</returns>
  protected virtual IEnumerable<string?> NamespaceFooter() => [];

  /// <summary>Produces the lines to use as a header for the formatted contents of the current namespace.</summary>
  /// <returns>The lines to use as a header for the formatted contents of the current namespace.</returns>
  protected virtual IEnumerable<string?> NamespaceHeader() => [];

  /// <summary>Formats the nested types declared by a type.</summary>
  /// <param name="td">The type to process.</param>
  /// <param name="indent">The number of spaces of indentation to use.</param>
  /// <returns>The formatted types.</returns>
  protected IEnumerable<string?> NestedTypes(TypeDefinition td, int indent) {
    if (!td.HasNestedTypes) {
      yield break;
    }
    var nestedTypes = new SortedDictionary<string, TypeDefinition>();
    foreach (var type in td.NestedTypes) {
      if (!this.ShouldInclude(td)) {
        continue;
      }
      if (nestedTypes.TryGetValue(type.Name, out var previousType)) {
        Trace.Fail(type.ToString(), $"Multiply defined nested type in {td}; previous was {previousType}.");
      }
      nestedTypes.Add(type.Name, type);
    }
    if (nestedTypes.Count == 0) {
      yield break;
    }
    var parentType = this.CurrentType;
    foreach (var nestedType in nestedTypes.Values) {
      yield return null;
      foreach (var line in this.Type(this.CurrentType = nestedType, indent)) {
        yield return line;
      }
    }
    this.CurrentType = parentType;
  }

  /// <summary>Formats a null literal.</summary>
  /// <returns>The formatted null literal.</returns>
  protected abstract string Null();

  /// <summary>Formats an "or" operator, as used to combine multiple values of a <c>[Flags]</c> enum.</summary>
  /// <returns>The formatted operator.</returns>
  protected abstract string Or();

  /// <summary>Formats the properties declared by a type.</summary>
  /// <param name="td">The type to process.</param>
  /// <param name="indent">The number of spaces of indentation to use.</param>
  /// <returns>The formatted properties.</returns>
  protected IEnumerable<string?> Properties(TypeDefinition td, int indent) {
    if (!td.HasProperties) {
      yield break;
    }
    var parametrizedProperties = new SortedDictionary<string, SortedSet<PropertyDefinition>>();
    var properties = new SortedSet<PropertyDefinition>(this);
    foreach (var property in td.Properties) {
      if (!this.ShouldInclude(property.GetMethod) && !this.ShouldInclude(property.SetMethod)) {
        continue;
      }
      if (property.HasParameters) {
        if (!parametrizedProperties.TryGetValue(property.Name, out var overloads)) {
          parametrizedProperties.Add(property.Name, overloads = new SortedSet<PropertyDefinition>(this));
        }
        // This ends up sorting on the return type first; given that this is probably an indexer (what other properties have
        // parameters?), that should be fine. Alternatively, we could stringify the parameters only.
        if (overloads.TryGetValue(property, out var previousProperty)) {
          Trace.Fail(property.ToString(), $"Multiply defined property; previous was {previousProperty}.");
        }
        overloads.Add(property);
      }
      else {
        if (properties.TryGetValue(property, out var previousProperty)) {
          Trace.Fail(property.ToString(), $"Multiply defined property in {td}; previous was {previousProperty}.");
        }
        properties.Add(property);
      }
    }
    foreach (var overloads in parametrizedProperties.Values) {
      foreach (var pd in overloads) {
        yield return null;
        foreach (var line in this.Property(pd, indent)) {
          yield return line;
        }
      }
    }
    foreach (var pd in properties) {
      yield return null;
      foreach (var line in this.Property(pd, indent)) {
        yield return line;
      }
    }
  }

  /// <summary>Formats a property.</summary>
  /// <param name="pd">The property to format.</param>
  /// <param name="indent">The number of spaces of indentation to use.</param>
  /// <returns>The formatted property (including any attributes attached to it).</returns>
  protected abstract IEnumerable<string?> Property(PropertyDefinition pd, int indent);

  /// <summary>Formats the name of a property.</summary>
  /// <param name="pd">The property.</param>
  /// <returns>The formatted name for the property.</returns>
  protected abstract string PropertyName(PropertyDefinition pd);

  /// <summary>Determines whether a particular custom attribute should be retained and formatted as an attribute.</summary>
  /// <param name="ca">The custom attribute.</param>
  /// <returns>
  /// <see langword="true"/> when the attribute is not handled by syntax (as determined by <see cref="IsHandledBySyntax"/>) and has
  /// not been marked for exclusion (via <see cref="ExcludeCustomAttributes"/> and/or <see cref="IncludeCustomAttributes"/>);
  /// <see langword="false"/> otherwise.
  /// </returns>
  protected bool Retain(ICustomAttribute ca) {
    // Attributes handled by syntax (like [Extension] and [ParamArray] for C#) are never retained.
    if (this.IsHandledBySyntax(ca)) {
      return false;
    }
    // For everything else, use the configured inclusion/exclusion processing
    var name = ca.AttributeType.FullName;
    if (this._attributesToInclude.Count > 0) {
      if (!this._attributesToInclude.Any(pattern => name.Matches(pattern))) {
        return false;
      }
    }
    return this._attributesToExclude.All(pattern => !name.Matches(pattern));
  }

  private bool ShouldInclude(EventDefinition ed) => ed.IsPublicApi() || (this.IncludeInternals && ed.IsInternalApi());

  private bool ShouldInclude(FieldDefinition fd) => fd.IsPublicApi() || (this.IncludeInternals && fd.IsInternalApi());

  private bool ShouldInclude(MethodDefinition? md)
    => md is not null && (md.IsPublicApi() || (this.IncludeInternals && md.IsInternalApi()));

  private bool ShouldInclude(TypeDefinition td) => td.IsPublicApi() || (this.IncludeInternals && td.IsInternalApi());

  private IEnumerable<string?> TopLevelAttributes(AssemblyDefinition ad) {
    foreach (var line in this.CustomAttributes(ad)) {
      yield return line;
    }
    foreach (var md in ad.Modules) {
      foreach (var line in this.CustomAttributes(md)) {
        yield return line;
      }
    }
  }

  private IEnumerable<string?> TopLevelTypes(AssemblyDefinition ad) {
    // Gather all types, grouping them by namespace.
    var namespacedTypes = new SortedDictionary<string, SortedDictionary<string, TypeDefinition>>();
    foreach (var md in ad.Modules) {
      if (md.HasTypes) {
        foreach (var td in md.Types) {
          if (!this.ShouldInclude(td)) {
            continue;
          }
          if (!namespacedTypes.TryGetValue(td.Namespace, out var types)) {
            namespacedTypes.Add(td.Namespace, types = new SortedDictionary<string, TypeDefinition>());
          }
          if (types.TryGetValue(td.Name, out var previousType)) {
            Trace.Fail(td.ToString(), $"Multiply defined type; previous was {previousType}.");
          }
          types.Add(td.Name, td);
        }
      }
      // FIXME: Do we need to care about "exported types"?
    }
    foreach (var ns in namespacedTypes) {
      this.CurrentNamespace = ns.Key;
      foreach (var line in this.NamespaceHeader()) {
        yield return line;
      }
      foreach (var type in ns.Value) {
        var td = type.Value;
        foreach (var line in this.TypeHeader(td)) {
          yield return line;
        }
        this.CurrentType = td;
        foreach (var line in this.Type(td, this.TopLevelTypeIndent)) {
          yield return line;
        }
        this.CurrentType = null;
        foreach (var line in this.TypeFooter(td)) {
          yield return line;
        }
      }
      foreach (var line in this.NamespaceFooter()) {
        yield return line;
      }
      this.CurrentNamespace = null;
    }
  }

  /// <summary>Formats a type.</summary>
  /// <param name="td">The type to format.</param>
  /// <param name="indent">The number of spaces of indentation to use.</param>
  /// <returns>The formatted type.</returns>
  protected abstract IEnumerable<string?> Type(TypeDefinition td, int indent);

  /// <summary>Produces the lines to use as a footer for a formatted type.</summary>
  /// <param name="td">The type.</param>
  /// <returns>The lines to use as a footer for a formatted type.</returns>
  protected virtual IEnumerable<string?> TypeFooter(TypeDefinition td) => [];

  /// <summary>Produces the lines to use as a header for a formatted type.</summary>
  /// <param name="td">The type.</param>
  /// <returns>The lines to use as a header for a formatted type.</returns>
  protected virtual IEnumerable<string?> TypeHeader(TypeDefinition td) {
    yield return null;
  }

  /// <summary>Formats the name of a type.</summary>
  /// <param name="tr">The type.</param>
  /// <param name="context">The source of custom attributes to use as the direct context, if available.</param>
  /// <param name="methodContext">The method to use as context, if applicable.</param>
  /// <param name="typeContext">The type to use as context, if applicable.</param>
  /// <returns></returns>
  protected abstract string TypeName(TypeReference tr, ICustomAttributeProvider? context = null,
                                     MethodDefinition? methodContext = null, TypeDefinition? typeContext = null);

  /// <summary>Formats a <see langword="typeof"/> operator.</summary>
  /// <param name="tr">The type passed to the operator.</param>
  /// <returns>The formatted operator.</returns>
  protected abstract string TypeOf(TypeReference tr);

  /// <summary>Formats a value.</summary>
  /// <param name="type">The type of the value, if explicitly specified.</param>
  /// <param name="value">The value to format.</param>
  /// <returns></returns>
  protected virtual string Value(TypeReference? type, object? value) {
    if (value is not null && type is { IsValueType: true }) {
      // Check for enum values
      var enumType = type;
      if (type.TryUnwrapNullable(out var unwrapped)) {
        enumType = unwrapped;
      }
      var td = enumType.Resolve();
      // If we can't resolve it, we can't know whether it was an enum
      if (td is not null && td.IsEnum) {
        // Check for [Flags]
        var flags = false;
        if (td.HasCustomAttributes) {
          foreach (var ca in td.CustomAttributes) {
            var at = ca.AttributeType;
            if (at.Scope == at.Module.TypeSystem.CoreLibrary && at is { Namespace: "System", Name: "FlagsAttribute" }) {
              flags = true;
              break;
            }
          }
        }
        var sortedValues = new SortedDictionary<string, FieldDefinition>();
        foreach (var fd in td.Fields) {
          if (fd.IsSpecialName || !fd.IsLiteral || !fd.HasConstant) {
            continue;
          }
          sortedValues.Add(fd.Name, fd);
        }
        var values = new List<(ulong Flags, string Name)>();
        if (flags) {
          var flagsValue = value.ToULong();
          var remainingFlags = flagsValue;
          var discardFlags = new HashSet<ulong>();
          foreach (var enumValue in sortedValues.Values) {
            var valueFlags = enumValue.Constant.ToULong();
            // FIXME: Should we include fields with value 0?
            if ((flagsValue & valueFlags) != valueFlags) {
              continue;
            }
            var addValue = true;
            // If there is already a value that is identical to or a superset of this one, ignore it.
            // Conversely, if this value is a superset of any existing values, discard those.
            foreach (var existing in values) {
              if (valueFlags == existing.Flags) {
                addValue = false;
                continue;
              }
              var commonFlags = existing.Flags & valueFlags;
              if (commonFlags == existing.Flags) {
                discardFlags.Add(existing.Flags);
              }
              else if (commonFlags == valueFlags) {
                addValue = false;
              }
            }
            if (addValue) {
              values.Add((valueFlags, enumValue.Name));
            }
            remainingFlags &= ~valueFlags;
          }
          var textValues = values.Where(e => !discardFlags.Contains(e.Flags)).Select(e => this.EnumValue(td, e.Name)).ToList();
          if (remainingFlags != 0) {
            // Unhandled flags remain - use a forced cast
            textValues.Add(this.Cast(td, this.Value(null, remainingFlags)));
          }
          return string.Join($" {this.Or()} ", textValues);
        }
        // Simple enum value
        foreach (var enumValue in td.Fields) {
          if (enumValue.IsSpecialName || !enumValue.IsLiteral || !enumValue.HasConstant) {
            continue;
          }
          if (enumValue.Constant.Equals(value)) {
            return this.EnumValue(td, enumValue.Name);
          }
        }
        // Not a valid value, use a forced cast
        return this.Cast(td, this.Value(null, value));
      }
    }
    return value switch {
      null => this.Null(),
      TypeReference tr => this.TypeOf(tr),
      bool b => this.Literal(b),
      byte b => this.Literal(b),
      char c => this.Literal(c),
      decimal d => this.Literal(d),
      double d => this.Literal(d),
      float f => this.Literal(f),
      int i => this.Literal(i),
      long l => this.Literal(l),
      sbyte sb => this.Literal(sb),
      short s => this.Literal(s),
      string s => this.Literal(s),
      uint ui => this.Literal(ui),
      ulong ul => this.Literal(ul),
      ushort us => this.Literal(us),
      // Assume everything else matches its ToString() - even though there's no way to tell it to use an invariant form
      _ => value.ToString() ?? "/* non-null object with null string form */"
    };
  }

}
