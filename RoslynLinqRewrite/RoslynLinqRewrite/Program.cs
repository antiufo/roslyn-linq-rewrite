using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RoslynLinqRewrite
{
    class Program
    {
        static void Main(string[] args)
        {
            var code = @"
using System;
using System.Linq;

class Meow
{
    static void Main()
    {
        var arr = new []{ 5, 457, 7464, 66 };
        var capture = 5;
        var meow = 2;
//arr.Any(x=>x>50);
         var k = arr/*.Where(x =>x > capture).Where(x =>x != 0)*/.Select(x =>{ return x * 3;}).Select(x => x - 4).Sum();
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
            var rewriter = new LinqRewriter(proj, comp.GetSemanticModel(syntaxTree));
            var rewritten = rewriter.Visit(syntaxTree.GetRoot());
            proj = doc.WithSyntaxRoot(rewritten).Project;
           // if (!workspace.TryApplyChanges(proj.Solution))
          //      throw new Exception();

            Console.WriteLine(rewritten.ToString());
            
            foreach (var item in proj.GetCompilationAsync().Result.GetDiagnostics())
            {
                Console.WriteLine(item);
            }
        }
    }
}
