using System;

namespace Jargon
{
    /*
                        +-----------+-----------+-----------+-----------+-----------+-----------+-----------+
                        |Usings		|Enums		|Structs	|Globals	|Functions	|Classes	|Templates  |
                        +-----------+-----------+-----------+-----------+-----------+-----------+-----------+
    First Pass			|Full		|Full		|Empty Decl	|Skip		|Skip		|Empty Decl |Full       |
                        +-----------+-----------+-----------+-----------+-----------+-----------+-----------+
    Second Pass			|Skip		|Skip		|Full		|Skip		|Proto Decl	|Field+Proto|Skip       |
                        +-----------+-----------+-----------+-----------+-----------+-----------+-----------+
    Third Pass			|Skip		|Skip		|Skip		|Definition	|Skip		|Skip       |Skip       |
                        +-----------+-----------+-----------+-----------+-----------+-----------+-----------+
    Fourth Pass			|Skip		|Skip		|Skip		|DeInit	    |Full		|Full       |Skip       |
                        +-----------+-----------+-----------+-----------+-----------+-----------+-----------+
    */

    public abstract class StageParser : ExpressionParser
    {
        protected StageParser(Module module, ICompilerErrorListener errorListener)
            : base(errorListener)
        {
            this.module = module;
        }

        protected abstract bool ParseUsing();
        protected abstract bool ParseEnum();
        protected abstract bool ParseStructure(bool isUnion);
        protected abstract bool ParseClass();
        protected abstract bool ParseGlobalOrFunction();
        protected abstract bool ParseTemplate();

        public bool ParseUnit(CompileUnit unit, CompileUnit parentUnit = null)
        {
            //Console.WriteLine($"ParseUnit({unit.FileName})");
            StartParse(unit);

            fileName = unit.Path;

            if (parentUnit != null)
            {
                bool found = false;
                foreach (var c in parentUnit.Children)
                {
                    if (c.Path == unit.Path)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                    parentUnit.Children.Add(unit);
                this.unit = parentUnit;
            }

            while (scanner.GetToken() != Token.EOF)
            {
                if (IsKeyword("using"))
                {
                    if (!ParseUsing())
                        return false;
                }
                else if (IsKeyword("enum"))
                {
                    if (!ParseEnum())
                        return false;
                }
                else if (IsKeyword("struct"))
                {
                    if (!ParseStructure(false))
                        return false;
                }
                else if (IsKeyword("union"))
                {
                    if (!ParseStructure(true))
                        return false;
                }
                else if (IsKeyword("class"))
                {
                    if (!ParseClass())
                        return false;
                }
                else if (IsKeyword("template"))
                {
                    if (!ParseTemplate())
                        return false;
                }
                else if (!ParseGlobalOrFunction())
                {
                    return false;
                }
            }

            EndParse();

            return true;
        }

        protected Expression ParseInitList(TypeSymbol type)
        {
            scanner.NextToken();
            var list = new InitList(type);
            int index = 0;
            while (scanner.GetToken() != Token.RBrace && scanner.GetToken() != Token.EOF)
            {
                Expression e = null;
                if (scanner.GetToken() == Token.LBrace)
                    e = ParseInitList(type.IsArray() ? type.ElementType : (type as StructType).Children[index].DataType);
                else
                    e = ParseExpression();
                if (e == null)
                    return null;
                list.Expressions.Add(e);
                if (scanner.GetToken() == Token.Comma)
                    scanner.NextToken();
                else if (scanner.GetToken() != Token.RBrace)
                {
                    Error("} expected");
                    return null;
                }
                ++index;
            }
            if (!Expect(Token.RBrace))
                return null;

            if (type is StructType st && index < st.Children.Count)
            {
                for (int i = index; i < st.Children.Count; ++i)
                {
                    list.Expressions.Add(DefaultFor(st.Children[i].DataType));
                }
            }
            else if (type is ArrayType at && index < at.ArraySize)
            {
                for (int i = index; i < at.ArraySize; ++i)
                    list.Expressions.Add(DefaultFor(at.ElementType));
            }

            return list;
        }

        InitList StructFiller(StructType type)
        {
            var result = new InitList(type);
            for (int i = 0; i < type.Children.Count; ++i)
            {
                result.Expressions.Add(DefaultFor(type.Children[i].DataType));
            }
            return result;
        }

        Expression DefaultFor(TypeSymbol type)
        {
            if (type == TypeSymbol.Bool)
            {
                return new ConstantExpression(false);
            }
            else if (type == TypeSymbol.Byte || type == TypeSymbol.UByte
                || type == TypeSymbol.Short || type == TypeSymbol.UShort
                || type == TypeSymbol.Int || type == TypeSymbol.UInt
                || type == TypeSymbol.Long || type == TypeSymbol.ULong)
            {
                return new ConstantExpression(0);
            }
            else if (type == TypeSymbol.Float)
            {
                return new ConstantExpression(0.0f);
            }
            else if (type == TypeSymbol.Double)
            {
                return new ConstantExpression(0.0);
            }
            else if (type.IsPointer())
            {
                return new ConstantExpression();
            }
            else if (type.IsStruct())
            {
                return StructFiller(type as StructType);
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        protected bool StaticAssign(StatementBlock sb, Expression left, Expression right, int line, LocalVariable decl)
        {
            if (left.DataType.IsArray() && right is InitList list)
            {
                for (int i = 0; i < list.Expressions.Count; ++i)
                {
                    if (!StaticAssign(sb, new IndexExpression(left, new ConstantExpression(i)), list.Expressions[i], line, i == 0 ? decl : null))
                        return false;
                }
                return true;
            }
            else if (left.DataType.IsStruct() && right is ConstantExpression ce)
            {
                if (right.DataType != TypeSymbol.Int || ce.Value != 0)
                {
                    Error("Invalid Assignment");
                    return false;
                }
                var ae = new AssignmentExpression(left, right);
                ae.line = line;
                ae.decl = decl;
                sb.Statements.Add(new ExpressionStatement(ae));
                return true;
            }
            else if (left.DataType.IsStruct() && right is InitList list2)
            {
                for (int i = 0; i < list2.Expressions.Count; ++i)
                    if (!StaticAssign(sb, new FieldExpression(left, left.DataType.Children[i] as FieldSymbol), list2.Expressions[i], line, i == 0 ? decl : null))
                        return false;
                return true;
            }
            else
            {
                ForceCast(ref right, left.DataType);

                var ae = new AssignmentExpression(left, right);
                ae.line = line;
                ae.decl = decl;
                ae.isInit = true;
                sb.Statements.Add(new ExpressionStatement(ae));
                return true;
            }
        }

        protected void SkipBraces()
        {
            int lvl = 0;
            while (scanner.GetToken() != Token.EOF)
            {
                if (scanner.GetToken() == Token.LBrace)
                    lvl++;
                else if (scanner.GetToken() == Token.RBrace)
                {
                    lvl--;
                    if (lvl == 0)
                        break;
                }
                scanner.NextToken();
            }
        }
    }
}

