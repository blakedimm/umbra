namespace Umbra.Core;

/// <summary>
/// Result status returned by the Umbra Virtual Machine after interpreting source code or bytecode.
/// </summary>
public enum InterpretResult
{
    Ok,
    CompileError,
    RuntimeError
}

/// <summary>
/// Stack-based virtual machine that executes compiled <see cref="UmbraFunction"/>s,
/// <see cref="UmbraNativeFunction"/>s, and <see cref="Chunk"/>s.
/// </summary>
public sealed class VM
{
    private const int FramesMax = 64;
    private const int StackMax = FramesMax * (byte.MaxValue + 1);

    private sealed class CallFrame
    {
        public UmbraFunction Function { get; set; } = null!;
        public int Ip { get; set; }
        public int SlotOffset { get; set; }
    }

    private sealed class NativeRuntimeException(string message) : Exception(message);

    private readonly CallFrame[] _frames = new CallFrame[FramesMax];
    private readonly object?[] _stack = new object?[StackMax];
    private readonly Dictionary<string, object?> _globals = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _mutableGlobals = new(StringComparer.Ordinal);
    private int _frameCount;
    private int _stackTop;

    public VM()
    {
        for (var i = 0; i < FramesMax; i++)
        {
            _frames[i] = new CallFrame();
        }

        RegisterStandardLibrary();
    }

    /// <summary>
    /// Enables step-by-step disassembly and stack inspection during execution.
    /// </summary>
    public bool TraceExecution { get; set; }

    /// <summary>
    /// Registers a host .NET function as an immutable global native function in the VM.
    /// </summary>
    public void DefineNative(string name, int arity, NativeFn function)
    {
        var native = new UmbraNativeFunction(name, arity, function);
        _globals[name] = native;
        _mutableGlobals[name] = false;
    }

    /// <summary>
    /// Compiles and executes the given Umbra source code.
    /// </summary>
    public InterpretResult Interpret(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();

        var pendingMutableGlobals = new Dictionary<string, bool>(_mutableGlobals, StringComparer.Ordinal);
        var compiler = new Compiler(tokens, pendingMutableGlobals);
        var scriptFunction = compiler.CompileFunction();

        if (scriptFunction is null)
        {
            return InterpretResult.CompileError;
        }

        foreach (var (name, isMutable) in pendingMutableGlobals)
        {
            _mutableGlobals[name] = isMutable;
        }

        return Interpret(scriptFunction);
    }

    /// <summary>
    /// Executes a compiled top-level <see cref="UmbraFunction"/> on the virtual machine.
    /// </summary>
    public InterpretResult Interpret(UmbraFunction scriptFunction)
    {
        ArgumentNullException.ThrowIfNull(scriptFunction);

        ResetStack();
        Push(scriptFunction);
        Call(scriptFunction, 0);

        return Run();
    }

    /// <summary>
    /// Executes a pre-compiled <see cref="Chunk"/> on the virtual machine by wrapping it in a script function.
    /// </summary>
    public InterpretResult Interpret(Chunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        var scriptFunction = new UmbraFunction(string.Empty, 0);
        scriptFunction.Chunk.Code.AddRange(chunk.Code);
        scriptFunction.Chunk.Constants.AddRange(chunk.Constants);
        scriptFunction.Chunk.Lines.AddRange(chunk.Lines);

        return Interpret(scriptFunction);
    }

    private void RegisterStandardLibrary()
    {
        DefineNative("len", 1, static (_, args) => args[0] switch
        {
            List<object?> list => (double)list.Count,
            string s => (double)s.Length,
            _ => throw new NativeRuntimeException("Argument to 'len' must be a list or a string.")
        });

        DefineNative("clock", 0, static (_, _) =>
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0);

        DefineNative("push", 2, static (_, args) =>
        {
            if (args[0] is not List<object?> list)
            {
                throw new NativeRuntimeException("First argument to 'push' must be a list.");
            }

            list.Add(args[1]);
            return list;
        });

        DefineNative("pop", 1, static (_, args) =>
        {
            if (args[0] is not List<object?> list)
            {
                throw new NativeRuntimeException("Argument to 'pop' must be a list.");
            }

            if (list.Count == 0)
            {
                throw new NativeRuntimeException("Cannot pop from an empty list.");
            }

            var lastIndex = list.Count - 1;
            var item = list[lastIndex];
            list.RemoveAt(lastIndex);
            return item;
        });

        DefineNative("str", 1, static (_, args) => Chunk.FormatValue(args[0]));

        DefineNative("type", 1, static (_, args) => args[0] switch
        {
            null => "nil",
            bool => "bool",
            double => "number",
            string => "string",
            List<object?> => "list",
            UmbraFunction => "function",
            UmbraNativeFunction => "native",
            _ => "unknown"
        });
    }

