using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Jargon
{
    public class Parser1 : StageParser
    {
        public Parser1(Module module, ICompilerErrorListener errorListener) : base(module, errorListener) { }

        protected override bool ParseUsing()
        {
            if (scanner.GetToken() != Token.Ident)
            {
                Error("Identifier expected");
                return false;
            }

            var name = scanner.GetTokenString();
            scanner.NextToken();

            foreach (var u in unit.Usings)
            {
                if (u.Name == name)
                    return true; // already using it
            }

            Module moduleToUse = null;
            bool foundInModule = false;
            foreach (var u in module.Usings)
            {
                if (u.Name == name)
                {
                    moduleToUse = u.Module;
                    foundInModule = true;
                    break;
                }
            }
            if (moduleToUse == null)
            {
                var dirs = CompilerOptions.LibraryDirectories.ToList();
                dirs.Insert(0, Directory.GetCurrentDirectory());
                string dllname = null;
                foreach (var dir in dirs)
                {
                    var ddir = dir;
                    if (dir.StartsWith("$"))
                    {
                        ddir = Environment.GetEnvironmentVariable(dir.Substring(1));
                        if (ddir == null)
                            continue;
                    }

                    if (File.Exists(ddir + "\\" + name + ".dll"))
                    {
                        dllname = ddir + "\\" + name + ".dll";
                        break;
                    }
                }
                if (dllname == null)
                {
                    Error("Could not find " + name + ".dll");
                    return false;
                }
                moduleToUse = TypeInfo.ParseModule(dllname);
                if (moduleToUse == null)
                {
                    Error("Error loading " + dllname);
                    return false;
                }

            }
            var use = new ModuleUsing(name);
            use.Module = moduleToUse;

            unit.Usings.Add(use);

            if (!foundInModule)
                module.Usings.Add(use);

            if (name == "jargon")
                ImportJargonDefaults();

            return Expect(Token.Semicolon);
        }

        private void ImportJargonDefaults()
        {
            FindOrImport("object__constructor");
            FindOrImport("object__addRef");
            FindOrImport("object__release");
            FindOrImport("object__finalize");
            FindOrImport("newObj");
        }

        protected override bool ParseEnum()
        {
            if (scanner.GetToken() != Token.Ident)
            {
                Error("Identifier expected");
                return false;
            }
            string name = scanner.GetTokenString();
            scanner.NextToken();

            TypeSymbol type = TypeSymbol.Int;

            if (scanner.GetToken() == Token.Colon)
            {
                scanner.NextToken();
                type = ParseType();
                if (type == null || !type.IsInteger())
                {
                    Error("Integer type expected");
                    return false;
                }
            }

            var enu = new EnumType(module, name, type);
            unit.AddSymbol(enu);

            if (!Expect(Token.LBrace))
                return false;

            long value = 0;
            while (scanner.GetToken() != Token.RBrace)
            {
                if (scanner.GetToken() != Token.Ident)
                {
                    Error("Identifier expected");
                    return false;
                }
                name = scanner.GetTokenString();
                scanner.NextToken();
                if (scanner.GetToken() == Token.Assign)
                {
                    scanner.NextToken();
                    Expression e = ParseExpression();
                    if (e == null)
                        return false;
                    var ce = e as ConstantExpression;
                    if (ce == null || !ce.DataType.IsInteger())
                    {
                        Error("Integer constant expected");
                        return false;
                    }
                    value = ce.Value;
                }
                if (scanner.GetToken() == Token.Comma)
                    scanner.NextToken();
                else if (scanner.GetToken() != Token.RBrace)
                {
                    Error("} expected");
                    return false;
                }
                var ev = new EnumValue(enu, name, value);
                value++;
            }

            return Expect(Token.RBrace);
        }
        protected override bool ParseStructure(bool isUnion)
        {
            if (scanner.GetToken() != Token.Ident)
            {
                Error("Identifier expected");
                return false;
            }
            var name = scanner.GetTokenString();
            scanner.NextToken();
            var existing = module.Find(name);
            if (existing == null)
            {
                var st = new StructType(module, name);
                unit.AddSymbol(st);
                if (isUnion) st.Flags |= SymbolFlags.Union;
            }
            else if (existing.SymbolType != SymbolType.Struct)
            {
                Error("Duplicate definition");
                return false;
            }
            if (scanner.GetToken() == Token.Semicolon)
            {
                scanner.NextToken();
                return true;
            }
            else
            {
                SkipBraces();
                return Expect(Token.RBrace);
            }
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

            if (scanner.GetToken() == Token.Colon)
            {
                scanner.NextToken();
                if (scanner.GetToken() != Token.Ident)
                {
                    Error("Identifier expected");
                    return false;
                }
                scanner.NextToken();
            }

            var existing = module.Find(name) as ClassType;
            if (existing == null)
            {
                var cls = new ClassType(module, name);
                if (name != "object")
                    cls.BaseClass = FindType("object") as ClassType;
                unit.AddSymbol(cls);
                existing = cls;
            }
            else if (existing.SymbolType != SymbolType.Class)
            {
                Error("Duplicate definition");
                return false;
            }

            var factory = new Function(module, existing.Name + "__new", existing.GetPointerType());
            existing.Factory = factory;
            unit.AddSymbol(factory);

            if (scanner.GetToken() == Token.Semicolon)
            {
                scanner.NextToken();
                return true;
            }
            else
            {
                SkipBraces();
                return Expect(Token.RBrace);
            }
        }
        protected override bool ParseTemplate()
        {
            if (!IsKeyword("class"))
                return false;

            if (scanner.GetToken() != Token.Ident)
            {
                Error("Identifier expected");
                return false;
            }
            string name = scanner.GetTokenString();
            scanner.NextToken();
            if (!Expect(Token.Less))
                return false;
            var templateParams = new List<string>();
            while (scanner.GetToken() != Token.Greater)
            {
                if (scanner.GetToken() != Token.Ident)
                {
                    Error("Template param identifier expected");
                    return false;
                }
                templateParams.Add(scanner.GetTokenString());
                scanner.NextToken();
                if (scanner.GetToken() == Token.Comma)
                    scanner.NextToken();
                else if (scanner.GetToken() == Token.Greater)
                    break;
            }
            var startState = scanner.GetState();
            if (!Expect(Token.Greater))
                return false;
            SkipBraces();
            var endState = scanner.GetState();
            var len = endState.pos - startState.pos;
            var templateSrc = startState.text.Substring(startState.pos, len);
            var ts = new Template(module, name);
            ts.TemplateParams.AddRange(templateParams);
            ts.Source = templateSrc;
            return Expect(Token.RBrace);

        }
        protected override bool ParseGlobalOrFunction()
        {
            bool _const = false;
            if(IsKeyword("const"))
            {
                _const = true;
            }

            if (IsType())
            {
                if (_const)
                {
                    Declaration item = new Declaration();
                    if(!ParseDeclaration(ref item, true, null, false, true))
                        return false;
                    Expression value = null;
                    if(!Expect(Token.Assign))
                        return false;
                    value = ParseExpression();
                    if (!(value is ConstantExpression))
                    {
                        Error("Constant expression expected");
                        return false;
                    }
                    var cv = new ConstantValue(module, item.name, value);
                    if(!Expect(Token.Semicolon))
                        return false;
                    return true;
                }
                else
                {
                    while (scanner.GetToken() != Token.EOF)
                    {
                        if (scanner.GetToken() == Token.Semicolon || scanner.GetToken() == Token.LBrace || scanner.GetToken() == Token.Assign)
                            break;
                        scanner.NextToken();
                    }
                    if (scanner.GetToken() == Token.Semicolon)
                    {
                        scanner.NextToken();
                        return true;
                    }
                    else if (scanner.GetToken() == Token.LBrace)
                    {
                        SkipBraces();
                        return Expect(Token.RBrace);
                    }
                    else if (scanner.GetToken() == Token.Assign)
                    {
                        while (scanner.GetToken() != Token.Semicolon && scanner.GetToken() != Token.EOF)
                            scanner.NextToken();
                        return Expect(Token.Semicolon);
                    }
                    else if (scanner.GetToken() == Token.DoubleArrow)
                    {
                        scanner.NextToken();
                        while (scanner.GetToken() != Token.Semicolon && scanner.GetToken() != Token.EOF)
                            scanner.NextToken();
                        return Expect(Token.Semicolon);
                    }
                }
            }
            Error("Syntax error");
            return false;
        }
    }
}

