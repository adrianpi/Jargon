using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Jargon
{
    public enum SymbolType
    {
        None = 0,
        Primitive = 1,
        LocalVar,
        GlobalVar,
        Function,
        Module,
        Root,
        Array,
        Pointer,
        Field,
        Struct,
        BlockScope,
        Class,
        Method,
        Property,
        Enum,
        EnumValue,
        Template,
        Constant,
    }

    public enum TypeCode
    {
        None = 0,
        Void = 1,
        Bool,
        Byte,
        UByte,
        Short,
        UShort,
        Int,
        UInt,
        Long,
        ULong,
        Float,
        Double,
        Function,
        Array,
        Pointer,
        Struct,
        CStr,
    }

    [Flags]
    public enum SymbolFlags
    {
        Variadic = 0x01,
        External = 0x02,
        Internal = 0x04,
        Virtual = 0x08,
        NoDebug = 0x10,
        Static = 0x20,
        ByRef = 0x40,
        Weak = 0x80,
        Verbatim = 0x100,
        Union = 0x200,
    }

    public class CompileUnit
    {
        public string Path { get; private set; }
        public string FileName { get; private set; }
        public string Text { get; private set; }
        public List<ModuleUsing> Usings { get; private set; } = new List<ModuleUsing>();
        public List<Symbol> Symbols { get; private set; } = new List<Symbol>();
        public List<Symbol> SymbolRefs { get; private set; } = new List<Symbol>();
        public HashSet<string> Strings { get; private set; } = new HashSet<string>();
        public List<CompileUnit> Children { get; private set; } = new List<CompileUnit>();

        public CompileUnit(string path)
        {
            Path = System.IO.Path.GetFullPath(path);
            FileName = System.IO.Path.GetFileName(path);
            Text = File.ReadAllText(path);
            Text += "\r\n";
        }

        public void AddSymbol(Symbol s)
        {
            foreach (var t in Symbols)
                if (t == s)
                    return;
            s.Unit = this;
            Symbols.Add(s);
        }

        public void AddSymbolRef(Symbol s)
        {
            if(s.Name == "")
                return;

            foreach (var sr in SymbolRefs)
                if (sr == s || sr.Name == s.Name)
                    return;

            // Recursively add references for struct/class fields and base classes
            StructType st = s as StructType;
            if (st != null)
            {
                for (int i = 0; i < st.Children.Count; ++i)
                {
                    Symbol child = st.Children[i];
                    FieldSymbol fs = child as FieldSymbol;
                    if (fs != null)
                    {
                        if (fs.DataType.IsStruct())
                        {
                            TypeSymbol dt = fs.DataType;
                            if (dt.Unit != this)
                                AddSymbolRef(dt);
                        }
                    }
                }
            }
            else
            {
                ClassType ct = s as ClassType;
                while (ct != null)
                {
                    for (int i = 0; i < ct.Children.Count; ++i)
                    {
                        Symbol child = ct.Children[i];
                        FieldSymbol fs = child as FieldSymbol;
                        if (fs != null)
                        {
                            if (fs.DataType.IsStruct())
                            {
                                TypeSymbol dt = fs.DataType;
                                if (dt.Unit != this)
                                    AddSymbolRef(dt);
                            }
                        }
                    }
                    ct = ct.BaseClass;
                }
            }

            SymbolRefs.Add(s);
        }
    }

    public abstract class Symbol
    {
        public string Name { get; set; }
        public SymbolFlags Flags { get; set; }
        protected Symbol parent;
        public Symbol Parent => parent;
        public List<Symbol> children = new List<Symbol>();
        public IReadOnlyList<Symbol> Children => children;
        public SymbolType SymbolType { get; private set; }
        public virtual bool IsType() { return false; }
        public CompileUnit Unit { get; set; }
        public int Line = 0;

        public Dictionary<string, Symbol> lookup  = new Dictionary<string, Symbol>();

        protected Symbol(Symbol parent, string name, SymbolType stype)
        {
            this.Name = name;
            this.SymbolType = stype;
            if (parent != null)
            {
                parent.AddChild(this);
            }
        }

        public void AddChild(Symbol child)
        {
            children.Add(child);
            child.parent = this;
            if (!(child is ArrayType) && !(child is PointerType))
            {
                System.Diagnostics.Debug.Assert(!lookup.ContainsKey(child.Name), $"Duplicate symbol name '{child.Name}' in '{Name}'");
                lookup[child.Name] = child;
            }
        }        

        public Symbol Find(string name)
        {
            /*foreach (Symbol child in children)
            {
                if (child.Name == name)
                    return child;
            }*/
            if (lookup.TryGetValue(name, out Symbol result))
                return result;

            if (parent != null)
            {
                return parent.Find(name);
            }
            else
            {
                return null;
            }
        }

        public virtual Symbol FindChild(string name)
        {
            /*foreach (Symbol child in children)
            {
                if (child.Name == name)
                    return child;
            }*/
            if (lookup.TryGetValue(name, out Symbol result))
                return result;

            return null;
        }

        public virtual TypeSymbol DataType => TypeSymbol.Void;
        public virtual void Visit(CodeVisitor visitor) { }
    }

    public class RootSymbol : Symbol
    {
        //public List<ModuleUsing> Usings = new List<ModuleUsing>();
        private static RootSymbol instance;
        public static RootSymbol Instance
        {
            get
            {
                if (instance == null)
                    instance = new RootSymbol();
                return instance;
            }
        }

        public RootSymbol()
            : base(null, "root", SymbolType.Root)
        {
        }
    }

    public abstract class TypeSymbol : Symbol
    {
        private TypeCode typeCode;
        private PointerType pointerType = null;
        public TypeCode TypeCode => typeCode;
        public int Size { get; protected set; }
        public TypeSymbol ElementType { get; protected set; }
        public override bool IsType() { return true; }
        public virtual bool IsPrimitive() { return false; }
        public bool IsBool()
        {
            return typeCode == TypeCode.Bool;
        }
        public bool IsInteger()
        {
            return typeCode >= TypeCode.Byte && typeCode <= TypeCode.ULong;
        }
        public bool IsUnsigned()
        {
            return typeCode == TypeCode.UByte || typeCode == TypeCode.UShort
                || typeCode == TypeCode.UInt || typeCode == TypeCode.ULong;
        }
        public bool IsFloatingPoint()
        {
            return typeCode == TypeCode.Float || typeCode == TypeCode.Double;
        }
        public virtual bool IsArray() => false;
        public virtual bool IsPointer() => false;
        public virtual bool IsStruct() => false;
        public virtual bool IsFunction() => false;
        public virtual bool IsClass() => false;
        public virtual bool IsEnum() => false;

        public bool IsClassRef() => IsPointer() && ElementType.IsClass();

        public virtual bool IsEqualTo(object obj)
        {
            return this == obj;
        }

        public PointerType GetPointerType()
        {
            if (pointerType == null)
                pointerType = new PointerType(Parent, this);
            return pointerType;
        }

        public ArrayType GetArrayType(int asize)
        {
            return new ArrayType(Parent, this, asize);
        }

        protected TypeSymbol(Symbol parent, string name, SymbolType stype, TypeCode tcode)
            : base(parent, name, stype)
        {
            this.typeCode = tcode;
        }

        public static TypeSymbol Void { get; private set; } = new PrimitiveType(RootSymbol.Instance, "void", TypeCode.Void, 0);
        public static TypeSymbol Bool { get; private set; } = new PrimitiveType(RootSymbol.Instance, "bool", TypeCode.Bool, 1);
        public static TypeSymbol Byte { get; private set; } = new PrimitiveType(RootSymbol.Instance, "byte", TypeCode.Byte, 1);
        public static TypeSymbol UByte { get; private set; } = new PrimitiveType(RootSymbol.Instance, "ubyte", TypeCode.UByte, 1);
        public static TypeSymbol Short { get; private set; } = new PrimitiveType(RootSymbol.Instance, "short", TypeCode.Short, 2);
        public static TypeSymbol UShort { get; private set; } = new PrimitiveType(RootSymbol.Instance, "ushort", TypeCode.UShort, 2);
        public static TypeSymbol Int { get; private set; } = new PrimitiveType(RootSymbol.Instance, "int", TypeCode.Int, 4);
        public static TypeSymbol UInt { get; private set; } = new PrimitiveType(RootSymbol.Instance, "uint", TypeCode.UInt, 4);
        public static TypeSymbol Long { get; private set; } = new PrimitiveType(RootSymbol.Instance, "long", TypeCode.Long, 8);
        public static TypeSymbol ULong { get; private set; } = new PrimitiveType(RootSymbol.Instance, "ulong", TypeCode.ULong, 8);
        public static TypeSymbol Float { get; private set; } = new PrimitiveType(RootSymbol.Instance, "float", TypeCode.Float, 4);
        public static TypeSymbol Double { get; private set; } = new PrimitiveType(RootSymbol.Instance, "double", TypeCode.Double, 8);
    }

    public class PrimitiveType : TypeSymbol
    {
        public override bool IsPrimitive() { return true; }

        public PrimitiveType(Symbol parent, string name, TypeCode tcode, int size)
            : base(parent, name, SymbolType.Primitive, tcode)
        {
            Size = size;
        }
    }

    public class ArrayType : TypeSymbol
    {
        public int ArraySize { get; private set; }

        public override bool IsArray() => true;

        public override bool IsEqualTo(object obj)
        {
            return (obj is ArrayType at) && at.ElementType == this.ElementType && at.ArraySize == ArraySize;
        }

        public ArrayType(Symbol parent, TypeSymbol elementType, int asize)
            : base(parent, elementType.Name + "[" + asize + "]", SymbolType.Array, TypeCode.Array)
        {
            this.ElementType = elementType;
            this.ArraySize = asize;
            this.Size = asize * elementType.Size;
        }
    }

    public class PointerType : TypeSymbol
    {
        public override bool IsPointer() => true;

        public override bool IsEqualTo(object obj)
        {
            return (obj is PointerType pt) && pt.ElementType.IsEqualTo(this.ElementType);
        }

        public PointerType(Symbol parent, TypeSymbol elementType)
            : base(parent, elementType.Name + "*", SymbolType.Pointer, TypeCode.Pointer)
        {
            this.ElementType = elementType;
            this.Size = 8;
        }
    }

    public class FieldSymbol : Symbol
    {
        private TypeSymbol type;
        public int Index { get; set; } = 0;
        public int Offset { get; set; } = 0;
        public Expression InitialValue { get; set; }

        public override TypeSymbol DataType => type;

        public FieldSymbol(Symbol parent, string name, TypeSymbol type)
            : base(parent, name, SymbolType.Field)
        {
            this.type = type;
        }
    }

    public class StructType : TypeSymbol
    {
        public int Alignment;

        public override bool IsStruct() => true;

        public StructType(Symbol parent, string name)
            : base(parent, name, SymbolType.Struct, TypeCode.Struct)
        {
        }

        public void SetSize(int size)
        {
            Size = size;
            //Alignment = 0;
        }

        public override void Visit(CodeVisitor visitor)
        {
            visitor.VisitStruct(this);
        }
    }

    public class MethodSymbol : Symbol
    {
        private Function type;
        public int VSlot { get; set; } = -1;

        public override TypeSymbol DataType => type;

        public MethodSymbol(Symbol parent, string name, Function type)
            : base(parent, name, SymbolType.Method)
        {
            this.type = type;
        }

        public void SetFunction(Function fn)
        {
            this.type = fn;
        }
    }

    public class PropertySymbol : Symbol
    {
        private TypeSymbol type;
        public MethodSymbol Setter { get; set; }
        public MethodSymbol Getter { get; set; }

        public PropertySymbol(Symbol parent, string name, TypeSymbol type)
            : base(parent, name, SymbolType.Property)
        {
            this.type = type;
        }

        public override TypeSymbol DataType => type;

        public override void Visit(CodeVisitor visitor)
        {
            base.Visit(visitor);
        }
    }

    public class ClassType : TypeSymbol
    {
        public ClassType BaseClass { get; set; }
        public override bool IsClass() => true;
        public int FieldCount { get; set; }
        public List<MethodSymbol> VirtualMethods = new List<MethodSymbol>();
        public Function Factory { get; set; }
        public ClassType(Symbol parent, string name)
            : base(parent, name, SymbolType.Class, TypeCode.Struct)
        {
        }
        public bool stage2Completed = false;

        public override Symbol FindChild(string name)
        {
            foreach (var c in children)
            {
                if (c.Name == name)
                    return c;
            }

            if (BaseClass != null)
                return BaseClass.FindChild(name);
            else
                return null;
        }

        public void SetSize(int size)
        {
            Size = size;
        }

        public override void Visit(CodeVisitor visitor)
        {
            visitor.VisitClass(this);
        }

        public bool IsClassOf(ClassType other)
        {
            var cls = this;
            while (cls != null)
            {
                if (cls == other) return true;
                cls = cls.BaseClass;
            }
            return false;
        }
    }

    public abstract class Variable : Symbol
    {
        private TypeSymbol type;
        public Expression Init { get; set; }
        public int Offset { get; set; } = 0;
        public string TagName { get; set; }

        public override TypeSymbol DataType => type;

        public Variable(Symbol parent, string name, SymbolType stype, TypeSymbol type, Expression init)
            : base(parent, name, stype)
        {
            this.type = type;
            this.Init = init;
        }
    }

    public class LocalVariable : Variable
    {
        public LocalVariable(Symbol parent, string name, TypeSymbol type, Expression init)
            : base(parent, name, SymbolType.LocalVar, type, init)
        {
        }
    }

    public class GlobalVariable : Variable
    {
        public GlobalVariable(Symbol parent, string name, TypeSymbol type, Expression init)
            : base(parent, name, SymbolType.GlobalVar, type, init)
        {
        }

        public override void Visit(CodeVisitor visitor)
        {
            visitor.VisitGlobalVariable(this);
        }
    }

    public class Function : TypeSymbol
    {
        public TypeSymbol ReturnType { get; private set; }
        public override TypeSymbol DataType => this;
        public List<LocalVariable> Parameters = new List<LocalVariable>();
        public List<LocalVariable> Locals = new List<LocalVariable>();
        public StatementBlock Body { get; set; }
        public int LocalsCount = 0;
        public int Temps = 0;
        public string Verbatim;
        public string fileName;
        public ClassType declaringClass;


        public override bool IsFunction() => true;

        public override bool IsEqualTo(object obj)
        {
            if ((obj is Function fn) && fn.ReturnType.IsEqualTo(this.ReturnType) && fn.Parameters.Count == Parameters.Count)
            {
                for (int i = 0; i < Parameters.Count; i++)
                {
                    var p1 = fn.Parameters[i];
                    var p2 = Parameters[i];
                    if (!p1.DataType.IsEqualTo(p2.DataType))
                        return false;
                }
                return true;
            }
            return false;
        }

        public Function(Symbol parent, string name, TypeSymbol returnType)
            : base(parent, name, SymbolType.Function, TypeCode.Function)
        {
            this.ReturnType = returnType;
        }

        public override void Visit(CodeVisitor visitor)
        {
            visitor.VisitFunction(this);
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(ReturnType.Name);
            sb.Append(" ");
            sb.Append(Name);
            sb.Append("(");
            foreach (var p in Parameters)
            {
                if (p != Parameters.First())
                    sb.Append(", ");
                sb.Append(p.DataType.Name);
                if (p.Name != "")
                    sb.Append(" ");
                sb.Append(p.Name);
            }
            sb.Append(")");
            return sb.ToString();
        }
    }

    public class ModuleUsing
    {
        public string Name { get; private set; }
        public Module Module { get; set; }

        public ModuleUsing(string name)
        {
            this.Name = name;
        }
    }

    public class Module : Symbol
    {
        public Dictionary<string, string> Strings = new Dictionary<string, string>();
        public List<ModuleUsing> Usings = new List<ModuleUsing>();

        public Module(string name)
            : base(RootSymbol.Instance, name, SymbolType.Module)
        {
        }

        public string AddString(string str)
        {
            string key = Strings.FirstOrDefault(pair => pair.Value == str).Key;
            if (key != null)
                return key;

            key = "$cstr_" + Strings.Count;
            Strings[key] = str;
            return key;
        }

        public override void Visit(CodeVisitor visitor)
        {
            visitor.VisitModule(this);
        }
    }

    public class BlockScope : Symbol
    {
        public BlockScope()
            : base(null, "", SymbolType.BlockScope)
        {
        }

        public void Reparent(Symbol parent)
        {
            this.parent = parent;
        }
    }

    public class EnumType : TypeSymbol
    {
        public EnumType(Symbol parent, string name, TypeSymbol type)
            : base(parent, name, SymbolType.Enum, type.TypeCode)
        {
            ElementType = type;
            Size = type.Size;
        }

        public override TypeSymbol DataType => this;

        public override bool IsEnum() => true;

        public override void Visit(CodeVisitor visitor)
        {
        }
    }

    public class EnumValue : Symbol
    {
        public long Value { get; private set; }
        public EnumValue(Symbol parent, string name, long value)
            : base(parent, name, SymbolType.EnumValue)
        {
            Value = value;
        }

        public override TypeSymbol DataType => parent.DataType.ElementType;

        public override void Visit(CodeVisitor visitor)
        {
        }
    }

    public class Template : TypeSymbol
    {
        public List<string> TemplateParams { get; private set; } = new List<string>();
        public string Source { get; set; }

        public Template(Symbol parent, string name)
            : base(parent, name, SymbolType.Template, TypeCode.Struct)
        {
        }
    }

    public class ConstantValue : Symbol
    {
        public Expression Value;

        public ConstantValue(Symbol parent, string name, Expression value)
            : base(parent, name, SymbolType.Constant)
        {
            Value = value;
        }

        public override TypeSymbol DataType => Value.DataType;
    }
}
