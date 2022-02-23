namespace Zastai.Build.ApiReference;

internal abstract class ReferenceWriter {

  protected string? CurrentNamespace { get; private set; }

  private static char[]? _indentationSpaces;

  protected readonly TextWriter Writer;

  protected ReferenceWriter(TextWriter writer) {
    this.Writer = writer;
  }

  protected virtual bool WriteBuiltinTypeKeyword(TypeReference tr) => false;

  protected abstract void WriteCast(TypeDefinition targetType, Action writeValue);

  protected abstract void WriteCommentLine(string comment);

  protected abstract void WriteCustomAttribute(CustomAttribute ca, string? target);

  private void WriteCustomAttributes(AssemblyDefinition ad) {
    if (!ad.HasCustomAttributes) {
      return;
    }
    this.Writer.WriteLine();
    this.WriteCommentLine("Assembly Attributes");
    this.Writer.WriteLine();
    this.WriteCustomAttributes(ad.CustomAttributes, "assembly");
  }

  protected void WriteCustomAttributes(ICustomAttributeProvider cap, int indent = 0) {
    if (!cap.HasCustomAttributes) {
      return;
    }
    this.WriteCustomAttributes(cap.CustomAttributes, null, indent);
  }

  protected void WriteCustomAttributes(IEnumerable<CustomAttribute> attributes, string? target = null, int indent = 0) {
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
      return;
    }
    // Now process them
    foreach (var item in sortedAttributes) {
      foreach (var attribute in item.Value) {
        if (indent >= 0) {
          this.WriteIndent(indent);
        }
        this.WriteCustomAttribute(attribute, target);
        if (indent < 0) {
          this.Writer.Write(' ');
        }
        else {
          this.Writer.WriteLine();
        }
      }
    }
  }

  private void WriteCustomAttributes(ModuleDefinition md) {
    if (!md.HasCustomAttributes) {
      return;
    }
    this.Writer.WriteLine();
    this.WriteCommentLine($"Assembly Attributes ({md.Name})");
    this.Writer.WriteLine();
    this.WriteCustomAttributes(md.CustomAttributes, "module");
  }

  protected abstract void WriteEnumField(FieldDefinition fd, int indent = 0);

  protected virtual void WriteEnumValue(TypeDefinition enumType, string name) {
    this.WriteTypeName(enumType);
    this.Writer.Write('.');
    this.Writer.Write(name);
  }

  protected abstract void WriteEvent(EventDefinition ed, int indent = 0);

  protected void WriteEvents(TypeDefinition td, int indent = 0) {
    if (!td.HasEvents) {
      return;
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
      return;
    }
    foreach (var @event in events) {
      this.Writer.WriteLine();
      this.WriteEvent(@event.Value, indent);
    }
  }

  protected abstract void WriteField(FieldDefinition fd, int indent = 0);

  protected void WriteFields(TypeDefinition td, int indent = 0) {
    if (!td.HasFields) {
      return;
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
      return;
    }
    if (td.IsEnum) {
      this.Writer.WriteLine();
      foreach (var field in fields) {
        if (field.Value.IsSpecialName) { // skip value__
          continue;
        }
        this.WriteEnumField(field.Value, indent);
      }
    }
    else {
      foreach (var field in fields) {
        this.Writer.WriteLine();
        this.WriteField(field.Value, indent);
      }
    }
  }

  protected abstract void WriteGenericParameter(GenericParameter gp);

  protected void WriteGenericParameterConstraint(GenericParameterConstraint constraint) {
    // FIXME: Do we need to care about custom attributes on these?
    this.WriteTypeName(constraint.ConstraintType);
  }

  protected abstract void WriteGenericParameterConstraints(GenericParameter parameter);

  protected void WriteGenericParameterConstraints(IGenericParameterProvider provider) {
    if (!provider.HasGenericParameters) {
      return;
    }
    foreach (var parameter in provider.GenericParameters) {
      this.WriteGenericParameterConstraints(parameter);
    }
  }

  protected abstract void WriteGenericParameters(IGenericParameterProvider provider);

  protected void WriteIndent(int indent) {
    if (ReferenceWriter._indentationSpaces == null || indent > ReferenceWriter._indentationSpaces.Length) {
      ReferenceWriter._indentationSpaces = new string(' ', Math.Max(64, 2 * indent)).ToCharArray();
    }
    this.Writer.Write(ReferenceWriter._indentationSpaces, 0, indent);
  }

  protected abstract void WriteMethod(MethodDefinition md, int indent = 0);

  protected void WriteMethods(TypeDefinition td, int indent = 0) {
    if (!td.HasMethods) {
      return;
    }
    var methods = new SortedDictionary<string, SortedDictionary<string, MethodDefinition>>();
    foreach (var method in td.Methods) {
      if (!method.IsPublicApi() || method.IsGetter || method.IsSetter) {
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
      return;
    }
    foreach (var method in methods) {
      foreach (var overload in method.Value) {
        this.Writer.WriteLine();
        this.WriteMethod(overload.Value, indent);
      }
    }
  }

  protected void WriteNamedCustomAttributeArguments(CustomAttribute ca, bool firstArgument) {
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
    foreach (var namedArgument in namedArguments) {
      this.WriteNamedCustomAttributeArgument(namedArgument.Key, namedArgument.Value, firstArgument);
      firstArgument = false;
    }
  }

  protected abstract void WriteNamedCustomAttributeArgument(string name, CustomAttributeArgument value, bool first);

  protected abstract void WriteLiteral(bool value);

  protected abstract void WriteLiteral(string value);

  protected abstract void WriteNamespace(string name, Action writeContents);

  protected void WriteNestedTypes(TypeDefinition td, int indent = 0) {
    if (!td.HasNestedTypes) {
      return;
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
      return;
    }
    foreach (var type in nestedTypes) {
      this.WriteType(type.Value, indent);
    }
  }

  protected abstract void WriteNull();

  protected abstract void WriteOr();

  protected void WriteProperties(TypeDefinition td, int indent = 0) {
    if (!td.HasProperties) {
      return;
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
      return;
    }
    foreach (var parametrizedProperty in parametrizedProperties) {
      foreach (var overload in parametrizedProperty.Value) {
        this.Writer.WriteLine();
        this.WriteProperty(overload.Value, indent);
      }
    }
    foreach (var property in properties) {
      this.Writer.WriteLine();
      this.WriteProperty(property.Value, indent);
    }
  }

  protected abstract void WriteProperty(PropertyDefinition pd, int indent = 0);

  public void WritePublicApi(AssemblyDefinition ad) {
    this.WriteCommentLine("=== Generated API Reference === DO NOT EDIT BY HAND ===");
    this.WriteCustomAttributes(ad);
    foreach (var md in ad.Modules) {
      this.WriteCustomAttributes(md);
    }
    this.WriteTopLevelTypes(ad);
  }

  protected void WriteSeparatedList<T>(IEnumerable<T> items, string separator, Action<T> write) {
    var first = true;
    foreach (var item in items) {
      if (!first) {
        this.Writer.Write(separator);
      }
      write(item);
      first = false;
    }
  }

  private void WriteTopLevelTypes(AssemblyDefinition ad) {
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
      this.Writer.WriteLine();
      this.CurrentNamespace = ns.Key;
      this.WriteNamespace(ns.Key, () => {
        foreach (var type in ns.Value) {
          this.WriteType(type.Value);
        }
      });
      this.CurrentNamespace = null;
    }
  }

  protected abstract void WriteType(TypeDefinition td, int indent = 2);

  protected void WriteTypeName(TypeReference tr, bool includeDeclaringType = true, bool forOutParameter = false) {
    // Check for pass-by-reference and make it ref T
    if (tr.IsByReference) {
      if (!forOutParameter) {
        this.Writer.Write("ref ");
      }
      tr = tr.GetElementType();
    }
    // Check for arrays and make them T[]
    if (tr.IsArray) {
      this.WriteTypeName(tr.GetElementType(), includeDeclaringType);
      this.Writer.Write("[]");
      return;
    }
    // Check for System.Nullable<T> and make it T?
    if (tr.TryUnwrapNullable(out var unwrapped)) {
      this.WriteTypeName(unwrapped, includeDeclaringType);
      this.Writer.Write('?');
      return;
    }
    // Check for specific framework types that have a keyword form
    if (this.WriteBuiltinTypeKeyword(tr)) {
      return;
    }
    // Otherwise, full stringification.
    if (tr.IsNested && includeDeclaringType) {
      // TODO: Possibly omit name of current type, or even all enclosing types?
      this.WriteTypeName(tr.DeclaringType, includeDeclaringType);
      this.Writer.Write('.');
    }
    else if (!string.IsNullOrEmpty(tr.Namespace) && tr.Namespace != this.CurrentNamespace) {
      this.Writer.Write(tr.Namespace);
      this.Writer.Write('.');
    }
    var name = tr.Name;
    // Strip off the part after a backtick. This used to assert that only generic types have a backtick, but non-generic nested
    // types inside a generic type can be generic while not themselves having a backtick.
    var backTick = name.IndexOf('`');
    if (backTick >= 0) {
      name = name.Substring(0, backTick);
    }
    this.Writer.Write(name);
    this.WriteGenericParameters(tr);
  }

  protected abstract void WriteTypeOf(TypeReference tr);

  protected virtual void WriteValue(TypeReference? type, object? value) {
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
        if (flags) {
          var flagsValue = value.ToULong();
          var remainingFlags = flagsValue;
          var first = true;
          foreach (var item in sortedValues) {
            var enumValue = item.Value;
            var valueFlags = enumValue.Constant.ToULong();
            // FIXME: Should we include fields with value 0?
            if ((flagsValue & valueFlags) != valueFlags) {
              continue;
            }
            if (!first) {
              this.WriteOr();
            }
            this.WriteEnumValue(td, enumValue.Name);
            remainingFlags &= ~valueFlags;
            first = false;
          }
          if (remainingFlags == 0 && !first) {
            return;
          }
          if (!first) {
            this.Writer.Write(" | ");
            value = remainingFlags;
          }
        }
        else {
          foreach (var enumValue in td.Fields) {
            if (enumValue.IsSpecialName || !enumValue.IsLiteral || !enumValue.HasConstant) {
              continue;
            }
            if (enumValue.Constant.Equals(value)) {
              this.WriteEnumValue(td, enumValue.Name);
              return;
            }
          }
        }
        this.WriteCast(td, () => this.WriteValue(null, value));
        this.Writer.Write('(');
        this.WriteTypeName(td);
        this.Writer.Write(") ");
        return;
      }
    }
    switch (value) {
      case null:
        this.WriteNull();
        break;
      case TypeReference tr:
        this.WriteTypeOf(tr);
        break;
      case bool b:
        this.WriteLiteral(b);
        break;
      case string s:
        this.WriteLiteral(s);
        break;
      default:
        // Assume everything else matches its ToString()
        this.Writer.Write(value);
        break;
    }
  }

}
