using System.Diagnostics;
using System.Text;

using ICustomAttributeProvider = Mono.Cecil.ICustomAttributeProvider;

namespace Zastai.Build.ApiReference;

internal static class CodeGeneration {

  private static char[]? _indentationSpaces;

  private static void WriteAttributes(this TextWriter writer, FieldDefinition fd) {
    if (fd.IsPublic) {
      writer.Write("public ");
    }
    else if (fd.IsFamily) {
      writer.Write("protected ");
    }
    else if (fd.IsFamilyOrAssembly) {
      writer.Write("protected internal ");
    }
    if (fd.IsLiteral) {
      writer.Write("const ");
    }
    else {
      if (fd.IsStatic) {
        writer.Write("static ");
      }
      if (fd.IsInitOnly) {
        writer.Write("readonly ");
      }
    }
  }

  private static void WriteAttributes(this TextWriter writer, MethodDefinition md) {
    if (md.IsPublic) {
      writer.Write("public ");
    }
    if (md.IsFamily) {
      writer.Write("protected ");
    }
    if (md.IsFamilyOrAssembly) {
      writer.Write("protected internal ");
    }
    if (md.IsAbstract) {
      writer.Write("abstract ");
    }
    else if (md.IsStatic) {
      writer.Write("static ");
    }
    else {
      if (md.IsFinal) {
        writer.Write("sealed ");
      }
      if (md.IsVirtual && !md.IsNewSlot) {
        writer.Write("override ");
      }
      else if (md.IsVirtual && !md.IsFinal) {
        writer.Write("virtual ");
      }
    }
  }

  private static void WriteAttributes(this TextWriter writer, ParameterDefinition pd) {
    if (pd.IsIn) {
      writer.Write("in ");
    }
    if (pd.IsOut) {
      writer.Write("out ");
    }
    if (pd.IsParamArray()) {
      writer.Write("params ");
    }
  }

  private static void WriteAttributes(this TextWriter writer, TypeDefinition td) {
    if (td.IsPublic || td.IsNestedPublic) {
      writer.Write("public ");
    }
    else if (td.IsNestedFamily) {
      writer.Write("protected ");
    }
    else if (td.IsNestedFamilyOrAssembly) {
      writer.Write("protected internal ");
    }
    if (td.IsClass && td.IsAbstract && td.IsSealed) {
      writer.Write("static ");
    }
    else if (td.IsAbstract && !td.IsInterface) {
      writer.Write("abstract ");
    }
    else if (td.IsSealed && !td.IsValueType) {
      writer.Write("sealed ");
    }
  }

  private static void WriteCustomAttributeArgument(this TextWriter writer, CustomAttributeArgument argument) {
    if (argument.Value is CustomAttributeArgument[] array) {
      writer.Write("new ");
      writer.WriteTypeName(argument.Type);
      writer.Write(" { ");
      var firstArgument = true;
      foreach (var element in array) {
        if (firstArgument) {
          firstArgument = false;
        }
        else {
          writer.Write(", ");
        }
        writer.WriteCustomAttributeArgument(element);
      }
      writer.Write(" }");
    }
    else {
      writer.WriteValue(argument.Type, argument.Value);
    }
  }

