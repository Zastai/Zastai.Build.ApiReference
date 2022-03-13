using System.Linq;

namespace Zastai.Build.ApiReference;

internal class CSharpFormatter : CodeFormatter {

  protected override string AssemblyAttributeLine(string attribute) => $"[assembly: {attribute}]";

  private string Attributes(MethodDefinition md) {
    var sb = new StringBuilder();
    if (md.IsPublic) {
      sb.Append("public ");
    }
    if (md.IsFamily) {
      sb.Append("protected ");
    }
    if (md.IsFamilyOrAssembly) {
      sb.Append("protected internal ");
    }
    if (md.IsStatic) {
      sb.Append("static ");
    }
    if (md.IsAbstract) {
      sb.Append("abstract ");
    }
    else {
      if (md.IsFinal) {
        sb.Append("sealed ");
      }
      if (md.IsVirtual) {
        if (!md.IsNewSlot || md.HasCovariantReturn()) {
          sb.Append("override ");
        }
        else if (!md.IsFinal) {
          sb.Append("virtual ");
        }
      }
    }
    return sb.ToString();
  }

  protected override string Cast(TypeDefinition targetType, string value) {
    // FIXME: Does this need a context?
    return $"({this.TypeName(targetType)}) {value}";
  }

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

  protected override string CustomAttributeArgument(CustomAttributeArgument argument) {
    if (argument.Value is not CustomAttributeArgument[] array) {
      return this.Value(argument.Type, argument.Value);
    }
    var arguments = array.Select(this.CustomAttributeArgument);
    var sb = new StringBuilder();
    sb.Append("new ").Append(this.TypeName(argument.Type)).Append(" { ").AppendJoin(", ", arguments).Append(" }");
    return sb.ToString();
  }

