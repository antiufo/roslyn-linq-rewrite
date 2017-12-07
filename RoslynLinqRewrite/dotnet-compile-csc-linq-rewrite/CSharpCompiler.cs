// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Utilities;
using Shaman.Runtime;
using Shaman.Runtime.ReflectionExtensions;
using CommonMessageProvider = System.Object;
using ErrorLogger2 = System.Object;
using TouchedFileLogger = System.Object;
using DiagnosticInfo = System.Object;
using IVsSqmMulti = System.Object;
using CommandLineDiagnosticFormatter = System.Object;
using System.Globalization;

namespace Microsoft.CodeAnalysis.CSharp
{
    internal abstract class CSharpCompiler : CommonCompiler
    {
        internal const string ResponseFileName = "csc.rsp";

        private readonly CSharpDiagnosticFormatter _diagnosticFormatter;

        protected CSharpCompiler(CSharpCommandLineParser parser, string responseFile, string[] args, string clientDirectory, string baseDirectory, string sdkDirectoryOpt, string additionalReferenceDirectories, IAnalyzerAssemblyLoader analyzerLoader)
            : base(parser, responseFile, args, clientDirectory, baseDirectory, sdkDirectoryOpt, additionalReferenceDirectories, analyzerLoader)
        {
            _diagnosticFormatter = ReflCommandLineDiagnosticFormatter.ctor(baseDirectory, Arguments.PrintFullPaths, ReflCSharpCommandLineArguments.get_ShouldIncludeErrorEndLocation(Arguments));
        }

        public override DiagnosticFormatter DiagnosticFormatter { get { return _diagnosticFormatter; } }
        protected internal new CSharpCommandLineArguments Arguments { get { return (CSharpCommandLineArguments)base.Arguments; } }

        public override Compilation CreateCompilation(TextWriter consoleOutput, TouchedFileLogger touchedFilesLogger, ErrorLogger2 errorLogger)
        {
            var parseOptions = Arguments.ParseOptions;

            // We compute script parse options once so we don't have to do it repeatedly in
            // case there are many script files.
            var scriptParseOptions = parseOptions.WithKind(SourceCodeKind.Script);

            bool hadErrors = false;

            var sourceFiles = Arguments.SourceFiles;
            var trees = new SyntaxTree[sourceFiles.Length];
            var normalizedFilePaths = new String[sourceFiles.Length];

            for (int i = 0; i < sourceFiles.Length; i++)
            {
                //NOTE: order of trees is important!!
                trees[i] = ParseFile(consoleOutput, parseOptions, scriptParseOptions, ref hadErrors, sourceFiles[i], errorLogger, out normalizedFilePaths[i]);
            }
            

            // If errors had been reported in ParseFile, while trying to read files, then we should simply exit.
            if (hadErrors)
            {
                return null;
            }

            var diagnostics = typeof(List<>).MakeGenericTypeFast(Refl.Type_DiagnosticInfo).InvokeFunction(".ctor");

            var uniqueFilePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < sourceFiles.Length; i++)
            {
                var normalizedFilePath = normalizedFilePaths[i];
                Debug.Assert(normalizedFilePath != null);
                Debug.Assert(ReflPathUtilities.IsAbsolute(normalizedFilePath));

                if (!uniqueFilePaths.Add(normalizedFilePath))
                {
                    // warning CS2002: Source file '{0}' specified multiple times
                    diagnostics.InvokeAction("Add", ReflDiagnosticInfo.ctor(MessageProvider, (int)Compatibility.WRN_FileAlreadyIncluded,
                        new object[] { Arguments.PrintFullPaths ? normalizedFilePath : ReflCommandLineDiagnosticFormatter.RelativizeNormalizedPath(_diagnosticFormatter, normalizedFilePath)}));

                    trees[i] = null;
                }
            }

            if (Arguments.TouchedFilesPath != null)
            {
                foreach (var path in uniqueFilePaths)
                {
                    ReflTouchedFileLogger.AddRead(touchedFilesLogger, path);
                }
            }