  private static void WriteCustomAttribute(this TextWriter writer, ICustomAttribute ca) {
    writer.WriteTypeName(ca.AttributeType);
    var firstArgument = true;
    if (ca.HasConstructorArguments) {
      foreach (var value in ca.ConstructorArguments) {
        if (firstArgument) {
          writer.Write('(');
          firstArgument = false;
        }
        else {
          writer.Write(", ");
        }
        writer.WriteCustomAttributeArgument(value);
      }
    }
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
      if (firstArgument) {
        writer.Write('(');
        firstArgument = false;
      }
      else {
        writer.Write(", ");
      }
      writer.Write("{0} = ", namedArgument.Key);
      writer.WriteCustomAttributeArgument(namedArgument.Value);
    }
    if (ca.HasConstructorArguments || ca.HasFields || ca.HasProperties) {
      writer.Write(')');
    }
  }

  private static void WriteCustomAttributes(this TextWriter writer, AssemblyDefinition ad, int indent = 0) {
    if (!ad.HasCustomAttributes) {
      return;
    }
    writer.WriteCustomAttributes(ad.CustomAttributes, "assembly", indent);
  }

  private static void WriteCustomAttributes(this TextWriter writer, ICustomAttributeProvider cap, int indent = 0) {
    if (!cap.HasCustomAttributes) {
      return;
    }
    writer.WriteCustomAttributes(cap.CustomAttributes, null, indent);
  }

  private static void WriteCustomAttributes(this TextWriter writer, IEnumerable<CustomAttribute> attributes, string? prefix = null,
                                            int indent = 0) {
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
          writer.WriteIndent(indent);
        }
        writer.Write('[');
        if (prefix is not null) {
          writer.Write(prefix);
          writer.Write(": ");
        }
        writer.WriteCustomAttribute(attribute);
        writer.Write(']');
        if (indent < 0) {
          writer.Write(' ');
        }
        else {
          writer.WriteLine();
        }
      }
    }
  }

  private static void WriteCustomAttributes(this TextWriter writer, ModuleDefinition md, int indent = 0) {
    if (!md.HasCustomAttributes) {
      return;
    }
    writer.WriteLine($"// Module: {md.Name}");
    writer.WriteCustomAttributes(md.CustomAttributes, "module", indent);
  }

  private static void WriteEnumField(this TextWriter writer, FieldDefinition fd, int indent = 0) {
    writer.WriteCustomAttributes(fd, indent);
    writer.WriteIndent(indent);
    Trace.Assert(fd.IsPublic, $"Enum field {fd} has unsupported access: {fd.Attributes}.");
    Trace.Assert(fd.IsLiteral, $"Enum field {fd} is not a literal.");
    Trace.Assert(fd.HasConstant, $"Enum field {fd} has no constant value.");
    writer.Write(fd.Name);
    writer.Write(" = ");
    writer.WriteValue(null, fd.Constant);
    writer.WriteLine(',');
  }

  private static void WriteEvent(this TextWriter writer, EventDefinition ed, int indent = 0) {
  }

  private static void WriteEvents(this TextWriter writer, TypeDefinition td, int indent = 0) {
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
      writer.WriteLine();
      writer.WriteEvent(@event.Value, indent);
    }
  }

  private static void WriteField(this TextWriter writer, FieldDefinition fd, int indent = 0) {
    writer.WriteCustomAttributes(fd, indent);
    Trace.Assert(fd.IsPublicApi(), $"Enum field {fd} has unsupported access: {fd.Attributes}.");
    writer.WriteIndent(indent);
    writer.WriteAttributes(fd);
    writer.WriteTypeName(fd.FieldType);
    writer.Write(' ');
    writer.Write(fd.Name);
    if (fd.IsLiteral && fd.HasConstant) {
      writer.Write(" = ");
      writer.WriteValue(fd.FieldType, fd.Constant);
    }
    writer.WriteLine(';');
  }

  private static void WriteFields(this TextWriter writer, TypeDefinition td, int indent = 0) {
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
      writer.WriteLine();
      foreach (var field in fields) {
        if (field.Value.IsSpecialName) { // skip value__
          continue;
        }
        writer.WriteEnumField(field.Value, indent);
      }
    }
    else {
      foreach (var field in fields) {
        writer.WriteLine();
        writer.WriteField(field.Value, indent);
      }
    }
  }

  private static void WriteGenericParameter(this TextWriter writer, GenericParameter gp) {
    if (gp.IsCovariant) {
      writer.Write("out ");
    }
    else if (gp.IsContravariant) {
      writer.Write("in ");
    }
    writer.Write(gp.Name);
  }

  private static void WriteGenericParameterConstraint(this TextWriter writer, GenericParameterConstraint constraint) {
    // FIXME: Do we need to care about custom attributes on these?
    writer.WriteTypeName(constraint.ConstraintType);
  }

  private static void WriteGenericParameterConstraints(this TextWriter writer, GenericParameter parameter) {
    if (!parameter.HasConstraints) {
      return;
    }
    writer.Write(" where ");
    writer.Write(parameter.Name);
    writer.Write(" : ");
    var first = true;
    foreach (var constraint in parameter.Constraints) {
      if (first) {
        first = false;
      }
      else {
        writer.Write(", ");
      }
      writer.WriteGenericParameterConstraint(constraint);
    }
  }

  private static void WriteGenericParameterConstraints(this TextWriter writer, IGenericParameterProvider provider) {
    if (!provider.HasGenericParameters) {
      return;
    }
    foreach (var parameter in provider.GenericParameters) {
      writer.WriteGenericParameterConstraints(parameter);
    }
  }

  private static void WriteGenericParameters(this TextWriter writer, IGenericParameterProvider provider) {
    if (!provider.HasGenericParameters) {
      return;
    }
    writer.Write('<');
    var first = true;
    foreach (var parameter in provider.GenericParameters) {
      if (first) {
        first = false;
      }
      else {
        writer.Write(", ");
      }
      writer.WriteGenericParameter(parameter);
    }
    writer.Write('>');
  }

  private static void WriteGenericParameters(this TextWriter writer, TypeReference tr) {
    if (tr.IsGenericInstance) {
      var gi = (IGenericInstance) tr;
      if (!gi.HasGenericArguments) {
        // FIXME: Can this happen? Is it an error? Should it produce <>?
        return;
      }
      writer.Write('<');
      var first = true;
      foreach (var argument in gi.GenericArguments) {
        if (first) {
          first = false;
        }
        else {
          writer.Write(", ");
        }
        if (argument.IsGenericParameter) {
          writer.WriteGenericParameter((GenericParameter) argument);
        }
        else {
          writer.WriteTypeName(argument);
        }
      }
      writer.Write('>');
    }
    else {
      writer.WriteGenericParameters((IGenericParameterProvider) tr);
    }
  }

  private static void WriteIndent(this TextWriter writer, int indent) {
    if (CodeGeneration._indentationSpaces == null || indent > CodeGeneration._indentationSpaces.Length) {
      CodeGeneration._indentationSpaces = new string(' ', Math.Max(64, 2 * indent)).ToCharArray();
    }
    writer.Write(CodeGeneration._indentationSpaces, 0, indent);
  }

  private static void WriteLiteral(this TextWriter writer, bool b) => writer.Write(b ? "true" : "false");

  private static void WriteLiteral(this TextWriter writer, string s) {
    var sb = new StringBuilder();
    sb.Append('"');
    foreach (var c in s) {
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
    writer.Write(sb.ToString());
  }

  private static void WriteMethod(this TextWriter writer, MethodDefinition md, int indent = 0) {
    writer.WriteCustomAttributes(md, indent);
    Trace.Assert(md.IsPublicApi(), $"Method {md} has unsupported access: {md.Attributes}.");
    writer.WriteCustomAttributes(md.MethodReturnType.CustomAttributes, "return", indent);
    writer.WriteIndent(indent);
    writer.WriteAttributes(md);
    if (md.IsRuntimeSpecialName) {
      // Runtime-Special Names
      if (md.Name is ".ctor" or ".cctor") {
        writer.WriteTypeName(md.DeclaringType, md.DeclaringType.Namespace, false, true);
      }
      else {
        writer.Write("/* TODO: Map RunTime-Special Method Name Correctly */ ");
        writer.WriteTypeName(md.ReturnType);
        writer.Write(' ');
        writer.Write(md.Name);
      }
    }
    else if (md.IsSpecialName) {
      // Other Special Names - these will probably look/work fine if not treated specially
      if (md.Name.StartsWith("op_")) {
        var op = md.Name.Substring(3);
        if (op is "Explicit" or "Implicit") {
          writer.Write(op.ToLowerInvariant());
          writer.Write(" operator ");
          writer.WriteTypeName(md.ReturnType);
        }
        else {
          writer.WriteTypeName(md.ReturnType);
          writer.Write(" operator ");
          switch (op) {
            // Relational
            case "Equality":
              writer.Write("==");
              break;
            case "GreaterThan":
              writer.Write('>');
              break;
            case "GreaterThanOrEqual":
              writer.Write(">=");
              break;
            case "Inequality":
              writer.Write("!=");
              break;
            case "LessThan":
              writer.Write('<');
              break;
            case "LessThanOrEqual":
              writer.Write("<=");
              break;
            // Logical
            case "False":
              writer.Write("false");
              break;
            case "LogicalNot":
              writer.Write('!');
              break;
            case "True":
              writer.Write("true");
              break;
            // Arithmetic
            case "Addition":
            case "UnaryPlus":
              writer.Write('+');
              break;
            case "BitwiseAnd":
              writer.Write("&");
              break;
            case "BitwiseOr":
              writer.Write("|");
              break;
            case "Decrement":
              writer.Write("--");
              break;
            case "Division":
              writer.Write('/');
              break;
            case "ExclusiveOr":
              writer.Write('^');
              break;
            case "Exponent":
              // No C# operator for this
              writer.Write("**");
              break;
            case "Increment":
              writer.Write("++");
              break;
            case "LeftShift":
              writer.Write("<<");
              break;
            case "Modulus":
              writer.Write('%');
              break;
            case "Multiply":
              writer.Write('*');
              break;
            case "OnesComplement":
              writer.Write('~');
              break;
            case "RightShift":
              writer.Write(">>");
              break;
            case "Subtraction":
            case "UnaryNegation":
              writer.Write('-');
              break;
            default:
              writer.Write("/* TODO: Map Operator Correctly */ ");
              writer.Write(op);
              break;
          }
        }
      }
      else {
        writer.Write("/* TODO: Map Special Method Name Correctly */ ");
        writer.WriteTypeName(md.ReturnType);
        writer.Write(' ');
        writer.Write(md.Name);
      }
    }
    else {
      writer.WriteTypeName(md.ReturnType);
      writer.Write(' ');
      writer.Write(md.Name);
    }
    writer.WriteGenericParameters(md);
    writer.WriteParameters(md);
    writer.WriteGenericParameterConstraints(md);
    writer.WriteLine(";");
  }

  private static void WriteMethods(this TextWriter writer, TypeDefinition td, int indent = 0) {
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
        // These are not included in the ToString, for some reason (https://github.com/jbevain/cecil/issues/834)
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
        writer.WriteLine();
        writer.WriteMethod(overload.Value, indent);
      }
    }
  }

  private static void WriteNestedTypes(this TextWriter writer, TypeDefinition td, int indent = 0) {
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
      writer.WriteType(type.Value, null, indent);
    }
  }

  private static void WriteParameter(this TextWriter writer, ParameterDefinition pd) {
    writer.WriteCustomAttributes(pd, -1);
    writer.WriteAttributes(pd);
    writer.WriteTypeName(pd.ParameterType, omitRefKeyword: pd.IsOut);
    writer.Write(' ');
    writer.Write(pd.Name);
    if (!pd.HasDefault) {
      return;
    }
    writer.Write(" = ");
    Trace.Assert(pd.HasConstant, pd.ToString(), "Parameter is marked as having a default value, but it has no constant.");
    if (pd.Constant == null && pd.ParameterType.IsValueType) {
      writer.Write("default");
    }
    else {
      writer.WriteValue(pd.ParameterType, pd.Constant);
    }
  }

  private static void WriteParameters(this TextWriter writer, IEnumerable<ParameterDefinition> parameters) {
    var first = true;
    foreach (var parameter in parameters) {
      if (first) {
        first = false;
      }
      else {
        writer.Write(", ");
      }
      writer.WriteParameter(parameter);
    }
  }

  private static void WriteParameters(this TextWriter writer, MethodDefinition md) {
    writer.Write('(');
    if (md.HasParameters) {
      // Detect extension methods
      if (md.HasCustomAttributes) {
        foreach (var ca in md.CustomAttributes) {
          if (ca.AttributeType.IsCoreLibraryType("System.Runtime.CompilerServices", "ExtensionAttribute")) {
            writer.Write("this ");
            break;
          }
        }
      }
      writer.WriteParameters(md.Parameters);
    }
    writer.Write(')');
  }

  private static void WriteParameters(this TextWriter writer, PropertyDefinition pd) {
    if (!pd.HasParameters) {
      return;
    }
    // Assumption: only indexers have parameters, and they use []
    writer.Write('[');
    writer.WriteParameters(pd.Parameters);
    writer.Write(']');
  }

  private static void WriteProperties(this TextWriter writer, TypeDefinition td, int indent = 0) {
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
        writer.WriteLine();
        writer.WriteProperty(overload.Value, indent);
      }
    }
    foreach (var property in properties) {
      writer.WriteLine();
      writer.WriteProperty(property.Value, indent);
    }
  }

  private static void WriteProperty(this TextWriter writer, PropertyDefinition pd, int indent = 0) {
    writer.WriteCustomAttributes(pd, indent);
    Trace.Assert(!pd.HasOtherMethods, $"Property has 'other methods' which is not yet supported: {pd}.");
    writer.WriteIndent(indent);
    var getter = pd.GetMethod;
    var setter = pd.SetMethod;
    MethodDefinition? singleAccess = null;
    if (getter is not null && getter.IsPublicApi()) {
      if (setter is not null && setter.IsPublicApi()) {
        singleAccess = getter.Attributes == setter.Attributes ? getter : null;
      }
      else {
        singleAccess = getter;
      }
    }
    else if (setter is not null && setter.IsPublicApi()) {
      singleAccess = setter;
    }
    else {
      // This should have been filtered out
      Trace.Fail($"Property {pd} has neither a public getter nor a setter.");
    }
    if (singleAccess is not null) {
      Trace.Assert(singleAccess.IsPublicApi(), $"Property {pd} has unsupported access: {singleAccess.Attributes}.");
      writer.WriteAttributes(singleAccess);
    }
    writer.WriteTypeName(pd.PropertyType);
    writer.Write(' ');
    // FIXME: Or should this only be done when the type has [System.Reflection.DefaultMemberAttribute("Item")]?
    if (pd.HasParameters && pd.Name == "Item") {
      writer.Write("this");
    }
    else {
      writer.Write(pd.Name);
    }
    writer.WriteParameters(pd);
    writer.Write(" { ");
    if (getter is not null && getter.IsPublicApi()) {
      if (singleAccess is null) {
        writer.WriteAttributes(getter);
      }
      writer.Write("get; ");
    }
    if (setter is not null && setter.IsPublicApi()) {
      if (singleAccess is null) {
        writer.WriteAttributes(setter);
      }
      writer.Write("set; ");
    }
    writer.WriteLine('}');
  }

  public static void WritePublicApi(this TextWriter writer, AssemblyDefinition ad) {
    writer.WriteLine("// === Generated API Reference === DO NOT EDIT BY HAND === //");
    writer.WriteCustomAttributes(ad);
    foreach (var md in ad.Modules) {
      writer.WriteCustomAttributes(md);
    }
    writer.WriteTopLevelTypes(ad);
  }

  private static void WriteTopLevelTypes(this TextWriter writer, AssemblyDefinition ad) {
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
      writer.WriteLine();
      writer.Write("namespace ");
      writer.Write(ns.Key);
      writer.WriteLine(" {");
      foreach (var type in ns.Value) {
        writer.WriteType(type.Value, ns.Key, 2);
      }
      writer.WriteLine();
      writer.WriteLine('}');
    }
  }

  private static void WriteType(this TextWriter writer, TypeDefinition td, string? ns = null, int indent = 0) {
    writer.WriteLine();
    writer.WriteCustomAttributes(td, indent);
    Trace.Assert(td.IsPublicApi(), $"Type {td} has unsupported access: {td.Attributes}.");
    writer.WriteIndent(indent);
    writer.WriteAttributes(td);
    if (td.IsEnum) {
      writer.Write("enum");
    }
    else if (td.IsInterface) {
      writer.Write("interface");
    }
    else if (td.IsValueType) {
      writer.Write("struct");
    }
    else if (td.IsClass) {
      // TODO: Maybe detect delegates; but then what of explicitly written classes deriving from MultiCastDelegate?
      writer.Write("class");
    }
    else { // What else can it be?
      Trace.Fail($"Type {td} has unsupported classification: {td.Attributes}.");
    }
    writer.Write(' ');
    writer.WriteTypeName(td, ns, false);
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
        writer.Write(" : ");
        writer.WriteTypeName(baseType);
      }
      if (td.HasInterfaces) {
        if (!isDerived) {
          writer.Write(" : ");
        }
        var first = true;
        foreach (var implementation in td.Interfaces) {
          if (first) {
            first = false;
          }
          else {
            writer.Write(", ");
          }
          // FIXME: Do we need to care about custom attributes on these?
          writer.WriteTypeName(implementation.InterfaceType);
        }
      }
    }
    writer.WriteGenericParameterConstraints(td);
    writer.WriteLine(" {");
    writer.WriteFields(td, indent + 2);
    writer.WriteProperties(td, indent + 2);
    writer.WriteEvents(td, indent + 2);
    writer.WriteMethods(td, indent + 2);
    writer.WriteNestedTypes(td, indent + 2);
    writer.WriteLine();
    writer.WriteIndent(indent);
    writer.WriteLine('}');
  }

  private static void WriteTypeName(this TextWriter writer, TypeReference tr, string? ns = null, bool includeDeclaringType = true,
                                    bool omitRefKeyword = false) {
    // Check for pass-by-reference and make it ref T
    if (tr.IsByReference) {
      if (!omitRefKeyword) {
        writer.Write("ref ");
      }
      tr = tr.GetElementType();
    }
    // Check for arrays and make them T[]
    if (tr.IsArray) {
      writer.WriteTypeName(tr.GetElementType(), ns, includeDeclaringType);
      writer.Write("[]");
      return;
    }
    // Check for System.Nullable<T> and make it T?
    if (tr.TryUnwrapNullable(out var unwrapped)) {
      writer.WriteTypeName(unwrapped, ns, includeDeclaringType);
      writer.Write('?');
      return;
    }
    // Check for specific framework types
    var ts = tr.Module.TypeSystem;
    if (tr == ts.Boolean) {
      writer.Write("bool");
      return;
    }
    if (tr == ts.Byte) {
      writer.Write("byte");
      return;
    }
    if (tr == ts.Char) {
      writer.Write("char");
      return;
    }
    if (tr == ts.Double) {
      writer.Write("double");
      return;
    }
    if (tr == ts.Int16) {
      writer.Write("short");
      return;
    }
    if (tr == ts.Int32) {
      writer.Write("int");
      return;
    }
    if (tr == ts.Int64) {
      writer.Write("long");
      return;
    }
    if (tr == ts.SByte) {
      writer.Write("sbyte");
      return;
    }
    if (tr == ts.Single) {
      writer.Write("float");
      return;
    }
    if (tr == ts.String) {
      writer.Write("string");
      return;
    }
    if (tr == ts.Object) {
      writer.Write("object");
      return;
    }
    if (tr == ts.UInt16) {
      writer.Write("ushort");
      return;
    }
    if (tr == ts.UInt32) {
      writer.Write("uint");
      return;
    }
    if (tr == ts.UInt64) {
      writer.Write("ulong");
      return;
    }
    if (tr == ts.Void) {
      writer.Write("void");
      return;
    }
    // This one has no special property on TypeSystem
    if (tr.IsCoreLibraryType("System", "Decimal")) {
      writer.Write("decimal");
      return;
    }
    // Otherwise, full stringification.
    if (tr.IsNested && includeDeclaringType) {
      writer.WriteTypeName(tr.DeclaringType, ns, includeDeclaringType);
      writer.Write('.');
    }
    else if (!string.IsNullOrEmpty(tr.Namespace) && (ns == null || ns != tr.Namespace)) {
      writer.Write(tr.Namespace);
      writer.Write('.');
    }
    var name = tr.Name;
    // Strip off the part after a backtick. This used to assert that only generic types have a backtick, but non-generic nested
    // types inside a generic type can be generic while not themselves having a backtick.
    var backTick = name.IndexOf('`');
    if (backTick >= 0) {
      name = name.Substring(0, backTick);
    }
    writer.Write(name);
    writer.WriteGenericParameters(tr);
  }

  private static void WriteValue(this TextWriter writer, TypeReference? type, object? value) {
    if (value is null) {
      writer.Write("null");
      return;
    }
    if (type is { IsValueType: true }) { // Check for enum values
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
            if (first) {
              first = false;
            }
            else {
              writer.Write(" | ");
            }
            writer.WriteTypeName(td);
            writer.Write('.');
            writer.Write(enumValue.Name);
            remainingFlags &= ~valueFlags;
          }
          if (remainingFlags == 0 && !first) {
            return;
          }
          if (!first) {
            writer.Write(" | ");
            value = remainingFlags;
          }
        }
        else {
          foreach (var enumValue in td.Fields) {
            if (enumValue.IsSpecialName || !enumValue.IsLiteral || !enumValue.HasConstant) {
              continue;
            }
            if (enumValue.Constant.Equals(value)) {
              writer.WriteTypeName(td);
              writer.Write('.');
              writer.Write(enumValue.Name);
              return;
            }
          }
        }
        writer.Write('(');
        writer.WriteTypeName(td);
        writer.Write(") ");
      }
    }
    switch (value) {
      case null:
        writer.Write("null");
        break;
      case TypeReference tr:
        writer.Write("typeof(");
        writer.WriteTypeName(tr);
        writer.Write(')');
        break;
      case bool b:
        writer.WriteLiteral(b);
        break;
      case string s:
        writer.WriteLiteral(s);
        break;
      default:
        // Assume everything else matches its ToString()
        writer.Write(value);
        break;
    }
  }

}
