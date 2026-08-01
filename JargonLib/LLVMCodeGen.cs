using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Jargon
{
    class LLVMLabel : Label
    {
        public string Name { get; set; }
        public LLVMLabel(string name)
        {
            Name = name;
        }
    }

    public class LLVMCodeGen : CodeVisitor
    {
        public StringBuilder sb = new StringBuilder();
        private Function function;
        private int label = 0;
        private Stack<LoopStatement> loops = new Stack<LoopStatement>();
        public Module Module;
        public CompileUnit Unit;
        public CompilerOptions CompileOptions;
        public ICompilerErrorListener CompilerErrorListener;
        private Stack<StatementBlock> blocks = new Stack<StatementBlock>();

        protected void Error(string s, string file, int line)
        {
            CompilerError err = CompilerError.Error(s, file, line, 1);
            CompilerErrorListener?.OnError(err);
        }

        private string NewLabel()
        {
            return "$label" + (++label);
        }

        private string NewTag()
        {
            return "%$t" + (++function.Temps);
        }

        public string IRType(TypeSymbol type, bool _long = false)
        {
            if (type.IsEnum())
            {
                return IRType(type.ElementType);
            }
            else if (type.IsArray())
            {
                ArrayType arrType = type as ArrayType;
                return "[" + arrType.ArraySize + " x " + IRType(arrType.ElementType) + "]";
            }
            else if (type.IsStruct() || type.IsClass())
            {
                return "%struct." + type.Name;
            }
            else if (type.IsPointer())
            {
                if (_long && type.ElementType is Function fn)
                {
                    return IRType(fn, _long);
                }
                else
                {
                    return "ptr";
                }
            }
            else if (type.IsFunction())
            {
                Function fn = type as Function;
                string n = IRType(fn.ReturnType);
                n += " (";
                for (int i = 0; i < fn.Parameters.Count; i++)
                {
                    if (i != 0)
                        n += ", ";
                    n += IRType(fn.Parameters[i].DataType);
                }
                if (fn.Flags.HasFlag(SymbolFlags.Variadic))
                {
                    if (fn.Parameters.Count > 0)
                        n += ", ...";
                    else
                        n += "...";
                }
                n += ")";
                return n;
            }
            else if (type == TypeSymbol.Void)
            {
                return "void";
            }
            else if (type == TypeSymbol.Bool)
            {
                return "i1";
            }
            else if (type == TypeSymbol.Byte || type == TypeSymbol.UByte)
            {
                return "i8";
            }
            else if (type == TypeSymbol.Short || type == TypeSymbol.UShort)
            {
                return "i16";
            }
            else if (type == TypeSymbol.Int || type == TypeSymbol.UInt)
            {
                return "i32";
            }
            else if (type == TypeSymbol.Long || type == TypeSymbol.ULong)
            {
                return "i64";
            }
            else if (type == TypeSymbol.Float)
            {
                return "float";
            }
            else if (type == TypeSymbol.Double)
            {
                return "double";
            }
            else
            {
                throw new InvalidOperationException();
            }
        }

        // Same as IRType() but returns 'ptr' for arrays.
        // Used on args in a fcall and on loads
        private string IRType2(TypeSymbol type, bool _long = false)
        {
            if (type.IsEnum())
            {
                return IRType(type.ElementType);
            }
            else if (type.IsArray())
            {
                return "ptr";
            }
            else if (type.IsStruct() || type.IsClass())
            {
                return "%struct." + type.Name;
            }
            else if (type.IsPointer())
            {
                if (_long && type.ElementType is Function fn)
                {
                    return IRType2(fn, _long);
                }
                else
                {
                    return "ptr";
                }
            }
            else if (type.IsFunction())
            {
                Function fn = type as Function;
                string n = IRType(fn.ReturnType);
                n += " (";
                for (int i = 0; i < fn.Parameters.Count; i++)
                {
                    if (i != 0)
                        n += ", ";
                    n += IRType(fn.Parameters[i].DataType);
                }
                if (fn.Flags.HasFlag(SymbolFlags.Variadic))
                {
                    if (fn.Parameters.Count > 0)
                        n += ", ...";
                    else
                        n += "...";
                }
                n += ")";
                return n;
            }
            else if (type == TypeSymbol.Void)
            {
                return "void";
            }
            else if (type == TypeSymbol.Bool)
            {
                return "i1";
            }
            else if (type == TypeSymbol.Byte || type == TypeSymbol.UByte)
            {
                return "i8";
            }
            else if (type == TypeSymbol.Short || type == TypeSymbol.UShort)
            {
                return "i16";
            }
            else if (type == TypeSymbol.Int || type == TypeSymbol.UInt)
            {
                return "i32";
            }
            else if (type == TypeSymbol.Long || type == TypeSymbol.ULong)
            {
                return "i64";
            }
            else if (type == TypeSymbol.Float)
            {
                return "float";
            }
            else if (type == TypeSymbol.Double)
            {
                return "double";
            }
            else
            {
                throw new InvalidOperationException();
            }
        }

        private static string ToLlvmHex(float f)
        {
            var fltBits = BitConverter.GetBytes((double)f);
            long doubleBits = (long)BitConverter.ToUInt64(fltBits, 0);
            return "0x" + doubleBits.ToString("X16");
        }

        private string dbg(Expression e)
        {
            if (e.line != 0 && function != null && !function.Flags.HasFlag(SymbolFlags.NoDebug)
                && CompileOptions.DebugInfo)
            {
                var loc = DINode.FunctionMap[function].GetLocation(e.line);
                return $", !dbg !{loc.ID}";
            }
            return "";
        }

        private string dbg(Statement s)
        {
            if (s.line != 0 && function != null && !function.Flags.HasFlag(SymbolFlags.NoDebug)
                && CompileOptions.DebugInfo)
            {
                var loc = DINode.FunctionMap[function].GetLocation(s.line);
                return $", !dbg !{loc.ID}";
            }
            return "";
        }

        public override void VisitConstantExpression(ConstantExpression e, VisitMode mode)
        {
            if (mode == VisitMode.Load)
            {
                if (e.DataType.IsBool())
                {
                    e.Tag = e.Value != 0 ? "true" : "false";
                }
                else if (e.DataType.IsPointer() && e.DataType.ElementType == TypeSymbol.Byte)
                {
                    e.Tag = '@' + e.CString;
                }
                else
                {
                    if (e.DataType.IsPointer() && e.Value == 0)
                    {
                        e.Tag = "null";
                    }
                    else if (e.DataType.IsFloatingPoint())
                    {
                        if (e.DataType == TypeSymbol.Float)
                            e.Tag = ToLlvmHex(e.FloatValue);
                        else if (e.DataType == TypeSymbol.Double)
                        {
                            e.Tag = e.DoubleValue.ToString();
                            if (e.Tag.IndexOf(".") == -1)
                                e.Tag += ".0";
                        }
                    }
                    else
                    {
                        if (e.DataType == TypeSymbol.Long || e.DataType == TypeSymbol.ULong)
                            e.Tag = e.Value64.ToString();
                        else
                            e.Tag = e.Value.ToString();
                    }
                }
            }
            else
            {
                Error("Cannot get the address of a constant expression", e.file, e.line);
            }
        }

        public override void VisitBinaryExpression(BinaryExpression e, VisitMode mode)
        {
            if (e.Op == BinOp.LAnd || e.Op == BinOp.LOr)
            {
                if (e.Op == BinOp.LAnd)
                {
                    /*string eval_right = NewLabel();
                    string false_end = NewLabel();
                    string merge = NewLabel();
                    e.Tag = NewTag();
                    e.Left.Visit(this, VisitMode.Load);
                    sb.AppendLine($"\tbr i1 {e.Left.Tag}, label %{eval_right}, label %{false_end}{dbg(e)}");
                    sb.AppendLine($"{eval_right}:");
                    e.Right.Visit(this, VisitMode.Load);
                    sb.AppendLine($"\tbr label %{merge}{dbg(e)}");
                    sb.AppendLine($"{false_end}:");
                    sb.AppendLine($"\tbr label %{merge}{dbg(e)}");
                    sb.AppendLine($"{merge}:");
                    sb.AppendLine($"\t{e.Tag} = phi i1 [0, %{false_end}], [{e.Right.Tag}, %{eval_right}]{dbg(e)}");*/

                    string rhs_label = NewLabel();   // was eval_right
                    string rhs_label2 = NewLabel();
                    string false_label = NewLabel();
                    string merge = NewLabel();

                    e.Tag = NewTag();

                    // Evaluate left operand
                    e.Left.Visit(this, VisitMode.Load);
                    ClearLingerRefs();
                    // Branch: if left == false → goto false_label, else evaluate right
                    sb.AppendLine($"\tbr i1 {e.Left.Tag}, label %{rhs_label}, label %{false_label}{dbg(e)}");

                    // ── Short-circuit path: left was false ─────────────────
                    sb.AppendLine($"{false_label}:");
                    sb.AppendLine($"\tbr label %{merge}{dbg(e)}");

                    // ── Evaluate right only when needed ────────────────────
                    sb.AppendLine($"{rhs_label}:");
                    e.Right.Visit(this, VisitMode.Load);
                    ClearLingerRefs();
                    sb.AppendLine($"\tbr label %{rhs_label2}{dbg(e)}");
                    sb.AppendLine($"{rhs_label2}:");
                    //ClearLingerRefs();
                    sb.AppendLine($"\tbr label %{merge}{dbg(e)}");

                    // ── Merge point ────────────────────────────────────────
                    sb.AppendLine($"{merge}:");
                    sb.AppendLine($"\t{e.Tag} = phi i1 [0, %{false_label}], [{e.Right.Tag}, %{rhs_label2}]{dbg(e)}");
                }
                else  // Or
                {
                    /*string true_end = NewLabel();
                    string eval_right = NewLabel();
                    string merge = NewLabel();
                    e.Tag = NewTag();
                    e.Left.Visit(this, VisitMode.Load);
                    sb.AppendLine($"\tbr i1 {e.Left.Tag}, label %{true_end}, label %{eval_right}{dbg(e)}");
                    sb.AppendLine($"{true_end}:");
                    sb.AppendLine($"\tbr label %{merge}{dbg(e)}");
                    sb.AppendLine($"{eval_right}:");
                    e.Right.Visit(this, VisitMode.Load);
                    sb.AppendLine($"\tbr label %{merge}{dbg(e)}");
                    sb.AppendLine($"{merge}:");
                    sb.AppendLine($"\t{e.Tag} = phi i1 [1, %{true_end}], [{e.Right.Tag}, %{eval_right}]{dbg(e)}");*/

                    string rhs_label = NewLabel();
                    string rhs_label2 = NewLabel();
                    string true_label = NewLabel();
                    string merge = NewLabel();

                    e.Tag = NewTag();

                    e.Left.Visit(this, VisitMode.Load);
                    ClearLingerRefs();
                    // Branch: if left == true → goto true_label, else evaluate right
                    sb.AppendLine($"\tbr i1 {e.Left.Tag}, label %{true_label}, label %{rhs_label}{dbg(e)}");

                    // ── Short-circuit path: left was true ──────────────────
                    sb.AppendLine($"{true_label}:");
                    sb.AppendLine($"\tbr label %{merge}{dbg(e)}");

                    // ── Evaluate right only when needed ────────────────────
                    sb.AppendLine($"{rhs_label}:");
                    e.Right.Visit(this, VisitMode.Load);
                    ClearLingerRefs();
                    sb.AppendLine($"\tbr label %{rhs_label2}{dbg(e)}");
                    sb.AppendLine($"{rhs_label2}:");
                    sb.AppendLine($"\tbr label %{merge}{dbg(e)}");

                    // ── Merge point ────────────────────────────────────────
                    sb.AppendLine($"{merge}:");
                    sb.AppendLine($"\t{e.Tag} = phi i1 [1, %{true_label}], [{e.Right.Tag}, %{rhs_label2}]{dbg(e)}");

                    //ClearLingerRefs();
                }
            }
            else
            {
                e.Left.Visit(this, VisitMode.Load);
                e.Right.Visit(this, VisitMode.Load);

                if (e.Left.DataType.IsPointer())
                {
                    if (e.Right.DataType.IsInteger())
                    {
                        if (e.Op == BinOp.Add)
                        {
                            e.Tag = NewTag();
                            sb.AppendLine($"\t{e.Tag} = getelementptr inbounds {IRType(e.Left.DataType.ElementType)}, ptr {e.Left.Tag}, {IRType(e.Right.DataType)} {e.Right.Tag}{dbg(e)}");
                        }
                        else if (e.Op == BinOp.Sub)
                        {
                            string temp = NewTag();
                            sb.AppendLine($"\t{temp} = sub {IRType(e.Right.DataType)} 0, {e.Right.Tag}{dbg(e)}");
                            e.Tag = NewTag();
                            sb.AppendLine($"\t{e.Tag} = getelementptr inbounds {IRType(e.Left.DataType.ElementType)}, ptr {e.Left.Tag}, {IRType(e.Right.DataType)} {temp}{dbg(e)}");
                        }
                        else
                        {
                            Error("Invalid pointer operation", e.file, e.line);
                        }
                    }
                    else if (e.Right.DataType.IsPointer())
                    {
                        if (e.Op == BinOp.Sub)
                        {
                            string temp1 = NewTag();
                            sb.AppendLine($"\t{temp1} = ptrtoint {IRType(e.Left.DataType)} {e.Left.Tag} to i64{dbg(e)}");
                            string temp2 = NewTag();
                            sb.AppendLine($"\t{temp2} = ptrtoint {IRType(e.Right.DataType)} {e.Right.Tag} to i64{dbg(e)}");
                            string temp3 = NewTag();
                            sb.AppendLine($"\t{temp3} = sub i64 {temp1}, {temp2}{dbg(e)}");
                            e.Tag = NewTag();
                            sb.AppendLine($"\t{e.Tag} = sdiv i64 {temp3}, {e.Left.DataType.ElementType.Size}{dbg(e)}");
                        }
                        else if (e.Op >= BinOp.Equal && e.Op <= BinOp.GEqual)
                        {
                            string op = "** ERR **";
                            if (e.Op == BinOp.Equal) op = "icmp eq";
                            else if (e.Op == BinOp.NEqual) op = "icmp ne";
                            else if (e.Op == BinOp.Less) op = "icmp ult";
                            else if (e.Op == BinOp.Greater) op = "icmp ugt";
                            else if (e.Op == BinOp.LEqual) op = "icmp ule";
                            else if (e.Op == BinOp.GEqual) op = "icmp uge";

                            e.Tag = NewTag();
                            sb.AppendLine($"\t{e.Tag} = {op} {IRType(e.Left.DataType)} {e.Left.Tag}, {e.Right.Tag}{dbg(e)}");
                        }
                        else
                        {
                            Error("Invalid pointer operation", e.file, e.line);
                        }
                    }
                }
                else if (e.Left.DataType.IsInteger())
                {
                    bool unsigned = e.Left.DataType.IsUnsigned();

                    string op = "add";
                    if (e.Op == BinOp.Add) op = "add";
                    else if (e.Op == BinOp.Sub) op = "sub";
                    else if (e.Op == BinOp.Mul) op = "mul";
                    else if (e.Op == BinOp.Div) op = unsigned ? "udiv" : "sdiv";
                    else if (e.Op == BinOp.Mod) op = unsigned ? "urem" : "srem";
                    else if (e.Op == BinOp.Shl) op = "shl";
                    else if (e.Op == BinOp.Shr) op = unsigned ? "lshr" : "ashr";
                    else if (e.Op == BinOp.And) op = "and";
                    else if (e.Op == BinOp.Or) op = "or";
                    else if (e.Op == BinOp.Xor) op = "xor";
                    else if (e.Op == BinOp.Equal) op = "icmp eq";
                    else if (e.Op == BinOp.NEqual) op = "icmp ne";
                    else if (e.Op == BinOp.Less) op = unsigned ? "icmp ult" : "icmp slt";
                    else if (e.Op == BinOp.Greater) op = unsigned ? "icmp ugt" : "icmp sgt";
                    else if (e.Op == BinOp.LEqual) op = unsigned ? "icmp ule" : "icmp sle";
                    else if (e.Op == BinOp.GEqual) op = unsigned ? "icmp uge" : "icmp sge";
                    else Error("Invalid operation", e.file, e.line);

                    e.Tag = NewTag();
                    sb.AppendLine($"\t{e.Tag} = {op} {IRType(e.Left.DataType)} {e.Left.Tag}, {e.Right.Tag}{dbg(e)}");
                }
                else if (e.Left.DataType.IsFloatingPoint())
                {
                    bool unsigned = e.Left.DataType.IsUnsigned();

                    string op = "fadd";
                    if (e.Op == BinOp.Add) op = "fadd";
                    else if (e.Op == BinOp.Sub) op = "fsub";
                    else if (e.Op == BinOp.Mul) op = "fmul";
                    else if (e.Op == BinOp.Div) op = "fdiv";
                    else if (e.Op == BinOp.Mod) op = "frem";
                    else if (e.Op == BinOp.Equal) op = "fcmp oeq";
                    else if (e.Op == BinOp.NEqual) op = "fcmp one";
                    else if (e.Op == BinOp.Less) op = "fcmp olt";
                    else if (e.Op == BinOp.Greater) op = "fcmp ogt";
                    else if (e.Op == BinOp.LEqual) op = "fcmp ole";
                    else if (e.Op == BinOp.GEqual) op = "fcmp oge";
                    else Error("Invalid operation", e.file, e.line);

                    e.Tag = NewTag();
                    sb.AppendLine($"\t{e.Tag} = {op} {IRType(e.Left.DataType)} {e.Left.Tag}, {e.Right.Tag}{dbg(e)}");
                }

                ReleaseExpression(e.Left, e);
                ReleaseExpression(e.Right, e);
            }
        }

        private void ReleaseExpression(Expression e, Expression d)
        {
            Expression f = e;
            while (f is CastExpression ce)
                f = ce.Expression;

            if (f.DataType.IsPointer() && f.DataType.ElementType.IsClass()
                    && IsFunctionCall(f))
            {
                sb.AppendLine($"\tcall void(ptr) @object__release(ptr {f.Tag}){dbg(d)}");
            }
        }

        public override void VisitUnaryExpression(UnaryExpression e, VisitMode mode)
        {
            e.Expression.Visit(this, VisitMode.Load);
            e.Tag = NewTag();

            if (e.Expression.DataType.IsFloatingPoint())
            {
                switch (e.Op)
                {
                    case UnaryOp.Neg:
                        sb.AppendLine($"\t{e.Tag} = fsub {IRType(e.DataType)} -0.0, {e.Expression.Tag}{dbg(e)}");
                        break;
                }
            }
            else
            {
                switch (e.Op)
                {
                    case UnaryOp.Neg:
                        sb.AppendLine($"\t{e.Tag} = sub {IRType(e.DataType)} 0, {e.Expression.Tag}{dbg(e)}");
                        break;
                    case UnaryOp.Not:
                        sb.AppendLine($"\t{e.Tag} = xor {IRType(e.DataType)} {e.Expression.Tag}, -1{dbg(e)}");
                        break;
                    case UnaryOp.LNot:
                        sb.AppendLine($"\t{e.Tag} = xor i1 {e.Expression.Tag}, true{dbg(e)}");
                        break;
                }
            }

            ReleaseExpression(e.Expression, e);
        }

        public override void VisitSymbolExpression(SymbolExpression e, VisitMode mode)
        {
            if (e.Symbol is ConstantValue cv)
            {
                cv.Value.Visit(this, VisitMode.Load);
                e.Tag = cv.Value.Tag;
                return;
            }

            string prefix = "%";
            if (e.Symbol is GlobalVariable || e.Symbol is Function)
                prefix = "@";

            if (e.Symbol is LocalVariable lv && lv.Offset > 0)
            {
                if (mode == VisitMode.Load)
                {
                    e.Tag = NewTag();
                    sb.AppendLine($"\t{e.Tag} = load {IRType2(lv.DataType)}, ptr %{lv.Name}.addr");
                }
                else
                {
                    e.Tag = '%' + lv.Name + ".addr";
                }
            }
            else
            {
                string name = e.Symbol.Name;
                if (e.Symbol is LocalVariable lv2)
                {
                    name += "_" + (-lv2.Offset).ToString("X");
                }
                if (mode == VisitMode.Load)
                {
                    if (e.Symbol is FieldSymbol fs && fs.Flags.HasFlag(SymbolFlags.Static))
                    {
                        prefix = "@";
                        name = fs.Parent.Name + "__" + fs.Name;
                    }
                    e.Tag = NewTag();
                    if (e.DataType.IsArray())
                        sb.AppendLine($"\t{e.Tag} = getelementptr inbounds ptr, ptr {prefix}{name}, i32 0{dbg(e)}");
                    else
                        sb.AppendLine($"\t{e.Tag} = load {IRType2(e.DataType)}, ptr {prefix}{name}{dbg(e)}");
                }
                else if (mode == VisitMode.Address)
                {
                    if (e.Symbol is FieldSymbol fs && fs.Flags.HasFlag(SymbolFlags.Static))
                    {
                        e.Tag = "@" + fs.Parent.Name + "__" + fs.Name;
                    }
                    else
                    {
                        if (e.Symbol is MethodSymbol ms)
                        {
                            if (ms.Flags.HasFlag(SymbolFlags.Virtual))
                                Error("Virtual method invalid here", e.file, e.line);
                            e.Tag = "@" + ms.DataType.Name;
                        }
                        else
                        {
                            e.Tag = prefix + name;
                        }
                    }
                }
            }
        }

        private bool IsFunctionCall(Expression e)
        {
            Expression f = e;
            while (f is CastExpression ce)
                f = ce.Expression;
            return f is FunctionCallExpression;
        }

        public override void VisitAssignmentExpression(AssignmentExpression e, VisitMode mode)
        {
            if (mode == VisitMode.Load)
            {
                if (e.Left is FieldExpression fs && fs.Field is PropertySymbol ps)
                {
                    // Now handled in AST Transform
                    Error("Property not expected after transform", e.file, e.line);
                }
                else if (e.Left.DataType.IsStruct() && e.Right.DataType == TypeSymbol.Int && e.Right is ConstantExpression ce && ce.Value == 0)
                {
                    e.Left.Visit(this, VisitMode.Address);
                    sb.AppendLine($"\tstore {IRType(e.Left.DataType)} zeroinitializer, ptr {e.Left.Tag}{dbg(e)}");
                    var t2 = NewTag();
                    sb.AppendLine($"\t{t2} = load {IRType2(e.Left.DataType)}, ptr {e.Left.Tag}{dbg(e)}");
                    e.Tag = t2;
                }
                else
                {
                    bool weak = false;

                    if (e.Left is FieldExpression fe && fe.Field is FieldSymbol fs2 && fs2.Flags.HasFlag(SymbolFlags.Weak))
                        weak = true;

                    /*if (e.Left.DataType.IsPointer() && e.Left.DataType.ElementType.IsClass() && e.Left is SymbolExpression se && se.Symbol is LocalVariable slv && slv.Offset > 0)
                    {
                        //Error($"Weak param {slv.Name} in {function.Name}", "", e.line);
                        weak = true; // parameters are weak
                    }*/

                    e.Right.Visit(this, VisitMode.Load);
                    e.Left.Visit(this, VisitMode.Address);

                    // Release old object* if not a declaration
                    if (!weak && !e.isInit && e.Left.DataType.IsPointer() && e.Left.DataType.ElementType.IsClass())
                    {
                        var temp = NewTag();
                        sb.AppendLine($"\t{temp} = load ptr, ptr {e.Left.Tag}");
                        sb.AppendLine($"\tcall void(ptr) @object__release(ptr {temp}){dbg(e)}");
                    }

                    var tag = e.Left.Tag;
                    sb.AppendLine($"\tstore {IRType(e.Left.DataType)} {e.Right.Tag}, ptr {e.Left.Tag}{dbg(e)}");
                    var t2 = NewTag();
                    sb.AppendLine($"\t{t2} = load {IRType2(e.Left.DataType)}, ptr {e.Left.Tag}{dbg(e)}");
                    e.Tag = t2;

                    if (e.decl != null && CompileOptions.DebugInfo)
                    {
                        var loc = DINode.FunctionMap[function].GetLocation(e.line);

                        sb.AppendLine($"\t#dbg_declare(ptr {tag}, !{DINode.LocalMap[e.decl].ID}, !DIExpression(), !{loc.ID})");
                    }

                    if (!weak && e.Left.DataType.IsPointer() && e.Left.DataType.ElementType.IsClass()
                        && !IsFunctionCall(e.Right))
                    {
                        // retain new object*
                        var temp = NewTag();
                        sb.AppendLine($"\t{temp} = load ptr, ptr {e.Left.Tag}");
                        sb.AppendLine($"\tcall void(ptr) @object__addRef(ptr {temp}){dbg(e)}");
                    }
                }
            }
            else
            {
                Error("Cannot get the address of an assignment", e.file, e.line);
            }
        }

        public override void VisitCastExpression(CastExpression e, VisitMode mode)
        {
            e.Expression.Visit(this, VisitMode.Load);

            if ((e.CastType.IsInteger() || e.CastType.IsBool()) && (e.Expression.DataType.IsInteger() || e.Expression.DataType.IsBool()))
            {
                if (e.CastType.IsBool() && !e.Expression.DataType.IsBool())
                {
                    e.Tag = NewTag();
                    sb.AppendLine($"\t{e.Tag} = icmp ne {IRType(e.Expression.DataType)} {e.Expression.Tag}, 0{dbg(e)}");
                }
                else if (e.CastType.Size < e.Expression.DataType.Size)
                {
                    e.Tag = NewTag();
                    sb.AppendLine($"\t{e.Tag} = trunc {IRType(e.Expression.DataType)} {e.Expression.Tag} to {IRType(e.CastType)}{dbg(e)}");
                }
                else if (e.CastType.Size > e.Expression.DataType.Size)
                {
                    if (e.Expression.DataType.IsUnsigned() || e.Expression.DataType.IsBool())
                    {
                        e.Tag = NewTag();
                        sb.AppendLine($"\t{e.Tag} = zext {IRType(e.Expression.DataType)} {e.Expression.Tag} to {IRType(e.CastType)}{dbg(e)}");
                    }
                    else
                    {
                        e.Tag = NewTag();
                        sb.AppendLine($"\t{e.Tag} = sext {IRType(e.Expression.DataType)} {e.Expression.Tag} to {IRType(e.CastType)}{dbg(e)}");
                    }
                }
                else
                {
                    e.Tag = e.Expression.Tag;
                }
            }
            else if (e.CastType.IsInteger() && e.Expression.DataType.IsPointer())
            {
                e.Tag = NewTag();
                sb.AppendLine($"\t{e.Tag} = ptrtoint {IRType(e.Expression.DataType)} {e.Expression.Tag} to {IRType(e.CastType)}{dbg(e)}");
            }
            else if (e.CastType.IsPointer() && e.Expression.DataType.IsInteger())
            {
                e.Tag = NewTag();
                sb.AppendLine($"\t{e.Tag} = inttoptr {IRType(e.Expression.DataType)} {e.Expression.Tag} to {IRType(e.CastType)}{dbg(e)}");
            }
            else if (e.CastType.IsPointer() && e.Expression.DataType.IsPointer() && e.Expression.DataType.ElementType is Function fn)
            {
                e.Tag = e.Expression.Tag;
            }
            else if (e.CastType.IsInteger() && e.Expression.DataType.IsFloatingPoint())
            {
                e.Tag = NewTag();
                if (e.CastType.IsUnsigned())
                    sb.AppendLine($"\t{e.Tag} = fptoui {IRType(e.Expression.DataType)} {e.Expression.Tag} to {IRType(e.CastType)}{dbg(e)}");
                else
                    sb.AppendLine($"\t{e.Tag} = fptosi {IRType(e.Expression.DataType)} {e.Expression.Tag} to {IRType(e.CastType)}{dbg(e)}");
            }
            else if (e.CastType.IsFloatingPoint() && e.Expression.DataType.IsInteger())
            {
                e.Tag = NewTag();
                if (e.Expression.DataType.IsUnsigned())
                    sb.AppendLine($"\t{e.Tag} = uitofp {IRType(e.Expression.DataType)} {e.Expression.Tag} to {IRType(e.CastType)}{dbg(e)}");
                else
                    sb.AppendLine($"\t{e.Tag} = sitofp {IRType(e.Expression.DataType)} {e.Expression.Tag} to {IRType(e.CastType)}{dbg(e)}");
            }
            else if (e.CastType == TypeSymbol.Float && e.Expression.DataType == TypeSymbol.Double)
            {
                e.Tag = NewTag();
                sb.AppendLine($"\t{e.Tag} = fptrunc {IRType(e.Expression.DataType)} {e.Expression.Tag} to {IRType(e.CastType)}{dbg(e)}");
            }
            else if (e.CastType == TypeSymbol.Double && e.Expression.DataType == TypeSymbol.Float)
            {
                e.Tag = NewTag();
                sb.AppendLine($"\t{e.Tag} = fpext {IRType(e.Expression.DataType)} {e.Expression.Tag} to {IRType(e.CastType)}{dbg(e)}");
            }
            else if (e.CastType == TypeSymbol.Bool && e.Expression.DataType.IsFloatingPoint())
            {
                e.Tag = NewTag();
                sb.AppendLine($"\t{e.Tag} = fcmp one {IRType(e.Expression.DataType)} {e.Expression.Tag}, 0.0{dbg(e)}");
            }
            else if (e.CastType == TypeSymbol.Bool && e.Expression.DataType.IsPointer())
            {
                e.Tag = NewTag();
                sb.AppendLine($"\t{e.Tag} = icmp ne {IRType(e.Expression.DataType)} {e.Expression.Tag}, null{dbg(e)}");
            }
            else
            {
                e.Tag = e.Expression.Tag;
            }
        }

        public override void VisitIndexExpression(IndexExpression e, VisitMode mode)
        {
            ArrayType arrType = e.Expression.DataType as ArrayType;

            if (mode == VisitMode.Load)
            {
                e.Expression.Visit(this, e.Expression.DataType.IsPointer() ? VisitMode.Load : VisitMode.Address);
                e.Index.Visit(this, VisitMode.Load);
                string temp = NewTag();
                if (e.Expression.DataType.IsPointer())
                    sb.AppendLine($"\t{temp} = getelementptr inbounds {IRType(e.DataType)}, ptr {e.Expression.Tag}, {IRType(e.Index.DataType)} {e.Index.Tag}{dbg(e)}");
                else
                    sb.AppendLine($"\t{temp} = getelementptr inbounds {IRType(e.Expression.DataType)}, ptr {e.Expression.Tag}, i32 0, {IRType(e.Index.DataType)} {e.Index.Tag}{dbg(e)}");
                e.Tag = NewTag();
                sb.AppendLine($"\t{e.Tag} = load {IRType(e.DataType)}, ptr {temp}{dbg(e)}");
            }
            else if (mode == VisitMode.Address)
            {
                e.Expression.Visit(this, e.Expression.DataType.IsPointer() ? VisitMode.Load : VisitMode.Address);
                e.Index.Visit(this, VisitMode.Load);
                e.Tag = NewTag();
                if (e.Expression.DataType.IsPointer())
                    sb.AppendLine($"\t{e.Tag} = getelementptr inbounds {IRType(e.DataType)}, ptr {e.Expression.Tag}, {IRType(e.Index.DataType)} {e.Index.Tag}{dbg(e)}");
                else
                    sb.AppendLine($"\t{e.Tag} = getelementptr inbounds {IRType(e.Expression.DataType)}, ptr {e.Expression.Tag}, i32 0, {IRType(e.Index.DataType)} {e.Index.Tag}{dbg(e)}");
            }

            if (e.Expression.DataType.IsPointer() && e.Expression.DataType.ElementType.IsClass() && IsFunctionCall(e.Expression))
            {
                DelayRelease dr = new DelayRelease();
                dr.tag = e.Expression.Tag;
                dr.dbg = dbg(e);
                lingerRefs.Add(dr);
            }
        }

        public override void VisitFieldExpression(FieldExpression e, VisitMode mode)
        {
            TypeSymbol st = null;
            if (e.Expression.DataType.IsPointer())
                st = e.Expression.DataType.ElementType;
            else
                st = e.Expression.DataType;

            if (e.Field is FieldSymbol fs)
            {
                if (mode == VisitMode.Load)
                {
                    e.Expression.Visit(this, e.Expression.DataType.IsPointer() ? VisitMode.Load : VisitMode.Address);
                    string temp = NewTag();
                    sb.AppendLine($"\t{temp} = getelementptr inbounds {IRType(st)}, ptr {e.Expression.Tag}, i32 0, i32 {fs.Index}{dbg(e)}");
                    e.Tag = NewTag();
                    sb.AppendLine($"\t{e.Tag} = load {IRType(e.DataType)}, ptr {temp}{dbg(e)}");
                }
                else
                {
                    e.Expression.Visit(this, e.Expression.DataType.IsPointer() ? VisitMode.Load : VisitMode.Address);
                    e.Tag = NewTag();
                    sb.AppendLine($"\t{e.Tag} = getelementptr inbounds {IRType(st)}, ptr {e.Expression.Tag}, i32 0, i32 {fs.Index}{dbg(e)}");
                }

                if (e.Expression.DataType.IsPointer() && e.Expression.DataType.ElementType.IsClass() && IsFunctionCall(e.Expression))
                {
                    DelayRelease dr = new DelayRelease();
                    dr.tag = e.Expression.Tag;
                    dr.dbg = dbg(e);
                    lingerRefs.Add(dr);
                }
            }
            else if (e.Field is MethodSymbol ms)
            {
                e.Tag = '@' + ms.DataType.Name;
            }
            else if (e.Field is PropertySymbol ps)
            {
                // Now handled in AST Transform
                Error("Property not expected after transform", e.file, e.line);
            }
            else if (e.Field is EnumValue ev)
            {
                e.Tag = ev.Value.ToString();
            }
        }

        public override void VisitAddressOfExpression(AddressOfExpression e, VisitMode mode)
        {
            if (mode == VisitMode.Load)
            {
                e.Expression.Visit(this, VisitMode.Address);
                e.Tag = e.Expression.Tag;
            }
            else
            {
                Error("Cannot get the address of an address", e.file, e.line);
            }
        }

        public override void VisitDerefExpression(DerefExpression e, VisitMode mode)
        {
            if (mode == VisitMode.Load)
            {
                e.Expression.Visit(this, VisitMode.Load);
                e.Tag = NewTag();
                sb.AppendLine($"\t{e.Tag} = load {IRType(e.DataType)}, ptr {e.Expression.Tag}{dbg(e)}");
            }
            else if (mode == VisitMode.Address)
            {
                e.Expression.Visit(this, VisitMode.Load);
                e.Tag = e.Expression.Tag;
            }
        }

        struct DelayRelease
        {
            public string tag;
            public string dbg;
        }

        List<DelayRelease> lingerRefs = new List<DelayRelease>();

        public override void VisitFunctionCallExpression(FunctionCallExpression e, VisitMode mode)
        {
            e.Callee.Visit(this, VisitMode.Address);

            Function fn = null;
            if (e.Callee.DataType is Function)
                fn = (Function)e.Callee.DataType;
            else if (e.Callee.DataType.IsPointer())
            {
                if (e.Callee.DataType.ElementType is Function)
                    fn = (Function)e.Callee.DataType.ElementType;
            }
            if (fn == null)
            {
                Error("Could not find function type", e.file, e.line);
                return;
            }

            if (fn.Flags.HasFlag(SymbolFlags.Verbatim))
            {
                sb.Append("\t");
                if (fn.ReturnType != TypeSymbol.Void)
                {
                    var tag = NewTag();
                    sb.Append($"{tag} = ");
                    e.Tag = tag;
                }
                string v = fn.Verbatim;

                foreach (var a in e.Arguments)
                {
                    if (a is SymbolExpression se && se.Symbol is Variable sv)
                    {
                        v = v.Replace("#" + sv.Name, sv.TagName);
                    }
                }

                sb.AppendLine(v);
                return;
            }

            for (int i = e.Arguments.Count - 1; i >= 0; i--)
            {
                e.Arguments[i].Visit(this, VisitMode.Load);
            }

            sb.Append($"\t");

            if (e.DataType != TypeSymbol.Void)
            {
                e.Tag = NewTag();
                sb.Append($"{e.Tag} = ");
            }

            sb.Append($"call {IRType(e.Callee.DataType, true)} {e.Callee.Tag}(");

            for (int i = 0; i < e.Arguments.Count; i++)
            {
                sb.Append($"{IRType2(e.Arguments[i].DataType)} {e.Arguments[i].Tag}");
                if (i < e.Arguments.Count - 1)
                    sb.Append(',');
            }
            sb.AppendLine($"){dbg(e)}");

            for (int i = 0; i < e.Arguments.Count; i++)
            {
                if (fn.Name != "object__addRef" && fn.Name != "object_release"
                    && e.Arguments[i].DataType.IsPointer() && e.Arguments[i].DataType.ElementType.IsClass()
                    && IsFunctionCall(e.Arguments[i]))
                {
                    DelayRelease dr = new DelayRelease();
                    dr.tag = e.Arguments[i].Tag;
                    dr.dbg = dbg(e);
                    lingerRefs.Add(dr);
                }
            }
        }

        public override void VisitTernaryExpression(TernaryExpression e, VisitMode mode)
        {
            string positive = NewLabel();
            string positive2 = NewLabel();
            string negative = NewLabel();
            string negative2 = NewLabel();
            string phi = NewLabel();

            e.Tag = NewTag();
            e.Condition.Visit(this, VisitMode.Load);
            sb.AppendLine($"\tbr i1 {e.Condition.Tag}, label %{positive}, label %{negative}{dbg(e)}");
            sb.AppendLine($"{positive}:");
            if (e.Positive.Tag == null)
                e.Positive.Visit(this, mode);
            sb.AppendLine($"\tbr label %{positive2}{dbg(e)}");
            sb.AppendLine($"{positive2}:");
            sb.AppendLine($"\tbr label %{phi}{dbg(e)}");
            sb.AppendLine($"{negative}:");
            if (e.Negative.Tag == null)
                e.Negative.Visit(this, mode);
            sb.AppendLine($"\tbr label %{negative2}{dbg(e)}");
            sb.AppendLine($"{negative2}:");
            sb.AppendLine($"\tbr label %{phi}{dbg(e)}");
            sb.AppendLine($"{phi}:");
            sb.AppendLine($"\t{e.Tag} = phi {(mode == VisitMode.Load ? IRType(e.Positive.DataType) : "ptr")} [{e.Positive.Tag}, %{positive2}], [{e.Negative.Tag}, %{negative2}]{dbg(e)}");
        }

        public override void VisitSizeOfExpression(SizeOfExpression e, VisitMode mode)
        {
            if (mode == VisitMode.Load)
            {
                e.Tag = e.SizeType.Size.ToString();
            }
        }

        public override void VisitPostFixExpression(PostFixExpression e, VisitMode mode)
        {
            e.Operand.Visit(this, mode);
            var tag = e.Operand.Tag;
            e.Operation.Visit(this, mode);
            e.Tag = tag;
        }

        private void ClearLingerRefs()
        {
            lingerRefs.Reverse();
            foreach (var dr in lingerRefs)
            {
                sb.AppendLine($"\tcall void(ptr) @object__release(ptr {dr.tag}){dr.dbg}");
            }
            lingerRefs.Clear();
        }

        public override void VisitExpressionStatement(ExpressionStatement s)
        {
            s.Expression.Visit(this, VisitMode.Load);

            ClearLingerRefs();
        }

        public override void VisitReturnStatement(ReturnStatement s)
        {
            if (s.Expression != null)
            {
                s.Expression.Visit(this, VisitMode.Load);

                if (s.Expression.DataType.IsPointer() && s.Expression.DataType.ElementType.IsClass()
                    && !IsFunctionCall(s.Expression))
                {
                    sb.AppendLine($"\tcall void(ptr) @object__addRef(ptr {s.Expression.Tag}){dbg(s)}");
                }

                ClearLingerRefs();

                sb.AppendLine($"\tstore {IRType(function.ReturnType)} {s.Expression.Tag}, ptr %$retVal{dbg(s)}");
            }

            var arr = blocks.ToArray();
            foreach (var a in arr)
                ClearStatementBlock(a);

            sb.AppendLine($"\tbr label %{"$" + function.Name + "_end"}{dbg(s)}");
        }

        public override void VisitDebugStatement(DebugStatement s)
        {
            sb.AppendLine($"\tcall void @llvm.debugtrap(){dbg(s)}");
        }

        private void ClearStatementBlock(StatementBlock s)
        {
            var locals = s.Scope.children.ToArray();
            Array.Reverse(locals);

            var dbg = "";
            if (s.endLine != 0 && function != null && !function.Flags.HasFlag(SymbolFlags.NoDebug) && CompileOptions.DebugInfo)
            {
                var loc = DINode.FunctionMap[function].GetLocation(s.endLine);
                dbg = $", !dbg !{loc.ID}";
            }

            foreach (var l in locals)
            {
                if (l is LocalVariable lv)
                {
                    if (lv.DataType.IsPointer() && lv.DataType.ElementType.IsClass())
                    {
                        var temp = NewTag();
                        sb.AppendLine($"\t{temp} = load ptr, ptr %{lv.Name + "_" + (-lv.Offset).ToString("X")}");
                        sb.AppendLine($"\tcall void(ptr) @object__release(ptr {temp}){dbg}");
                    }
                }
            }
        }

        public override void VisitStatementBlock(StatementBlock s)
        {
            BlockScope bs = s.Scope;
            for (int i = 0; i < bs.children.Count; i++)
            {
                if (bs.children[i] is LocalVariable lv && lv.DataType.IsClassRef())
                {
                    //sb.AppendLine($"\t%{lv.Name}.addr = alloca {IRType2(lv.DataType)}{dbg(s)}");
                    sb.AppendLine($"\tstore ptr null, ptr %{lv.Name + "_" + (-lv.Offset).ToString("X")}");
                }
            }

            blocks.Push(s);

            foreach (var e in s.Statements)
            {
                e.Visit(this);
            }

            ClearStatementBlock(s);

            blocks.Pop();
        }

        public override void VisitIfStatement(IfStatement s)
        {
            s.Condition.Visit(this, VisitMode.Load);
            ReleaseExpression(s.Condition, s.Condition);
            ClearLingerRefs();
            string label1 = NewLabel();
            string label2 = NewLabel();
            string label3 = NewLabel();
            sb.AppendLine($"\tbr i1 {s.Condition.Tag}, label %{label1}, label %{label2}{dbg(s)}");
            sb.AppendLine(label1 + ":");
            s.Then.Visit(this);
            sb.AppendLine($"\tbr label %{label3}");
            sb.AppendLine(label2 + ":");
            if (s.Else != null)
            {
                s.Else.Visit(this);
            }
            sb.AppendLine($"\tbr label %{label3}");
            sb.AppendLine(label3 + ":");
        }

        public override void VisitWhileStatement(WhileStatement s)
        {
            string label1 = NewLabel();
            string label2 = NewLabel();
            string label3 = NewLabel();
            s.ContinueLabel = new LLVMLabel(label1);
            s.BreakLabel = new LLVMLabel(label3);
            loops.Push(s);
            sb.AppendLine($"\tbr label %{label1}");
            sb.AppendLine(label1 + ":");
            s.Condition.Visit(this, VisitMode.Load);
            ReleaseExpression(s.Condition, s.Condition);
            ClearLingerRefs();
            sb.AppendLine($"\tbr i1 {s.Condition.Tag}, label %{label2}, label %{label3}");
            sb.AppendLine(label2 + ":");
            s.Body.Visit(this);
            sb.AppendLine($"\tbr label %{label1}");
            sb.AppendLine(label3 + ":");
            loops.Pop();
        }

        public override void VisitDoStatement(DoStatement s)
        {
            string label1 = NewLabel();
            string label2 = NewLabel();
            s.ContinueLabel = new LLVMLabel(label1);
            s.BreakLabel = new LLVMLabel(label2);
            loops.Push(s);
            sb.AppendLine($"\tbr label %{label1}");
            sb.AppendLine(label1 + ":");
            s.Body.Visit(this);
            s.Condition.Visit(this, VisitMode.Load);
            ReleaseExpression(s.Condition, s.Condition);
            ClearLingerRefs();
            sb.AppendLine($"\tbr i1 {s.Condition.Tag}, label %{label1}, label %{label2}");
            sb.AppendLine($"\tbr label %{label1}");
            sb.AppendLine(label2 + ":");
            loops.Pop();
        }

        public override void VisitForStatement(ForStatement s)
        {
            string label1 = NewLabel();
            string label2 = NewLabel();
            string label3 = NewLabel();
            string label4 = NewLabel();

            s.ContinueLabel = new LLVMLabel(label3);
            s.BreakLabel = new LLVMLabel(label4);

            loops.Push(s);
            if (s.Init != null)
            {
                s.Init.Visit(this, VisitMode.Load);
                ReleaseExpression(s.Init, s.Init);
                ClearLingerRefs();
            }
            sb.AppendLine($"\tbr label %{label1}");
            sb.AppendLine(label1 + ":");
            if (s.Condition != null)
            {
                s.Condition.Visit(this, VisitMode.Load);
                ReleaseExpression(s.Condition, s.Condition);
                ClearLingerRefs();
            }
            sb.AppendLine($"\tbr i1 {s.Condition.Tag}, label %{label2}, label %{label4}");
            sb.AppendLine($"\tbr label %{label2}");
            sb.AppendLine(label2 + ":");
            s.Body.Visit(this);
            sb.AppendLine($"\tbr label %{label3}");
            sb.AppendLine(label3 + ":");
            if (s.Iter != null)
            {
                s.Iter.Visit(this, VisitMode.Load);
                ReleaseExpression(s.Iter, s.Iter);
                ClearLingerRefs();
            }
            sb.AppendLine($"\tbr label %{label1}");
            sb.AppendLine(label4 + ":");
            loops.Pop();
        }

        public override void VisitBreakStatement(BreakStatement s)
        {
            LoopStatement loop = loops.Peek();

            var blockList = blocks.ToArray();
            foreach (StatementBlock block in blockList)
            {
                ClearStatementBlock(block);
                if (block == loop.Body)
                    break;
            }

            sb.AppendLine($"\tbr label %{(loop.BreakLabel as LLVMLabel).Name}{dbg(s)}");
        }

        public override void VisitContinueStatement(ContinueStatement s)
        {
            LoopStatement loop = loops.Peek();

            var blockList = blocks.ToArray();
            foreach (StatementBlock block in blockList)
            {
                ClearStatementBlock(block);
                if (block == loop.Body)
                    break;
            }

            sb.AppendLine($"\tbr label %{(loop.ContinueLabel as LLVMLabel).Name}{dbg(s)}");
        }

        public override void VisitGlobalVariable(GlobalVariable s)
        {
            /*if(s.Unit != this.Unit)
            {
                if (s.Flags.HasFlag(SymbolFlags.External))
                    sb.AppendLine($"@{s.Name} = external dllimport global {IRType(s.DataType)}");
                else
                    sb.AppendLine($"@{s.Name} = external global {IRType(s.DataType)}");
                return;
            }*/
            string dbg = "";
            if (CompileOptions.DebugInfo)
            {
                var gv = new DIGlobalVariable();
                gv.name = s.Name;
                gv.linkageName = s.Name;
                gv.type = DINode.GetTypeInfo(s.DataType);
                var gve = new DIGlobalVariableExpression();
                gve.var = gv;
                dbg = $", !dbg !{gve.ID}";
            }

            string linkeage = "";
            if (s.Name == "object__Yoyo")
                linkeage = "dllexport ";

            if (s.Init != null && s.DataType.IsPointer() && s.DataType.ElementType == TypeSymbol.Byte
                && s.Init.DataType.IsPointer() && s.Init.DataType.ElementType == TypeSymbol.Byte
                && s.Init is ConstantExpression cstr)
            {
                sb.AppendLine($"@{s.Name} = global ptr @{cstr.CString}{dbg}");
            }
            else if (s.DataType.IsStruct() || s.DataType.IsArray() || s.DataType.IsPointer())
            {
                sb.AppendLine($"@{s.Name} = global {IRType(s.DataType)} zeroinitializer{dbg}");
            }
            else
            {
                long value = 0;
                sb.AppendLine($"@{s.Name} = {linkeage}global {IRType(s.DataType)} {value}{dbg}");
            }
        }

        public override void VisitStruct(StructType s)
        {
            if (s.Flags.HasFlag(SymbolFlags.Union))
            {
                sb.Append("%struct." + s.Name + $" = type {{ [{s.Size} x i8] }}");
            }
            else
            {
                sb.Append("%struct." + s.Name + " = type {");
                foreach (FieldSymbol fs in s.Children)
                {
                    sb.Append(IRType(fs.DataType));
                    if (fs != s.Children.Last())
                        sb.Append(",");
                }
                sb.AppendLine("}");
            }
        }

        private void ListFields(ClassType cls, List<FieldSymbol> fields)
        {
            if (cls.BaseClass != null)
                ListFields(cls.BaseClass, fields);

            foreach (var c in cls.children)
            {
                if (c is FieldSymbol fs)
                    fields.Add(fs);
            }
        }

        public override void VisitClass(ClassType s)
        {
            sb.Append("%struct." + s.Name + " = type {");
            List<FieldSymbol> fields = new List<FieldSymbol>();
            ListFields(s, fields);
            bool first = true;
            foreach (var fs in fields)
            {
                if (fs.Flags.HasFlag(SymbolFlags.Static))
                    continue;
                if (!first)
                    sb.Append(",");
                sb.Append(IRType(fs.DataType));
                first = false;
            }
            sb.AppendLine("}");

            if (s.Unit == Unit)
            {
                foreach (var fs in fields)
                {
                    if (fs.Flags.HasFlag(SymbolFlags.Static))
                    {
                        var gv = new GlobalVariable(null, s.Name + "__" + fs.Name, fs.DataType, null);
                        gv.Visit(this);
                    }
                }
            }
        }

        public override void VisitFunction(Function f)
        {
            if (f.Body == null && !f.Flags.HasFlag(SymbolFlags.External))
                return;

            function = f;
            //sb.AppendLine("; Function Attrs: noinline nounwind optnone uwtable");

            bool isDLL = System.IO.Path.GetExtension(CompileOptions.OutputFileName).ToLower() == ".dll";

            string linkage = f.Flags.HasFlag(SymbolFlags.Internal) ? "internal " : (isDLL ? "dllexport " : "");
            bool inOtherUnit = Unit != null && f.Unit != Unit;

            if(f.Name == "DllMain")
                linkage = "";

            if (f.Flags.HasFlag(SymbolFlags.External) || inOtherUnit)
                sb.Append($"declare {IRType(f.ReturnType)} @{f.Name}(");
            else
                sb.Append($"define {linkage}{IRType(f.ReturnType)} @{f.Name}(");

            foreach (var par in f.Parameters)
            {
                if(par != f.Parameters.First())
                    sb.Append(", ");
                sb.Append($"{IRType(par.DataType)} %{par.Name}");
            }

            if (f.Flags.HasFlag(SymbolFlags.Variadic))
            {
                if (f.Parameters.Count > 0)
                    sb.Append(", ...");
                else
                    sb.Append("...");
            }

            sb.Append(")");

            if (f.Flags.HasFlag(SymbolFlags.External) || inOtherUnit)
            {
                sb.AppendLine("");
                return;
            }

            if (!f.Flags.HasFlag(SymbolFlags.NoDebug) && CompileOptions.DebugInfo)
                sb.Append($" !dbg !{DINode.FunctionMap[f].ID} ");

            sb.AppendLine("{");

            sb.AppendLine("$" + f.Name + "_entry:");

            foreach (var p in f.Parameters)
            {
                sb.AppendLine($"\t%{p.Name}.addr = alloca {IRType(p.DataType)}");
                sb.AppendLine($"\tstore {IRType(p.DataType)} %{p.Name}, ptr %{p.Name}.addr");
                p.TagName = $"%{p.Name}.addr";
            }

            if (!f.Flags.HasFlag(SymbolFlags.NoDebug) && CompileOptions.DebugInfo)
            {
                foreach (var lv in f.Parameters)
                {
                    var loc = DINode.FunctionMap[function].GetLocation(f.Line);
                    sb.AppendLine($"\t#dbg_declare(ptr %{lv.Name}.addr, !{DINode.LocalMap[lv].ID}, !DIExpression(), !{loc.ID})");
                }
            }

            // Local variables declaration
            foreach (var child in f.Locals)
            {
                if (child is LocalVariable lv && lv.Offset < 0)
                {
                    sb.AppendLine($"\t%{lv.Name + "_" + (-lv.Offset).ToString("X")} = alloca {IRType(lv.DataType)}");
                    if (lv.Init == null)
                    {
                        if (CompileOptions.DebugInfo)
                        {
                            var loc = DINode.FunctionMap[function].GetLocation(lv.Line);
                            sb.AppendLine($"\t#dbg_declare(ptr %{lv.Name + "_" + (-lv.Offset).ToString("X")}, !{DINode.LocalMap[lv].ID}, !DIExpression(), !{loc.ID})");
                        }

                        /*if(lv.DataType.IsClassRef())
                            sb.AppendLine($"\tstore ptr null, ptr %{lv.Name + "_" + (-lv.Offset).ToString("X")}");*/
                    }

                    lv.TagName = $"%{lv.Name + "_" + (-lv.Offset).ToString("X")}";
                }
            }

            if (f.ReturnType != TypeSymbol.Void)
            {
                sb.AppendLine($"\t%$retVal = alloca {IRType(f.ReturnType)}");
            }

            // Init to zero variables that are object*
            foreach (var child in f.Locals)
            {
                if (child is LocalVariable lv && lv.Offset < 0 && lv.DataType.IsPointer() && lv.DataType.ElementType.IsClass())
                {
                    sb.AppendLine($"\tstore ptr null, ptr %{lv.Name + "_" + (-lv.Offset).ToString("X")}");
                }
            }

            if (f.Body != null)
            {
                f.Body.Visit(this);
            }

            string dbg = "";
            if (!f.Flags.HasFlag(SymbolFlags.NoDebug) && CompileOptions.DebugInfo)
            {
                var endLoc = DINode.FunctionMap[f].GetLocation(f.Body.endLine);
                dbg = $", !dbg !{endLoc.ID}";
            }

            sb.AppendLine($"\tbr label %{"$" + f.Name + "_end"}");
            sb.AppendLine("$" + f.Name + "_end:");

            if (f.ReturnType != TypeSymbol.Void)
            {
                string temp = NewTag();
                sb.AppendLine($"\t{temp} = load {IRType(f.ReturnType)}, ptr %$retVal{dbg}");
                sb.AppendLine($"\tret {IRType(f.ReturnType)} {temp}{dbg}");
            }
            else
            {
                sb.AppendLine($"\tret void{dbg}");
            }

            sb.AppendLine("}");
            function = null;
        }

        private string Escape(string str)
        {
            string res = "";
            foreach (var c in str)
            {
                if (c < ' ' || (int)c > 127 || c == '\"')
                {
                    res += "\\" + ((int)c).ToString("X2");
                }
                else if (c == '\\')
                {
                    res += "\\\\";
                }
                else
                {
                    res += c;
                }
            }
            return res;
        }

        public override void VisitModule(Module m)
        {
            sb.AppendLine("target datalayout = \"e-m:w-p270:32:32-p271:32:32-p272:64:64-i64:64-i128:128-f80:128-n8:16:32:64-S128\"");
            sb.AppendLine("target triple = \"x86_64-pc-windows-msvc19.44.35215\"");
            sb.AppendLine("");

            foreach (var k in m.Strings.Keys)
            {
                var s = m.Strings[k];
                sb.AppendLine($"@{k} = private constant[{s.Length + 1} x i8] c\"{Escape(s)}\\00\", align 1");
            }

            foreach (var child in m.children)
            {
                if (child.Name == "")
                    continue;
                if (child.SymbolType != SymbolType.Struct && child.SymbolType != SymbolType.Class)
                    continue;
                child.Visit(this);
            }

            foreach (var child in m.Children)
            {
                if (child.Name == "")
                    continue;
                if (child.SymbolType == SymbolType.Struct || child.SymbolType == SymbolType.Class)
                    continue;
                child.Visit(this);
            }

            sb.AppendLine("@llvm.global_ctors = appending global [1 x { i32, ptr, ptr }]\r\n    [{ i32, ptr, ptr } { i32 65535, ptr @static_init, ptr null }]");
        }

        public void VisitUnit(CompileUnit cu, Module module, DICompileUnit unit, bool tinfo)
        {
            sb.AppendLine("; ================================================================================");
            sb.AppendLine($"; Module: {cu.FileName}");
            sb.AppendLine("; ================================================================================");
            sb.AppendLine("target datalayout = \"e-m:w-p270:32:32-p271:32:32-p272:64:64-i64:64-i128:128-f80:128-n8:16:32:64-S128\"");
            sb.AppendLine("target triple = \"x86_64-pc-windows-msvc19.44.35215\"");
            sb.AppendLine("");

            sb.AppendLine("; ================================================================================");
            sb.AppendLine("; Strings");
            sb.AppendLine("; ================================================================================");

            foreach (var k in cu.Strings)
            {
                var s = module.Strings[k];
                var len = s.Length + 1;
                var esc = Escape(s);
                /*if (esc.StartsWith("@%s = private constant"))
                    len = 50;*/
                sb.AppendLine($"@{k} = private constant[{len} x i8] c\"{esc}\\00\", align 1");
            }

            sb.AppendLine("; ================================================================================");
            sb.AppendLine("; SymbolRefs");
            sb.AppendLine("; ================================================================================");

            foreach (var sr in cu.SymbolRefs)
            {
                //sb.AppendLine("; " + sr.GetType().Name);
                if (sr is GlobalVariable gv)
                {
                    if (gv.Flags.HasFlag(SymbolFlags.External))
                        sb.AppendLine($"@{gv.Name} = external dllimport global {IRType(gv.DataType)}");
                    else
                        sb.AppendLine($"@{gv.Name} = external global {IRType(gv.DataType)}");
                }
                else
                {
                    sr.Visit(this);
                }
            }

            sb.AppendLine("; ================================================================================");
            sb.AppendLine("; Structs");
            sb.AppendLine("; ================================================================================");

            foreach (var s in cu.Symbols)
            {
                if ((s is ClassType) || (s is StructType))
                    s.Visit(this);
            }

            sb.AppendLine("; ================================================================================");
            sb.AppendLine("; Code");
            sb.AppendLine("; ================================================================================");

            foreach (var s in cu.Symbols)
            {
                if (!((s is ClassType) || (s is StructType)))
                {
                    if (s is Function fn && !fn.Flags.HasFlag(SymbolFlags.External))
                    {
                        sb.AppendLine("; --------------------------------------------------------------------------------");
                        sb.AppendLine("; " + fn.ToString());
                        sb.AppendLine("; --------------------------------------------------------------------------------");
                    }
                    s.Visit(this);
                }
            }

            sb.AppendLine("; ================================================================================");
            sb.AppendLine("; Static Init/DeInit");
            sb.AppendLine("; ================================================================================");

            sb.AppendLine("declare i32 @_onexit(ptr %p)");
            sb.AppendLine("define internal void @register_cleanup()");
            sb.AppendLine("{");
            sb.AppendLine("    %fp = bitcast void()* @" + Path.GetFileNameWithoutExtension(cu.FileName) + "_static_deinit to ptr");
            sb.AppendLine("    %res = call i32 @_onexit(ptr %fp)");
            sb.AppendLine("    ret void");
            sb.AppendLine("}");

            sb.AppendLine("@llvm.global_ctors = appending global [2 x { i32, ptr, ptr }]\r\n"
                + "\t[{ i32, ptr, ptr } { i32 65535, ptr @" + Path.GetFileNameWithoutExtension(cu.FileName) + "_static_init, ptr null },\r\n"
                + "\t { i32, ptr, ptr } { i32 65535, ptr @register_cleanup, ptr null }]");

            if (CompileOptions.DebugInfo)
            {
                sb.AppendLine("; ================================================================================");
                sb.AppendLine("; Debug Info");
                sb.AppendLine("; ================================================================================");

                sb.AppendLine($"!llvm.dbg.cu = !{{!{unit.ID}}}");

                DIText dt1 = new DIText() { text = "!{ i32 2, !\"CodeView\", i32 1}" };
                DIText dt2 = new DIText() { text = "!{ i32 2, !\"Debug Info Version\", i32 3}" };
                DIText dt3 = new DIText() { text = "!{ i32 1, !\"wchar_size\", i32 2}" };
                DIText dt4 = new DIText() { text = "!{ i32 8, !\"PIC Level\", i32 2}" };
                DIText dt5 = new DIText() { text = "!{ i32 7, !\"uwtable\", i32 2}" };
                DIText dt6 = new DIText() { text = "!{ i32 1, !\"MaxTLSAlign\", i32 65536}" };
                DIText dt7 = new DIText() { text = "!{ !\"Jargon version 1.0.0\"}" };

                sb.AppendLine($"!llvm.module.flags = !{{!{dt1.ID}, !{dt2.ID}, !{dt3.ID}, !{dt4.ID}, !{dt5.ID}, !{dt6.ID}}}");
                sb.AppendLine($"!llvm.ident = !{{!{dt7.ID}}}");

                foreach (var g in DINode.Globals)
                    unit.globals.Add(g);

                foreach (DINode node in DINode.AllNodes)
                {
                    node.Serialize(sb);
                }
            }

            if (tinfo)
            {
                MemoryStream ms = new MemoryStream();
                TypeInfo.Write(ms, module);
                var arr = ms.ToArray();
                sb.Append($"@type_info = private constant [{arr.Length} x i8] c\"");
                foreach (var i8 in arr)
                    sb.Append("\\" + i8.ToString("X2"));
                sb.AppendLine("\", section \".module\"");
                sb.AppendLine("@llvm.used = appending global [1 x ptr] [ptr @type_info], section \"llvm.metadata\"");
            }
        }
    }
}