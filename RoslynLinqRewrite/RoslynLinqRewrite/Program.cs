using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RoslynLinqRewrite
{
    class Program
    {
        static int Main(string[] args)
        {
            try
            {
                var norewrite = args.Contains("--norewrite");
                var posargs = args.Where(x => !x.StartsWith("-")).ToList();
                if (posargs.Count >= 1)
                {
                    CompileSolution(posargs.First(), posargs.ElementAtOrDefault(1), false, !norewrite);
                }
                return 0;
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
                CompileSolution(@"C:\Repositories\Awdee\Shaman.ApiServer.sln", "Shaman.Inferring.FullLogic", true);
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




            var workspace = new AdhocWorkspace();
            var proj = workspace.AddProject("LinqRewriteExample", "C#").WithMetadataReferences(
                new[] {
                MetadataReference.CreateFromFile(typeof(int).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location),
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
        }

        private static void PrintDiagnostic(Diagnostic item)
        {
            if (item.Severity == DiagnosticSeverity.Hidden) return;
            if (item.Severity == DiagnosticSeverity.Error) Console.ForegroundColor = ConsoleColor.Red;
            if (item.Severity == DiagnosticSeverity.Warning) Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(item);
            Console.ResetColor();
        }

        private static void CompileSolution(string path, string projectName, bool writeFiles, bool enable = true)
        {
            var properties = new Dictionary<string, string>();
            properties.Add("Configuration", "Release");
            var workspace = MSBuildWorkspace.Create(properties);
            Solution solution = null;
            if (".csproj".Equals(Path.GetExtension(path), StringComparison.OrdinalIgnoreCase))
            {
                CompileProject(workspace.OpenProjectAsync(path).Result, writeFiles, enable);
            }
            else
            {
                solution = workspace.OpenSolutionAsync(path).Result;
                if (projectName != null)
                {
                    CompileProject(solution.Projects.Single(x => x.Name == projectName), writeFiles, enable);
                }
                else
                {
                    foreach (var project in solution.Projects)
                    {
                        CompileProject(project, writeFiles, enable);
                    }
                }
            }

            
            
        }

        private static void CompileProject(Project project, bool writeFiles, bool enable = true)
        {
            var compilation = project.GetCompilationAsync().Result;

            var hasErrs = false;
            foreach (var item in compilation.GetDiagnostics())
            {
                PrintDiagnostic(item);
                if (item.Severity == DiagnosticSeverity.Error) hasErrs = true;
            }

            if (hasErrs) Environment.Exit(1);
            var updatedProject = project;
            if (enable)
            {
                foreach (var doc in project.Documents)
                {
                    Console.WriteLine(doc.FilePath);
                    var syntaxTree = doc.GetSyntaxTreeAsync().Result;

                    var rewriter = new LinqRewriter(project, compilation.GetSemanticModel(syntaxTree), doc.Id);

                    var rewritten = rewriter.Visit(syntaxTree.GetRoot()).NormalizeWhitespace();
                    if (writeFiles)
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
            compilation.Emit(@"C:\temp\roslynrewrite\" + project.AssemblyName + ".dll");
            if (hasErrs) Environment.Exit(1);
        }
    }
}
