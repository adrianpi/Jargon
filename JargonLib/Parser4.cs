using System.Diagnostics;
using System.IO;

namespace Jargon
{
    public class Parser4 : StageParser
    {
        public Parser4(Module module, ICompilerErrorListener errorListener) : base(module, errorListener) { }

        protected override void OnEndParse()
        {
            var deinit = module.Find(Path.GetFileNameWithoutExtension(unit.FileName) + "_static_deinit") as Function;
            Debug.Assert(deinit != null);
            var releaseFn = FindOrImport("object__release");

            for (int i = unit.Symbols.Count - 1; i >= 0; --i)
            {
                var symbol = unit.Symbols[i];
                if (symbol is GlobalVariable gv && gv.DataType.IsPointer() && gv.DataType.ElementType.IsClass())
                {
                    var fc = new FunctionCallExpression(new SymbolExpression(releaseFn));
                    fc.Arguments.Add(new SymbolExpression(gv));
                    deinit.Body.Statements.Add(new ExpressionStatement(fc));
                }
            }
        }

        private void SetContext(Statement s, int line = -1)
        {
            s.file = scanner.GetFileName();
            s.line = line == -1 ? scanner.GetLine() : line;
        }

        protected override bool ParseUsing()
        {
            while (scanner.GetToken() != Token.EOF)
            {
                if (scanner.GetToken() == Token.Semicolon)
                    break;
                scanner.NextToken();
            }
            return Expect(Token.Semicolon);
        }
        protected override bool ParseEnum()
        {
            SkipBraces();
            return Expect(Token.RBrace);
        }
        protected override bool ParseStructure(bool isUnion)
        {
            SkipBraces();
            return Expect(Token.RBrace);
        }
        protected override bool ParseClass()
        {
            if (scanner.GetToken() != Token.Ident)
            {
                Error("Identifier expected");
                return false;
            }
            var name = scanner.GetTokenString();
            scanner.NextToken();
            ClassType cls = module.Find(name) as ClassType;
            Debug.Assert(cls != null);
            _class = cls;
            if (scanner.GetToken() == Token.Colon)
            {
                scanner.NextToken();
                Debug.Assert(scanner.GetToken() == Token.Ident);
                scanner.NextToken();
            }
            if (scanner.GetToken() == Token.LBrace)
            {
                scanner.NextToken();
                while (scanner.GetToken() != Token.RBrace && scanner.GetToken() != Token.EOF)
                {
                    bool _static = false;
                    bool _const = false;
                    if (IsKeyword("const"))
                    {
                        _const = true;
                    }
                    else
                    {
                        if (IsKeyword("virtual"))
                            ;
                        else if (IsKeyword("static"))
                            _static = true;

                        if (IsKeyword("weak"))
                            ;
                    }

                    Declaration decl = new Declaration();

                    if (!ParseDeclaration(ref decl, true, null, false, true))
                    {
                        _class = null;
                        return false;
                    }

                    if (decl.type is Function fn)
                    {
                        if (decl.getter)
                        {
                            fn.Name = "get_" + decl.name;
                        }
                        else if (decl.setter)
                        {
                            fn.Name = "set_" + decl.name;
                        }

                        if (_static
                            && fn.Parameters.Count > 0
                            && fn.Parameters[0].Name == "this")
                        {
                            // extension method
                            fn.Name = "ext_" + fn.Name;
                        }

                        fn.Name = cls.Name + "__" + fn.Name;

                        var existingFn = module.Find(fn.Name) as Function;
                        Debug.Assert(existingFn != null);
                        Debug.Assert(existingFn != fn);
                        fn = existingFn;
                        fn.fileName = fileName;
                        fn.declaringClass = cls;

                        if (scanner.GetToken() == Token.LBrace || scanner.GetToken() == Token.DoubleArrow)
                        {
                            scope = fn;
                            function = fn;
                            fn.Body = ParseStatementBlock();
                            if (fn.Body == null)
                            {
                                _class = null;
                                function = null;
                                return false;
                            }
                            scope = fn.Parent;
                            function = null;
                        }
                        else
                        {
                            Error("Syntax error");
                            return false;
                        }
                    }
                    else
                    {
                        if (_const)
                        {
                            while (scanner.GetToken() != Token.EOF)
                            {
                                if (scanner.GetToken() == Token.Semicolon)
                                    break;
                                scanner.NextToken();
                            }
                        }
                        else
                        {
                            FieldSymbol fs = cls.FindChild(decl.name) as FieldSymbol;
                            Debug.Assert(fs != null);

                            if (scanner.GetToken() == Token.Assign)
                            {
                                scanner.NextToken();
                                //fs.InitialValue = ParseExpression();
                                Expression init = ParseExpression();
                                //if (fs.InitialValue == null)
                                if (init == null)
                                    return false;
                                ForceCast(ref init, fs.DataType);
                                fs.InitialValue = init;
                            }                            
                        }
                        if (!Expect(Token.Semicolon))
                        {
                            _class = null;
                            return false;
                        }
                    }
                }
                _class = null;

                if (!AddStaticFieldsInitializations(cls))
                    return false;

                if (!UpdateInstanceInitMethod(cls))
                    return false;

                return Expect(Token.RBrace);
            }
            else
            {
                return Expect(Token.Semicolon);
            }
        }

