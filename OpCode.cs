namespace Umbra.Core;

/// <summary>
/// Bytecode instruction opcodes for the Umbra stack-based virtual machine.
/// </summary>
public enum OpCode : byte
{
    /// <summary>Pushes a constant from the chunk's constant pool onto the stack (1-byte operand: constant index).</summary>
    OpConstant,

    /// <summary>Pushes <c>nil</c> onto the stack.</summary>
    OpNil,

    /// <summary>Pushes <c>true</c> onto the stack.</summary>
    OpTrue,

    /// <summary>Pushes <c>false</c> onto the stack.</summary>
    OpFalse,

    /// <summary>Pops two operands, adds numbers or concatenates strings, and pushes the result.</summary>
    OpAdd,

    /// <summary>Pops two numeric operands, subtracts the right from the left, and pushes the result.</summary>
    OpSubtract,

    /// <summary>Pops two numeric operands, multiplies them, and pushes the result.</summary>
    OpMultiply,

    /// <summary>Pops two numeric operands, divides the left by the right, and pushes the result.</summary>
    OpDivide,

    /// <summary>Negates the top numeric value on the stack.</summary>
    OpNegate,

    /// <summary>Pops two operands, compares them for equality, and pushes a boolean result.</summary>
    OpEqual,

    /// <summary>Pops two numeric operands, checks <c>a &gt; b</c>, and pushes a boolean result.</summary>
    OpGreater,

    /// <summary>Pops two numeric operands, checks <c>a &lt; b</c>, and pushes a boolean result.</summary>
    OpLess,

    /// <summary>Applies logical NOT to the top value on the stack.</summary>
    OpNot,

    /// <summary>Pops the top value from the stack, formats it, and prints it to standard output.</summary>
    OpPrint,

    /// <summary>Discards the top value from the stack.</summary>
    OpPop,

    /// <summary>Defines a global variable with the value on top of the stack (1-byte operand: name constant index).</summary>
    OpDefineGlobal,

    /// <summary>Pushes the value of a global variable onto the stack (1-byte operand: name constant index).</summary>
    OpGetGlobal,

    /// <summary>Sets an existing global variable to the value on top of the stack (1-byte operand: name constant index).</summary>
    OpSetGlobal,

    /// <summary>Pushes the value of a local stack slot onto the stack (1-byte operand: stack slot index).</summary>
    OpGetLocal,

    /// <summary>Sets a local stack slot to the value on top of the stack (1-byte operand: stack slot index).</summary>
    OpSetLocal,

    /// <summary>Jumps forward by a 16-bit unsigned offset if the condition on top of the stack is falsey (2-byte operand).</summary>
    OpJumpIfFalse,

    /// <summary>Unconditionally jumps forward by a 16-bit unsigned offset (2-byte operand).</summary>
    OpJump,

    /// <summary>Unconditionally jumps backward by a 16-bit unsigned offset (2-byte operand).</summary>
    OpLoop,

    /// <summary>Invokes a callable function with the specified argument count (1-byte operand: argument count).</summary>
    OpCall,

    /// <summary>Pops N elements from the stack, constructs a <see cref="List{T}"/>, and pushes it (2-byte operand: count).</summary>
    OpBuildList,

    /// <summary>Pops an index and a target collection (list or string) and pushes the indexed element.</summary>
    OpGetIndex,

    /// <summary>Pops a value, an index, and a target list, updates the element at index, and pushes the value.</summary>
    OpSetIndex,

    /// <summary>Returns from the current function or script execution.</summary>
    OpReturn
}
