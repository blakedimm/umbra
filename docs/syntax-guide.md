# Umbra Language Syntax Guide (`v1.0.0`)

This document specifies the lexical grammar, data types, operators, control-flow constructs, functions, and collection semantics of the **Umbra** programming language (`.umb`).

---

## 1. Comments & Whitespace

Single-line comments begin with `//` and extend to the end of the line:

```umbra
// This is a single-line comment
let version = "1.0.0"; // Inline comment
```

Statements are terminated with a semicolon (`;`), while block constructs (`{ ... }`, `if`, `while`, `for`, `fn`) do not require a trailing semicolon.

---

## 2. Variables, Mutability & Lexical Scopes

Umbra enforces **strict mutability by default** at compile time.

### Immutable Bindings (`let`)
Variables declared with `let` cannot be reassigned after declaration. Attempting to reassign a `let` variable produces a compile-time error:

```umbra
let pi = 3.14159;
// pi = 3.14; // Compile Error: Cannot reassign immutable variable declared with 'let'. Use 'mut' instead.
```

### Mutable Bindings (`mut`)
Variables that need to be reassigned must be explicitly declared with `mut`:

```umbra
mut counter = 0;
counter = counter + 1;
```

### Default Initialization
If a declaration omits an initializer, the variable is initialized to `nil`:

```umbra
let uninitialized;
mut state;
print uninitialized; // nil
```

### Block Scopes & Variable Shadowing
Curly braces `{ ... }` introduce a new lexical scope. Local variables live directly in stack slots of the virtual machine and are automatically popped when leaving the block. Inner scopes may shadow variables from outer scopes:

```umbra
let val = 100;
{
    mut val = 50;
    val = val + 20;
    print val; // 70
}
print val; // 100
```

---

## 3. Data Types

Umbra is dynamically typed. Every runtime value belongs to one of seven fundamental types (inspectable via `type(val)`):

| Type Name (`type(x)`) | Description | Examples |
| :--- | :--- | :--- |
| `"nil"` | Absence of a value | `nil` |
| `"bool"` | Boolean truth value | `true`, `false` |
| `"number"` | 64-bit IEEE 754 floating-point number (`double`) | `42`, `3.14`, `-0.5` |
| `"string"` | Immutable UTF-16 text sequence | `"Hello, Umbra!"` |
| `"list"` | Dynamic, mutable ordered collection | `[1, 2, "three"]`, `[]` |
| `"function"` | First-class user-defined function | `fn add(a, b) { ... }` |
| `"native"` | Built-in host FFI function | `len`, `push`, `pop`, `clock` |

### Truthiness
Only `nil` and `false` are **falsey**. All other values (including `0`, `""`, and `[]`) are **truthy**.

---

## 4. Operators & Precedence

Operators are listed from **highest** precedence to **lowest** precedence:

| Precedence | Category | Operators | Associativity |
| :---: | :--- | :--- | :---: |
| 1 | Primary / Grouping | `(...)`, `[...]` (literal) | — |
| 2 | Call & Indexing | `fn(...)`, `list[i]` | Left |
| 3 | Unary | `-`, `not`, `!` | Right |
| 4 | Multiplicative (Factor) | `*`, `/` | Left |
| 5 | Additive (Term) | `+` (add / concat), `-` | Left |
| 6 | Comparison | `<`, `<=`, `>`, `>=` | Left |
| 7 | Equality | `==`, `!=` | Left |
| 8 | Logical AND | `and` | Left |
| 9 | Logical OR | `or` | Left |
| 10 | Pipeline | `\|>` | Left |
| 11 | Assignment | `=` | Right |

### The Pipeline Operator (`|>`)
The pipeline operator evaluates its left-hand operand and passes it as the argument to the callable on the right-hand side:

```umbra
// x |> f |> g is equivalent to g(f(x))
let result = 4 |> square |> increment;
```

---

## 5. Control Flow

### Conditional Branching (`if / else`)
```umbra
if (score >= 90) {
    print "Grade: A";
} else if (score >= 75) {
    print "Grade: B";
} else {
    print "Grade: C";
}
```

### `while` Loops
```umbra
mut i = 0;
while (i < 5) {
    print i;
    i = i + 1;
}
```

### `for .. in` Collection Loops
Iterates over the elements of a `list` or characters of a `string`. Under the hood, the compiler desugars `for (item in collection)` into hidden stack-allocated loop state:

```umbra
let items = ["Alpha", "Beta", "Gamma"];
for (item in items) {
    print item;
}
```

---

## 6. Functions (`fn`)

Functions are first-class values. Parameters are bound as immutable local variables (`let`) within the function scope.

```umbra
fn fib(n) {
    if (n <= 1) {
        return n;
    }
    return fib(n - 1) + fib(n - 2);
}

print fib(7); // 13
```

- If execution reaches the end of a function body without hitting a `return` statement, the function implicitly returns `nil`.
- An empty `return;` statement also returns `nil`.
- Passing the wrong number of arguments raises a runtime error:
  `[Line X] RuntimeError: Expected Y arguments but got Z.`

---

## 7. Lists & Indexing

Lists are dynamic, heap-allocated collections that can hold values of any type, including nested lists:

```umbra
mut matrix = [[1, 2], [3, 4]];
let firstRow = matrix[0];
print firstRow[1]; // 2
```

### Negative Indexing
Negative indices count backward from the end of the list or string (`-1` refers to the last element):

```umbra
let scores = [10, 20, 30];
print scores[-1]; // 30
print scores[-2]; // 20
```

### Index Assignment & Mutability
Elements of a list can be mutated in place via `list[index] = value;`. Note that `let` vs `mut` controls whether the variable binding itself can be reassigned to a different list, whereas `list[i] = v` mutates the list object in the heap:

```umbra
let numbers = [1, 2, 3];
numbers[0] = 99; // Allowed: mutates list contents
// numbers = [4, 5, 6]; // Compile Error: 'numbers' was declared with 'let'
```

### Bounds Checking
Accessing an index outside `[-length, length - 1]` triggers a runtime error:

```text
[Line X] RuntimeError: Index out of bounds (index 5, length 3).
```
