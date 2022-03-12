using System.Linq;

namespace Zastai.Build.ApiReference;

internal abstract class CodeFormatter {

  protected string? CurrentNamespace { get; private set; }

  protected TypeDefinition? CurrentType { get; private set; }

  protected virtual int TopLevelTypeIndent => 2;

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
      if (!ca.IsPublicApi()) {
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
    foreach (var item in sortedAttributes) {
      foreach (var attribute in item.Value) {
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

  protected abstract string CustomAttributesInline(ICustomAttributeProvider cap);

  protected abstract string EnumField(FieldDefinition fd, int indent);

  protected virtual string EnumValue(TypeDefinition enumType, string name) => $"{this.TypeName(enumType)}.{name}";

  protected abstract string Event(EventDefinition ed, int indent);

  protected IEnumerable<string?> Events(TypeDefinition td, int indent) {
    if (!td.HasEvents) {
      yield break;
    }
    var events = new SortedDictionary<string, EventDefinition>();
    foreach (var ed in td.Events) {
      if (!ed.IsPublicApi()) {
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
    foreach (var @event in events) {
      yield return null;
      foreach (var line in this.CustomAttributes(@event.Value, indent)) {
        yield return line;
      }
      yield return this.Event(@event.Value, indent);
    }
  }

  protected abstract string Field(FieldDefinition fd, int indent);

  protected IEnumerable<string?> Fields(TypeDefinition td, int indent) {
    if (!td.HasFields) {
      yield break;
    }
    var fields = new SortedDictionary<string, FieldDefinition>();
    foreach (var field in td.Fields) {
      if (!field.IsPublicApi()) {
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
      foreach (var field in fields) {
        if (field.Value.IsSpecialName) { // skip value__
          continue;
        }
        foreach (var line in this.CustomAttributes(field.Value, indent)) {
          yield return line;
        }
        yield return this.EnumField(field.Value, indent);
      }
    }
    else {
      foreach (var field in fields) {
        yield return null;
        foreach (var line in this.CustomAttributes(field.Value, indent)) {
          yield return line;
        }
        yield return this.Field(field.Value, indent);
      }
    }
  }

  protected virtual IEnumerable<string?> FileFooter(AssemblyDefinition ad) => Enumerable.Empty<string?>();

  protected virtual IEnumerable<string?> FileHeader(AssemblyDefinition ad) {
    yield return this.LineComment("=== Generated API Reference === DO NOT EDIT BY HAND ===");
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

  protected abstract string LineComment(string comment);

  protected abstract string Literal(bool value);

  protected abstract string Literal(string value);

  protected abstract IEnumerable<string?> Method(MethodDefinition md, int indent);

  protected IEnumerable<string?> Methods(TypeDefinition td, int indent) {
    if (!td.HasMethods) {
      yield break;
    }
    var methods = new SortedDictionary<string, SortedDictionary<string, MethodDefinition>>();
    foreach (var method in td.Methods) {
      if (!method.IsPublicApi() || method.IsAddOn || method.IsGetter || method.IsRemoveOn || method.IsSetter) {
        continue;
      }
      if (!methods.TryGetValue(method.Name, out var overloads)) {
        methods.Add(method.Name, overloads = new SortedDictionary<string, MethodDefinition>());
      }
      var signature = method.ToString();
      if (method.HasGenericParameters) {
        // These are not included in the ToString(), and they are needed for uniqueness
        foreach (var gp in method.GenericParameters) {
          signature += $"<{gp}>";
        }
      }
      if (overloads.TryGetValue(signature, out var previousMethod)) {
        Trace.Fail(signature, $"Multiply defined method; previous was {previousMethod}.");
      }
      overloads.Add(signature, method);
    }
    if (methods.Count == 0) {
      yield break;
    }
    foreach (var method in methods) {
      foreach (var overload in method.Value) {
        yield return null;
        foreach (var line in this.Method(overload.Value, indent)) {
          yield return line;
        }
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
      if (!type.IsPublicApi()) {
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
    foreach (var type in nestedTypes) {
      yield return null;
      foreach (var line in this.Type(this.CurrentType = type.Value, indent)) {
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
    var parametrizedProperties = new SortedDictionary<string, SortedDictionary<string, PropertyDefinition>>();
    var properties = new SortedDictionary<string, PropertyDefinition>();
    foreach (var property in td.Properties) {
      if ((property.GetMethod?.IsPublicApi() ?? false) || (property.SetMethod?.IsPublicApi() ?? false)) {
        if (property.HasParameters) {
          if (!parametrizedProperties.TryGetValue(property.Name, out var overloads)) {
            parametrizedProperties.Add(property.Name, overloads = new SortedDictionary<string, PropertyDefinition>());
          }
          // This ends up sorting on the return type first; given that this is probably an indexer (what other properties have
          // parameters?), that should be fine. Alternatively, we could stringify the parameters only.
          var signature = property.ToString();
          if (overloads.TryGetValue(signature, out var previousProperty)) {
            Trace.Fail(signature, $"Multiply defined property; previous was {previousProperty}.");
          }
          overloads.Add(signature, property);
        }
        else {
          if (properties.TryGetValue(property.Name, out var previousProperty)) {
            Trace.Fail(property.ToString(), $"Multiply defined property in {td}; previous was {previousProperty}.");
          }
          properties.Add(property.Name, property);
        }
      }
    }
    if (properties.Count == 0) {
      yield break;
    }
    foreach (var parametrizedProperty in parametrizedProperties) {
      foreach (var overload in parametrizedProperty.Value) {
        yield return null;
        foreach (var line in this.Property(overload.Value, indent)) {
          yield return line;
        }
      }
    }
    foreach (var property in properties) {
      yield return null;
      foreach (var line in this.Property(property.Value, indent)) {
        yield return line;
      }
    }
  }

  protected abstract IEnumerable<string?> Property(PropertyDefinition pd, int indent);

  public IEnumerable<string?> FormatPublicApi(AssemblyDefinition ad) {
    foreach (var line in this.FileHeader(ad)) {
      yield return line;
    }
    foreach (var line in this.TopLevelAttributes(ad)) {
      yield return line;
    }
    foreach (var line in this.TopLevelTypes(ad)) {
      yield return line;
    }
    foreach (var line in this.FileFooter(ad)) {
      yield return line;
    }
  }

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
          if (!td.IsPublicApi()) {
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

  protected abstract string TypeName(TypeReference tr, ICustomAttributeProvider? context = null);

  protected abstract string TypeOf(TypeReference tr);

  protected virtual string Value(TypeReference? type, object? value) {
    if (value is not null && type is { IsValueType: true }) { // Check for enum values
      var enumType = type;
      if (type.TryUnwrapNullable(out var unwrapped)) {
        enumType = unwrapped;
      }
      var td = enumType.Resolve();
      if (td.IsEnum) {
        // Check for [Flags]
        var flags = false;
        if (td.HasCustomAttributes) {
          foreach (var ca in td.CustomAttributes) {
            var at = ca.AttributeType;
            if (at.Scope == at.Module.TypeSystem.CoreLibrary && at.Namespace == "System" && at.Name == "FlagsAttribute") {
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
        var values = new List<string>();
        if (flags) {
          var flagsValue = value.ToULong();
          var remainingFlags = flagsValue;
          foreach (var item in sortedValues) {
            var enumValue = item.Value;
            var valueFlags = enumValue.Constant.ToULong();
            // FIXME: Should we include fields with value 0?
            if ((flagsValue & valueFlags) != valueFlags) {
              continue;
            }
            values.Add(this.EnumValue(td, enumValue.Name));
            remainingFlags &= ~valueFlags;
          }
          if (remainingFlags != 0) {
            // Unhandled flags remain - use a forced cast
            values.Add(this.Cast(td, this.Value(null, remainingFlags)));
          }
          return string.Join($" {this.Or()} ", values);
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
    switch (value) {
      case null:
        return this.Null();
      case TypeReference tr:
        return this.TypeOf(tr);
      case bool b:
        return this.Literal(b);
      case string s:
        return this.Literal(s);
      default:
        // Assume everything else matches its ToString()
        return value.ToString() ?? "/* non-null object with null string form */";
    }
  }

}
