using System.Collections.Generic;
using System.Diagnostics;

namespace Jargon
{
    public class ExpressionParser : BaseParser
    {
        protected ExpressionParser(ICompilerErrorListener errorListener) : base(errorListener)
        {
        }

        private Expression ParsePrimary()
        {
            if (IsKeyword("null"))
            {
                return new ConstantExpression();
            }
            else if (scanner.GetToken() == Token.Bool)
            {
                bool val = scanner.GetTokenBool();
                scanner.NextToken();
                return new ConstantExpression(val);
            }
            else if (scanner.GetToken() == Token.Byte)
            {
                byte val = (byte)scanner.GetTokenInt();
                scanner.NextToken();
                return new ConstantExpression(val);
            }
            else if (scanner.GetToken() == Token.Int)
            {
                int val = scanner.GetTokenInt();
                scanner.NextToken();
                return new ConstantExpression(val);
            }
            else if (scanner.GetToken() == Token.UInt)
            {
                uint val = unchecked((uint)scanner.GetTokenInt());
                scanner.NextToken();
                return new ConstantExpression(val);
            }
            else if (scanner.GetToken() == Token.Long)
            {
                long val = scanner.GetTokenLong();
                scanner.NextToken();
                return new ConstantExpression(val);
            }
            else if (scanner.GetToken() == Token.ULong)
            {
                ulong val = unchecked((ulong)scanner.GetTokenLong());
                scanner.NextToken();
                return new ConstantExpression(val);
            }
            else if (scanner.GetToken() == Token.Float)
            {
                float val = scanner.GetTokenFloat();
                scanner.NextToken();
                return new ConstantExpression(val);
            }
            if (scanner.GetToken() == Token.Double)
            {
                double val = scanner.GetTokenDouble();
                scanner.NextToken();
                return new ConstantExpression(val);
            }
            else if (scanner.GetToken() == Token.String)
            {
                string str = scanner.GetTokenString();
                scanner.NextToken();
                while (scanner.GetToken() == Token.String)
                {
                    str += scanner.GetTokenString();
                    scanner.NextToken();
                }
                string key = module.AddString(str);
                unit.Strings.Add(key);
                return new ConstantExpression(key);
            }
            else if (scanner.GetToken() == Token.LPar)
            {
                scanner.NextToken();
                if (IsType())
                {
                    Declaration decl = new Declaration();
                    if (!ParseDeclaration(ref decl, false, null, true))
                        return null;

                    TypeSymbol castType = decl.type;

                    if (scanner.GetToken() != Token.RPar)
                    {
                        Error(") expected");
                        return null;
                    }
                    scanner.NextToken();
                    Expression e = ParsePointerOp();
                    if (e == null)
                        return null;

                    return new CastExpression(e, castType);
                }
                else
                {
                    Expression e = ParseExpression();
                    if (e == null)
                        return null;
                    if (scanner.GetToken() != Token.RPar)
                    {
                        Error(") expected");
                        return null;
                    }
                    scanner.NextToken();
                    return e;
                }
            }
            else if (scanner.GetToken() == Token.Ident)
            {
                string name = scanner.GetTokenString();

                Symbol sym = (scope != null ? scope : module).Find(name);
                if (sym == null && _class != null)
                {
                    sym = _class.FindChild(name);
                    if (sym != null && sym is PropertySymbol ps)
                    {
                        if (ps.Getter != null)
                            FindOrImport(ps.Getter.DataType.Name);
                        if (ps.Setter != null)
                            FindOrImport(ps.Setter.DataType.Name);
                    }
                }

                if (sym == null)
                {
                    //foreach (var u in module.Usings)
                    foreach (var u in unit.Usings)
                    {
                        sym = u.Module.Find(name);
                        if (sym != null)
                        {
                            // import!
                            if (sym is Function extFn)
                            {
                                Function impFn = new Function(module, extFn.Name, extFn.ReturnType);
                                impFn.Flags = extFn.Flags | SymbolFlags.External;
                                impFn.Verbatim = extFn.Verbatim;
                                foreach (var extParam in extFn.Parameters)
                                {
                                    var impParam = new LocalVariable(impFn, extParam.Name, extParam.DataType, null);
                                    impParam.Flags = extParam.Flags;
                                    impParam.Offset = extParam.Offset;
                                    impFn.Parameters.Add(impParam);
                                }
                                sym = impFn;
                            }
                            break;
                        }
                    }

                    if (sym == null)
                    {
                        Error("Undefined symbol " + name);
                        return null;
                    }
                }
                scanner.NextToken();

                if (sym is Function fn && fn.Unit != unit)
                    unit.AddSymbolRef(fn);

                if (sym.Flags.HasFlag(SymbolFlags.ByRef))
                    return new DerefExpression(new SymbolExpression(sym));
                else
                    return new SymbolExpression(sym);
            }
            else if (IsKeyword("sizeof"))
            {
                if (!Expect(Token.LPar))
                    return null;
                TypeSymbol type = null;
                if (IsType())
                {
                    Declaration decl = new Declaration();
                    if (!ParseDeclaration(ref decl, false, null, true))
                        return null;
                    type = decl.type;
                }
                else
                {
                    Expression e = ParseExpression();
                    if (e == null)
                        return null;
                    type = e.DataType;
                }
                if (!Expect(Token.RPar))
                    return null;
                return new SizeOfExpression(type);
            }
            else if (IsKeyword("base"))
            {
                MethodSymbol m = null;
                if (_class == null || _class.BaseClass == null)
                {
                    Error("Invalid use of base");
                    return null;
                }
                foreach (var c in _class.Children)
                {
                    if (c is MethodSymbol ms && ms.DataType == function)
                    {
                        m = ms;
                        break;
                    }
                }
                if (m == null)
                {
                    Error("Invalid use of base");
                    return null;
                }
                m = _class.BaseClass.FindChild(m.Name) as MethodSymbol;
                if (m == null)
                {
                    Error("Invalid use of base");
                    return null;
                }
                var fe = new FieldExpression(
                    new SymbolExpression(function.FindChild("this")),
                    m);
                fe.Explicit = true;

                if (_class.BaseClass.Parent != module)
                    FindOrImport(m.DataType.Name);

                return fe;
            }
            else if (IsKeyword("new"))
            {
                ClassType cls = null;
                TypeSymbol ts = ParseType();
                if(ts == null)
                {
                    Error("Type expected");
                    return null;
                }
                if (ts.SymbolType == SymbolType.Template)
                {
                    if (!Expect(Token.Less))
                        return null;
                    var targs = new List<TypeSymbol>();
                    while (scanner.GetToken() != Token.EOF)
                    {
                        if (IsType())
                        {
                            /*var t = ParseType();
                            if (scanner.GetToken() == Token.Mul)
                            {
                                scanner.NextToken();
                                t = t.GetPointerType();
                            }*/
                            Declaration tt = new Declaration();
                            ParseDeclaration(ref tt, false, null, true, true);
                            TypeSymbol t = tt.type;
                            targs.Add(t);
                        }
                        else
                        {
                            Error("Template arg type expected");
                            return null;
                        }

                        if (scanner.GetToken() == Token.Comma)
                            scanner.NextToken();
                        else if (scanner.GetToken() == Token.Greater)
                            break;
                    }
                    if (!Expect(Token.Greater))
                        return null;
                    var name = ts.Name;
                    foreach (var t in targs)
                    {
                        name += "_" + TypeToFName(t);
                    }
                    cls = FindType(name) as ClassType;
                }
                else if (ts.SymbolType == SymbolType.Class)
                {
                    cls = ts as ClassType;
                }
                if (cls == null)
                {
                    Error("Class expected");
                    return null;
                }
                var args = new List<Expression>();
                if (!Expect(Token.LPar))
                    return null;
                var ctor = FindOrImport(cls.Name + "__new");
                Debug.Assert(ctor != null);
                int idx = 0;
                while (scanner.GetToken() != Token.RPar)
                {
                    var e = ParseExpression();
                    if (e == null)
                        return null;
                    if (ctor != null && idx >= 0 && idx < ctor.Parameters.Count)
                        ForceCast(ref e, ctor.Parameters[idx].DataType);
                    args.Add(e);
                    if (scanner.GetToken() == Token.Comma)
                        scanner.NextToken();
                    else if (scanner.GetToken() != Token.RPar)
                    {
                        Error(") expected");
                        return null;
                    }
                    ++idx;
                }
                if (!Expect(Token.RPar))
                    return null;
                var ctorCall = new FunctionCallExpression(new SymbolExpression(ctor));
                ctorCall.line = scanner.GetLine();
                ctorCall.Arguments.AddRange(args);
                return ctorCall;
            }
            else
            {
                Error("Syntax error");
                return null;
            }
        }