        private bool AddStaticFieldsInitializations(ClassType cls)
        {
            var sinitBody = (module.FindChild(Path.GetFileNameWithoutExtension(unit.FileName) + "_static_init") as Function).Body;
            foreach (var c in cls.Children)
            {
                if (c is FieldSymbol fs && fs.Flags.HasFlag(SymbolFlags.Static) && fs.InitialValue != null)
                {
                    var se = new SymbolExpression(fs);
                    //var ae = new AssignmentExpression(se, new CastExpression(fs.InitialValue, fs.DataType));
                    var ae = new AssignmentExpression(se, fs.InitialValue);
                    sinitBody.Statements.Add(new ExpressionStatement(ae));
                }
            }
            return true;
        }

        private bool UpdateInstanceInitMethod(ClassType cls)
        {
            var init = module.Find(cls.Name + "__init") as Function;
            Debug.Assert(init != null);
            var iniThis = init.Parameters[0];

            foreach (var c in cls.Children)
            {
                if (c is FieldSymbol fs && !fs.Flags.HasFlag(SymbolFlags.Static) && fs.InitialValue != null)
                {
                    var fe = new FieldExpression(new SymbolExpression(iniThis), fs);
                    //var ae = new AssignmentExpression(fe, new CastExpression(fs.InitialValue, fs.DataType));
                    var ae = new AssignmentExpression(fe, fs.InitialValue);
                    init.Body.Statements.Add(new ExpressionStatement(ae));
                }
            }
            init.Body.Statements.Add(new ReturnStatement(new SymbolExpression(iniThis)));

            return true;
        }

        protected override bool ParseTemplate()
        {
            if (!IsKeyword("class"))
                return false;
            SkipBraces();
            return Expect(Token.RBrace);
        }
        protected override bool ParseGlobalOrFunction()
        {
            if (IsKeyword("const"))
            {
                while (scanner.GetToken() != Token.EOF && scanner.GetToken() != Token.Semicolon)
                    scanner.NextToken();
                return Expect(Token.Semicolon);
            }

            int line = scanner.GetLine();

            if (IsType())
            {
                Declaration decl = new Declaration();
                if (!ParseDeclaration(ref decl, true, null, false))
                {
                    return false;
                }

                string str = decl.type.ToString();

                if (decl.type.IsFunction())
                {
                    Function f = decl.type as Function;
                    function = f;
                    f.Line = line;
                    f.fileName = fileName;
                    foreach (var p in f.Parameters)
                        p.Line = line;

                    if (scanner.GetToken() == Token.LBrace || scanner.GetToken() == Token.DoubleArrow)
                    {
                        f.Flags &= ~SymbolFlags.External;
                        scope = f;
                        f.Body = ParseStatementBlock();
                        if (f.Body == null)
                        {
                            return false;
                        }
                        scope = module;
                        function = null;
                        return true;
                    }
                    else
                    {
                        if (IsKeyword("external"))
                            ;
                        else if (IsKeyword("verbatim"))
                            scanner.NextToken();

                        function = null;
                        return Expect(Token.Semicolon);
                    }
                }
                else
                {
                    while (scanner.GetToken() != Token.EOF)
                    {
                        if (scanner.GetToken() == Token.Semicolon)
                            break;
                        scanner.NextToken();
                    }
                    return Expect(Token.Semicolon);
                }
            }
            else
            {
                Error("Syntax error");
                return false;
            }
        }
        private Statement ParseReturn()
        {
            Expression exp = null;
            if (scanner.GetToken() != Token.Semicolon)
            {
                exp = ParseExpression();
                if (exp == null)
                    return null;
                ForceCast(ref exp, function.ReturnType);
            }
            if (scanner.GetToken() != Token.Semicolon)
            {
                Error("; expected");
                return null;
            }
            scanner.NextToken();
            return new ReturnStatement(exp);
        }

