using Cx.Compiler.Semantic;

namespace Cx.Compiler.Tests;

public sealed class SymbolUsageCollectorTests
{
    [Fact]
    public void Collect_FindsResolvedFreeFunctionCallsAndTypes()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn add(a: int, b: int) -> int {
                return a + b;
            }

            fn main() -> int {
                let value: int = add(20, 22);
                return value;
            }
            """);
        CompilerTestHelpers.Resolve(program);

        var report = new SymbolUsageCollector().Collect(program);

        Assert.Contains("main", report.FunctionDefinitions);
        Assert.Contains("add", report.FunctionDefinitions);
        Assert.Contains("add", report.FunctionCalls);
        Assert.Contains("int", report.TypeReferences);
        Assert.Contains("Local:value", report.Symbols);
        Assert.Contains("Function:add", report.Symbols);
    }

    [Fact]
    public void Collect_FindsResolvedMemberCalls()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct Point {
                x: int;

                fn get() -> int {
                    return self.x;
                }
            }

            fn main() -> int {
                let point: Point = Point { x: 10 };
                return point.get();
            }
            """);
        CompilerTestHelpers.Resolve(program);

        var report = new SymbolUsageCollector().Collect(program);

        Assert.Contains("Point.get", report.FunctionDefinitions);
        Assert.Contains("Point.get", report.FunctionCalls);
        Assert.Contains("Point", report.TypeReferences);
        Assert.Contains("Function:get", report.Symbols);
    }
}