            var assemblyIdentityComparer = DesktopAssemblyIdentityComparer.Default;
            var appConfigPath = this.Arguments.AppConfigPath;
            if (appConfigPath != null)
            {
                try
                {
                    using (var appConfigStream = new FileStream(appConfigPath, FileMode.Open, FileAccess.Read))
                    {
                        assemblyIdentityComparer = DesktopAssemblyIdentityComparer.LoadFromXml(appConfigStream);
                    }

                    if (touchedFilesLogger != null)
                    {
                        ReflTouchedFileLogger.AddRead(touchedFilesLogger, appConfigPath);
                    }
                }
                catch (Exception ex)
                {
                    diagnostics.InvokeAction("Add", ReflDiagnosticInfo.ctor(MessageProvider, (int)Compatibility.ERR_CantReadConfigFile, new object[]{ appConfigPath, ex.Message}));
                }
            }

            var xmlFileResolver = ReflLoggingXmlFileResolver.ctor(Arguments.BaseDirectory, touchedFilesLogger);
            var sourceFileResolver = ReflLoggingSourceFileResolver.ctor(ImmutableArray<string>.Empty, Arguments.BaseDirectory, Arguments.PathMap, touchedFilesLogger);

            MetadataReferenceResolver referenceDirectiveResolver;
            var resolvedReferences = ResolveMetadataReferences(diagnostics, touchedFilesLogger, out referenceDirectiveResolver);
            if (ReportErrors((IEnumerable<DiagnosticInfo>)diagnostics, consoleOutput, errorLogger))
            {
                return null;
            }

            var strongNameProvider = ReflLoggingStrongNameProvider.ctor(Arguments.KeyFileSearchPaths, touchedFilesLogger, null);

            var compilation = CSharpCompilation.Create(
                Arguments.CompilationName,
                trees.Where(x => x != null),
                resolvedReferences,
                Arguments.CompilationOptions.
                    WithMetadataReferenceResolver(referenceDirectiveResolver).
                    WithAssemblyIdentityComparer(assemblyIdentityComparer).
                    WithStrongNameProvider(strongNameProvider).
                    WithXmlReferenceResolver(xmlFileResolver).
                    WithSourceReferenceResolver(sourceFileResolver));

            return compilation;
        }

        private SyntaxTree ParseFile(
            TextWriter consoleOutput,
            CSharpParseOptions parseOptions,
            CSharpParseOptions scriptParseOptions,
            ref bool hadErrors,
            CommandLineSourceFile file,
            ErrorLogger2 errorLogger,
            out string normalizedFilePath)
        {
            var fileReadDiagnostics = new List<DiagnosticInfo>();
            var content = ReadFileContent(file, fileReadDiagnostics, out normalizedFilePath);

            if (content == null)
            {
                ReportErrors(fileReadDiagnostics, consoleOutput, errorLogger);
                fileReadDiagnostics.Clear();
                hadErrors = true;
                return null;
            }
            else
            {
                return ParseFile(parseOptions, scriptParseOptions, content, file);
            }
        }

        private static SyntaxTree ParseFile(
            CSharpParseOptions parseOptions,
            CSharpParseOptions scriptParseOptions,
            SourceText content,
            CommandLineSourceFile file)
        {
            var tree = SyntaxFactory.ParseSyntaxTree(
                content,
                file.IsScript ? scriptParseOptions : parseOptions,
                file.Path);

            // prepopulate line tables.
            // we will need line tables anyways and it is better to not wait until we are in emit
            // where things run sequentially.
            bool isHiddenDummy;
            ReflSyntaxTree.GetMappedLineSpanAndVisibility(tree, default(TextSpan), out isHiddenDummy);

            return tree;
        }
        