        protected void SetContext(Expression e, int line = -1)
        {
            e.file = scanner.GetFileName();
            e.line = line == -1 ? scanner.GetLine() : line;
        }

        private Expression ParseSelector()
        {
            Expression left = ParsePrimary();
            if (left == null)
                return null;
            SetContext(left);

            while (scanner.GetToken() == Token.LBracket || scanner.GetToken() == Token.Period || scanner.GetToken() == Token.LPar)
            {
                if (scanner.GetToken() == Token.LBracket)
                {
                    scanner.NextToken();
                    Expression index = ParseExpression();
                    if (index == null)
                        return null;
                    if (!Expect(Token.RBracket))
                        return null;
                    SetContext(index);
                    AutoThis(ref left);
                    AutoThis(ref index);

                    if (left.DataType.IsPointer() && left.DataType.ElementType.IsClass())
                    {
                        var cls = left.DataType.ElementType;
                        var m = cls.Find("get_item");
                        if (m != null)
                            FindOrImport(m.DataType.Name);
                        m = cls.Find("set_item");
                        if (m != null)
                            FindOrImport(m.DataType.Name);
                    }

                    left = new IndexExpression(left, index);
                    SetContext(left);
                }
                else if (scanner.GetToken() == Token.Period)
                {
                    scanner.NextToken();
                    if (scanner.GetToken() != Token.Ident)
                    {
                        Error("Identifier expected");
                        return null;
                    }
                    string name = scanner.GetTokenString();
                    scanner.NextToken();
                    TypeSymbol stype = left.DataType;
                    if (left is SymbolExpression se && se.Symbol is ClassType ct)
                        stype = ct;
                    if (left.DataType.IsPointer())
                        stype = stype.ElementType;
                    Symbol field = stype.FindChild(name);
                    if (field == null)
                    {
                        string extName = "ext_" + TypeToFName(stype);
                        ClassType extCls = FindType(extName) as ClassType;
                        if (extCls != null)
                            field = extCls.FindChild(name);
                        if (field == null)
                        {
                            Error("Invalid field '" + name + "'");
                            return null;
                        }
                    }

                    int line = left.line;
                    if (field.Flags.HasFlag(SymbolFlags.Static))
                    {
                        if (field is MethodSymbol ms)
                        {
                            FindOrImport(ms.DataType.Name);
                            left = new SymbolExpression(ms.DataType);
                        }
                        else
                        {
                            var gv = new GlobalVariable(null, field.Parent.Name + "__" + field.Name, field.DataType, null);
                            // if the field owner is external, the global variable should be external to be dllimported
                            if (field.Flags.HasFlag(SymbolFlags.External))
                                gv.Flags |= SymbolFlags.External;
                            if (field.Parent.Unit != unit)
                                unit.AddSymbolRef(gv);
                            left = new SymbolExpression(field);
                        }
                        left.line = line;
                        continue;
                    }
                    else
                    {
                        if ((stype.IsStruct() || stype.IsClass()) && stype.Unit != unit)
                            unit.AddSymbolRef(stype);
                    }


                    AutoThis(ref left);

                    if (field is MethodSymbol ms2)
                    {
                        FindOrImport(ms2.DataType.Name);
                    }
                    else if (field is PropertySymbol ps2)
                    {
                        if (ps2.Getter != null)
                            FindOrImport(ps2.Getter.DataType.Name);
                        if (ps2.Setter != null)
                            FindOrImport(ps2.Setter.DataType.Name);
                    }

                    left = new FieldExpression(left, field);
                    SetContext(left, line);
                }
                else if (scanner.GetToken() == Token.LPar)
                {
                    scanner.NextToken();

                    Expression vcall = null;

                    AutoThis(ref left);

                    if (left is FieldExpression fe1 && fe1.Field is MethodSymbol ms1 && !fe1.Explicit && ms1.Flags.HasFlag(SymbolFlags.Virtual))
                    {
                        var vtf = new FieldExpression(fe1.Expression, fe1.Expression.DataType.ElementType.FindChild("vtable"));
                        var vie = new IndexExpression(vtf, new ConstantExpression(ms1.VSlot));
                        var vcs = new CastExpression(vie, ms1.DataType.GetPointerType());
                        var vdr = new DerefExpression(vcs);
                        vcall = vdr;
                    }

                    var fc = new FunctionCallExpression(vcall != null ? vcall : left);
                    SetContext(fc);

                    Function fn = left.DataType.IsPointer() ? left.DataType.ElementType as Function : left.DataType as Function;
                    FindOrImport(fn.Name);

                    int idx = 0;

                    if (left is FieldExpression fe && fe.Field is MethodSymbol ms)
                    {
                        fc.Arguments.Add(fe.Expression);
                        ++idx;
                    }

                    while (scanner.GetToken() != Token.RPar && scanner.GetToken() != Token.EOF)
                    {
                        var e = ParseExpression();
                        if (e == null)
                            return null;

                        if (idx < fn.Parameters.Count)
                        {
                            if (fn.Parameters[idx].Flags.HasFlag(SymbolFlags.ByRef))
                                e = new AddressOfExpression(e);
                            ForceCast(ref e, fn.Parameters[idx].DataType);
                        }
                        else if (fn.Flags.HasFlag(SymbolFlags.Variadic))
                        {
                            if (e.DataType == TypeSymbol.Float)
                                ForceCast(ref e, TypeSymbol.Double);
                            else if (e.DataType == TypeSymbol.Bool)
                                ForceCast(ref e, TypeSymbol.Int);
                            else if(e.DataType.IsInteger() && e.DataType.Size < 4)
                            {
                                if(e.DataType.IsUnsigned())
                                    ForceCast(ref e, TypeSymbol.UInt);
                                else
                                    ForceCast(ref e, TypeSymbol.Int);
                            }
                            else if (e.DataType.Name == "string*")
                            {
                                var c_strFn = FindOrImport("string__c_str");
                                var c_strCall = new FunctionCallExpression(new SymbolExpression(c_strFn));
                                c_strCall.Arguments.Add(e);
                                c_strCall.line = e.line;
                                c_strCall.file = e.file;
                                e = c_strCall;
                            }
                        }
                        fc.Arguments.Add(e);
                        if (scanner.GetToken() == Token.Comma)
                            scanner.NextToken();
                        else if (scanner.GetToken() != Token.RPar)
                        {
                            Error(") expected");
                            return null;
                        }
                        ++idx;
                    }
                    if (!Expect(Token.RPar))
                        return null;
                    left = fc;
                }
            }

            if (IsKeyword("is"))
            {
                if (!left.DataType.IsPointer() || !left.DataType.ElementType.IsClass())
                {
                    Error("Invalid use of 'is'");
                    return null;
                }
                ClassType cls = null;
                TypeSymbol ts = ParseType();
                if (ts == null)
                {
                    Error("Type expected");
                    return null;
                }
                if (ts.SymbolType == SymbolType.Template)
                {
                    if (!Expect(Token.Less))
                        return null;
                    var targs = new List<TypeSymbol>();
                    while (scanner.GetToken() != Token.EOF)
                    {
                        if (IsType())
                        {
                            var tt = new Declaration();
                            ParseDeclaration(ref tt, false, null, true, true);
                            targs.Add(tt.type);
                        }
                        else
                        {
                            Error("Template arg type expected");
                            return null;
                        }

                        if (scanner.GetToken() == Token.Comma)
                            scanner.NextToken();
                        else if (scanner.GetToken() == Token.Greater)
                            break;
                    }
                    if (!Expect(Token.Greater))
                        return null;
                    var name = ts.Name;
                    foreach (var t in targs)
                    {
                        name += "_" + TypeToFName(t);
                    }
                    cls = FindType(name) as ClassType;
                }
                else if (ts.SymbolType == SymbolType.Class)
                {
                    cls = ts as ClassType;
                }
                if (cls == null)
                {
                    Error("Class expected");
                    return null;
                }
                var fn = FindOrImport("_isClassOf");
                var fc = new FunctionCallExpression(new SymbolExpression(fn));
                fc.Arguments.Add(left);
                var cname = module.AddString(cls.Name);
                unit.Strings.Add(cname);
                fc.Arguments.Add(new ConstantExpression(cname));
                left = fc;
                SetContext(left);
            }
            else if (IsKeyword("as"))
            {
                if (!left.DataType.IsPointer() || !left.DataType.ElementType.IsClass())
                {
                    Error("Invalid use of 'as'");
                    return null;
                }
                ClassType cls = null;
                TypeSymbol ts = ParseType();
                if(ts == null)
                {
                    Error("Type expected");
                    return null;
                }
                if (ts.SymbolType == SymbolType.Template)
                {
                    if (!Expect(Token.Less))
                        return null;
                    var targs = new List<TypeSymbol>();
                    while (scanner.GetToken() != Token.EOF)
                    {
                        if (IsType())
                        {
                            var tt = new Declaration();
                            ParseDeclaration(ref tt, false, null, true, true);
                            targs.Add(tt.type);
                        }
                        else
                        {
                            Error("Template arg type expected");
                            return null;
                        }

                        if (scanner.GetToken() == Token.Comma)
                            scanner.NextToken();
                        else if (scanner.GetToken() == Token.Greater)
                            break;
                    }
                    if (!Expect(Token.Greater))
                        return null;
                    var name = ts.Name;
                    foreach (var t in targs)
                    {
                        name += "_" + TypeToFName(t);
                    }
                    cls = FindType(name) as ClassType;
                }
                else if (ts.SymbolType == SymbolType.Class)
                {
                    cls = ts as ClassType;
                }
                if (cls == null)
                {
                    Error("Class expected");
                    return null;
                }
                var fn = FindOrImport("_dynamicCast");
                var fc = new FunctionCallExpression(new SymbolExpression(fn));
                fc.Arguments.Add(left);
                var cname = module.AddString(cls.Name);
                unit.Strings.Add(cname);
                fc.Arguments.Add(new ConstantExpression(cname));
                left = new CastExpression(fc, cls.GetPointerType());
                SetContext(left);
            }

            return left;
        }

