using System;
using System.Diagnostics;
using System.IO;

namespace Jargon
{
    public class Parser2 : StageParser
    {
        public Parser2(Module module, ICompilerErrorListener errorListener) : base(module, errorListener) { }

        protected override void OnStartParse()
        {
            if (unit.Text.Contains("//@!line"))
                return;

            CreateStaticInitFunctions();
        }

        private void CreateStaticInitFunctions()
        {
            //Console.WriteLine($"Creating init function for {unit.FileName}");

            var static_init = new Function(module, Path.GetFileNameWithoutExtension(unit.FileName) + "_static_init", TypeSymbol.Void);
            static_init.Body = new StatementBlock();
            static_init.Flags |= SymbolFlags.Internal;
            static_init.Flags |= SymbolFlags.NoDebug;
            unit.AddSymbol(static_init);

            var static_deinit = new Function(module, Path.GetFileNameWithoutExtension(unit.FileName) + "_static_deinit", TypeSymbol.Void);
            static_deinit.Body = new StatementBlock();
            static_deinit.Flags |= SymbolFlags.Internal;
            static_deinit.Flags |= SymbolFlags.NoDebug;
            unit.AddSymbol(static_deinit);
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

        static int GetAlignment(TypeSymbol type)
        {
            if (type.IsStruct())
                return (type as StructType).Alignment;
            else if (type.IsArray())
                return GetAlignment(type.ElementType);
            else
                return type.Size;   // otherwise align to the type size
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
            var st = module.Find(name) as StructType;
            Debug.Assert(st != null);
            if (scanner.GetToken() == Token.LBrace)
            {
                scanner.NextToken();
                while (scanner.GetToken() != Token.RBrace && scanner.GetToken() != Token.EOF)
                {
                    Declaration decl = new Declaration();

                    if (!ParseDeclaration(ref decl, true, null))
                        return false;

                    if (decl.isByRef)
                    {
                        Error("Reference not allowed here");
                        return false;
                    }

                    var fs = new FieldSymbol(st, decl.name, decl.type);
                    fs.Index = st.Children.Count - 1;
                    fs.Unit = unit;
                    if (fs.DataType.IsPointer() && fs.DataType.ElementType.IsClass())
                        fs.Flags |= SymbolFlags.Weak;   // references in a struct are always weak
                                                        // as structs have no finalizers

                    int alignment = GetAlignment(decl.type);
                    st.Alignment = st.Alignment > alignment ? st.Alignment : alignment;

                    if (isUnion)
                    {
                        fs.Offset = 0;
                        fs.Index = 0;
                        st.SetSize(Math.Max(st.Size, decl.type.Size));
                    }
                    else
                    {
                        //int offs = ((st.Size + decl.type.Size - 1) / decl.type.Size) * decl.type.Size;
                        int offs = ((st.Size + alignment - 1) / alignment) * alignment;
                        fs.Offset = offs;
                        st.SetSize(offs + decl.type.Size);
                    }

                    if (!Expect(Token.Semicolon))
                        return false;
                }

                if (!Expect(Token.RBrace))
                    return false;

                // Final struct size is rounded up to the alignment
                int size = st.Size;
                if (size != 0 && st.Alignment != 0)
                {
                    size = ((size + st.Alignment - 1) / st.Alignment) * st.Alignment;
                    st.SetSize(size);
                }
                return true;
            }
            else
            {
                return Expect(Token.Semicolon);
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
            ClassType cls = module.Find(name) as ClassType;
            Debug.Assert(cls != null);

            if (scanner.GetToken() == Token.Colon)
            {
                scanner.NextToken();
                ClassType baseClass = ParseType() as ClassType;
                if (baseClass == null)
                {
                    Error("Class expected");
                    return false;
                }
                cls.BaseClass = baseClass;
                cls.SetSize(baseClass.Size);
                cls.FieldCount = baseClass.FieldCount;
                cls.VirtualMethods.AddRange(baseClass.VirtualMethods);
            }
            else
            {
                if (cls.Name != "object")
                {
                    ClassType baseClass = FindType("object") as ClassType;
                    if (baseClass == null)
                    {
                        Error("Base 'object' class not found. Missing 'using jargon;'? ");
                        return false;
                    }
                    Debug.Assert(baseClass != null);
                    cls.BaseClass = baseClass;
                    cls.SetSize(baseClass.Size);
                    cls.FieldCount = baseClass.FieldCount;
                    cls.VirtualMethods.AddRange(baseClass.VirtualMethods);
                }
                else
                {
                    var vtable = new FieldSymbol(cls, "vtable", TypeSymbol.Void.GetPointerType().GetPointerType());
                    vtable.Index = cls.FieldCount;
                    cls.FieldCount++;
                    vtable.Offset = 0;
                    cls.SetSize(8);
                }
            }

            _class = cls;

            var factory = cls.Factory;

            if (scanner.GetToken() == Token.LBrace)
            {
                scanner.NextToken();
                while (scanner.GetToken() != Token.RBrace && scanner.GetToken() != Token.EOF)
                {
                    bool _virtual = false;
                    bool _static = false;
                    bool _weak = false;
                    bool _const = false;

                    if (IsKeyword("const"))
                    {
                        _const = true;
                    }
                    else
                    {
                        if (IsKeyword("virtual"))
                            _virtual = true;
                        else if (IsKeyword("static"))
                            _static = true;

                        if (IsKeyword("weak"))
                            _weak = true;
                    }

                    Declaration decl = new Declaration();

                    if (!ParseDeclaration(ref decl, true, null, false, true))
                    {
                        _class = null;
                        return false;
                    }

                    if (decl.isByRef)
                    {
                        Error("Reference not allowed here");
                        _class = null;
                        return false;
                    }

                    if (decl.type is Function fn)
                    {
                        if (_weak)
                        {
                            Error("Methods cannot be weak");
                            return false;
                        }

                        if (_const)
                        {
                            Error("Methods cannot be const");
                            return false;
                        }

                        fn.Line = scanner.GetLine();
                        if (decl.getter)
                        {
                            fn.Name = "get_" + decl.name;
                        }
                        else if (decl.setter)
                        {
                            fn.Name = "set_" + decl.name;
                        }
                        else if (fn.Name == "constructor")
                        {
                            foreach (var p in fn.Parameters)
                            {
                                var p2 = new LocalVariable(factory, p.Name, p.DataType, null);
                                p2.Offset = p.Offset;
                                factory.Parameters.Add(p2);
                            }
                        }

                        var fname = fn.Name;

                        if (!_static)
                        {
                            var _this = new LocalVariable(fn, "this", cls.GetPointerType(), null);
                            foreach (var p in fn.Parameters)
                                p.Offset++;
                            _this.Offset = 1;
                            fn.Parameters.Insert(0, _this);
                        }

                        var ms = new MethodSymbol(cls, fname, fn);

                        if (decl.getter)
                        {
                            var p = cls.FindChild(decl.name) as PropertySymbol;
                            if (p == null)
                            {
                                p = new PropertySymbol(cls, decl.name, fn.ReturnType);
                            }
                            p.Getter = ms;
                        }
                        else if (decl.setter)
                        {
                            var p = cls.FindChild(decl.name) as PropertySymbol;
                            if (p == null)
                            {
                                p = new PropertySymbol(cls, decl.name, fn.Parameters[1].DataType);
                            }
                            p.Setter = ms;
                        }

                        if (_static)
                        {
                            ms.Flags |= SymbolFlags.Static;
                            fn.Flags |= SymbolFlags.Static;
                        }

                        if (_virtual)
                        {
                            ms.Flags |= SymbolFlags.Virtual;
                            ms.VSlot = cls.VirtualMethods.Count;
                            cls.VirtualMethods.Add(ms);
                        }
                        else if (cls.BaseClass != null && !_static)
                        {
                            var vm = cls.BaseClass.FindChild(fn.Name) as MethodSymbol;
                            if (vm != null && vm.Flags.HasFlag(SymbolFlags.Virtual))
                            {
                                ms.VSlot = vm.VSlot;
                                cls.VirtualMethods[ms.VSlot] = ms;
                                ms.Flags |= SymbolFlags.Virtual;
                            }
                        }

                        if (_static && fn.Parameters.Count > 0 && fn.Parameters[0].Name == "this")
                        {
                            // extension method
                            var typeToExtend = fn.Parameters[0].DataType;
                            if (typeToExtend.IsPointer() && typeToExtend.ElementType.IsClass())
                                typeToExtend = typeToExtend.ElementType;
                            string extName = "ext_" + TypeToFName(typeToExtend);
                            ClassType extCls = FindType(extName) as ClassType;
                            if (extCls == null)
                                extCls = new ClassType(module, extName);
                            //var extensionMethod = new MethodSymbol(typeToExtend, fn.Name, fn);
                            var extensionMethod = new MethodSymbol(extCls, fn.Name, fn);
                            fn.Name = "ext_" + fn.Name;
                        }

                        fn.Name = cls.Name + "__" + fn.Name;
                        module.AddChild(fn);
                        unit.AddSymbol(fn);

                        if (scanner.GetToken() == Token.LBrace)
                        {
                            SkipBraces();
                            if (!Expect(Token.RBrace))
                                return false;
                        }
                        else if (scanner.GetToken() == Token.DoubleArrow)
                        {
                            scanner.NextToken();
                            while (scanner.GetToken() != Token.Semicolon && scanner.GetToken() != Token.EOF)
                                scanner.NextToken();
                            if (!Expect(Token.Semicolon))
                                return false;
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
                            if(!Expect(Token.Assign))
                                return false;
                            Expression value = ParseExpression();
                            if (value == null)
                                return false;
                            if (!(value is ConstantExpression))
                            {
                                Error("Constant expression expected");
                                return false;
                            }
                            ForceCast(ref value, decl.type);
                            var cf = new ConstantValue(cls, decl.name, value);
                            cf.Flags |= SymbolFlags.Static;
                        }
                        else
                        {
                            var fs = new FieldSymbol(cls, decl.name, decl.type);
                            if (!_static)
                            {
                                fs.Index = cls.FieldCount;
                                cls.FieldCount++;
                                int offs = ((cls.Size + decl.type.Size - 1) / decl.type.Size) * decl.type.Size;
                                fs.Offset = offs;
                                cls.SetSize(offs + decl.type.Size);
                            }
                            else
                            {
                                fs.Flags |= SymbolFlags.Static;
                            }

                            if (_weak)
                                fs.Flags |= SymbolFlags.Weak;

                            while (scanner.GetToken() != Token.Semicolon && scanner.GetToken() != Token.EOF)
                                scanner.NextToken();                            
                        }
                        if (!Expect(Token.Semicolon))
                        {
                            _class = null;
                            return false;
                        }
                    }
                }

                if (!Expect(Token.RBrace))
                {
                    _class = null;
                    return false;
                }

                if (cls.Name != "object")
                {
                    // Add default constructor if not present
                    var ownCtorMethod = cls.Find("constructor") as MethodSymbol;
                    if (ownCtorMethod == null)
                    {
                        if (!CreateConstructor(cls))
                            return false;
                    }

                    // Add virtual getClassName() method
                    if (!CreateGetClassNameMethod(cls))
                        return false;

                    // Add finalizer
                    if (!CreateFinalizer(cls))
                        return false;

                }

                // Create virtual table global var
                if (!CreateVirtualTable(cls))
                    return false;

                // Add static fields initializations to unit static init function
                /*if (!AddStaticFieldsInitializations(cls))
                    return false;*/

                // Create instance fields init method
                if (!CreateInstanceInitMethod(cls))
                    return false;

                _class = null;
                return true;
            }
            else
            {
                return Expect(Token.Semicolon);
            }
        }

        bool CreateConstructor(ClassType cls)
        {
            Function ownCtor = new Function(module, cls.Name + "__constructor", cls.GetPointerType());
            LocalVariable ownCtorThis = new LocalVariable(ownCtor, "this", cls.GetPointerType(), null);
            ownCtorThis.Offset = 1;
            ownCtor.Parameters.Add(ownCtorThis);
            ownCtor.Body = new StatementBlock();
            unit.AddSymbol(ownCtor);
            var baseCtorMethod = cls.BaseClass.FindChild("constructor") as MethodSymbol;
            Debug.Assert(baseCtorMethod != null);
            var baseCtorFn = baseCtorMethod.DataType as Function;
            Debug.Assert(baseCtorFn != null);
            FindOrImport(baseCtorFn.Name);
            if (baseCtorFn.Unit != unit)
                unit.AddSymbolRef(baseCtorFn);
            if (baseCtorFn.Parameters.Count > 1)
            {
                Error("No default constructor for " + cls.BaseClass.Name);
                return false;
            }
            var baseFCall = new FunctionCallExpression(new SymbolExpression(baseCtorFn));
            baseFCall.Arguments.Add(new SymbolExpression(ownCtorThis));

            var lv = new LocalVariable(ownCtor.Body.Scope, "__temp" + scanner.GetLine(), ownCtorThis.DataType, null);
            lv.Offset = -(++ownCtor.LocalsCount);
            ownCtor.Locals.Add(lv);

            var assign = new AssignmentExpression(new SymbolExpression(lv), baseFCall);
            ownCtor.Body.Statements.Add(new ExpressionStatement(assign));

            ownCtor.Body.Statements.Add(new ReturnStatement(new SymbolExpression(lv)));

            ownCtor.Flags |= SymbolFlags.NoDebug;

            return true;
        }

        private bool CreateGetClassNameMethod(ClassType cls)
        {
            if (cls.Name.EndsWith("Extensions"))
            {
                return true;    // dont add getClassName to extension classes
            }

            Function getClassName = new Function(module, cls.Name + "__getClassName", TypeSymbol.Byte.GetPointerType());
            getClassName.Flags |= SymbolFlags.NoDebug;
            LocalVariable getClassNameThis = new LocalVariable(getClassName, "this", cls.GetPointerType(), null);
            getClassNameThis.Offset = 1;
            getClassName.Parameters.Add(getClassNameThis);
            string clsName = module.AddString(cls.Name);
            unit.Strings.Add(clsName);
            unit.AddSymbol(getClassName);
            getClassName.Body = new StatementBlock();
            getClassName.Body.Statements.Add(new ReturnStatement(new ConstantExpression(clsName)));

            var getClassNameMethod = new MethodSymbol(cls, "getClassName", getClassName);
            getClassNameMethod.Flags |= SymbolFlags.Virtual;

            if (cls.BaseClass == null)
            {
                getClassNameMethod.VSlot = cls.VirtualMethods.Count;
                cls.VirtualMethods.Add(getClassNameMethod);
            }
            else
            {
                var getBaseClassName = cls.BaseClass.FindChild("getClassName") as MethodSymbol;
                Debug.Assert(getBaseClassName != null);
                getClassNameMethod.VSlot = getBaseClassName.VSlot;
                cls.VirtualMethods[getClassNameMethod.VSlot] = getClassNameMethod;
            }
            return true;
        }

        private bool CreateFinalizer(ClassType cls)
        {
            var finalize = new Function(module, cls.Name + "__finalize", cls.GetPointerType());
            unit.AddSymbol(finalize);
            var finalizeThis = new LocalVariable(finalize, "this", cls.GetPointerType(), null);
            finalizeThis.Offset = 1;
            finalize.Parameters.Add(finalizeThis);
            finalize.Body = new StatementBlock();
            finalize.Flags |= SymbolFlags.NoDebug;
            foreach (var c in cls.Children)
            {
                if (c is FieldSymbol fs && !fs.Flags.HasFlag(SymbolFlags.Static)
                    && fs.DataType.IsPointer() && fs.DataType.ElementType.IsClass())
                {
                    if (fs.Flags.HasFlag(SymbolFlags.Weak))
                        continue;

                    var fe = new FieldExpression(new SymbolExpression(finalizeThis), fs);
                    var cls2 = fs.DataType.ElementType as ClassType;
                    var rel = FindType("object").FindChild("release") as MethodSymbol;
                    Debug.Assert(rel != null);
                    var fc = new FunctionCallExpression(new SymbolExpression(rel.DataType));
                    fc.Arguments.Add(fe);
                    finalize.Body.Statements.Add(new ExpressionStatement(fc));
                }
            }

            var baseFinalizeMethod = cls.BaseClass.FindChild("finalize") as MethodSymbol;
            Debug.Assert(baseFinalizeMethod != null);
            var baseFinalize = baseFinalizeMethod.DataType as Function;
            Debug.Assert(baseFinalize != null);
            FindOrImport(baseFinalize.Name);
            var baseCall = new FunctionCallExpression(new SymbolExpression(baseFinalize));
            baseCall.Arguments.Add(new SymbolExpression(finalizeThis));
            finalize.Body.Statements.Add(new ExpressionStatement(baseCall));

            var finBase = cls.FindChild("finalize") as MethodSymbol;
            Debug.Assert(finBase != null);
            var vslot = finBase.VSlot;
            Debug.Assert(vslot != -1);
            var finalizeMethod = new MethodSymbol(cls, "finalize", finalize.DataType as Function);
            finalizeMethod.VSlot = vslot;
            finalizeMethod.Flags |= SymbolFlags.Virtual;
            cls.VirtualMethods[vslot] = finalizeMethod;

            return true;
        }

        private bool CreateVirtualTable(ClassType cls)
        {
            //Console.WriteLine($"Creating virtual table for {cls.Name} in {unit.FileName}");

            if (cls.VirtualMethods.Count == 0)
                cls.VirtualMethods.Add(null);   // llvm doesnt like empty arrays

            var vptr = TypeSymbol.Void.GetPointerType();
            var vpar = vptr.GetArrayType(cls.VirtualMethods.Count);
            var vtab = new GlobalVariable(module, cls.Name + "__vtable", vpar, null);
            unit.AddSymbol(vtab);
            var vini = new InitList(vpar);
            foreach (var v in cls.VirtualMethods)
            {
                if (v == null)
                    break;
                FindOrImport(v.DataType.Name);
                vini.Expressions.Add(new AddressOfExpression(new SymbolExpression(v.DataType)));
            }
            // Add to unit static init function
            var sinitBody = (module.FindChild(Path.GetFileNameWithoutExtension(unit.FileName) + "_static_init") as Function).Body;
            StaticAssign(sinitBody,
                new SymbolExpression(vtab),
                vini, 0, null);

            return true;
        }

        /*private bool AddStaticFieldsInitializations(ClassType cls)
        {
            var sinitBody = (module.FindChild(Path.GetFileNameWithoutExtension(unit.FileName) + "_static_init") as Function).Body;
            foreach (var c in cls.Children)
            {
                if (c is FieldSymbol fs && fs.Flags.HasFlag(SymbolFlags.Static) && fs.InitialValue != null)
                {
                    var se = new SymbolExpression(fs);
                    var ae = new AssignmentExpression(se, new CastExpression(fs.InitialValue, fs.DataType));
                    sinitBody.Statements.Add(new ExpressionStatement(ae));
                }
            }
            return true;
        }*/

        private bool CreateInstanceInitMethod(ClassType cls)
        {
            var init = new Function(module, cls.Name + "__init", cls.GetPointerType());
            unit.AddSymbol(init);
            var iniThis = new LocalVariable(init, "this", cls.GetPointerType(), null);
            iniThis.Offset = 1;
            init.Parameters.Add(iniThis);
            init.Body = new StatementBlock();
            init.Flags |= SymbolFlags.NoDebug;
            /*foreach (var c in cls.Children)
            {
                if (c is FieldSymbol fs && !fs.Flags.HasFlag(SymbolFlags.Static) && fs.InitialValue != null)
                {
                    var fe = new FieldExpression(new SymbolExpression(iniThis), fs);
                    var ae = new AssignmentExpression(fe, new CastExpression(fs.InitialValue, fs.DataType));
                    init.Body.Statements.Add(new ExpressionStatement(ae));
                }
            }
            init.Body.Statements.Add(new ReturnStatement(new SymbolExpression(iniThis)));*/

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
            if(IsKeyword("const"))
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

                if (decl.isByRef)
                {
                    Error("Reference not allowed here");
                    return false;
                }

                if (decl.type.IsFunction())
                {
                    module.AddChild(decl.type);
                    unit.AddSymbol(decl.type);
                    decl.type.Line = line;

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
                            decl.type.Flags |= SymbolFlags.External;
                        else if (IsKeyword("verbatim"))
                        {
                            decl.type.Flags |= SymbolFlags.Verbatim;
                            if (scanner.GetToken() != Token.String)
                            {
                                Error("Verbatim string expected");
                                return false;
                            }
                            (decl.type as Function).Verbatim = scanner.GetTokenString();
                            scanner.NextToken();
                        }

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
    }
}

