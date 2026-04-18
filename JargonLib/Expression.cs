using System.Collections.Generic;

namespace Jargon
{
    public abstract class Expression
    {
        public string file;
        public int line;

        public abstract TypeSymbol DataType { get; }
        public abstract void Visit(CodeVisitor visitor, VisitMode mode);
        public string Tag { get; set; }
        //public bool clearFlag = false;
    }

    public class ConstantExpression : Expression
    {
        public int Value;
        public long Value64;
        public string CString;
        private TypeSymbol type;
        public float FloatValue;
        public double DoubleValue;

        public override TypeSymbol DataType => type;

        public ConstantExpression()
        {
            this.Value = 0;
            this.type = TypeSymbol.Void.GetPointerType();
        }

        public ConstantExpression(bool value)
        {
            this.Value = value ? 1 : 0;
            this.type = TypeSymbol.Bool;
        }

        public ConstantExpression(byte value)
        {
            this.Value = value;
            this.type = TypeSymbol.Byte;
        }

        public ConstantExpression(int value)
        {
            this.Value = value;
            this.type = TypeSymbol.Int;
        }

        public ConstantExpression(uint value)
        {
            this.Value = unchecked((int)value);
            this.type = TypeSymbol.UInt;
        }

        public ConstantExpression(long value)
        {
            this.Value64 = value;
            this.type = TypeSymbol.Long;
        }

        public ConstantExpression(ulong value)
        {
            this.Value64 = unchecked((long)value);
            this.type = TypeSymbol.ULong;
        }

        public ConstantExpression(string cstr)
        {
            this.CString = cstr;
            this.type = TypeSymbol.Byte.GetPointerType();
        }

        public ConstantExpression(float value)
        {
            this.FloatValue = value;
            this.type = TypeSymbol.Float;
        }

        public ConstantExpression(double value)
        {
            this.DoubleValue = value;
            this.type = TypeSymbol.Double;
        }

        public override void Visit(CodeVisitor visitor, VisitMode mode)
        {
            visitor.VisitConstantExpression(this, mode);
        }
    }

    public enum BinOp
    {
        Add,
        Sub,
        Mul,
        Div,
        Mod,
        Shl,
        Shr,
        And,
        Or,
        Xor,
        LAnd,
        LOr,
        // Same order as tokens for relational operators
        Equal,
        NEqual,
        Less,
        Greater,
        LEqual,
        GEqual,
        Min,
        Max,
    }

    public class BinaryExpression : Expression
    {
        public BinOp Op { get; private set; }
        public Expression Left { get; set; }
        public Expression Right { get; set; }

        public override TypeSymbol DataType
        {
            get
            {
                if (Op >= BinOp.Equal && Op <= BinOp.GEqual)
                {
                    return TypeSymbol.Bool;
                }
                else
                {
                    if (Left.DataType.IsPointer() /*|| Right.DataType.IsPointer()*/)
                    {
                        if (!Right.DataType.IsPointer())
                            return Left.DataType;
                        else
                            return TypeSymbol.Long;
                    }
                    else
                    {
                        TypeSymbol lt = Left.DataType;
                        TypeSymbol rt = Right.DataType;
                        if (lt.TypeCode > rt.TypeCode)
                        {
                            return lt;
                        }
                        else
                        {
                            return rt;
                        }
                    }
                }
            }
        }

        public BinaryExpression(BinOp op, Expression left, Expression right)
        {
            this.Op = op;
            this.Left = left;
            this.Right = right;
        }

        public override void Visit(CodeVisitor visitor, VisitMode mode)
        {
            visitor.VisitBinaryExpression(this, mode);
        }
    }

    public class TernaryExpression : Expression
    {
        public Expression Condition { get; set; }
        public Expression Positive { get; set; }
        public Expression Negative { get; set; }

        public override TypeSymbol DataType => Positive.DataType;

        public TernaryExpression(Expression condition, Expression positive, Expression negative)
        {
            Condition = condition;
            Positive = positive;
            Negative = negative;
        }

        public override void Visit(CodeVisitor visitor, VisitMode mode)
        {
            visitor.VisitTernaryExpression(this, mode);
        }
    }

    public enum UnaryOp
    {
        Neg,
        Not,
        LNot
    }

    public class UnaryExpression : Expression
    {
        public UnaryOp Op { get; private set; }
        public Expression Expression { get; set; }

        public override TypeSymbol DataType
        {
            get
            {
                if (Op == UnaryOp.LNot)
                    return TypeSymbol.Bool;
                else
                    return Expression.DataType;
            }
        }

        public UnaryExpression(UnaryOp op, Expression expression)
        {
            this.Op = op;
            this.Expression = expression;
        }

        public override void Visit(CodeVisitor visitor, VisitMode mode)
        {
            visitor.VisitUnaryExpression(this, mode);
        }
    }

    public class SymbolExpression : Expression
    {
        public Symbol Symbol { get; private set; }
        public override TypeSymbol DataType => Symbol.DataType;
        public SymbolExpression(Symbol symbol)
        {
            System.Diagnostics.Debug.Assert(symbol != null);
            Symbol = symbol;
        }

