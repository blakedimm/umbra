namespace Umbra.Core;

/// <summary>
/// Single-pass Pratt parser and bytecode compiler for the Umbra language.
/// Supports first-class functions (<c>fn</c>), recursion, <c>return</c> statements,
/// the pipeline operator (<c>|&gt;</c>), lists (<c>[...]</c>), indexing (<c>list[i]</c>),
/// <c>for (item in collection)</c> loops, immutable/mutable variables (<c>let</c>/<c>mut</c>),
/// lexical scopes, <c>if/else</c> branching, and <c>while</c> loops.
/// </summary>
public sealed class Compiler
{
    private const int MaxLocals = byte.MaxValue + 1;

    private enum Precedence
    {
        None,
        Assignment, // =
        Pipe,       // |>
        Or,         // or
        And,        // and
        Equality,   // == !=
        Comparison, // < > <= >=
        Term,       // + -
        Factor,     // * /
        Unary,      // not ! -
        Call,       // . () []
        Primary
    }

    private enum FunctionType
    {
        Script,
        Function
    }

    private readonly record struct ParseRule(
        Action<bool>? Prefix,
        Action<bool>? Infix,
        Precedence Precedence);

    private record struct Local(Token Name, int Depth, bool IsMutable);

    private sealed class FunctionContext
    {
        public FunctionContext? Enclosing { get; }
        public UmbraFunction Function { get; }
        public FunctionType Type { get; }
        public List<Local> Locals { get; } = [];
        public int ScopeDepth { get; set; }

        public FunctionContext(FunctionContext? enclosing, FunctionType type, string name)
        {
            Enclosing = enclosing;
            Type = type;
            Function = new UmbraFunction(name);

            // Slot 0 is reserved for the function being called (or empty for the top-level script)
            var slotZeroName = type == FunctionType.Function ? name : string.Empty;
            Locals.Add(new Local(new Token(TokenType.Identifier, slotZeroName, null, 0), 0, IsMutable: false));
        }
    }

    private readonly IReadOnlyList<Token> _tokens;
    private readonly IDictionary<string, bool> _mutableGlobals;
    private readonly Dictionary<TokenType, ParseRule> _rules;
    private FunctionContext _context = null!;
    private int _currentIndex;
    private Token _current = null!;
    private Token _previous = null!;
    private bool _hadError;
    private bool _panicMode;

    private Chunk CurrentChunk => _context.Function.Chunk;

