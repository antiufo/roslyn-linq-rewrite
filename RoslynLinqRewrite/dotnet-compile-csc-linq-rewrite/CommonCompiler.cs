// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using Shaman.Runtime;
using Shaman.Runtime.ReflectionExtensions;
using Roslyn.Utilities;
using Microsoft.VisualStudio.Shell.Interop;
using CommonMessageProvider = System.Object;
using ErrorLogger2 = System.Object;
using TouchedFileLogger = System.Object;
using DiagnosticInfo = System.Object;
using IVsSqmMulti = System.Object;
using AdditionalTextFile = System.Object;
using AnalyzerManager = System.Object;
using AnalyzerDriver = System.Object;
using Microsoft.CodeAnalysis.CommandLine;
using Shaman.Roslyn.LinqRewrite;
using System.Collections;

namespace Microsoft.CodeAnalysis
{
    /// <summary>
    /// Base class for csc.exe, csi.exe, vbc.exe and vbi.exe implementations.
    /// </summary>
    internal abstract partial class CommonCompiler
    {
        internal const int Failed = 1;
        internal const int Succeeded = 0;

        private readonly string _clientDirectory;

        public CommonMessageProvider MessageProvider { get; }
        public CommandLineArguments Arguments { get; }
        public IAnalyzerAssemblyLoader AnalyzerLoader { get; private set; }
        public abstract DiagnosticFormatter DiagnosticFormatter { get; }
        private readonly HashSet<Diagnostic> _reportedDiagnostics = new HashSet<Diagnostic>();

        public abstract Compilation CreateCompilation(TextWriter consoleOutput, TouchedFileLogger touchedFilesLogger, ErrorLogger2 errorLoggerOpt);
        public abstract void PrintLogo(TextWriter consoleOutput);
        public abstract void PrintHelp(TextWriter consoleOutput);
        internal abstract string GetToolName();

        protected abstract uint GetSqmAppID();
        protected abstract bool TryGetCompilerDiagnosticCode(string diagnosticId, out uint code);
        protected abstract void CompilerSpecificSqm(IVsSqmMulti sqm, uint sqmSession);
        protected abstract ImmutableArray<DiagnosticAnalyzer> ResolveAnalyzersAndGeneratorsFromArguments(object diagnostics, CommonMessageProvider messageProvider, TouchedFileLogger touchedFiles);

        public CommonCompiler(CommandLineParser parser, string responseFile, string[] args, string clientDirectory, string baseDirectory, string sdkDirectoryOpt, string additionalReferenceDirectories, IAnalyzerAssemblyLoader analyzerLoader)
        {
            IEnumerable<string> allArgs = args;
            _clientDirectory = clientDirectory;

            Debug.Assert(null == responseFile || ReflPathUtilities.IsAbsolute(responseFile));
            if (!SuppressDefaultResponseFile(args) && File.Exists(responseFile))
            {
                allArgs = new[] { "@" + responseFile }.Concat(allArgs);
            }

            this.Arguments = parser.Parse(allArgs, baseDirectory, sdkDirectoryOpt, additionalReferenceDirectories);
            this.MessageProvider = ReflCommandLineParser.get_MessageProvider(parser);
            this.AnalyzerLoader = analyzerLoader;

            if (Arguments.ParseOptions.Features.ContainsKey("debug-determinism"))
            {
                EmitDeterminismKey(Arguments, args, baseDirectory, parser);
            }
        }

        internal abstract bool SuppressDefaultResponseFile(IEnumerable<string> args);

        internal string GetAssemblyFileVersion()
        {
            if (_clientDirectory != null)
            {
                return FileVersionInfo.GetVersionInfo(Refl.Assembly_Common.Location).FileVersion;
            }

            return "";
        }

        internal Version GetAssemblyVersion()
        {
            return typeof(CommonCompiler).GetTypeInfo().Assembly.GetName().Version;
        }

        internal string GetCultureName()
        {
            return Culture.Name;
        }

        internal virtual Func<string, MetadataReferenceProperties, PortableExecutableReference> GetMetadataProvider()
        {
            return (path, properties) => MetadataReference.CreateFromFile(path, properties);
        }

        internal virtual MetadataReferenceResolver GetCommandLineMetadataReferenceResolver(TouchedFileLogger loggerOpt)
        {
            var pathResolver = ReflRelativePathResolver.ctor(Arguments.ReferencePaths, Arguments.BaseDirectory);
            return ReflLoggingMetadataFileReferenceResolver.ctor(pathResolver, GetMetadataProvider(), loggerOpt);
        }

        /// <summary>
        /// Resolves metadata references stored in command line arguments and reports errors for those that can't be resolved.
        /// </summary>
        internal List<MetadataReference> ResolveMetadataReferences(
            object diagnostics,
            TouchedFileLogger touchedFiles,
            out MetadataReferenceResolver referenceDirectiveResolver)
        {
            var commandLineReferenceResolver = GetCommandLineMetadataReferenceResolver(touchedFiles);

            List<MetadataReference> resolved = new List<MetadataReference>();
            ReflCommandLineArguments.ResolveMetadataReferences(Arguments, commandLineReferenceResolver, diagnostics, this.MessageProvider, resolved);

            //if (Arguments.IsScriptRunner)
            //{
            //    referenceDirectiveResolver = commandLineReferenceResolver;
            //}
            //else
            {
                // when compiling into an assembly (csc/vbc) we only allow #r that match references given on command line:
                referenceDirectiveResolver = ReflExistingReferencesResolver.ctor(commandLineReferenceResolver, resolved.ToImmutableArray());
            }

            return resolved;
        }

