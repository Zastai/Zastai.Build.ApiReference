namespace Zastai.Build.ApiReference;

internal class CSharpWriter : ReferenceWriter {

  public CSharpWriter(TextWriter writer) : base(writer) {
  }

  private void WriteAttributes(EventDefinition ed) {
    if (ed.AddMethod is not null) {
      this.WriteAttributes(ed.AddMethod);
    }
    else {
      this.Writer.Write("/* no add-on method */");
    }
  }

  private void WriteAttributes(FieldDefinition fd) {
    if (fd.IsPublic) {
      this.Writer.Write("public ");
    }
    else if (fd.IsFamily) {
      this.Writer.Write("protected ");
    }
    else if (fd.IsFamilyOrAssembly) {
      this.Writer.Write("protected internal ");
    }
    if (fd.IsLiteral) {
      this.Writer.Write("const ");
    }
    else {
      if (fd.IsStatic) {
        this.Writer.Write("static ");
      }
      if (fd.IsInitOnly) {
        this.Writer.Write("readonly ");
      }
    }
  }

  private void WriteAttributes(MethodDefinition md) {
    if (md.IsPublic) {
      this.Writer.Write("public ");
    }
    if (md.IsFamily) {
      this.Writer.Write("protected ");
    }
    if (md.IsFamilyOrAssembly) {
      this.Writer.Write("protected internal ");
    }
    if (md.IsStatic) {
      this.Writer.Write("static ");
    }
    if (md.IsAbstract) {
      this.Writer.Write("abstract ");
    }
    else {
      if (md.IsFinal) {
        this.Writer.Write("sealed ");
      }
      if (md.IsVirtual) {
        if (!md.IsNewSlot || md.HasCovariantReturn()) {
          this.Writer.Write("override ");
        }
        else if (!md.IsFinal) {
          this.Writer.Write("virtual ");
        }
      }
    }
  }

  private void WriteAttributes(ParameterDefinition pd) {
    if (pd.IsIn) {
      this.Writer.Write("in ");
    }
    if (pd.IsOut) {
      this.Writer.Write("out ");
    }
    if (pd.IsParamArray()) {
      this.Writer.Write("params ");
    }
  }

  private void WriteAttributes(TypeDefinition td) {
    if (td.IsPublic || td.IsNestedPublic) {
      this.Writer.Write("public ");
    }
    else if (td.IsNestedFamily) {
      this.Writer.Write("protected ");
    }
    else if (td.IsNestedFamilyOrAssembly) {
      this.Writer.Write("protected internal ");
    }
    if (td.IsClass && td.IsAbstract && td.IsSealed) {
      this.Writer.Write("static ");
    }
    else if (td.IsAbstract && !td.IsInterface) {
      this.Writer.Write("abstract ");
    }
    else if (td.IsSealed && !td.IsValueType) {
      this.Writer.Write("sealed ");
    }
  }

  protected override void WriteCast(TypeDefinition targetType, Action writeValue) {
    this.Writer.Write('(');
    this.WriteTypeName(targetType, targetType);
    this.Writer.Write(") ");
    writeValue();
  }

  protected override void WriteCommentLine(string comment) => this.Writer.WriteLine($"// {comment}".TrimEnd());

  protected override void WriteCustomAttribute(CustomAttribute ca, string? target) {
    this.Writer.Write('[');
    if (target is not null) {
      this.Writer.Write(target);
      this.Writer.Write(": ");
    }
    this.WriteTypeName(ca.AttributeType);
    if (ca.HasConstructorArguments || ca.HasFields || ca.HasProperties) {
      this.Writer.Write('(');
    }
    var first = true;
    if (ca.HasConstructorArguments) {
      foreach (var value in ca.ConstructorArguments) {
        if (!first) {
          this.Writer.Write(", ");
        }
        this.WriteCustomAttributeArgument(value);
        first = false;
      }
    }
    this.WriteNamedCustomAttributeArguments(ca, first);
    if (ca.HasConstructorArguments || ca.HasFields || ca.HasProperties) {
      this.Writer.Write(')');
    }
    this.Writer.Write(']');
  }

