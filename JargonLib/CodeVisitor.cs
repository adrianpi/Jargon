namespace Jargon
{
    public enum VisitMode
    {
        Load,
        Address,
    }

    public abstract class CodeVisitor
    {
        public abstract void VisitConstantExpression(ConstantExpression e, VisitMode mode);
        public abstract void VisitBinaryExpression(BinaryExpression e, VisitMode mode);
        public abstract void VisitUnaryExpression(UnaryExpression e, VisitMode mode);
        public abstract void VisitSymbolExpression(SymbolExpression e, VisitMode mode);
        public abstract void VisitAssignmentExpression(AssignmentExpression e, VisitMode mode);
        public abstract void VisitCastExpression(CastExpression e, VisitMode mode);
        public abstract void VisitIndexExpression(IndexExpression e, VisitMode mode);
        public abstract void VisitFieldExpression(FieldExpression e, VisitMode mode);
        public abstract void VisitAddressOfExpression(AddressOfExpression e, VisitMode mode);
        public abstract void VisitDerefExpression(DerefExpression e, VisitMode mode);
        public abstract void VisitFunctionCallExpression(FunctionCallExpression e, VisitMode mode);
        public abstract void VisitTernaryExpression(TernaryExpression e, VisitMode mode);
        public abstract void VisitSizeOfExpression(SizeOfExpression e, VisitMode mode);
        public abstract void VisitPostFixExpression(PostFixExpression e, VisitMode mode);

        public abstract void VisitExpressionStatement(ExpressionStatement s);
        public abstract void VisitReturnStatement(ReturnStatement s);
        public abstract void VisitDebugStatement(DebugStatement s);
        public abstract void VisitStatementBlock(StatementBlock s);
        public abstract void VisitIfStatement(IfStatement s);
        public abstract void VisitWhileStatement(WhileStatement s);
        public abstract void VisitForStatement(ForStatement s);
        public abstract void VisitDoStatement(DoStatement s);
        public abstract void VisitBreakStatement(BreakStatement s);
        public abstract void VisitContinueStatement(ContinueStatement s);

        public abstract void VisitGlobalVariable(GlobalVariable s);
        public abstract void VisitStruct(StructType s);
        public abstract void VisitFunction(Function f);
        public abstract void VisitModule(Module m);
        public abstract void VisitClass(ClassType c);
    }
}
