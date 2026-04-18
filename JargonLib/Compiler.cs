using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Jargon
{
    public class CompilerOptions
    {
        public string OutputFileName { get; set; }
        public string OutputDirectory { get; set; } = ".";
        public string AdditionalFlags { get; set; } = "";
        public bool DebugInfo { get; set; } = true;
        public bool KeepIntermediateFiles { get; set; } = false;
        public string OptimizationLevel { get; set; } = "Og";

        public List<string> AdditionalLibraries { get; private set; } = new List<string>()
        {
            "ucrt.lib", "vcruntime.lib", "msvcrt.lib", "legacy_stdio_definitions.lib"
        };
        public List<string> LibraryDirectories { get; private set; } = new List<string>()
        {
            "$JARGON_LIB",
        };
    }

    public class CompilerOutput
    {
        public Module Module { get; internal set; }
        public List<CompilerError> Errors { get; private set; } = new List<CompilerError>();
        public int ErrorCount { get; internal set; }
        public string OutputFile { get; internal set; }
    }

    public class Compiler : ICompilerErrorListener
    {
        private List<CompileUnit> units = new List<CompileUnit>();
        private CompilerOptions options;
        private CompilerOutput output;

        public static string ComputeMD5(string filePath)
        {
            var md5 = MD5.Create();
            using (var stream = File.OpenRead(filePath))
            {
                byte[] hash = md5.ComputeHash(stream);
                // Convert to lowercase hex string (matches LLVM debug info style)
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        private void CreateDebugInfo(CompileUnit cu, ref DICompileUnit unit)
        {
            DINode.Reset();

            var diFiles = new Dictionary<string, DIFile>();

            DIFile file = new DIFile();
            file.filename = cu.FileName;
            file.directory = Path.GetDirectoryName(cu.Path).Replace("\\", "\\\\");
            file.checksumkind = DIChecksumKind.CSK_MD5;
            file.checksum = ComputeMD5(cu.Path);
            diFiles[cu.Path] = file;

            foreach (var ch in cu.Children)
            {
                DIFile file2 = new DIFile();
                file2.filename = ch.FileName;
                file2.directory = Path.GetDirectoryName(ch.Path).Replace("\\", "\\\\");
                file2.checksumkind = DIChecksumKind.CSK_MD5;
                file2.checksum = ComputeMD5(ch.Path);
                diFiles[ch.Path] = file2;
            }

            unit = new DICompileUnit();
            unit.file = file;
            unit.globals = new DINodeList<DIGlobalVariableExpression>();

            foreach (var c in cu.Symbols)
            {
                if (c is Function fn)
                {
                    if (fn.Flags.HasFlag(SymbolFlags.External))
                        continue;

                    var st = new DISubroutineType();
                    if (fn.ReturnType == TypeSymbol.Void)
                        st.types.Add(null);
                    else
                        st.types.Add(DINode.GetTypeInfo(fn.ReturnType));

                    foreach (var p in fn.Parameters)
                    {
                        st.types.Add(DINode.GetTypeInfo(p.DataType));
                    }

                    var sp = new DISubprogram();
                    sp.name = fn.Name;
                    if (fn.declaringClass != null)
                        sp.name = sp.name.Replace(fn.declaringClass.Name + "__", "");
                    sp.linkageName = fn.Name;
                    if (fn.declaringClass != null)
                        sp.scope = DINode.GetTypeInfo(fn.declaringClass);
                    else
                        sp.scope = (fn.fileName == null ? file : diFiles[fn.fileName]);
                    sp.file = fn.fileName == null ? file : diFiles[fn.fileName];
                    sp.line = fn.Line;
                    sp.scopeLine = fn.Line + 1;
                    sp.unit = unit;
                    sp.type = st;
                    DINode.FunctionMap[fn] = sp;

                    foreach (var lv in fn.Parameters)
                    {
                        var dlv = new DILocalVariable();
                        dlv.name = lv.Name;
                        dlv.scope = sp;
                        dlv.file = file;
                        dlv.line = lv.Line;
                        dlv.type = DINode.GetTypeInfo(lv.DataType);
                        dlv.arg = lv.Offset;
                        DINode.LocalMap[lv] = dlv;
                    }

                    foreach (var lv in fn.Locals)
                    {
                        var dlv = new DILocalVariable();
                        dlv.name = lv.Name;
                        dlv.scope = sp;
                        dlv.file = file;
                        dlv.line = lv.Line;
                        dlv.type = DINode.GetTypeInfo(lv.DataType);
                        DINode.LocalMap[lv] = dlv;
                    }
                }
            }
        }

        public CompilerOutput Compile(string[] files, CompilerOptions compilerOptions)
        {
            options = compilerOptions;
            output = new CompilerOutput();

            foreach (var file in files)
            {
                if (!File.Exists(file))
                {
                    OnError(CompilerError.Error($"Could not open file {file}", null, 0, 0));
                    return output;
                }
                CompileUnit unit = new CompileUnit(file);
                units.Add(unit);
            }

            Module module = new Module(Path.GetFileNameWithoutExtension(options.OutputFileName));
            output.Module = module;

            HashSet<string> usingLibs = new HashSet<string>();
            bool isDLL = Path.GetExtension(compilerOptions.OutputFileName).ToLower() == ".dll";

            Parser1 parser1 = new Parser1(module, this);
            parser1.CompilerOptions = compilerOptions;
            foreach (var cu in units)
            {
                if (!parser1.ParseUnit(cu))
                {
                    OnError(CompilerError.Info("Compilation failed on stage 1"));
                    return output;
                }
            }

            Parser2 parser2 = new Parser2(module, this);
            parser2.CompilerOptions = compilerOptions;
            foreach (var cu in units)
            {
                if (!parser2.ParseUnit(cu))
                {
                    OnError(CompilerError.Info("Compilation failed on stage 2"));
                    return output;
                }
            }

            Parser3 parser3 = new Parser3(module, this);
            parser3.CompilerOptions = compilerOptions;
            foreach (var cu in units)
            {
                if (!parser3.ParseUnit(cu))
                {
                    OnError(CompilerError.Info("Compilation failed on stage 3"));
                    return output;
                }
            }

            Parser4 parser4 = new Parser4(module, this);
            parser4.CompilerOptions = compilerOptions;
            foreach (var cu in units)
            {
                if (!parser4.ParseUnit(cu))
                {
                    OnError(CompilerError.Info("Compilation failed on stage 4"));
                    return output;
                }
            }

            List<string> outFiles = new List<string>();

            foreach (var cu in units)
            {
                foreach (var u in cu.Usings)
                    usingLibs.Add(u.Name + ".lib");

                DICompileUnit unit = null;
                if (compilerOptions.DebugInfo)
                {
                    CreateDebugInfo(cu, ref unit);
                }

                Transformer trans = new Transformer();
                trans.CompilerErrorListener = this;
                trans.Parser = parser4;

                var symbols = cu.Symbols.ToList();
                foreach (var s in symbols)
                    s.Visit(trans);

                string outFile = Path.GetFileNameWithoutExtension(cu.FileName) + ".ll";

                if (File.Exists(outFile))
                    File.Delete(outFile);

                if (output.ErrorCount > 0)
                {
                    OnError(CompilerError.Info("Compilation failed on AST transform"));
                    return output;
                }

                LLVMCodeGen cg = new LLVMCodeGen();
                cg.Module = module;
                cg.Unit = cu;
                cg.CompileOptions = compilerOptions;
                cg.CompilerErrorListener = this;

                cg.VisitUnit(cu, module, unit, cu == units.Last());

                File.WriteAllText(outFile, cg.sb.ToString());
                outFiles.Add(outFile);

                if (output.ErrorCount > 0)
                {
                    OnError(CompilerError.Info("Compilation failed during code generation"));
                    return output;
                }
            }

            var inputFiles = string.Join(" ", outFiles.ToArray());
            output.OutputFile = Path.GetFullPath(Path.Combine(options.OutputDirectory, options.OutputFileName));

            var libs = string.Join(" ", options.AdditionalLibraries);
            if (usingLibs.Count > 0)
                libs += " " + string.Join(" ", usingLibs.ToArray());

            foreach (var lib in usingLibs)
                compilerOptions.AdditionalLibraries.Add(lib);

            string libPaths = "";
            foreach (var ld in compilerOptions.LibraryDirectories)
            {
                var ldd = ld;
                if (ld.StartsWith("$"))
                {
                    ldd = Environment.GetEnvironmentVariable(ld.Substring(1));
                    if (ldd == null)
                        continue;
                }
                libPaths += $" /LIBPATH:\"{ldd}\"";
            }
            string flags = options.AdditionalFlags;
            string outputPath = Path.Combine(options.OutputDirectory, options.OutputFileName);

            var startInfo = new ProcessStartInfo
            {
                //FileName = "clang.exe",               // clang must be in PATH or provide full path                
                //Arguments = $"-v -g {inputFiles} -o \"{output.OutputFile}\" {options.AdditionalFlags}",
                FileName = "clang-cl.exe",
                Arguments = $"{(compilerOptions.DebugInfo ? "-g " : "")} /MD {(isDLL ? "/LD " : "")} /{compilerOptions.OptimizationLevel} {flags} {inputFiles} -o \"{outputPath}\" /link {libs}{libPaths}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            OnError(CompilerError.Info(">" + startInfo.FileName + " " + startInfo.Arguments));

            try
            {
                using (var process = Process.Start(startInfo))
                {
                    // Read output and error streams (optional, for diagnostics)
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();

                    var errorLines = (error).Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);

                    /*Regex ErrorLineRegex = new Regex(
                        @"^(?<file>.+?)"              // everything until first :
                        + @":(?<line>\d+)"        // line number
                        + @"(?::(?<column>\d+))?" // optional :column
                        + @":\s*(?<severity>error|warning|note)?" // optional severity
                        + @"\s*:\s*(?<message>[^\r\n]+)", // message until end of line
                        RegexOptions.Compiled | RegexOptions.CultureInvariant
                    );*/
                    string pattern = @"^(?<file>[^(]+)\((?<line>\d+),(?<column>\d+)\):\s*(?<severity>error|warning|note)(?:\s*[A-Z]+[A-Z0-9]*:\s*)?(?<message>.*)$";
                    Regex ErrorLineRegex = new Regex(pattern);

                    foreach (var line in errorLines)
                    {
                        var match = ErrorLineRegex.Match(line);
                        if (match.Success)
                        {
                            string file = match.Groups["file"].Value;
                            int ln = int.Parse(match.Groups["line"].Value);
                            int column = match.Groups["column"].Success
                                                ? int.Parse(match.Groups["column"].Value)
                                                : 1;
                            var severity = match.Groups["severity"].Success
                                                ? match.Groups["severity"].Value
                                                : "error"; // default assumption
                            string message = match.Groups["message"].Value.Trim();

                            if (severity == "error")
                            {
                                OnError(CompilerError.Error(message, file, ln, column));
                            }
                            else if (severity == "warning")
                            {
                                OnError(CompilerError.Warning(message, file, ln, column));
                            }
                            else
                            {
                                OnError(CompilerError.Info(message));
                            }
                        }
                    }

                    process.WaitForExit();

                    if (process.ExitCode == 0)
                    {
                        OnError(CompilerError.Info("\t" + module.Name + " -> " + outputPath));
                    }
                    else
                    {
                        OnError(CompilerError.Info("Compilation error: " + process.ExitCode));
                    }
                }
            }
            catch (Exception ex)
            {
                //txtError.AppendText($"\r\nFailed to start clang: {ex.Message}\r\n");
            }

            if(!options.KeepIntermediateFiles)
            {
                foreach (var f in outFiles)
                {
                    if (File.Exists(f))
                        File.Delete(f);
                }
            }

            return output;
        }

        public void OnError(CompilerError error)
        {
            output.Errors.Add(error);
            if (error.Severity == ErrorSeverity.Error)
                output.ErrorCount++;
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
                else if(c == '\\')
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
    }

    class Transformer : CodeVisitor
    {
        public ICompilerErrorListener CompilerErrorListener;
        public BaseParser Parser;
        private Expression transformed;

        protected void Error(string s, string file, int line)
        {
            CompilerError err = CompilerError.Error(s, file, line, 1);
            CompilerErrorListener?.OnError(err);
        }

        public override void VisitAddressOfExpression(AddressOfExpression e, VisitMode mode)
        {
            e.Expression.Visit(this, mode);
            e.Expression = transformed;
            transformed = e;
        }

        public override void VisitAssignmentExpression(AssignmentExpression e, VisitMode mode)
        {
            if (e.Left is IndexExpression ie && ie.Expression.DataType.IsPointer() && ie.Expression.DataType.ElementType.IsClass())
            {
                var cls = ie.Expression.DataType.ElementType;
                var m = cls.FindChild("set_item") as MethodSymbol;
                if (m != null)
                {
                    var r = e.Right;
                    Parser.ForceCast(ref r, (m.DataType as Function).Parameters[2].DataType);
                    r.Visit(this, VisitMode.Load);
                    e.Right = transformed;

                    ie.Expression.Visit(this, VisitMode.Load);
                    ie.Expression = transformed;
                    var i = ie.Index;
                    Parser.ForceCast(ref i, (m.DataType as Function).Parameters[1].DataType);
                    i.Visit(this, VisitMode.Load);
                    ie.Index = transformed;

                    if (m.Flags.HasFlag(SymbolFlags.Virtual))
                    {
                        var vtf = new FieldExpression(ie.Expression, ie.Expression.DataType.ElementType.FindChild("vtable"));
                        var vie = new IndexExpression(vtf, new ConstantExpression(m.VSlot));
                        var vcs = new CastExpression(vie, m.DataType.GetPointerType());
                        var vdr = new DerefExpression(vcs);
                        var fc = new FunctionCallExpression(vdr);
                        fc.Arguments.Add(ie.Expression);
                        fc.Arguments.Add(ie.Index);
                        fc.Arguments.Add(r);
                        transformed = fc;
                    }
                    else
                    {
                        var fc = new FunctionCallExpression(new SymbolExpression(m));
                        fc.Arguments.Add(ie.Expression);
                        fc.Arguments.Add(ie.Index);
                        fc.Arguments.Add(r);
                        transformed = fc;
                    }
                    transformed.file = e.file;
                    transformed.line = e.line;
                    return;
                }
            }

            if (e.Left is FieldExpression fe && fe.Field is PropertySymbol ps)
            {
                if (ps.Setter != null)
                {
                    fe.Expression.Visit(this, VisitMode.Load);
                    fe.Expression = transformed;
                    var r = e.Right;
                    Parser.ForceCast(ref r, (ps.Setter.DataType as Function).Parameters[1].DataType);
                    r.Visit(this, VisitMode.Load);
                    e.Right = transformed;

                    if (ps.Setter.Flags.HasFlag(SymbolFlags.Virtual))
                    {
                        var vtf = new FieldExpression(fe.Expression, fe.Expression.DataType.ElementType.FindChild("vtable"));
                        var vie = new IndexExpression(vtf, new ConstantExpression(ps.Setter.VSlot));
                        var vcs = new CastExpression(vie, ps.Setter.DataType.GetPointerType());
                        var vdr = new DerefExpression(vcs);
                        var fc = new FunctionCallExpression(vdr);
                        fc.Arguments.Add(fe.Expression);
                        fc.Arguments.Add(e.Right);
                        transformed = fc;
                    }
                    else if (ps.Setter.Flags.HasFlag(SymbolFlags.Static))
                    {
                        var fc = new FunctionCallExpression(new SymbolExpression(ps.Setter.DataType));
                        fc.Arguments.Add(e.Right);
                        transformed = fc;
                    }
                    else
                    {
                        var fc = new FunctionCallExpression(new SymbolExpression(ps.Setter.DataType));
                        fc.Arguments.Add(fe.Expression);
                        fc.Arguments.Add(e.Right);
                        transformed = fc;
                    }
                    transformed.file = e.file;
                    transformed.line = e.line;
                    return;
                }
                else
                {
                    Error($"Property '{ps.Name}' is read-only", e.file, e.line);
                }
            }

            e.Right.Visit(this, VisitMode.Load);
            e.Right = transformed;
            e.Left.Visit(this, VisitMode.Address);
            e.Left = transformed;
            transformed = e;
        }

        public override void VisitBinaryExpression(BinaryExpression e, VisitMode mode)
        {
            e.Left.Visit(this, mode);
            e.Left = transformed;
            e.Right.Visit(this, mode);
            e.Right = transformed;
            transformed = e;
        }

        public override void VisitBreakStatement(BreakStatement s)
        {
        }

        public override void VisitCastExpression(CastExpression e, VisitMode mode)
        {
            e.Expression.Visit(this, mode);
            e.Expression = transformed;
            transformed = e;
        }

        public override void VisitClass(ClassType c)
        {
        }

        public override void VisitConstantExpression(ConstantExpression e, VisitMode mode)
        {
            transformed = e;
        }

        public override void VisitContinueStatement(ContinueStatement s)
        {
        }

        public override void VisitDebugStatement(DebugStatement s)
        {
        }

        public override void VisitDerefExpression(DerefExpression e, VisitMode mode)
        {
            e.Expression.Visit(this, mode);
            e.Expression = transformed;
            transformed = e;
        }

        public override void VisitDoStatement(DoStatement s)
        {
            s.Condition.Visit(this, VisitMode.Load);
            s.Condition = transformed;
            s.Body.Visit(this);
        }

        public override void VisitExpressionStatement(ExpressionStatement s)
        {
            s.Expression.Visit(this, VisitMode.Load);
            s.Expression = transformed;
        }

        public override void VisitFieldExpression(FieldExpression e, VisitMode mode)
        {
            if (e.Field is PropertySymbol ps)
            {
                if (ps.Getter != null)
                {
                    e.Expression.Visit(this, VisitMode.Load);
                    e.Expression = transformed;

                    if (ps.Getter.Flags.HasFlag(SymbolFlags.Virtual))
                    {
                        var vtf = new FieldExpression(e.Expression, e.Expression.DataType.ElementType.FindChild("vtable"));
                        var vie = new IndexExpression(vtf, new ConstantExpression(ps.Getter.VSlot));
                        var vcs = new CastExpression(vie, ps.Getter.DataType.GetPointerType());
                        var vdr = new DerefExpression(vcs);
                        var fc = new FunctionCallExpression(vdr);
                        fc.Arguments.Add(e.Expression);
                        transformed = fc;
                    }
                    else if (ps.Getter.Flags.HasFlag(SymbolFlags.Static))
                    {
                        var fc = new FunctionCallExpression(new SymbolExpression(ps.Getter.DataType));
                        transformed = fc;
                    }
                    else
                    {
                        var fc = new FunctionCallExpression(new SymbolExpression(ps.Getter.DataType));
                        fc.Arguments.Add(e.Expression);
                        transformed = fc;
                    }
                    transformed.file = e.file;
                    transformed.line = e.line;
                    //transformed.clearFlag = true;
                    return;
                }
                else
                {
                    Error($"Property '{ps.Name}' is write-only", e.file, e.line);
                }
            }

            e.Expression.Visit(this, mode);
            e.Expression = transformed;
            transformed = e;
        }

        public override void VisitForStatement(ForStatement s)
        {
            if (s.Init != null)
            {
                s.Init.Visit(this, VisitMode.Load);
                s.Init = transformed;
            }
            if (s.Condition != null)
            {
                s.Condition.Visit(this, VisitMode.Load);
                s.Condition = transformed;
            }
            if (s.Iter != null)
            {
                s.Iter.Visit(this, VisitMode.Load);
                s.Iter = transformed;
            }
            s.Body.Visit(this);
        }

        public override void VisitFunction(Function f)
        {
            /*foreach (var lv in f.Locals)
            {
                if (lv.Init != null)
                {
                    lv.Init.Visit(this, VisitMode.Load);
                    lv.Init = transformed;
                }
            }*/
            if (f.Body != null)
                f.Body.Visit(this);
        }

        public override void VisitFunctionCallExpression(FunctionCallExpression e, VisitMode mode)
        {
            e.Callee.Visit(this, VisitMode.Load);
            e.Callee = transformed;

            for (int i = 0; i < e.Arguments.Count; i++)
            {
                e.Arguments[i].Visit(this, VisitMode.Load);
                e.Arguments[i] = transformed;
            }
            transformed = e;
        }

        public override void VisitGlobalVariable(GlobalVariable s)
        {
        }

        public override void VisitIfStatement(IfStatement s)
        {
            s.Condition.Visit(this, VisitMode.Load);
            s.Condition = transformed;
            s.Then.Visit(this);
            if (s.Else != null)
                s.Else.Visit(this);
        }

        public override void VisitIndexExpression(IndexExpression e, VisitMode mode)
        {
            if (e.Expression.DataType.IsPointer() && e.Expression.DataType.ElementType.IsClass())
            {
                var cls = e.Expression.DataType.ElementType;
                var m = cls.FindChild("get_item") as MethodSymbol;
                if (m != null)
                {
                    e.Expression.Visit(this, VisitMode.Load);
                    e.Expression = transformed;
                    var i = e.Index;
                    Parser.ForceCast(ref i, (m.DataType as Function).Parameters[1].DataType);
                    i.Visit(this, VisitMode.Load);
                    e.Index = transformed;

                    if (m.Flags.HasFlag(SymbolFlags.Virtual))
                    {
                        var vtf = new FieldExpression(e.Expression, e.Expression.DataType.ElementType.FindChild("vtable"));
                        var vie = new IndexExpression(vtf, new ConstantExpression(m.VSlot));
                        var vcs = new CastExpression(vie, m.DataType.GetPointerType());
                        var vdr = new DerefExpression(vcs);
                        var fc = new FunctionCallExpression(vdr);
                        fc.Arguments.Add(e.Expression);
                        fc.Arguments.Add(e.Index);
                        transformed = fc;
                    }
                    else
                    {
                        var fc = new FunctionCallExpression(new SymbolExpression(m));
                        fc.Arguments.Add(e.Expression);
                        fc.Arguments.Add(e.Index);
                        transformed = fc;
                    }

                    transformed.file = e.file;
                    transformed.line = e.line;
                    //transformed.clearFlag = true;

                    return;
                }
            }

            e.Expression.Visit(this, mode);
            e.Expression = transformed;
            e.Index.Visit(this, mode);
            e.Index = transformed;
            transformed = e;
        }

        public override void VisitModule(Module m)
        {
        }

        public override void VisitPostFixExpression(PostFixExpression e, VisitMode mode)
        {
            e.Operand.Visit(this, mode);
            e.Operand = transformed;
            e.Operation.Visit(this, mode);
            e.Operation = transformed;
            transformed = e;
        }

        public override void VisitReturnStatement(ReturnStatement s)
        {
            if (s.Expression != null)
            {
                s.Expression.Visit(this, VisitMode.Load);
                s.Expression = transformed;
            }
        }

        public override void VisitSizeOfExpression(SizeOfExpression e, VisitMode mode)
        {
            transformed = e;
        }

        public override void VisitStatementBlock(StatementBlock s)
        {
            foreach (var e in s.Statements)
            {
                e.Visit(this);
            }
        }

        public override void VisitStruct(StructType s)
        {

        }

        public override void VisitSymbolExpression(SymbolExpression e, VisitMode mode)
        {
            transformed = e;
        }

        public override void VisitTernaryExpression(TernaryExpression e, VisitMode mode)
        {
            e.Condition.Visit(this, VisitMode.Load);
            e.Condition = transformed;
            e.Positive.Visit(this, mode);
            e.Positive = transformed;
            e.Negative.Visit(this, mode);
            e.Negative = transformed;
            transformed = e;
        }

        public override void VisitUnaryExpression(UnaryExpression e, VisitMode mode)
        {
            e.Expression.Visit(this, mode);
            e.Expression = transformed;
            transformed = e;
        }

        public override void VisitWhileStatement(WhileStatement s)
        {
            s.Condition.Visit(this, VisitMode.Load);
            s.Condition = transformed;
            s.Body.Visit(this);
        }
    }
}
