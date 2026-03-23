namespace Jargon
{
    public enum ErrorSeverity
    {
        Info,
        Warning,
        Error,
    }

    public class CompilerError
    {
        public ErrorSeverity Severity { get; internal set; }
        public string FileName { get; internal set; }
        public int Line { get; internal set; }
        public int Column { get; internal set; }
        public string Message { get; internal set; }

        public static CompilerError Info(string message)
        {
            CompilerError error = new CompilerError();
            error.Severity = ErrorSeverity.Info;
            error.Message = message;
            return error;
        }

        public static CompilerError Warning(string message, string fileName, int line, int column)
        {
            CompilerError error = new CompilerError();
            error.Severity = ErrorSeverity.Warning;
            error.Message = message;
            error.FileName = fileName;
            error.Line = line;
            error.Column = column;
            return error;
        }

        public static CompilerError Error(string message, string fileName, int line, int column)
        {
            CompilerError error = new CompilerError();
            error.Severity = ErrorSeverity.Error;
            error.Message = message;
            error.FileName = fileName;
            error.Line = line;
            error.Column = column;
            return error;
        }

        public override string ToString()
        {
            string output = "";
            if (FileName != null)
                output += FileName;
            if (Line != 0)
                output += "(" + Line + "," + Column + "): ";
            if (Severity != ErrorSeverity.Info)
                output += Severity.ToString() + ": ";
            output += Message;
            return output;
        }
    }

    public interface ICompilerErrorListener
    {
        void OnError(CompilerError error);
    }
}
