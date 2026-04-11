using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Jargon
{
    public class BaseParser
    {
        protected Scanner scanner;
        protected Module module;
        protected Function function;
        protected Symbol scope;
        protected ClassType _class = null;
        protected CompileUnit unit;
        protected string fileName;
        protected ICompilerErrorListener errorListener;
        public CompilerOptions CompilerOptions { get; set; }

        protected BaseParser(ICompilerErrorListener errorListener)
        {
            _ = TypeSymbol.Void;

            this.errorListener = errorListener;
        }

        protected struct Declaration
        {
            public TypeSymbol type;
            public string name;
            public bool ellipsis;
            public bool getter;
            public bool setter;
            public bool isByRef;
        }

        protected enum DeclaratorType
        {
            Pointer,
            Array,
            Function,
            Identifier,
            Reference,
        }

        protected struct Declarator
        {
            public DeclaratorType type;
            public int arraySize;
            public string name;
            public List<Declaration> pars;
            public bool getter;
            public bool setter;
            public bool op;
        }

        protected void StartParse(CompileUnit cu)
        {
            unit = cu;
            scanner = new Scanner(cu.Text, cu.Path);
            scanner.errorListener = this.errorListener;
            OnStartParse();
        }

        protected void EndParse()
        {
            OnEndParse();
        }

        protected virtual void OnStartParse()
        {
        }

        protected virtual void OnEndParse()
        {
        }

        protected void Error(string str)
        {
            //StackTrace st = new StackTrace(1, true);
            //Console.WriteLine(st.ToString());           
            errorListener.OnError(CompilerError.Error(str, fileName, scanner.GetLine(), scanner.GetColumn()));
        }

        protected void Warning(string str)
        {
            errorListener.OnError(CompilerError.Warning(str, fileName, scanner.GetLine(), scanner.GetColumn()));
        }

        protected void Info(string str)
        {
            errorListener.OnError(CompilerError.Info(str));
        }


        protected bool IsType()
        {
            if (scanner.GetToken() == Token.Ident)
            {
                Symbol type = module.Find(scanner.GetTokenString());

                if (type == null)
                {
                    //foreach (var u in module.Usings)
                    foreach (var u in unit.Usings)
                    {
                        type = u.Module.Find(scanner.GetTokenString());
                        if (type != null)
                            break;
                    }
                }

                if (type != null && type.IsType() && type.SymbolType != SymbolType.Function)
                {
                    return true;
                }
            }
            return false;
        }

        protected bool IsKeyword(string keyword)
        {
            if (scanner.GetToken() == Token.Keyword && scanner.GetTokenString() == keyword)
            {
                scanner.NextToken();
                return true;
            }

            return false;
        }

        public Function FindOrImport(string name)
        {
            Function fn = module.Find(name) as Function;
            if (fn != null)
            {
                if (unit != null && fn.Unit != unit)
                    unit.AddSymbolRef(fn);
                return fn;
            }

            foreach (var u in unit.Usings)
            {
                fn = u.Module.Find(name) as Function;
                if (fn != null)
                {
                    Function impFn = new Function(module, fn.Name, fn.ReturnType);
                    impFn.Flags = fn.Flags | SymbolFlags.External;
                    foreach (var extParam in fn.Parameters)
                    {
                        var impParam = new LocalVariable(impFn, extParam.Name, extParam.DataType, null);
                        impParam.Flags = extParam.Flags;
                        impParam.Offset = extParam.Offset;
                        impFn.Parameters.Add(impParam);
                    }
                    impFn.Verbatim = fn.Verbatim;
                    fn = impFn;
                    unit.AddSymbol(fn);
                    // TODO: unit.AddSymbolRef(fn);
                    break;
                }
            }

            return fn;
        }

        public void ForceCast(ref Expression e, TypeSymbol castType)
        {
            if (e.DataType.IsEqualTo(castType))
                return;

            if (castType.IsClassRef()
                && e.DataType.IsClassRef()
                && (e.DataType.ElementType as ClassType).IsClassOf(castType.ElementType as ClassType))
                return;

            if (castType.IsClassRef())
            {
                var cls = castType.ElementType as ClassType;
                var newName = cls.Name + "__new";
                var fact = FindOrImport(newName);
                if (fact != null && fact.Parameters.Count == 1)
                {
                    LocalVariable param = fact.Parameters[0];
                    TypeSymbol parDt = param.DataType;
                    TypeSymbol parElemDt = parDt.ElementType;
                    TypeSymbol eDt = e.DataType;
                    if (parDt.IsEqualTo(e.DataType) ||
                            (parDt.IsPointer() &&
                            eDt.IsArray() && parElemDt.IsEqualTo(eDt.ElementType)))
                    {
                        FunctionCallExpression fcall = new FunctionCallExpression(new SymbolExpression(fact));
                        fcall.Arguments.Add(e);
                        fcall.file = e.file;
                        fcall.line = e.line;
                        e = fcall;
                        return;
                    }
                }
            }

            var cast = new CastExpression(e, castType);
            cast.file = e.file;
            cast.line = e.line;
            e = cast;
        }

        protected Function GetFactory(ClassType t)
        {
            string name = t.Name + "__new";
            return FindOrImport(name);
        }

        protected bool CanConstructFrom(TypeSymbol t, TypeSymbol f)
        {
            if (t.IsClassRef())
            {
                ClassType cls = (ClassType)t.ElementType;
                Function fact = GetFactory(cls);
                if (fact != null && fact.Parameters.Count == 1)
                {
                    LocalVariable param = fact.Parameters[0];
                    return param.DataType.IsEqualTo(f);
                }
            }
            return false;
        }

        protected void AutoCast(ref Expression a, ref Expression b)
        {
            TypeSymbol at = a.DataType;
            TypeSymbol bt = b.DataType;

            if (at == bt)
                return;

            if (at.IsPointer() && bt.IsPointer() && !CanConstructFrom(at, bt) && !CanConstructFrom(bt, at))
                return;

            if (at.TypeCode < bt.TypeCode)
                ForceCast(ref a, bt);
            else
                ForceCast(ref b, at);
        }

        protected void AutoThis(ref Expression e)
        {
            if (e is SymbolExpression sr
                && !sr.Symbol.Flags.HasFlag(SymbolFlags.Static)
                && _class != null
                && _class.FindChild(sr.Symbol.Name) != null)
            {
                var symbolType = sr.Symbol.SymbolType;

                if (symbolType != SymbolType.Field
                    && symbolType != SymbolType.Method
                    && symbolType != SymbolType.Property)
                    return;

                if (symbolType == SymbolType.Method && sr.Symbol.Parent.Parent != module)
                    FindOrImport((sr.Symbol as MethodSymbol).DataType.Name);

                e = new FieldExpression(new SymbolExpression(function.Parameters[0]), sr.Symbol);
            }
        }

        protected static string TypeToFName(TypeSymbol type)
        {
            if (type.TypeCode == TypeCode.Pointer)
            {
                return "p" + TypeToFName(type.ElementType);
            }
            else if (type.TypeCode == TypeCode.Array)
            {
                return "a" + TypeToFName(type.ElementType);
            }
            else
            {
                return type.Name;
            }
        }

        protected static string TokenToFName(Token token)
        {
            switch (token)
            {
                case Token.Add:
                    return "add";
                case Token.Sub:
                    return "sub";
                case Token.Mul:
                    return "mul";
                case Token.Div:
                    return "div";
                case Token.Mod:
                    return "mod";
                case Token.Shl:
                    return "lshift";
                case Token.Shr:
                    return "rshift";
                case Token.Equal:
                    return "equal";
                case Token.NEqual:
                    return "nequal";
                case Token.Greater:
                    return "greater";
                case Token.Less:
                    return "less";
                case Token.GEqual:
                    return "gequal";
                case Token.LEqual:
                    return "lequal";
                case Token.And:
                    return "and";
                case Token.Xor:
                    return "xor";
                case Token.Or:
                    return "or";
                case Token.LAnd:
                    return "land";
                case Token.LOr:
                    return "lor";
                case Token.Not:
                    return "not";
                case Token.LNot:
                    return "lnot";
                case Token.Min:
                    return "min";
                case Token.Max:
                    return "max";
                /*case Token.Assign:
                    return "assign";
                case Token.AssignAdd:
                    return "assign_add";
                case Token.AssignSub:
                    return "assign_sub";
                case Token.AssignMul:
                    return "assign_mul";
                case Token.AssignDiv:
                    return "assign_div";
                case Token.AssignMod:
                    return "assign_mod";
                case Token.AssignAnd:
                    return "assign_and";
                case Token.AssignOr:
                    return "assign_or";
                case Token.AssignXor:
                    return "assign_xor";
                case Token.AssignShl:
                    return "assign_shl";
                case Token.AssignShr:
                    return "assign_shr";
                case Token.AssignLAnd:
                    return "assign_land";
                case Token.AssignLOr:
                    return "assign_lor";*/
                default:
                    return "";
            }
        }

        protected bool Expect(Token token)
        {
            Token tok = scanner.GetToken();
            if (tok != token)
            {
                switch (token)
                {
                    case Token.RBracket: Error("] expected"); break;
                    default: Error(token.ToString() + " expected"); break;
                }
                return false;
            }
            else
            {
                scanner.NextToken();
                return true;
            }
        }

        protected bool CheckBinaryOperatorOverride(Token token, ref Expression left, ref Expression right)
        {
            string fname = "operator_" + TokenToFName(token) + "_"
                + TypeToFName(left.DataType) + "_"
                + TypeToFName(right.DataType);

            var opovr = FindOrImport(fname);

            if (opovr != null)
            {
                var fc = new FunctionCallExpression(new SymbolExpression(opovr));
                fc.Arguments.Add(left);
                fc.Arguments.Add(right);
                left = fc;
                return true;
            }

            fname = "operator_" + TokenToFName(token) + "_p"
                + TypeToFName(left.DataType) + "_p"
                + TypeToFName(right.DataType);

            opovr = FindOrImport(fname);

            if (opovr != null)
            {
                var fc = new FunctionCallExpression(new SymbolExpression(opovr));
                fc.Arguments.Add(new AddressOfExpression(left));
                fc.Arguments.Add(new AddressOfExpression(right));
                left = fc;
                return true;
            }

            return false;
        }

        protected bool CheckUnaryOperatorOverride(Token token, ref Expression e)
        {
            string fname = "operator_" + TokenToFName(token) + "_"
                + TypeToFName(e.DataType);

            var opovr = FindOrImport(fname);

            if (opovr != null)
            {
                var fc = new FunctionCallExpression(new SymbolExpression(opovr));
                fc.Arguments.Add(e);
                e = fc;
                return true;
            }

            fname = "operator_" + TokenToFName(token) + "_p"
                + TypeToFName(e.DataType);

            opovr = FindOrImport(fname);

            if (opovr != null)
            {
                var fc = new FunctionCallExpression(new SymbolExpression(opovr));
                fc.Arguments.Add(new AddressOfExpression(e));
                e = fc;
                return true;
            }

            return false;
        }

        protected TypeSymbol FindType(string name)
        {
            Symbol type = module.Find(name);

            if (type == null)
            {
                //foreach (var u in module.Usings)
                foreach (var u in unit.Usings)
                {
                    type = u.Module.Find(name);
                }
            }

            if (type != null && type.IsType() && type.SymbolType != SymbolType.Function)
            {
                //scanner.NextToken();

                if (type is TypeSymbol ts && (ts.IsStruct() || ts.IsClass()) && ts.Unit != unit)
                {
                    TypeSymbol atype = ts;
                    while (atype != null)
                    {
                        if(atype.Unit != unit)
                            unit.AddSymbolRef(atype);
                        if (atype is ClassType ct)
                            atype = ct.BaseClass;
                        else
                            atype = null;
                    }
                }

                /*if(!SinglePass && type is TypeSymbol ts && ts.IsStruct() && ts.Parent != module)
                {
                    var imp = new StructType(module, ts.Name);
                    foreach(var tf in ts.Children)
                    {
                        var nf = new FieldSymbol(imp, tf.Name, tf.DataType);
                        nf.Index = (tf as FieldSymbol).Index;
                        nf.Offset = (tf as FieldSymbol).Offset;
                    }
                }*/

                return type as TypeSymbol;
            }

            return null;
        }

        protected TypeSymbol ParseType()
        {
            if (scanner.GetToken() == Token.Ident)
            {
                TypeSymbol t = FindType(scanner.GetTokenString());
                if (t != null)
                    scanner.NextToken();
                return t;
            }
            return null;
        }

        protected bool ParseDeclaration(ref Declaration item, bool nameRequired, TypeSymbol initType, bool noName = false, bool noCheck = false)
        {
            if (initType != null)
            {
                TypeSymbol t = initType;
                /*while (t.ElementType != null)
                    t = t.ElementType;*/
                item.type = t;
            }
            else
            {
                item.type = ParseType();
                if (item.type == null)
                {
                    Error("Type expected");
                    return false;
                }

                if (item.type.SymbolType == SymbolType.Template)
                {
                    var templateArgs = new List<TypeSymbol>();
                    if (!Expect(Token.Less))
                        return false;
                    while (scanner.GetToken() != Token.EOF)
                    {
                        if (IsType())
                        {
                            Declaration tt = new Declaration();
                            ParseDeclaration(ref tt, false, null, true, true);
                            TypeSymbol t = tt.type;
                            templateArgs.Add(t);
                        }
                        else
                        {
                            Error("Type expected");
                            return false;
                        }
                        if (scanner.GetToken() == Token.Comma)
                            scanner.NextToken();
                        else if (scanner.GetToken() == Token.Greater)
                            break;
                    }
                    if (!Expect(Token.Greater))
                        return false;

                    var tname = item.type.Name;
                    foreach (var t in templateArgs)
                    {
                        tname += "_" + TypeToFName(t);
                    }

                    var existing = FindType(tname) as ClassType;
                    if (existing != null)
                    {
                        item.type = existing;
                    }
                    else
                    {
                        var src = ((Template)item.type).Source;

                        for (int i = 0; i < templateArgs.Count; i++)
                        {
                            src = Regex.Replace(src,
                                "\\b" + ((Template)item.type).TemplateParams[i] + "\\b",
                                templateArgs[i].Name);
                        }

                        var text = "class " + tname + src;

                        if(!Directory.Exists("jrt"))
                        {
                            Directory.CreateDirectory("jrt");
                        }

                        File.WriteAllText("jrt\\" + tname + ".jrt", text);
                        CompileUnit cu = new CompileUnit("jrt\\" + tname + ".jrt");
                        //File.Delete(tname + ".cm");

                        Parser1 p1 = new Parser1(module, errorListener);
                        p1.CompilerOptions = this.CompilerOptions;
                        if (!p1.ParseUnit(cu, unit))
                            return false;
                        Parser2 p2 = new Parser2(module, errorListener);
                        p2.CompilerOptions = this.CompilerOptions;
                        if (!p2.ParseUnit(cu, unit))
                            return false;
                        Parser3 p3 = new Parser3(module, errorListener);
                        p3.CompilerOptions = this.CompilerOptions;
                        if (!p3.ParseUnit(cu, unit))
                            return false;
                        Parser4 p4 = new Parser4(module, errorListener);
                        p4.CompilerOptions = this.CompilerOptions;
                        if (!p4.ParseUnit(cu, unit))
                            return false;

                        existing = module.Find(tname) as ClassType;

                        if (existing == null)
                            return false;

                        item.type = existing;
                    }
                }
            }

            List<Declarator> declarators = new List<Declarator>();

            if (!ParseDeclarator(declarators, nameRequired, noName))
                return false;

            item.name = declarators[0].name;
            item.getter = declarators[0].getter;
            item.setter = declarators[0].setter;

            if (declarators[0].op)
            {
                for (int i = declarators.Count - 1; i >= 1; i--)
                {
                    var td = declarators[i];
                    if (td.type == DeclaratorType.Function)
                    {
                        foreach (var a in td.pars)
                        {
                            item.name += "_";
                            item.name += TypeToFName(a.type);
                        }
                        break;
                    }
                }
            }

            ////

            //if(item.type.IsClass() && (declarators.Count < 2 || declarators[1].type != DeclaratorType.Pointer))
            if (item.type.IsClass() && declarators.Last().type != DeclaratorType.Pointer)
            {
                item.type = item.type.GetPointerType();
            }

            ////
            
            Function originalFn = null;

            for (int i = declarators.Count - 1; i >= 1; i--)
            {
                var td = declarators[i];

                switch (td.type)
                {
                    case DeclaratorType.Pointer:
                    case DeclaratorType.Reference:
                        if (item.type is Function)
                            item.type.Name = "";
                        item.type = item.type.GetPointerType();
                        item.isByRef = td.type == DeclaratorType.Reference;
                        break;
                    case DeclaratorType.Array:
                        item.type = item.type.GetArrayType(td.arraySize);
                        break;
                    case DeclaratorType.Function:
                        {
                            originalFn = module.FindChild(item.name) as Function;
                            Function fptrtype = new Function(null, item.name, item.type);
                            int ii = 0;
                            foreach (var a in td.pars)
                            {
                                if (a.ellipsis)
                                {
                                    if (ii != td.pars.Count - 1)
                                    {
                                        Error("')' expected");
                                        return false;
                                    }
                                    fptrtype.Flags |= SymbolFlags.Variadic;
                                }
                                else
                                {
                                    foreach (var p in fptrtype.Parameters)
                                    {
                                        if (a.name != "" && p.Name == a.name)
                                        {
                                            Error("Duplicate definition");
                                            return false;
                                        }
                                    }
                                    string anonName = "p" + ii;
                                    LocalVariable lv = new LocalVariable(fptrtype, a.name == "" ? anonName : a.name, a.type, null);
                                    if (a.isByRef)
                                        lv.Flags |= SymbolFlags.ByRef;
                                    fptrtype.Parameters.Add(lv);
                                    lv.Offset = fptrtype.Parameters.Count;
                                }
                                ii++;
                            }
                            item.type = fptrtype;
                        }
                        break;
                }
            }

            if (item.type is Function fn && !noCheck)
            {
                var orig = originalFn;
                if (orig != null)
                {
                    if (!orig.IsEqualTo(item.type))
                    {
                        Error("Invalid redefinition of '" + orig.Name + "'");
                        return false;
                    }
                    item.type = orig;
                }
            }

            return true;
        }

        private bool ParseDeclarator(List<Declarator> declarators, bool nameRequired, bool noName)
        {
            if (scanner.GetToken() == Token.Mul)
            {
                scanner.NextToken();

                Declarator td = new Declarator();
                td.type = DeclaratorType.Pointer;

                /*if (isKeyword("const"))
                {
                    td._const = true;
                    scanner->NextToken();
                }*/

                if (!ParseDeclarator(declarators, nameRequired, noName))
                    return false;

                declarators.Add(td);

                return true;
            }
            else if (scanner.GetToken() == Token.Reference)
            {
                scanner.NextToken();

                Declarator td = new Declarator();
                td.type = DeclaratorType.Reference;
                if (!ParseDeclarator(declarators, nameRequired, noName))
                    return false;
                declarators.Add(td);
                return true;
            }
            else
            {
                return ParseDeclaratorSelector(declarators, nameRequired, noName);
            }
        }

        private bool ParseDeclaratorSelector(List<Declarator> declarators, bool nameRequired, bool noName)
        {
            if (!ParseDeclaratorPrimary(declarators, nameRequired, noName))
                return false;

            while (scanner.GetToken() == Token.LBracket || scanner.GetToken() == Token.LPar)
            {
                if (scanner.GetToken() == Token.LBracket)
                {
                    scanner.NextToken();
                    if (scanner.GetToken() != Token.Int)
                    {
                        Error("Number expected");
                        return false;
                    }
                    int size = scanner.GetTokenInt();
                    scanner.NextToken();
                    if (!Expect(Token.RBracket))
                    {
                        return false;
                    }
                    Declarator td = new Declarator();
                    td.type = DeclaratorType.Array;
                    td.arraySize = size;
                    declarators.Add(td);
                }
                else if (scanner.GetToken() == Token.LPar)
                {
                    scanner.NextToken();

                    Declarator td = new Declarator();
                    td.type = DeclaratorType.Function;
                    td.pars = new List<Declaration>();

                    while (scanner.GetToken() != Token.RPar && scanner.GetToken() != Token.EOF)
                    {
                        Declaration aitem = new Declaration();
                        if (scanner.GetToken() == Token.Ellipsis)
                        {
                            aitem.ellipsis = true;
                            scanner.NextToken();
                        }
                        else if (!ParseDeclaration(ref aitem, false, null))
                        {
                            return false;
                        }

                        if (aitem.type != null && aitem.type.IsArray())
                        {
                            aitem.isByRef = true;
                            aitem.type = aitem.type.GetPointerType();
                        }

                        td.pars.Add(aitem);

                        if (scanner.GetToken() == Token.Comma)
                            scanner.NextToken();
                        else if (scanner.GetToken() != Token.RPar)
                        {
                            Error(") Expected");
                            return false;
                        }
                    }

                    if (!Expect(Token.RPar))
                        return false;

                    declarators.Add(td);
                }
            }
            return true;
        }

        private bool ParseDeclaratorPrimary(List<Declarator> declarators, bool nameRequired, bool noName)
        {
            if (scanner.GetToken() == Token.LPar)
            {
                scanner.NextToken();
                if (!ParseDeclarator(declarators, nameRequired, noName))
                    return false;
                if (!Expect(Token.RPar))
                    return false;
                return true;
            }
            else
            {
                bool getter = false;
                bool setter = false;

                if (IsKeyword("get"))
                {
                    getter = true;
                }
                else if (IsKeyword("set"))
                {
                    setter = true;
                }
                else if (IsKeyword("operator"))
                {
                    string opName = TokenToFName(scanner.GetToken());
                    if (opName == "")
                    {
                        Error("Invalid operator");
                        return false;
                    }
                    Declarator td = new Declarator();
                    td.type = DeclaratorType.Identifier;
                    td.name = "operator_" + opName;
                    td.op = true;
                    declarators.Add(td);
                    scanner.NextToken();
                    return true;
                }

                if (scanner.GetToken() == Token.Ident)
                {
                    if (noName)
                    {
                        Error("Syntax Error");
                        return false;
                    }
                    Declarator td = new Declarator();
                    td.type = DeclaratorType.Identifier;
                    td.name = scanner.GetTokenString();
                    td.setter = setter;
                    td.getter = getter;
                    declarators.Add(td);
                    scanner.NextToken();
                    return true;
                }
                else if (!nameRequired)
                {
                    Declarator td = new Declarator();
                    td.type = DeclaratorType.Identifier;
                    td.name = "";
                    declarators.Add(td);
                    return true;
                }
                else
                {
                    Error("Identifier expected");
                    return false;
                }
            }
        }
    }
}