  private void WriteCustomAttributeArgument(CustomAttributeArgument argument) {
    if (argument.Value is CustomAttributeArgument[] array) {
      this.Writer.Write("new ");
      // FIXME: Does this need a context?
      this.WriteTypeName(argument.Type);
      this.Writer.Write(" { ");
      this.WriteSeparatedList(array, ", ", this.WriteCustomAttributeArgument);
      this.Writer.Write(" }");
    }
    else {
      this.WriteValue(argument.Type, argument.Value);
    }
  }

  protected override void WriteEnumField(FieldDefinition fd, int indent) {
    this.WriteCustomAttributes(fd, indent);
    this.WriteIndent(indent);
    if (!fd.IsPublic) {
      this.Writer.Write("/* not public */ ");
    }
    this.Writer.Write(fd.Name);
    if (fd.IsLiteral && fd.HasConstant) {
      this.Writer.Write(" = ");
      this.WriteValue(null, fd.Constant);
    }
    else if (fd.IsLiteral) {
      this.Writer.Write(" /* constant value missing */");
    }
    else {
      this.Writer.Write(" /* not a literal */");
    }
    this.Writer.WriteLine(',');
  }

  protected override void WriteEvent(EventDefinition ed, int indent) {
    this.WriteCustomAttributes(ed, indent);
    this.WriteIndent(indent);
    this.WriteAttributes(ed);
    this.Writer.Write("event ");
    this.WriteTypeName(ed.EventType, ed);
    this.Writer.Write(' ');
    this.Writer.Write(ed.Name);
    this.Writer.WriteLine(';');
  }

  protected override void WriteField(FieldDefinition fd, int indent) {
    this.WriteCustomAttributes(fd, indent);
    this.WriteIndent(indent);
    this.WriteAttributes(fd);
    this.WriteTypeName(fd.FieldType, fd);
    this.Writer.Write(' ');
    this.Writer.Write(fd.Name);
    if (fd.IsLiteral && fd.HasConstant) {
      this.Writer.Write(" = ");
      this.WriteValue(fd.FieldType, fd.Constant);
    }
    this.Writer.WriteLine(';');
  }

  private void WriteGenericParameter(GenericParameter gp) {
    if (gp.IsCovariant) {
      this.Writer.Write("out ");
    }
    else if (gp.IsContravariant) {
      this.Writer.Write("in ");
    }
    this.Writer.Write(gp.Name);
  }

  protected override void WriteGenericParameterConstraints(GenericParameter gp, int indent) {
    // Some constraints (like "class" and "new()") are stored as attributes.
    // However, there seems to be no difference between "class" and "class?", and the "notnull" constraint does not seem to be
    // reflected in the assembly at all.
    if (!gp.HasConstraints && !gp.HasReferenceTypeConstraint && !gp.HasDefaultConstructorConstraint) {
      return;
    }
    this.Writer.WriteLine();
    this.WriteIndent(indent);
    this.Writer.Write("where ");
    this.Writer.Write(gp.Name);
    this.Writer.Write(" : ");
    var first = true;
    var isValueType = gp.HasNotNullableValueTypeConstraint;
    var isUnmanaged = isValueType && gp.IsUnmanaged();
    if (gp.HasReferenceTypeConstraint) {
      this.Writer.Write("class");
      first = false;
    }
    if (gp.HasConstraints) {
      foreach (var gpc in gp.Constraints) {
        var ct = gpc.ConstraintType;
        if (first && isValueType) {
          if (isUnmanaged) {
            // Expectation: First constraint is on ValueType modified by UnmanagedType; if so, write that as "unmanaged"
            // Note that UnmanagedType is neither a core library type nor a locally synthesized one.
            if (ct is RequiredModifierType rmt && rmt.ElementType.IsCoreLibraryType("System", "ValueType") &&
                rmt.ModifierType.FullName == "System.Runtime.InteropServices.UnmanagedType") {
              this.WriteCustomAttributes(gpc, -1);
              this.Writer.Write("unmanaged");
              first = false;
              isValueType = false;
              continue;
            }
          }
          else {
            // Expectation: First constraint is on ValueType; if so, write that as "struct"
            if (ct.IsCoreLibraryType("System", "ValueType")) {
              this.WriteCustomAttributes(gpc, -1);
              this.Writer.Write("struct");
              first = false;
              isValueType = false;
              continue;
            }
          }
        }
        if (!first) {
          this.Writer.Write(", ");
        }
        this.WriteCustomAttributes(gpc, -1);
        this.WriteTypeName(ct, gp);
        if (gpc.IsNullable()) {
          this.Writer.Write('?');
        }
        first = false;
      }
    }
    // These should have been issued above, but make sure
    if (isValueType) {
      if (!first) {
        this.Writer.Write(", ");
      }
      this.Writer.Write(isUnmanaged ? "unmanaged" : "struct");
      first = false;
    }
    if (gp.HasDefaultConstructorConstraint && !gp.HasNotNullableValueTypeConstraint) {
      if (!first) {
        this.Writer.Write(", ");
      }
      this.Writer.Write("new()");
    }
  }

