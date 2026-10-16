# Umbra Programming Language

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Build](https://img.shields.io/badge/build-passing-brightgreen.svg)](https://github.com/blakedimm/umbra)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](./LICENSE)
[![Author](https://img.shields.io/badge/author-blakedimm-181717?logo=github)](https://github.com/blakedimm)

**Umbra** (`.umb`) is a high-performance, dynamically-typed programming language and stack-based bytecode virtual machine built from scratch in modern C# (`.NET 10`) with zero external dependencies.

Created and maintained by **[blakedimm](https://github.com/blakedimm)**.

---

## Documentation

- [Language Syntax Guide](docs/syntax-guide.md) — Complete specification of variables, syntax, and control flow.
- [Standard Library Reference](docs/stdlib.md) — Built-in functions, signatures, and runtime behavior.
- [Architecture & VM Internals](docs/architecture.md) — Deep dive into bytecode, Pratt parser, and VM design.

---

## Architecture Overview

Umbra follows the classic single-pass compilation and bytecode execution pipeline inspired by `clox` and CPython:

```text
[Source .umb] ──► [Lexer] ──► [Pratt Compiler] ──► [Chunk (Bytecode + Constants)] ──► [Stack VM] ──► [Stdout]
```

### Core Subsystems (`Umbra.Core`)

- **Hand-Written Lexical Scanner (`Lexer.cs`)**: Zero-dependency tokenizer supporting numeric and string literals, single-line comments (`//`), two-character comparison operators, and the functional pipeline operator (`|>`).
- **Single-Pass Pratt Compiler (`Compiler.cs`)**: Top-down operator precedence parser that compiles expressions and statements directly into compact bytecode `Chunk`s with 16-bit jump backpatching (`OpJump`, `OpJumpIfFalse`, `OpLoop`) and compile-time `for .. in` desugaring.
- **Strict Mutability Enforcement (`let` vs `mut`)**: Variables declared with `let` are immutable constants; reassignments are rejected at compile time and protected at runtime. Mutable state requires explicit `mut` declarations.
- **Multi-Frame Stack Virtual Machine (`VM.cs`)**: Executes bytecode using a pre-allocated `CallFrame` stack (`FramesMax = 64`) and flat value stack with relative local slot indexing (`SlotOffset + slot`).
- **First-Class Functions & Pipeline Operator (`|>`)**: Functions (`UmbraFunction`) are first-class values supporting recursion, higher-order passing, and left-associative pipeline chaining (`x |> f |> g` $\equiv$ `g(f(x))`).
- **Dynamic Lists & Negative Indexing**: Heap-allocated `List<object?>` collections supporting nested literals (`[[1, 2], [3, 4]]`), Python-style negative indices (`scores[-1]`), in-place index mutation (`scores[1] = 99`), and strict bounds checking.
- **Native FFI & Standard Library (`UmbraNativeFunction.cs`)**: Built-in host bindings for `len(x)`, `push(list, item)`, `pop(list)`, `type(x)`, `str(x)`, and `clock()`.

---

## Code Examples

### 1. Language Tour & Bubble Sort (`demo.umb`)

```umbra
// 1. Lists and indexing
mut scores = [10, 20, 30];
scores[1] = 99;
print scores;                 // [10, 99, 30]
print scores[0] + scores[-1]; // 40

// 2. Built-in native functions & pipelines
print len(scores);            // 3
print scores |> len;          // 3

push(scores, 500);
let last = pop(scores);
print last;                   // 500

// 3. Iterating with for .. in
let fruits = ["Apple", "Pear", "Plum"];
for (f in fruits) {
    print "Fruit: " + f;
}

// 4. Bubble Sort in pure Umbra
mut arr = [64, 34, 25, 12, 22, 11, 90];
mut i = 0;
let n = len(arr);

while (i < n) {
    mut j = 0;
    while (j < n - i - 1) {
        if (arr[j] > arr[j + 1]) {
            let temp = arr[j];
            arr[j] = arr[j + 1];
            arr[j + 1] = temp;
        }
        j = j + 1;
    }
    i = i + 1;
}

print arr; // [11, 12, 22, 25, 34, 64, 90]
```

### 2. Data Pipeline Analyzer (`analyzer.umb`)

```umbra
fn filterPositive(items) {
    mut result = [];
    for (x in items) {
        if (x > 0) {
            push(result, x);
        }
    }
    return result;
}

fn mapSquares(items) {
    mut result = [];
    for (x in items) {
        push(result, x * x);
    }
    return result;
}

fn sumList(items) {
    mut total = 0;
    for (x in items) {
        total = total + x;
    }
    return total;
}

let dataset = [-5, 2, 0, 4, -1, 6, 8, -3, 10];
let total = dataset |> filterPositive |> mapSquares |> sumList;
print "Sum of squares: " + str(total); // 220
```

---

## Showcase Projects

### [Aether — Procedural Dungeon Generator & World Simulator](projects/aether/)

Located in [`projects/aether/aether.umb`](projects/aether/aether.umb), **Aether** is a complete procedural dungeon generator written in pure Umbra. It features a deterministic 32-bit LCG PRNG, 2D matrix generation, a 4-pass **Cellular Automata (Rule 4-5)** smoothing pipeline (`|>`), procedural entity placement (`@`, `>`, `$`, `M`), and execution profiling via `clock()`.

```bash
dotnet run -- projects/aether/aether.umb
```

```text
+----------------------------------------------------+
|####################################################|
|##@.#############..#######........###..#############|
|##...###########.M.....................$############|
|##....#########.........................####...#####|
|###..$M.M.......####..................########$...##|
|####..........$.####........$.........#######.....##|
|##########.>########################################|
|####################################################|
+----------------------------------------------------+
Legend: [#] Wall  [.] Floor  [@] Player  [$] Loot  [M] Mob  [>] Portal
```

---

## Building and Running

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### Build the Project
```bash
dotnet build -c Release
```

### Run an Umbra Script (`.umb`)
```bash
dotnet run -- demo.umb
dotnet run -- analyzer.umb
```

### Run Built-in Acceptance Test Suite
```bash
dotnet run -- --test
```

### Launch Interactive REPL
```bash
dotnet run -- --repl
```

---

## VS Code Syntax Highlighting Extension

The repository includes an official Visual Studio Code syntax highlighting extension in [`editors/vscode`](./editors/vscode).

### Install Locally

**Windows (PowerShell):**
```powershell
Copy-Item -Path .\editors\vscode -Destination "$HOME\.vscode\extensions\blakedimm.umbra-syntax-1.0.0" -Recurse -Force
```

**macOS / Linux:**
```bash
cp -r ./editors/vscode ~/.vscode/extensions/blakedimm.umbra-syntax-1.0.0
```

Reload VS Code (`Ctrl+Shift+P` → `Developer: Reload Window`) and open any `.umb` file to enable full syntax highlighting and bracket auto-closing.

---

## License

Released under the [MIT License](./LICENSE). Copyright (c) 2026 **blakedimm**.
