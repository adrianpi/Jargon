using System;
using System.Collections.Generic;

namespace Jargon
{
    public enum Token
    {
        EOF = 0,
        Error,
        Keyword,
        Bool,
        Byte,
        Int,
        UInt,
        Long,
        ULong,
        String,
        Float,
        Double,
        Ident,
        LPar,
        RPar,
        Comma,
        Period,
        Colon,
        Semicolon,
        Ellipsis,
        Question,
        LBracket,
        RBracket,
        LBrace,
        RBrace,
        Add,
        Sub,
        Mul,
        Div,
        Mod,
        And,
        Or,
        Xor,
        Shl,
        Shr,
        LAnd,
        LOr,
        Assign,
        AssignAdd,
        AssignSub,
        AssignMul,
        AssignDiv,
        AssignMod,
        AssignAnd,
        AssignOr,
        AssignXor,
        AssignShl,
        AssignShr,
        AssignLAnd,
        AssignLOr,
        Not,
        LNot,
        Equal,
        NEqual,
        Less,
        Greater,
        LEqual,
        GEqual,
        Inc,
        Dec,
        Min,
        Max,
        DoubleArrow,
        Reference,
    }

    public class Scanner
    {
        private string fileName;
        private string text;
        private int pos;
        private int column;
        private int line;
        private char c;
        private Token token;
        private string tokenString;
        private float tokenFloat;
        private double tokenDouble;
        private int tokenInt;
        private long tokenLong;
        public ICompilerErrorListener errorListener;

        public Token GetToken() => token;
        public bool GetTokenBool() => tokenInt != 0;
        public int GetTokenInt() => tokenInt;
        public long GetTokenLong() => tokenLong;
        public string GetTokenString() => tokenString;
        public float GetTokenFloat() => tokenFloat;
        public double GetTokenDouble() => tokenDouble;
        public string GetFileName() => fileName;
        public int GetLine() => line;
        public int GetColumn() => column;

        public struct State
        {
            public string text;
            public string fileName;
            public int pos;
            public int column;
            public int line;
            public char c;
            public Token token;
            public string tokenString;
            public float tokenFloat;
            public double tokenDouble;
            public int tokenInt;
        }

        public State GetState()
        {
            State s = new State();
            s.text = text;
            s.fileName = fileName;
            s.pos = pos;
            s.column = column;
            s.line = line;
            s.c = c;
            s.token = token;
            s.tokenString = tokenString;
            s.tokenFloat = tokenFloat;
            s.tokenDouble = tokenDouble;
            s.tokenInt = tokenInt;
            return s;
        }

        public void Restore(State state)
        {
            text = state.text;
            fileName = state.fileName;
            pos = state.pos;
            column = state.column;
            line = state.line;
            c = state.c;
            token = state.token;
            tokenString = state.tokenString;
            tokenFloat = state.tokenFloat;
            tokenDouble = state.tokenDouble;
            tokenInt = state.tokenInt;
        }

        private List<string> keywords = new List<string>()
        {
            "as",
            "base",
            "break",
            "class",
            "const",
            "continue",
            "debug",
            "delete",
            "do",
            "else",
            "enum",
            "external",
            "for",
            "get",
            "if",
            "is",
            "new",
            "null",
            "operator",
            "return",
            "set",
            "sizeof",
            "static",
            "struct",
            "template",
            "typeof",
            "union",
            "using",
            "verbatim",
            "virtual",
            "weak",
            "while",
        };

        public Scanner(string text, string fileName)
        {
            this.text = text;
            this.fileName = fileName;
            pos = -1;
            column = 0;
            line = 1;
            NextChar();
            NextToken();
        }

        private void NextChar()
        {
            if (pos < text.Length - 1)
            {
                pos++;
                c = text[pos];
                if (c == '\n')
                {
                    column = 0;
                    line++;
                }
                else
                {
                    column++;
                }
            }
            else
            {
                c = '\0';
            }
        }

