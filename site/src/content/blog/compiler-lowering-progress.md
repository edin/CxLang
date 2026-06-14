---
title: "Compiler Lowering Is Getting Cleaner"
description: "A short note on moving CX toward explicit AST lowering before C emission."
author: "Edin"
coAuthor: "Codex"
pubDate: 2026-06-14
tags: ["compiler", "lowering", "devlog"]
---

The compiler is moving toward a cleaner pipeline:

```text
CX AST -> typed semantic model -> lowered CX AST -> C AST -> C text
```

That direction matters because it keeps the emitter from becoming the place where
language features secretly live. A feature should be validated semantically, lowered
into simpler CX/C-shaped constructs, and then printed by a backend that mostly
walks the tree it was given.

## Lowering foreach, one case at a time

The recent work focused on `foreach`. Range loops now lower into explicit `for`
loops, iterator-based loops lower into an iterator local plus `while iterator.next()`,
and contiguous collections lower into cached data and length locals with an index
loop.

The approach is intentionally small: each lowering pass owns one AST shape and
returns a replacement tree. A pass does not need to scan source text or know how C
will eventually be printed.

For example, this CX:

```cx
fn sum(values: int[4]) -> int {
    let total: int = 0;
    foreach value: int in values {
        total += value;
    }
    return total;
}
```

is lowered into an explicit indexed shape before C emission:

```cx
fn sum(values: int[4]) -> int {
    let total: int = 0;
    let __cx_foreach_data_0: int* = values;
    let __cx_foreach_length_0: usize = 4;
    for (let __cx_foreach_index_0: usize = 0;
         __cx_foreach_index_0 < __cx_foreach_length_0;
         __cx_foreach_index_0 = __cx_foreach_index_0 + 1) {
        let value: int = __cx_foreach_data_0[__cx_foreach_index_0];
        total += value;
    }
    return total;
}
```

The C backend can then print ordinary C-like code:

```c
int sum(int values[4])
{
    int total = 0;
    int* __cx_foreach_data_0 = values;
    size_t __cx_foreach_length_0 = 4;
    for (size_t __cx_foreach_index_0 = 0;
         __cx_foreach_index_0 < __cx_foreach_length_0;
         __cx_foreach_index_0 = __cx_foreach_index_0 + 1) {
        int value = __cx_foreach_data_0[__cx_foreach_index_0];
        total += value;
    }
    return total;
}
```

At the implementation level, this is just a focused AST transform. The pass matches
one node type, checks whether that node has the shape it knows how to lower, and
returns either `Unchanged` or a replacement node. The surrounding rewrite pipeline
handles walking the rest of the tree and splicing the replacement back into the
program.

```csharp
internal sealed class RangeForeachTransform : IAstNodeTransform<ForeachStatement>
{
    public AstTransformResult Transform(
        ForeachStatement node,
        AstTransformContext context)
    {
        if (node.IterableExpression is not ScalarRangeExpressionNode range)
        {
            return AstTransformResult.Unchanged;
        }

        var endName = context.UniqueName("__cx_range_end");
        var loopValue = new ForDeclarationInitializerNode(
            node.ValueBinding.Location,
            IsConst: false,
            node.ValueBinding.Name,
            range.Start,
            node.ValueBinding.TypeNode);

        var condition = new BinaryExpressionNode(
            range.Location,
            $"{node.ValueBinding.Name} < {endName}",
            new NameExpressionNode(node.ValueBinding.Location, node.ValueBinding.Name),
            "<",
            new NameExpressionNode(range.End.Location, endName));

        return AstTransformResult.ReplaceStatement(
            new ForStatement(
                node.Location,
                loopValue,
                condition,
                Increment(node.ValueBinding.Location, node.ValueBinding.Name),
                node.Body,
                CachedRangeEndInitializer: new ForDeclarationInitializerNode(
                    range.End.Location,
                    IsConst: true,
                    endName,
                    range.End,
                    node.ValueBinding.TypeNode)));
    }
}
```

That gives CX a nicer internal shape:

- lowering passes are isolated and testable
- generated code paths become easier to reason about
- the C emitter can keep shrinking toward a printer
- future constructs can follow the same transform model

There is still plenty to do, but this is the kind of cleanup that makes the next
round of compiler work calmer instead of heavier.
