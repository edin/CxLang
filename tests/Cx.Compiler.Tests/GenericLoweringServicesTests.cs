using Cx.Compiler.Diagnostics;
using Cx.Compiler.Lowering;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class GenericLoweringServicesTests
{
    [Fact]
    public void GenericUseCollector_UsesInferredLocalTypesForConstrainedCall()
    {
        var program = CompilerTestHelpers.Parse(
            """
            requires Add<T> {
                fn operator +(other: T) -> T;
            }

            struct Vec2 {
                x: int;

                fn operator +(other: Vec2) -> Vec2 {
                    return Vec2 { x: self.x + other.x };
                }
            }

            fn sum<T>(left: T, right: T) -> T
            where T: Add<T> {
                return left + right;
            }

            fn main() -> int {
                let left = Vec2 { x: 10 };
                let right = Vec2 { x: 20 };
                let result = sum(left, right);
                return result.x;
            }
            """);
        var diagnostics = new DiagnosticBag();
        var model = new SemanticModel();
        new ScopeResolver(diagnostics, model).Resolve(program);
        new TypeResolutionPass(diagnostics, model).Resolve(program);
        program = new TypeInferencePass(diagnostics, model).Apply(program);
        CompilerTestHelpers.AssertNoErrors(diagnostics);

        var main = program.Functions.Single(function => function.Name == "main");
        Assert.Equal(
            ["Vec2", "Vec2"],
            main.Body
                .OfType<LetStatement>()
                .Take(2)
                .Select(let => let.TypeNode!.ToSourceText()));
        var resultInitializer = Assert.IsType<CallExpressionNode>(
            main.Body
                .OfType<LetStatement>()
                .Single(let => let.Name == "result")
                .Initializer);
        Assert.NotNull(resultInitializer.Semantic.ResolvedCall);
        Assert.Equal(
            ["Vec2"],
            resultInitializer.Semantic.ResolvedCall!.TypeArgumentRefs
                .Select(TypeRefFormatter.ToCxString));

        var uses = new GenericUseCollector(program, model.FunctionCatalog)
            .Collect(program)
            .Where(use => use.Function.Name == "sum")
            .ToList();

        Assert.NotEmpty(uses);
        Assert.All(uses, use => Assert.Equal(["Vec2"], TypeArguments(use)));
        Assert.Single(uses
            .Select(use => string.Join(",", TypeArguments(use)))
            .Distinct(StringComparer.Ordinal));
    }

    [Fact]
    public void GenericUseCollector_FindsExplicitAndInferredGenericCalls()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn identity<T>(value: T) -> T {
                return value;
            }

            fn main() -> int {
                let explicitValue: int = identity<int>(10);
                return identity(explicitValue);
            }
            """);
        CompilerTestHelpers.Resolve(program);

        var collector = new GenericUseCollector(program);
        var uses = collector
            .Collect(program)
            .Select(use => $"{use.Function.Name}<{string.Join(",", TypeArguments(use))}>")
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("identity<int>", uses);
    }

    [Fact]
    public void AuditRawGenericUses_ReportsCompletedAstMigration()
    {
        var result = new CxCompiler().AuditRawGenericUses([
            CompilerTestHelpers.Source(
                """
                fn identity<T>(value: T) -> T {
                    return value;
                }

                fn main() -> int {
                    return identity<int>(10);
                }
                """),
        ]);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Equal("No raw generic use fallback found.", result.Output);
    }

    [Fact]
    public void GenericUseCollector_UsesCallResolverForNormalGenericCalls()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct Box<T> {
                value: T;

                static fn make(value: T) -> Box<T> {
                    return Box<T> { value: value };
                }

                fn replace(value: T) -> bool {
                    self.value = value;
                    return true;
                }
            }

            fn identity<T>(value: T) -> T {
                return value;
            }

            fn main() -> int {
                let box: Box<int> = Box<int>.make(10);
                box.replace(20);
                return identity(box.value);
            }
            """);
        CompilerTestHelpers.Resolve(program);

        var uses = new GenericUseCollector(program)
            .Collect(program)
            .Select(use => $"{(use.Function.OwnerTypeNode is null ? "" : use.Function.OwnerTypeNode.ToSourceText() + ".")}{use.Function.Name}<{string.Join(",", TypeArguments(use))}>")
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("Box.make<int>", uses);
        Assert.Contains("Box.replace<int>", uses);
        Assert.Contains("identity<int>", uses);
    }

    [Fact]
    public void GenericSpecialization_HandlesNestedMemberReceiversAndUsingBindings()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            struct Box<T> {
                value: T;

                fn get() -> T {
                    return self.value;
                }
            }

            struct Holder {
                box: Box<int>;

                static fn create() -> Holder {
                    return Holder {
                        box: Box<int> { value: 21 }
                    };
                }

                fn read() -> int {
                    return self.box.get();
                }

                fn dispose() -> void {
                }
            }

            fn main() -> int {
                using holder = Holder.create();
                return holder.box.get() + holder.read();
            }
            """)
            .Succeeds()
            .OutputContains("int Box_get_int(", "int Holder_read(");
    }

    [Fact]
    public void GenericUseCollector_RebindsCatalogResultsToTheCurrentAstDeclaration()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn identity<T>(value: T) -> T {
                return value;
            }

            fn main() -> int {
                return identity(10);
            }
            """);
        var model = CompilerTestHelpers.Resolve(program);
        var catalog = Assert.IsType<FunctionCatalog>(model.FunctionCatalog);
        var originalIdentity = Assert.Single(
            program.Functions,
            function => function.Name == "identity");
        var rewrittenIdentity = originalIdentity with
        {
            Body = originalIdentity.Body.ToList(),
        };
        var rewrittenProgram = program with
        {
            Functions = program.Functions
                .Select(function => ReferenceEquals(function, originalIdentity)
                    ? rewrittenIdentity
                    : function)
                .ToList(),
        };

        var uses = new GenericUseCollector(rewrittenProgram, catalog)
            .Collect(rewrittenProgram)
            .Where(use => use.Function.Name == "identity")
            .ToList();

        Assert.NotEmpty(uses);
        Assert.All(uses, use => Assert.Same(rewrittenIdentity, use.Function));
        Assert.NotSame(catalog.GetFunctions("identity").Single().Declaration, rewrittenIdentity);
        Assert.Equal(
            originalIdentity.FunctionSymbol?.Id,
            rewrittenIdentity.FunctionSymbol?.Id);
    }

    [Fact]
    public void GenericUseCollector_DoesNotMergeResolvedOverloadsWithTheSameTypeArguments()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn select<T>(value: T) -> T {
                return value;
            }

            fn select<T>(value: T, fallback: T) -> T {
                return value;
            }

            fn main() -> int {
                let first: int = select(10);
                return select(first, 20);
            }
            """);
        var model = CompilerTestHelpers.Resolve(program);
        var catalog = Assert.IsType<FunctionCatalog>(model.FunctionCatalog);
        var main = Assert.Single(
            program.Functions,
            function => function.Name == "main");
        var overloads = program.Functions
            .Where(function => function.Name == "select")
            .OrderBy(function => function.Parameters.Count)
            .ToList();
        var calls = ExecutableAstTraversal
            .DescendantsAndSelf<CallExpressionNode>(main.Body)
            .OrderBy(call => call.Arguments.Count)
            .ToList();
        Assert.Equal(2, overloads.Count);
        Assert.Equal(2, calls.Count);
        for (var index = 0; index < calls.Count; index++)
        {
            calls[index].Semantic.ResolvedCall = new ResolvedCallInfo(
                overloads[index],
                [TypeRef.Int],
                IsInstance: false);
        }

        var uses = new GenericUseCollector(program, catalog)
            .Collect(main)
            .Where(use => use.Function.Name == "select")
            .ToList();

        Assert.Equal(2, uses.Count);
        Assert.Equal(
            2,
            uses.Select(use => use.Function.FunctionSymbol?.Id)
                .Distinct()
                .Count());
        Assert.All(
            uses,
            use => Assert.True(
                TypeIdentity.SpecializationEquals(
                    TypeRef.Int,
                    Assert.Single(use.TypeArgumentRefs))));
    }

    [Fact]
    public void GenericUseCollector_UsesResolvedAdapterExposedCalls()
    {
        var program = CompilerTestHelpers.Parse(
            """
            type u8 = unsigned char;
            type usize = unsigned long long;

            struct MiniVec<T> {
                static fn with_capacity(capacity: usize) -> MiniVec<T> {
                    return MiniVec<T> {};
                }

                fn add(value: T) -> bool {
                    return true;
                }
            }

            type MiniByteBuffer using MiniVec<u8> {
                expose static with_capacity -> Self;
                expose add as write_u8;
            }

            type MiniStringBuilder using MiniByteBuffer {
                expose static with_capacity -> Self;
                expose write_u8;
            }

            fn main() -> int {
                let builder: MiniStringBuilder = MiniStringBuilder.with_capacity(8);
                builder.write_u8(65);
                return 0;
            }
            """);
        var diagnostics = new DiagnosticBag();
        program = TypeAdapterLoweringPass.Apply(program, diagnostics);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
        CompilerTestHelpers.Resolve(program);

        var uses = new GenericUseCollector(program)
            .Collect(program)
            .Select(use => $"{use.Function.OwnerTypeNode?.ToSourceText()}.{use.Function.Name}<{string.Join(",", TypeArguments(use))}>")
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("MiniVec.with_capacity<u8>", uses);
        Assert.Contains("MiniVec.add<u8>", uses);
    }

    [Fact]
    public void GenericUseCollector_UsesCallResolverForAdapterSelfApiCalls()
    {
        var program = CompilerTestHelpers.Parse(
            """
            type u8 = unsigned char;

            struct MiniVec<T> {
                fn add(value: T) -> bool {
                    return true;
                }
            }

            type MiniByteBuffer using MiniVec<u8> {
                expose add as write_u8;
            }

            type MiniStringBuilder using MiniByteBuffer {
                expose write_u8;

                fn append_byte(value: u8) -> bool {
                    return self.write_u8(value);
                }
            }

            fn main() -> int {
                let builder: MiniStringBuilder = MiniStringBuilder {};
                return builder.append_byte((u8)65) ? 0 : 1;
            }
            """);
        var diagnostics = new DiagnosticBag();
        program = TypeAdapterLoweringPass.Apply(program, diagnostics);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
        CompilerTestHelpers.Resolve(program);

        var uses = new GenericUseCollector(program)
            .Collect(program)
            .Select(use => $"{use.Function.OwnerTypeNode?.ToSourceText()}.{use.Function.Name}<{string.Join(",", TypeArguments(use))}>")
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("MiniVec.add<u8>", uses);
    }

    [Fact]
    public void GenericUseCollector_FindsIteratorUsesInsideElseIf()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct Iterator<T> {
                current: T;

                fn next() -> bool {
                    return false;
                }

                fn value() -> T {
                    return self.current;
                }
            }

            struct Values<T> {
                fn iterator() -> Iterator<T> {
                    return Iterator<T> {};
                }
            }

            fn main() -> int {
                let values: Values<int> = Values<int> {};
                if (false) {
                } else if (true) {
                    foreach value: int in values {}
                }
                return 0;
            }
            """);
        CompilerTestHelpers.Resolve(program);
        var main = program.Functions.Single(function => function.Name == "main");

        var uses = new GenericUseCollector(program)
            .Collect(main)
            .Select(use => use.Function.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("iterator", uses);
        Assert.Contains("next", uses);
        Assert.Contains("value", uses);
    }

    [Fact]
    public void GenericTypeRewriter_RewritesNestedConcreteStructTypes()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct Box<T> {
                value: T;
            }

            fn use_box(value: Box<Box<int>>*) -> Box<int> {
                return value.value;
            }
            """);
        var diagnostics = new DiagnosticBag();
        new TypeResolutionPass(diagnostics).Resolve(program);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
        var concreteStructNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Box_int",
            "Box_Box_int",
        };

        var rewritten = GenericTypeRewriter.Rewrite(program, concreteStructNames);
        var function = Assert.Single(rewritten.Functions);

        Assert.Equal("Box_int", function.ReturnTypeNode.ToSourceText());
        Assert.Equal("Box_Box_int*", Assert.Single(function.Parameters).TypeNode.ToSourceText());
        var resolvedParameter = Assert.IsType<Cx.Compiler.Semantic.TypeRef.Pointer>(Assert.Single(function.Parameters).TypeNode?.Semantic.Type);
        Assert.Equal("Box_Box_int", Assert.IsType<Cx.Compiler.Semantic.TypeRef.Named>(resolvedParameter.Element).Name);
    }

    [Fact]
    public void GenericFunctionSpecializer_RewritesTypeNodesBesideCompatibilityStrings()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn identity<T>(value: T) -> T {
                let local: T = value;
                return local;
            }
            """);
        var generic = Assert.Single(program.Functions);

        var specialized = GenericFunctionSpecializer.Specialize(generic, [Type("int")]);
        var parameter = Assert.Single(specialized.Parameters);
        var local = Assert.IsType<LetStatement>(specialized.Body[0]);

        Assert.Equal("int", specialized.ReturnTypeNode.ToSourceText());
        Assert.Equal("int", parameter.TypeNode.ToSourceText());
        Assert.Equal("int", local.TypeNode?.ToSourceText());
    }

    [Fact]
    public void GenericFunctionSpecializer_RewritesSemanticTypeRefsOnTypeNodes()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct Box<T> {
                value: T;
            }

            fn identity<T>(value: Box<T>*) -> Box<T>* {
                let local: Box<T>* = value;
                return local;
            }
            """);
        var diagnostics = new DiagnosticBag();
        new TypeResolutionPass(diagnostics).Resolve(program);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
        var generic = program.Functions.Single();

        var specialized = GenericFunctionSpecializer.Specialize(generic, [Type("int")]);
        var parameter = Assert.Single(specialized.Parameters);
        var local = Assert.IsType<LetStatement>(specialized.Body[0]);

        Assert.Equal("Box<int>*", parameter.TypeNode.ToSourceText());
        Assert.Equal("Box<int>*", TypeRefFormatter.ToCxString(parameter.TypeNode!.Semantic.Type!));
        Assert.Equal("Box<int>*", local.TypeNode?.ToSourceText());
        Assert.Equal("Box<int>*", TypeRefFormatter.ToCxString(local.TypeNode!.Semantic.Type!));
    }

    [Fact]
    public void GenericTypeRewriter_RewritesExpressionTypeNodes()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct Box<T> {
                value: T;
            }

            fn identity<T>(value: T) -> T {
                return value;
            }

            fn main(value: void*) -> int {
                let casted: Box<int>* = (Box<int>*)value;
                let bytes: usize = sizeof(Box<int>);
                let box: Box<int> = Box<int> { value: 10 };
                let same = identity<Box<int>>(box);
                let map = fn(value: Box<int>) -> Box<int> => value;
                return 0;
            }
            """);
        var diagnostics = new DiagnosticBag();
        new TypeResolutionPass(diagnostics).Resolve(program);
        CompilerTestHelpers.AssertNoErrors(diagnostics);

        var rewritten = GenericTypeRewriter.Rewrite(program, new HashSet<string>(StringComparer.Ordinal) { "Box_int" });
        var body = rewritten.Functions.Single(function => function.Name == "main").Body;
        var cast = Assert.IsType<CastExpressionNode>(Assert.IsType<LetStatement>(body[0]).Initializer);
        var sizeOf = Assert.IsType<SizeOfExpressionNode>(Assert.IsType<LetStatement>(body[1]).Initializer);
        var initializer = Assert.IsType<InitializerExpressionNode>(Assert.IsType<LetStatement>(body[2]).Initializer);
        var genericCall = Assert.IsType<GenericCallExpressionNode>(Assert.IsType<LetStatement>(body[3]).Initializer);
        var functionExpression = Assert.IsType<FunctionExpressionNode>(Assert.IsType<LetStatement>(body[4]).Initializer);

        Assert.Equal("Box_int*", cast.TargetTypeNode?.ToSourceText());
        Assert.Equal("Box_int", Assert.IsType<SizeOfTypeOperandNode>(sizeOf.Operand).TypeNode.ToSourceText());
        Assert.Equal("Box_int", initializer.TypeNameNode?.ToSourceText());
        Assert.Equal(["Box_int"], genericCall.TypeArgumentNodes.Select(node => node.ToSourceText()).ToList());
        Assert.Equal("Box_int", functionExpression.ReturnTypeNode?.ToSourceText());
        Assert.Equal("Box_int", Assert.Single(functionExpression.Parameters).TypeNode.ToSourceText());
    }

    [Fact]
    public void GenericTypeRewriter_DoesNotShareSemanticInfoWithOriginalNodes()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct Box<T> {
                value: T;
            }

            fn use_box(value: Box<int>) -> Box<int> {
                return value;
            }
            """);
        var original = Assert.Single(program.Functions);
        original.Semantic.ModuleName = "app.main";

        var rewritten = GenericTypeRewriter.Rewrite(program, new HashSet<string>(StringComparer.Ordinal) { "Box_int" });
        var rewrittenFunction = Assert.Single(rewritten.Functions);

        Assert.Equal("app.main", rewrittenFunction.Semantic.ModuleName);
        Assert.NotSame(original.Semantic, rewrittenFunction.Semantic);
        var returnTypeNode = Assert.IsType<TypeNode>(rewrittenFunction.ReturnTypeNode);
        Assert.Equal("Box_int", returnTypeNode.ToSourceText());
        Assert.IsType<NamedTypeSyntaxNode>(returnTypeNode.Syntax);
        Assert.Null(returnTypeNode.Semantic.Type);

        rewrittenFunction.Semantic.ModuleName = "rewritten";
        Assert.Equal("app.main", original.Semantic.ModuleName);
    }

    [Fact]
    public void GenericCallRetargeter_RepointsResolvedCallsToSpecializedFunction()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn identity<T>(value: T) -> T {
                return value;
            }

            fn main() -> int {
                return identity<int>(10);
            }
            """);
        var model = CompilerTestHelpers.Resolve(program);
        var catalog = Assert.IsType<FunctionCatalog>(model.FunctionCatalog);
        var generic = program.Functions.Single(function => function.Name == "identity");
        var specialized = GenericFunctionSpecializer.Specialize(generic, [Type("int")]);
        var specializations = new Dictionary<FunctionInstanceKey, FunctionNode>
        {
            [FunctionInstanceKey.Create(generic, [Type("int")])] = specialized,
        };

        GenericCallRetargeter.Retarget(program, specializations);

        var main = program.Functions.Single(function => function.Name == "main");
        var ret = Assert.IsType<ReturnStatement>(Assert.Single(main.Body));
        var call = Assert.IsType<GenericCallExpressionNode>(ret.Expression);
        Assert.Same(specialized, call.Semantic.ResolvedCall?.Function);
        Assert.Same(specialized.Semantic.Symbol, call.Semantic.Symbol);
    }

    [Fact]
    public void GenericStructSpecializer_CreatesConcreteStructFromTypeUsage()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct Box<T> {
                value: T;
            }

            struct Holder {
                box: Box<int>;
            }
            """);

        var structs = GenericStructSpecializer.Specialize(program, []);

        var box = Assert.Single(structs);
        var field = Assert.Single(box.Fields);
        Assert.Equal("Box_int", box.Name);
        Assert.Equal("int", field.TypeNode?.ToSourceText());
    }

    [Fact]
    public void GenericStructSpecializer_RewritesTypeNodesAndSemanticTypeRefs()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct Box<T> {
                value: T;
                next: Box<T>*;
            }

            struct Holder {
                box: Box<int>;
            }
            """);
        var diagnostics = new DiagnosticBag();
        new TypeResolutionPass(diagnostics).Resolve(program);
        CompilerTestHelpers.AssertNoErrors(diagnostics);

        var structs = GenericStructSpecializer.Specialize(program, []);
        var box = Assert.Single(structs);
        var value = box.Fields.Single(field => field.Name == "value");
        var next = box.Fields.Single(field => field.Name == "next");

        Assert.Equal("int", value.TypeNode?.ToSourceText());
        Assert.Equal("int", TypeRefFormatter.ToCxString(value.TypeNode!.Semantic.Type!));
        Assert.Equal("Box<int>*", next.TypeNode?.ToSourceText());
        Assert.Equal("Box<int>*", TypeRefFormatter.ToCxString(next.TypeNode!.Semantic.Type!));
    }

    [Fact]
    public void GenericStructSpecializer_FindsTypesInNestedLocalBindings()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct Box<T> {
                value: T;
            }

            fn main() -> void {
                if (false) {
                } else if (true) {
                    let value: Box<int>;
                }
            }
            """);

        var structs = GenericStructSpecializer.Specialize(program, []);

        Assert.Equal(
            "Box_int",
            Assert.Single(structs).Name);
    }

    private static IReadOnlyList<string> TypeArguments(GenericFunctionUse use) =>
        use.TypeArgumentRefs.Select(TypeRefFormatter.ToCxString).ToList();

    private static TypeRef Type(string name) => new TypeRef.Named(name, []);
}