        private Expression ParseIncDec()
        {
            if (scanner.GetToken() == Token.Inc || scanner.GetToken() == Token.Dec)
            {
                var tkn = scanner.GetToken();
                BinOp op = scanner.GetToken() == Token.Inc ? BinOp.Add : BinOp.Sub;
                scanner.NextToken();
                var e = ParseSelector();
                if (e == null)
                    return null;
                AutoThis(ref e);

                if (CheckUnaryOperatorOverride(tkn, ref e))
                    return e;

                Expression one = new ConstantExpression(1);
                if (!e.DataType.IsPointer())
                    one = new CastExpression(one, e.DataType);
                var asgn = new AssignmentExpression(e,
                    new BinaryExpression(op, e, one));
                SetContext(asgn);
                return asgn;
            }
            else
            {
                var e = ParseSelector();
                if (e == null)
                    return null;
                AutoThis(ref e);
                if (scanner.GetToken() == Token.Inc || scanner.GetToken() == Token.Dec)
                {
                    var tkn = scanner.GetToken();
                    BinOp op = scanner.GetToken() == Token.Inc ? BinOp.Add : BinOp.Sub;
                    scanner.NextToken();

                    Expression dummy = new ConstantExpression(0);

                    if (CheckBinaryOperatorOverride(tkn, ref e, ref dummy))
                        return e;

                    Expression one = new ConstantExpression(1);
                    if (!e.DataType.IsPointer())
                        one = new CastExpression(one, e.DataType);
                    var asgn = new AssignmentExpression(e,
                        new BinaryExpression(op, e, one));
                    SetContext(asgn);
                    var pf = new PostFixExpression(e, asgn);
                    SetContext(pf);
                    return pf;
                }
                else
                {
                    return e;
                }
            }
        }

