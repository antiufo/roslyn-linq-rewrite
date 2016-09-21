using System.IO;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Microsoft.CodeAnalysis.CommandLine
{
	internal delegate int CompileFunc2(string[] arguments, object buildPaths, TextWriter textWriter, IAnalyzerAssemblyLoader analyzerAssemblyLoader);
}
