namespace Zastai.Build.ApiReference;

internal class CSharpWriter : ReferenceWriter {

  public CSharpWriter(TextWriter writer) : base(writer) {
  }

  private void WriteAttributes(EventDefinition ed) {
    if (ed.AddMethod != null) {
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
        if (!md.IsNewSlot) {
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

  protected override bool WriteBuiltinTypeKeyword(TypeReference tr) {
    var ts = tr.Module.TypeSystem;
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
    else if (tr == ts.IntPtr) {
      // Technically this is only the case if it's marked with [System.Runtime.CompilerServices.NativeIntegerAttribute]
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
      this.Writer.Write("object");
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
    else if (tr == ts.UIntPtr) {
      // Technically this is only the case if it's marked with [System.Runtime.CompilerServices.NativeIntegerAttribute]
      this.Writer.Write("nuint");
    }
    else if (tr == ts.Void) {
      this.Writer.Write("void");
    }
    else if (tr.IsCoreLibraryType("System", "Decimal")) {
      this.Writer.Write("decimal");
    }
    else if (tr.IsCoreLibraryType("System", "ValueType")) {
      this.Writer.Write("struct");
    }
    else {
      return false;
    }
    return true;
  }

  protected override void WriteCast(TypeDefinition targetType, Action writeValue) {
    this.Writer.Write('(');
    this.WriteTypeName(targetType);
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
    this.WriteTypeName(ed.EventType);
    this.Writer.Write(' ');
    this.Writer.Write(ed.Name);
    this.Writer.WriteLine(';');
  }

  protected override void WriteField(FieldDefinition fd, int indent) {
    this.WriteCustomAttributes(fd, indent);
    this.WriteIndent(indent);
    this.WriteAttributes(fd);
    this.WriteTypeName(fd.FieldType);
    this.Writer.Write(' ');
    this.Writer.Write(fd.Name);
    if (fd.IsLiteral && fd.HasConstant) {
      this.Writer.Write(" = ");
      this.WriteValue(fd.FieldType, fd.Constant);
    }
    this.Writer.WriteLine(';');
  }

  protected override void WriteGenericParameter(GenericParameter gp) {
    if (gp.IsCovariant) {
      this.Writer.Write("out ");
    }
    else if (gp.IsContravariant) {
      this.Writer.Write("in ");
    }
    this.Writer.Write(gp.Name);
  }

  protected override void WriteGenericParameterConstraints(GenericParameter parameter) {
    if (!parameter.HasConstraints) {
      return;
    }
    this.Writer.Write(" where ");
    this.Writer.Write(parameter.Name);
    this.Writer.Write(" : ");
    var first = true;
    foreach (var constraint in parameter.Constraints) {
      if (!first) {
        this.Writer.Write(", ");
      }
      this.WriteGenericParameterConstraint(constraint);
      first = false;
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
        this.Writer.Write(md.DeclaringType.Name);
      }
      else {
        this.WriteTypeName(md.ReturnType);
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
          this.WriteTypeName(md.ReturnType);
        }
        else {
          this.WriteTypeName(md.ReturnType);
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
        this.WriteTypeName(md.ReturnType);
        this.Writer.Write(' ');
        this.Writer.Write(md.Name);
      }
    }
    else {
      this.WriteTypeName(md.ReturnType);
      this.Writer.Write(' ');
      this.Writer.Write(md.Name);
    }
    this.WriteGenericParameters(md);
    this.WriteParameters(md);
    this.WriteGenericParameterConstraints(md);
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
    this.WriteTypeName(pd.ParameterType, forOutParameter: pd.IsOut);
    this.Writer.Write(' ');
    this.Writer.Write(pd.Name);
    if (!pd.HasDefault) {
      return;
    }
    this.Writer.Write(" = ");
    if (!pd.HasConstant) {
      this.Writer.Write("/* constant value missing */");
    }
    else if (pd.Constant == null && pd.ParameterType.IsValueType) {
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
    this.WriteTypeName(pd.PropertyType);
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
      this.Writer.Write("set");
    }
    this.Writer.WriteLine(';');
  }

  protected override void WriteRequiredModifierTypeName(RequiredModifierType rmt) {
    // These are weird things
    var type = rmt.ElementType;
    var modifier = rmt.ModifierType;
    // This is for "where T : unmanaged"; UnmanagedType is not a core library type though, it's in System.Runtime.InteropServices.
    if (type.IsCoreLibraryType("System", "ValueType") && modifier.FullName == "System.Runtime.InteropServices.UnmanagedType") {
      this.Writer.Write("unmanaged");
    }
    else if (type.IsByReference && modifier.IsCoreLibraryType("System.Runtime.InteropServices", "InAttribute")) {
      // This signals a `ref readonly xxx` return type
      this.Writer.Write("ref readonly ");
      this.WriteTypeName(type.GetElementType());
    }
    else {
      // Actual meanings to be determined - put the modifier in a comment for now
      this.WriteTypeName(type);
      this.Writer.Write(" /* modified by: ");
      this.WriteTypeName(modifier);
      this.Writer.Write(" */");
    }
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
    this.WriteTypeName(td, false);
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
          this.WriteTypeName(implementation.InterfaceType);
          first = false;
        }
      }
    }
    this.WriteGenericParameterConstraints(td);
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

  protected override void WriteTypeName(TypeReference tr, bool includeDeclaringType = true, bool forOutParameter = false) {
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
    // These are weird things
    if (tr is RequiredModifierType rmt) {
      this.WriteRequiredModifierTypeName(rmt);
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

  protected override void WriteTypeOf(TypeReference tr) {
    this.Writer.Write("typeof(");
    this.WriteTypeName(tr);
    this.Writer.Write(')');
  }

}