  protected override void WriteGenericParameters(IGenericParameterProvider provider) {
    if (provider is IGenericInstance gi) {
      if (!gi.HasGenericArguments) {
        // FIXME: Can this happen? Is it an error? Should it produce <>?
        return;
      }
      this.Writer.Write('<');
      var first = true;
      foreach (var argument in gi.GenericArguments) {
        if (!first) {
          this.Writer.Write(", ");
        }
        if (argument.IsGenericParameter) {
          this.WriteGenericParameter((GenericParameter) argument);
        }
        else {
          // FIXME: Does this need a context?
          this.WriteTypeName(argument);
        }
        first = false;
      }
      this.Writer.Write('>');
    }
    else {
      if (!provider.HasGenericParameters) {
        return;
      }
      this.Writer.Write('<');
      var first = true;
      foreach (var parameter in provider.GenericParameters) {
        if (!first) {
          this.Writer.Write(", ");
        }
        this.WriteGenericParameter(parameter);
        first = false;
      }
      this.Writer.Write('>');
    }
  }

  protected override void WriteLiteral(bool value) => this.Writer.Write(value ? "true" : "false");

  protected override void WriteLiteral(string value) {
    var sb = new StringBuilder();
    sb.Append('"');
    foreach (var c in value) {
      switch (c) {
        case '\0':
          sb.Append("\\0");
          break;
        case '\\':
          sb.Append("\\\\");
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
            sb.Append(c > 0xff ? $"\\u{codePoint:x4}" : $"\\x{codePoint:x2}");
          }
          else {
            sb.Append(c);
          }
          break;
      }
    }
    sb.Append('"');
    this.Writer.Write(sb.ToString());
  }

  protected override void WriteMethod(MethodDefinition md, int indent) {
    this.WriteCustomAttributes(md, indent);
    this.WriteCustomAttributes(md.MethodReturnType.CustomAttributes, "return", indent);
    this.WriteIndent(indent);
    this.WriteAttributes(md);
    if (md.IsReadOnly()) {
      this.Writer.Write("readonly ");
    }
    if (md.IsRuntimeSpecialName) {
      // Runtime-Special Names
      if (md.Name is ".ctor" or ".cctor") {
        this.Writer.Write(md.DeclaringType.NonGenericName());
      }
      else {
        this.WriteTypeName(md.ReturnType, md.MethodReturnType);
        this.Writer.Write(" /* TODO: Map RunTime-Special Method Name Correctly */");
        this.Writer.Write(' ');
        this.Writer.Write(md.Name);
      }
    }
    else if (md.IsSpecialName) {
      // Other Special Names - these will probably look/work fine if not treated specially
      if (md.Name.StartsWith("op_")) {
        var op = md.Name.Substring(3);
        if (op is "Explicit" or "Implicit") {
          this.Writer.Write(op.ToLowerInvariant());
          this.Writer.Write(" operator ");
          this.WriteTypeName(md.ReturnType, md.MethodReturnType);
        }
        else {
          this.WriteTypeName(md.ReturnType, md.MethodReturnType);
          this.Writer.Write(" operator ");
          switch (op) {
            // Relational
            case "Equality":
              this.Writer.Write("==");
              break;
            case "GreaterThan":
              this.Writer.Write('>');
              break;
            case "GreaterThanOrEqual":
              this.Writer.Write(">=");
              break;
            case "Inequality":
              this.Writer.Write("!=");
              break;
            case "LessThan":
              this.Writer.Write('<');
              break;
            case "LessThanOrEqual":
              this.Writer.Write("<=");
              break;
            // Logical
            case "False":
              this.Writer.Write("false");
              break;
            case "LogicalNot":
              this.Writer.Write('!');
              break;
            case "True":
              this.Writer.Write("true");
              break;
            // Arithmetic
            case "Addition":
            case "UnaryPlus":
              this.Writer.Write('+');
              break;
            case "BitwiseAnd":
              this.Writer.Write("&");
              break;
            case "BitwiseOr":
              this.Writer.Write("|");
              break;
            case "Decrement":
              this.Writer.Write("--");
              break;
            case "Division":
              this.Writer.Write('/');
              break;
            case "ExclusiveOr":
              this.Writer.Write('^');
              break;
            case "Exponent":
              // No C# operator for this
              this.Writer.Write("**");
              break;
            case "Increment":
              this.Writer.Write("++");
              break;
            case "LeftShift":
              this.Writer.Write("<<");
              break;
            case "Modulus":
              this.Writer.Write('%');
              break;
            case "Multiply":
              this.Writer.Write('*');
              break;
            case "OnesComplement":
              this.Writer.Write('~');
              break;
            case "RightShift":
              this.Writer.Write(">>");
              break;
            case "Subtraction":
            case "UnaryNegation":
              this.Writer.Write('-');
              break;
            default:
              this.Writer.Write("/* TODO: Map Operator Correctly */ ");
              this.Writer.Write(op);
              break;
          }
        }
      }
      else {
        this.Writer.Write("/* TODO: Map Special Method Name Correctly */ ");
        this.WriteTypeName(md.ReturnType, md.MethodReturnType);
        this.Writer.Write(' ');
        this.Writer.Write(md.Name);
      }
    }
    else {
      this.WriteTypeName(md.ReturnType, md.MethodReturnType);
      this.Writer.Write(' ');
      this.Writer.Write(md.Name);
    }
    this.WriteGenericParameters(md);
    this.WriteParameters(md);
    this.WriteGenericParameterConstraints(md, indent + 2);
    this.Writer.WriteLine(";");
  }

  protected override void WriteNamedCustomAttributeArgument(string name, CustomAttributeArgument value, bool first) {
    if (!first) {
      this.Writer.Write(", ");
    }
    this.Writer.Write("{0} = ", name);
    this.WriteCustomAttributeArgument(value);
  }

  protected override void WriteNamespaceFooter() {
    this.Writer.WriteLine();
    this.Writer.WriteLine('}');
  }

  protected override void WriteNamespaceHeader() {
    this.Writer.WriteLine();
    this.Writer.Write("namespace ");
    this.Writer.Write(this.CurrentNamespace);
    this.Writer.WriteLine(" {");
  }

  protected override void WriteNull() => this.Writer.Write("null");

  protected override void WriteOr() => this.Writer.Write(" | ");

  private void WriteParameter(ParameterDefinition pd) {
    this.WriteCustomAttributes(pd, -1);
    this.WriteAttributes(pd);
    this.WriteTypeName(pd.ParameterType, pd);
    this.Writer.Write(' ');
    this.Writer.Write(pd.Name);
    if (!pd.HasDefault) {
      return;
    }
    this.Writer.Write(" = ");
    if (!pd.HasConstant) {
      this.Writer.Write("/* constant value missing */");
    }
    else if (pd.Constant is null && pd.ParameterType.IsValueType) {
      this.Writer.Write("default");
    }
    else {
      this.WriteValue(pd.ParameterType, pd.Constant);
    }
  }

  private void WriteParameters(IEnumerable<ParameterDefinition> parameters) {
    var first = true;
    foreach (var parameter in parameters) {
      if (!first) {
        this.Writer.Write(", ");
      }
      this.WriteParameter(parameter);
      first = false;
    }
  }

  private void WriteParameters(MethodDefinition md) {
    this.Writer.Write('(');
    if (md.HasParameters) {
      // Detect extension methods
      if (md.HasCustomAttributes) {
        foreach (var ca in md.CustomAttributes) {
          if (ca.AttributeType.IsCoreLibraryType("System.Runtime.CompilerServices", "ExtensionAttribute")) {
            this.Writer.Write("this ");
            break;
          }
        }
      }
      this.WriteParameters(md.Parameters);
    }
    this.Writer.Write(')');
  }

  private void WriteParameters(PropertyDefinition pd) {
    if (!pd.HasParameters) {
      return;
    }
    // Assumption: only indexers have parameters, and they use []
    this.Writer.Write('[');
    this.WriteParameters(pd.Parameters);
    this.Writer.Write(']');
  }

  protected override void WriteProperty(PropertyDefinition pd, int indent) {
    this.WriteCustomAttributes(pd, indent);
    Trace.Assert(!pd.HasOtherMethods, $"Property has 'other methods' which is not yet supported: {pd}.");
    this.WriteIndent(indent);
    this.WriteTypeName(pd.PropertyType, pd);
    this.Writer.Write(' ');
    // FIXME: Or should this only be done when the type has [System.Reflection.DefaultMemberAttribute("Item")]?
    if (pd.HasParameters && pd.Name == "Item") {
      this.Writer.Write("this");
    }
    else {
      this.Writer.Write(pd.Name);
    }
    this.WriteParameters(pd);
    this.Writer.WriteLine(" {");
    this.WritePropertyAccessor(pd.GetMethod.IfPublicApi(), indent + 2);
    this.WritePropertyAccessor(pd.SetMethod.IfPublicApi(), indent + 2);
    if (pd.HasOtherMethods) {
      this.WriteIndent(indent + 2);
      this.Writer.WriteLine("/* unsupported: \"other methods\" */");
    }
    this.WriteIndent(indent);
    this.Writer.WriteLine('}');
  }

  private void WritePropertyAccessor(MethodDefinition? method, int indent) {
    if (method is null) {
      return;
    }
    this.WriteCustomAttributes(method, indent);
    this.WriteIndent(indent);
    this.WriteAttributes(method);
    if (method.IsReadOnly()) {
      this.Writer.Write("readonly ");
    }
    if (method.IsGetter) {
      this.Writer.Write("get");
    }
    else if (method.IsSetter) {
      // For `init`, it's not an attribute on the setter method, but rather a "required modifier" of
      // on its return type (void). A bit weird, but whatever.
      if (method.ReturnType is RequiredModifierType rmt && rmt.ElementType == rmt.Module.TypeSystem.Void &&
          rmt.ModifierType.IsCoreLibraryType("System.Runtime.CompilerServices", "IsExternalInit")) {
        this.Writer.Write("init");
      }
      else {
        this.Writer.Write("set");
      }
    }
    this.Writer.WriteLine(';');
  }

  protected override void WriteType(TypeDefinition td, int indent) {
    this.WriteCustomAttributes(td, indent);
    Trace.Assert(td.IsPublicApi(), $"Type {td} has unsupported access: {td.Attributes}.");
    this.WriteIndent(indent);
    this.WriteAttributes(td);
    if (td.IsEnum) {
      this.Writer.Write("enum");
    }
    else if (td.IsInterface) {
      this.Writer.Write("interface");
    }
    else if (td.IsValueType) {
      if (td.IsReadOnly()) {
        this.Writer.Write("readonly ");
      }
      if (td.IsByRefLike()) {
        this.Writer.Write("ref ");
      }
      this.Writer.Write("struct");
    }
    else if (td.IsClass) {
      // TODO: Maybe detect delegates; but then what of explicitly written classes deriving from MultiCastDelegate?
      this.Writer.Write("class");
    }
    else { // What else can it be?
      Trace.Fail($"Type {td} has unsupported classification: {td.Attributes}.");
    }
    this.Writer.Write(' ');
    this.WriteTypeName(td, td);
    {
      var isDerived = false;
      var baseType = td.BaseType;
      if (baseType is not null) {
        if (td.IsClass && baseType.IsCoreLibraryType("System", "Object")) {
          baseType = null;
        }
        else if (td.IsEnum && baseType.IsCoreLibraryType("System", "Enum")) {
          baseType = null;
        }
        else if (td.IsValueType && baseType.IsCoreLibraryType("System", "ValueType")) {
          baseType = null;
        }
      }
      if (baseType is null && td.IsEnum) {
        // Look for the special-named 'value__' field and use its type
        if (td.HasFields) {
          foreach (var fd in td.Fields) {
            if (fd.IsSpecialName && fd.Name == "value__") {
              // If it's Int32, leave it off
              if (fd.FieldType != fd.Module.TypeSystem.Int32) {
                baseType = fd.FieldType;
              }
            }
          }
        }
      }
      if (baseType is not null) {
        isDerived = true;
        this.Writer.Write(" : ");
        // FIXME: Does this need a context?
        this.WriteTypeName(baseType);
      }
      if (td.HasInterfaces) {
        if (!isDerived) {
          this.Writer.Write(" : ");
        }
        var first = true;
        foreach (var implementation in td.Interfaces) {
          if (!first) {
            this.Writer.Write(", ");
          }
          this.WriteCustomAttributes(implementation, -1);
          // FIXME: Does this need a context?
          this.WriteTypeName(implementation.InterfaceType);
          first = false;
        }
      }
    }
    this.WriteGenericParameterConstraints(td, indent + 2);
    this.Writer.WriteLine(" {");
    this.WriteFields(td, indent + 2);
    this.WriteProperties(td, indent + 2);
    this.WriteEvents(td, indent + 2);
    this.WriteMethods(td, indent + 2);
    this.WriteNestedTypes(td, indent + 2);
    this.Writer.WriteLine();
    this.WriteIndent(indent);
    this.Writer.WriteLine('}');
  }

  private void WriteTypeName(TypeReference tr, ref int dynamicIndex, ref int integerIndex, ICustomAttributeProvider? context) {
    // Note: these checks used to use things like `IsArray` and then `GetElementType()` to get at the contents. However,
    // `GetElementType()` gets the _innermost_ element type (https://github.com/jbevain/cecil/issues/841). So given we need casts
    // anyway to access the `ElementType` property, and this isn't very performance-critical code, we just use pattern matching to
    // keep things readable.
    switch (tr) {
      case ArrayType at: { // => T[]
        // Any reference type, including an array, gets an entry in [Dynamic]
        ++dynamicIndex;
        this.WriteTypeName(at.ElementType, ref dynamicIndex, ref integerIndex, context);
        this.Writer.Write("[]");
        return;
      }
      case OptionalModifierType omt: {
        var type = omt.ElementType;
        var modifier = omt.ModifierType;
        // Actual meanings to be determined - put the modifier in a comment for now
        this.WriteTypeName(type, ref dynamicIndex, ref integerIndex, context);
        this.Writer.Write(" /* optionally modified by: ");
        // FIXME: Does this need a context? Should it affect include the indexes?
        this.WriteTypeName(modifier);
        this.Writer.Write(" */");
        return;
      }
      case PointerType pt: // => T*
        this.WriteTypeName(pt.ElementType, ref dynamicIndex, ref integerIndex, context);
        this.Writer.Write("*");
        return;
      case RequiredModifierType rmt: {
        var type = rmt.ElementType;
        var modifier = rmt.ModifierType;
        if (modifier.IsCoreLibraryType("System.Runtime.InteropServices", "InAttribute") && type is ByReferenceType brt) {
          // This signals a `ref readonly xxx` return type
          this.Writer.Write("ref readonly ");
          tr = brt.ElementType;
          break;
        }
        // Actual meanings to be determined - put the modifier in a comment for now
        this.WriteTypeName(type, ref dynamicIndex, ref integerIndex, context);
        this.Writer.Write(" /* modified by: ");
        // FIXME: Does this need a context? Should it affect include the indexes?
        this.WriteTypeName(modifier);
        this.Writer.Write(" */");
        return;
      }
    }
    // Check for System.Nullable<T> and make it T?
    if (tr.TryUnwrapNullable(out var unwrapped)) {
      this.WriteTypeName(unwrapped, ref dynamicIndex, ref integerIndex, context);
      this.Writer.Write('?');
      return;
    }
    { // Check for specific framework types that have a keyword form
      var isBuiltinType = true;
      var ts = tr.Module.TypeSystem;
      if (tr.IsPrimitive) {

      }
      if (tr == ts.Boolean) {
        this.Writer.Write("bool");
      }
      else if (tr == ts.Byte) {
        this.Writer.Write("byte");
      }
      else if (tr == ts.Char) {
        this.Writer.Write("char");
      }
      else if (tr == ts.Double) {
        this.Writer.Write("double");
      }
      else if (tr == ts.Int16) {
        this.Writer.Write("short");
      }
      else if (tr == ts.Int32) {
        this.Writer.Write("int");
      }
      else if (tr == ts.Int64) {
        this.Writer.Write("long");
      }
      else if (tr == ts.IntPtr && context.IsNativeInteger(integerIndex++)) {
        this.Writer.Write("nint");
      }
      else if (tr == ts.SByte) {
        this.Writer.Write("sbyte");
      }
      else if (tr == ts.Single) {
        this.Writer.Write("float");
      }
      else if (tr == ts.String) {
        this.Writer.Write("string");
      }
      else if (tr == ts.Object) {
        this.Writer.Write(context.IsDynamic(dynamicIndex) ? "dynamic" : "object");
      }
      else if (tr == ts.UInt16) {
        this.Writer.Write("ushort");
      }
      else if (tr == ts.UInt32) {
        this.Writer.Write("uint");
      }
      else if (tr == ts.UInt64) {
        this.Writer.Write("ulong");
      }
      else if (tr == ts.UIntPtr && context.IsNativeInteger(integerIndex++)) {
        this.Writer.Write("nuint");
      }
      else if (tr == ts.Void) {
        this.Writer.Write("void");
      }
      else if (tr.IsCoreLibraryType("System", "Decimal")) {
        this.Writer.Write("decimal");
      }
      else {
        isBuiltinType = false;
      }
      if (isBuiltinType) {
        ++dynamicIndex;
        return;
      }
    }
    // Any type gets an entry in [Dynamic]
    ++dynamicIndex;
    // Check for C# tuples (i.e. System.ValueTuple)
    if (tr.IsGenericInstance && tr.IsCoreLibraryType() && tr.Namespace == "System" && tr.Name.StartsWith("ValueTuple`")) {
      this.Writer.Write('(');
      var element = 0;
      var elementNames = context.GetTupleElementNames();
    moreGenericArguments:
      var genericArguments = ((GenericInstanceType) tr).GenericArguments;
      var item = 0;
      foreach (var ga in genericArguments) {
        if (++item == 8 && tr.Name == "ValueTuple`8") {
          // a 10-element tuple is an 8-element tuple where the 8th element is a 3-element tuple
          if (ga.IsGenericInstance && ga.IsCoreLibraryType() && ga.Namespace == "System" && ga.Name.StartsWith("ValueTuple`")) {
            // skip this type
            ++dynamicIndex;
            // switch to this one and continue processing
            tr = ga;
            goto moreGenericArguments;
          }
        }
        if (element > 0) {
          this.Writer.Write(", ");
        }
        this.WriteTypeName(ga, ref dynamicIndex, ref integerIndex, context);
        if (elementNames is not null) {
          if (element >= elementNames.Length) {
            this.Writer.Write(" /* name missing */");
          }
          else {
            var elementName = elementNames[element];
            if (elementName is not null) {
              this.Writer.Write(' ');
              this.Writer.Write(elementName);
            }
          }
        }
        ++element;
      }
      this.Writer.Write(')');
      return;
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
      this.Writer.Write("delegate* ");
      this.WriteCustomAttributes(fpt.MethodReturnType, -1);
      var returnType = fpt.ReturnType;
      var callingConventions = new List<string>();
      // https://github.com/jbevain/cecil/issues/842 - no enum entry for this yet
      if (fpt.CallingConvention == (MethodCallingConvention) 9) {
        // look for (and drop) modopt(.CallConvXXX) on the return type, keeping the XXXs
        while (returnType is OptionalModifierType omt) {
          var modifier = omt.ModifierType;
          if (modifier.IsCoreLibraryType("System.Runtime.CompilerServices") && modifier.Name.StartsWith("CallConv")) {
            callingConventions.Add(modifier.Name.Substring(8));
            returnType = omt.ElementType;
            continue;
          }
          break;
        }
      }
      this.WriteTypeName(returnType, ref dynamicIndex, ref integerIndex, context);
      this.Writer.Write(' ');
      switch (fpt.CallingConvention) {
        case MethodCallingConvention.Default:
          this.Writer.Write("managed");
          break;
        case MethodCallingConvention.C:
          this.Writer.Write("unmanaged[Cdecl] ");
          break;
        case MethodCallingConvention.FastCall:
          this.Writer.Write("unmanaged[Fastcall] ");
          break;
        case MethodCallingConvention.StdCall:
          this.Writer.Write("unmanaged[Stdcall] ");
          break;
        case MethodCallingConvention.ThisCall:
          this.Writer.Write("unmanaged[Thiscall] ");
          break;
        // https://github.com/jbevain/cecil/issues/842 - no enum entry for this yet
        case (MethodCallingConvention) 9:
          this.Writer.Write("unmanaged");
          if (callingConventions.Count > 0) {
            this.Writer.Write('[');
            this.Writer.Write(string.Join(", ", callingConventions));
            this.Writer.Write("] ");
          }
          break;
        default:
          this.Writer.Write($"unmanaged /* unhandled calling convention: {(int) fpt.CallingConvention} */ ");
          break;
      }
      this.Writer.Write('<');
      if (fpt.HasParameters) {
        var first = true;
        foreach (var parameter in fpt.Parameters) {
          if (!first) {
            this.Writer.Write(", ");
          }
          this.WriteCustomAttributes(parameter, -1);
          this.WriteTypeName(parameter.ParameterType, ref dynamicIndex, ref integerIndex, context);
          first = false;
        }
      }
      this.Writer.Write('>');
      return;
    }
    // TODO: Check for function pointers
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
        // FIXME: Does this need a context? Should it continue our indexes?
        this.WriteTypeName(tr.DeclaringType);
        this.Writer.Write('.');
      }
    }
    else if (!string.IsNullOrEmpty(tr.Namespace) && tr.Namespace != this.CurrentNamespace) {
      this.Writer.Write(tr.Namespace);
      this.Writer.Write('.');
    }
    this.Writer.Write(tr.NonGenericName());
    this.WriteGenericParameters(tr);
  }

  protected override void WriteTypeName(TypeReference tr, ICustomAttributeProvider? context = null) {
    // ref X can only occur on outer types; can't have an array of `ref int` or a `Func<ref int>`, so handle that here
    if (tr is ByReferenceType brt) { // => ref T
      // omit the "ref" for "out" parameters - it's covered by the "out"
      if (context is not ParameterDefinition { IsOut: true }) {
        this.Writer.Write("ref ");
      }
      tr = brt.ElementType;
    }
    // 3 things we care about are handled by attributes on the context:
    // - [Dynamic] to distinguish `dynamic` from `object`
    //   - in simple/normal context this has no arguments
    //   - in a context with multiple types (`dynamic[]`, tuple, ...) it has an array with 1 bool argument per type
    // - [NativeInteger] to distinguish n[u]int from [U]IntPtr
    //   - in simple/normal context this has no arguments
    //   - in a context with multiple types it has an array with 1 bool argument per `[U]IntPtr`
    // - [Nullable] for nullable reference types
    //   - in normal context this has one argument
    //   - in a context with multiple types it has an array with one byte argument per reference type
    //   - but there is also some degree of inheritance, using [NullableContext]
    // As a result, we need to keep track of separate indexes for each type.
    // However, nullable reference types are a bit of a nightmare with [NullableContext] also in play, so leave those off for now.
    var dynamicIndex = 0;
    var integerIndex = 0;
    this.WriteTypeName(tr, ref dynamicIndex, ref integerIndex, context);
  }

  protected override void WriteTypeOf(TypeReference tr) {
    this.Writer.Write("typeof(");
    // FIXME: Does this need a context?
    this.WriteTypeName(tr);
    this.Writer.Write(')');
  }

}
