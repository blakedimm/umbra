namespace Umbra.Core;

/// <summary>
/// Represents all lexical token types in the Umbra programming language.
/// </summary>
public enum TokenType
{
    // Single-character tokens
    LeftParen,      // (
    RightParen,     // )
    LeftBrace,      // {
    RightBrace,     // }
    LeftBracket,    // [
    RightBracket,   // ]
    Comma,          // ,
    Dot,            // .
    Semicolon,      // ;
    Plus,           // +
    Minus,          // -
    Star,           // *
    Slash,          // /
    Question,       // ?
    Colon,          // :

    // One or two character tokens
    Bang,           // !
    BangEqual,      // !=
    Equal,          // =
    EqualEqual,     // ==
    Less,           // <
    LessEqual,      // <=
    Greater,        // >
    GreaterEqual,   // >=
    PipeGreater,    // |>

    // Literals
    Identifier,
    String,
    Number,

    // Keywords
    Let,            // let
    Mut,            // mut
    Fn,             // fn
    Return,         // return
    If,             // if
    Else,           // else
    While,          // while
    For,            // for
    In,             // in
    Match,          // match
    True,           // true
    False,          // false
    Nil,            // nil
    And,            // and
    Or,             // or
    Not,            // not
    Print,          // print

    // Special tokens
    Error,
    Eof
}
