using System.Globalization;

namespace Umbra.Core;

/// <summary>
/// Lexical analyzer (scanner) that transforms raw Umbra source code into a sequence of <see cref="Token"/>s.
/// </summary>
public sealed class Lexer
{
    private static readonly Dictionary<string, TokenType> Keywords = new(StringComparer.Ordinal)
    {
        ["let"] = TokenType.Let,
        ["mut"] = TokenType.Mut,
        ["fn"] = TokenType.Fn,
        ["return"] = TokenType.Return,
        ["if"] = TokenType.If,
        ["else"] = TokenType.Else,
        ["while"] = TokenType.While,
        ["for"] = TokenType.For,
        ["in"] = TokenType.In,
        ["match"] = TokenType.Match,
        ["true"] = TokenType.True,
        ["false"] = TokenType.False,
        ["nil"] = TokenType.Nil,
        ["and"] = TokenType.And,
        ["or"] = TokenType.Or,
        ["not"] = TokenType.Not,
        ["print"] = TokenType.Print
    };

    private readonly string _source;
    private readonly List<Token> _tokens = [];
    private int _start;
    private int _current;
    private int _line = 1;

    public Lexer(string source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    /// <summary>
    /// Scans the entire source string and returns all tokens, ending with <see cref="TokenType.Eof"/>.
    /// </summary>
    public List<Token> ScanTokens()
    {
        _tokens.Clear();
        _start = 0;
        _current = 0;
        _line = 1;

        while (!IsAtEnd())
        {
            _start = _current;
            ScanToken();
        }

        _tokens.Add(new Token(TokenType.Eof, string.Empty, null, _line));
        return _tokens;
    }

    private void ScanToken()
    {
        var c = Advance();

        switch (c)
        {
            // Single-character tokens
            case '(': AddToken(TokenType.LeftParen); break;
            case ')': AddToken(TokenType.RightParen); break;
            case '{': AddToken(TokenType.LeftBrace); break;
            case '}': AddToken(TokenType.RightBrace); break;
            case '[': AddToken(TokenType.LeftBracket); break;
            case ']': AddToken(TokenType.RightBracket); break;
            case ',': AddToken(TokenType.Comma); break;
            case '.': AddToken(TokenType.Dot); break;
            case ';': AddToken(TokenType.Semicolon); break;
            case '+': AddToken(TokenType.Plus); break;
            case '-': AddToken(TokenType.Minus); break;
            case '*': AddToken(TokenType.Star); break;
            case '?': AddToken(TokenType.Question); break;
            case ':': AddToken(TokenType.Colon); break;

            // Two-character tokens
            case '!':
                AddToken(Match('=') ? TokenType.BangEqual : TokenType.Bang);
                break;
            case '=':
                AddToken(Match('=') ? TokenType.EqualEqual : TokenType.Equal);
                break;
            case '<':
                AddToken(Match('=') ? TokenType.LessEqual : TokenType.Less);
                break;
            case '>':
                AddToken(Match('=') ? TokenType.GreaterEqual : TokenType.Greater);
                break;
            case '|':
                if (Match('>'))
                {
                    AddToken(TokenType.PipeGreater);
                }
                else
                {
                    AddErrorToken($"Unexpected character '{c}'. Did you mean '|>'?");
                }
                break;

            // Slash or single-line comment
            case '/':
                if (Match('/'))
                {
                    while (Peek() != '\n' && !IsAtEnd())
                    {
                        Advance();
                    }
                }
                else
                {
                    AddToken(TokenType.Slash);
                }
                break;

            // Whitespace
            case ' ':
            case '\r':
            case '\t':
                break;

            case '\n':
                _line++;
                break;

            // String literals
            case '"':
                ScanString();
                break;

            default:
                if (IsDigit(c))
                {
                    ScanNumber();
                }
                else if (IsAlpha(c))
                {
                    ScanIdentifier();
                }
                else
                {
                    AddErrorToken($"Unexpected character '{c}'.");
                }
                break;
        }
    }

    private void ScanString()
    {
        var startLine = _line;

        while (Peek() != '"' && !IsAtEnd())
        {
            if (Peek() == '\n')
            {
                _line++;
            }
            Advance();
        }

        if (IsAtEnd())
        {
            _tokens.Add(new Token(TokenType.Error, "Unterminated string.", null, startLine));
            return;
        }

        // Consume closing quote '"'
        Advance();

        var value = _source.Substring(_start + 1, _current - _start - 2);
        AddToken(TokenType.String, value);
    }

    private void ScanNumber()
    {
        while (IsDigit(Peek()))
        {
            Advance();
        }

        // Fractional part
        if (Peek() == '.' && IsDigit(PeekNext()))
        {
            // Consume the '.'
            Advance();

            while (IsDigit(Peek()))
            {
                Advance();
            }
        }

        var lexeme = _source.Substring(_start, _current - _start);
        var value = double.Parse(lexeme, CultureInfo.InvariantCulture);
        AddToken(TokenType.Number, value);
    }

    private void ScanIdentifier()
    {
        while (IsAlphaNumeric(Peek()))
        {
            Advance();
        }

        var text = _source.Substring(_start, _current - _start);
        var type = Keywords.GetValueOrDefault(text, TokenType.Identifier);

        object? literal = type switch
        {
            TokenType.True => true,
            TokenType.False => false,
            _ => null
        };

        AddToken(type, literal);
    }

    private bool IsAtEnd() => _current >= _source.Length;

    private char Advance() => _source[_current++];

    private bool Match(char expected)
    {
        if (IsAtEnd() || _source[_current] != expected)
        {
            return false;
        }

        _current++;
        return true;
    }

    private char Peek() => IsAtEnd() ? '\0' : _source[_current];

    private char PeekNext() => _current + 1 >= _source.Length ? '\0' : _source[_current + 1];

    private static bool IsDigit(char c) => c is >= '0' and <= '9';

    private static bool IsAlpha(char c) => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or '_';

    private static bool IsAlphaNumeric(char c) => IsAlpha(c) || IsDigit(c);

    private void AddToken(TokenType type, object? literal = null)
    {
        var lexeme = _source.Substring(_start, _current - _start);
        _tokens.Add(new Token(type, lexeme, literal, _line));
    }

    private void AddErrorToken(string message)
    {
        _tokens.Add(new Token(TokenType.Error, message, null, _line));
    }
}
