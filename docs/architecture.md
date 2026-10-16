# Umbra Architecture & VM Internals (`v1.0.0`)

Umbra is engineered around a clean, single-pass compilation pipeline targeting a custom stack-based bytecode virtual machine (`Umbra.Core`).

---

## 1. End-to-End Execution Pipeline

```text
[Source .umb]
      │
      ▼
┌─────────────┐
│  Lexer.cs   │  Scans raw characters into immutable Token records.
└─────────────┘
      │
      ▼
┌─────────────┐
│ Compiler.cs │  Single-pass Pratt Parser & recursive-descent statement compiler.
└─────────────┘
      │
      ▼
┌─────────────┐
│  Chunk.cs   │  Compact bytecode stream (Code), constant pool (Constants), and line map (Lines).
└─────────────┘
      │
      ▼
┌─────────────┐
│    VM.cs    │  Stack-based Virtual Machine with CallFrame isolation and Native FFI.
└─────────────┘
```

---

## 2. Lexical Analysis (`Lexer.cs`)

The `Lexer` scans the source text in a single $O(N)$ pass without regular-expression allocations:
- Tracks `_start`, `_current`, and 1-based `_line` numbers.
- Recognizes single-character and two-character operators (`==`, `!=`, `<=`, `>=`, `|>`).
- Parses numeric literals using `CultureInfo.InvariantCulture` into 64-bit `double` values.
- Emits synthetic `TokenType.Error` tokens for lexical errors (such as unterminated strings or invalid characters) so the compiler can report and synchronize cleanly.

---

## 3. Single-Pass Pratt Compiler (`Compiler.cs`)

Rather than constructing an intermediate Abstract Syntax Tree (AST) in memory, `Compiler.cs` translates tokens directly into bytecode in a single pass:

### Top-Down Operator Precedence (Pratt Parsing)
- Every `TokenType` maps to a `ParseRule(Prefix, Infix, Precedence)` entry.
- `ParsePrecedence(Precedence)` dispatches the prefix handler for the first token and iteratively invokes infix handlers while the lookahead token's precedence is greater than or equal to the current precedence level.

### Nested `FunctionContext` Hierarchy
- Compiling the top-level script creates a root `FunctionContext` (`FunctionType.Script`) whose target is an implicit `<script>` `UmbraFunction`.
- Encountering `fn name(...)` pushes a new `FunctionContext` (`FunctionType.Function`) linked via `Enclosing`, compiles the function body into its own dedicated `Chunk`, emits an implicit `OpNil` + `OpReturn`, and pops back to the enclosing context.

### Control-Flow Backpatching & `for .. in` Desugaring
- **Conditional & Unconditional Jumps**: `EmitJump()` writes a 1-byte jump opcode followed by a 2-byte `0xFFFF` placeholder and returns the operand offset. Once the target block is compiled, `PatchJump()` overwrites the placeholder with the exact 16-bit unsigned byte delta.
- **`for (item in collection)` Desugaring**: Automatically lowers into three hidden stack slots (`(for_collection)`, `(for_index)`, and `item`), evaluating the collection expression once and iterating from `0` to `len(collection) - 1`.

---

## 4. Bytecode Format (`OpCode.cs` & `Chunk.cs`)

Each `Chunk` encapsulates three parallel data structures:
1. `List<byte> Code` — tightly packed instruction stream.
2. `List<object?> Constants` — constant pool storing literal values, variable names, and compiled `UmbraFunction` objects.
3. `List<int> Lines` — 1-to-1 source line mapping for accurate runtime error reporting.

### Instruction Set Summary

| OpCode | Operand Size | Stack Effect | Description |
| :--- | :---: | :---: | :--- |
| `OpConstant` | 1 byte | $+1$ | Pushes `Constants[index]` onto the stack. |
| `OpNil`, `OpTrue`, `OpFalse` | 0 bytes | $+1$ | Pushes literal `nil`, `true`, or `false`. |
| `OpAdd`, `OpSubtract`, `OpMultiply`, `OpDivide` | 0 bytes | $-1$ | Pops 2 operands, computes result, pushes result. |
| `OpNegate`, `OpNot` | 0 bytes | $0$ | Unary arithmetic negation or logical NOT on top of stack. |
| `OpEqual`, `OpGreater`, `OpLess` | 0 bytes | $-1$ | Pops 2 operands, pushes boolean comparison result. |
| `OpPrint` | 0 bytes | $-1$ | Pops top value, formats via `Chunk.FormatValue`, prints to stdout. |
| `OpPop` | 0 bytes | $-1$ | Discards top value from the stack. |
| `OpDefineGlobal`, `OpGetGlobal`, `OpSetGlobal` | 1 byte | $-1 / +1 / 0$ | Defines, reads, or updates a global variable by name constant index. |
| `OpGetLocal`, `OpSetLocal` | 1 byte | $+1 / 0$ | Reads or updates `_stack[frame.SlotOffset + slot]`. |
| `OpJumpIfFalse`, `OpJump` | 2 bytes | $0$ | Forward 16-bit relative jump (`frame.Ip += offset`). |
| `OpLoop` | 2 bytes | $0$ | Backward 16-bit relative jump (`frame.Ip -= offset`). |
| `OpCall` | 1 byte | `-(argCount)` | Invokes an `UmbraFunction` or `UmbraNativeFunction`. |
| `OpBuildList` | 2 bytes | `-(count) + 1` | Pops `count` elements and pushes a new `List<object?>`. |
| `OpGetIndex` | 0 bytes | $-1$ | Pops index and collection, pushes `target[index]`. |
| `OpSetIndex` | 0 bytes | $-2$ | Pops value, index, and list, sets `list[index] = value`, pushes value. |
| `OpReturn` | 0 bytes | Frame pop | Pops return value, unwinds current `CallFrame`, pushes result to caller. |

---

## 5. Virtual Machine Internals (`VM.cs`)

### Flat Value Stack & Pre-Allocated `CallFrame`s
- **Value Stack (`_stack`)**: A contiguous `object?[]` array indexed by `_stackTop`. Pushing and popping values are simple pointer increments/decrements without linked-list or collection allocations.
- **Call Stack (`_frames`)**: Pre-allocated array of 64 `CallFrame` objects (`Function`, `Ip`, `SlotOffset`). Entering a user-defined function increments `_frameCount` and sets `SlotOffset = _stackTop - argCount - 1` without allocating a new `CallFrame` on the managed heap.

### Foreign Function Interface (`UmbraNativeFunction.cs`)
Host .NET methods are wrapped in `UmbraNativeFunction` delegates (`NativeFn(VM vm, IReadOnlyList<object?> args)`) and registered via `VM.DefineNative()`. When `OpCall` encounters an `UmbraNativeFunction`, it validates arity, slices the arguments from the top of `_stack`, invokes the delegate directly, unwinds the arguments, and pushes the returned value.
