using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Jargon
{
    public static class TypeInfo
    {
        private static void WriteString(BinaryWriter bw, string s)
        {
            foreach (char c in s)
            {
                bw.Write((byte)c);
            }
            bw.Write((byte)0);
        }

        private static void WriteType(BinaryWriter bw, TypeSymbol type)
        {
            if (type == null)
            {
                bw.Write((byte)TypeCode.None);
                return;
            }

            bw.Write((byte)type.TypeCode);

            if (type.TypeCode == TypeCode.Pointer)
            {
                WriteType(bw, type.ElementType);
            }
            else if (type.TypeCode == TypeCode.Array)
            {
                bw.Write((int)(type as ArrayType).ArraySize);
                WriteType(bw, type.ElementType);
            }
            else if (type.TypeCode == TypeCode.Struct)
            {
                WriteString(bw, type.Name);
            }
        }

        public static void WriteHeader(MemoryStream ms, string name)
        {
            BinaryWriter bw = new BinaryWriter(ms);
            bw.Write((byte)SymbolType.Module);
            bw.Write((int)0x01000000);
            WriteString(bw, name);
        }

        public static void Write(MemoryStream ms, Module m)
        {
            WriteHeader(ms, m.Name);

            BinaryWriter bw = new BinaryWriter(ms);

            foreach (var u in m.Usings)
            {
                WriteString(bw, u.Name);
            }
            WriteString(bw, "");

            ConstantExpression cexpr = null;

            foreach (var c in m.Children)
            {
                if (c.Flags.HasFlag(SymbolFlags.External))
                    continue;
                switch (c.SymbolType)
                {
                    case SymbolType.Constant:
                        bw.Write((byte)c.SymbolType);
                        bw.Write((short)c.Flags);
                        WriteType(bw, (c as ConstantValue).DataType);
                        WriteString(bw, c.Name);
                        cexpr = (c as ConstantValue).Value as ConstantExpression;
                        if ((c as ConstantValue).DataType.IsPrimitive())
                        {
                            if ((c as ConstantValue).DataType == TypeSymbol.Bool)
                                bw.Write(unchecked((byte)cexpr.Value));
                            else if ((c as ConstantValue).DataType == TypeSymbol.Byte)
                                bw.Write(unchecked((byte)cexpr.Value));
                            else if ((c as ConstantValue).DataType == TypeSymbol.UByte)
                                bw.Write(unchecked((byte)cexpr.Value));
                            else if ((c as ConstantValue).DataType == TypeSymbol.Short)
                                bw.Write(unchecked((short)cexpr.Value));
                            else if ((c as ConstantValue).DataType == TypeSymbol.UShort)
                                bw.Write(unchecked((ushort)cexpr.Value));
                            else if ((c as ConstantValue).DataType == TypeSymbol.Int)
                                bw.Write(cexpr.Value);
                            else if ((c as ConstantValue).DataType == TypeSymbol.UInt)
                                bw.Write(cexpr.Value);
                            else if ((c as ConstantValue).DataType == TypeSymbol.Long)
                                bw.Write(cexpr.Value64);
                            else if ((c as ConstantValue).DataType == TypeSymbol.ULong)
                                bw.Write(cexpr.Value64);
                            else if ((c as ConstantValue).DataType == TypeSymbol.Float)
                                bw.Write(cexpr.FloatValue);
                            else if ((c as ConstantValue).DataType == TypeSymbol.Double)
                                bw.Write(cexpr.DoubleValue);
                            else
                                System.Diagnostics.Debug.Assert(false); // should not happen
                        }
                        break;
                    case SymbolType.Enum:
                        bw.Write((byte)c.SymbolType);
                        bw.Write((short)c.Flags);
                        WriteType(bw, (c as EnumType).ElementType);
                        WriteString(bw, c.Name);
                        foreach (var cc in c.Children)
                        {
                            if (cc is EnumValue ev)
                            {
                                bw.Write((byte)cc.SymbolType);
                                bw.Write((short)cc.Flags);
                                WriteString(bw, cc.Name);
                                bw.Write(ev.Value);
                            }
                        }
                        bw.Write((byte)0);
                        break;
                    case SymbolType.Function:
                        bw.Write((byte)c.SymbolType);
                        bw.Write((short)c.Flags);
                        WriteType(bw, (c as Function).ReturnType);
                        WriteString(bw, c.Name);
                        if ((c as Function).Verbatim != null)
                            WriteString(bw, (c as Function).Verbatim);
                        else
                            WriteString(bw, "");
                        foreach (var p in (c as Function).Parameters)
                        {
                            bw.Write((byte)p.SymbolType);
                            bw.Write((short)p.Flags);
                            WriteType(bw, p.DataType);
                            WriteString(bw, p.Name);
                            bw.Write((byte)p.Offset);
                        }
                        bw.Write((byte)0);
                        break;
                    case SymbolType.Struct:
                        bw.Write((byte)c.SymbolType);
                        bw.Write((short)c.Flags);
                        WriteString(bw, c.Name);
                        bw.Write((ushort)(c as StructType).Size);
                        foreach (var cc in c.Children)
                        {
                            if (cc is FieldSymbol fs)
                            {
                                bw.Write((byte)cc.SymbolType);
                                bw.Write((short)cc.Flags);
                                WriteType(bw, fs.DataType);
                                WriteString(bw, cc.Name);
                                bw.Write((ushort)fs.Offset);
                                bw.Write((ushort)fs.Index);
                            }
                        }
                        bw.Write((byte)0);
                        break;
                    case SymbolType.Class:
                        bw.Write((byte)c.SymbolType);
                        bw.Write((short)c.Flags);
                        WriteString(bw, c.Name);
                        WriteType(bw, (c as ClassType).BaseClass);
                        bw.Write((ushort)(c as ClassType).Size);
                        bw.Write((ushort)(c as ClassType).FieldCount);
                        foreach (var cc in c.Children)
                        {
                            if (cc is FieldSymbol fs)
                            {
                                bw.Write((byte)cc.SymbolType);
                                bw.Write((short)cc.Flags);
                                WriteType(bw, fs.DataType);
                                WriteString(bw, cc.Name);
                                bw.Write((ushort)fs.Offset);
                                bw.Write((ushort)fs.Index);
                            }
                            else if (cc is MethodSymbol mthd)
                            {
                                bw.Write((byte)cc.SymbolType);
                                bw.Write((short)cc.Flags);
                                WriteString(bw, cc.Name);
                                string tname = c.Name + "__" + mthd.Name;
                                if(mthd.DataType.Name != tname)
                                    WriteString(bw, mthd.DataType.Name);
                                else
                                    WriteString(bw, "");
                                bw.Write((short)mthd.VSlot);
                            }
                            else if (cc is PropertySymbol ps)
                            {
                                bw.Write((byte)cc.SymbolType);
                                bw.Write((short)cc.Flags);
                                WriteType(bw, ps.DataType);
                                WriteString(bw, cc.Name);
                                byte getset = 0;
                                if (ps.Getter != null)
                                    /*WriteString(bw, ps.Getter.Name);
                                else
                                    WriteString(bw, "");*/
                                    getset |= 1;
                                if (ps.Setter != null)
                                    /*WriteString(bw, ps.Setter.Name);
                                else
                                    WriteString(bw, "");*/
                                    getset |= 2;
                                bw.Write((byte)getset);
                            }
                        }
                        bw.Write((byte)0);
                        foreach (var vm in (c as ClassType).VirtualMethods)
                        {
                            if (vm == null) continue;
                            WriteString(bw, vm.Name);
                        }
                        WriteString(bw, "");
                        break;
                    case SymbolType.Template:
                        bw.Write((byte)c.SymbolType);
                        bw.Write((short)c.Flags);
                        WriteString(bw, c.Name);
                        foreach (var tp in ((c as Template).TemplateParams))
                            WriteString(bw, tp);
                        WriteString(bw, "");
                        WriteString(bw, ((c as Template).Source));
                        break;
                    default:
                        break;
                }
            }
            bw.Write((byte)0);
        }

        public static byte[] GetSectionData(string filePath, string sectionName)
        {
            try
            {
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    var br = new BinaryReader(fs);

                    // Verify DOS header ('MZ')
                    if (br.ReadUInt16() != 0x5A4D)
                        return null;

                    // Get offset to NT header
                    fs.Seek(0x3C, SeekOrigin.Begin);
                    int ntOffset = br.ReadInt32();

                    // Verify NT header ('PE\0\0')
                    fs.Seek(ntOffset, SeekOrigin.Begin);
                    if (br.ReadUInt32() != 0x00004550)
                        return null;

                    // Read IMAGE_FILE_HEADER
                    br.ReadUInt16(); // Machine
                    ushort numSections = br.ReadUInt16();
                    br.ReadUInt32(); // TimeDateStamp
                    br.ReadUInt32(); // PointerToSymbolTable
                    br.ReadUInt32(); // NumberOfSymbols
                    ushort sizeOfOptionalHeader = br.ReadUInt16();
                    br.ReadUInt16(); // Characteristics

                    fs.Seek(sizeOfOptionalHeader, SeekOrigin.Current);

                    for (ushort i = 0; i < numSections; i++)
                    {
                        byte[] nameBytes = br.ReadBytes(8);
                        string secName = Encoding.ASCII.GetString(nameBytes).TrimEnd('\0');

                        uint virtualSize = br.ReadUInt32();
                        uint virtualAddress = br.ReadUInt32();
                        uint sizeOfRawData = br.ReadUInt32();
                        uint pointerToRawData = br.ReadUInt32();

                        fs.Seek(16, SeekOrigin.Current);

                        if (string.Equals(secName, sectionName, StringComparison.OrdinalIgnoreCase))
                        {
                            if (pointerToRawData + sizeOfRawData > fs.Length)
                                return null;

                            fs.Seek(pointerToRawData, SeekOrigin.Begin);
                            return br.ReadBytes((int)sizeOfRawData);
                        }
                    }
                    return null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                return null;
            }
        }

        private static string ReadString(BinaryReader br)
        {
            string str = "";
            char c = (char)br.ReadByte();
            while (c != 0)
            {
                str += c;
                c = (char)br.ReadByte();
            }
            return str;
        }

        private static TypeSymbol ReadType(BinaryReader br, Module module)
        {
            TypeCode tc = (TypeCode)br.ReadByte();
            if (tc == TypeCode.None)
                return null;
            else if (tc == TypeCode.Void)
                return TypeSymbol.Void;
            else if (tc == TypeCode.Bool)
                return TypeSymbol.Bool;
            else if (tc == TypeCode.Byte)
                return TypeSymbol.Byte;
            else if (tc == TypeCode.UByte)
                return TypeSymbol.UByte;
            else if (tc == TypeCode.Short)
                return TypeSymbol.Short;
            else if (tc == TypeCode.UShort)
                return TypeSymbol.UShort;
            else if (tc == TypeCode.Int)
                return TypeSymbol.Int;
            else if (tc == TypeCode.UInt)
                return TypeSymbol.UInt;
            else if (tc == TypeCode.Long)
                return TypeSymbol.Long;
            else if (tc == TypeCode.ULong)
                return TypeSymbol.ULong;
            else if (tc == TypeCode.Float)
                return TypeSymbol.Float;
            else if (tc == TypeCode.Double)
                return TypeSymbol.Double;
            else if (tc == TypeCode.Pointer)
            {
                TypeSymbol t = ReadType(br, module);
                return t.GetPointerType();
            }
            else if (tc == TypeCode.Array)
            {
                int sz = br.ReadInt32();
                TypeSymbol t = ReadType(br, module);
                return t.GetArrayType(sz);
            }
            else if (tc == TypeCode.Struct)
            {
                var name = ReadString(br);
                var t = module.Find(name) as TypeSymbol;
                if (t == null)
                {
                    foreach (var u in module.Usings)
                    {
                        t = u.Module.Find(name) as TypeSymbol;
                        if (t != null)
                            return t;
                    }
                    System.Diagnostics.Debug.Assert(false);
                }
                return t;
            }
            else
            {
                System.Diagnostics.Debug.Assert(false);
                return null;
            }
        }

        public static Module ParseModule(string filePath)
        {
            byte[] typeInfo = GetSectionData(filePath, ".module");
            var ms = new MemoryStream(typeInfo);
            BinaryReader br = new BinaryReader(ms);

            SymbolType st = (SymbolType)br.ReadByte();
            if (st != SymbolType.Module)
                return null;
            uint version = (uint)br.ReadInt32();
            if (version != 0x01000000)
                return null;
            string name = ReadString(br);

            Module module = new Module(name);

            string un = ReadString(br);
            while (un != "")
            {
                var mu = new ModuleUsing(un);
                mu.Module = ParseModule(mu.Name + ".dll");
                module.Usings.Add(mu);
                un = ReadString(br);
            }

            int flags = 0;
            st = (SymbolType)br.ReadByte();

            var methodMap = new Dictionary<MethodSymbol, string>();
            var getterMap = new Dictionary<PropertySymbol, string>();
            var setterMap = new Dictionary<PropertySymbol, string>();

            while (st != SymbolType.None)
            {
                switch (st)
                {
                    case SymbolType.Constant:
                        {
                            flags = br.ReadInt16();
                            TypeSymbol et = ReadType(br, module);
                            name = ReadString(br);
                            ConstantExpression cexpr = null;
                            switch (et.TypeCode)
                            {
                                case TypeCode.Bool:
                                    cexpr = new ConstantExpression(br.ReadByte() != 0);
                                    break;
                                case TypeCode.Byte:
                                    cexpr = new ConstantExpression(br.ReadSByte());
                                    break;
                                case TypeCode.UByte:
                                    cexpr = new ConstantExpression(br.ReadByte());
                                    break;
                                case TypeCode.Short:
                                    cexpr = new ConstantExpression(br.ReadInt16());
                                    break;
                                case TypeCode.UShort:
                                    cexpr = new ConstantExpression(br.ReadUInt16());
                                    break;
                                case TypeCode.Int:
                                    cexpr = new ConstantExpression(br.ReadInt32());
                                    break;
                                case TypeCode.UInt:
                                    cexpr = new ConstantExpression(br.ReadUInt32());
                                    break;
                                case TypeCode.Long:
                                    cexpr = new ConstantExpression(br.ReadInt64());
                                    break;
                                case TypeCode.ULong:
                                    cexpr = new ConstantExpression(br.ReadUInt64());
                                    break;
                                case TypeCode.Float:
                                    cexpr = new ConstantExpression(br.ReadSingle());
                                    break;
                                case TypeCode.Double:
                                    cexpr = new ConstantExpression(br.ReadDouble());
                                    break;
                                default:
                                    System.Diagnostics.Debug.Assert(false); // should not happen
                                    break;
                            }
                            var cv = new ConstantValue(module, name, cexpr);
                            cv.Flags = (SymbolFlags)flags;
                        }
                        break;
                    case SymbolType.Enum:
                        {
                            flags = br.ReadInt16();
                            TypeSymbol et = ReadType(br, module);
                            name = ReadString(br);
                            var enu = new EnumType(module, name, et);
                            enu.Flags = (SymbolFlags)flags;
                            SymbolType st2 = (SymbolType)br.ReadByte();
                            while (st2 != 0)
                            {
                                flags = br.ReadInt16();
                                name = ReadString(br);
                                long value = br.ReadInt64();
                                var ev = new EnumValue(enu, name, value);
                                ev.Flags = (SymbolFlags)flags;
                                st2 = (SymbolType)br.ReadByte();
                            }
                        }
                        break;

                    case SymbolType.Function:
                        {
                            flags = br.ReadInt16();
                            TypeSymbol t = ReadType(br, module);
                            name = ReadString(br);
                            var fn = new Function(module, name, t);
                            fn.Flags = (SymbolFlags)flags;
                            fn.Verbatim = ReadString(br);
                            if (fn.Verbatim == "")
                                fn.Verbatim = null;
                            SymbolType st2 = (SymbolType)br.ReadByte();
                            while (st2 != 0)
                            {
                                flags = br.ReadInt16();
                                TypeSymbol pt = ReadType(br, module);
                                name = ReadString(br);
                                var offs = br.ReadByte();
                                var p = new LocalVariable(fn, name, pt, null);
                                p.Flags = (SymbolFlags)flags;
                                p.Offset = offs;
                                fn.Parameters.Add(p);
                                st2 = (SymbolType)br.ReadByte();
                            }
                        }
                        break;

                    case SymbolType.Struct:
                        {
                            flags = br.ReadInt16();
                            name = ReadString(br);
                            int sz = br.ReadUInt16();
                            var struc = new StructType(module, name);
                            struc.Flags = (SymbolFlags)flags;
                            struc.SetSize(sz);
                            SymbolType st2 = (SymbolType)br.ReadByte();
                            while (st2 != 0)
                            {
                                flags = br.ReadInt16();
                                TypeSymbol ft = ReadType(br, module);
                                name = ReadString(br);
                                var offs = br.ReadUInt16();
                                var idx = br.ReadUInt16();
                                var f = new FieldSymbol(struc, name, ft);
                                f.Flags = (SymbolFlags)flags;
                                f.Offset = offs;
                                f.Index = idx;
                                st2 = (SymbolType)br.ReadByte();
                            }
                        }
                        break;
                    case SymbolType.Class:
                        {
                            flags = br.ReadInt16();
                            name = ReadString(br);
                            ClassType baseType = ReadType(br, module) as ClassType;
                            var sz = br.ReadUInt16();
                            var fcnt = br.ReadUInt16();
                            var cls = new ClassType(module, name);
                            cls.Flags = (SymbolFlags)flags;
                            //cls.Flags |= SymbolFlags.External;
                            cls.BaseClass = baseType;
                            cls.SetSize(sz);
                            cls.FieldCount = fcnt;
                            SymbolType st2 = (SymbolType)br.ReadByte();
                            while (st2 != 0)
                            {
                                switch (st2)
                                {
                                    case SymbolType.Field:
                                        {
                                            flags = br.ReadInt16();
                                            TypeSymbol ft = ReadType(br, module);
                                            name = ReadString(br);
                                            var offs = br.ReadUInt16();
                                            var idx = br.ReadUInt16();
                                            var field = new FieldSymbol(cls, name, ft);
                                            field.Flags = (SymbolFlags)flags;
                                            if(field.Flags.HasFlag(SymbolFlags.Static))
                                                field.Flags |= SymbolFlags.External;
                                            field.Offset = offs;
                                            field.Index = idx;
                                        }
                                        break;
                                    case SymbolType.Method:
                                        {
                                            flags = br.ReadInt16();
                                            name = ReadString(br);
                                            string fname = ReadString(br);
                                            if(fname == "")
                                                fname = cls.Name + "__" + name;
                                            int vslot = br.ReadInt16();
                                            var mi = new MethodSymbol(cls, name, null);
                                            mi.Flags = (SymbolFlags)flags;
                                            mi.VSlot = vslot;
                                            methodMap[mi] = fname;
                                        }
                                        break;
                                    case SymbolType.Property:
                                        {
                                            flags = br.ReadInt16();
                                            TypeSymbol pt = ReadType(br, module);
                                            name = ReadString(br);
                                            byte getset = br.ReadByte();
                                            //var getter = ReadString(br);
                                            //var setter = ReadString(br);
                                            var prop = new PropertySymbol(cls, name, pt);
                                            /*if (getter != "")
                                                getterMap[prop] = getter;
                                            if (setter != "")
                                                setterMap[prop] = setter;*/
                                            if((getset & 1) != 0)
                                                getterMap[prop] = "get_" + name;
                                            if((getset & 2) != 0)
                                                setterMap[prop] = "set_" + name;
                                        }
                                        break;
                                }
                                st2 = (SymbolType)br.ReadByte();
                            }
                            cls.VirtualMethods.Clear();
                            name = ReadString(br);
                            while (name != "")
                            {
                                var m = cls.FindChild(name) as MethodSymbol;
                                System.Diagnostics.Debug.Assert(m != null);
                                cls.VirtualMethods.Add(m);
                                name = ReadString(br);
                            }
                            if (cls.VirtualMethods.Count == 0)
                                cls.VirtualMethods.Add(null);
                        }
                        break;
                    case SymbolType.Template:
                        {
                            flags = br.ReadInt16();
                            name = ReadString(br);
                            Template tmpl = new Template(module, name);
                            tmpl.Flags = (SymbolFlags)flags;
                            string tp = ReadString(br);
                            while (tp != "")
                            {
                                tmpl.TemplateParams.Add(tp);
                                tp = ReadString(br);
                            }
                            tmpl.Source = ReadString(br);
                        }
                        break;
                }
                st = (SymbolType)br.ReadByte();
            }

            foreach (MethodSymbol m in methodMap.Keys)
            {
                Function fn = module.Find(methodMap[m]) as Function;
                System.Diagnostics.Debug.Assert(fn != null);
                m.SetFunction(fn);

                if (m.Flags.HasFlag(SymbolFlags.Static)
                    && (m.DataType as Function).Parameters.Count > 0
                    && (m.DataType as Function).Parameters[0].Name == "this")
                {
                    // load extension method
                    var typeToExtend = (m.DataType as Function).Parameters[0].DataType;
                    if (typeToExtend.IsPointer() && typeToExtend.ElementType.IsClass())
                        typeToExtend = typeToExtend.ElementType;
                    var extensionMethod = new MethodSymbol(typeToExtend, m.Name, m.DataType as Function);
                }
            }

            foreach (PropertySymbol p in getterMap.Keys)
            {
                MethodSymbol msym = p.Parent.Find(getterMap[p]) as MethodSymbol;
                System.Diagnostics.Debug.Assert(msym != null);
                p.Getter = msym;
            }

            foreach (PropertySymbol p in setterMap.Keys)
            {
                MethodSymbol msym = p.Parent.Find(setterMap[p]) as MethodSymbol;
                System.Diagnostics.Debug.Assert(msym != null);
                p.Setter = msym;
            }

            return module;
        }
    }
}