        private Expression ParsePointerOp()
        {
            if (scanner.GetToken() == Token.Mul)
            {
                scanner.NextToken();
                var e = ParsePointerOp();
                if (e == null)
                    return null;
                AutoThis(ref e);
                return new DerefExpression(e);
            }
            else if (scanner.GetToken() == Token.And)
            {
                scanner.NextToken();
                var e = ParseSelector();
                if (e == null)
                    return null;
                if (!(e is SymbolExpression || e is FieldExpression || e is IndexExpression))
                {
                    Error("Cannot get address of expression");
                    return null;
                }
                AutoThis(ref e);
                return new AddressOfExpression(e);
            }
            else
            {
                return ParseIncDec();
            }
        }

        private Expression ParseUnary()
        {
            if (scanner.GetToken() == Token.Sub)
            {
                scanner.NextToken();
                Expression e = ParseUnary();
                if (e == null)
                    return null;
                SetContext(e);
                if (CheckUnaryOperatorOverride(Token.Sub, ref e))
                    return e;
                return new UnaryExpression(UnaryOp.Neg, e);
            }
            else if (scanner.GetToken() == Token.Add)
            {
                scanner.NextToken();
                return ParseUnary();
            }
            else if (scanner.GetToken() == Token.Not)
            {
                scanner.NextToken();
                Expression e = ParseUnary();
                if (e == null)
                    return null;
                SetContext(e);
                if (CheckUnaryOperatorOverride(Token.Not, ref e))
                    return e;
                return new UnaryExpression(UnaryOp.Not, e);
            }
            else if (scanner.GetToken() == Token.LNot)
            {
                scanner.NextToken();
                Expression e = ParseUnary();
                if (e == null)
                    return null;
                SetContext(e);
                if (CheckUnaryOperatorOverride(Token.LNot, ref e))
                    return e;
                ForceCast(ref e, TypeSymbol.Bool);
                return new UnaryExpression(UnaryOp.LNot, e);
            }
            else
            {
                return ParsePointerOp();
            }
        }

