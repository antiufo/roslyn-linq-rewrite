using Microsoft.CodeAnalysis;
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
using System.Linq;

class Meow
{
    static int Meeeow;
    static void Main()
    {
        var arr = new []{ 5, 457, 7464, 66 };
        var capture = 5;
        var lol = 3;
        //const int c = 2;
        var k = arr.Any(x =>{return x > capture++ + Meeeow-- + lol;});
    }
}
";
            var syntaxTree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(code);


            var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("Example", new[] { syntaxTree }, new[] {
                MetadataReference.CreateFromFile(typeof(int).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location),
            });
            foreach (var item in compilation.GetDiagnostics())
            {
                Console.WriteLine(item);
            }
            
            var rewriter = new LinqRewriter(compilation.GetSemanticModel(syntaxTree, false));
            var rewritten = rewriter.Visit(syntaxTree.GetRoot());

            Console.WriteLine(rewritten.ToString());

            compilation = compilation.ReplaceSyntaxTree(syntaxTree, rewritten.SyntaxTree);
            foreach (var item in compilation.GetDiagnostics())
            {
                Console.WriteLine(item);
            }
        }
    }
}
