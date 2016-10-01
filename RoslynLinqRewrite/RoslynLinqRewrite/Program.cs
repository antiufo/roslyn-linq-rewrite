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
    class Program
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
#if DESKTOP
            EnsureInstalled();
            if (!MaybeSyncCsc()) return 1;
#endif

            var useCsc = !args.Contains("--show") && (args.Contains("--csc") || args.Any(x => x.StartsWith("@") || x.EndsWith(".cs") || x.StartsWith("--reference") || x.StartsWith("-r:") || x.StartsWith("-out:")));
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
                if (args.Any(x => x.StartsWith("--temp-output:")))
                {
                    return Microsoft.DotNet.Tools.Compiler.Csc.CompileCscLinqRewriteCommand.Run(args);
                }
                else
                {
                    return Microsoft.CodeAnalysis.CSharp.CommandLine.ProgramLinqRewrite.MainInternal(args);
                }
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

            if (file == null || Directory.Exists(file))
            {
                var candidates = Directory.EnumerateFiles(file ?? Directory.GetCurrentDirectory())
                    .Where(x => Path.GetFileName(x) == "project.json" || Path.GetExtension(x) == ".sln" || Path.GetExtension(x) == ".csproj")
                    .ToList();
                if (candidates.Count == 0)
                {
                    PrintUsage();
                    return 1;
                }
                if (candidates.Count > 1)
                {
                    Console.WriteLine("Multiple projects found in " + (file != null ? "'" + file + "'." : "the current directory."));
                    Console.WriteLine("Please specify a specific project file.");
                    return 1;
                }
                file = candidates[0];
            }
            file = Path.GetFullPath(file);
            if (!File.Exists(file))
            {
                Console.WriteLine("File does not exist: '" + file + "'");
                return 1;
            }
            var ext = Path.GetExtension(file).ToLower();
            if (ext == ".xproj")
            {
                Console.WriteLine("XPROJ files are not supported. Please target the corresponding project.json instead.");
                return 1;
            }
            else if (ext == ".sln" || ext == ".csproj")
            {
                var projectNamesIdx = Array.IndexOf(args, "--project");
                var projectNames = projectNamesIdx != -1 ? args[projectNamesIdx + 1].Split(',') : null;

                var release = args.Contains("--release");
                CompileSolution(file, projectNames, release, args.Contains("--detailed"));
                if (!release && !args.Contains("--debug"))
                {
                    Console.WriteLine("Note: for consistency with MSBuild, this tool compiles by default in debug mode. Consider specifying --release.");
                }
                return 0;
            }
            else if (ext == ".json")
            {
                if (args.Contains("--configure"))
                {
                    ConfigureProjectJson(file);
                    return 0;
                }
                else
                {
                    Console.WriteLine("In order to build project.json projects, please specify the appropriate configuration in \"compilerName\" and \"tools\", and then use \"dotnet build\".");
                    Console.WriteLine("Use roslyn-linq-rewrite <path-to-project-json> --configure to let the tool automatically perform this task for you.");
                    return 1;
                }
            }
            else
            {
                Console.WriteLine("Unsupported project type: " + ext);
                return 1;
            }

        }
#if DESKTOP
        private void EnsureInstalled()
        {
            var dir = Path.GetDirectoryName(typeof(Program).GetTypeInfo().Assembly.Location);
            if (!File.Exists(Path.Combine(dir, "installed")) && !dir.Contains("bin\\Debug") && !dir.Contains("bin\\Release") )
            {
                Console.WriteLine("Installing dependencies for first use…");
                var init = Path.Combine(dir, "Shaman.Roslyn.LinqRewrite.Initialization.dll");
                var exe = Path.Combine(dir, "Shaman.Roslyn.LinqRewrite.Initialization.exe");
                File.Copy(init, exe, true);
                var p = new ProcessStartInfo();
                p.WorkingDirectory = dir;
                p.UseShellExecute = false;
                p.Arguments = "--install";
                p.FileName = Path.Combine(dir, exe);
                using (var pr = Process.Start(p))
                {
                    pr.WaitForExit();
                    if (pr.ExitCode != 0)
                        throw new Exception("An error occured.");
                }
                try
                {
                    File.Delete(exe);
                }
                catch
                {
                }
            }
        }

        private bool MaybeSyncCsc()
        {
            if (Debugger.IsAttached) return true;
            // We need a copy of the program called "csc.exe". MSBuild only allows to specify the folder, not the full path of csc (CscToolPath).
            var exe = typeof(Program).GetTypeInfo().Assembly.Location;

            var dir = Path.GetDirectoryName(exe);
            var master = Path.Combine(dir, "roslyn-linq-rewrite.exe");
            var csc = Path.Combine(dir, "csc.exe");
            foreach (var file in Directory.EnumerateFiles(dir, "csc.*.tmp"))
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                }
            }
            if (File.Exists(master) && !FilesLookEqual(csc, master))
            {
                Console.WriteLine("csc.exe is out of sync with roslyn-linq-rewrite.exe. Synchronizing executables (required by msbuild)…");
                try
                {
                    File.Delete(csc);
                }
                catch (Exception)
                {
                    var tmp = csc + "." + Guid.NewGuid() + ".tmp";
                    File.Move(csc, tmp);
                }
                File.Copy(master, csc, true);
                File.Copy(master + ".config", csc + ".config", true);

                if (Path.GetFileNameWithoutExtension(exe).Equals("csc", StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine("Synchronized. Please restart the build process");
                    return false;
                }

            }


            return true;
        }