        private Expression ParseFactor()
        {
            Expression left = ParseUnary();
            if (left == null)
                return null;
            SetContext(left);
            while (scanner.GetToken() == Token.Mul || scanner.GetToken() == Token.Div || scanner.GetToken() == Token.Mod)
            {
                var tkn = scanner.GetToken();
                BinOp op = scanner.GetToken() == Token.Mul ? BinOp.Mul : (scanner.GetToken() == Token.Div ? BinOp.Div : BinOp.Mod);
                scanner.NextToken();
                Expression right = ParseUnary();
                if (right == null)
                    return null;

                AutoCast(ref left, ref right);
                SetContext(right);

                if (CheckBinaryOperatorOverride(tkn, ref left, ref right))
                    continue;

                SetContext(right);
                //AutoCast(ref left, ref right);
                //SetContext(right);
                left = new BinaryExpression(op, left, right);
                SetContext(left);
            }
            return left;
        }

        private Expression ParseTerm()
        {
            Expression left = ParseFactor();
            if (left == null)
                return null;
            SetContext(left);
            while (scanner.GetToken() == Token.Add || scanner.GetToken() == Token.Sub)
            {
                var tkn = scanner.GetToken();
                BinOp op = scanner.GetToken() == Token.Add ? BinOp.Add : BinOp.Sub;
                scanner.NextToken();
                Expression right = ParseFactor();
                if (right == null)
                    return null;

                /*if (CheckBinaryOperatorOverride(tkn, ref left, ref right))
                    continue;

                if (left.DataType.IsPointer())
                {
                    if (!(right.DataType.IsInteger() && (op == BinOp.Add || op == BinOp.Sub))
                        && !(right.DataType.IsPointer() && left.DataType.ElementType == right.DataType.ElementType && op == BinOp.Sub))
                    {
                        Error("Invalid Operation");
                        return null;
                    }
                }
                else
                {
                    AutoCast(ref left, ref right);
                }
                SetContext(right);
                left = new BinaryExpression(op, left, right);
                SetContext(left);*/

                bool skip = false;
                if (!CheckBinaryOperatorOverride(tkn, ref left, ref right))
                {
                    TypeSymbol ldt = left.DataType;
                    TypeSymbol rdt = right.DataType;
                    if (ldt.IsPointer() && !CanConstructFrom(ldt, rdt) && !CanConstructFrom(rdt, ldt))
                    {
                        if (!(rdt.IsInteger() && (op == BinOp.Add || op == BinOp.Sub))
                            && !(rdt.IsPointer() && ldt.ElementType == rdt.ElementType && op == BinOp.Sub))
                        {
                            Error("Invalid Operation");
                            return null;
                        }
                    }
                    else
                    {
                        AutoCast(ref left, ref right);
                        if (CanConstructFrom(ldt, rdt) || CanConstructFrom(rdt, ldt))
                        {
                            if (CheckBinaryOperatorOverride(tkn, ref left, ref right)) // check again
                                skip = true;
                        }
                    }
                    SetContext(right);
                    if (!skip)
                        left = new BinaryExpression(op, left, right);
                    SetContext(left);
                }
            }
            return left;
        }