    public Compiler(IReadOnlyList<Token> tokens, IDictionary<string, bool>? mutableGlobals = null)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _mutableGlobals = mutableGlobals ?? new Dictionary<string, bool>(StringComparer.Ordinal);
        _rules = BuildRules();
    }

    /// <summary>
    /// Lexes and compiles raw Umbra source code into a top-level script <see cref="UmbraFunction"/>.
    /// Returns <see langword="null"/> if any lexical or syntax error occurs.
    /// </summary>
    public static UmbraFunction? CompileSourceFunction(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var compiler = new Compiler(tokens);
        return compiler.CompileFunction();
    }

    /// <summary>
    /// Lexes and compiles raw Umbra source code into a <see cref="Chunk"/>.
    /// Returns <see langword="null"/> if any lexical or syntax error occurs.
    /// </summary>
    public static Chunk? CompileSource(string source) =>
        CompileSourceFunction(source)?.Chunk;

    /// <summary>
    /// Compiles the token stream into a top-level script <see cref="UmbraFunction"/>.
    /// Returns <see langword="null"/> if compilation fails due to syntax errors.
    /// </summary>
    public UmbraFunction? CompileFunction()
    {
        _context = new FunctionContext(enclosing: null, FunctionType.Script, string.Empty);
        _currentIndex = 0;
        _hadError = false;
        _panicMode = false;

        Advance();

        while (!Match(TokenType.Eof))
        {
            Declaration();
        }

        var function = EndFunctionContext();
        return _hadError ? null : function;
    }

    /// <summary>
    /// Compiles the token stream into a new <see cref="Chunk"/>.
    /// Returns <see langword="null"/> if compilation fails due to syntax errors.
    /// </summary>
    public Chunk? Compile() => CompileFunction()?.Chunk;

    /// <summary>
    /// Compiles the token stream and populates the provided <paramref name="chunk"/>.
    /// Returns <see langword="true"/> if compilation succeeded without errors.
    /// </summary>
    public bool Compile(Chunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        var function = CompileFunction();
        if (function is null)
        {
            return false;
        }

        chunk.Code.Clear();
        chunk.Code.AddRange(function.Chunk.Code);
        chunk.Constants.Clear();
        chunk.Constants.AddRange(function.Chunk.Constants);
        chunk.Lines.Clear();
        chunk.Lines.AddRange(function.Chunk.Lines);
        return true;
    }

    private UmbraFunction EndFunctionContext()
    {
        EmitReturn();
        var function = _context.Function;
        if (_context.Enclosing is not null)
        {
            _context = _context.Enclosing;
        }
        return function;
    }

    private void Declaration()
    {
        if (Match(TokenType.Fn))
        {
            FnDeclaration();
        }
        else if (Match(TokenType.Let))
        {
            VarDeclaration(isMutable: false);
        }
        else if (Match(TokenType.Mut))
        {
            VarDeclaration(isMutable: true);
        }
        else
        {
            Statement();
        }

        if (_panicMode)
        {
            Synchronize();
        }
    }

    private void FnDeclaration()
    {
        var global = ParseVariable("Expect function name.", isMutable: false);
        var declLine = _previous.Line;
        var functionName = _previous.Lexeme;

        MarkInitialized();
        Function(FunctionType.Function, functionName);
        DefineVariable(global, isMutable: false, declLine);
    }

    private void Function(FunctionType type, string functionName)
    {
        _context = new FunctionContext(_context, type, functionName);
        BeginScope();

        Consume(TokenType.LeftParen, "Expect '(' after function name.");
        if (!Check(TokenType.RightParen))
        {
            do
            {
                _context.Function.Arity++;
                if (_context.Function.Arity > 255)
                {
                    ErrorAtCurrent("Can't have more than 255 parameters.");
                }

                var constant = ParseVariable("Expect parameter name.", isMutable: false);
                DefineVariable(constant, isMutable: false, _previous.Line);
            }
            while (Match(TokenType.Comma));
        }

        Consume(TokenType.RightParen, "Expect ')' after parameters.");
        Consume(TokenType.LeftBrace, "Expect '{' before function body.");

        Block();

        var compiledFunction = EndFunctionContext();
        EmitConstant(compiledFunction, _previous.Line);
    }

    private void VarDeclaration(bool isMutable)
    {
        var global = ParseVariable("Expect variable name.", isMutable);
        var declLine = _previous.Line;

        if (Match(TokenType.Equal))
        {
            Expression();
        }
        else
        {
            EmitByte(OpCode.OpNil, declLine);
        }

        Consume(TokenType.Semicolon, "Expect ';' after variable declaration.");
        DefineVariable(global, isMutable, declLine);
    }

    private byte ParseVariable(string errorMessage, bool isMutable)
    {
        Consume(TokenType.Identifier, errorMessage);

        DeclareVariable(isMutable);
        if (_context.ScopeDepth > 0)
        {
            return 0;
        }

        return IdentifierConstant(_previous);
    }

    private byte IdentifierConstant(Token name) => MakeConstant(name.Lexeme);

    private void DeclareVariable(bool isMutable)
    {
        if (_context.ScopeDepth == 0)
        {
            return;
        }

        var name = _previous;
        var locals = _context.Locals;
        for (var i = locals.Count - 1; i >= 0; i--)
        {
            var local = locals[i];
            if (local.Depth != -1 && local.Depth < _context.ScopeDepth)
            {
                break;
            }

            if (IdentifiersEqual(name, local.Name))
            {
                Error("Already a variable with this name in this scope.");
            }
        }

        AddLocal(name, isMutable);
    }

    private void AddLocal(Token name, bool isMutable)
    {
        if (_context.Locals.Count >= MaxLocals)
        {
            Error("Too many local variables in function.");
            return;
        }

        _context.Locals.Add(new Local(name, -1, isMutable));
    }

    private byte AddInitializedLocal(Token name, bool isMutable)
    {
        if (_context.Locals.Count >= MaxLocals)
        {
            Error("Too many local variables in function.");
            return 0;
        }

        var slot = (byte)_context.Locals.Count;
        _context.Locals.Add(new Local(name, _context.ScopeDepth, isMutable));
        return slot;
    }

    private byte AddSyntheticLocal(string syntheticName, bool isMutable, int line) =>
        AddInitializedLocal(new Token(TokenType.Identifier, syntheticName, null, line), isMutable);

    private void DefineVariable(byte global, bool isMutable, int line)
    {
        if (_context.ScopeDepth > 0)
        {
            MarkInitialized();
            return;
        }

        if (CurrentChunk.Constants[global] is string globalName)
        {
            _mutableGlobals[globalName] = isMutable;
        }

        EmitBytes(OpCode.OpDefineGlobal, global, line);
    }

    private void MarkInitialized()
    {
        if (_context.ScopeDepth == 0)
        {
            return;
        }

        var lastIndex = _context.Locals.Count - 1;
        _context.Locals[lastIndex] = _context.Locals[lastIndex] with { Depth = _context.ScopeDepth };
    }

    private void Statement()
    {
        if (Match(TokenType.Print))
        {
            PrintStatement();
        }
        else if (Match(TokenType.Return))
        {
            ReturnStatement();
        }
        else if (Match(TokenType.If))
        {
            IfStatement();
        }
        else if (Match(TokenType.While))
        {
            WhileStatement();
        }
        else if (Match(TokenType.For))
        {
            ForStatement();
        }
        else if (Match(TokenType.LeftBrace))
        {
            BeginScope();
            Block();
            EndScope();
        }
        else
        {
            ExpressionStatement();
        }
    }

    private void PrintStatement()
    {
        var printLine = _previous.Line;
        Expression();
        Consume(TokenType.Semicolon, "Expect ';' after value.");
        EmitByte(OpCode.OpPrint, printLine);
    }

    private void ReturnStatement()
    {
        if (_context.Type == FunctionType.Script)
        {
            Error("Can't return from top-level code.");
        }

        var returnLine = _previous.Line;
        if (Match(TokenType.Semicolon))
        {
            EmitReturn();
        }
        else
        {
            Expression();
            Consume(TokenType.Semicolon, "Expect ';' after return value.");
            EmitByte(OpCode.OpReturn, returnLine);
        }
    }

    private void IfStatement()
    {
        Consume(TokenType.LeftParen, "Expect '(' after 'if'.");
        Expression();
        Consume(TokenType.RightParen, "Expect ')' after 'if' condition.");

        var thenJump = EmitJump(OpCode.OpJumpIfFalse);
        EmitByte(OpCode.OpPop, _previous.Line);
        Statement();

        var elseJump = EmitJump(OpCode.OpJump);
        PatchJump(thenJump);
        EmitByte(OpCode.OpPop, _previous.Line);

        if (Match(TokenType.Else))
        {
            Statement();
        }

        PatchJump(elseJump);
    }

    private void WhileStatement()
    {
        var loopStart = CurrentChunk.Code.Count;

        Consume(TokenType.LeftParen, "Expect '(' after 'while'.");
        Expression();
        Consume(TokenType.RightParen, "Expect ')' after condition.");

        var exitJump = EmitJump(OpCode.OpJumpIfFalse);
        EmitByte(OpCode.OpPop, _previous.Line);
        Statement();
        EmitLoop(loopStart);

        PatchJump(exitJump);
        EmitByte(OpCode.OpPop, _previous.Line);
    }

    /// <summary>
    /// Desugars <c>for (item in collection) { ... }</c> into hidden stack-allocated locals
    /// (<c>collection</c>, <c>_i = 0</c>, <c>item</c>) and an indexed loop up to <c>len(collection)</c>.
    /// </summary>
    private void ForStatement()
    {
        BeginScope();

        Consume(TokenType.LeftParen, "Expect '(' after 'for'.");
        Consume(TokenType.Identifier, "Expect loop variable name.");
        var itemToken = _previous;
        var line = itemToken.Line;

        Consume(TokenType.In, "Expect 'in' after loop variable.");

        // 1. Evaluate collection once and store in a hidden local slot
        Expression();
        var collectionSlot = AddSyntheticLocal(" (for_collection)", isMutable: false, line);

        // 2. Initialize hidden loop index `mut _i = 0`
        EmitConstant(0.0, line);
        var indexSlot = AddSyntheticLocal(" (for_index)", isMutable: true, line);

        // 3. Declare user loop variable `let item = nil`
        EmitByte(OpCode.OpNil, line);
        var itemSlot = AddInitializedLocal(itemToken, isMutable: false);

        Consume(TokenType.RightParen, "Expect ')' after 'for' clause.");

        // 4. Loop condition: _i < len(collection)
        var loopStart = CurrentChunk.Code.Count;
        EmitBytes(OpCode.OpGetLocal, indexSlot, line);
        EmitBytes(OpCode.OpGetGlobal, MakeConstant("len"), line);
        EmitBytes(OpCode.OpGetLocal, collectionSlot, line);
        EmitBytes(OpCode.OpCall, 1, line);
        EmitByte(OpCode.OpLess, line);

        var exitJump = EmitJump(OpCode.OpJumpIfFalse);
        EmitByte(OpCode.OpPop, line);

        // 5. Bind current element: item = collection[_i]
        EmitBytes(OpCode.OpGetLocal, collectionSlot, line);
        EmitBytes(OpCode.OpGetLocal, indexSlot, line);
        EmitByte(OpCode.OpGetIndex, line);
        EmitBytes(OpCode.OpSetLocal, itemSlot, line);
        EmitByte(OpCode.OpPop, line);

        // 6. Execute loop body
        Statement();

        // 7. Increment index: _i = _i + 1
        EmitBytes(OpCode.OpGetLocal, indexSlot, line);
        EmitConstant(1.0, line);
        EmitByte(OpCode.OpAdd, line);
        EmitBytes(OpCode.OpSetLocal, indexSlot, line);
        EmitByte(OpCode.OpPop, line);

        EmitLoop(loopStart);

        PatchJump(exitJump);
        EmitByte(OpCode.OpPop, line);

        EndScope();
    }

    private void Block()
    {
        while (!Check(TokenType.RightBrace) && !Check(TokenType.Eof))
        {
            Declaration();
        }

        Consume(TokenType.RightBrace, "Expect '}' after block.");
    }

    private void BeginScope() => _context.ScopeDepth++;

    private void EndScope()
    {
        _context.ScopeDepth--;
        var line = _previous?.Line ?? 1;
        var locals = _context.Locals;

        while (locals.Count > 0 && locals[^1].Depth > _context.ScopeDepth)
        {
            EmitByte(OpCode.OpPop, line);
            locals.RemoveAt(locals.Count - 1);
        }
    }

    private void ExpressionStatement()
    {
        Expression();
        Consume(TokenType.Semicolon, "Expect ';' after expression.");
        EmitByte(OpCode.OpPop, _previous.Line);
    }

    private void Expression()
    {
        ParsePrecedence(Precedence.Assignment);
    }

    private void Grouping(bool canAssign)
    {
        Expression();
        Consume(TokenType.RightParen, "Expect ')' after expression.");
    }

    private void ListLiteral(bool canAssign)
    {
        var bracketLine = _previous.Line;
        ushort elementCount = 0;

        if (!Check(TokenType.RightBracket))
        {
            do
            {
                if (Check(TokenType.RightBracket))
                {
                    break;
                }

                Expression();
                if (elementCount == ushort.MaxValue)
                {
                    Error("Can't have more than 65535 elements in a list literal.");
                }
                elementCount++;
            }
            while (Match(TokenType.Comma));
        }

        Consume(TokenType.RightBracket, "Expect ']' after list elements.");
        EmitByte(OpCode.OpBuildList, bracketLine);
        EmitByte((byte)((elementCount >> 8) & 0xFF), bracketLine);
        EmitByte((byte)(elementCount & 0xFF), bracketLine);
    }

    private void Subscript(bool canAssign)
    {
        var bracketLine = _previous.Line;
        Expression();
        Consume(TokenType.RightBracket, "Expect ']' after index.");

        if (canAssign && Match(TokenType.Equal))
        {
            Expression();
            EmitByte(OpCode.OpSetIndex, bracketLine);
        }
        else
        {
            EmitByte(OpCode.OpGetIndex, bracketLine);
        }
    }

    private void Call(bool canAssign)
    {
        var callLine = _previous.Line;
        var argCount = ArgumentList();
        EmitBytes(OpCode.OpCall, argCount, callLine);
    }

    private byte ArgumentList()
    {
        byte argCount = 0;
        if (!Check(TokenType.RightParen))
        {
            do
            {
                Expression();
                if (argCount == 255)
                {
                    Error("Can't have more than 255 arguments.");
                }
                argCount++;
            }
            while (Match(TokenType.Comma));
        }

        Consume(TokenType.RightParen, "Expect ')' after arguments.");
        return argCount;
    }

    private void Pipe(bool canAssign)
    {
        var pipeLine = _previous.Line;

        // Left-hand side argument is already on top of the stack.
        // Compile the right-hand side target function with higher precedence (left-associative).
        ParsePrecedence(Precedence.Pipe + 1);

        // Invoke the function with 1 piped argument.
        EmitBytes(OpCode.OpCall, 1, pipeLine);
    }

    private void Number(bool canAssign)
    {
        EmitConstant(_previous.Literal, _previous.Line);
    }

    private void String(bool canAssign)
    {
        EmitConstant(_previous.Literal, _previous.Line);
    }

    private void Literal(bool canAssign)
    {
        switch (_previous.Type)
        {
            case TokenType.False:
                EmitByte(OpCode.OpFalse, _previous.Line);
                break;
            case TokenType.Nil:
                EmitByte(OpCode.OpNil, _previous.Line);
                break;
            case TokenType.True:
                EmitByte(OpCode.OpTrue, _previous.Line);
                break;
        }
    }

    private void Variable(bool canAssign)
    {
        NamedVariable(_previous, canAssign);
    }

    private void NamedVariable(Token name, bool canAssign)
    {
        OpCode getOp;
        OpCode setOp;
        var arg = ResolveLocal(_context, name);

        if (arg != -1)
        {
            getOp = OpCode.OpGetLocal;
            setOp = OpCode.OpSetLocal;
        }
        else
        {
            arg = IdentifierConstant(name);
            getOp = OpCode.OpGetGlobal;
            setOp = OpCode.OpSetGlobal;
        }

        if (canAssign && Match(TokenType.Equal))
        {
            if (setOp == OpCode.OpSetLocal)
            {
                if (!_context.Locals[arg].IsMutable)
                {
                    Error("Cannot reassign immutable variable declared with 'let'. Use 'mut' instead.");
                }
            }
            else if (_mutableGlobals.TryGetValue(name.Lexeme, out var isMutable) && !isMutable)
            {
                Error("Cannot reassign immutable variable declared with 'let'. Use 'mut' instead.");
            }

            Expression();
            EmitBytes(setOp, (byte)arg, name.Line);
        }
        else
        {
            EmitBytes(getOp, (byte)arg, name.Line);
        }
    }

    private int ResolveLocal(FunctionContext context, Token name)
    {
        var locals = context.Locals;
        for (var i = locals.Count - 1; i >= 0; i--)
        {
            var local = locals[i];
            if (IdentifiersEqual(name, local.Name))
            {
                if (local.Depth == -1)
                {
                    Error("Can't read local variable in its own initializer.");
                }
                return i;
            }
        }

        return -1;
    }

    private static bool IdentifiersEqual(Token a, Token b) =>
        string.Equals(a.Lexeme, b.Lexeme, StringComparison.Ordinal);

    private void Unary(bool canAssign)
    {
        var operatorType = _previous.Type;
        var operatorLine = _previous.Line;

        // Compile the operand
        ParsePrecedence(Precedence.Unary);

        // Emit the operator instruction
        switch (operatorType)
        {
            case TokenType.Minus:
                EmitByte(OpCode.OpNegate, operatorLine);
                break;
            case TokenType.Not:
            case TokenType.Bang:
                EmitByte(OpCode.OpNot, operatorLine);
                break;
        }
    }

    private void Binary(bool canAssign)
    {
        var operatorType = _previous.Type;
        var operatorLine = _previous.Line;
        var rule = GetRule(operatorType);

        // Compile the right-hand operand with one higher precedence level (left-associative)
        ParsePrecedence(rule.Precedence + 1);

        switch (operatorType)
        {
            case TokenType.BangEqual:
                EmitBytes(OpCode.OpEqual, OpCode.OpNot, operatorLine);
                break;
            case TokenType.EqualEqual:
                EmitByte(OpCode.OpEqual, operatorLine);
                break;
            case TokenType.Greater:
                EmitByte(OpCode.OpGreater, operatorLine);
                break;
            case TokenType.GreaterEqual:
                EmitBytes(OpCode.OpLess, OpCode.OpNot, operatorLine);
                break;
            case TokenType.Less:
                EmitByte(OpCode.OpLess, operatorLine);
                break;
            case TokenType.LessEqual:
                EmitBytes(OpCode.OpGreater, OpCode.OpNot, operatorLine);
                break;
            case TokenType.Plus:
                EmitByte(OpCode.OpAdd, operatorLine);
                break;
            case TokenType.Minus:
                EmitByte(OpCode.OpSubtract, operatorLine);
                break;
            case TokenType.Star:
                EmitByte(OpCode.OpMultiply, operatorLine);
                break;
            case TokenType.Slash:
                EmitByte(OpCode.OpDivide, operatorLine);
                break;
        }
    }

    private void And(bool canAssign)
    {
        var endJump = EmitJump(OpCode.OpJumpIfFalse);
        EmitByte(OpCode.OpPop, _previous.Line);
        ParsePrecedence(Precedence.And);
        PatchJump(endJump);
    }

    private void Or(bool canAssign)
    {
        var elseJump = EmitJump(OpCode.OpJumpIfFalse);
        var endJump = EmitJump(OpCode.OpJump);

        PatchJump(elseJump);
        EmitByte(OpCode.OpPop, _previous.Line);
        ParsePrecedence(Precedence.Or);
        PatchJump(endJump);
    }

    private void ParsePrecedence(Precedence precedence)
    {
        Advance();
        var prefixRule = GetRule(_previous.Type).Prefix;
        if (prefixRule is null)
        {
            Error("Expect expression.");
            return;
        }

        var canAssign = precedence <= Precedence.Assignment;
        prefixRule(canAssign);

        while (precedence <= GetRule(_current.Type).Precedence)
        {
            Advance();
            var infixRule = GetRule(_previous.Type).Infix;
            infixRule?.Invoke(canAssign);
        }

        if (canAssign && Match(TokenType.Equal))
        {
            Error("Invalid assignment target.");
        }
    }

    private ParseRule GetRule(TokenType type) =>
        _rules.TryGetValue(type, out var rule)
            ? rule
            : new ParseRule(null, null, Precedence.None);

    private Dictionary<TokenType, ParseRule> BuildRules() => new()
    {
        [TokenType.LeftParen] = new(Grouping, Call, Precedence.Call),
        [TokenType.RightParen] = new(null, null, Precedence.None),
        [TokenType.LeftBrace] = new(null, null, Precedence.None),
        [TokenType.RightBrace] = new(null, null, Precedence.None),
        [TokenType.LeftBracket] = new(ListLiteral, Subscript, Precedence.Call),
        [TokenType.RightBracket] = new(null, null, Precedence.None),
        [TokenType.Comma] = new(null, null, Precedence.None),
        [TokenType.Dot] = new(null, null, Precedence.None),
        [TokenType.Semicolon] = new(null, null, Precedence.None),
        [TokenType.Question] = new(null, null, Precedence.None),
        [TokenType.Colon] = new(null, null, Precedence.None),

        [TokenType.PipeGreater] = new(null, Pipe, Precedence.Pipe),

        [TokenType.Minus] = new(Unary, Binary, Precedence.Term),
        [TokenType.Plus] = new(null, Binary, Precedence.Term),
        [TokenType.Slash] = new(null, Binary, Precedence.Factor),
        [TokenType.Star] = new(null, Binary, Precedence.Factor),

        [TokenType.Bang] = new(Unary, null, Precedence.None),
        [TokenType.Not] = new(Unary, null, Precedence.None),
        [TokenType.BangEqual] = new(null, Binary, Precedence.Equality),
        [TokenType.Equal] = new(null, null, Precedence.None),
        [TokenType.EqualEqual] = new(null, Binary, Precedence.Equality),
        [TokenType.Greater] = new(null, Binary, Precedence.Comparison),
        [TokenType.GreaterEqual] = new(null, Binary, Precedence.Comparison),
        [TokenType.Less] = new(null, Binary, Precedence.Comparison),
        [TokenType.LessEqual] = new(null, Binary, Precedence.Comparison),

        [TokenType.And] = new(null, And, Precedence.And),
        [TokenType.Or] = new(null, Or, Precedence.Or),

        [TokenType.Identifier] = new(Variable, null, Precedence.None),
        [TokenType.String] = new(String, null, Precedence.None),
        [TokenType.Number] = new(Number, null, Precedence.None),

        [TokenType.True] = new(Literal, null, Precedence.None),
        [TokenType.False] = new(Literal, null, Precedence.None),
        [TokenType.Nil] = new(Literal, null, Precedence.None),

        [TokenType.Print] = new(null, null, Precedence.None),
        [TokenType.Let] = new(null, null, Precedence.None),
        [TokenType.Mut] = new(null, null, Precedence.None),
        [TokenType.Fn] = new(null, null, Precedence.None),
        [TokenType.Return] = new(null, null, Precedence.None),
        [TokenType.If] = new(null, null, Precedence.None),
        [TokenType.Else] = new(null, null, Precedence.None),
        [TokenType.While] = new(null, null, Precedence.None),
        [TokenType.For] = new(null, null, Precedence.None),
        [TokenType.In] = new(null, null, Precedence.None),
        [TokenType.Eof] = new(null, null, Precedence.None)
    };

    private void Advance()
    {
        _previous = _current;

        while (true)
        {
            _current = _currentIndex < _tokens.Count
                ? _tokens[_currentIndex++]
                : new Token(TokenType.Eof, string.Empty, null, _previous?.Line ?? 1);

            if (_current.Type != TokenType.Error)
            {
                break;
            }

            ErrorAtCurrent(_current.Lexeme);
        }
    }

    private void Consume(TokenType type, string message)
    {
        if (_current.Type == type)
        {
            Advance();
            return;
        }

        ErrorAtCurrent(message);
    }

    private bool Match(TokenType type)
    {
        if (!Check(type))
        {
            return false;
        }

        Advance();
        return true;
    }

    private bool Check(TokenType type) => _current.Type == type;

    private void Synchronize()
    {
        _panicMode = false;

        while (_current.Type != TokenType.Eof)
        {
            if (_previous.Type == TokenType.Semicolon)
            {
                return;
            }

            switch (_current.Type)
            {
                case TokenType.Fn:
                case TokenType.Let:
                case TokenType.Mut:
                case TokenType.If:
                case TokenType.While:
                case TokenType.For:
                case TokenType.Match:
                case TokenType.Print:
                case TokenType.Return:
                    return;
            }

            Advance();
        }
    }

    private int EmitJump(OpCode instruction)
    {
        var line = _previous?.Line ?? 1;
        EmitByte(instruction, line);
        EmitByte(0xFF, line);
        EmitByte(0xFF, line);
        return CurrentChunk.Code.Count - 2;
    }

    private void PatchJump(int offset)
    {
        var jump = CurrentChunk.Code.Count - offset - 2;
        if (jump > ushort.MaxValue)
        {
            Error("Too much code to jump over.");
        }

        CurrentChunk.Code[offset] = (byte)((jump >> 8) & 0xFF);
        CurrentChunk.Code[offset + 1] = (byte)(jump & 0xFF);
    }

    private void EmitLoop(int loopStart)
    {
        var line = _previous?.Line ?? 1;
        EmitByte(OpCode.OpLoop, line);

        var offset = CurrentChunk.Code.Count - loopStart + 2;
        if (offset > ushort.MaxValue)
        {
            Error("Loop body too large.");
        }

        EmitByte((byte)((offset >> 8) & 0xFF), line);
        EmitByte((byte)(offset & 0xFF), line);
    }

    private void EmitByte(byte byteValue, int line) => CurrentChunk.Write(byteValue, line);

    private void EmitByte(OpCode opCode, int line) => CurrentChunk.Write(opCode, line);

    private void EmitBytes(OpCode opCode, byte operand, int line)
    {
        EmitByte(opCode, line);
        EmitByte(operand, line);
    }

    private void EmitBytes(OpCode opCode1, OpCode opCode2, int line)
    {
        EmitByte(opCode1, line);
        EmitByte(opCode2, line);
    }

    private void EmitReturn()
    {
        var line = _previous?.Line ?? 1;
        EmitByte(OpCode.OpNil, line);
        EmitByte(OpCode.OpReturn, line);
    }

    private void EmitConstant(object? value, int line) =>
        EmitBytes(OpCode.OpConstant, MakeConstant(value), line);

    private byte MakeConstant(object? value)
    {
        var constant = CurrentChunk.AddConstant(value);
        if (constant > byte.MaxValue)
        {
            Error("Too many constants in one chunk.");
            return 0;
        }

        return (byte)constant;
    }

    private void ErrorAtCurrent(string message) => ErrorAt(_current, message);

    private void Error(string message) => ErrorAt(_previous, message);

    private void ErrorAt(Token token, string message)
    {
        if (_panicMode)
        {
            return;
        }

        _panicMode = true;
        _hadError = true;

        var location = token.Type switch
        {
            TokenType.Eof => " at end",
            TokenType.Error => string.Empty,
            _ => $" at '{token.Lexeme}'"
        };

        Console.Error.WriteLine($"[Line {token.Line}] Error{location}: {message}");
    }
}