        /// <summary>
        /// Reads content of a source file.
        /// </summary>
        /// <param name="file">Source file information.</param>
        /// <param name="diagnostics">Storage for diagnostics.</param>
        /// <returns>File content or null on failure.</returns>
        internal SourceText ReadFileContent(CommandLineSourceFile file, IList<DiagnosticInfo> diagnostics)
        {
            string discarded;
            return ReadFileContent(file, diagnostics, out discarded);
        }

        /// <summary>
        /// Reads content of a source file.
        /// </summary>
        /// <param name="file">Source file information.</param>
        /// <param name="diagnostics">Storage for diagnostics.</param>
        /// <param name="normalizedFilePath">If given <paramref name="file"/> opens successfully, set to normalized absolute path of the file, null otherwise.</param>
        /// <returns>File content or null on failure.</returns>
        internal SourceText ReadFileContent(CommandLineSourceFile file, IList<DiagnosticInfo> diagnostics, out string normalizedFilePath)
        {
            var filePath = file.Path;
            try
            {
                // PERF: Using a very small buffer size for the FileStream opens up an optimization within EncodedStringText where
                // we read the entire FileStream into a byte array in one shot. For files that are actually smaller than the buffer
                // size, FileStream.Read still allocates the internal buffer.
                using (var data = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 1, options: FileOptions.None))
                {
                    normalizedFilePath = data.Name;
                    return ReflEncodedStringText.Create(data, Arguments.Encoding, Arguments.ChecksumAlgorithm);
                }
            }
            catch (Exception e)
            {
                diagnostics.Add(ToFileReadDiagnostics(this.MessageProvider, e, filePath));
                normalizedFilePath = null;
                return null;
            }
        }

        internal static DiagnosticInfo ToFileReadDiagnostics(CommonMessageProvider messageProvider, Exception e, string filePath)
        {
            DiagnosticInfo diagnosticInfo;

            if (e is FileNotFoundException || e.GetType().Name == "DirectoryNotFoundException")
            {
                diagnosticInfo = ReflDiagnosticInfo.ctor(messageProvider, messageProvider.GetFieldOrProperty<int>("ERR_FileNotFound"), new[]{filePath});
            }
            else if (e is InvalidDataException)
            {
                diagnosticInfo = ReflDiagnosticInfo.ctor(messageProvider, messageProvider.GetFieldOrProperty<int>("ERR_BinaryFile"), new[]{ filePath});
            }
            else
            {
                diagnosticInfo = ReflDiagnosticInfo.ctor(messageProvider, messageProvider.GetFieldOrProperty<int>("ERR_NoSourceFile"), new[]{filePath, e.Message});
            }

            return diagnosticInfo;
        }

        public bool ReportErrors(IEnumerable<Diagnostic> diagnostics, TextWriter consoleOutput, ErrorLogger2 errorLoggerOpt)
        {
            bool hasErrors = false;
            foreach (var diag in diagnostics)
            {
                if (_reportedDiagnostics.Contains(diag))
                {
                    // TODO: This invariant fails (at least) in the case where we see a member declaration "x = 1;".
                    // First we attempt to parse a member declaration starting at "x".  When we see the "=", we
                    // create an IncompleteMemberSyntax with return type "x" and an error at the location of the "x".
                    // Then we parse a member declaration starting at "=".  This is an invalid member declaration start
                    // so we attach an error to the "=" and attach it (plus following tokens) to the IncompleteMemberSyntax
                    // we previously created.
                    //this assert isn't valid if we change the design to not bail out after each phase.
                    //System.Diagnostics.Debug.Assert(diag.Severity != DiagnosticSeverity.Error);
                    continue;
                }
                else if (diag.Severity == DiagnosticSeverity.Hidden)
                {
                    // Not reported from the command-line compiler.
                    continue;
                }

                // We want to report diagnostics with source suppression in the error log file.
                // However, these diagnostics should not be reported on the console output.
                errorLoggerOpt?.InvokeAction("LogDiagnostic", diag);
                if (diag.IsSuppressed)
                {
                    continue;
                }

                consoleOutput.WriteLine(DiagnosticFormatter.Format(diag));

                if (diag.Severity == DiagnosticSeverity.Error)
                {
                    hasErrors = true;
                }

                _reportedDiagnostics.Add(diag);
            }

            return hasErrors;
        }