  protected override IEnumerable<string?> CustomAttributes(ICustomAttributeProvider cap, int indent) {
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

  protected override string EnumField(FieldDefinition fd, int indent) {
    var sb = new StringBuilder();
    sb.Append(' ', indent);
    if (!fd.IsPublic) {
      sb.Append("/* not public */ ");
    }
    sb.Append(fd.Name);
    if (fd.IsLiteral) {
      sb.Append(" = ").Append(fd.HasConstant ? this.Value(null, fd.Constant) : " /* constant value missing */");
    }
    else {
      sb.Append(" /* not a literal */");
    }
    sb.Append(',');
    return sb.ToString();
  }

  protected override string Event(EventDefinition ed, int indent) {
    var sb = new StringBuilder();
    sb.Append(' ', indent)
      .Append(ed.AddMethod is not null ? this.Attributes(ed.AddMethod) : "/* no add-on method */ ")
      .Append("event ").Append(this.TypeName(ed.EventType, ed)).Append(' ').Append(ed.Name).Append(';');
    return sb.ToString();
  }

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

  protected override string Field(FieldDefinition fd, int indent) {
    var sb = new StringBuilder();
    sb.Append(' ', indent);
    if (fd.IsPublic) {
      sb.Append("public ");
    }
    else if (fd.IsFamily) {
      sb.Append("protected ");
    }
    else if (fd.IsFamilyOrAssembly) {
      sb.Append("protected internal ");
    }
    if (fd.IsLiteral) {
      sb.Append("const ");
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
    if (fd.IsLiteral) {
      sb.Append(" = ").Append(fd.HasConstant ? this.Value(null, fd.Constant) : " /* constant value missing */");
    }
    sb.Append(';');
    return sb.ToString();
  }

  private string GenericParameter(GenericParameter gp) {
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

  protected override string? GenericParameterConstraints(GenericParameter gp) {
    // Some constraints (like "class" and "new()") are stored as attributes.
    // However, there seems to be no difference between "class" and "class?", and the "notnull" constraint does not seem to be
    // reflected in the assembly at all.
    if (!gp.HasConstraints && !gp.HasReferenceTypeConstraint && !gp.HasDefaultConstructorConstraint) {
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
            if (ct is RequiredModifierType rmt && rmt.ElementType.IsCoreLibraryType("System", "ValueType") &&
                rmt.ModifierType.FullName == "System.Runtime.InteropServices.UnmanagedType") {
              sb.Append(this.CustomAttributesInline(gpc)).Append("unmanaged");
              first = false;
              isValueType = false;
              continue;
            }
          }
          else {
            // Expectation: First constraint is on ValueType; if so, write that as "struct"
            if (ct.IsCoreLibraryType("System", "ValueType")) {
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
        if (gpc.IsNullable()) {
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
    if (gp.HasDefaultConstructorConstraint && !gp.HasNotNullableValueTypeConstraint) {
      if (!first) {
        sb.Append(", ");
      }
      sb.Append("new()");
    }
    return sb.ToString();
  }

  protected virtual string GenericParameters(IGenericParameterProvider provider) {
    var sb = new StringBuilder();
    if (provider is IGenericInstance gi) {
      // FIXME: Can this be false? If so, is it an error? Or should it produce <>?
      if (gi.HasGenericArguments) {
        var arguments = new List<string>();
        foreach (var argument in gi.GenericArguments) {
          if (argument is GenericParameter gp) {
            arguments.Add(this.GenericParameter(gp));
          }
          else {
            // FIXME: Does this need a context?
            arguments.Add(this.TypeName(argument));
          }
        }
        sb.Append('<').AppendJoin(", ", arguments).Append('>');
      }
    }
    else if (provider.HasGenericParameters) {
      sb.Append('<').AppendJoin(", ", provider.GenericParameters.Select(this.GenericParameter)).Append('>');
    }
    return sb.ToString();
  }

  protected override string LineComment(string comment) => $"// {comment}".TrimEnd();

  protected override string Literal(bool value) => value ? "true" : "false";

  protected override string Literal(string value) {
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
    return sb.ToString();
  }

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
    sb.Append(' ', indent).Append(this.Attributes(md));
    if (md.IsReadOnly()) {
      sb.Append("readonly ");
    }
    if (md.IsRuntimeSpecialName) {
      // Runtime-Special Names
      if (md.Name is ".ctor" or ".cctor") {
        sb.Append(md.DeclaringType.NonGenericName());
      }
      else {
        sb.Append(this.TypeName(md.ReturnType, md.MethodReturnType))
          .Append(" /* TODO: Map RunTime-Special Method Name Correctly */ ")
          .Append(md.Name);
      }
    }
    else if (md.IsSpecialName) {
      // Other Special Names - these will probably look/work fine if not treated specially
      if (md.Name.StartsWith("op_")) {
        var op = md.Name.Substring(3);
        if (op is "Explicit" or "Implicit") {
          sb.Append(op.ToLowerInvariant()).Append(" operator ").Append(this.TypeName(md.ReturnType, md.MethodReturnType));
        }
        else {
          sb.Append(this.TypeName(md.ReturnType, md.MethodReturnType)).Append(" operator ");
          switch (op) {
            // Relational
            case "Equality":
              sb.Append("==");
              break;
            case "GreaterThan":
              sb.Append('>');
              break;
            case "GreaterThanOrEqual":
              sb.Append(">=");
              break;
            case "Inequality":
              sb.Append("!=");
              break;
            case "LessThan":
              sb.Append('<');
              break;
            case "LessThanOrEqual":
              sb.Append("<=");
              break;
            // Logical
            case "False":
              sb.Append("false");
              break;
            case "LogicalNot":
              sb.Append('!');
              break;
            case "True":
              sb.Append("true");
              break;
            // Arithmetic
            case "Addition":
            case "UnaryPlus":
              sb.Append('+');
              break;
            case "BitwiseAnd":
              sb.Append("&");
              break;
            case "BitwiseOr":
              sb.Append("|");
              break;
            case "Decrement":
              sb.Append("--");
              break;
            case "Division":
              sb.Append('/');
              break;
            case "ExclusiveOr":
              sb.Append('^');
              break;
            case "Exponent":
              // No C# operator for this
              sb.Append("**");
              break;
            case "Increment":
              sb.Append("++");
              break;
            case "LeftShift":
              sb.Append("<<");
              break;
            case "Modulus":
              sb.Append('%');
              break;
            case "Multiply":
              sb.Append('*');
              break;
            case "OnesComplement":
              sb.Append('~');
              break;
            case "RightShift":
              sb.Append(">>");
              break;
            case "Subtraction":
            case "UnaryNegation":
              sb.Append('-');
              break;
            default:
              sb.Append("/* TODO: Map Operator Correctly */ ").Append(op);
              break;
          }
        }
      }
      else {
        sb.Append(this.TypeName(md.ReturnType, md.MethodReturnType))
          .Append(" /* TODO: Map Special Method Name Correctly */ ")
          .Append(md.Name);
      }
    }
    else {
      sb.Append(this.TypeName(md.ReturnType, md.MethodReturnType)).Append(' ').Append(md.Name);
    }
    sb.Append(this.GenericParameters(md)).Append(this.Parameters(md));
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

  protected override string ModuleAttributeLine(string attribute) => $"[module: {attribute}]";

  protected override IEnumerable<string?> NamespaceFooter() {
    yield return null;
    yield return "}";
  }

  protected override IEnumerable<string?> NamespaceHeader() {
    yield return null;
    yield return $"namespace {this.CurrentNamespace} {{";
  }

  protected override string Null() => "null";

  protected override string Or() => "|";

  private string Parameter(ParameterDefinition pd) {
    var sb = new StringBuilder();
    sb.Append(this.CustomAttributesInline(pd));
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
          if (ca.AttributeType.IsCoreLibraryType("System.Runtime.CompilerServices", "ExtensionAttribute")) {
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

  protected override IEnumerable<string?> Property(PropertyDefinition pd, int indent) {
    foreach (var line in this.CustomAttributes(pd, indent)) {
      yield return line;
    }
    {
      var sb = new StringBuilder();
      sb.Append(' ', indent).Append(this.TypeName(pd.PropertyType, pd)).Append(' ');
      // FIXME: Or should this only be done when the type has [System.Reflection.DefaultMemberAttribute("Item")]?
      if (pd.HasParameters && pd.Name == "Item") {
        sb.Append("this");
      }
      else {
        sb.Append(pd.Name);
      }
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
          rmt.ModifierType.IsCoreLibraryType("System.Runtime.CompilerServices", "IsExternalInit")) {
        sb.Append("init");
      }
      else {
        sb.Append("set");
      }
    }
    sb.Append(';');
    yield return sb.ToString();
  }

  protected override IEnumerable<string?> Type(TypeDefinition td, int indent) {
    foreach (var line in this.CustomAttributes(td, indent)) {
      yield return line;
    }
    if (!td.IsPublicApi()) {
      yield return this.LineComment($"ERROR: Type {td} has unsupported access: {td.Attributes}.");
    }
    {
      var sb = new StringBuilder();
      sb.Append(' ', indent);
      if (td.IsPublic || td.IsNestedPublic) {
        sb.Append("public ");
      }
      else if (td.IsNestedFamily) {
        sb.Append("protected ");
      }
      else if (td.IsNestedFamilyOrAssembly) {
        sb.Append("protected internal ");
      }
      if (td.IsClass && td.IsAbstract && td.IsSealed) {
        sb.Append("static ");
      }
      else if (td.IsAbstract && !td.IsInterface) {
        sb.Append("abstract ");
      }
      else if (td.IsSealed && !td.IsValueType) {
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
      sb.Append(' ').Append(this.TypeName(td, td));
      {
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
        if (baseType is not null || td.HasInterfaces) {
          sb.Append(" : ");
        }
        if (baseType is not null) {
          // FIXME: Does this need a context?
          sb.Append(this.TypeName(baseType));
          if (td.HasInterfaces) {
            sb.Append(", ");
          }
        }
        if (td.HasInterfaces) {
          // Ensure these are emitted sorted
          var interfaces = new SortedDictionary<string, string>();
          foreach (var implementation in td.Interfaces) {
            var type = this.TypeName(implementation.InterfaceType);
            interfaces.Add(type, this.CustomAttributesInline(implementation) + type);
          }
          sb.AppendJoin(", ", interfaces.Values);
        }
      }
      var constraints = this.GenericParameterConstraints(td, indent + 2).ToList();
      if (constraints.Count == 0) {
        sb.Append(" {");
      }
      else {
        constraints[constraints.Count - 1] += " {";
      }
      yield return sb.ToString();
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
    {
      var sb = new StringBuilder();
      sb.Append(' ', indent).Append('}');
      yield return sb.ToString();
    }
  }

  protected virtual string TypeName(ExportedType et) {
    var sb = new StringBuilder();
    if (et.DeclaringType is not null) {
      sb.Append(this.TypeName(et.DeclaringType)).Append('.');
    }
    else if (!string.IsNullOrEmpty(et.Namespace)) {
      sb.Append(et.Namespace).Append('.');
    }
    // There is no provision for generic parameters on ExportedType, so this will have the "ugly name" (Foo`3).
    // We _could_ detect that and map it to Foo<T1, T2, T3> here.
    sb.Append(et.Name);
    return sb.ToString();
  }

  private string TypeName(TypeReference tr, ref int dynamicIndex, ref int integerIndex, ICustomAttributeProvider? context) {
    var sb = new StringBuilder();
    // Note: these checks used to use things like `IsArray` and then `GetElementType()` to get at the contents. However,
    // `GetElementType()` gets the _innermost_ element type (https://github.com/jbevain/cecil/issues/841). So given we need casts
    // anyway to access the `ElementType` property, and this isn't very performance-critical code, we just use pattern matching to
    // keep things readable.
    switch (tr) {
      case ArrayType at: { // => T[]
        // Any reference type, including an array, gets an entry in [Dynamic]
        ++dynamicIndex;
        sb.Append(this.TypeName(at.ElementType, ref dynamicIndex, ref integerIndex, context)).Append("[]");
        return sb.ToString();
      }
      case OptionalModifierType omt: {
        var type = omt.ElementType;
        var modifier = omt.ModifierType;
        // Actual meanings to be determined - put the modifier in a comment for now
        var mainType = this.TypeName(type, ref dynamicIndex, ref integerIndex, context);
        // FIXME: Does this need a context? Should it affect the indexes?
        var modifierType = this.TypeName(modifier);
        return $"{mainType} /* optionally modified by: {modifierType} */";
      }
      case PointerType pt: // => T*
        sb.Append(this.TypeName(pt.ElementType, ref dynamicIndex, ref integerIndex, context)).Append('*');
        return sb.ToString();
      case RequiredModifierType rmt: {
        var type = rmt.ElementType;
        var modifier = rmt.ModifierType;
        if (modifier.IsCoreLibraryType("System.Runtime.InteropServices", "InAttribute") && type is ByReferenceType brt) {
          // This signals a `ref readonly xxx` return type
          sb.Append("ref readonly ");
          tr = brt.ElementType;
          break;
        }
        // Actual meanings to be determined - put the modifier in a comment for now
        var mainType = this.TypeName(type, ref dynamicIndex, ref integerIndex, context);
        // FIXME: Does this need a context? Should it affect the indexes?
        var modifierType = this.TypeName(modifier);
        return $"{mainType} /* modified by: {modifierType} */";
      }
    }
    // Check for System.Nullable<T> and make it T?
    if (tr.TryUnwrapNullable(out var unwrapped)) {
      sb.Append(this.TypeName(unwrapped, ref dynamicIndex, ref integerIndex, context)).Append('?');
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
      else if (tr == ts.IntPtr && context.IsNativeInteger(integerIndex++)) {
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
        sb.Append(context.IsDynamic(dynamicIndex) ? "dynamic" : "object");
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
      else if (tr == ts.UIntPtr && context.IsNativeInteger(integerIndex++)) {
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
        ++dynamicIndex;
        return sb.ToString();
      }
    }
    // Any type gets an entry in [Dynamic]
    ++dynamicIndex;
    // Check for C# tuples (i.e. System.ValueTuple)
    if (tr.IsGenericInstance && tr.IsCoreLibraryType() && tr.Namespace == "System" && tr.Name.StartsWith("ValueTuple`")) {
      sb.Append('(');
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
          sb.Append(", ");
        }
        sb.Append(this.TypeName(ga, ref dynamicIndex, ref integerIndex, context));
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
      var returnTypeAttributes = this.CustomAttributesInline(fpt.MethodReturnType);
      // This needs to be done right away to use the correct indexes, even though it appears near the end in the syntax
      var returnTypeName = this.TypeName(returnType, ref dynamicIndex, ref integerIndex, context);
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
        // https://github.com/jbevain/cecil/issues/842 - no enum entry for this yet
        case (MethodCallingConvention) 9:
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
          sb.Append(this.CustomAttributesInline(parameter))
            .Append(this.TypeName(parameter.ParameterType, ref dynamicIndex, ref integerIndex, context))
            .Append(", ");
        }
      }
      sb.Append(returnTypeAttributes).Append(returnTypeName).Append('>');
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
    sb.Append(tr.NonGenericName()).Append(this.GenericParameters(tr));
    return sb.ToString();
  }

  protected override string TypeName(TypeReference tr, ICustomAttributeProvider? context = null) {
    var prefix = "";
    // ref X can only occur on outer types; can't have an array of `ref int` or a `Func<ref int>`, so handle that here
    if (tr is ByReferenceType brt) { // => ref T
      // omit the "ref" for "out" parameters - it's covered by the "out"
      if (context is not ParameterDefinition { IsOut: true }) {
        prefix = "ref ";
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
    return prefix + this.TypeName(tr, ref dynamicIndex, ref integerIndex, context);
  }

  protected override string TypeOf(TypeReference tr) {
    // FIXME: Does this need a context?
    return $"typeof({this.TypeName(tr)})";
  }

}