        private Expression ParseShift()
        {
            Expression left = ParseTerm();
            if (left == null)
                return null;
            SetContext(left);
            if (scanner.GetToken() == Token.Shl || scanner.GetToken() == Token.Shr)
            {
                var tkn = scanner.GetToken();
                BinOp op = scanner.GetToken() == Token.Shl ? BinOp.Shl : BinOp.Shr;
                scanner.NextToken();
                Expression right = ParseTerm();
                if (right == null)
                    return null;
                AutoCast(ref left, ref right);
                if (CheckBinaryOperatorOverride(tkn, ref left, ref right))
                    return left;
                //AutoCast(ref left, ref right);
                SetContext(right);
                left = new BinaryExpression(op, left, right);
                SetContext(left);
            }
            return left;
        }

        private Expression ParseMinMax()
        {
            Expression left = ParseShift();
            if (left == null)
                return null;
            SetContext(left);
            while (scanner.GetToken() == Token.Min || scanner.GetToken() == Token.Max)
            {
                var tkn = scanner.GetToken();
                BinOp op = tkn == Token.Min ? BinOp.Less : BinOp.Greater;
                scanner.NextToken();
                Expression right = ParseShift();
                if (right == null)
                    return null;
                AutoCast(ref left, ref right);
                if (CheckBinaryOperatorOverride(tkn, ref left, ref right))
                    return left;
                //AutoCast(ref left, ref right);
                SetContext(right);
                var cond = new BinaryExpression(op, left, right);
                left = new TernaryExpression(cond, left, right);
                SetContext(left);
            }
            return left;
        }

