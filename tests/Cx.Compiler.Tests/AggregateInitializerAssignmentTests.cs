namespace Cx.Compiler.Tests;

public sealed class AggregateInitializerAssignmentTests
{
    [Fact]
    public void Compile_ReassignsNamedPositionalAndNestedAggregates()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            struct Point {
                x: int;
                y: int;
            }

            struct Line {
                start: Point;
                end: Point;
            }

            fn main() -> int {
                let point = Point { x: 1, y: 2 };
                point = Point { x: 3, y: 4 };
                point = { 5, 6 };

                let line = Line {
                    start: Point { x: 0, y: 0 },
                    end: Point { x: 0, y: 0 }
                };
                line = Line {
                    start: { x: 7, y: 8 },
                    end: { x: 9, y: 10 }
                };

                return point.x + line.end.y;
            }
            """)
            .Succeeds()
            .OutputContains(
                "point = (Point){ .x = 3, .y = 4 };",
                "point = (Point){ 5, 6 };",
                "line = (Line){ .start = { .x = 7, .y = 8 }, .end = { .x = 9, .y = 10 } };")
            .OutputOmits("(point = Point){", "(line = Line){");
    }

    [Fact]
    public void Compile_ReassignmentOfUsingAggregatePreservesCleanupOrder()
    {
        var test = CompilerTestHelpers.VerifyCompilation(
            """
            struct Resource {
                value: int;

                fn dispose() -> void {
                    self.value = 0;
                }
            }

            fn main() -> int {
                using resource = Resource { value: 1 };
                resource = Resource { value: 2 };
                return resource.value;
            }
            """)
            .Succeeds()
            .OutputAppearsInOrder(
                "Resource __cx_using_replacement_",
                "= (Resource){ .value = 2 };",
                "Resource_dispose(&resource);",
                "resource = __cx_using_replacement_");

        Assert.Equal(
            2,
            test.Result.Output!.Split("Resource_dispose(&resource);", StringSplitOptions.None).Length - 1);
    }
}