        private Statement ParseDebug()
        {
            /*if (scanner.GetToken() != Token.Semicolon)
            {
                Error("; expected");
                return null;
            }
            scanner.NextToken();*/
            if (!Expect(Token.Semicolon))
                return null;
            return new DebugStatement();
        }

        private Statement ParseIf()
        {
            if (!Expect(Token.LPar))
                return null;
            var cond = ParseExpression();
            if (cond == null)
                return null;
            ForceCast(ref cond, TypeSymbol.Bool);
            if (!Expect(Token.RPar))
                return null;
            var _then = ParseStatementOrBlock();
            if (_then == null)
                return null;
            Statement _else = null;
            if (IsKeyword("else"))
            {
                _else = ParseStatementOrBlock();
                if (_else == null)
                    return null;
            }
            return new IfStatement(cond, _then, _else);
        }

        private StatementBlock ForceBlock(Statement s)
        {
            if (s is StatementBlock)
            {
                return (StatementBlock)s;
            }
            else
            {
                StatementBlock sb = new StatementBlock();
                sb.Statements.Add(s);
                return sb;
            }
        }

        private Statement ParseWhile()
        {
            if (!Expect(Token.LPar))
                return null;
            var cond = ParseExpression();
            if (cond == null)
                return null;
            ForceCast(ref cond, TypeSymbol.Bool);
            if (!Expect(Token.RPar))
                return null;
            var body = ParseStatementOrBlock();
            if (body == null)
                return null;
            body = ForceBlock(body);
            return new WhileStatement(cond, body);
        }

        private Statement ParseDo()
        {
            var body = ParseStatementOrBlock();
            if (body == null)
                return null;
            body = ForceBlock(body);
            if (!IsKeyword("while"))
            {
                Error("Syntax error");
                return null;
            }
            if (!Expect(Token.LPar))
                return null;
            var cond = ParseExpression();
            if (cond == null)
                return null;
            ForceCast(ref cond, TypeSymbol.Bool);
            if (!Expect(Token.RPar))
                return null;
            if (!Expect(Token.Semicolon))
                return null;
            return new DoStatement(body, cond);
        }

        private Statement ParseFor()
        {
            if (!Expect(Token.LPar))
                return null;
            Expression init = null;
            Expression cond = null;
            Expression iter = null;
            Statement body = null;
            BlockScope bs = new BlockScope();
            bs.Reparent(scope);
            scope = bs;
            if (scanner.GetToken() != Token.Semicolon)
            {
                init = ParseDeclarationOrExpression();
                if (init == null)
                    return null;
            }
            if (!Expect(Token.Semicolon))
                return null;
            if (scanner.GetToken() != Token.Semicolon)
            {
                cond = ParseExpression();
                if (cond == null)
                    return null;
                ForceCast(ref cond, TypeSymbol.Bool);
            }
            if (!Expect(Token.Semicolon))
                return null;
            if (scanner.GetToken() != Token.RPar)
            {
                iter = ParseExpression();
                if (iter == null)
                    return null;
            }
            if (!Expect(Token.RPar))
                return null;
            body = ParseStatementOrBlock();
            if (body == null)
                return null;
            body = ForceBlock(body);
            scope = bs.Parent;

            var fs = new ForStatement(init, cond, iter, body);
            fs.Scope = bs;
            return fs;
        }

        private Statement ParseBreak()
        {
            if (!Expect(Token.Semicolon))
                return null;
            return new BreakStatement();
        }

        private Statement ParseContinue()
        {
            if (!Expect(Token.Semicolon))
                return null;
            return new ContinueStatement();
        }

