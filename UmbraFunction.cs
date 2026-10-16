namespace Umbra.Core;

/// <summary>
/// First-class runtime representation of a compiled Umbra function.
/// </summary>
public sealed class UmbraFunction
{
    public string Name { get; }
    public int Arity { get; set; }
    public Chunk Chunk { get; } = new();

    public UmbraFunction(string name, int arity = 0)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Arity = arity;
    }

    public override string ToString() =>
        string.IsNullOrEmpty(Name) ? "<script>" : $"<fn {Name}>";
}