        private Expression ParseRelation()
        {
            Expression left = ParseMinMax();
            if (left == null)
                return null;
            SetContext(left);
            if (scanner.GetToken() >= Token.Equal && scanner.GetToken() <= Token.GEqual)
            {
                var tkn = scanner.GetToken();
                BinOp op = BinOp.Equal + (scanner.GetToken() - Token.Equal);
                scanner.NextToken();
                Expression right = ParseMinMax();
                if (right == null)
                    return null;
                AutoCast(ref left, ref right);
                if (CheckBinaryOperatorOverride(tkn, ref left, ref right))
                    return left;
                //AutoCast(ref left, ref right);
                SetContext(right);
                left = new BinaryExpression(op, left, right);
                SetContext(left);
            }
            return left;
        }

        private Expression ParseAnd()
        {
            Expression left = ParseRelation();
            if (left == null)
                return null;
            SetContext(left);
            while (scanner.GetToken() == Token.And)
            {
                scanner.NextToken();
                Expression right = ParseRelation();
                if (right == null)
                    return null;
                AutoCast(ref left, ref right);
                if (CheckBinaryOperatorOverride(Token.And, ref left, ref right))
                    return left;
                //AutoCast(ref left, ref right);
                SetContext(right);
                left = new BinaryExpression(BinOp.And, left, right);
                SetContext(left);
            }
            return left;
        }

        private Expression ParseXor()
        {
            Expression left = ParseAnd();
            if (left == null)
                return null;
            SetContext(left);
            while (scanner.GetToken() == Token.Xor)
            {
                scanner.NextToken();
                Expression right = ParseAnd();
                if (right == null)
                    return null;
                AutoCast(ref left, ref right);
                if (CheckBinaryOperatorOverride(Token.Xor, ref left, ref right))
                    return left;
                //AutoCast(ref left, ref right);
                SetContext(right);
                left = new BinaryExpression(BinOp.Xor, left, right);
                SetContext(left);
            }
            return left;
        }

        private Expression ParseOr()
        {
            Expression left = ParseXor();
            if (left == null)
                return null;
            SetContext(left);
            while (scanner.GetToken() == Token.Or)
            {
                scanner.NextToken();
                Expression right = ParseXor();
                if (right == null)
                    return null;
                AutoCast(ref left, ref right);
                if (CheckBinaryOperatorOverride(Token.Or, ref left, ref right))
                    return left;
                //AutoCast(ref left, ref right);
                SetContext(right);
                left = new BinaryExpression(BinOp.Or, left, right);
                SetContext(left);
            }
            return left;
        }

        private Expression ParseLogicAnd()
        {
            Expression left = ParseOr();
            if (left == null)
                return null;
            SetContext(left);
            while (scanner.GetToken() == Token.LAnd)
            {
                scanner.NextToken();
                Expression right = ParseOr();
                if (right == null)
                    return null;
                if (CheckBinaryOperatorOverride(Token.LAnd, ref left, ref right))
                    continue;
                ForceCast(ref left, TypeSymbol.Bool);
                ForceCast(ref right, TypeSymbol.Bool);
                SetContext(right);
                left = new BinaryExpression(BinOp.LAnd, left, right);
                SetContext(left);
            }
            return left;
        }

