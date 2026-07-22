using Cx.Compiler.CompileTime;
using Cx.Compiler.Diagnostics;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class CompileTimeIntrinsicTests
{
    [Fact]
    public void Concat_CombinesStringsBindingsAndNestedCalls()
    {
        var context = new CompileTimeEvaluationContext();
        context.Define("field_name", new CompileTimeValue.String("value"));

        var (value, diagnostics) = Evaluate(
            "concat(\"get_\", concat(field_name, \"_ptr\"))",
            context);

        Assert.Equal("get_value_ptr", Assert.IsType<CompileTimeValue.String>(value).Value);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void AsName_ConvertsGeneratedTextToTypedName()
    {
        var (value, diagnostics) = Evaluate("as_name(concat(\"get_\", \"value\"))");

        Assert.Equal("get_value", Assert.IsType<CompileTimeValue.Name>(value).Value);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Concat_RejectsNonStringArgument()
    {
        var (value, diagnostics) = Evaluate("concat(\"value_\", 1)");

        Assert.Null(value);
        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains(
                "'concat' expects string arguments, but argument 2 is integer",
                StringComparison.Ordinal));
    }

    [Fact]
    public void CompileError_ReportsEvaluatedMessage()
    {
        var (value, diagnostics) = Evaluate(
            "compile_error(concat(\"Unsupported type: \", \"Widget\"))");

        Assert.False(Assert.IsType<CompileTimeValue.Boolean>(value).Value);
        var diagnostic = Assert.Single(diagnostics.Diagnostics);
        Assert.Equal("Unsupported type: Widget", diagnostic.Message);
    }

    [Fact]
    public void AsName_RejectsInvalidIdentifier()
    {
        var (value, diagnostics) = Evaluate("as_name(\"not a name\")");

        Assert.Null(value);
        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("invalid identifier 'not a name'", StringComparison.Ordinal));
    }

    [Fact]
    public void Fields_UsesReadOnlyProgramReflection()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct User {
                name: int;
                age: int;
            }
            """);
        var context = new CompileTimeEvaluationContext();
        context.Define(
            "target",
            new CompileTimeValue.Type(new TypeRef.Named("User", [])));

        var (value, diagnostics) = Evaluate(
            "fields(target)",
            context,
            new ProgramCompileTimeReflection(program));

        var fields = Assert.IsType<CompileTimeValue.List>(value).Values;
        Assert.Equal(2, fields.Count);
        Assert.Equal(
            ["name", "age"],
            fields.Select(field =>
                Assert.IsType<CompileTimeValue.ResolvedField>(field).Value.Name));
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Name_ReadsNameFromReflectedSyntaxValue()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct User {
                name: int;
            }
            """);
        var context = new CompileTimeEvaluationContext();
        context.Define(
            "field",
            new CompileTimeValue.Syntax(Assert.Single(Assert.Single(program.Structs).Fields)));

        var (value, diagnostics) = Evaluate("name(field)", context);

        Assert.Equal("name", Assert.IsType<CompileTimeValue.String>(value).Value);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Fields_ReportsUnavailableReflectionContext()
    {
        var context = new CompileTimeEvaluationContext();
        context.Define(
            "target",
            new CompileTimeValue.Type(new TypeRef.Named("User", [])));

        var (value, diagnostics) = Evaluate("fields(target)", context);

        Assert.Null(value);
        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains(
                "Compile-time reflection is not available",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Type_ReturnsStructuredFieldType()
    {
        var program = ReflectionProgram();
        var field = Assert.Single(program.Structs).Fields[0];
        var context = new CompileTimeEvaluationContext();
        context.Define("field", new CompileTimeValue.Syntax(field));

        var (value, diagnostics) = Evaluate(
            "type(field)",
            context,
            new ProgramCompileTimeReflection(program));

        Assert.Equal("int", Assert.IsType<TypeRef.Named>(
            Assert.IsType<CompileTimeValue.Type>(value).Value).Name);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Attributes_ReturnsAttributeSyntaxThatNameCanInspect()
    {
        var program = ReflectionProgram();
        var field = Assert.Single(program.Structs).Fields[0];
        var context = new CompileTimeEvaluationContext();
        context.Define("field", new CompileTimeValue.Syntax(field));
        var reflection = new ProgramCompileTimeReflection(program);

        var (value, diagnostics) = Evaluate("attributes(field)", context, reflection);
        var attribute = Assert.IsType<AttributeApplicationNode>(
            Assert.IsType<CompileTimeValue.Syntax>(
                Assert.Single(Assert.IsType<CompileTimeValue.List>(value).Values)).Value);
        context.Define("attr", new CompileTimeValue.Syntax(attribute));
        var (name, nameDiagnostics) = Evaluate("name(attr)", context, reflection);

        Assert.Equal("json_name", Assert.IsType<CompileTimeValue.String>(name).Value);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
        CompilerTestHelpers.AssertNoErrors(nameDiagnostics);
    }

    [Theory]
    [InlineData("json_name", true)]
    [InlineData("json_skip", false)]
    public void HasAttribute_ReturnsBooleanForReflectedSyntax(string name, bool expected)
    {
        var program = ReflectionProgram();
        var field = Assert.Single(program.Structs).Fields[0];
        var context = new CompileTimeEvaluationContext();
        context.Define("field", new CompileTimeValue.Syntax(field));

        var (value, diagnostics) = Evaluate(
            $"has_attribute(field, \"{name}\")",
            context,
            new ProgramCompileTimeReflection(program));

        Assert.Equal(expected, Assert.IsType<CompileTimeValue.Boolean>(value).Value);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Attributes_ReturnsEmptyListForSyntaxWithoutAttributes()
    {
        var program = ReflectionProgram();
        var field = Assert.Single(program.Structs).Fields[1];
        var context = new CompileTimeEvaluationContext();
        context.Define("field", new CompileTimeValue.Syntax(field));

        var (value, diagnostics) = Evaluate(
            "attributes(field)",
            context,
            new ProgramCompileTimeReflection(program));

        Assert.Empty(Assert.IsType<CompileTimeValue.List>(value).Values);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Arguments_ReturnsStructuredAttributeArguments()
    {
        var program = ReflectionProgram();
        var attribute = Assert.Single(Assert.Single(program.Structs).Fields[0].Attributes);
        var context = new CompileTimeEvaluationContext();
        context.Define("attr", new CompileTimeValue.Syntax(attribute));

        var (value, diagnostics) = Evaluate("arguments(attr)", context);

        var argument = Assert.IsType<AttributeArgumentNode>(
            Assert.IsType<CompileTimeValue.Syntax>(
                Assert.Single(Assert.IsType<CompileTimeValue.List>(value).Values)).Value);
        Assert.IsType<LiteralExpressionNode>(argument.Value);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Value_EvaluatesAttributeArgumentWithCurrentLexicalBindings()
    {
        var program = CompilerTestHelpers.Parse(
            """
            @metadata(concat(prefix, "_value"))
            fn sample() -> int {
                return 0;
            }
            """);
        var argument = Assert.Single(
            Assert.Single(Assert.Single(program.Functions).Attributes).Arguments);
        var context = new CompileTimeEvaluationContext();
        context.Define("arg", new CompileTimeValue.Syntax(argument));
        context.Define("prefix", new CompileTimeValue.String("field"));

        var (value, diagnostics) = Evaluate("value(arg)", context);

        Assert.Equal("field_value", Assert.IsType<CompileTimeValue.String>(value).Value);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Value_EvaluatesVariableLengthAttributeList()
    {
        var program = CompilerTestHelpers.Parse(
            """
            @aliases(["first", "second", "third"])
            fn sample() -> int {
                return 0;
            }
            """);
        var argument = Assert.Single(
            Assert.Single(Assert.Single(program.Functions).Attributes).Arguments);
        var context = new CompileTimeEvaluationContext();
        context.Define("arg", new CompileTimeValue.Syntax(argument));

        var (value, diagnostics) = Evaluate("value(arg)", context);

        Assert.Equal(
            ["first", "second", "third"],
            Assert.IsType<CompileTimeValue.List>(value).Values.Select(item =>
                Assert.IsType<CompileTimeValue.String>(item).Value));
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void TypeKind_ReturnsStableStructuralKinds()
    {
        var context = new CompileTimeEvaluationContext();
        context.Define("named", new CompileTimeValue.Type(new TypeRef.Named("Vec", [TypeRef.Int])));
        context.Define("pointer", new CompileTimeValue.Type(new TypeRef.Pointer(TypeRef.Int)));
        context.Define("function", new CompileTimeValue.Type(new TypeRef.Function([TypeRef.Int], TypeRef.Bool)));

        var (named, namedDiagnostics) = Evaluate("type_kind(named)", context);
        var (pointer, pointerDiagnostics) = Evaluate("type_kind(pointer)", context);
        var (function, functionDiagnostics) = Evaluate("type_kind(function)", context);

        Assert.Equal("named", Assert.IsType<CompileTimeValue.String>(named).Value);
        Assert.Equal("pointer", Assert.IsType<CompileTimeValue.String>(pointer).Value);
        Assert.Equal("function", Assert.IsType<CompileTimeValue.String>(function).Value);
        CompilerTestHelpers.AssertNoErrors(namedDiagnostics);
        CompilerTestHelpers.AssertNoErrors(pointerDiagnostics);
        CompilerTestHelpers.AssertNoErrors(functionDiagnostics);
    }

    [Fact]
    public void IsType_UsesResolvedStructuralTypeIdentity()
    {
        var context = new CompileTimeEvaluationContext();
        context.Define(
            "left",
            new CompileTimeValue.Type(new TypeRef.Named("Result", [TypeRef.Int, TypeRef.Bool], "sample")));
        context.Define(
            "same",
            new CompileTimeValue.Type(new TypeRef.Named("Result", [TypeRef.Int, TypeRef.Bool], "sample")));
        context.Define(
            "different",
            new CompileTimeValue.Type(new TypeRef.Named("Result", [TypeRef.Int, TypeRef.Bool], "other")));

        var (same, sameDiagnostics) = Evaluate("is_type(left, same)", context);
        var (different, differentDiagnostics) = Evaluate("is_type(left, different)", context);

        Assert.True(Assert.IsType<CompileTimeValue.Boolean>(same).Value);
        Assert.False(Assert.IsType<CompileTimeValue.Boolean>(different).Value);
        CompilerTestHelpers.AssertNoErrors(sameDiagnostics);
        CompilerTestHelpers.AssertNoErrors(differentDiagnostics);
    }

    [Fact]
    public void ElementType_UnwrapsPointerAndConstTypes()
    {
        var context = new CompileTimeEvaluationContext();
        context.Define("pointer", new CompileTimeValue.Type(new TypeRef.Pointer(TypeRef.Int)));
        context.Define("constant", new CompileTimeValue.Type(new TypeRef.Const(TypeRef.Bool)));

        var (pointer, pointerDiagnostics) = Evaluate("element_type(pointer)", context);
        var (constant, constantDiagnostics) = Evaluate("element_type(constant)", context);

        Assert.True(TypeIdentity.ResolvedEquals(
            TypeRef.Int,
            Assert.IsType<CompileTimeValue.Type>(pointer).Value));
        Assert.True(TypeIdentity.ResolvedEquals(
            TypeRef.Bool,
            Assert.IsType<CompileTimeValue.Type>(constant).Value));
        CompilerTestHelpers.AssertNoErrors(pointerDiagnostics);
        CompilerTestHelpers.AssertNoErrors(constantDiagnostics);
    }

    [Fact]
    public void TypeArguments_ReturnsGenericArgumentsWithoutCollectionSemantics()
    {
        var context = new CompileTimeEvaluationContext();
        context.Define(
            "map",
            new CompileTimeValue.Type(new TypeRef.Named(
                "Map",
                [new TypeRef.Named("String", []), TypeRef.Int])));

        var (value, diagnostics) = Evaluate("type_arguments(map)", context);

        var arguments = Assert.IsType<CompileTimeValue.List>(value).Values
            .Select(argument => Assert.IsType<CompileTimeValue.Type>(argument).Value)
            .ToList();
        Assert.Equal(["String", "int"], arguments.Select(TypeRefFormatter.ToCxString));
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void StructuralTypeIntrinsics_ReportUnsupportedKinds()
    {
        var context = new CompileTimeEvaluationContext();
        context.Define("value", new CompileTimeValue.Type(TypeRef.Int));

        var (value, diagnostics) = Evaluate("element_type(value)", context);

        Assert.Null(value);
        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("does not support type kind 'named'", StringComparison.Ordinal));
    }

    [Fact]
    public void ReflectionProperties_ExposeNameTypeAttributesAndArguments()
    {
        var program = ReflectionProgram();
        var field = Assert.Single(program.Structs).Fields[0];
        var attribute = Assert.Single(field.Attributes);
        var argument = Assert.Single(attribute.Arguments);
        var context = new CompileTimeEvaluationContext();
        context.Define("field", new CompileTimeValue.Syntax(field));
        context.Define("attr", new CompileTimeValue.Syntax(attribute));
        context.Define("argument", new CompileTimeValue.Syntax(argument));
        context.Define("target", new CompileTimeValue.Type(new TypeRef.Named("User", [])));
        var reflection = new ProgramCompileTimeReflection(program);

        var (name, nameDiagnostics) = Evaluate("field.name", context, reflection);
        var (typeName, typeNameDiagnostics) = Evaluate("field.type.name", context, reflection);
        var (displayName, displayNameDiagnostics) = Evaluate("field.type.display_name", context, reflection);
        var (kind, kindDiagnostics) = Evaluate("field.type.kind", context, reflection);
        var (isStruct, isStructDiagnostics) = Evaluate("target.is_struct", context, reflection);
        var (fieldCount, fieldCountDiagnostics) = Evaluate("target.fields.count", context, reflection);
        var (attributeCount, attributeDiagnostics) = Evaluate("field.attributes.count", context, reflection);
        var (argumentCount, argumentDiagnostics) = Evaluate("attr.arguments.count", context, reflection);
        var (argumentValue, valueDiagnostics) = Evaluate("argument.value", context, reflection);

        Assert.Equal("name", Assert.IsType<CompileTimeValue.String>(name).Value);
        Assert.Equal("int", Assert.IsType<CompileTimeValue.String>(typeName).Value);
        Assert.Equal("int", Assert.IsType<CompileTimeValue.String>(displayName).Value);
        Assert.Equal("named", Assert.IsType<CompileTimeValue.String>(kind).Value);
        Assert.True(Assert.IsType<CompileTimeValue.Boolean>(isStruct).Value);
        Assert.Equal(2, Assert.IsType<CompileTimeValue.Integer>(fieldCount).Value);
        Assert.Equal(1, Assert.IsType<CompileTimeValue.Integer>(attributeCount).Value);
        Assert.Equal(1, Assert.IsType<CompileTimeValue.Integer>(argumentCount).Value);
        Assert.Equal("displayName", Assert.IsType<CompileTimeValue.String>(argumentValue).Value);
        CompilerTestHelpers.AssertNoErrors(nameDiagnostics);
        CompilerTestHelpers.AssertNoErrors(typeNameDiagnostics);
        CompilerTestHelpers.AssertNoErrors(displayNameDiagnostics);
        CompilerTestHelpers.AssertNoErrors(kindDiagnostics);
        CompilerTestHelpers.AssertNoErrors(isStructDiagnostics);
        CompilerTestHelpers.AssertNoErrors(fieldCountDiagnostics);
        CompilerTestHelpers.AssertNoErrors(attributeDiagnostics);
        CompilerTestHelpers.AssertNoErrors(argumentDiagnostics);
        CompilerTestHelpers.AssertNoErrors(valueDiagnostics);
    }

    [Fact]
    public void EnumReflection_ExposesMembersIndexesAndEffectiveData()
    {
        var program = CompilerTestHelpers.Parse(
            """
            enum Associativity { None, Left }

            enum TokenKind(
                text: const char* = null,
                precedence: int = 0,
                associativity: Associativity = Associativity.None,
                code: int
            ) {
                Identifier { code: 1 },
                Plus { text: "+", precedence: 90, associativity: Associativity.Left, code: 2 },
            }
            """);
        var reflection = new ProgramCompileTimeReflection(program);
        var context = new CompileTimeEvaluationContext();

        var (membersValue, membersDiagnostics) = Evaluate("TokenKind.members", context, reflection);
        var (ordinaryMemberCount, ordinaryMemberDiagnostics) = Evaluate("Associativity.members.count", context, reflection);
        var (isEnum, isEnumDiagnostics) = Evaluate("TokenKind.is_enum", context, reflection);
        var (isDataEnum, isDataEnumDiagnostics) = Evaluate("TokenKind.is_data_enum", context, reflection);
        var (ordinaryIsDataEnum, ordinaryIsDataEnumDiagnostics) = Evaluate("Associativity.is_data_enum", context, reflection);
        var (dataFieldsValue, dataFieldsDiagnostics) = Evaluate("TokenKind.data_fields", context, reflection);
        var members = Assert.IsType<CompileTimeValue.List>(membersValue).Values
            .Select(Assert.IsType<CompileTimeValue.EnumMember>)
            .ToList();
        var dataFields = Assert.IsType<CompileTimeValue.List>(dataFieldsValue).Values
            .Select(Assert.IsType<CompileTimeValue.EnumDataField>)
            .ToList();
        Assert.Equal(2, members.Count);
        Assert.Equal(2, Assert.IsType<CompileTimeValue.Integer>(ordinaryMemberCount).Value);
        Assert.True(Assert.IsType<CompileTimeValue.Boolean>(isEnum).Value);
        Assert.True(Assert.IsType<CompileTimeValue.Boolean>(isDataEnum).Value);
        Assert.False(Assert.IsType<CompileTimeValue.Boolean>(ordinaryIsDataEnum).Value);
        Assert.Equal(4, dataFields.Count);
        context.Define("member", members[1]);
        context.Define("field", dataFields[1]);
        context.Define("required_field", dataFields[3]);

        var (name, nameDiagnostics) = Evaluate("member.name", context, reflection);
        var (index, indexDiagnostics) = Evaluate("member.index", context, reflection);
        var (precedence, precedenceDiagnostics) = Evaluate("member.precedence", context, reflection);
        var (text, textDiagnostics) = Evaluate("member.data.text", context, reflection);
        var (associativity, associativityDiagnostics) = Evaluate("member.associativity.name", context, reflection);
        var (fieldName, fieldNameDiagnostics) = Evaluate("field.name", context, reflection);
        var (fieldType, fieldTypeDiagnostics) = Evaluate("field.type.name", context, reflection);
        var (fieldIndex, fieldIndexDiagnostics) = Evaluate("field.index", context, reflection);
        var (hasDefault, hasDefaultDiagnostics) = Evaluate("field.has_default", context, reflection);
        var (defaultValue, defaultValueDiagnostics) = Evaluate("field.default_value", context, reflection);
        var (requiredHasDefault, requiredHasDefaultDiagnostics) = Evaluate("required_field.has_default", context, reflection);
        var (requiredDefault, requiredDefaultDiagnostics) = Evaluate("required_field.default_value", context, reflection);

        Assert.Equal("Plus", Assert.IsType<CompileTimeValue.String>(name).Value);
        Assert.Equal(1, Assert.IsType<CompileTimeValue.Integer>(index).Value);
        Assert.Equal(90, Assert.IsType<CompileTimeValue.Integer>(precedence).Value);
        Assert.Equal("+", Assert.IsType<CompileTimeValue.String>(text).Value);
        Assert.Equal("Left", Assert.IsType<CompileTimeValue.String>(associativity).Value);
        Assert.Equal("precedence", Assert.IsType<CompileTimeValue.String>(fieldName).Value);
        Assert.Equal("int", Assert.IsType<CompileTimeValue.String>(fieldType).Value);
        Assert.Equal(1, Assert.IsType<CompileTimeValue.Integer>(fieldIndex).Value);
        Assert.True(Assert.IsType<CompileTimeValue.Boolean>(hasDefault).Value);
        Assert.Equal(0, Assert.IsType<CompileTimeValue.Integer>(defaultValue).Value);
        Assert.False(Assert.IsType<CompileTimeValue.Boolean>(requiredHasDefault).Value);
        Assert.IsType<CompileTimeValue.Null>(requiredDefault);
        CompilerTestHelpers.AssertNoErrors(membersDiagnostics);
        CompilerTestHelpers.AssertNoErrors(ordinaryMemberDiagnostics);
        CompilerTestHelpers.AssertNoErrors(isEnumDiagnostics);
        CompilerTestHelpers.AssertNoErrors(isDataEnumDiagnostics);
        CompilerTestHelpers.AssertNoErrors(ordinaryIsDataEnumDiagnostics);
        CompilerTestHelpers.AssertNoErrors(dataFieldsDiagnostics);
        CompilerTestHelpers.AssertNoErrors(nameDiagnostics);
        CompilerTestHelpers.AssertNoErrors(indexDiagnostics);
        CompilerTestHelpers.AssertNoErrors(precedenceDiagnostics);
        CompilerTestHelpers.AssertNoErrors(textDiagnostics);
        CompilerTestHelpers.AssertNoErrors(associativityDiagnostics);
        CompilerTestHelpers.AssertNoErrors(fieldNameDiagnostics);
        CompilerTestHelpers.AssertNoErrors(fieldTypeDiagnostics);
        CompilerTestHelpers.AssertNoErrors(fieldIndexDiagnostics);
        CompilerTestHelpers.AssertNoErrors(hasDefaultDiagnostics);
        CompilerTestHelpers.AssertNoErrors(defaultValueDiagnostics);
        CompilerTestHelpers.AssertNoErrors(requiredHasDefaultDiagnostics);
        CompilerTestHelpers.AssertNoErrors(requiredDefaultDiagnostics);
    }

    [Fact]
    public void FunctionProperties_ExposeSignatureAndDeclarationFlags()
    {
        var program = CompilerTestHelpers.Parse(
            """
            public extern fn native_call(value: int, ...) -> bool;
            """);
        var function = Assert.Single(program.ExternFunctions);
        var context = new CompileTimeEvaluationContext();
        context.Define("function", new CompileTimeValue.Syntax(function));
        var reflection = new ProgramCompileTimeReflection(program);

        var (name, nameDiagnostics) = Evaluate("function.name", context, reflection);
        var (parameters, parameterDiagnostics) = Evaluate("function.parameters.count", context, reflection);
        var (returnType, returnTypeDiagnostics) = Evaluate("function.return_type.name", context, reflection);
        var (isPublic, publicDiagnostics) = Evaluate("function.is_public", context, reflection);
        var (isStatic, staticDiagnostics) = Evaluate("function.is_static", context, reflection);
        var (isExtern, externDiagnostics) = Evaluate("function.is_extern", context, reflection);

        Assert.Equal("native_call", Assert.IsType<CompileTimeValue.String>(name).Value);
        Assert.Equal(2, Assert.IsType<CompileTimeValue.Integer>(parameters).Value);
        Assert.Equal("bool", Assert.IsType<CompileTimeValue.String>(returnType).Value);
        Assert.True(Assert.IsType<CompileTimeValue.Boolean>(isPublic).Value);
        Assert.False(Assert.IsType<CompileTimeValue.Boolean>(isStatic).Value);
        Assert.True(Assert.IsType<CompileTimeValue.Boolean>(isExtern).Value);
        CompilerTestHelpers.AssertNoErrors(nameDiagnostics);
        CompilerTestHelpers.AssertNoErrors(parameterDiagnostics);
        CompilerTestHelpers.AssertNoErrors(returnTypeDiagnostics);
        CompilerTestHelpers.AssertNoErrors(publicDiagnostics);
        CompilerTestHelpers.AssertNoErrors(staticDiagnostics);
        CompilerTestHelpers.AssertNoErrors(externDiagnostics);
    }

    [Fact]
    public void MethodReflection_ExposesStructMethodsAndOwnerType()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct User {
                public fn score(multiplier: int) -> int {
                    return multiplier;
                }
            }
            """);
        var structNode = Assert.Single(program.Structs);
        var method = Assert.Single(structNode.Methods);
        var context = new CompileTimeEvaluationContext();
        context.Define("target", new CompileTimeValue.Type(new TypeRef.Named("User", [])));
        context.Define("declaration", new CompileTimeValue.Syntax(structNode));
        context.Define("method", new CompileTimeValue.Syntax(method));
        var reflection = new ProgramCompileTimeReflection(program);

        var (typeMethods, typeMethodsDiagnostics) = Evaluate("target.methods.count", context, reflection);
        var (syntaxMethods, syntaxMethodsDiagnostics) = Evaluate("declaration.methods.count", context, reflection);
        var (ownerName, ownerDiagnostics) = Evaluate("method.owner_type.name", context, reflection);
        var (parameterCount, parameterDiagnostics) = Evaluate("method.parameters.count", context, reflection);
        var (returnType, returnTypeDiagnostics) = Evaluate("method.return_type.name", context, reflection);

        Assert.Equal(1, Assert.IsType<CompileTimeValue.Integer>(typeMethods).Value);
        Assert.Equal(1, Assert.IsType<CompileTimeValue.Integer>(syntaxMethods).Value);
        Assert.Equal("User", Assert.IsType<CompileTimeValue.String>(ownerName).Value);
        Assert.Equal(2, Assert.IsType<CompileTimeValue.Integer>(parameterCount).Value);
        Assert.Equal("int", Assert.IsType<CompileTimeValue.String>(returnType).Value);
        CompilerTestHelpers.AssertNoErrors(typeMethodsDiagnostics);
        CompilerTestHelpers.AssertNoErrors(syntaxMethodsDiagnostics);
        CompilerTestHelpers.AssertNoErrors(ownerDiagnostics);
        CompilerTestHelpers.AssertNoErrors(parameterDiagnostics);
        CompilerTestHelpers.AssertNoErrors(returnTypeDiagnostics);
    }

    [Fact]
    public void ResolvedMemberReflection_SpecializesGenericsAndIncludesExtensions()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct Box<T> {
                value: T;

                fn get() -> T {
                    return self.value;
                }
            }

            extension Box<T> {
                fn replace(value: T) -> T {
                    return value;
                }
            }
            """);
        var context = new CompileTimeEvaluationContext();
        context.Define("target", new CompileTimeValue.Type(
            new TypeRef.Named("Box", [TypeRef.Int])));
        var reflection = new ProgramCompileTimeReflection(program);

        var (fieldListValue, fieldListDiagnostics) = Evaluate("target.fields", context, reflection);
        var field = Assert.IsType<CompileTimeValue.ResolvedField>(
            Assert.Single(Assert.IsType<CompileTimeValue.List>(fieldListValue).Values));
        context.Define("field", field);

        var (methodListValue, methodListDiagnostics) = Evaluate("target.methods", context, reflection);
        var methods = Assert.IsType<CompileTimeValue.List>(methodListValue).Values
            .Select(Assert.IsType<CompileTimeValue.ResolvedMethod>)
            .ToList();
        var replace = Assert.Single(methods, method => method.Value.Name == "replace");
        context.Define("method", replace);
        context.Define("parameter", replace.Value.Parameters.Last() is { } parameter
            ? new CompileTimeValue.ResolvedParameter(parameter)
            : throw new InvalidOperationException());

        var (fieldType, fieldTypeDiagnostics) = Evaluate("field.type.name", context, reflection);
        var (rawFieldType, rawFieldTypeDiagnostics) = Evaluate("field.declaration.type.name", context, reflection);
        var (returnType, returnTypeDiagnostics) = Evaluate("method.return_type.name", context, reflection);
        var (parameterType, parameterTypeDiagnostics) = Evaluate("parameter.type.name", context, reflection);
        var (ownerType, ownerTypeDiagnostics) = Evaluate("method.owner_type.display_name", context, reflection);

        Assert.Equal("int", Assert.IsType<CompileTimeValue.String>(fieldType).Value);
        Assert.Equal("T", Assert.IsType<CompileTimeValue.String>(rawFieldType).Value);
        Assert.Equal("int", Assert.IsType<CompileTimeValue.String>(returnType).Value);
        Assert.Equal("int", Assert.IsType<CompileTimeValue.String>(parameterType).Value);
        Assert.Equal("Box<int>", Assert.IsType<CompileTimeValue.String>(ownerType).Value);
        Assert.Equal(
            "Box<int>*",
            TypeRefFormatter.ToCxString(replace.Value.Parameters[0].Type));
        Assert.Equal(2, methods.Count);
        CompilerTestHelpers.AssertNoErrors(fieldListDiagnostics);
        CompilerTestHelpers.AssertNoErrors(methodListDiagnostics);
        CompilerTestHelpers.AssertNoErrors(fieldTypeDiagnostics);
        CompilerTestHelpers.AssertNoErrors(rawFieldTypeDiagnostics);
        CompilerTestHelpers.AssertNoErrors(returnTypeDiagnostics);
        CompilerTestHelpers.AssertNoErrors(parameterTypeDiagnostics);
        CompilerTestHelpers.AssertNoErrors(ownerTypeDiagnostics);
    }

    [Fact]
    public void RequirementMatch_ExposesSuccessAndInferredTypeBindingsAsProperties()
    {
        var program = RequirementReflectionProgram();
        var context = new CompileTimeEvaluationContext();
        context.Define("target", new CompileTimeValue.Type(new TypeRef.Named("Buffer", [])));
        context.Define("expected", new CompileTimeValue.Type(TypeRef.Int));
        var reflection = new ProgramCompileTimeReflection(program);

        var (success, successDiagnostics) = Evaluate(
            "requirement_match(target, Contiguous).success",
            context,
            reflection);
        var (binding, bindingDiagnostics) = Evaluate(
            "requirement_match(target, Contiguous).T",
            context,
            reflection);
        var (same, sameDiagnostics) = Evaluate(
            "is_type(requirement_match(target, Contiguous).T, expected)",
            context,
            reflection);
        var (mappingKey, mappingKeyDiagnostics) = Evaluate(
            "requirement_match(target, Mapping).K",
            context,
            reflection);
        var (mappingValue, mappingValueDiagnostics) = Evaluate(
            "requirement_match(target, Mapping).V",
            context,
            reflection);
        var (methodBinding, methodBindingDiagnostics) = Evaluate(
            "target.match(Contiguous).T",
            context,
            reflection);

        Assert.True(Assert.IsType<CompileTimeValue.Boolean>(success).Value);
        Assert.True(TypeIdentity.ResolvedEquals(
            TypeRef.Int,
            Assert.IsType<CompileTimeValue.Type>(binding).Value));
        Assert.True(Assert.IsType<CompileTimeValue.Boolean>(same).Value);
        Assert.True(TypeIdentity.ResolvedEquals(
            TypeRef.Usize,
            Assert.IsType<CompileTimeValue.Type>(mappingKey).Value));
        Assert.True(TypeIdentity.ResolvedEquals(
            TypeRef.Int,
            Assert.IsType<CompileTimeValue.Type>(mappingValue).Value));
        Assert.True(TypeIdentity.ResolvedEquals(
            TypeRef.Int,
            Assert.IsType<CompileTimeValue.Type>(methodBinding).Value));
        CompilerTestHelpers.AssertNoErrors(successDiagnostics);
        CompilerTestHelpers.AssertNoErrors(bindingDiagnostics);
        CompilerTestHelpers.AssertNoErrors(sameDiagnostics);
        CompilerTestHelpers.AssertNoErrors(mappingKeyDiagnostics);
        CompilerTestHelpers.AssertNoErrors(mappingValueDiagnostics);
        CompilerTestHelpers.AssertNoErrors(methodBindingDiagnostics);
    }

    [Fact]
    public void SatisfiesAndDeclaresRequirement_KeepStructuralAndMarkerSemanticsSeparate()
    {
        var program = RequirementReflectionProgram();
        var reflection = new ProgramCompileTimeReflection(program);
        var bufferContext = new CompileTimeEvaluationContext();
        bufferContext.Define("target", new CompileTimeValue.Type(new TypeRef.Named("Buffer", [])));
        var viewContext = new CompileTimeEvaluationContext();
        viewContext.Define("target", new CompileTimeValue.Type(new TypeRef.Named("View", [])));

        var (bufferSatisfies, bufferSatisfiesDiagnostics) = Evaluate(
            "satisfies(target, Contiguous)", bufferContext, reflection);
        var (viewSatisfies, viewSatisfiesDiagnostics) = Evaluate(
            "satisfies(target, Contiguous)", viewContext, reflection);
        var (bufferDeclares, bufferDeclaresDiagnostics) = Evaluate(
            "declares_requirement(target, Mapping)", bufferContext, reflection);
        var (viewDeclares, viewDeclaresDiagnostics) = Evaluate(
            "declares_requirement(target, Mapping)", viewContext, reflection);

        Assert.True(Assert.IsType<CompileTimeValue.Boolean>(bufferSatisfies).Value);
        Assert.True(Assert.IsType<CompileTimeValue.Boolean>(viewSatisfies).Value);
        Assert.True(Assert.IsType<CompileTimeValue.Boolean>(bufferDeclares).Value);
        Assert.False(Assert.IsType<CompileTimeValue.Boolean>(viewDeclares).Value);
        CompilerTestHelpers.AssertNoErrors(bufferSatisfiesDiagnostics);
        CompilerTestHelpers.AssertNoErrors(viewSatisfiesDiagnostics);
        CompilerTestHelpers.AssertNoErrors(bufferDeclaresDiagnostics);
        CompilerTestHelpers.AssertNoErrors(viewDeclaresDiagnostics);
    }

    [Fact]
    public void RequirementMatch_ReportsUnknownBindingProperty()
    {
        var program = RequirementReflectionProgram();
        var context = new CompileTimeEvaluationContext();
        context.Define("target", new CompileTimeValue.Type(new TypeRef.Named("Buffer", [])));

        var (value, diagnostics) = Evaluate(
            "requirement_match(target, Contiguous).Missing",
            context,
            new ProgramCompileTimeReflection(program));

        Assert.Null(value);
        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("does not have property 'Missing'", StringComparison.Ordinal));
    }

    [Fact]
    public void Registry_DoesNotReplaceExistingIntrinsic()
    {
        var registry = CompileTimeIntrinsicRegistry.CreateDefault();

        Assert.False(registry.Register(new ConcatCompileTimeIntrinsic()));
        Assert.True(registry.TryGet("concat", out var intrinsic));
        Assert.IsType<ConcatCompileTimeIntrinsic>(intrinsic);
    }

    [Fact]
    public void ModuleIntrinsic_ReportsUnknownModule()
    {
        var program = CompilerTestHelpers.Parse(
            """
            module sample;
            fn main() -> int { return 0; }
            """);

        var (value, diagnostics) = Evaluate(
            "module(\"missing\")",
            reflection: new ProgramCompileTimeReflection(program));

        Assert.Null(value);
        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
                diagnostic.Message.Contains("module 'missing' is not visible", StringComparison.Ordinal));
    }

    [Fact]
    public void ModuleTypeLookup_ConstructsModuleQualifiedTypes()
    {
        var program = CompilerTestHelpers.Parse(
            """
            module sample;

            public struct User {}
            public struct Box<T> {}
            """);
        var reflection = new ProgramCompileTimeReflection(program);

        var (user, userDiagnostics) = Evaluate(
            "module(\"sample\").public_type(\"User\")",
            reflection: reflection);
        var (pointer, pointerDiagnostics) = Evaluate(
            "Type.pointer(module(\"sample\").type(\"User\"))",
            reflection: reflection);
        var (box, boxDiagnostics) = Evaluate(
            "Type.generic(module(\"sample\").type(\"Box\"), [int])",
            reflection: reflection);

        var userType = Assert.IsType<TypeRef.Named>(
            Assert.IsType<CompileTimeValue.Type>(user).Value);
        Assert.Equal("User", userType.Name);
        Assert.Equal("sample", userType.ModuleName);

        var pointerType = Assert.IsType<TypeRef.Pointer>(
            Assert.IsType<CompileTimeValue.Type>(pointer).Value);
        Assert.Equal(
            "sample",
            Assert.IsType<TypeRef.Named>(pointerType.Element).ModuleName);

        var boxType = Assert.IsType<TypeRef.Named>(
            Assert.IsType<CompileTimeValue.Type>(box).Value);
        Assert.Equal("Box", boxType.Name);
        Assert.Equal("sample", boxType.ModuleName);
        Assert.Equal(TypeRef.Int, Assert.Single(boxType.Arguments));
        CompilerTestHelpers.AssertNoErrors(userDiagnostics);
        CompilerTestHelpers.AssertNoErrors(pointerDiagnostics);
        CompilerTestHelpers.AssertNoErrors(boxDiagnostics);
    }

    [Fact]
    public void ModulePublicTypeLookup_RejectsPrivateAndMissingTypes()
    {
        var program = CompilerTestHelpers.Parse(
            """
            module sample;

            struct Hidden {}
            """);
        var reflection = new ProgramCompileTimeReflection(program);

        var (privateType, privateDiagnostics) = Evaluate(
            "module(\"sample\").public_type(\"Hidden\")",
            reflection: reflection);
        var (missingType, missingDiagnostics) = Evaluate(
            "module(\"sample\").type(\"Missing\")",
            reflection: reflection);

        Assert.Null(privateType);
        Assert.Null(missingType);
        Assert.Contains(privateDiagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("sample.Hidden", StringComparison.Ordinal)
            && diagnostic.Message.Contains("not public", StringComparison.Ordinal));
        Assert.Contains(missingDiagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("does not contain type 'Missing'", StringComparison.Ordinal));
    }

    [Fact]
    public void AttributeLookup_ReturnsDynamicFieldsAndNullWhenAbsent()
    {
        var program = CompilerTestHelpers.Parse(
            """
            attribute route on fn {
                method: string;
                path: string;
            }

            @route("GET", path: "/users")
            public fn users() -> int {
                return 0;
            }
            """);
        var reflection = new ProgramCompileTimeReflection(program);
        var context = new CompileTimeEvaluationContext();
        context.Define("handler", new CompileTimeValue.Syntax(Assert.Single(program.Functions)));

        var (method, methodDiagnostics) = Evaluate(
            "handler.attribute(\"route\").method",
            context,
            reflection);
        var (path, pathDiagnostics) = Evaluate(
            "handler.attribute(\"route\").path",
            context,
            reflection);
        var (missing, missingDiagnostics) = Evaluate(
            "handler.attribute(\"missing\") == null",
            context,
            reflection);

        Assert.Equal("GET", Assert.IsType<CompileTimeValue.String>(method).Value);
        Assert.Equal("/users", Assert.IsType<CompileTimeValue.String>(path).Value);
        Assert.True(Assert.IsType<CompileTimeValue.Boolean>(missing).Value);
        CompilerTestHelpers.AssertNoErrors(methodDiagnostics);
        CompilerTestHelpers.AssertNoErrors(pathDiagnostics);
        CompilerTestHelpers.AssertNoErrors(missingDiagnostics);
    }

    [Fact]
    public void NullPropertyAccess_ReportsObjectLikeDiagnostic()
    {
        var program = CompilerTestHelpers.Parse(
            """
            public fn users() -> int {
                return 0;
            }
            """);
        var reflection = new ProgramCompileTimeReflection(program);
        var context = new CompileTimeEvaluationContext();
        context.Define("handler", new CompileTimeValue.Syntax(Assert.Single(program.Functions)));

        var (value, diagnostics) = Evaluate(
            "handler.attribute(\"missing\").path",
            context,
            reflection);

        Assert.Null(value);
        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains(
                "Compile-time null value is not object-like",
                StringComparison.Ordinal));
    }

    [Fact]
    public void FunctionSignature_MatchesStructuredFunctionTypeLiteral()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct Request {}
            struct Response {}

            struct Service {
                public fn handle(request: Request*) -> Response {
                    return Response {};
                }
            }

            public fn handler(request: Request*) -> Response {
                return Response {};
            }
            """);
        var reflection = new ProgramCompileTimeReflection(program);
        var context = new CompileTimeEvaluationContext();
        context.Define("handler", new CompileTimeValue.Syntax(Assert.Single(program.Functions)));
        Assert.True(reflection.TryGetMethods(new TypeRef.Named("Service", []), out var methods));
        context.Define("method", new CompileTimeValue.ResolvedMethod(Assert.Single(methods)));

        var (signature, signatureDiagnostics) = Evaluate(
            "handler.signature",
            context,
            reflection);
        var (matches, matchDiagnostics) = Evaluate(
            "handler.match(Type.from(fn(Request*) -> Response))",
            context,
            reflection);
        var (doesNotMatch, mismatchDiagnostics) = Evaluate(
            "handler.match(Type.from(fn(Request*) -> int))",
            context,
            reflection);
        var (methodMatches, methodMatchDiagnostics) = Evaluate(
            "method.match(Type.from(fn(Request*) -> Response))",
            context,
            reflection);

        Assert.IsType<TypeRef.Function>(Assert.IsType<CompileTimeValue.Type>(signature).Value);
        Assert.True(Assert.IsType<CompileTimeValue.Boolean>(matches).Value);
        Assert.False(Assert.IsType<CompileTimeValue.Boolean>(doesNotMatch).Value);
        Assert.True(Assert.IsType<CompileTimeValue.Boolean>(methodMatches).Value);
        CompilerTestHelpers.AssertNoErrors(signatureDiagnostics);
        CompilerTestHelpers.AssertNoErrors(matchDiagnostics);
        CompilerTestHelpers.AssertNoErrors(mismatchDiagnostics);
        CompilerTestHelpers.AssertNoErrors(methodMatchDiagnostics);
    }

    [Fact]
    public void TypeFactories_ConstructStructuredTypes()
    {
        var context = new CompileTimeEvaluationContext();
        context.Define("user", new CompileTimeValue.Type(new TypeRef.Named("User", [])));
        context.Define("box", new CompileTimeValue.Type(new TypeRef.Named("Box", [])));

        var (pointer, pointerDiagnostics) = Evaluate("Type.pointer(user)", context);
        var (constant, constDiagnostics) = Evaluate("Type.const(user)", context);
        var (array, arrayDiagnostics) = Evaluate("Type.array(int, 16)", context);
        var (generic, genericDiagnostics) = Evaluate("Type.generic(box, [int])", context);
        var (function, functionDiagnostics) = Evaluate(
            "Type.function([Type.pointer(user), usize], bool)",
            context);

        Assert.Equal(
            new TypeRef.Pointer(new TypeRef.Named("User", [])),
            Assert.IsType<CompileTimeValue.Type>(pointer).Value);
        Assert.Equal(
            new TypeRef.Const(new TypeRef.Named("User", [])),
            Assert.IsType<CompileTimeValue.Type>(constant).Value);
        Assert.Equal(
            new TypeRef.FixedArray(TypeRef.Int, new ArrayLengthNode.Integer(16)),
            Assert.IsType<CompileTimeValue.Type>(array).Value);
        var genericType = Assert.IsType<TypeRef.Named>(
            Assert.IsType<CompileTimeValue.Type>(generic).Value);
        Assert.Equal("Box", genericType.Name);
        Assert.Equal(TypeRef.Int, Assert.Single(genericType.Arguments));

        var functionType = Assert.IsType<TypeRef.Function>(
            Assert.IsType<CompileTimeValue.Type>(function).Value);
        Assert.Equal(2, functionType.Parameters.Count);
        Assert.IsType<TypeRef.Pointer>(functionType.Parameters[0]);
        Assert.Equal(TypeRef.Usize, functionType.Parameters[1]);
        Assert.Equal(TypeRef.Bool, functionType.ReturnType);
        CompilerTestHelpers.AssertNoErrors(pointerDiagnostics);
        CompilerTestHelpers.AssertNoErrors(constDiagnostics);
        CompilerTestHelpers.AssertNoErrors(arrayDiagnostics);
        CompilerTestHelpers.AssertNoErrors(genericDiagnostics);
        CompilerTestHelpers.AssertNoErrors(functionDiagnostics);
    }

    [Theory]
    [InlineData("Type.pointer(1)", "Type.pointer")]
    [InlineData("Type.array(int, -1)", "Type.array")]
    [InlineData("Type.generic(int, [int, 1])", "Type.generic")]
    [InlineData("Type.function([int, 1], bool)", "Type.function")]
    public void TypeFactories_RejectInvalidArguments(string source, string method)
    {
        var (value, diagnostics) = Evaluate(source);

        Assert.Null(value);
        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains(method, StringComparison.Ordinal));
    }

    private static (CompileTimeValue? Value, DiagnosticBag Diagnostics) Evaluate(
        string source,
        CompileTimeEvaluationContext? context = null,
        ICompileTimeReflection? reflection = null)
    {
        var diagnostics = new DiagnosticBag();
        var evaluator = new CompileTimeExpressionEvaluator(
            diagnostics,
            reflection: reflection);
        var value = evaluator.Evaluate(
            CompilerTestHelpers.ParseTokenExpression(source),
            context ?? new CompileTimeEvaluationContext());
        return (value, diagnostics);
    }

    private static ProgramNode ReflectionProgram() => CompilerTestHelpers.Parse(
        """
        attribute json_name on field {
            value: string;
        }

        struct User {
            @json_name("displayName")
            name: int;

            age: int;
        }
        """);

    private static ProgramNode RequirementReflectionProgram() => CompilerTestHelpers.Parse(
        """
        requires Contiguous<T> {
            data: T*;
            length: usize;
        }

        requires Mapping<K, V>;

        struct Buffer: Contiguous<int>, Mapping<usize, int> {
            data: int*;
            length: usize;
        }

        struct View {
            data: int*;
            length: usize;
        }
        """);
}
