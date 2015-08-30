using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
        static void Main(string[] args)
        {
            if (true)
            {
                //CompileSolution(@"D:\Repositories\shaman-fizzler\Fizzler.sln", "Fizzler", false);
                CompileSolution(@"C:\Repositories\Awdee\Shaman.ApiServer.sln", "Shaman.Core", true);
                //CompileSolution(@"C:\Repositories\Awdee\Shaman.ApiServer.sln", "Shaman.Inferring.FullLogic", true);
                return;
            }



            var code = @"
using System;
using System.Collections.Generic;
using System.Linq;
using Shaman;

class Meow
{
    static void Main()
    {
        var arr = new []{ 5, 457, 7464, 66 };
        var arr2 = new []{ ""a"", ""b"" };
        var capture = 5;
        var meow = 2;
var k = arr2.Where(x => x.StartsWith(""t"")).Select(x=>x==""miao"").LastOrDefault();
         //var k = arr.Where(x =>x > capture).Where(x =>x != 0).Select(x =>{return (double)x - 4;}).Where(x => x < 99).Any(x => x == 100);
       // var ka = arr.Sum();
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

            foreach (var item in comp.GetDiagnostics())
            {
                Console.WriteLine(item);
            }
            var syntaxTree = doc.GetSyntaxTreeAsync().Result;
            var rewriter = new LinqRewriter(proj, comp.GetSemanticModel(syntaxTree), doc.Id);
            var rewritten = rewriter.Visit(syntaxTree.GetRoot());
            proj = doc.WithSyntaxRoot(rewritten).Project;

            Console.WriteLine(rewritten.ToString());

            foreach (var item in proj.GetCompilationAsync().Result.GetDiagnostics())
            {
                Console.WriteLine(item);
            }
        }

        private static void CompileSolution(string path, string projectName, bool writeFiles)
        {
            var workspace = MSBuildWorkspace.Create();
            Solution solution = null;
            if (".csproj".Equals(Path.GetExtension(path), StringComparison.OrdinalIgnoreCase))
            {
                solution = workspace.OpenSolutionAsync(path).Result;
            }
            else
            {
                solution = workspace.OpenSolutionAsync(path).Result;
            }
            var project = solution.Projects.First(x => x.Name == projectName);

            var comp = project.GetCompilationAsync().Result;

            foreach (var item in comp.GetDiagnostics())
            {
                if (item.Severity != DiagnosticSeverity.Hidden)
                    Console.WriteLine(item);
            }
            
            var updatedProject = project;
            foreach (var doc in project.Documents.Where(x=>x.Name=="RestRequestHandler.cs"))
            {
                Console.WriteLine(doc.FilePath);
                var syntaxTree = doc.GetSyntaxTreeAsync().Result;

                var rewriter = new LinqRewriter(project, comp.GetSemanticModel(syntaxTree), doc.Id);

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
            var compilation = project.GetCompilationAsync().Result;
            foreach (var item in compilation.GetDiagnostics())
            {
                if (item.Severity != DiagnosticSeverity.Hidden)
                    Console.WriteLine(item);
            }
            compilation.Emit(@"C:\temp\roslynrewrite\" + project.AssemblyName + ".dll");
        }
    }
}