        public override void Visit(CodeVisitor visitor, VisitMode mode)
        {
            visitor.VisitSymbolExpression(this, mode);
        }
    }

    public class AssignmentExpression : Expression
    {
        public Expression Left { get; set; }
        public Expression Right { get; set; }

        public LocalVariable decl;
        public bool isInit;

        public override TypeSymbol DataType => Left.DataType;

        public AssignmentExpression(Expression left, Expression right)
        {
            this.Left = left;
            this.Right = right;
        }

        public override void Visit(CodeVisitor visitor, VisitMode mode)
        {
            visitor.VisitAssignmentExpression(this, mode);
        }
    }

    public class CastExpression : Expression
    {
        public Expression Expression { get; set; }
        public TypeSymbol CastType { get; private set; }

        public override TypeSymbol DataType => CastType;

        public CastExpression(Expression expression, TypeSymbol castType)
        {
            this.Expression = expression;
            this.CastType = castType;
        }

        public override void Visit(CodeVisitor visitor, VisitMode mode)
        {
            visitor.VisitCastExpression(this, mode);
        }
    }

    public class IndexExpression : Expression
    {
        public Expression Expression { get; set; }
        public Expression Index { get; set; }

        //public override TypeSymbol DataType => Expression.DataType.ElementType;
        public override TypeSymbol DataType
        {
            get
            {
                if (Expression.DataType.IsPointer() && Expression.DataType.ElementType.IsClass())
                {
                    var cls = Expression.DataType.ElementType;
                    var m = cls.Find("get_item") as MethodSymbol;
                    if (m != null)
                    {
                        var fn = m.DataType as Function;
                        return fn.ReturnType;
                    }
                }
                return Expression.DataType.ElementType;
            }
        }

        public IndexExpression(Expression expression, Expression index)
        {
            this.Expression = expression;
            this.Index = index;
        }

        public override void Visit(CodeVisitor visitor, VisitMode mode)
        {
            visitor.VisitIndexExpression(this, mode);
        }
    }

    public class FieldExpression : Expression
    {
        public Expression Expression { get; set; }
        public Symbol Field { get; private set; }
        public bool Explicit { get; set; }
        public override TypeSymbol DataType => Field.DataType;

        public FieldExpression(Expression expression, Symbol field)
        {
            this.Expression = expression;
            this.Field = field;
        }

        public override void Visit(CodeVisitor visitor, VisitMode mode)
        {
            visitor.VisitFieldExpression(this, mode);
        }
    }

    public class AddressOfExpression : Expression
    {
        public Expression Expression { get; set; }

        public override TypeSymbol DataType => Expression.DataType.GetPointerType();

        public AddressOfExpression(Expression expression)
        {
            this.Expression = expression;
        }

        public override void Visit(CodeVisitor visitor, VisitMode mode)
        {
            visitor.VisitAddressOfExpression(this, mode);
        }
    }

    public class DerefExpression : Expression
    {
        public Expression Expression { get; set; }

        public override TypeSymbol DataType => Expression.DataType.ElementType;

        public DerefExpression(Expression expression)
        {
            this.Expression = expression;
        }

        public override void Visit(CodeVisitor visitor, VisitMode mode)
        {
            visitor.VisitDerefExpression(this, mode);
        }
    }

    public class FunctionCallExpression : Expression
    {
        public Expression Callee { get; set; }
        public List<Expression> Arguments { get; private set; } = new List<Expression>();

        public override TypeSymbol DataType
        {
            get
            {
                if (Callee.DataType.IsPointer() && Callee.DataType.ElementType is Function fn)
                {
                    return fn.ReturnType;
                }
                else
                {
                    return (Callee.DataType as Function).ReturnType;
                }
            }
        }

        public FunctionCallExpression(Expression callee)
        {
            this.Callee = callee;
        }

        public override void Visit(CodeVisitor visitor, VisitMode mode)
        {
            visitor.VisitFunctionCallExpression(this, mode);
        }
    }

    public class InitList : Expression
    {
        private TypeSymbol type;
        public List<Expression> Expressions { get; private set; } = new List<Expression>();

        public InitList(TypeSymbol type)
        {
            this.type = type;
        }

        public override TypeSymbol DataType => type;

        public override void Visit(CodeVisitor visitor, VisitMode mode)
        {
        }
    }

    public class SizeOfExpression : Expression
    {
        public TypeSymbol SizeType { get; private set; }

        public SizeOfExpression(TypeSymbol type)
        {
            this.SizeType = type;
        }

        public override TypeSymbol DataType => TypeSymbol.Int;

        public override void Visit(CodeVisitor visitor, VisitMode mode)
        {
            visitor.VisitSizeOfExpression(this, mode);
        }
    }

    public class PostFixExpression : Expression
    {
        public Expression Operand { get; set; }
        public Expression Operation { get; set; }

        public PostFixExpression(Expression operand, Expression operation)
        {
            this.Operand = operand;
            this.Operation = operation;
        }

        public override TypeSymbol DataType => Operand.DataType;

        public override void Visit(CodeVisitor visitor, VisitMode mode)
        {
            visitor.VisitPostFixExpression(this, mode);
        }
    }
}
