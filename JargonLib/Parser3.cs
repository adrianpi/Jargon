using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Jargon
{
    public class Parser3 : StageParser
    {
        public Parser3(Module module, ICompilerErrorListener errorListener) : base(module, errorListener) { }

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
                SkipBraces();

                // Create factory function for new operator
                if (!CreateFactoryFunction(cls))
                    return false;

                _class = null;
                return Expect(Token.RBrace);
            }
            else
            {
                return Expect(Token.Semicolon);
            }
        }
        private bool CreateFactoryFunction(ClassType cls)
        {
            // return constructor(initB(initA...(newObj(size, vtable))), params...);

            var factory = cls.Factory;
            var vtab = module.Find(cls.Name + "__vtable") as GlobalVariable;
            Debug.Assert(vtab != null);

            //var newObj = module.Find("newObj") as Function;
            var newObj = FindOrImport("newObj");
            Debug.Assert(newObj != null);

            var newObjCall = new FunctionCallExpression(
                                new SymbolExpression(newObj));
            newObjCall.Arguments.Add(new ConstantExpression(cls.Size));
            newObjCall.Arguments.Add(new CastExpression(new SymbolExpression(vtab), TypeSymbol.Void.GetPointerType()));
            // Traverse class hierarchy bottom-up
            var hierarchy = new List<ClassType>();
            hierarchy.Add(cls);
            ClassType hc = cls.BaseClass;
            while (hc != null)
            {
                hierarchy.Add(hc);
                hc = hc.BaseClass;
            }
            hierarchy.Reverse(); // make class hierarchy top-down
            Expression arg = newObjCall;
            // call init methods in order
            foreach (var c in hierarchy)
            {
                var initFn = FindOrImport(c.Name + "__init");
                Debug.Assert(initFn != null);
                var initCall = new FunctionCallExpression(new SymbolExpression(initFn));
                initCall.Arguments.Add(arg);
                arg = initCall;
            }
            // add call to constructor if present
            var ctor = FindOrImport(cls.Name + "__constructor");
            factory.Body = new StatementBlock();
            if (ctor != null)
            {
                var ctorCall = new FunctionCallExpression(
                    new SymbolExpression(ctor));
                ctorCall.Arguments.Add(arg);
                foreach (var p in factory.Parameters)
                    ctorCall.Arguments.Add(new SymbolExpression(p));
                factory.Body.Statements.Add(new ReturnStatement(ctorCall));
            }
            else
            {
                // Now creating a default constructor if no constructor is provided
                //factory.Body.Statements.Add(new ReturnStatement(arg));
                Debug.Assert(false);
            }

            factory.Flags |= SymbolFlags.NoDebug;

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

                if (decl.type.IsFunction())
                {
                    if (scanner.GetToken() == Token.LBrace)
                    {
                        SkipBraces();
                        return Expect(Token.RBrace);
                    }
                    else if (scanner.GetToken() == Token.DoubleArrow)
                    {
                        scanner.NextToken();
                        while (scanner.GetToken() != Token.Semicolon && scanner.GetToken() != Token.EOF)
                            scanner.NextToken();
                        return Expect(Token.Semicolon);
                    }
                    else
                    {
                        if (IsKeyword("external"))
                            ;
                        else if (IsKeyword("verbatim"))
                            scanner.NextToken();

                        return Expect(Token.Semicolon);
                    }
                }
                else
                {
                    Expression init = null;
                    if (scanner.GetToken() == Token.Assign)
                    {
                        scanner.NextToken();
                        if (scanner.GetToken() == Token.LBrace)
                            init = ParseInitList(decl.type);
                        else
                            init = ParseExpression();
                    }

                    var gv = new GlobalVariable(module, decl.name, decl.type, init);
                    gv.Line = line;
                    unit.AddSymbol(gv);

                    if (init != null)
                    {
                        Function static_init = module.Find(Path.GetFileNameWithoutExtension(unit.FileName) + "_static_init") as Function;
                        if (static_init != null)
                        {
                            StatementBlock sb = static_init.Body;
                            var se = new SymbolExpression(gv);
                            StaticAssign(sb, se, init, scanner.GetLine(), null);
                        }
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
    }
}

