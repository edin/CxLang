using Cx.Compiler.CompileTime;
using Cx.Compiler.Diagnostics;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class CompileTimeExpressionEvaluatorTests
{
    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("!false", true)]
    [InlineData("3 < 4", true)]
    [InlineData("3 >= 4", false)]
    [InlineData("\"alpha\" < \"beta\"", true)]
    [InlineData("1 == 1", true)]
    [InlineData("1 != 1", false)]
    public void Evaluate_ReturnsBooleanValues(string source, bool expected)
    {
        var (value, diagnostics) = Evaluate(source);

        Assert.Equal(expected, Assert.IsType<CompileTimeValue.Boolean>(value).Value);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Theory]
    [InlineData("42", 42L)]
    [InlineData("1_024", 1024L)]
    [InlineData("0xff", 255L)]
    [InlineData("0b1010", 10L)]
    [InlineData("-12", -12L)]
    public void Evaluate_ReturnsIntegerValues(string source, long expected)
    {
        var (value, diagnostics) = Evaluate(source);

        Assert.Equal(expected, Assert.IsType<CompileTimeValue.Integer>(value).Value);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Theory]
    [InlineData("int", "int")]
    [InlineData("bool", "bool")]
    [InlineData("double", "double")]
    public void Evaluate_ResolvesKnownTypesAsCompileTimeValues(string source, string expectedName)
    {
        var (value, diagnostics) = Evaluate(source);

        var type = Assert.IsType<CompileTimeValue.Type>(value);
        Assert.Equal(expectedName, Assert.IsType<TypeRef.Named>(type.Value).Name);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Theory]
    [InlineData("pointer", "is_pointer")]
    [InlineData("array", "is_array")]
    [InlineData("named", "is_named")]
    [InlineData("function", "is_function")]
    [InlineData("constant", "is_const")]
    public void Evaluate_TypeShapePropertiesReturnTypedBooleans(
        string bindingName,
        string propertyName)
    {
        var context = new CompileTimeEvaluationContext();
        context.Define("pointer", new CompileTimeValue.Type(new TypeRef.Pointer(TypeRef.Int)));
        context.Define("array", new CompileTimeValue.Type(new TypeRef.FixedArray(
            TypeRef.Int,
            new ArrayLengthNode.Integer(4))));
        context.Define("named", new CompileTimeValue.Type(TypeRef.Int));
        context.Define("function", new CompileTimeValue.Type(new TypeRef.Function([], TypeRef.Int)));
        context.Define("constant", new CompileTimeValue.Type(new TypeRef.Const(TypeRef.Int)));

        var (value, diagnostics) = Evaluate($"{bindingName}.{propertyName}", context);

        Assert.True(Assert.IsType<CompileTimeValue.Boolean>(value).Value);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Evaluate_UnescapesStringValues()
    {
        var (value, diagnostics) = Evaluate("\"line\\nnext\"");

        Assert.Equal("line\nnext", Assert.IsType<CompileTimeValue.String>(value).Value);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Evaluate_ReturnsVariableLengthCompileTimeList()
    {
        var (value, diagnostics) = Evaluate("[1, 2, 3]");

        var values = Assert.IsType<CompileTimeValue.List>(value).Values;
        Assert.Equal([1L, 2L, 3L], values.Select(item =>
            Assert.IsType<CompileTimeValue.Integer>(item).Value));
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Evaluate_UsesLexicalBindingsAndChildShadowing()
    {
        var parent = new CompileTimeEvaluationContext();
        Assert.True(parent.Define("enabled", new CompileTimeValue.Boolean(true)));
        var child = parent.CreateChild();
        Assert.True(child.Define("enabled", new CompileTimeValue.Boolean(false)));
        Assert.False(child.Define("enabled", new CompileTimeValue.Boolean(true)));

        var (parentValue, parentDiagnostics) = Evaluate("enabled", parent);
        var (childValue, childDiagnostics) = Evaluate("enabled", child);

        Assert.True(Assert.IsType<CompileTimeValue.Boolean>(parentValue).Value);
        Assert.False(Assert.IsType<CompileTimeValue.Boolean>(childValue).Value);
        CompilerTestHelpers.AssertNoErrors(parentDiagnostics);
        CompilerTestHelpers.AssertNoErrors(childDiagnostics);
    }

    [Fact]
    public void Evaluate_CombinesBindingsWithBooleanOperators()
    {
        var context = new CompileTimeEvaluationContext();
        context.Define("enabled", new CompileTimeValue.Boolean(true));
        context.Define("count", new CompileTimeValue.Integer(4));

        var (value, diagnostics) = Evaluate("enabled && count >= 3", context);

        Assert.True(Assert.IsType<CompileTimeValue.Boolean>(value).Value);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Theory]
    [InlineData("target_os == \"windows\"", true)]
    [InlineData("target_os == \"linux\"", false)]
    [InlineData("target_os != \"linux\"", true)]
    [InlineData("target_os == \"windows\" && target_arch == \"x64\"", true)]
    [InlineData("target_os == \"linux\" || debug", false)]
    public void Evaluate_UsesExplicitTargetLikeBindings(string source, bool expected)
    {
        var context = new CompileTimeEvaluationContext();
        context.Define("target_os", new CompileTimeValue.String("windows"));
        context.Define("target_arch", new CompileTimeValue.String("x64"));
        context.Define("debug", new CompileTimeValue.Boolean(false));

        var (value, diagnostics) = Evaluate(source, context);

        Assert.Equal(expected, Assert.IsType<CompileTimeValue.Boolean>(value).Value);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Theory]
    [InlineData("false && missing")]
    [InlineData("true || missing")]
    [InlineData("true ? 1 : missing")]
    public void Evaluate_DoesNotEvaluateUnselectedExpressions(string source)
    {
        var (value, diagnostics) = Evaluate(source);

        Assert.NotNull(value);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Evaluate_ReportsUnknownCompileTimeName()
    {
        var (value, diagnostics) = Evaluate("missing");

        Assert.Null(value);
        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("Unknown compile-time name 'missing'", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_ReportsInvalidOperandKinds()
    {
        var (value, diagnostics) = Evaluate("1 && true");

        Assert.Null(value);
        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains(
                "Compile-time operator '&&' does not support integer values",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_ReportsUnknownCompileTimeIntrinsic()
    {
        var (value, diagnostics) = Evaluate("function_call()");

        Assert.Null(value);
        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains(
                "Unknown compile-time intrinsic 'function_call'",
                StringComparison.Ordinal));
    }

    [Fact]
    public void EvaluateMember_UsesRegisteredCompileTimeProperty()
    {
        var context = new CompileTimeEvaluationContext();
        context.Define("sample", new SampleCompileTimeObject());

        var properties = CompileTimePropertyRegistry.CreateFromObjects(new SampleScriptObject());
        var (value, diagnostics) = Evaluate("sample.answer", context, properties);

        Assert.Equal(42, Assert.IsType<CompileTimeValue.Integer>(value).Value);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void EvaluateMember_DoesNotReportMissingPropertyAfterPropertyFailure()
    {
        var context = new CompileTimeEvaluationContext();
        context.Define("sample", new SampleCompileTimeObject());

        var properties = CompileTimePropertyRegistry.CreateFromObjects(new SampleScriptObject());
        var (value, diagnostics) = Evaluate("sample.failed", context, properties);

        Assert.Null(value);
        var diagnostic = Assert.Single(diagnostics.Diagnostics);
        Assert.Contains("Sample property failed", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EvaluateMethod_ListAddMutatesCompileTimeListAndReturnsReceiver()
    {
        var context = new CompileTimeEvaluationContext();
        var list = new CompileTimeValue.List([]);
        context.Define("values", list);

        var (result, diagnostics) = Evaluate("values.add(42)", context);

        Assert.Same(list, result);
        Assert.Equal(42, Assert.IsType<CompileTimeValue.Integer>(Assert.Single(list.Values)).Value);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void EvaluateMethod_ReportsMissingObjectMethod()
    {
        var context = new CompileTimeEvaluationContext();
        context.Define("values", new CompileTimeValue.List([]));

        var (result, diagnostics) = Evaluate("values.missing()", context);

        Assert.Null(result);
        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains(
                "Compile-time list value does not have method 'missing'",
                StringComparison.Ordinal));
    }

    [Fact]
    public void EvaluateMethod_ParameterCreateBuildsTypedParameterSyntax()
    {
        var (result, diagnostics) = Evaluate("Parameter.create(\"value\", int)");

        var parameter = Assert.IsType<ParameterNode>(
            Assert.IsType<CompileTimeValue.Syntax>(result).Value);
        Assert.Equal("value", parameter.Name);
        Assert.Equal("int", parameter.TypeNode.ToSourceText());
        Assert.Empty(parameter.Attributes);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void EvaluateMethod_ParameterCreateAcceptsAttributeSyntaxList()
    {
        var context = new CompileTimeEvaluationContext();
        var attribute = new AttributeApplicationNode(
            Cx.Compiler.Source.Location.Synthetic("<test>"),
            "export",
            []);
        context.Define("attributes", new CompileTimeValue.List([
            new CompileTimeValue.Syntax(attribute),
        ]));

        var (result, diagnostics) = Evaluate(
            "Parameter.create(as_name(\"context\"), int, attributes)",
            context);

        var parameter = Assert.IsType<ParameterNode>(
            Assert.IsType<CompileTimeValue.Syntax>(result).Value);
        Assert.Equal("context", parameter.Name);
        Assert.Same(attribute, Assert.Single(parameter.Attributes));
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void EvaluateMethod_ParameterCreateReportsInvalidArguments()
    {
        var (result, diagnostics) = Evaluate("Parameter.create(1, true)");

        Assert.Null(result);
        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains(
                "expects a string or name as argument 1",
                StringComparison.Ordinal));
    }

    [Fact]
    public void EvaluateMethod_AttributeArgumentAndAttributeCreateBuildTypedSyntax()
    {
        var (result, diagnostics) = Evaluate(
            "Attribute.create(\"binding_name\", [AttributeArgument.named(\"value\", \"native_context\")])");

        var attribute = Assert.IsType<AttributeApplicationNode>(
            Assert.IsType<CompileTimeValue.Syntax>(result).Value);
        Assert.Equal("binding_name", attribute.Name);
        var argument = Assert.Single(attribute.Arguments);
        Assert.Equal("value", argument.Name);
        Assert.Equal("\"native_context\"", argument.Value.ToSourceText());
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void EvaluateMethod_AttributeCreateComposesWithParameterCreate()
    {
        var (result, diagnostics) = Evaluate(
            """
            Parameter.create("context", int, [
                Attribute.create("binding_name", [
                    AttributeArgument.positional("native_context")
                ])
            ])
            """);

        var parameter = Assert.IsType<ParameterNode>(
            Assert.IsType<CompileTimeValue.Syntax>(result).Value);
        var attribute = Assert.Single(parameter.Attributes);
        Assert.Equal("binding_name", attribute.Name);
        Assert.Null(Assert.Single(attribute.Arguments).Name);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void EvaluateMethod_ParameterTransformationsReturnModifiedCopies()
    {
        var context = new CompileTimeEvaluationContext();
        var source = new ParameterNode(
            Cx.Compiler.Source.Location.Synthetic("<test>"),
            "value",
            [],
            TypeNode: TypeNode.Named(Cx.Compiler.Source.Location.Synthetic("<test>"), "int"));
        context.Define("parameter", new CompileTimeValue.Syntax(source));

        var (result, diagnostics) = Evaluate(
            "parameter.with_name(as_name(\"renamed\")).with_type(usize)",
            context);

        var transformed = Assert.IsType<ParameterNode>(
            Assert.IsType<CompileTimeValue.Syntax>(result).Value);
        Assert.Equal("renamed", transformed.Name);
        Assert.Equal("usize", transformed.TypeNode.ToSourceText());
        Assert.Equal("value", source.Name);
        Assert.Equal("int", source.TypeNode.ToSourceText());
        Assert.NotSame(source, transformed);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void EvaluateMethod_ParameterTransformationReportsWrongValueKind()
    {
        var context = new CompileTimeEvaluationContext();
        context.Define("parameter", new CompileTimeValue.Syntax(new ParameterNode(
            Cx.Compiler.Source.Location.Synthetic("<test>"),
            "value",
            [])));

        var (result, diagnostics) = Evaluate("parameter.with_type(true)", context);

        Assert.Null(result);
        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains(
                "parameter.with_type' expects a type",
                StringComparison.Ordinal));
    }

    [Fact]
    public void EvaluateMethod_ParameterAttributeTransformationsReturnModifiedCopies()
    {
        var location = Cx.Compiler.Source.Location.Synthetic("<test>");
        var sourceAttribute = new AttributeApplicationNode(location, "source", []);
        var source = new ParameterNode(
            location,
            "value",
            [sourceAttribute],
            TypeNode: TypeNode.Named(location, "int"));
        var context = new CompileTimeEvaluationContext();
        context.Define("parameter", new CompileTimeValue.Syntax(source));

        var (result, diagnostics) = Evaluate(
            """
            parameter
                .with_attributes([Attribute.create("replacement")])
                .add_attribute(Attribute.create("extra"))
            """,
            context);

        var transformed = Assert.IsType<ParameterNode>(
            Assert.IsType<CompileTimeValue.Syntax>(result).Value);
        Assert.Equal(["replacement", "extra"], transformed.Attributes.Select(attribute => attribute.Name));
        Assert.Same(sourceAttribute, Assert.Single(source.Attributes));
        Assert.NotSame(source, transformed);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void EvaluateMethod_ParameterWithAttributesRejectsNonAttributeItems()
    {
        var context = new CompileTimeEvaluationContext();
        context.Define("parameter", new CompileTimeValue.Syntax(new ParameterNode(
            Cx.Compiler.Source.Location.Synthetic("<test>"),
            "value",
            [])));

        var (result, diagnostics) = Evaluate("parameter.with_attributes([1])", context);

        Assert.Null(result);
        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains(
                "expects attribute syntax items",
                StringComparison.Ordinal));
    }

    private static (CompileTimeValue? Value, DiagnosticBag Diagnostics) Evaluate(
        string source,
        CompileTimeEvaluationContext? context = null,
        CompileTimePropertyRegistry? properties = null)
    {
        var diagnostics = new DiagnosticBag();
        var evaluator = new CompileTimeExpressionEvaluator(diagnostics, properties: properties);
        var value = evaluator.Evaluate(
            CompilerTestHelpers.ParseTokenExpression(source),
            context ?? new CompileTimeEvaluationContext());
        return (value, diagnostics);
    }

    private sealed record SampleCompileTimeObject : CompileTimeObjectValue
    {
        public override string DisplayType => "sample object";
    }

    private sealed class SampleScriptObject : CompileTimeScriptObject
    {
        public override Type ReceiverType => typeof(SampleCompileTimeObject);

        [CompileTimeProperty("answer")]
        private CompileTimePropertyResult Answer(
            SampleCompileTimeObject receiver,
            CompileTimePropertyContext context) =>
            CompileTimePropertyResult.From(new CompileTimeValue.Integer(42));

        [CompileTimeProperty("failed")]
        private CompileTimePropertyResult Failed(
            SampleCompileTimeObject receiver,
            CompileTimePropertyContext context)
        {
            context.Diagnostics.Report(context.Location, "Sample property failed.");
            return new CompileTimePropertyResult.Failed();
        }
    }
}
