using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Shaman.Runtime;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using System.Diagnostics;
#if false
using Microsoft.Dnx.Runtime;
using Microsoft.Dnx.Tooling;
using Microsoft.Dnx.Runtime.Common.CommandLine;
#endif

namespace Shaman.Roslyn.LinqRewrite
{
    public class Program
    {
        
#if false
        private static IServiceProvider _hostServices;
        private static IApplicationEnvironment _environment;
        private static IRuntimeEnvironment _runtimeEnv;
        public Program(IServiceProvider hostServices, IApplicationEnvironment environment, IRuntimeEnvironment runtimeEnv)
        {
            _hostServices = hostServices;
            _environment = environment;
            _runtimeEnv = runtimeEnv;
        }
#endif
        public static int Main(string[] args)
        {
            try
            {
                var p = new Program();
                return p.MainInternal(args);
            }
            catch (ExitException ex)
            {
                return ex.Code;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return 1;
            }
        }
        private int MainInternal(string[] args)
        {

            var useCsc = Path.GetFileName(Assembly.GetEntryAssembly().Location).Equals("csc.exe", StringComparison.OrdinalIgnoreCase) || args.Contains("--csc");
            if (useCsc)
            {
                var saveCmdline = Environment.GetEnvironmentVariable("ROSLYN_LINQ_REWRITE_DUMP_CSC_CMDLINE");
                if (!string.IsNullOrEmpty(saveCmdline) && saveCmdline != "0")
                {
                    // This exe works in this way:
                    // 1. It launches msbuild with a custom CscToolPath
                    // 2. Is launched again by msbuild and acts as a csc.exe compiler
                    // In order to more easily debug the "inner" execution, you can set the above variable to 1 and then launch
                    // roslyn-linq-rewrite <path-to-csproj>. Then, set the command line options in Visual Studio to
                    // @C:\Path\To\roslyn-linq-rewrite-csc-command-line.rsp
                    // Don't forget to also set the working directory to be the folder with the .csproj.
                    // That file will be located in the project folder (not in the initial working directory)
                    // You can now debug what happens when the program is being called by msbuild for the actual compilation.


                    File.WriteAllText("roslyn-linq-rewrite-csc-command-line.txt", Environment.CommandLine);
                    var at = args.FirstOrDefault(x => x.StartsWith("@"));
                    if (at != null)
                    {
                        File.WriteAllText("roslyn-linq-rewrite-csc-command-line.rsp", File.ReadAllText(at.Substring(1)));
                    }
                }
                args = args.Where(x => x != "--csc").ToArray();
                return Microsoft.CodeAnalysis.CSharp.CommandLine.ProgramLinqRewrite.MainInternal(args);
            }
            if ((args.Contains("-h") || args.Contains("--help") || args.Contains("/?")))
            {
                PrintUsage();
                return 0;
            }
            if (args.Contains("--sandbox"))
            {
                Sandbox();
                return 0;
            }

            var file = args.TakeWhile(x => !x.StartsWith("--")).FirstOrDefault();

            if (args.Contains("--show"))
            {
                file = args.FirstOrDefault(x => !x.StartsWith("--"));
                if (file == null) throw new ArgumentException("No input .cs was specified.");
                CompileExample(file, false);
                return 0;
            }

            

            var release = args.Contains("/p:Configuration=Release");


            var a = new List<object>();
      
            // MSBuild doesn't take CscToolPath into account when deciding whether to recompile. Rebuild always.

            if(!args.Any(x => x.StartsWith("/t:") || x.StartsWith("/target:")))
                a.Add(new Shaman.Runtime.ProcessUtils.RawCommandLineArgument("/t:Rebuild"));

            var infofile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");
            Environment.SetEnvironmentVariable("ROSLYN_LINQ_REWRITE_OUT_STATISTICS_TO", infofile);

            a.Add(new Shaman.Runtime.ProcessUtils.RawCommandLineArgument("/p:CscToolPath=\"" + Path.GetDirectoryName(typeof(Program).GetTypeInfo().Assembly.Location) + "\""));


            a.AddRange(args);
            int exitcode = 0;
            try
            {
                RunMsbuild(a);
            }
            catch (ProcessException ex)
            {
                exitcode = ex.ExitCode;
            }

            Console.WriteLine();
            if (File.Exists(infofile))
            {
                Console.WriteLine(File.ReadAllText(infofile));
                File.Delete(infofile);
            }

            if (!release)
            {
                Console.WriteLine("Note: for consistency with MSBuild, this tool compiles by default in debug mode. Consider specifying /p:Configuration=Release.");
            }
            return exitcode;
            


        }

   
        private void PrintUsage()
        {
            Console.WriteLine("roslyn-linq-rewrite " + typeof(Program).GetTypeInfo().Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
            Console.WriteLine(
@"github.com/antiufo/roslyn-linq-rewrite

Usage:
  roslyn-linq-rewrite <path-to-csproj> [msbuild-options]
  roslyn-linq-rewrite <path-to-sln> [msbuild-options]
  roslyn-linq-rewrite --show <path-to-cs>

If you prefer to call msbuild directly, use msbuild /t:Rebuild /p:CscToolPath=""" + Path.GetDirectoryName(typeof(Program).Assembly.Location) +@"""
However, you won't see statistics about the rewritten methods.

");
        }

        private void Sandbox()
        {
            CompileExample("Example3.cs");
        }

        private void CompileExample(string path, bool devPath = true)
        {
            var source = File.ReadAllText(devPath ? Path.Combine("../../Samples/", path) : path);
            var isScript = Path.GetExtension(path).Equals(".csx");
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(kind: isScript ? SourceCodeKind.Script : SourceCodeKind.Regular));
            var references = new[] {
                MetadataReference.CreateFromFile(typeof(int).GetTypeInfo().Assembly.Location), // mscorlib
                MetadataReference.CreateFromFile(typeof(Uri).GetTypeInfo().Assembly.Location), // System
                MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).GetTypeInfo().Assembly.Location), // System.Core
                };
            var compilation = isScript
                ? CSharpCompilation.CreateScriptCompilation("LinqRewriteExample", syntaxTree, references)
                : CSharpCompilation.Create("LinqRewriteExample", new[] { syntaxTree }, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));