        private Expression ParseLogicOr()
        {
            Expression left = ParseLogicAnd();
            if (left == null)
                return null;
            SetContext(left);
            while (scanner.GetToken() == Token.LOr)
            {
                scanner.NextToken();
                Expression right = ParseLogicAnd();
                if (right == null)
                    return null;
                if (CheckBinaryOperatorOverride(Token.LOr, ref left, ref right))
                    continue;
                ForceCast(ref left, TypeSymbol.Bool);
                ForceCast(ref right, TypeSymbol.Bool);
                SetContext(right);
                left = new BinaryExpression(BinOp.LOr, left, right);
                SetContext(left);
            }
            return left;
        }

        private Expression ParseTernary()
        {
            Expression left = ParseLogicOr();
            if (left == null)
                return null;
            SetContext(left);
            if (scanner.GetToken() == Token.Question)
            {
                scanner.NextToken();
                Expression positive = ParseLogicOr();
                if (!Expect(Token.Colon))
                    return null;
                Expression negative = ParseLogicOr();
                ForceCast(ref left, TypeSymbol.Bool);
                AutoCast(ref positive, ref negative);
                SetContext(left);
                SetContext(positive);
                SetContext(negative);
                left = new TernaryExpression(left, positive, negative);
                SetContext(left);
            }
            return left;
        }

        private bool IsFunctionCall(Expression e)
        {
            Expression f = e;
            while (f is CastExpression ce)
                f = ce.Expression;
            return f is FunctionCallExpression;
        }

        private Expression ParseAssignment()
        {
            Expression left = ParseTernary();
            if (left == null)
                return null;
            SetContext(left);
            if (scanner.GetToken() == Token.Assign)
            {
                scanner.NextToken();

                if(left is SymbolExpression cs && cs.Symbol is ConstantValue)
                {
                    Error("Not an l-value");
                    return null;
                }

                Expression right = ParseAssignment();
                if (right == null)
                    return null;

                SymbolExpression se = left as SymbolExpression;
                if (se != null)
                {
                    LocalVariable lv = se.Symbol as LocalVariable;
                    if (lv != null && lv.Offset >= 0 && lv.DataType.IsClassRef() && (lv.Flags & SymbolFlags.ByRef) == 0 /*&& IsFunctionCall(right)*/)
                    {
                        // Assigning to a classref parameter would cause a leak.
                        Error("Invalid assignment. By-val parameters cannot take ownership.");
                        return null;
                    }
                }

                if (!(left.DataType.IsStruct() && right is ConstantExpression ce && ce.DataType == TypeSymbol.Int && ce.Value == 0))
                    ForceCast(ref right, left.DataType);
                right.line = scanner.GetLine();
                left = new AssignmentExpression(left, right);
                SetContext(left);
            }
            else if (scanner.GetToken() >= Token.AssignAdd && scanner.GetToken() <= Token.AssignShr)
            {
                var tkn = scanner.GetToken();

                if (left is SymbolExpression cs && cs.Symbol is ConstantValue)
                {
                    Error("Not an l-value");
                    return null;
                }

                BinOp op = BinOp.Add + (scanner.GetToken() - Token.AssignAdd);
                var tkn2 = Token.Add + (scanner.GetToken() - Token.AssignAdd);
                scanner.NextToken();
                Expression right = ParseTernary();
                if (right == null)
                    return null;
                /*var left2 = left;
                CheckBinaryOperatorOverride(tkn2, ref left2, ref right);
                if (!(left.DataType.IsPointer() && right.DataType.IsInteger()))
                    ForceCast(ref right, left.DataType);
                SetContext(left);
                left = new AssignmentExpression(left,
                            left2 != left ? left2 : new BinaryExpression(op, left, right));
                SetContext(left);*/
                TypeSymbol lt = left.DataType;
                TypeSymbol rt = right.DataType;
                if (!(lt.IsPointer() && rt.IsInteger()))
                    ForceCast(ref right, lt);
                SetContext(left);
                Expression binExp = null;
                Expression origLeft = left;
                if (CheckBinaryOperatorOverride(tkn2, ref left, ref right))
                    binExp = left;
                else
                    binExp = new BinaryExpression(op, left, right);
                left = new AssignmentExpression(origLeft, binExp);
                SetContext(left);
            }
            return left;
        }

        protected Expression ParseExpression()
        {
            return ParseAssignment();
        }
    }
}
