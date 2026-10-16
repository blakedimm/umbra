namespace Umbra.Core;

/// <summary>
/// Immutable lexical token produced by the <see cref="Lexer"/>.
/// </summary>
/// <param name="Type">The classification of the token.</param>
/// <param name="Lexeme">The raw substring from the source code (or error message for <see cref="TokenType.Error"/>).</param>
/// <param name="Literal">The parsed literal value (e.g., <see cref="double"/> or <see cref="string"/>), if applicable.</param>
/// <param name="Line">The 1-based source line number where the token was encountered.</param>
public sealed record Token(TokenType Type, string Lexeme, object? Literal, int Line)
{
    public override string ToString() =>
        Literal is not null
            ? $"[{Line}] {Type,-14} '{Lexeme}' ({Literal})"
            : $"[{Line}] {Type,-14} '{Lexeme}'";
}