            var hasErrs = false;
            foreach (var item in compilation.GetDiagnostics())
            {
                if (item.Severity == DiagnosticSeverity.Error) hasErrs = true;
                PrintDiagnostic(item);
            }

            if (hasErrs) return;

            var rewriter = new LinqRewriter(compilation.GetSemanticModel(syntaxTree));
            var rewritten = rewriter.Visit(syntaxTree.GetRoot());

            hasErrs = false;
            foreach (var item in compilation.GetDiagnostics())
            {
                if (item.Severity == DiagnosticSeverity.Error) hasErrs = true;
                if (item.Severity == DiagnosticSeverity.Warning) continue;
                PrintDiagnostic(item);
            }
            if (hasErrs) return;
            Console.WriteLine(rewritten.ToString());


        }

        private static void PrintDiagnostic(Diagnostic item)
        {
            if (item.Severity == DiagnosticSeverity.Hidden) return;
            if (item.Severity == DiagnosticSeverity.Error) Console.ForegroundColor = ConsoleColor.Red;
            if (item.Severity == DiagnosticSeverity.Warning) Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(item);
            Console.ResetColor();
        }

  
        
        private static void RunMsbuild(List<object> args)
        {
            var argsArray = args.ToArray();

            
            var msbuildCandidates = new List<string>() {
                @"%ProgramFiles(x86)%\Microsoft Visual Studio\Preview\Enterprise\MSBuild\15.0\Bin\amd64\MSBuild.exe",
                @"%ProgramFiles(x86)%\Microsoft Visual Studio\Preview\Professional\MSBuild\15.0\Bin\amd64\MSBuild.exe",
                @"%ProgramFiles(x86)%\Microsoft Visual Studio\Preview\Community\MSBuild\15.0\Bin\amd64\MSBuild.exe",
                @"%ProgramFiles(x86)%\Microsoft Visual Studio\VS16\MSBuild\16.0\Bin\amd64\MSBuild.exe",
                @"%ProgramFiles(x86)%\Microsoft Visual Studio\VS15\MSBuild\15.0\Bin\amd64\MSBuild.exe",
                @"%ProgramFiles(x86)%\Microsoft Visual Studio\VS15Preview\MSBuild\15.0\Bin\amd64\MSBuild.exe",
                @"%ProgramFiles(x86)%\MSBuild\15.0\Bin\amd64\MSBuild.exe",
                @"%ProgramFiles(x86)%\MSBuild\14.0\Bin\amd64\MSBuild.exe",
                @"%ProgramFiles(x86)%\MSBuild\12.0\Bin\amd64\MSBuild.exe",
                @"%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe",
            };

            try
            {
                var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\VisualStudio\SxS", false);
                if (key != null)
                {
                    foreach (var subkey in key.GetSubKeyNames())
                    {
                        var z = key.OpenSubKey(subkey);
                        var ids = z.GetValueNames();
                        foreach (var id in ids)
                        {
                            var path = z.GetValue(id) as string;
                            if (!string.IsNullOrEmpty(path))
                            {
                                msbuildCandidates.Add(Path.Combine(path, @"MSBuild\17.0\Bin\amd64\MSBuild.exe"));
                                msbuildCandidates.Add(Path.Combine(path, @"MSBuild\16.0\Bin\amd64\MSBuild.exe"));
                                msbuildCandidates.Add(Path.Combine(path, @"MSBuild\15.0\Bin\amd64\MSBuild.exe"));
                            }
                            
                        }
                    }
                }
            }
            catch
            {
            }

            
            try
            {
                ProcessUtils.RunPassThrough("msbuild", argsArray);
            }
            catch (Exception ex) when (!(ex is ProcessException))
            {
                foreach (var candidate in msbuildCandidates)
                {
                    var path = Environment.ExpandEnvironmentVariables(candidate);
                    if (File.Exists(path))
                    {
                        ProcessUtils.RunPassThrough(path, argsArray);
                        return;
                    }

                    if (path.Contains("\\amd64"))
                    {
                        path = path.Replace("\\amd64", string.Empty);
                        if (File.Exists(path))
                        {
                            ProcessUtils.RunPassThrough(path, argsArray);
                            return;
                        }
                    }
                }
                ProcessUtils.RunPassThrough("xbuild", argsArray);
            }
        }
        
        private static bool FilesLookEqual(string a, string b)
        {
            var sfi = new FileInfo(a);
            var dfi = new FileInfo(b);
            return sfi.Exists == dfi.Exists && sfi.LastWriteTimeUtc == dfi.LastWriteTimeUtc && sfi.Length == dfi.Length;
        }
    }
}
