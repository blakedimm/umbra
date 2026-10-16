using System.Globalization;

namespace Umbra.Core;

/// <summary>
/// Represents a compiled sequence of bytecode instructions, a constant pool, and line number metadata.
/// </summary>
public sealed class Chunk
{
    /// <summary>
    /// Bytecode instruction stream (opcodes and inline operands).
    /// </summary>
    public List<byte> Code { get; } = [];

    /// <summary>
    /// Constant pool storing literal values referenced by <see cref="OpCode.OpConstant"/> and global variable names.
    /// </summary>
    public List<object?> Constants { get; } = [];

    /// <summary>
    /// Source line numbers corresponding 1-to-1 with each byte in <see cref="Code"/>.
    /// </summary>
    public List<int> Lines { get; } = [];

    /// <summary>
    /// Appends a raw byte (opcode or operand) and its source line number to the chunk.
    /// </summary>
    public void Write(byte byteValue, int line)
    {
        Code.Add(byteValue);
        Lines.Add(line);
    }

    /// <summary>
    /// Appends an <see cref="OpCode"/> and its source line number to the chunk.
    /// </summary>
    public void Write(OpCode opCode, int line) => Write((byte)opCode, line);

    /// <summary>
    /// Adds a literal value to the constant pool and returns its index.
    /// </summary>
    public int AddConstant(object? value)
    {
        Constants.Add(value);
        return Constants.Count - 1;
    }

    /// <summary>
    /// Formats an Umbra runtime value into its canonical string representation.
    /// </summary>
    public static string FormatValue(object? value) =>
        value switch
        {
            null => "nil",
            bool b => b ? "true" : "false",
            double d => NormalizeZero(d).ToString("0.###############", CultureInfo.InvariantCulture),
            string s => s,
            List<object?> list => FormatList(list),
            _ => value.ToString() ?? string.Empty
        };

    private static string FormatList(List<object?> list)
    {
        if (list.Count == 0)
        {
            return "[]";
        }

        var parts = new string[list.Count];
        for (var i = 0; i < list.Count; i++)
        {
            parts[i] = FormatListElement(list[i]);
        }

        return $"[{string.Join(", ", parts)}]";
    }

    private static string FormatListElement(object? element) =>
        element switch
        {
            string s => $"\"{s}\"",
            _ => FormatValue(element)
        };

    /// <summary>
    /// Disassembles the entire chunk to the console for debugging and inspection.
    /// </summary>
    public void Disassemble(string name)
    {
        Console.WriteLine($"== {name} ==");
        for (var offset = 0; offset < Code.Count;)
        {
            offset = DisassembleInstruction(offset);
        }
    }

    /// <summary>
    /// Disassembles a single instruction at the given byte offset and returns the next instruction's offset.
    /// </summary>
    public int DisassembleInstruction(int offset)
    {
        Console.Write($"{offset:D4} ");
        if (offset > 0 && Lines[offset] == Lines[offset - 1])
        {
            Console.Write("   | ");
        }
        else
        {
            Console.Write($"{Lines[offset],4} ");
        }

        var instruction = (OpCode)Code[offset];
        return instruction switch
        {
            OpCode.OpConstant => ConstantInstruction(nameof(OpCode.OpConstant), offset),
            OpCode.OpNil => SimpleInstruction(nameof(OpCode.OpNil), offset),
            OpCode.OpTrue => SimpleInstruction(nameof(OpCode.OpTrue), offset),
            OpCode.OpFalse => SimpleInstruction(nameof(OpCode.OpFalse), offset),
            OpCode.OpAdd => SimpleInstruction(nameof(OpCode.OpAdd), offset),
            OpCode.OpSubtract => SimpleInstruction(nameof(OpCode.OpSubtract), offset),
            OpCode.OpMultiply => SimpleInstruction(nameof(OpCode.OpMultiply), offset),
            OpCode.OpDivide => SimpleInstruction(nameof(OpCode.OpDivide), offset),
            OpCode.OpNegate => SimpleInstruction(nameof(OpCode.OpNegate), offset),
            OpCode.OpEqual => SimpleInstruction(nameof(OpCode.OpEqual), offset),
            OpCode.OpGreater => SimpleInstruction(nameof(OpCode.OpGreater), offset),
            OpCode.OpLess => SimpleInstruction(nameof(OpCode.OpLess), offset),
            OpCode.OpNot => SimpleInstruction(nameof(OpCode.OpNot), offset),
            OpCode.OpPrint => SimpleInstruction(nameof(OpCode.OpPrint), offset),
            OpCode.OpPop => SimpleInstruction(nameof(OpCode.OpPop), offset),
            OpCode.OpDefineGlobal => ConstantInstruction(nameof(OpCode.OpDefineGlobal), offset),
            OpCode.OpGetGlobal => ConstantInstruction(nameof(OpCode.OpGetGlobal), offset),
            OpCode.OpSetGlobal => ConstantInstruction(nameof(OpCode.OpSetGlobal), offset),
            OpCode.OpGetLocal => ByteInstruction(nameof(OpCode.OpGetLocal), offset),
            OpCode.OpSetLocal => ByteInstruction(nameof(OpCode.OpSetLocal), offset),
            OpCode.OpJump => JumpInstruction(nameof(OpCode.OpJump), 1, offset),
            OpCode.OpJumpIfFalse => JumpInstruction(nameof(OpCode.OpJumpIfFalse), 1, offset),
            OpCode.OpLoop => JumpInstruction(nameof(OpCode.OpLoop), -1, offset),
            OpCode.OpCall => ByteInstruction(nameof(OpCode.OpCall), offset),
            OpCode.OpBuildList => ShortInstruction(nameof(OpCode.OpBuildList), offset),
            OpCode.OpGetIndex => SimpleInstruction(nameof(OpCode.OpGetIndex), offset),
            OpCode.OpSetIndex => SimpleInstruction(nameof(OpCode.OpSetIndex), offset),
            OpCode.OpReturn => SimpleInstruction(nameof(OpCode.OpReturn), offset),
            _ => UnknownInstruction((byte)instruction, offset)
        };
    }

    private static double NormalizeZero(double value) => value == 0.0 ? 0.0 : value;

    private static int SimpleInstruction(string name, int offset)
    {
        Console.WriteLine(name);
        return offset + 1;
    }

    private int ConstantInstruction(string name, int offset)
    {
        var constantIndex = Code[offset + 1];
        var value = Constants[constantIndex];
        Console.WriteLine($"{name,-16} {constantIndex,4} '{FormatValue(value)}'");
        return offset + 2;
    }

    private int ByteInstruction(string name, int offset)
    {
        var slot = Code[offset + 1];
        Console.WriteLine($"{name,-16} {slot,4}");
        return offset + 2;
    }

    private int ShortInstruction(string name, int offset)
    {
        var value = (ushort)((Code[offset + 1] << 8) | Code[offset + 2]);
        Console.WriteLine($"{name,-16} {value,4}");
        return offset + 3;
    }

    private int JumpInstruction(string name, int sign, int offset)
    {
        var jump = (ushort)((Code[offset + 1] << 8) | Code[offset + 2]);
        Console.WriteLine($"{name,-16} {offset,4} -> {offset + 3 + sign * jump}");
        return offset + 3;
    }

    private static int UnknownInstruction(byte opcode, int offset)
    {
        Console.WriteLine($"Unknown opcode {opcode}");
        return offset + 1;
    }
}
