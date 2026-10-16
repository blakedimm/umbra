namespace Umbra.Core;

/// <summary>
/// Delegate representing a host .NET method callable from Umbra code.
/// </summary>
public delegate object? NativeFn(VM vm, IReadOnlyList<object?> args);

/// <summary>
/// Runtime representation of a built-in / FFI native function exposed to Umbra.
/// </summary>
public sealed class UmbraNativeFunction
{
    public string Name { get; }
    public int Arity { get; }
    public NativeFn Function { get; }

    public UmbraNativeFunction(string name, int arity, NativeFn function)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Arity = arity;
        Function = function ?? throw new ArgumentNullException(nameof(function));
    }

    public override string ToString() => $"<native fn {Name}>";
}