        /// <summary>
        /// Given a compilation and a destination directory, determine three names:
        ///   1) The name with which the assembly should be output.
        ///   2) The path of the assembly/module file.
        ///   3) The path of the pdb file.
        /// 
        /// When csc produces an executable, but the name of the resulting assembly
        /// is not specified using the "/out" switch, the name is taken from the name
        /// of the file (note: file, not class) containing the assembly entrypoint
        /// (as determined by binding and the "/main" switch).
        /// 
        /// For example, if the command is "csc /target:exe a.cs b.cs" and b.cs contains the
        /// entrypoint, then csc will produce "b.exe" and "b.pdb" in the output directory,
        /// with assembly name "b" and module name "b.exe" embedded in the file.
        /// </summary>
        protected override string GetOutputFileName(Compilation compilation, CancellationToken cancellationToken)
        {
            if (Arguments.OutputFileName == null)
            {
                Debug.Assert(
                    Arguments.CompilationOptions.OutputKind == OutputKind.ConsoleApplication ||
                    Arguments.CompilationOptions.OutputKind == OutputKind.WindowsApplication ||
                    Arguments.CompilationOptions.OutputKind == OutputKind.WindowsRuntimeApplication);

                var comp = (CSharpCompilation)compilation;

                ISymbol entryPoint = comp.ScriptClass;
                if ((object)entryPoint == null)
                {
                    var method = comp.GetEntryPoint(cancellationToken);
                    if ((object)method != null)
                    {
                        entryPoint = method.PartialImplementationPart ?? method;
                    }
                }

                if ((object)entryPoint != null)
                {
                    var syntaxTree = entryPoint.Locations.First().SourceTree;
                    var location = syntaxTree.FilePath;
                    if (location == null) OriginalPaths.TryGetValue(syntaxTree, out location);
                    string entryPointFileName = ReflPathUtilities.GetFileName(location, true);
                    return Path.ChangeExtension(entryPointFileName, ".exe");
                }
                else
                {
                    // no entrypoint found - an error will be reported and the compilation won't be emitted
                    return "error";
                }
            }
            else
            {
                return base.GetOutputFileName(compilation, cancellationToken);
            }
        }

        internal override bool SuppressDefaultResponseFile(IEnumerable<string> args)
        {
            return args.Any(arg => new[] { "/noconfig", "-noconfig" }.Contains(arg.ToLowerInvariant()));
        }

        /// <summary>
        /// Print compiler logo
        /// </summary>
        /// <param name="consoleOutput"></param>
        public override void PrintLogo(TextWriter consoleOutput)
        {
            
            consoleOutput.WriteLine(ErrorFacts_GetMessage(MessageID.IDS_LogoLine1, Culture), GetToolName(), GetAssemblyFileVersion());
            consoleOutput.WriteLine(ErrorFacts_GetMessage(MessageID.IDS_LogoLine2, Culture), Culture);
            consoleOutput.WriteLine("LINQ Rewriter version");
            consoleOutput.WriteLine();
        }

        internal override string GetToolName()
        {
            return ErrorFacts_GetMessage(MessageID.IDS_ToolName, Culture);
        }

        public static string ErrorFacts_GetMessage(MessageID messageId, CultureInfo culture)
        {
            var func = Refl.Type_ErrorFacts.GetMethods().Single(x => x.Name == "GetMessage" && x.GetParameters().Length == 2 && x.GetParameters()[0].ParameterType == Refl.Type_MessageId);
            return (string)func.Invoke(null, new object[] { Enum.ToObject(Refl.Type_MessageId, (int)messageId), culture });
        }

        /// <summary>
        /// Print Commandline help message (up to 80 English characters per line)
        /// </summary>
        /// <param name="consoleOutput"></param>
        public override void PrintHelp(TextWriter consoleOutput)
        {
            consoleOutput.WriteLine(ErrorFacts_GetMessage(MessageID.IDS_CSCHelp, Culture));
        }

        protected override bool TryGetCompilerDiagnosticCode(string diagnosticId, out uint code)
        {
            return CommonCompiler.TryGetCompilerDiagnosticCode(diagnosticId, "CS", out code);
        }

        protected override ImmutableArray<DiagnosticAnalyzer> ResolveAnalyzersAndGeneratorsFromArguments(object diagnostics, CommonMessageProvider messageProvider, TouchedFileLogger touchedFiles)
        {
            var func = Arguments.GetType().GetMethod("ResolveAnalyzersFromArguments", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var arr = new object[4];
            arr[0] = LanguageNames.CSharp;
            arr[1] = diagnostics;
            arr[2] = MessageProvider;
            // TODO What happened to parameter touchedFiles?
            arr[3] = AnalyzerLoader;
            return (ImmutableArray<DiagnosticAnalyzer>)func.Invoke(Arguments, arr);
            //return arr[4];
        }
    }
}