    private InterpretResult Run()
    {
        var frame = _frames[_frameCount - 1];

        while (true)
        {
            if (TraceExecution)
            {
                TraceStack();
                frame.Function.Chunk.DisassembleInstruction(frame.Ip);
            }

            var instruction = (OpCode)ReadByte(frame);
            switch (instruction)
            {
                case OpCode.OpConstant:
                {
                    var constant = ReadConstant(frame);
                    Push(constant);
                    break;
                }

                case OpCode.OpNil:
                    Push(null);
                    break;

                case OpCode.OpTrue:
                    Push(true);
                    break;

                case OpCode.OpFalse:
                    Push(false);
                    break;

                case OpCode.OpPop:
                    Pop();
                    break;

                case OpCode.OpGetLocal:
                {
                    var slot = ReadByte(frame);
                    Push(_stack[frame.SlotOffset + slot]);
                    break;
                }

                case OpCode.OpSetLocal:
                {
                    var slot = ReadByte(frame);
                    _stack[frame.SlotOffset + slot] = Peek(0);
                    break;
                }

                case OpCode.OpDefineGlobal:
                {
                    var name = ReadString(frame);
                    _globals[name] = Peek(0);
                    _mutableGlobals.TryAdd(name, true);
                    Pop();
                    break;
                }

                case OpCode.OpGetGlobal:
                {
                    var name = ReadString(frame);
                    if (!_globals.TryGetValue(name, out var value))
                    {
                        RuntimeError($"Undefined variable '{name}'.");
                        return InterpretResult.RuntimeError;
                    }
                    Push(value);
                    break;
                }

                case OpCode.OpSetGlobal:
                {
                    var name = ReadString(frame);
                    if (!_globals.ContainsKey(name))
                    {
                        RuntimeError($"Undefined variable '{name}'.");
                        return InterpretResult.RuntimeError;
                    }
                    if (_mutableGlobals.TryGetValue(name, out var isMutable) && !isMutable)
                    {
                        RuntimeError("Cannot reassign immutable variable declared with 'let'. Use 'mut' instead.");
                        return InterpretResult.RuntimeError;
                    }
                    _globals[name] = Peek(0);
                    break;
                }

                case OpCode.OpEqual:
                {
                    var b = Pop();
                    var a = Pop();
                    Push(AreEqual(a, b));
                    break;
                }

                case OpCode.OpGreater:
                {
                    var b = Pop();
                    var a = Pop();
                    if (a is not double da || b is not double db)
                    {
                        RuntimeError("Operands for '>' must be numbers.");
                        return InterpretResult.RuntimeError;
                    }
                    Push(da > db);
                    break;
                }

                case OpCode.OpLess:
                {
                    var b = Pop();
                    var a = Pop();
                    if (a is not double da || b is not double db)
                    {
                        RuntimeError("Operands for '<' must be numbers.");
                        return InterpretResult.RuntimeError;
                    }
                    Push(da < db);
                    break;
                }

                case OpCode.OpAdd:
                {
                    var b = Pop();
                    var a = Pop();
                    if (a is double da && b is double db)
                    {
                        Push(da + db);
                    }
                    else if (a is string sa && b is string sb)
                    {
                        Push(sa + sb);
                    }
                    else
                    {
                        RuntimeError("Operands for '+' must be two numbers or two strings.");
                        return InterpretResult.RuntimeError;
                    }
                    break;
                }

                case OpCode.OpSubtract:
                {
                    var b = Pop();
                    var a = Pop();
                    if (a is not double da || b is not double db)
                    {
                        RuntimeError("Operands for '-' must be numbers.");
                        return InterpretResult.RuntimeError;
                    }
                    Push(da - db);
                    break;
                }

                case OpCode.OpMultiply:
                {
                    var b = Pop();
                    var a = Pop();
                    if (a is not double da || b is not double db)
                    {
                        RuntimeError("Operands for '*' must be numbers.");
                        return InterpretResult.RuntimeError;
                    }
                    Push(da * db);
                    break;
                }

                case OpCode.OpDivide:
                {
                    var b = Pop();
                    var a = Pop();
                    if (a is not double da || b is not double db)
                    {
                        RuntimeError("Operands for '/' must be numbers.");
                        return InterpretResult.RuntimeError;
                    }
                    if (db == 0.0)
                    {
                        RuntimeError("Division by zero.");
                        return InterpretResult.RuntimeError;
                    }
                    Push(da / db);
                    break;
                }

                case OpCode.OpNot:
                    Push(IsFalsey(Pop()));
                    break;

                case OpCode.OpNegate:
                {
                    if (Peek(0) is not double d)
                    {
                        RuntimeError("Operand for unary '-' must be a number.");
                        return InterpretResult.RuntimeError;
                    }
                    Pop();
                    Push(-d);
                    break;
                }

                case OpCode.OpPrint:
                {
                    var value = Pop();
                    Console.WriteLine(Chunk.FormatValue(value));
                    break;
                }

                case OpCode.OpJump:
                {
                    var offset = ReadShort(frame);
                    frame.Ip += offset;
                    break;
                }

                case OpCode.OpJumpIfFalse:
                {
                    var offset = ReadShort(frame);
                    if (IsFalsey(Peek(0)))
                    {
                        frame.Ip += offset;
                    }
                    break;
                }

                case OpCode.OpLoop:
                {
                    var offset = ReadShort(frame);
                    frame.Ip -= offset;
                    break;
                }

                case OpCode.OpCall:
                {
                    var argCount = ReadByte(frame);

                    // Support pipeline operator (`arg |> fn`) where `arg` is pushed before `fn`
                    if (argCount == 1 && !IsCallable(Peek(1)) && IsCallable(Peek(0)))
                    {
                        (_stack[_stackTop - 2], _stack[_stackTop - 1]) = (_stack[_stackTop - 1], _stack[_stackTop - 2]);
                    }

                    if (!CallValue(Peek(argCount), argCount))
                    {
                        return InterpretResult.RuntimeError;
                    }

                    frame = _frames[_frameCount - 1];
                    break;
                }

                case OpCode.OpBuildList:
                {
                    var count = ReadShort(frame);
                    var list = new List<object?>(count);
                    var startIndex = _stackTop - count;
                    for (var i = 0; i < count; i++)
                    {
                        list.Add(_stack[startIndex + i]);
                    }

                    ClearStackFrom(startIndex);
                    Push(list);
                    break;
                }

                case OpCode.OpGetIndex:
                {
                    var indexObj = Pop();
                    var target = Pop();

                    if (indexObj is not double rawDouble || (int)rawDouble != rawDouble)
                    {
                        RuntimeError("Index must be an integer.");
                        return InterpretResult.RuntimeError;
                    }

                    var rawIndex = (int)rawDouble;
                    if (target is List<object?> list)
                    {
                        var length = list.Count;
                        var resolvedIndex = rawIndex < 0 ? length + rawIndex : rawIndex;
                        if (resolvedIndex < 0 || resolvedIndex >= length)
                        {
                            RuntimeError($"Index out of bounds (index {rawIndex}, length {length}).");
                            return InterpretResult.RuntimeError;
                        }

                        Push(list[resolvedIndex]);
                    }
                    else if (target is string str)
                    {
                        var length = str.Length;
                        var resolvedIndex = rawIndex < 0 ? length + rawIndex : rawIndex;
                        if (resolvedIndex < 0 || resolvedIndex >= length)
                        {
                            RuntimeError($"Index out of bounds (index {rawIndex}, length {length}).");
                            return InterpretResult.RuntimeError;
                        }

                        Push(str[resolvedIndex].ToString());
                    }
                    else
                    {
                        RuntimeError("Only lists and strings can be indexed.");
                        return InterpretResult.RuntimeError;
                    }

                    break;
                }

                case OpCode.OpSetIndex:
                {
                    var value = Pop();
                    var indexObj = Pop();
                    var target = Pop();

                    if (target is not List<object?> list)
                    {
                        RuntimeError("Only lists support index assignment.");
                        return InterpretResult.RuntimeError;
                    }

                    if (indexObj is not double rawDouble || (int)rawDouble != rawDouble)
                    {
                        RuntimeError("Index must be an integer.");
                        return InterpretResult.RuntimeError;
                    }

                    var rawIndex = (int)rawDouble;
                    var length = list.Count;
                    var resolvedIndex = rawIndex < 0 ? length + rawIndex : rawIndex;
                    if (resolvedIndex < 0 || resolvedIndex >= length)
                    {
                        RuntimeError($"Index out of bounds (index {rawIndex}, length {length}).");
                        return InterpretResult.RuntimeError;
                    }

                    list[resolvedIndex] = value;
                    Push(value);
                    break;
                }

                case OpCode.OpReturn:
                {
                    var result = _stackTop > frame.SlotOffset ? Pop() : null;
                    _frameCount--;

                    if (_frameCount == 0)
                    {
                        ResetStack();
                        return InterpretResult.Ok;
                    }

                    ClearStackFrom(frame.SlotOffset);
                    Push(result);
                    frame = _frames[_frameCount - 1];
                    break;
                }

                default:
                    RuntimeError($"Unknown opcode {(byte)instruction}.");
                    return InterpretResult.RuntimeError;
            }
        }
    }