        private char Unescape(char cc)
        {
            try
            {
                if (cc == '\\')
                {
                    NextChar();
                    switch (c)
                    {
                        case '0': return '\0';
                        case 'a': return '\a';
                        case 'b': return '\b';
                        case 'f': return '\f';
                        case 'n': return '\n';
                        case 'r': return '\r';
                        case 't': return '\t';
                        case 'v': return '\v';
                        case '\"': return '\"';
                        case '\'': return '\'';
                        case '\\': return '\\';
                    }
                    if (char.IsDigit(c))
                    {
                        string octal = "";
                        octal += c;
                        NextChar();
                        while (char.IsDigit(c))
                        {
                            octal += c;
                            NextChar();
                        }
                        pos--;
                        return (char)Convert.ToInt32(octal, 8);
                    }
                    else if (c == 'x' || c == 'X')
                    {
                        string hex = "";
                        NextChar();
                        while (char.IsDigit(c) || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))
                        {
                            hex += c;
                            NextChar();
                        }
                        pos--;
                        return (char)Convert.ToInt32(hex, 16);
                    }
                }
                return cc;
            }
            catch (Exception)
            {
                Error("Invalid escape sequence");
                return '\0';
            }
        }

        public void NextToken()
        {
            again:
            while (char.IsWhiteSpace(c))
            {
                NextChar();
            }

            if (c == '\0')
            {
                token = Token.EOF;
            }
            else if (c == ',')
            {
                NextChar();
                token = Token.Comma;
            }
            else if (c == '.')
            {
                NextChar();
                if (c == '.')
                {
                    NextChar();
                    if (c == '.')
                    {
                        NextChar();
                        token = Token.Ellipsis;
                    }
                    else
                    {
                        token = Token.Error;
                    }
                }
                else
                {
                    token = Token.Period;
                }
            }
            else if (c == ':')
            {
                NextChar();
                token = Token.Colon;
            }
            else if (c == ';')
            {
                NextChar();
                token = Token.Semicolon;
            }
            else if (c == '[')
            {
                NextChar();
                token = Token.LBracket;
            }
            else if (c == ']')
            {
                NextChar();
                token = Token.RBracket;
            }
            else if (c == '{')
            {
                NextChar();
                token = Token.LBrace;
            }
            else if (c == '}')
            {
                NextChar();
                token = Token.RBrace;
            }
            else if (c == '(')
            {
                NextChar();
                token = Token.LPar;
            }
            else if (c == ')')
            {
                NextChar();
                token = Token.RPar;
            }
            else if (c == '=')
            {
                NextChar();
                if (c == '=')
                {
                    NextChar();
                    token = Token.Equal;
                }
                else if (c == '>')
                {
                    NextChar();
                    token = Token.DoubleArrow;
                }
                else
                {
                    token = Token.Assign;
                }
            }
            else if (c == '+')
            {
                NextChar();
                if (c == '=')
                {
                    NextChar();
                    token = Token.AssignAdd;
                }
                else if (c == '+')
                {
                    NextChar();
                    token = Token.Inc;
                }
                else
                {
                    token = Token.Add;
                }
            }
            else if (c == '-')
            {
                NextChar();
                if (c == '=')
                {
                    NextChar();
                    token = Token.AssignSub;
                }
                else if (c == '-')
                {
                    NextChar();
                    token = Token.Dec;
                }
                else
                {
                    token = Token.Sub;
                }
            }
            else if (c == '*')
            {
                NextChar();
                if (c == '=')
                {
                    NextChar();
                    token = Token.AssignMul;
                }
                else
                {
                    token = Token.Mul;
                }
            }
            else if (c == '/')
            {
                NextChar();
                if (c == '=')
                {
                    NextChar();
                    token = Token.AssignDiv;
                }
                else if (c == '/')
                {
                    NextChar();
                    //tokenString = "";
                    while (c != '\n' && c != '\0')
                    {
                        //tokenString += c;
                        NextChar();
                    }
                    goto again;
                }
                else if (c == '*')
                {
                    NextChar();
                    while (true)
                    {
                        if (c == '\0')
                        {
                            token = Token.EOF;
                            return;
                        }
                        else if (c == '*' && pos + 1 < text.Length && text[pos + 1] == '/')
                        {
                            NextChar();
                            NextChar();
                            goto again;
                        }
                        NextChar();
                    }
                }
                else
                {
                    token = Token.Div;
                }
            }
            else if (c == '%')
            {
                NextChar();
                if (c == '=')
                {
                    NextChar();
                    token = Token.AssignMod;
                }
                else
                {
                    token = Token.Mod;
                }
            }
            else if (c == '&')
            {
                NextChar();
                if (c == '&')
                {
                    NextChar();
                    if (c == '=')
                    {
                        NextChar();
                        token = Token.AssignLAnd;
                    }
                    else
                    {
                        token = Token.LAnd;
                    }
                }
                else if (c == '=')
                {
                    NextChar();
                    token = Token.AssignAnd;
                }
                else
                {
                    token = Token.And;
                }
            }
            else if (c == '|')
            {
                NextChar();
                if (c == '|')
                {
                    NextChar();
                    if (c == '=')
                    {
                        NextChar();
                        token = Token.AssignLOr;
                    }
                    else
                    {
                        token = Token.LOr;
                    }
                }
                else if (c == '=')
                {
                    NextChar();
                    token = Token.AssignOr;
                }
                else
                {
                    token = Token.Or;
                }
            }
            else if (c == '^')
            {
                NextChar();
                if (c == '=')
                {
                    NextChar();
                    token = Token.AssignXor;
                }
                else
                {
                    token = Token.Xor;
                }
            }
            else if (c == '~')
            {
                NextChar();
                token = Token.Not;
            }
            else if (c == '!')
            {
                NextChar();
                if (c == '=')
                {
                    NextChar();
                    token = Token.NEqual;
                }
                else
                {
                    token = Token.LNot;
                }
            }
            else if (c == '<')
            {
                NextChar();
                if (c == '=')
                {
                    NextChar();
                    token = Token.LEqual;
                }
                else if (c == '<')
                {
                    NextChar();
                    if (c == '=')
                    {
                        NextChar();
                        token = Token.AssignShl;
                    }
                    else
                    {
                        token = Token.Shl;
                    }
                }
                else if (c == '?')
                {
                    NextChar();
                    token = Token.Min;
                }
                else
                {
                    token = Token.Less;
                }
            }
            else if (c == '>')
            {
                NextChar();
                if (c == '=')
                {
                    NextChar();
                    token = Token.GEqual;
                }
                else if (c == '>')
                {
                    NextChar();
                    if (c == '=')
                    {
                        NextChar();
                        token = Token.AssignShr;
                    }
                    else
                    {
                        token = Token.Shr;
                    }
                }
                else if (c == '?')
                {
                    NextChar();
                    token = Token.Max;
                }
                else
                {
                    token = Token.Greater;
                }
            }
            else if (c == '?')
            {
                NextChar();
                token = Token.Question;
            }
            else if (c == '@')
            {
                NextChar();
                token = Token.Reference;
            }
            else if (char.IsDigit(c))
            {
                tokenString = "" + c;
                NextChar();

                bool isHex = false;
                bool isBinary = false;
                bool isUnsigned = false;
                bool isReal = false;
                bool isFloat = false;
                bool isLong = false;

                if (tokenString == "0" && (c == 'x' || c == 'X' || c == 'b' || c == 'B'))
                {
                    if (c == 'x' || c == 'X')
                        isHex = true;
                    else
                        isBinary = true;
                    NextChar();
                }

                while ((isHex && isxdigit(c)) || (isBinary && (c == '0' || c == '1' || c == '_')) || (!isHex && !isBinary && char.IsDigit(c)) || (!isReal && !isHex && !isBinary && c == '.'))
                {
                    if (isBinary && c == '_')
                    {
                        NextChar();
                        continue;
                    }
                    else if (!isHex && !isBinary && !isReal && c == '.')
                    {
                        isReal = true;
                    }
                    tokenString += c;
                    NextChar();
                }

                if(isReal && (c == 'e' || c == 'E'))
                {
                    isReal = true;
                    tokenString += c;
                    NextChar();
                    if (c == '+' || c == '-')
                    {
                        tokenString += c;
                        NextChar();
                    }
                    while (char.IsDigit(c) || c == '_')
                    {
                        if (c == '_')
                        {
                            NextChar();
                            continue;
                        }
                        tokenString += c;
                        NextChar();
                    }
                }

                if (isReal && (c == 'f' || c == 'F'))
                {
                    isFloat = true;
                    NextChar();
                }
                else if (!isReal)
                {
                    if(c == 'u' || c == 'U')
                    {
                        NextChar();
                        isUnsigned = true;
                    }
                    if (c == 'l' || c == 'L')
                    {
                        NextChar();
                        isLong = true;
                    }
                }

                try
                {

                    if (isHex)
                    {
                        if (isLong)
                        {
                            tokenLong = Convert.ToInt64(tokenString, 16);
                            token = isUnsigned ? Token.ULong : Token.Long;
                        }
                        else
                        {
                            tokenInt = Convert.ToInt32(tokenString, 16);
                            token = isUnsigned ? Token.UInt : Token.Int;
                        }
                    }
                    else if (isBinary)
                    {
                        if (isLong)
                        {
                            tokenLong = Convert.ToInt64(tokenString, 2);
                            token = isUnsigned ? Token.ULong : Token.Long;
                        }
                        else
                        {
                            tokenInt = Convert.ToInt32(tokenString, 2);
                            token = isUnsigned ? Token.UInt : Token.Int;
                        }
                    }
                    else if (isFloat)
                    {
                        tokenFloat = Convert.ToSingle(tokenString);
                        token = Token.Float;
                    }
                    else if (isReal)
                    {
                        tokenDouble = Convert.ToDouble(tokenString);
                        token = Token.Double;
                    }
                    else
                    {
                        if(isLong)
                        {
                            if (isUnsigned)
                            {
                                tokenLong = unchecked((long)Convert.ToUInt64(tokenString));
                                token = Token.ULong;
                            }
                            else
                            {
                                tokenLong = Convert.ToInt64(tokenString);
                                token = Token.Long;
                            }
                        }
                        else
                        {
                            if (isUnsigned)
                            {
                                tokenInt = unchecked((int)Convert.ToUInt32(tokenString));
                                token = Token.UInt;
                            }
                            else
                            {
                                tokenInt = Convert.ToInt32(tokenString);
                                token = Token.Int;
                            }
                        }                        
                    }
                }
                catch (Exception)
                {
                    token = Token.Error;
                    Error("Invalid number format");
                }
            }
            else if (c == '\'')
            {
                tokenString = "";
                NextChar();
                while (c != '\'' && c != '\0')
                {
                    tokenString += Unescape(c);
                    NextChar();
                }
                token = Token.Byte;
                if (c == '\0')
                {
                    Error("Unterminated character constant");
                    token = Token.Error;
                }
                NextChar();
                if (tokenString.Length != 1)
                    tokenInt = 0;
                else
                    tokenInt = (int)tokenString[0];
            }
            else if (c == '"')
            {
                tokenString = "";
                NextChar();
                while (c != '"' && c != '\0')
                {
                    tokenString += Unescape(c);
                    NextChar();
                }
                token = Token.String;
                if (c == '\0')
                {
                    Error("Unterminated string constant");
                    token = Token.Error;
                }
                NextChar();
            }
            else if (c == '_' || char.IsLetter(c))
            {
                tokenString = "" + c;
                NextChar();
                while (c == '_' || char.IsLetterOrDigit(c))
                {
                    tokenString += c;
                    NextChar();
                }

                if (tokenString == "true")
                {
                    token = Token.Bool;
                    tokenInt = 1;
                }
                else if (tokenString == "false")
                {
                    token = Token.Bool;
                    tokenInt = 0;
                }
                else if (keywords.Contains(tokenString))
                {
                    token = Token.Keyword;
                }
                else
                {
                    token = Token.Ident;
                }
            }
            else
            {
                token = Token.Error;
                Error("Invalid char '" + c + "'");
                NextChar();
            }
        }

        bool isxdigit(char c)
        {
            return char.IsDigit(c) || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        }

        void Error(string message)
        {
            errorListener.OnError(CompilerError.Error(message, fileName, line, column));
        }
    }
}
