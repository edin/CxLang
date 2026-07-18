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
                Assert.IsType<StructFieldNode>(Assert.IsType<CompileTimeValue.Syntax>(field).Value).Name));
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
            @aliases({ "first", "second", "third" })
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
        CompilerTestHelpers.AssertNoErrors(successDiagnostics);
        CompilerTestHelpers.AssertNoErrors(bindingDiagnostics);
        CompilerTestHelpers.AssertNoErrors(sameDiagnostics);
        CompilerTestHelpers.AssertNoErrors(mappingKeyDiagnostics);
        CompilerTestHelpers.AssertNoErrors(mappingValueDiagnostics);
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