#endif
        private void ConfigureProjectJson(string file)
        {
            var f = File.ReadAllText(file);
            var json = JObject.Parse(f);

            var tools = json["tools"];
            if (tools == null)
            {
                tools = new JObject();
                json["tools"] = tools;
            }

            var rewriteTool = new JObject();
            var vers = typeof(Program).GetTypeInfo().Assembly.GetName().Version.ToString();
            rewriteTool["version"] = vers != "1.0.0.0" ? vers : "1.0.1.11";
            rewriteTool["imports"] = "portable-net45+win8+wp8+wpa81";
            tools["dotnet-compile-csc-linq-rewrite"] = rewriteTool;

            var buildOptions = json["buildOptions"];
            if (buildOptions == null)
            {
                buildOptions = json["compilationOptions"];
                if (buildOptions != null)
                {
                    json.Property("compilationOptions").Replace(new JProperty("buildOptions", buildOptions));
                }
                else
                {
                    buildOptions = new JObject();
                    json["buildOptions"] = buildOptions;
                }
            }

            buildOptions["compilerName"] = "csc-linq-rewrite";
            File.WriteAllText(file, json.ToString(Newtonsoft.Json.Formatting.Indented), Encoding.UTF8);
            Console.WriteLine("Updated '" + file + "'. You can now use dotnet restore && dotnet build.");
        }

        private void PrintUsage()
        {
            Console.WriteLine("roslyn-linq-rewrite " + typeof(Program).GetTypeInfo().Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
            Console.WriteLine(
@"github.com/antiufo/roslyn-linq-rewrite

Usage:
  roslyn-linq-rewrite <path-to-csproj> [options]
  roslyn-linq-rewrite <path-to-sln> [options]
  roslyn-linq-rewrite <path-to-project-json> --configure
  roslyn-linq-rewrite <standard-csc-parameters>
  roslyn-linq-rewrite --show <path-to-cs>

Options for project.json:
  --configure                   Configures project.json to use the roslyn-linq-rewrite compiler.

Options for .sln files:
  --project ProjectA            Determines which project(s) to compile (default: all).

Options for .sln and .csproj files:
  --debug/--release             Sets the project configuration

Options for csc.exe mode:
  --csc /?

Options for translation preview mode:
  --show                        Translates and shows the produced code for a single .cs file
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

        private static void CompileSolution(string path, IReadOnlyList<string> projectNames, bool release, bool detailed)
        {
            var a = new List<object>();
            a.Add(path);
            if (release)
                a.Add(new Shaman.Runtime.ProcessUtils.RawCommandLineArgument("/p:Configuration=Release"));

            // MSBuild doesn't take CscToolPath into account when deciding whether to recompile. Rebuild always.
            a.Add(new Shaman.Runtime.ProcessUtils.RawCommandLineArgument("/t:Rebuild"));

            if (detailed)
                a.Add(new Shaman.Runtime.ProcessUtils.RawCommandLineArgument("/verbosity:detailed"));

            if (projectNames != null) throw new ArgumentException("--project is not allowed when --internal-build-process is not used.");
            a.Add(new Shaman.Runtime.ProcessUtils.RawCommandLineArgument("/p:CscToolPath=\"" + Path.GetDirectoryName(typeof(Program).GetTypeInfo().Assembly.Location) + "\""));

                
            RunMsbuild(a);
              

        }
        
        private static void RunMsbuild(List<object> args)
        {
            var argsArray = args.ToArray();
            var msbuildCandidates = new[] {
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
