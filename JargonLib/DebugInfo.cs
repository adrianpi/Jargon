using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace Jargon
{
    public abstract class DINode
    {
        public int ID;

        public virtual void Serialize(StringBuilder sb) { }

        private static int id = 0;
        public static List<DINode> AllNodes = new List<DINode>();
        public static Dictionary<TypeSymbol, DIType> TypeMap = new Dictionary<TypeSymbol, DIType>();
        public static Dictionary<LocalVariable, DILocalVariable> LocalMap = new Dictionary<LocalVariable, DILocalVariable>();
        public static Dictionary<Function, DISubprogram> FunctionMap = new Dictionary<Function, DISubprogram>();
        public static Dictionary<GlobalVariable, DIGlobalVariableExpression> GlobalMap = new Dictionary<GlobalVariable, DIGlobalVariableExpression>();
        public static List<DIGlobalVariableExpression> Globals = new List<DIGlobalVariableExpression>();

        public static void Reset()
        {
            AllNodes.Clear();
            TypeMap.Clear();
            LocalMap.Clear();
            FunctionMap.Clear();
            GlobalMap.Clear();
            Globals.Clear();
            id = 0;
        }

        public DINode()
        {
            ID = id++;
            AllNodes.Add(this);
        }

        int prev = 0;

        protected void SerializeID(StringBuilder sb)
        {
            sb.Append($"!{ID} = ");
        }

        public virtual void SerializeClassName(StringBuilder sb)
        {
            sb.Append($"!{GetType().Name}");
            prev = 0;
        }

        public virtual void SerializeProperty(StringBuilder sb, string name)
        {
            if (prev == 1)
                sb.Append(", ");
            var pi = GetType().GetField(name);
            sb.Append(name);
            sb.Append(": ");
            if (pi.FieldType == typeof(string))
            {
                sb.Append("\"");
                sb.Append(pi.GetValue(this).ToString());
                sb.Append("\"");
            }
            else if (pi.FieldType == typeof(DINode) || pi.FieldType.IsSubclassOf(typeof(DINode)))
            {
                var node = pi.GetValue(this) as DINode;
                if (node == null)
                    sb.Append("null");
                else
                {
                    sb.Append("!");
                    sb.Append(node.ID);
                }
            }
            else if (pi.FieldType == typeof(bool))
            {
                sb.Append(pi.GetValue(this).ToString().ToLower());
            }
            else
            {
                sb.Append(pi.GetValue(this).ToString());
            }
            prev = 1;
        }

        public static DIType GetTypeInfo(TypeSymbol t)
        {
            if (t == TypeSymbol.Void)
                return null;

            if (TypeMap.ContainsKey(t))
            {
                return TypeMap[t];
            }

            if (t.IsPrimitive())
            {
                var pt = t as PrimitiveType;
                var bt = new DIBasicType();
                bt.name = pt.Name;
                bt.size = pt.Size * 8;
                if (pt.IsPointer())
                    bt.encoding = DIEncoding.DW_ATE_address;
                else if (pt.IsBool())
                    bt.encoding = DIEncoding.DW_ATE_boolean;
                else if (pt.IsFloatingPoint())
                    bt.encoding = DIEncoding.DW_ATE_float;
                else
                {
                    if (pt == TypeSymbol.UByte)
                        bt.encoding = DIEncoding.DW_ATE_unsigned_char;
                    else if (pt == TypeSymbol.Byte)
                        bt.encoding = DIEncoding.DW_ATE_signed_char;
                    else if (pt.IsUnsigned())
                        bt.encoding = DIEncoding.DW_ATE_unsigned;
                    else
                        bt.encoding = DIEncoding.DW_ATE_signed;
                }
                TypeMap[pt] = bt;
                return bt;
            }
            else if (t.IsArray())
            {
                var at = t as ArrayType;
                var ct = new DICompositeType();
                ct.tag = DITypeTag.DW_TAG_array_type;
                ct.baseType = GetTypeInfo(at.ElementType);
                ct.size = at.ElementType.Size * 8 * at.ArraySize;
                ct.elements = new DINodeList<DINode>();
                var sr = new DISubrange();
                sr.count = at.ArraySize;
                ct.elements.Add(sr);
                TypeMap[at] = ct;
                return ct;
            }
            else if (t.IsPointer())
            {
                var pt = t as PointerType;
                var dt = new DIDerivedType();
                dt.tag = DITypeTag.DW_TAG_pointer_type;
                dt.baseType = GetTypeInfo(pt.ElementType);
                dt.size = 64;
                TypeMap[pt] = dt;
                return dt;
            }
            else if (t.IsStruct())
            {
                var st = t as StructType;
                var ct = new DICompositeType();
                ct.tag = DITypeTag.DW_TAG_structure_type;
                ct.name = st.Name;
                ct.size = st.Size * 8;
                ct.elements = new DINodeList<DINode>();
                TypeMap[st] = ct;
                foreach (FieldSymbol fs in st.Children)
                {
                    var dt = new DIDerivedType();
                    dt.tag = DITypeTag.DW_TAG_member;
                    dt.name = fs.Name;
                    dt.baseType = GetTypeInfo(fs.DataType);
                    dt.offset = fs.Offset * 8;
                    dt.size = fs.DataType.Size * 8;
                    ct.elements.Add(dt);
                }
                return ct;
            }
            else if (t.IsClass())
            {
                var st = t as ClassType;
                var ct = new DICompositeType();
                ct.tag = DITypeTag.DW_TAG_structure_type;
                ct.name = st.Name;
                ct.size = st.Size * 8;
                ct.elements = new DINodeList<DINode>();
                TypeMap[st] = ct;
                while (st != null)
                {
                    foreach (var c in st.Children)
                    {
                        if (c is FieldSymbol fs && !fs.Flags.HasFlag(SymbolFlags.Static))
                        {
                            var dt = new DIDerivedType();
                            dt.tag = DITypeTag.DW_TAG_member;
                            dt.name = fs.Name;
                            dt.baseType = GetTypeInfo(fs.DataType);
                            dt.offset = fs.Offset * 8;
                            dt.size = fs.DataType.Size * 8;
                            ct.elements.Add(dt);
                        }
                    }
                    st = st.BaseClass;
                }
                return ct;
            }
            else if (t.IsEnum())
            {
                var et = t as EnumType;
                var ct = new DICompositeType();
                ct.tag = DITypeTag.DW_TAG_enumeration_type;
                ct.baseType = GetTypeInfo(et.ElementType);
                ct.name = et.Name;
                ct.size = et.Size * 8;
                ct.align = ct.size;
                ct.elements = new DINodeList<DINode>();
                TypeMap[et] = ct;
                foreach (EnumValue ev in et.Children)
                {
                    var dt = new DIEnumerator();
                    dt.name = ev.Name;
                    dt.value = ev.Value;
                    ct.elements.Add(dt);
                }
                return ct;
            }
            else
            {
                return null;
            }
        }
    }

    public enum DILanguage
    {
        DW_LANG_C_plus_plus_14,
    }

    public enum DIEmissionKind
    {
        FullDebug,
    }

    public enum DINameTableKind
    {
        None,
    }

    public class DICompileUnit : DINode
    {
        public DILanguage language = DILanguage.DW_LANG_C_plus_plus_14;
        public DIFile file;
        public string producer = "clang version 21.1.2";
        public bool isOptimized = false;
        public int runtimeVersion = 0;
        public DIEmissionKind emissionKind = DIEmissionKind.FullDebug;
        public DINodeList<DIGlobalVariableExpression> globals;
        public DINameTableKind nameTableKind = DINameTableKind.None;

        public override void Serialize(StringBuilder sb)
        {
            SerializeID(sb);
            sb.Append("distinct ");
            SerializeClassName(sb);
            sb.Append("(");
            SerializeProperty(sb, "language");
            SerializeProperty(sb, "file");
            SerializeProperty(sb, "producer");
            SerializeProperty(sb, "isOptimized");
            SerializeProperty(sb, "runtimeVersion");
            SerializeProperty(sb, "emissionKind");
            SerializeProperty(sb, "globals");
            SerializeProperty(sb, "nameTableKind");
            sb.AppendLine(")");
        }
    }

    public enum DIChecksumKind
    {
        CSK_MD5,
    }

    public class DIFile : DINode
    {
        public string filename;
        public string directory;
        public DIChecksumKind checksumkind = DIChecksumKind.CSK_MD5;
        public string checksum;

        public override void Serialize(StringBuilder sb)
        {
            SerializeID(sb);
            SerializeClassName(sb);
            sb.Append("(");
            SerializeProperty(sb, "filename");
            SerializeProperty(sb, "directory");
            SerializeProperty(sb, "checksumkind");
            SerializeProperty(sb, "checksum");
            sb.AppendLine(")");
        }
    }

    public class DIGlobalVariableExpression : DINode
    {
        public DIGlobalVariable var;
        //public string expr = "!DIExpression()";

        public DIGlobalVariableExpression()
        {
            Globals.Add(this);
        }

        public override void Serialize(StringBuilder sb)
        {
            SerializeID(sb);
            SerializeClassName(sb);
            sb.Append("(");
            SerializeProperty(sb, "var");
            sb.Append(", expr: !DIExpression()");
            sb.AppendLine(")");
        }
    }

    public class DIGlobalVariable : DINode
    {
        public string name;
        public string linkageName;
        //public DICompileUnit scope;
        //public DIFile file;
        //public int line;
        public DIType type;
        public bool isLocal = false;
        public bool isDefinition = true;

        public override void Serialize(StringBuilder sb)
        {
            SerializeID(sb);
            sb.Append("distinct ");
            SerializeClassName(sb);
            sb.Append("(");
            SerializeProperty(sb, "name");
            SerializeProperty(sb, "linkageName");
            SerializeProperty(sb, "type");
            SerializeProperty(sb, "isLocal");
            SerializeProperty(sb, "isDefinition");
            sb.AppendLine(")");
        }
    }

    public class DILocalVariable : DINode
    {
        public string name;
        public int arg;
        public DISubprogram scope;
        public DIFile file;
        public int line;
        public DIType type;

        public override void Serialize(StringBuilder sb)
        {
            SerializeID(sb);
            SerializeClassName(sb);
            sb.Append("(");
            SerializeProperty(sb, "name");
            if (arg != 0)
                SerializeProperty(sb, "arg");
            SerializeProperty(sb, "scope");
            SerializeProperty(sb, "file");
            SerializeProperty(sb, "line");
            SerializeProperty(sb, "type");
            sb.AppendLine(")");
        }
    }

    public class DIType : DINode
    {
    }

    public enum DIEncoding
    {
        DW_ATE_address,
        DW_ATE_boolean,
        DW_ATE_float,
        DW_ATE_signed,
        DW_ATE_signed_char,
        DW_ATE_unsigned,
        DW_ATE_unsigned_char,
    }

    public class DIBasicType : DIType
    {
        public string name;
        public int size;
        public DIEncoding encoding = DIEncoding.DW_ATE_signed;

        public override void Serialize(StringBuilder sb)
        {
            SerializeID(sb);
            SerializeClassName(sb);
            sb.Append("(");
            SerializeProperty(sb, "name");
            SerializeProperty(sb, "size");
            SerializeProperty(sb, "encoding");
            sb.AppendLine(")");
        }
    }

    public enum DITypeTag
    {
        DW_TAG_const_type,
        DW_TAG_pointer_type,
        DW_TAG_array_type,
        DW_TAG_structure_type,
        DW_TAG_member,
        DW_TAG_enumeration_type
    }

    public class DIDerivedType : DIType
    {
        public DITypeTag tag;
        public string name;
        public DIType baseType;
        public int size;
        public int offset;

        public override void Serialize(StringBuilder sb)
        {
            SerializeID(sb);
            SerializeClassName(sb);
            sb.Append("(");
            SerializeProperty(sb, "tag");
            if (name != null)
                SerializeProperty(sb, "name");
            SerializeProperty(sb, "baseType");
            if (offset != 0)
                SerializeProperty(sb, "offset");
            SerializeProperty(sb, "size");
            sb.AppendLine(")");
        }
    }

    public class DICompositeType : DIType
    {
        public DITypeTag tag;
        public string name;
        public DIType baseType;
        public int size;
        public int align;
        public DINodeList<DINode> elements;

        public override void Serialize(StringBuilder sb)
        {
            SerializeID(sb);
            SerializeClassName(sb);
            sb.Append("(");
            SerializeProperty(sb, "tag");
            if (name != null)
                SerializeProperty(sb, "name");
            if (baseType != null)
                SerializeProperty(sb, "baseType");
            SerializeProperty(sb, "size");
            if (tag == DITypeTag.DW_TAG_enumeration_type)
            {
                SerializeProperty(sb, "align");
            }
            SerializeProperty(sb, "elements");
            sb.AppendLine(")");
        }
    }

    public class DISubrange : DINode
    {
        public int count;

        public override void Serialize(StringBuilder sb)
        {
            SerializeID(sb);
            SerializeClassName(sb);
            sb.Append("(");
            SerializeProperty(sb, "count");
            sb.AppendLine(")");
        }
    }

    public class DISubroutineType : DIType
    {
        public DINodeList<DIType> types = new DINodeList<DIType>();

        public override void Serialize(StringBuilder sb)
        {
            SerializeID(sb);
            SerializeClassName(sb);
            sb.Append("(");
            SerializeProperty(sb, "types");
            sb.AppendLine(")");
        }
    }

    public class DIEnumerator : DINode
    {
        public string name;
        public long value;

        public override void Serialize(StringBuilder sb)
        {
            SerializeID(sb);
            SerializeClassName(sb);
            sb.Append("(");
            SerializeProperty(sb, "name");
            SerializeProperty(sb, "value");
            sb.AppendLine(")");
        }
    }

    public enum DIFlags
    {
        DIFlagPrototyped
    }

    public enum DISPFlags
    {
        DISPFlagDefinition,
    }

    public class DISubprogram : DINode
    {
        public string name;
        public string linkageName;
        public DINode scope;
        public DIFile file;
        public int line;
        public DISubroutineType type;
        public int scopeLine;
        public DIFlags flags = DIFlags.DIFlagPrototyped;
        public DISPFlags spFlags = DISPFlags.DISPFlagDefinition;
        public DICompileUnit unit;
        public DINodeList<DINode> retainedNodes = new DINodeList<DINode>();
        internal List<DILocation> locations = new List<DILocation>();

        public DILocation GetLocation(int line)
        {
            foreach (var loc in locations)
            {
                if (loc.line == line)
                    return loc;
            }
            DILocation dloc = new DILocation();
            dloc.line = line;
            dloc.scope = this;
            locations.Add(dloc);
            return dloc;
        }

        public override void Serialize(StringBuilder sb)
        {
            SerializeID(sb);
            sb.Append("distinct ");
            SerializeClassName(sb);
            sb.Append("(");
            SerializeProperty(sb, "name");
            SerializeProperty(sb, "linkageName");
            SerializeProperty(sb, "scope");
            SerializeProperty(sb, "file");
            SerializeProperty(sb, "line");
            SerializeProperty(sb, "type");
            SerializeProperty(sb, "scopeLine");
            SerializeProperty(sb, "flags");
            SerializeProperty(sb, "spFlags");
            SerializeProperty(sb, "unit");
            SerializeProperty(sb, "retainedNodes");
            sb.AppendLine(")");
        }
    }

    public class DILocation : DINode
    {
        public int line;
        public DISubprogram scope;

        public override void Serialize(StringBuilder sb)
        {
            SerializeID(sb);
            SerializeClassName(sb);
            sb.Append("(");
            SerializeProperty(sb, "line");
            SerializeProperty(sb, "scope");
            sb.AppendLine(")");
        }
    }

    public class DIText : DINode
    {
        public string text;

        public override void Serialize(StringBuilder sb)
        {
            SerializeID(sb);
            sb.AppendLine(text);
        }
    }

    public class DINodeList<T> : DINode, IList<T>, IReadOnlyList<T>
    where T : DINode
    {
        private readonly List<T> _items = new List<T>();

        // ───────────────────────────────────────────────────────────────
        // Core collection implementation
        // ───────────────────────────────────────────────────────────────

        public int Count => _items.Count;

        public bool IsReadOnly => false;

        public T this[int index]
        {
            get => _items[index];
            set => _items[index] = value ?? throw new ArgumentNullException(nameof(value));
        }

        public void Add(T item)
        {
            _items.Add(item);
        }

        public void Clear() => _items.Clear();

        public bool Contains(T item) => _items.Contains(item);

        public void CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

        public bool Remove(T item) => _items.Remove(item);

        public int IndexOf(T item) => _items.IndexOf(item);

        public void Insert(int index, T item)
        {
            _items.Insert(index, item);
        }

        public void RemoveAt(int index) => _items.RemoveAt(index);

        // ───────────────────────────────────────────────────────────────
        // IEnumerable<T> & IEnumerable implementations
        // ───────────────────────────────────────────────────────────────

        public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        // ───────────────────────────────────────────────────────────────
        // Domain-specific helper methods (optional but very useful)
        // ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Finds the first node with the given name (case-sensitive)
        /// </summary>
        public T FindByID(int id)
        {
            return _items.Find(n => n.ID == id);
        }

        public override void Serialize(StringBuilder sb)
        {
            SerializeID(sb);
            sb.Append("!{");
            bool first = true;
            foreach (var item in _items)
            {
                if (!first)
                    sb.Append(", ");
                if (item == null)
                    sb.Append("null");
                else
                    sb.Append($"!{item.ID}");
                first = false;
            }
            sb.AppendLine("}");
        }
    }
}