        public bool ReportErrors(IEnumerable<DiagnosticInfo> diagnostics, TextWriter consoleOutput, ErrorLogger2 errorLoggerOpt)
        {
            bool hasErrors = false;
            if (diagnostics != null && diagnostics.Any())
            {
                foreach (var diagnostic in diagnostics)
                {
                    if (diagnostic.GetFieldOrProperty<DiagnosticSeverity>("Severity") == DiagnosticSeverity.Hidden)
                    {
                        // Not reported from the command-line compiler.
                        continue;
                    }

                    PrintError(diagnostic, consoleOutput);
                    errorLoggerOpt?.InvokeAction("LogDiagnostic", typeof(Diagnostic).InvokeFunction("Create", diagnostic));

                    if (diagnostic.GetFieldOrProperty<DiagnosticSeverity>("Severity") == DiagnosticSeverity.Error)
                    {
                        hasErrors = true;
                    }
                }
            }

            return hasErrors;
        }

        protected virtual void PrintError(DiagnosticInfo diagnostic, TextWriter consoleOutput)
        {
            consoleOutput.WriteLine(ReflDiagnosticInfo.ToString(diagnostic, Culture));
        }

        public ErrorLogger2 GetErrorLogger(TextWriter consoleOutput, CancellationToken cancellationToken)
        {
            Debug.Assert(Arguments.ErrorLogPath != null);

            var errorLog = OpenFile(Arguments.ErrorLogPath, consoleOutput, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            if (errorLog == null)
            {
                return null;
            }

            return Refl.Type_ErrorLogger.InvokeFunction(".ctor", errorLog, GetToolName(), GetAssemblyFileVersion(), GetAssemblyVersion(), Culture);
        }


        /// <summary>
        /// csc.exe and vbc.exe entry point.
        /// </summary>
        public virtual int Run(TextWriter consoleOutput, CancellationToken cancellationToken = default(CancellationToken))
        {
            var saveUICulture = CultureInfo.CurrentUICulture;
            ErrorLogger2 errorLogger = null;

            try
            {
                // Messages from exceptions can be used as arguments for errors and they are often localized.
                // Ensure they are localized to the right language.
                var culture = this.Culture;
                if (culture != null)
                {
                    Compatibility.SetCurrentUICulture(culture);
                }

                if (Arguments.ErrorLogPath != null)
                {
                    errorLogger = GetErrorLogger(consoleOutput, cancellationToken);
                    if (errorLogger == null)
                    {
                        return Failed;
                    }
                }

                return RunCore(consoleOutput, errorLogger, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                var errorCode = MessageProvider.GetFieldOrProperty<int>("ERR_CompileCancelled");
                if (errorCode > 0)
                {
                    var diag = ReflDiagnosticInfo.ctor(MessageProvider, errorCode, new object[]{});
                    ReportErrors(new[] { diag }, consoleOutput, errorLogger);
                }

                return Failed;
            }
            finally
            {
                Compatibility.SetCurrentUICulture(saveUICulture);
                ((IDisposable)errorLogger)?.Dispose();
            }
        }

        internal string backup_responseFile; 
        internal BuildPaths backup_buildPaths;
        internal string[] backup_args;
        internal IAnalyzerAssemblyLoader backup_analyzerLoader;

        private int RunCore(TextWriter consoleOutput, ErrorLogger2 errorLogger, CancellationToken cancellationToken)
        {
            Debug.Assert(!Arguments.GetFieldOrProperty<bool>("IsScriptRunner"));

            cancellationToken.ThrowIfCancellationRequested();

            if (Arguments.DisplayLogo)
            {
                PrintLogo(consoleOutput);
            }

            if (Arguments.DisplayHelp)
            {
                PrintHelp(consoleOutput);
                return Succeeded;
            }

            if (ReportErrors(Arguments.Errors, consoleOutput, errorLogger))
            {
                return Failed;
            }

            var touchedFilesLogger = (Arguments.TouchedFilesPath != null) ? new TouchedFileLogger() : null;

            Compilation compilation = CreateCompilation(consoleOutput, touchedFilesLogger, errorLogger);
            if (compilation == null)
            {
                return Failed;
            }


            var diagnostics = typeof(List<>).MakeGenericTypeFast(Refl.Type_DiagnosticInfo).InvokeFunction(".ctor");
            var analyzers = ResolveAnalyzersAndGeneratorsFromArguments(diagnostics, MessageProvider, touchedFilesLogger);
            var additionalTextFiles = ResolveAdditionalFilesFromArguments(diagnostics, MessageProvider, touchedFilesLogger);
            if (ReportErrors((IEnumerable<DiagnosticInfo>)diagnostics, consoleOutput, errorLogger))
            {
                return Failed;
            }

            cancellationToken.ThrowIfCancellationRequested();

            CancellationTokenSource analyzerCts = null;
            AnalyzerManager analyzerManager = null;
            AnalyzerDriver analyzerDriver = null;
            try
            {
                Func<ImmutableArray<Diagnostic>> getAnalyzerDiagnostics = null;
                ConcurrentSet<Diagnostic> analyzerExceptionDiagnostics = null;
                if (!analyzers.IsDefaultOrEmpty)
                {
                    analyzerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    analyzerManager = new AnalyzerManager();
                    analyzerExceptionDiagnostics = new ConcurrentSet<Diagnostic>();
                    Action<Diagnostic> addExceptionDiagnostic = diagnostic => analyzerExceptionDiagnostics.Add(diagnostic);
                    var analyzerOptions = new AnalyzerOptions(ImmutableArray.CreateRange(additionalTextFiles.Select(x => (AdditionalText)x)));

                    analyzerDriver = ReflAnalyzerDriver.CreateAndAttachToCompilation(
                        compilation,
                        analyzers,
                        analyzerOptions,
                        analyzerManager,
                        addExceptionDiagnostic,
                        Arguments.ReportAnalyzer,
                        out compilation,
                        analyzerCts.Token);
                    getAnalyzerDiagnostics = () => ((Task<ImmutableArray<Diagnostic>>)analyzerDriver.InvokeFunction("GetDiagnosticsAsync", compilation)).Result;
                }

                // Print the diagnostics produced during the parsing stage and exit if there were any errors.
                if (ReportErrors(compilation.GetParseDiagnostics(), consoleOutput, errorLogger))
                {
                    return Failed;
                }

                if (ReportErrors(compilation.GetDeclarationDiagnostics(), consoleOutput, errorLogger))
                {
                    return Failed;
                }
                consoleOutput.WriteLine("Rewriting LINQ to procedural code..."); 
                var originalCompilation = compilation;
                
                var rewrittenLinqInvocations = 0;
                var rewrittenMethods = 0;
                foreach (var syntaxTree in compilation.SyntaxTrees)
                {
                    var rewriter = new LinqRewriter(originalCompilation.GetSemanticModel(syntaxTree));
                    var root = syntaxTree.GetRoot();
                    SyntaxNode rewritten;
                    try
                    {
                        rewritten = rewriter.Visit(root);
                    }
                    catch (Exception ex)
                    {
                        ReportErrors(new[] { rewriter.CreateDiagnosticForException(ex, syntaxTree.FilePath) }, consoleOutput, errorLogger);
                        return Failed;
                    }
                    
                    if (rewritten != root)
                    {
                        OriginalPaths[syntaxTree] = syntaxTree.FilePath;
                        compilation = compilation.ReplaceSyntaxTree(syntaxTree, rewritten.SyntaxTree);

                        rewrittenLinqInvocations += rewriter.RewrittenLinqQueries;
                        rewrittenMethods += rewriter.RewrittenMethods;
                    }
                }
                consoleOutput.WriteLine(string.Format("Rewritten {0} LINQ queries in {1} methods as procedural code.", rewrittenLinqInvocations, rewrittenMethods));

                var k = compilation.GetDiagnostics().Where(x => x.Severity == DiagnosticSeverity.Error).ToList();
                if (k.Count != 0)
                {
                    foreach (var err in k)
                    {
                        System.Console.WriteLine("Could not compile rewritten method. Consider applying a [Shaman.Runtime.NoLinqRewrite] attribute."); 
                        var m = err.Location.SourceTree.GetRoot().FindNode(err.Location.SourceSpan);
                        System.Console.WriteLine(err.Location.SourceTree.FilePath);
                        //var z = m.FirstAncestorOrSelf<CSharp.Syntax.BaseMethodDeclarationSyntax>(x => x.Kind() == CSharp.SyntaxKind.MethodDeclaration);
                        //compilation.GetSemanticModel().GetEnclosingSymbol(err.Location.SourceSpan.Start)

                    }
                }
                if (ReportErrors(compilation.GetParseDiagnostics().Where(x => x.Severity == DiagnosticSeverity.Error), consoleOutput, errorLogger))
                {
                    return Failed;
                }

                if (ReportErrors(compilation.GetDeclarationDiagnostics().Where(x => x.Severity == DiagnosticSeverity.Error), consoleOutput, errorLogger))
                {
                    return Failed;
                }



                EmitResult emitResult;

                // NOTE: as native compiler does, we generate the documentation file
                // NOTE: 'in place', replacing the contents of the file if it exists

                string finalPeFilePath;
                string finalPdbFilePath;
                string finalXmlFilePath;

                Stream xmlStreamOpt = null;

                cancellationToken.ThrowIfCancellationRequested();

                finalXmlFilePath = Arguments.DocumentationPath;
                if (finalXmlFilePath != null)
                {
                    xmlStreamOpt = OpenFile(finalXmlFilePath, consoleOutput, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                    if (xmlStreamOpt == null)
                    {
                        return Failed;
                    }

                    xmlStreamOpt.SetLength(0);
                }

                cancellationToken.ThrowIfCancellationRequested();
                ImmutableArray<Diagnostic> emitDiagnostics;
                IEnumerable<DiagnosticInfo> errors;
                using (var win32ResourceStreamOpt = GetWin32Resources(Arguments, compilation, out errors))
                using (xmlStreamOpt)
                {
                    if (ReportErrors(errors, consoleOutput, errorLogger))
                    {
                        return Failed;
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    string outputName = GetOutputFileName(compilation, cancellationToken);

                    finalPeFilePath = Path.Combine(Arguments.OutputDirectory, outputName);
                    finalPdbFilePath = Arguments.PdbPath ?? Path.ChangeExtension(finalPeFilePath, ".pdb");

                    // NOTE: Unlike the PDB path, the XML doc path is not embedded in the assembly, so we don't need to pass it to emit.
                    var emitOptions = Arguments.EmitOptions.
                        WithOutputNameOverride(outputName).
                        WithPdbFilePath(finalPdbFilePath);

                    // The PDB path is emitted in it's entirety into the PE.  This makes it impossible to have deterministic
                    // builds that occur in different source directories.  To enable this we shave all path information from
                    // the PDB when specified by the user.  
                    //
                    // This is a temporary work around to allow us to make progress with determinism.  The following issue 
                    // tracks getting an official solution here.
                    //
                    // https://github.com/dotnet/roslyn/issues/9813
                    if (Arguments.ParseOptions.Features.ContainsKey("pdb-path-determinism") && !string.IsNullOrEmpty(emitOptions.PdbFilePath))
                    {
                        emitOptions = emitOptions.WithPdbFilePath(Path.GetFileName(emitOptions.PdbFilePath));
                    }

                    //var dummyCompiler = Refl.Type_Csc.InvokeFunction(".ctor", backup_responseFile, (object)null, backup_args, backup_analyzerLoader);
                    
                    var constr = Refl.Type_Csc.GetConstructors(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).First(); 
                    var backup_buildPaths2 = Refl.Type_BuildPaths.InvokeFunction(".ctor", backup_buildPaths.ClientDirectory, backup_buildPaths.WorkingDirectory, backup_buildPaths.SdkDirectory);
                    var dummyCompiler = constr.Invoke(new object[]{ backup_responseFile, backup_buildPaths2, backup_args, backup_analyzerLoader});
                    //var dummyCompiler = Activator.CreateInstance(Refl.Type_Csc, BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.CreateInstance|BindingFlags.Instance, null, new object[]{ backup_responseFile, (object)null, backup_args, backup_analyzerLoader}, null);
                    using (var peStreamProvider = (IDisposable)Refl.Type_CompilerEmitStreamProvider.InvokeFunction(".ctor", dummyCompiler, finalPeFilePath))
                    using (var pdbStreamProviderOpt = Arguments.EmitPdb ? (IDisposable)Refl.Type_CompilerEmitStreamProvider.InvokeFunction(".ctor", dummyCompiler, finalPdbFilePath) : null)
                    {
                        /*
                        internal EmitResult Emit(
                            Compilation.EmitStreamProvider peStreamProvider, 
                            Compilation.EmitStreamProvider pdbStreamProvider, 
                            Compilation.EmitStreamProvider xmlDocumentationStreamProvider,
                            Compilation.EmitStreamProvider win32ResourcesStreamProvider,
                            IEnumerable<ResourceDescription> manifestResources,
                            EmitOptions options,
                            IMethodSymbol debugEntryPoint,
                            CompilationTestData testData,
                            Func<ImmutableArray<Diagnostic>> getHostDiagnostics,
                            CancellationToken cancellationToken)
                        */
                        var diagnosticBag = Refl.Type_DiagnosticBag.InvokeFunction(".ctor");
                        Exception emitException = null;
                        emitResult = null;
                        try
                        {
                            var peStream = ReflEmitStreamProvider.CreateStream(peStreamProvider, diagnosticBag);
                            var pdbStream = pdbStreamProviderOpt != null ? ReflEmitStreamProvider.CreateStream(pdbStreamProviderOpt, diagnosticBag) : null;
                            emitResult = compilation.Emit(
                                peStream,
                                pdbStream,
                                xmlStreamOpt,
                                win32ResourceStreamOpt,
                                Arguments.ManifestResources,
                                emitOptions,
                                null,
                                cancellationToken);
                        }
                        catch(Exception ex)
                        {
                            emitException = ex;
                        }
                        emitDiagnostics = (ImmutableArray<Diagnostic>)diagnosticBag.GetType().GetMethods().Single(x => x.Name == "ToReadOnly" && !x.IsGenericMethod).Invoke(diagnosticBag, Array.Empty<object>());
                        if (emitDiagnostics.Length == 0 && emitException != null) throw emitException;
                        
                        if (emitResult != null && emitResult.Success && touchedFilesLogger != null)
                        {
                            if (pdbStreamProviderOpt != null)
                            {
                                touchedFilesLogger.InvokeAction("AddWritten", finalPdbFilePath);
                            }

                            touchedFilesLogger.InvokeAction("AddWritten", finalPeFilePath);
                        }
                    }
                }


                if (ReportErrors((emitResult != null ? emitResult.Diagnostics : Enumerable.Empty<Diagnostic>()).Union(emitDiagnostics), consoleOutput, errorLogger))
                {
                    return Failed;
                }

                cancellationToken.ThrowIfCancellationRequested();

                if (analyzerExceptionDiagnostics != null && ReportErrors(analyzerExceptionDiagnostics, consoleOutput, errorLogger))
                {
                    return Failed;
                }

                bool errorsReadingAdditionalFiles = false;
                foreach (var additionalFile in additionalTextFiles)
                {
                    if (ReportErrors(additionalFile.GetFieldOrProperty<IEnumerable<Diagnostic>>("Diagnostics"), consoleOutput, errorLogger))
                    {
                        errorsReadingAdditionalFiles = true;
                    }
                }

                if (errorsReadingAdditionalFiles)
                {
                    return Failed;
                }

                cancellationToken.ThrowIfCancellationRequested();

                if (Arguments.TouchedFilesPath != null)
                {
                    Debug.Assert(touchedFilesLogger != null);

                    if (finalXmlFilePath != null)
                    {
                        touchedFilesLogger.InvokeAction("AddWritten", finalXmlFilePath);
                    }

                    var readStream = OpenFile(Arguments.TouchedFilesPath + ".read", consoleOutput, mode: FileMode.OpenOrCreate);
                    if (readStream == null)
                    {
                        return Failed;
                    }

                    using (var writer = new StreamWriter(readStream))
                    {
                        touchedFilesLogger.InvokeAction("WriteReadPaths", writer);
                    }

                    var writtenStream = OpenFile(Arguments.TouchedFilesPath + ".write", consoleOutput, mode: FileMode.OpenOrCreate);
                    if (writtenStream == null)
                    {
                        return Failed;
                    }

                    using (var writer = new StreamWriter(writtenStream))
                    {
                        touchedFilesLogger.InvokeAction("WriteWrittenPaths", writer);
                    }
                }
            }
            finally
            {
                // At this point analyzers are already complete in which case this is a no-op.  Or they are 
                // still running because the compilation failed before all of the compilation events were 
                // raised.  In the latter case the driver, and all its associated state, will be waiting around 
                // for events that are never coming.  Cancel now and let the clean up process begin.
                if (analyzerCts != null)
                {
                    analyzerCts.Cancel();

                    if (analyzerManager != null)
                    {
                        // Clear cached analyzer descriptors and unregister exception handlers hooked up to the LocalizableString fields of the associated descriptors.
                        analyzerManager.InvokeAction("ClearAnalyzerState", analyzers);
                    }

                    if (Arguments.ReportAnalyzer && analyzerDriver != null && compilation != null)
                    {
                        ReportAnalyzerExecutionTime(consoleOutput, analyzerDriver, Culture, compilation.Options.ConcurrentBuild);
                    }
                }
            }

            return Succeeded;
        }

        protected Dictionary<SyntaxTree, string> OriginalPaths = new Dictionary<SyntaxTree, string>();

        protected virtual ImmutableArray<AdditionalTextFile> ResolveAdditionalFilesFromArguments(object diagnostics, CommonMessageProvider messageProvider, TouchedFileLogger touchedFilesLogger)
        {
            var builder = ImmutableArray.CreateBuilder<AdditionalTextFile>();

            foreach (var file in Arguments.AdditionalFiles)
            {
                builder.Add(Refl.Type_AdditionalTextFile.InvokeFunction(".ctor", file, this));
            }

            return builder.ToImmutableArray();
        }

        private static void ReportAnalyzerExecutionTime(TextWriter consoleOutput, AnalyzerDriver analyzerDriver, CultureInfo culture, bool isConcurrentBuild)
        {
            var exectimes = analyzerDriver.GetFieldOrProperty<ImmutableDictionary<DiagnosticAnalyzer, TimeSpan>>("AnalyzerExecutionTimes"); 
            Debug.Assert(exectimes != null);
            if (exectimes.IsEmpty)
            {
                return;
            }

            var totalAnalyzerExecutionTime = analyzerDriver.GetFieldOrProperty<ImmutableDictionary<DiagnosticAnalyzer, TimeSpan>>("AnalyzerExecutionTimes").Sum(kvp => kvp.Value.TotalSeconds);
            Func<double, string> getFormattedTime = d => d.ToString("##0.000", culture);
            consoleOutput.WriteLine();
            consoleOutput.WriteLine(string.Format((string)Refl.Type_CodeAnalysisResources.InvokeFunction("get_AnalyzerTotalExecutionTime"), getFormattedTime(totalAnalyzerExecutionTime)));

            if (isConcurrentBuild)
            {
                consoleOutput.WriteLine((string)Refl.Type_CodeAnalysisResources.InvokeFunction("get_MultithreadedAnalyzerExecutionNote"));
            }

            var analyzersByAssembly = exectimes
                .GroupBy(kvp => kvp.Key.GetType().GetTypeInfo().Assembly)
                .OrderByDescending(kvp => kvp.Sum(entry => entry.Value.Ticks));

            consoleOutput.WriteLine();

            getFormattedTime = d => d < 0.001 ?
                string.Format(culture, "{0,8:<0.000}", 0.001) :
                string.Format(culture, "{0,8:##0.000}", d);
            Func<int, string> getFormattedPercentage = i => string.Format("{0,5}", i < 1 ? "<1" : i.ToString());
            Func<string, string> getFormattedAnalyzerName = s => "   " + s;

            // Table header
            var analyzerTimeColumn = string.Format("{0,8}", (string)Refl.Type_CodeAnalysisResources.InvokeFunction("get_AnalyzerExecutionTimeColumnHeader"));
            var analyzerPercentageColumn = string.Format("{0,5}", "%");
            var analyzerNameColumn = getFormattedAnalyzerName((string)Refl.Type_CodeAnalysisResources.InvokeFunction("get_AnalyzerNameColumnHeader"));
            consoleOutput.WriteLine(analyzerTimeColumn + analyzerPercentageColumn + analyzerNameColumn);

            // Table rows grouped by assembly.
            foreach (var analyzerGroup in analyzersByAssembly)
            {
                var executionTime = analyzerGroup.Sum(kvp => kvp.Value.TotalSeconds);
                var percentage = (int)(executionTime * 100 / totalAnalyzerExecutionTime);

                analyzerTimeColumn = getFormattedTime(executionTime);
                analyzerPercentageColumn = getFormattedPercentage(percentage);
                analyzerNameColumn = getFormattedAnalyzerName(analyzerGroup.Key.FullName);

                consoleOutput.WriteLine(analyzerTimeColumn + analyzerPercentageColumn + analyzerNameColumn);

                // Rows for each diagnostic analyzer in the assembly.
                foreach (var kvp in analyzerGroup.OrderByDescending(kvp => kvp.Value))
                {
                    executionTime = kvp.Value.TotalSeconds;
                    percentage = (int)(executionTime * 100 / totalAnalyzerExecutionTime);

                    analyzerTimeColumn = getFormattedTime(executionTime);
                    analyzerPercentageColumn = getFormattedPercentage(percentage);
                    analyzerNameColumn = getFormattedAnalyzerName("   " + kvp.Key.ToString());

                    consoleOutput.WriteLine(analyzerTimeColumn + analyzerPercentageColumn + analyzerNameColumn);
                }

                consoleOutput.WriteLine();
            }
        }

        private void GenerateSqmData(CompilationOptions compilationOptions, ImmutableArray<Diagnostic> diagnostics)
        {
        }

        /// <summary>
        /// Given a compilation and a destination directory, determine three names:
        ///   1) The name with which the assembly should be output (default = null, which indicates that the compilation output name should be used).
        ///   2) The path of the assembly/module file (default = destination directory + compilation output name).
        ///   3) The path of the pdb file (default = assembly/module path with ".pdb" extension).
        /// </summary>
        /// <remarks>
        /// C# has a special implementation that implements idiosyncratic behavior of csc.
        /// </remarks>
        protected virtual string GetOutputFileName(Compilation compilation, CancellationToken cancellationToken)
        {
            return Arguments.OutputFileName;
        }

        /// <summary>
        /// Test hook for intercepting File.Open.
        /// </summary>
        internal Stream FileOpen(string path, FileMode mode, FileAccess access, FileShare share)
        {
            return new FileStream(path, mode, access, share);
        }
        

        private Stream OpenFile(string filePath, TextWriter consoleOutput, FileMode mode = FileMode.Open, FileAccess access = FileAccess.ReadWrite, FileShare share = FileShare.None)
        {
        
            try
            {
                return FileOpen(filePath, mode, access, share);
            }
            catch (Exception e)
            {
                if (consoleOutput != null)
                {
                    // TODO: distinct error message?
                    DiagnosticInfo diagnosticInfo = ReflDiagnosticInfo.ctor(MessageProvider, (int)MessageProvider.GetFieldOrProperty<int>("ERR_OutputWriteFailed"), new[]{ filePath, e.Message});
                    consoleOutput.WriteLine(diagnosticInfo.InvokeFunction("ToString", Culture));
                }

                return null;
            }
        }

        protected Stream GetWin32Resources(CommandLineArguments arguments, Compilation compilation, out IEnumerable<DiagnosticInfo> errors)
        {
            return GetWin32ResourcesInternal(MessageProvider, arguments, compilation, out errors);
        }

        // internal for testing
        internal static Stream GetWin32ResourcesInternal(CommonMessageProvider messageProvider, CommandLineArguments arguments, Compilation compilation, out IEnumerable<DiagnosticInfo> errors)
        {
            List<DiagnosticInfo> errorList = new List<DiagnosticInfo>();
            errors = errorList;

            if (arguments.Win32ResourceFile != null)
            {
                return OpenStream(messageProvider, arguments.Win32ResourceFile, arguments.BaseDirectory, messageProvider.GetFieldOrProperty<int>("ERR_CantOpenWin32Resource"), errorList);
            }

            using (Stream manifestStream = OpenManifestStream(messageProvider, compilation.Options.OutputKind, arguments, errorList))
            {
                using (Stream iconStream = OpenStream(messageProvider, arguments.Win32Icon, arguments.BaseDirectory, messageProvider.GetFieldOrProperty<int>("ERR_CantOpenWin32Icon"), errorList))
                {
                    try
                    {
                        return compilation.CreateDefaultWin32Resources(true, arguments.NoWin32Manifest, manifestStream, iconStream);
                    }
                    catch (Exception ex) when (ex.GetType().FullName == "Microsoft.CodeAnalysis.ResourceException")
                    {
                        errorList.Add(ReflDiagnosticInfo.ctor(messageProvider, messageProvider.GetFieldOrProperty<int>("ERR_ErrorBuildingWin32Resource"), new[]{ ex.Message }));
                    }
                    catch (OverflowException ex)
                    {
                        errorList.Add(ReflDiagnosticInfo.ctor(messageProvider, messageProvider.GetFieldOrProperty<int>("ERR_ErrorBuildingWin32Resource"), new[]{ ex.Message }));
                    }
                }
            }

            return null;
        }

        private static Stream OpenManifestStream(CommonMessageProvider messageProvider, OutputKind outputKind, CommandLineArguments arguments, List<DiagnosticInfo> errorList)
        {
            return outputKind == OutputKind.NetModule
                ? null
                : OpenStream(messageProvider, arguments.Win32Manifest, arguments.BaseDirectory, messageProvider.GetFieldOrProperty<int>("ERR_CantOpenWin32Manifest"), errorList);
        }

        private static Stream OpenStream(CommonMessageProvider messageProvider, string path, string baseDirectory, int errorCode, IList<DiagnosticInfo> errors)
        {
            if (path == null)
            {
                return null;
            }

            string fullPath = ResolveRelativePath(messageProvider, path, baseDirectory, errors);
            if (fullPath == null)
            {
                return null;
            }

            try
            {
                return new FileStream(fullPath, FileMode.Open, FileAccess.Read);
            }
            catch (Exception ex)
            {
                errors.Add(ReflDiagnosticInfo.ctor(messageProvider, errorCode, new[]{ fullPath, ex.Message }));
            }

            return null;
        }

        private static string ResolveRelativePath(CommonMessageProvider messageProvider, string path, string baseDirectory, IList<DiagnosticInfo> errors)
        {
            string fullPath = (string)Refl.Type_FileUtilities.InvokeFunction("ResolveRelativePath", path, baseDirectory);
            if (fullPath == null)
            {
                errors.Add(ReflDiagnosticInfo.ctor(messageProvider, messageProvider.GetFieldOrProperty<int>("FTL_InputFileNameTooLong"), new[]{ path }));
            }

            return fullPath;
        }

        internal static bool TryGetCompilerDiagnosticCode(string diagnosticId, string expectedPrefix, out uint code)
        {
            code = 0;
            return diagnosticId.StartsWith(expectedPrefix, StringComparison.Ordinal) && uint.TryParse(diagnosticId.Substring(expectedPrefix.Length), out code);
        }

        /// <summary>
        ///   When overridden by a derived class, this property can override the current thread's
        ///   CurrentUICulture property for diagnostic message resource lookups.
        /// </summary>
        protected virtual CultureInfo Culture
        {
            get
            {
                return Arguments.PreferredUILang ?? CultureInfo.CurrentUICulture;
            }
        }

        private static void EmitDeterminismKey(CommandLineArguments args, string[] rawArgs, string baseDirectory, CommandLineParser parser)
        {
            var key = CreateDeterminismKey(args, rawArgs, baseDirectory, parser);
            var filePath = Path.Combine(args.OutputDirectory, args.OutputFileName + ".key");
            using (var stream = File.OpenWrite(filePath))
            {
                var bytes = Encoding.UTF8.GetBytes(key);
                stream.Write(bytes, 0, bytes.Length);
            }
        }

        /// <summary>
        /// The string returned from this function represents the inputs to the compiler which impact determinism.  It is 
        /// meant to be inline with the specification here:
        /// 
        ///     - https://github.com/dotnet/roslyn/blob/master/docs/compilers/Deterministic%20Inputs.md
        /// 
        /// Issue #8193 tracks filling this out to the full specification. 
        /// 
        ///     https://github.com/dotnet/roslyn/issues/8193
        /// </summary>
        private static string CreateDeterminismKey(CommandLineArguments args, string[] rawArgs, string baseDirectory, CommandLineParser parser)
        {
            List<Diagnostic> diagnostics = new List<Diagnostic>();
            List<string> flattenedArgs = new List<string>();
            parser.InvokeAction("FlattenArgs", rawArgs, diagnostics, flattenedArgs, (object)null, baseDirectory);

            var builder = new StringBuilder();
            var name = !string.IsNullOrEmpty(args.OutputFileName)
                ? Path.GetFileNameWithoutExtension(Path.GetFileName(args.OutputFileName))
                : $"no-output-name-{Guid.NewGuid().ToString()}";

            builder.AppendLine($"{name}");
            builder.AppendLine($"Command Line:");
            foreach (var current in flattenedArgs)
            {
                builder.AppendLine($"\t{current}");
            }

            builder.AppendLine("Source Files:");
            var hash = Refl.Type_MD5CryptoServiceProvider.InvokeFunction(".ctor");
            foreach (var sourceFile in args.SourceFiles)
            {
                var sourceFileName = Path.GetFileName(sourceFile.Path);

                string hashValue;
                try
                {
                    var bytes = File.ReadAllBytes(sourceFile.Path);
                    var hashBytes = (byte[])hash.InvokeFunction("ComputeHash", bytes);
                    var data = BitConverter.ToString(hashBytes);
                    hashValue = data.Replace("-", "");
                }
                catch (Exception ex)
                {
                    hashValue = $"Could not compute {ex.Message}";
                }
                builder.AppendLine($"\t{sourceFileName} - {hashValue}");
            }

            return builder.ToString();
        }
    }
}
