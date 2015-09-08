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

namespace RoslynLinqRewrite
{
    class Program
    {
        private static bool NoRewrite;
        private static bool ForProjBuild;
        private static bool WriteFiles;
        
        static int Main(string[] args)
        {
            try
            {
                // how do dnu commands installs really work?
                if (args.FirstOrDefault() == "RoslynLinqRewrite") args = args.Skip(1).ToArray();

                NoRewrite = args.Contains("--norewrite");
                ForProjBuild = args.Contains("--projbuild");
                var posargs = args.Where(x => !x.StartsWith("-")).ToList();
                if (posargs.Count >= 1)
                {
                    WriteFiles = false;
                    CompileSolution(posargs.First(), posargs.ElementAtOrDefault(1));
                }
                return 0;
            }
            catch (ExitException ex)
            {
                return ex.Code;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
                return 1;
            }



            if (!args.Contains("--VisualStudio"))
            {
                Console.WriteLine("LinqRewrite <path-to-sln> [project-name]");
                return 1;
            }
            if (true)
            {
                //CompileSolution(@"D:\Repositories\shaman-fizzler\Fizzler.sln", "Fizzler", false);
                //CompileSolution(@"C:\Repositories\Awdee\Shaman.ApiServer.sln", "Shaman.Core", true);
                CompileSolution(@"C:\Repositories\Awdee\Shaman.ApiServer.sln", "Shaman.Inferring.FullLogic");
                return 0;
            }



            var code = @"
using System;
using System.Collections.Generic;
using System.Linq;

static class Meow
{
    static void Main()
    {
var sdfsdf = (new Exception()).RecursiveEnumeration(x => x.InnerException).Last();
        var arr = new []{ 5, 457, 7464, 66 };
        var arr2 = new []{ ""a"", ""b"" };
        var capture = 5;
        var meow = 2;
var k = arr2.Where(x => x.StartsWith(""t"")).Select(x=>x==""miao"").LastOrDefault();
         //var k = arr.Where(x =>x > capture).Where(x =>x != 0).Select(x =>{return (double)x - 4;}).Where(x => x < 99).Any(x => x == 100);
       // var ka = arr.Sum();
    }
    public static IEnumerable<T> RecursiveEnumeration<T>(this T first, Func<T, T> parent)
        {
            var current = first;
            while (current != null)
            {
                yield return current;
                current = parent(current);
            }
        }
}
";
            //var syntaxTree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(code);


#if !CORECLR

            var workspace = new AdhocWorkspace();
            var proj = workspace.AddProject("LinqRewriteExample", "C#").WithMetadataReferences(
                new[] {
                MetadataReference.CreateFromFile(typeof(int).GetTypeInfo().Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).GetTypeInfo().Assembly.Location),
                }
                ).WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var doc = proj.AddDocument("source.cs", code);
            if (!workspace.TryApplyChanges(doc.Project.Solution)) throw new Exception();
            proj = doc.Project;
            var comp = proj.GetCompilationAsync().Result;

            var hasErrs = false;
            foreach (var item in comp.GetDiagnostics())
            {
                if (item.Severity == DiagnosticSeverity.Error) hasErrs = true;
                PrintDiagnostic(item);
            }

            if (hasErrs) return 1;

            var syntaxTree = doc.GetSyntaxTreeAsync().Result;
            var rewriter = new LinqRewriter(proj, comp.GetSemanticModel(syntaxTree), doc.Id);
            var rewritten = rewriter.Visit(syntaxTree.GetRoot());
            proj = doc.WithSyntaxRoot(rewritten).Project;


            foreach (var item in proj.GetCompilationAsync().Result.GetDiagnostics())
            {
                PrintDiagnostic(item);
            }
#endif
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

        private static void CompileProject(Project project)
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

                    var rewriter = new LinqRewriter(project, compilation.GetSemanticModel(syntaxTree), doc.Id);

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
                
                var args =new object[] { project.FilePath, new Shaman.Runtime.ProcessUtils.RawCommandLineArgument("/p:Configuration=Release") };
                try
                {
                    ProcessUtils.RunPassThrough("msbuild", args);
                }
                catch (Exception ex) when(!(ex is ProcessException))
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
