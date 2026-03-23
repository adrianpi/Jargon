using System.Collections.Generic;

namespace Jargon
{
    public abstract class Statement
    {
        public string file;
        public int line;
        public abstract void Visit(CodeVisitor visitor);
    }

    public abstract class Label
    {
    }

    public class ExpressionStatement : Statement
    {
        public Expression Expression { get; set; }
        public ExpressionStatement(Expression expression)
        {
            this.Expression = expression;
        }

        public override void Visit(CodeVisitor visitor)
        {
            visitor.VisitExpressionStatement(this);
        }
    }

    public class ReturnStatement : Statement
    {
        public Expression Expression { get; set; }
        public ReturnStatement(Expression expression)
        {
            this.Expression = expression;
        }

        public override void Visit(CodeVisitor visitor)
        {
            visitor.VisitReturnStatement(this);
        }
    }

    public class DebugStatement : Statement
    {
        public override void Visit(CodeVisitor visitor)
        {
            visitor.VisitDebugStatement(this);
        }
    }

    public class StatementBlock : Statement
    {
        public BlockScope Scope { get; private set; } = new BlockScope();
        public List<Statement> Statements { get; private set; } = new List<Statement>();
        public int endLine;
        //public bool cleared = false;

        public override void Visit(CodeVisitor visitor)
        {
            visitor.VisitStatementBlock(this);
        }
    }

    public class IfStatement : Statement
    {
        public Expression Condition { get; set; }
        public Statement Then { get; private set; }
        public Statement Else { get; private set; }

        public IfStatement(Expression condition, Statement Then, Statement Else)
        {
            this.Condition = condition;
            this.Then = Then;
            this.Else = Else;
        }

        public override void Visit(CodeVisitor visitor)
        {
            visitor.VisitIfStatement(this);
        }
    }

    public abstract class LoopStatement : Statement
    {
        public Label ContinueLabel { get; set; }
        public Label BreakLabel { get; set; }
        public Statement Body { get; protected set; }
    }

    public class WhileStatement : LoopStatement
    {
        public Expression Condition { get; set; }

        public WhileStatement(Expression condition, Statement body)
        {
            this.Condition = condition;
            Body = body;
        }

        public override void Visit(CodeVisitor visitor)
        {
            visitor.VisitWhileStatement(this);
        }
    }

    public class DoStatement : LoopStatement
    {
        //public Statement Body { get; private set; }
        public Expression Condition { get; set; }

        public DoStatement(Statement body, Expression condition)
        {
            Body = body;
            Condition = condition;
        }

        public override void Visit(CodeVisitor visitor)
        {
            visitor.VisitDoStatement(this);
        }
    }

    public class ForStatement : LoopStatement
    {
        public Expression Init { get; set; }
        public Expression Condition { get; set; }
        public Expression Iter { get; set; }
        //public Statement Body { get; private set; }
        public BlockScope Scope { get; set; }

        public ForStatement(Expression init, Expression condition, Expression iter, Statement body)
        {
            this.Init = init;
            this.Condition = condition;
            this.Iter = iter;
            Body = body;
        }

        public override void Visit(CodeVisitor visitor)
        {
            visitor.VisitForStatement(this);
        }
    }

    public class BreakStatement : Statement
    {
        public override void Visit(CodeVisitor visitor)
        {
            visitor.VisitBreakStatement(this);
        }
    }

    public class ContinueStatement : Statement
    {
        public override void Visit(CodeVisitor visitor)
        {
            visitor.VisitContinueStatement(this);
        }
    }

    public class EmptyStatement : Statement
    {
        public override void Visit(CodeVisitor visitor)
        {

        }
    }
}