        private Statement ParseDelete()
        {
            var e = ParseExpression();
            if (e == null)
                return null;
            if (!Expect(Token.Semicolon))
                return null;

            if (!e.DataType.IsPointer() || !e.DataType.ElementType.IsClass())
            {
                Error("Pointer to class expected");
                return null;
            }
            var cls = e.DataType.ElementType as ClassType;

            var sb = new StatementBlock();

            var dtor = cls.FindChild("destructor") as MethodSymbol;
            if (dtor != null)
            {
                if (dtor.Flags.HasFlag(SymbolFlags.Virtual))
                {
                    var vtf = new FieldExpression(e, cls.FindChild("vtable"));
                    var vie = new IndexExpression(vtf, new ConstantExpression(dtor.VSlot));
                    var vcs = new CastExpression(vie, dtor.DataType.GetPointerType());
                    var vdr = new DerefExpression(vcs);
                    var fc = new FunctionCallExpression(vdr);
                    fc.line = e.line;
                    fc.Arguments.Add(e);
                    sb.Statements.Add(new ExpressionStatement(fc));
                }
                else
                {
                    /*if (dtor.Parent.Parent != module)
                        FindOrImport(dtor.DataType.Name);

                    var fc = new FunctionCallExpression(new SymbolExpression(dtor.DataType));
                    fc.Arguments.Add(e);
                    fc.line = e.line;
                    sb.Statements.Add(new ExpressionStatement(fc));*/
                    Debug.Assert(false); // destructors are always virtual now
                }
            }

            var finalize = cls.FindChild("finalize") as MethodSymbol;

            if (finalize != null)
            {
                if (finalize.Flags.HasFlag(SymbolFlags.Virtual))
                {
                    var vtf = new FieldExpression(e, cls.FindChild("vtable"));
                    var vie = new IndexExpression(vtf, new ConstantExpression(finalize.VSlot));
                    var vcs = new CastExpression(vie, finalize.DataType.GetPointerType());
                    var vdr = new DerefExpression(vcs);
                    var fc = new FunctionCallExpression(vdr);
                    fc.line = e.line;
                    fc.Arguments.Add(e);
                    sb.Statements.Add(new ExpressionStatement(fc));
                }
                else
                {
                    Debug.Assert(false); // finalizer should be virtual
                }
            }

            var free = module.Find("free") as Function;
            if (free != null)
            {
                var fcall = new FunctionCallExpression(new SymbolExpression(free));
                fcall.Arguments.Add(e);
                sb.Statements.Add(new ExpressionStatement(fcall));
            }

            return sb;
        }

        private Statement ParseExpressionStatement()
        {
            Expression exp = ParseExpression();
            if (exp == null)
                return null;
            /*if (scanner.GetToken() != Token.Semicolon)
            {
                Error("; expected");
                return null;
            }
            scanner.NextToken();*/
            if (!Expect(Token.Semicolon))
                return null;
            return new ExpressionStatement(exp);
        }

        private Statement ParseStatement()
        {
            if (IsKeyword("return"))
                return ParseReturn();
            else if (IsKeyword("debug"))
                return ParseDebug();
            else if (IsKeyword("if"))
                return ParseIf();
            else if (IsKeyword("while"))
                return ParseWhile();
            else if (IsKeyword("do"))
                return ParseDo();
            else if (IsKeyword("for"))
                return ParseFor();
            else if (IsKeyword("break"))
                return ParseBreak();
            else if (IsKeyword("continue"))
                return ParseContinue();
            else if (IsKeyword("delete"))
                return ParseDelete();
            else if(scanner.GetToken() == Token.LBrace)
                return ParseStatementBlock();
            else if(scanner.GetToken() == Token.Semicolon)
            {
                scanner.NextToken();
                return new EmptyStatement();
            }
            else
            {
                var line = scanner.GetLine();
                var s = ParseExpressionStatement();
                if (s != null)
                    SetContext(s, line);

                // Expression is a function call returning an object ref, assign to a temp local variable so it is
                // properly released at the end of current scope.

                if (s is ExpressionStatement es
                    && es.Expression is FunctionCallExpression fce
                    && fce.DataType.IsPointer() && fce.DataType.ElementType.IsClass())
                {
                    //Info($"*** Adding temp variable to {scope} in call to {fce.Callee.DataType.Name}");

                    var lv = new LocalVariable(scope, "__temp" + scanner.GetLine(), fce.DataType, null);
                    lv.Offset = -(++function.LocalsCount);
                    function.Locals.Add(lv);

                    var assign = new AssignmentExpression(new SymbolExpression(lv), es.Expression);
                    assign.file = unit.Path;
                    assign.line = es.line;
                    s = new ExpressionStatement(assign);
                    s.file = unit.Path;
                    s.line = es.line;
                }

                return s;
            }
        }

