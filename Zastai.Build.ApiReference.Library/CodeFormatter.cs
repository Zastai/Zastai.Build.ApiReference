using System.Linq;

namespace Zastai.Build.ApiReference;

public abstract partial class CodeFormatter {

  protected string? CurrentNamespace { get; private set; }

  protected TypeDefinition? CurrentType { get; private set; }

  protected virtual int TopLevelTypeIndent => 2;

  private readonly ISet<string> _attributesToExclude = new HashSet<string>();

  private readonly ISet<string> _attributesToInclude = new HashSet<string>();

  private bool _binaryEnumsEnabled;

  private bool _charEnumsEnabled;

  private bool _hexEnumsEnabled;

  // FIXME: IReadOnlySet would be better, but is not available on .NET Framework.
  private ISet<string>? _runtimeFeatures;

  protected virtual IEnumerable<string?> AssemblyAttributeFooter(AssemblyDefinition ad) => Enumerable.Empty<string?>();

  protected virtual IEnumerable<string?> AssemblyAttributeHeader(AssemblyDefinition ad) {
    yield return null;
    yield return this.LineComment("Assembly Attributes");
    yield return null;
  }

  protected abstract string AssemblyAttributeLine(string attribute);

  protected abstract string Cast(TypeDefinition targetType, string value);

  protected abstract string CustomAttribute(CustomAttribute ca);

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

  protected abstract IEnumerable<string?> CustomAttributes(ICustomAttributeProvider cap, int indent);

  protected IEnumerable<string> CustomAttributes(IEnumerable<CustomAttribute> attributes) {
    // Sort by the (full) type name; unfortunately, I'm not sure how to sort duplicates in a stable way.
    var sortedAttributes = new SortedDictionary<string, IList<CustomAttribute>>();
    foreach (var ca in attributes) {
      if (!this.Retain(ca)) {
        continue;
      }
      var attributeType = ca.AttributeType.FullName;
      if (!sortedAttributes.TryGetValue(attributeType, out var list)) {
        sortedAttributes.Add(attributeType, list = new List<CustomAttribute>());
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

  public void EnableBinaryEnums(bool yes) => this._binaryEnumsEnabled = yes;

  public void EnableCharEnums(bool yes) => this._charEnumsEnabled = yes;

  public void EnableHexEnums(bool yes) => this._hexEnumsEnabled = yes;

  protected abstract string EnumField(FieldDefinition fd, int indent, EnumFieldValueMode mode, int highestBit);

  protected abstract string EnumValue(TypeDefinition enumType, string name);

  protected abstract string Event(EventDefinition ed, int indent);

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
    return exportedTypes.Count == 0 ? Enumerable.Empty<string?>() : this.ExportedTypes(exportedTypes);
  }

  public void ExcludeCustomAttributes(IEnumerable<string> patterns) {
    foreach (var pattern in patterns) {
      if (string.IsNullOrWhiteSpace(pattern)) {
        continue;
      }
      this._attributesToExclude.Add(pattern.Trim());
    }
  }

  protected abstract IEnumerable<string?> ExportedTypes(SortedDictionary<string, IDictionary<string, ExportedType>> exportedTypes);

  protected abstract string Field(FieldDefinition fd, int indent);

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
            // Character values never makse sense for [Flags] enums.
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
            highestBit = Math.Max(highestBit, binary.Length);
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

  protected virtual IEnumerable<string?> FileFooter(AssemblyDefinition ad) => Enumerable.Empty<string?>();

  protected virtual IEnumerable<string?> FileHeader(AssemblyDefinition ad) {
    yield return this.LineComment("=== Generated API Reference === DO NOT EDIT BY HAND ===");
  }

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

  protected abstract string? GenericParameterConstraints(GenericParameter gp);

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

  protected bool HasRuntimeFeature(string feature) => this._runtimeFeatures?.Contains(feature) ?? false;

  public void IncludeCustomAttributes(IEnumerable<string> patterns) {
    foreach (var pattern in patterns) {
      if (string.IsNullOrWhiteSpace(pattern)) {
        continue;
      }
      this._attributesToInclude.Add(pattern.Trim());
    }
  }

  public bool IncludeInternals { get; set; }

  protected abstract string LineComment(string comment);

  protected abstract string Literal(bool value);

  protected abstract string Literal(byte value);

  protected abstract string Literal(char value);

  protected abstract string Literal(decimal value);

  protected abstract string Literal(double value);

  protected abstract string Literal(float value);

  protected abstract string Literal(int value);

  protected abstract string Literal(long value);

  protected abstract string Literal(sbyte value);

  protected abstract string Literal(short value);

  protected abstract string Literal(string value);

  protected abstract string Literal(uint value);

  protected abstract string Literal(ulong value);

  protected abstract string Literal(ushort value);

  protected abstract IEnumerable<string?> Method(MethodDefinition md, int indent);

  protected abstract string MethodName(MethodDefinition md, out string returnTypeName);

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

  protected virtual IEnumerable<string?> ModuleAttributeFooter(ModuleDefinition md) => Enumerable.Empty<string?>();

  protected virtual IEnumerable<string?> ModuleAttributeHeader(ModuleDefinition md) {
    yield return null;
    yield return this.LineComment($"Module Attributes ({md.Name})");
    yield return null;
  }

  protected abstract string ModuleAttributeLine(string attribute);

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

  protected virtual string NamedCustomAttributeArgument(string name, CustomAttributeArgument value)
    => name + " = " + this.CustomAttributeArgument(value);

  protected virtual IEnumerable<string?> NamespaceFooter() => Enumerable.Empty<string?>();

  protected virtual IEnumerable<string?> NamespaceHeader() => Enumerable.Empty<string?>();

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

  protected abstract string Null();

  protected abstract string Or();

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

  protected abstract IEnumerable<string?> Property(PropertyDefinition pd, int indent);

  protected abstract string PropertyName(PropertyDefinition pd);

  protected abstract bool IsHandledBySyntax(ICustomAttribute ca);

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

  protected abstract IEnumerable<string?> Type(TypeDefinition td, int indent);

  protected virtual IEnumerable<string?> TypeFooter(TypeDefinition td) => Enumerable.Empty<string?>();

  protected virtual IEnumerable<string?> TypeHeader(TypeDefinition td) {
    yield return null;
  }

  protected abstract string TypeName(TypeReference tr, ICustomAttributeProvider? context = null,
                                     MethodDefinition? methodContext = null, TypeDefinition? typeContext = null);

  protected abstract string TypeOf(TypeReference tr);

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