    private static bool IsCallable(object? value) =>
        value is UmbraFunction or UmbraNativeFunction;

    private bool CallValue(object? callee, int argCount)
    {
        switch (callee)
        {
            case UmbraFunction function:
                return Call(function, argCount);

            case UmbraNativeFunction native:
            {
                if (argCount != native.Arity)
                {
                    RuntimeError($"Expected {native.Arity} arguments but got {argCount}.");
                    return false;
                }

                var args = argCount == 0 ? [] : new object?[argCount];
                var argStart = _stackTop - argCount;
                for (var i = 0; i < argCount; i++)
                {
                    args[i] = _stack[argStart + i];
                }

                try
                {
                    var result = native.Function(this, args);
                    ClearStackFrom(argStart - 1);
                    Push(result);
                    return true;
                }
                catch (NativeRuntimeException ex)
                {
                    RuntimeError(ex.Message);
                    return false;
                }
            }

            default:
                RuntimeError("Can only call functions.");
                return false;
        }
    }

    private bool Call(UmbraFunction function, int argCount)
    {
        if (argCount != function.Arity)
        {
            RuntimeError($"Expected {function.Arity} arguments but got {argCount}.");
            return false;
        }

        if (_frameCount == FramesMax)
        {
            RuntimeError("Stack overflow.");
            return false;
        }

        var frame = _frames[_frameCount++];
        frame.Function = function;
        frame.Ip = 0;
        frame.SlotOffset = _stackTop - argCount - 1;
        return true;
    }

