# Methods, extensions, operators, and conversions

CX lets behavior live beside a type, in a separate qualified declaration, or
in an extension block. The same method model supports operator overloads and
target-owned implicit conversions without hiding either feature behind special
C output.

The complete example for this chapter is
[`examples/methods-extensions-operators-conversions.cx`](../../examples/methods-extensions-operators-conversions.cx).

## Instance and static methods

A method declared inside a struct receives an implicit `self` value. A static
method does not, so it is called through the type:

```cx
struct Counter {
    value: int;

    static fn create(value: int) -> Counter {
        return Counter { value: value };
    }

    fn add(amount: int) -> void {
        self.value = self.value + amount;
    }
}

let counter = Counter.create(10);
counter.add(5);
```

`self` is implicit in the parameter list but explicit in the body. Instance
methods therefore read like ordinary member calls while lowering to ordinary
C functions whose receiver is an argument.

## Qualified method declarations

A method can be declared away from the original type by qualifying its name:

```cx
fn Counter.take(amount: int) -> void {
    self.value = self.value - amount;
}

counter.take(3);
```

This is useful when the implementation should not interrupt the data
declaration. It still belongs to `Counter`; it is not a free function that
happens to take a counter.

## Extension blocks

Extensions add methods to an existing type without reopening its declaration:

```cx
extension Counter {
    fn reset() -> void {
        self.value = 0;
    }
}

counter.reset();
```

Extensions may also target generic types:

```cx
extension Vec<T> {
    fn first() -> T {
        return self.data[0];
    }
}
```

An extension can be conditional on a structural requirement:

```cx
extension Vec<T>
where T: Equal<T> {
    fn contains(value: T) -> bool {
        for (let i: usize = 0; i < self.length; i++) {
            if (self.data[i] == value) {
                return true;
            }
        }

        return false;
    }
}
```

Here `contains` is available only when `T` satisfies `Equal<T>`. The constraint
also gives the body permission to use `==` on values of `T`.

Extension lookup participates in normal overload resolution. Multiple methods
with the same source name are valid when their signatures distinguish them;
CX does not select a method merely by taking the first matching name.

## Operator methods

An overloadable operator is declared as a method with `operator` followed by
the symbol:

```cx
struct Score {
    value: int;

    fn operator +(other: Score) -> Score {
        return Score { value: self.value + other.value };
    }
}

let total = Score { value: 20 } + Score { value: 22 };
```

Both call forms name the same function:

```cx
let infix = left + right;
let explicit = left.operator +(right);
```

Operator methods can live on their type or in an extension. Like ordinary
instance methods, they receive an implicit `self: Self*` pointer. CX takes the
address of an addressable left operand automatically:

```cx
let total = left + right; // calls the operator with &left
```

When the left operand is a temporary, CX materializes it once before taking
its address. If the temporary provides `dispose() -> void`, the generated
binding participates in normal scoped cleanup. An identical intrinsic
operator signature on a primitive type cannot be replaced by an extension.

CX currently supports declarations for these binary operators:

```text
+  -  *  /  %  <=>  ==  !=  <  <=  >  >=
```

## Derived comparison operators

Defining the three-way comparison operator gives CX enough information to
derive all four ordering operators:

```cx
struct Score {
    value: int;

    fn operator <=>(other: Score) -> int {
        return self.value <=> other.value;
    }
}

let earlier = left < right;
let later_or_equal = left >= right;
```

Similarly, defining `==` allows CX to derive `!=`:

```cx
fn operator ==(other: Score) -> bool {
    return self.value == other.value;
}

let different = left != right;
```

Derivation avoids six nearly identical comparison bodies while leaving one
canonical semantic operation for equality and one for ordering.

## Operators in generic requirements

Requirements can state that a type provides an operator:

```cx
requires Add<T> {
    fn operator +(other: T) -> T;
}

fn sum<T>(left: T, right: T) -> T
where T: Add<T> {
    return left + right;
}
```

When `sum` is specialized, the constrained operator call is retargeted to the
concrete implementation for `T`. This keeps the generic body expressed in CX
operators rather than callbacks or manually supplied function pointers.

## Implicit conversion factories

A target type opts into a conversion with a `static implicit fn` that accepts
one value and returns the target type:

```cx
struct Text {
    data: const char*;

    static implicit fn from(value: const char*) -> Self {
        return Text { data: value };
    }
}
```

The function name is ordinary API design; `from` is a convention, not special
syntax. The semantic shape is what matters:

- it must be declared `static implicit fn`;
- it must have exactly one non-variadic parameter;
- its result must belong to the target type.

The conversion is inserted where an expected target type is known:

```cx
fn accept(value: Text) -> void {}

fn create() -> Text {
    return "created";       // return conversion
}

let value: Text = "first"; // typed initializer conversion
value = "second";          // assignment conversion
accept("argument");        // argument conversion
```

The generated C remains direct and readable. For example, the argument call
becomes conceptually:

```c
accept(Text_from("argument"));
```

## Predictable conversion boundaries

Implicit conversions are intentionally limited to one step. Given conversions
from `int` to `Intermediate` and from `Intermediate` to `Target`, CX will not
silently convert an `int` all the way to `Target`:

```cx
let value: Target = 42; // error: cannot assign 'int' to 'Target'
```

The intermediate construction must be written explicitly. This prevents a
small set of convenient factories from growing into surprising conversion
paths.

If the target declares two equally eligible factories for the same source
type, compilation fails with an ambiguous implicit conversion diagnostic that
names the candidates. CX never resolves that ambiguity by declaration order.

## Choosing the declaration form

Use an owned method when behavior is central to the type. Use a qualified
method when it belongs to the type but reads better outside its declaration.
Use an extension when adding behavior separately, especially when it is
generic or conditional. Use an operator only when the symbol has a clear
domain meaning, and use an implicit factory only for unsurprising, lossless or
deliberately conventional construction.

## Related chapters

- [Data types](02-data-types.md)
- [Variables and constants](03-variables-and-constants.md)
- [Expressions and operators](04-expressions-and-operators.md)
- [Constructors, initializers, and typed AST macros](../features/initializers-and-typed-macros.md)

Requirements and generic specialization will receive their full treatment in
the planned generics chapter. This chapter documents only the pieces needed to
understand constrained extensions and operator calls.
