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
        private static bool ForProjBuild;
        private static bool WriteFiles;
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

            CompileExample("Example2.cs");


            return 0;
        }

        private void CompileExample(string path)
        {

            var workspace = new AdhocWorkspace();
            var proj = workspace.AddProject("LinqRewriteExample", "C#").WithMetadataReferences(
                new[] {
                MetadataReference.CreateFromFile(typeof(int).GetTypeInfo().Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).GetTypeInfo().Assembly.Location),
                }
                ).WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var doc = proj.AddDocument("source.cs", File.ReadAllText(Path.Combine("../../Samples/", path)));
            proj = proj.AddDocument("FastLinqExtensions.cs", File.ReadAllText("../../../Shaman.FastLinq.Sources/FastLinqExtensions.cs")).Project;
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

        private static void CompileSolution(string path, string projectName)
        {
            var properties = new Dictionary<string, string>();
            properties.Add("Configuration", "Release");
#if CORECLR
            throw new NotSupportedException("Compiling CSPROJ files is not supported on CORECLR");
#else
            var workspace = Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace.Create(properties);

            Solution solution = null;
            if (".csproj".Equals(Path.GetExtension(path), StringComparison.OrdinalIgnoreCase))
            {
                CompileProject(workspace.OpenProjectAsync(path).Result);
            }
            else
            {
                solution = workspace.OpenSolutionAsync(path).Result;
                if (projectName != null)
                {
                    CompileProject(solution.Projects.Single(x => x.Name == projectName));
                }
                else
                {
                    foreach (var project in solution.Projects)
                    {
                        CompileProject(project);
                    }
                }
            }

#endif

        }

        private static void CompileProject(Microsoft.CodeAnalysis.Project project)
        {
            project = project.WithParseOptions(((CSharpParseOptions)project.ParseOptions).WithPreprocessorSymbols(project.ParseOptions.PreprocessorSymbolNames.Concat(new[] { "LINQREWRITE" })));
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
                foreach (var doc in project.Documents)
                {
                    Console.WriteLine(doc.FilePath);
                    var syntaxTree = doc.GetSyntaxTreeAsync().Result;

                    var rewriter = new LinqRewriter(compilation.GetSemanticModel(syntaxTree));

                    var rewritten = rewriter.Visit(syntaxTree.GetRoot()).NormalizeWhitespace();
                    if (WriteFiles)
                    {
                        var tostring = rewritten.ToFullString();
                        if (syntaxTree.ToString() != tostring)
                        {
                            File.WriteAllText(doc.FilePath, tostring, Encoding.UTF8);
                        }
                    }
                    updatedProject = updatedProject.GetDocument(doc.Id).WithSyntaxRoot(rewritten).Project;

                }
                project = updatedProject;
                compilation = project.GetCompilationAsync().Result;
                hasErrs = false;
                foreach (var item in compilation.GetDiagnostics())
                {
                    PrintDiagnostic(item);
                    if (item.Severity == DiagnosticSeverity.Error)
                    {
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
            string outputPath = project.OutputFilePath.Replace("\\", "/");
            var objpath = outputPath.Replace("/bin/", "/obj/");
            if (ForProjBuild) outputPath = objpath;

            var ns = XNamespace.Get("http://schemas.microsoft.com/developer/msbuild/2003");
            var xml = XDocument.Load(project.FilePath);
            var hasResources = xml
                .DescendantNodes()
                .OfType<XElement>()
                .Where(x => x.Name == ns + "EmbeddedResource" || x.Name == ns + "Resource")
                .Any();
            if (hasResources)
            {
                foreach (var resource in Directory.GetFiles(Path.GetDirectoryName(objpath), "*.resources"))
                {
                    File.Delete(resource);
                }

                var args = new object[] { project.FilePath, new Shaman.Runtime.ProcessUtils.RawCommandLineArgument("/p:Configuration=Release") };
                try
                {
                    ProcessUtils.RunPassThrough("msbuild", args);
                }
                catch (Exception ex) when (!(ex is ProcessException))
                {
                    ProcessUtils.RunPassThrough("xbuild", args);
                }
            }
            var resources = hasResources ? Directory.EnumerateFiles(Path.GetDirectoryName(objpath), "*.resources")
                .Select(x =>
                {
                    return new ResourceDescription(Path.GetFileName(x), () => File.OpenRead(x), true);
                }).ToList() : Enumerable.Empty<ResourceDescription>();
            /*
            var resources = XDocument.Load(project.FilePath)
                .DescendantNodes()
                .OfType<XElement>().Where(x => x.Name == ns + "EmbeddedResource")
                .Select(x => x.Attribute(ns + "Include"))
                .Select(x => Path.Combine(Path.GetDirectoryName(project.FilePath), x.Value))
                .Select(x =>
                {
                    var rd = new ResourceDescription();
                }).ToList();
            */

            compilation.Emit(outputPath, manifestResources: resources);



            //compilation.Emit(@"C:\temp\roslynrewrite\" + project.AssemblyName + ".dll");
            if (hasErrs) throw new ExitException(1);
        }
    }
}