        private StatementBlock ParseStatementBlock()
        {
            if (scanner.GetToken() == Token.DoubleArrow)
            {
                scanner.NextToken();
                Expression expression = ParseExpression();
                if (expression == null) return null;
                if (!Expect(Token.Semicolon))
                    return null;
                StatementBlock blk = new StatementBlock();
                var rs = new ReturnStatement(expression);
                rs.line = scanner.GetLine();
                rs.file = scanner.GetFileName();
                blk.Statements.Add(rs);
                return blk;
            }

            scanner.NextToken();
            StatementBlock block = new StatementBlock();

            block.Scope.Reparent(scope);
            scope = block.Scope;

            while (scanner.GetToken() != Token.RBrace)
            {
                bool _const = false;
                if(IsKeyword("const"))
                    _const = true;

                if (IsType())
                {
                    if (_const)
                    {
                        Declaration decl = new Declaration();

                        if (!ParseDeclaration(ref decl, true, null))
                            return null;

                        if (decl.isByRef)
                        {
                            Error("Reference not allowed here");
                            return null;
                        }

                        Expression value = null;
                        if(!Expect(Token.Assign))
                            return null;
                        value = ParseExpression();
                        if(value == null) 
                            return null; 
                        if(!(value is ConstantExpression))
                        {
                            Error("Constant expression expected");
                            return null;
                        }
                        ForceCast(ref value, decl.type);
                        var cv = new ConstantValue(scope, decl.name, value);
                        if (!Expect(Token.Semicolon))
                            return null;
                    }
                    else
                    {
                        var state = scanner.GetState();
                        TypeSymbol t = ParseType();
                        if (scanner.GetToken() == Token.Period)
                        {
                            scanner.Restore(state);
                            Expression e = ParseExpression();
                            if (e == null)
                                return null;
                            var s = new ExpressionStatement(e);
                            if (!Expect(Token.Semicolon))
                                return null;
                            block.Statements.Add(s);
                            continue;
                        }
                        else
                        {
                            scanner.Restore(state);
                        }

                        Declaration decl = new Declaration();

                        if (!ParseDeclaration(ref decl, true, null))
                            return null;

                        if (decl.isByRef)
                        {
                            Error("Reference not allowed here");
                            return null;
                        }

                        string name = decl.name;
                        TypeSymbol type = decl.type;

                        Expression init = null;
                        if (scanner.GetToken() == Token.Assign)
                        {
                            scanner.NextToken();
                            if (scanner.GetToken() == Token.LBrace)
                                init = ParseInitList(type);
                            else
                                init = ParseExpression();
                            if (init == null)
                                return null;
                        }

                        LocalVariable lv = new LocalVariable(scope, name, type, init);
                        lv.Offset = -(++function.LocalsCount);
                        lv.Line = scanner.GetLine();

                        function.Locals.Add(lv);
                        if (init != null)
                        {
                            var se = new SymbolExpression(lv);
                            StaticAssign(block, se, init, scanner.GetLine(), lv);
                        }
                        if (!Expect(Token.Semicolon))
                            return null;
                    }
                }
                else
                {
                    if(_const)
                    {
                        Error("Syntax error");
                        return null;
                    }

                    int line = scanner.GetLine();
                    Statement s = ParseStatement();
                    if (s == null)
                        return null;
                    SetContext(s, line);
                    block.Statements.Add(s);
                }
            }
            block.endLine = scanner.GetLine();
            scanner.NextToken();

            scope = scope.Parent;

            return block;
        }

        private Statement ParseStatementOrBlock()
        {
            if (scanner.GetToken() == Token.LBrace)
                return ParseStatementBlock();
            else
            {
                int line = scanner.GetLine();
                var s = ParseStatement();
                if (s == null)
                    return null;
                SetContext(s, line);
                return s;
            }
        }

        private Expression ParseDeclarationOrExpression()
        {
            if (IsType())
            {
                Declaration decl = new Declaration();

                if (!ParseDeclaration(ref decl, true, null))
                    return null;

                string name = decl.name;
                TypeSymbol type = decl.type;

                if (!Expect(Token.Assign))
                    return null;
                Expression init = ParseExpression();
                if (init == null)
                    return null;

                LocalVariable lv = new LocalVariable(scope, name, type, init);
                lv.Offset = -(++function.LocalsCount);
                function.Locals.Add(lv);
                ForceCast(ref init, type);
                AssignmentExpression a = new AssignmentExpression(
                        new SymbolExpression(lv),
                        init);
                SetContext(a);
                return a;
            }
            else
            {
                return ParseExpression();
            }
        }
    }
}

