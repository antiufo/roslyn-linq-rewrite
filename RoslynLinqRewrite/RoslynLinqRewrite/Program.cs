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
        private static bool NoRewrite;
        private static bool WriteFiles;
        private readonly static string DotnetCscRewriteVersion = "1.0.1.9";
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
            var p = new Program();
            return p.MainInternal(args);
        }
        private int MainInternal(string[] args)
        {
            var exe = typeof(Program).Assembly.Location;
            if (Path.GetFileNameWithoutExtension(exe).Equals("csc", StringComparison.OrdinalIgnoreCase))
            {
                var master = Path.Combine(Path.GetDirectoryName(exe), "roslyn-linq-rewrite.exe");
                if (File.Exists(master) && !FilesLookEqual(exe, master))
                {
                    Console.WriteLine("csc.exe is out of sync with roslyn-linq-rewrite.exe. Synchronizing executables…");
                    var tmp = exe + "." + Guid.NewGuid() + ".tmp";
                    File.Move(exe, tmp);
                    File.Copy(master, exe, true);
                    File.Copy(master + ".config", exe + ".config", true);
                    try
                    {
                        File.Delete(tmp);
                    }
                    catch
                    {
                    }



                    Console.Error.WriteLine("Synchronized. Please restart the build process");
                    return 1;

                }
            }
            var useCsc = !args.Contains("--show") && (args.Contains("--csc") || args.Any(x => x.StartsWith("@") || x.EndsWith(".cs") || x.StartsWith("--reference") || x.StartsWith("-r:") || x.StartsWith("-out:")));
            if (useCsc)
            {
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
                var candidates = Directory.EnumerateFiles(file ?? Environment.CurrentDirectory)
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

                CompileSolution(file, projectNames, !args.Contains("--debug"), !args.Contains("--skip-generate-resources"));
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
            rewriteTool["version"] = DotnetCscRewriteVersion;
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
            Console.WriteLine("roslyn-linq-rewrite " + typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
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
  --debug/--release             Sets the project configuration (default: release)
  --skip-generate-resources     Skips .resources files generation (relies on existing ones)

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

            var workspace = new AdhocWorkspace();
            var proj = workspace.AddProject("LinqRewriteExample", "C#").WithMetadataReferences(
                new[] {
                MetadataReference.CreateFromFile(typeof(int).GetTypeInfo().Assembly.Location), // mscorlib
                MetadataReference.CreateFromFile(typeof(Uri).GetTypeInfo().Assembly.Location), // System
                MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).GetTypeInfo().Assembly.Location), // System.Core
                }
                ).WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            // proj = proj.AddDocument("FastLinqExtensions.cs", File.ReadAllText("../../../Shaman.FastLinq.Sources/FastLinqExtensions.cs")).Project;
            var doc = proj.AddDocument("source.cs", File.ReadAllText(devPath ? Path.Combine("../../Samples/", path) : path));

            if (!workspace.TryApplyChanges(doc.Project.Solution)) throw new Exception();
            proj = doc.Project;
            var comp = proj.GetCompilationAsync().Result;

            var hasErrs = false;
            foreach (var item in comp.GetDiagnostics())
            {
                if (item.Severity == DiagnosticSeverity.Error) hasErrs = true;
                PrintDiagnostic(item);
            }

            if (hasErrs) return;

            var syntaxTree = doc.GetSyntaxTreeAsync().Result;
            var rewriter = new LinqRewriter(comp.GetSemanticModel(syntaxTree));
            var rewritten = rewriter.Visit(syntaxTree.GetRoot());
            proj = doc.WithSyntaxRoot(rewritten).Project;

            hasErrs = false;
            foreach (var item in proj.GetCompilationAsync().Result.GetDiagnostics())
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

        private static void CompileSolution(string path, IReadOnlyList<string> projectNames, bool release, bool generateResources)
        {
            var properties = new Dictionary<string, string>();
            if (release)
                properties.Add("Configuration", "Release");
#if CORECLR
            throw new NotSupportedException("Compiling CSPROJ files is not supported on CORECLR");
#else
            var workspace = Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace.Create(properties);

            Solution solution = null;
            if (".csproj".Equals(Path.GetExtension(path), StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Building '" + path + "'");
                CompileProject(workspace.OpenProjectAsync(path).Result, release, generateResources);
            }
            else
            {
                Console.WriteLine("Loading '" + path + "'");
                solution = workspace.OpenSolutionAsync(path).Result;
                var projsToCompile = projectNames != null ? solution.Projects.Where(x => projectNames.Contains(x.Name)).Select(x => x.Id).ToList() : null;

                var missing = projectNames?.FirstOrDefault(x => !solution.Projects.Any(y => y.Name == x));
                if (missing != null)
                {
                    throw new ArgumentException("Cannot find project '" + missing + "'.");
                }

                foreach (var projid in solution.GetProjectDependencyGraph().GetTopologicallySortedProjects())
                {
                    if (projsToCompile == null || projsToCompile.Contains(projid))
                    {
                        var proj = solution.GetProject(projid);
                        Console.WriteLine("Building " + proj.Name);
                        CompileProject(proj, release, generateResources);
                    }
                }
            }

#endif

        }

        private static void CompileProject(Microsoft.CodeAnalysis.Project project, bool release, bool generateResources)
        {
            var compilation = project.GetCompilationAsync().Result;

            var hasErrs = false;
            foreach (var item in compilation.GetDiagnostics())
            {
                PrintDiagnostic(item);
                if (item.Severity == DiagnosticSeverity.Error) hasErrs = true;
            }

            if (hasErrs) throw new ExitException(1);
            var updatedProject = project;
            if (!NoRewrite)
            {

                Console.WriteLine("Rewriting LINQ to procedural code...");


                var rewrittenLinqInvocations = 0;
                var rewrittenMethods = 0;

                foreach (var doc in project.Documents)
                {
                    var syntaxTree = doc.GetSyntaxTreeAsync().Result;

                    var rewriter = new LinqRewriter(compilation.GetSemanticModel(syntaxTree));

                    var rewritten = rewriter.Visit(syntaxTree.GetRoot());
                    if (WriteFiles)
                    {
                        rewritten = rewritten.NormalizeWhitespace();
                        var tostring = rewritten.ToFullString();
                        if (syntaxTree.ToString() != tostring)
                        {
                            File.WriteAllText(doc.FilePath, tostring, Encoding.UTF8);
                        }
                    }

                    rewrittenLinqInvocations += rewriter.RewrittenLinqQueries;
                    rewrittenMethods += rewriter.RewrittenMethods;

                    updatedProject = updatedProject.GetDocument(doc.Id).WithSyntaxRoot(rewritten).Project;

                }
                Console.WriteLine(string.Format("Rewritten {0} LINQ queries in {1} methods as procedural code.", rewrittenLinqInvocations, rewrittenMethods));
                project = updatedProject;
                compilation = project.GetCompilationAsync().Result;
                hasErrs = false;
                foreach (var item in compilation.GetDiagnostics())
                {
                    if (item.Severity == DiagnosticSeverity.Error)
                    {
                        PrintDiagnostic(item);
                        hasErrs = true;
                        if (item.Location != Location.None)
                        {
                            Console.ForegroundColor = ConsoleColor.White;
                            //var lines = item.Location.GetLineSpan();
                            var node = item.Location.SourceTree.GetRoot().FindNode(item.Location.SourceSpan);
                            var k = node.AncestorsAndSelf().FirstOrDefault(x => x is MethodDeclarationSyntax);
                            if (k != null)
                            {
                                Console.WriteLine(k.ToString());
                            }
                            Console.ResetColor();
                        }
                    }
                }
            }
            var outputPath = project.OutputFilePath;
            var objpath = GetObjPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(objpath));
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));


            var ns = XNamespace.Get("http://schemas.microsoft.com/developer/msbuild/2003");
            var xml = XDocument.Load(project.FilePath);
            var hasResources = xml
                .DescendantNodes()
                .OfType<XElement>()
                .Where(x => x.Name == ns + "EmbeddedResource" || x.Name == ns + "Resource" || x.Name == ns + "CopyToOutputDirectory")
                .Any();
            if (hasResources && generateResources)
            {
                Console.WriteLine("Compiling resources");
                foreach (var resource in Directory.GetFiles(Path.GetDirectoryName(objpath), "*.resources"))
                {
                    File.Delete(resource);
                }

                var args = new List<object>();
                args.Add(project.FilePath);
                args.Add("/verbosity:quiet");
                if (release)
                    args.Add(new Shaman.Runtime.ProcessUtils.RawCommandLineArgument("/p:Configuration=Release"));


                try
                {
                    ProcessUtils.RunPassThrough("msbuild", args.ToArray());
                }
                catch (Exception ex) when (!(ex is ProcessException))
                {
                    ProcessUtils.RunPassThrough("xbuild", args.ToArray());
                }
            }
            else
            {
                CopyReferencedDllsToOutput(project);
            }
            var resources = hasResources ? Directory.EnumerateFiles(Path.GetDirectoryName(objpath), "*.resources")
                .Select(x =>
                {
                    return new ResourceDescription(Path.GetFileName(x), () => File.OpenRead(x), true);
                }).ToList() : Enumerable.Empty<ResourceDescription>();

            compilation.Emit(objpath, manifestResources: resources);


            File.Copy(objpath, outputPath, true);



            Console.WriteLine("Compiled.");

            if (hasErrs) throw new ExitException(1);
        }

        private static void CopyReferencedDllsToOutput(Project project)
        {
            foreach (var reference in project.MetadataReferences)
            {
                var per = reference as PortableExecutableReference;
                if (per != null)
                {
                    if (!per.FilePath.Replace("\\", "/").Contains("/Reference Assemblies/"))
                    {
                        MaybeCopyReference(per.FilePath, project);
                    }
                }
            }
            foreach (var reference in project.AllProjectReferences)
            {
                var proj = project.Solution.GetProject(reference.ProjectId);
                if (proj != null)
                {
                    var path = GetObjPath(proj.OutputFilePath);
                    if (!File.Exists(path)) path = proj.OutputFilePath;
                    MaybeCopyReference(path, project);
                }
            }
        }

        private static string GetObjPath(string outputFilePath)
        {
            return outputFilePath
                .Replace("/bin/", "/obj/")
                .Replace("\\bin\\", "\\obj\\");
        }

        private static void MaybeCopyReference(string source, Project project)
        {
            var dest = Path.Combine(Path.GetDirectoryName(project.OutputFilePath), Path.GetFileName(source));
            if (File.Exists(source) && !source.Equals(dest, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(dest))
                {
                    if (FilesLookEqual(source, dest)) return;
                }
                File.Copy(source, dest, true);
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