    private static byte ReadByte(CallFrame frame) => frame.Function.Chunk.Code[frame.Ip++];

    private static ushort ReadShort(CallFrame frame)
    {
        var chunk = frame.Function.Chunk;
        frame.Ip += 2;
        return (ushort)((chunk.Code[frame.Ip - 2] << 8) | chunk.Code[frame.Ip - 1]);
    }

    private static object? ReadConstant(CallFrame frame) =>
        frame.Function.Chunk.Constants[ReadByte(frame)];

    private static string ReadString(CallFrame frame) =>
        ReadConstant(frame) as string ?? throw new InvalidOperationException("Expected string constant for variable name.");

    private void Push(object? value)
    {
        if (_stackTop >= StackMax)
        {
            throw new InvalidOperationException("Umbra VM stack overflow.");
        }

        _stack[_stackTop++] = value;
    }

    private object? Pop()
    {
        if (_stackTop <= 0)
        {
            throw new InvalidOperationException("Umbra VM stack underflow.");
        }

        var value = _stack[--_stackTop];
        _stack[_stackTop] = null;
        return value;
    }

    private object? Peek(int distance) => _stack[_stackTop - 1 - distance];

    private void ClearStackFrom(int newStackTop)
    {
        if (_stackTop > newStackTop)
        {
            Array.Clear(_stack, newStackTop, _stackTop - newStackTop);
            _stackTop = newStackTop;
        }
    }

    private void ResetStack()
    {
        if (_stackTop > 0)
        {
            Array.Clear(_stack, 0, _stackTop);
            _stackTop = 0;
        }

        _frameCount = 0;
    }

    /// <summary>
    /// In Umbra, only <c>nil</c> and <c>false</c> are falsey; all other values are truthy.
    /// </summary>
    private static bool IsFalsey(object? value) => value is null or false;

    private static bool AreEqual(object? a, object? b) =>
        (a, b) switch
        {
            (null, null) => true,
            (null, _) or (_, null) => false,
            (double da, double db) => da == db,
            (bool ba, bool bb) => ba == bb,
            (string sa, string sb) => string.Equals(sa, sb, StringComparison.Ordinal),
            _ => Equals(a, b)
        };

    private void RuntimeError(string message)
    {
        var line = 1;
        if (_frameCount > 0)
        {
            var frame = _frames[_frameCount - 1];
            var lines = frame.Function.Chunk.Lines;
            if (lines.Count > 0)
            {
                var instructionIndex = Math.Clamp(frame.Ip - 1, 0, lines.Count - 1);
                line = lines[instructionIndex];
            }
        }

        Console.Error.WriteLine($"[Line {line}] RuntimeError: {message}");
        ResetStack();
    }

    private void TraceStack()
    {
        Console.Write("          ");
        for (var i = 0; i < _stackTop; i++)
        {
            Console.Write($"[ {Chunk.FormatValue(_stack[i])} ]");
        }
        Console.WriteLine();
    }
}
