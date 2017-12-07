using System;
using System.Collections.Generic;
using System.Reflection;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Shaman;
using Shaman.Runtime;
using Microsoft.CodeAnalysis.Emit;
using System.Threading;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Globalization;


// Disable field never assigned warning
#pragma warning disable CS0649

internal static class Refl
{
    public static Assembly Assembly_Csc = typeof(Microsoft.CodeAnalysis.CSharp.CommandLine.Program).GetTypeInfo().Assembly;
    public static Assembly Assembly_Common = typeof(Microsoft.CodeAnalysis.Compilation).GetTypeInfo().Assembly;
    public static Assembly Assembly_Csharp = typeof(Microsoft.CodeAnalysis.CSharpExtensions).GetTypeInfo().Assembly;
    public static Type Type_Csc = Assembly_Csc.GetType("Microsoft.CodeAnalysis.CSharp.CommandLine.Csc");
    public static Type Type_BuildPaths = typeof(Microsoft.CodeAnalysis.AssemblyMetadata).Assembly.GetType("Microsoft.CodeAnalysis.BuildPaths");
    public static Type Type_ErrorLogger = Assembly_Common.GetType("Microsoft.CodeAnalysis.ErrorLogger");
    public static Type Type_CompilerEmitStreamProvider = Assembly_Common.GetType("Microsoft.CodeAnalysis.CommonCompiler+CompilerEmitStreamProvider");
    public static Type Type_SimpleEmitStreamProvider = Assembly_Common.GetType("Microsoft.CodeAnalysis.Compilation+SimpleEmitStreamProvider");
    public static Type Type_EmitStreamProvider = Assembly_Common.GetType("Microsoft.CodeAnalysis.Compilation+EmitStreamProvider");
    public static Type Type_AdditionalTextFile = Assembly_Common.GetType("Microsoft.CodeAnalysis.AdditionalTextFile");
    public static Type Type_FileUtilities = Assembly_Common.GetType("Roslyn.Utilities.FileUtilities");

    public static Type Type_MD5CryptoServiceProvider = Assembly_Common.GetType("Roslyn.Utilities.MD5CryptoServiceProvider");
    public static Type Type_DiagnosticBag = Assembly_Common.GetType("Microsoft.CodeAnalysis.DiagnosticBag");

    public static Type Type_ErrorFacts = Assembly_Csharp.GetType("Microsoft.CodeAnalysis.CSharp.ErrorFacts");

    public static Type Type_MessageId = Assembly_Csharp.GetType("Microsoft.CodeAnalysis.CSharp.MessageID");
    public static Type Type_LoggingMetadataFileReferenceResolver = Refl.Assembly_Common.GetType("Microsoft.CodeAnalysis.CommonCompiler+LoggingMetadataFileReferenceResolver");



    public static Type Type_CodeAnalysisResources = Assembly_Common.GetType("Microsoft.CodeAnalysis.CodeAnalysisResources");
    public static Type Type_Compilation = Assembly_Common.GetType("Microsoft.CodeAnalysis.Compilation");

    public static Type Type_DiagnosticInfo = Assembly_Common.GetType("Microsoft.CodeAnalysis.DiagnosticInfo", true, false);
}

internal static class ReflCSharpCommandLineArguments
{
    public static Func<CSharpCommandLineArguments, bool> get_ShouldIncludeErrorEndLocation;
    static ReflCSharpCommandLineArguments()
    {
        ReflectionHelper.InitializeWrapper(typeof(ReflCSharpCommandLineArguments), typeof(CSharpCommandLineArguments));
    }
}

internal static class ReflEmitStreamProvider
{
    public static Func<object, object, Stream> CreateStream;
    static ReflEmitStreamProvider()
    {
        ReflectionHelper.InitializeWrapper(typeof(ReflEmitStreamProvider), Refl.Type_EmitStreamProvider);
    }
}

internal static class ReflCommandLineDiagnosticFormatter
{
    public static Func<string, bool, bool, CSharpDiagnosticFormatter> ctor;
    public static Func<CSharpDiagnosticFormatter, string, string> RelativizeNormalizedPath;
    static ReflCommandLineDiagnosticFormatter()
    {
        ReflectionHelper.InitializeWrapper(typeof(ReflCommandLineDiagnosticFormatter), Refl.Assembly_Csharp, "Microsoft.CodeAnalysis.CSharp.CommandLineDiagnosticFormatter");
    }
}

internal static class ReflCoreClrAnalyzerAssemblyLoader
{
    public static Func<IAnalyzerAssemblyLoader> ctor;
    static ReflCoreClrAnalyzerAssemblyLoader()
    {
        ReflectionHelper.InitializeWrapper(typeof(ReflCoreClrAnalyzerAssemblyLoader), Refl.Assembly_Csc, "Microsoft.CodeAnalysis.CoreClrAnalyzerAssemblyLoader");
    }
}


internal static class ReflPathUtilities
{
    public static Func<string, bool> IsAbsolute;
    public static Func<bool> get_IsUnixLikePlatform;
    public static Func<string, bool, string> GetFileName;
    static ReflPathUtilities()
    {
        ReflectionHelper.InitializeWrapper(typeof(ReflPathUtilities), Refl.Assembly_Common, "Roslyn.Utilities.PathUtilities");
    }
}

internal static class ReflFatalError
{
    public static Action<Action<Exception>> set_Handler;
    static ReflFatalError()
    {
        ReflectionHelper.InitializeWrapper(typeof(ReflFatalError), Refl.Assembly_Common, "Microsoft.CodeAnalysis.FatalError");
    }
}





internal static class ReflCommandLineParser
{
    public static bool TryParseClientArgs(
    IEnumerable<string> args,
    out List<string> parsedArgs,
    out bool containsShared,
    out string keepAliveValue,
    out string sessionKey,
    out string errorMessage){
        var m = typeof(CommandLineParser).GetMethod("TryParseClientArgs", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        var parameters = new object[6];
        parameters[0] = args;
        var r = m.Invoke(null, parameters);
        parsedArgs = (List<string>)parameters[1];
        containsShared = (bool)parameters[2];
        keepAliveValue = (string)parameters[3];
        sessionKey = (string)parameters[4];
        errorMessage = (string)parameters[5];
        return (bool)r;
    }

    public static Func<CommandLineParser, object> get_MessageProvider;
    static ReflCommandLineParser()
    {
        ReflectionHelper.InitializeWrapper(typeof(ReflCommandLineParser), typeof(CommandLineParser));
    }
}

internal static class ReflCommandLineArguments
{
    public static Func<CommandLineArguments, MetadataReferenceResolver, object, object, List<MetadataReference>, bool> ResolveMetadataReferences;
    static ReflCommandLineArguments()
    {
        ReflectionHelper.InitializeWrapper(typeof(ReflCommandLineArguments), typeof(CommandLineArguments));
    }
}



internal static class ReflDiagnosticInfo
{
    public static Func<object, int, object[], object> ctor;
    public new static Func<object, IFormatProvider, string> ToString;
    static ReflDiagnosticInfo()
    {
        ReflectionHelper.InitializeWrapper(typeof(ReflDiagnosticInfo), Refl.Assembly_Common, "Microsoft.CodeAnalysis.DiagnosticInfo");
    }
}
internal static class ReflLoggingMetadataFileReferenceResolver
{
    public static Func<object, Func<string, MetadataReferenceProperties, PortableExecutableReference>, object, MetadataReferenceResolver> ctor;
    static ReflLoggingMetadataFileReferenceResolver()
    {
        ReflectionHelper.InitializeWrapper(typeof(ReflLoggingMetadataFileReferenceResolver), Refl.Type_LoggingMetadataFileReferenceResolver);
    }
}
internal static class ReflEncodedStringText
{

    public static Func<Stream, Encoding, SourceHashAlgorithm, bool, SourceText> Create;
    static ReflEncodedStringText()
    {
        ReflectionHelper.InitializeWrapper(typeof(ReflEncodedStringText), Refl.Assembly_Common, "Microsoft.CodeAnalysis.Text.EncodedStringText");
    }
}

internal static class ReflCompilation
{
    public static Func<
                            Compilation,
                            object,//peStreamProvider, 
                            object,//pdbStreamProvider, 
                            object,//xmlDocumentationStreamProvider,
                            object,//win32ResourcesStreamProvider,
                            IEnumerable<ResourceDescription>,//manifestResources,
                            EmitOptions,//options,
                            IMethodSymbol,//debugEntryPoint,
                            CancellationToken, //cancellationToken)

                            EmitResult
                        > Emit;
    static ReflCompilation()
    {
        var e = typeof(ReflCompilation).GetField("Emit", BindingFlags.Public | BindingFlags.Static);
        var m = Refl.Type_Compilation.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(x=>x.Name == "Emit" && x.GetParameters().Length == 9);
        
        e.SetValue(null, ReflectionHelper.GetWrapper(m, e.FieldType));
    }
}

internal static class ReflLoggingXmlFileResolver
{
    public static Func<string, object, XmlFileResolver> ctor;
    static ReflLoggingXmlFileResolver()
    {
        ReflectionHelper.InitializeWrapper(typeof(ReflLoggingXmlFileResolver), Refl.Assembly_Common, "Microsoft.CodeAnalysis.CommonCompiler+LoggingXmlFileResolver");
    }
}
internal static class ReflLoggingSourceFileResolver
{
    public static Func<ImmutableArray<string>, string, ImmutableArray<KeyValuePair<string, string>>, object, SourceFileResolver> ctor;
    static ReflLoggingSourceFileResolver()
    {
        ReflectionHelper.InitializeWrapper(typeof(ReflLoggingSourceFileResolver), Refl.Assembly_Common, "Microsoft.CodeAnalysis.CommonCompiler+LoggingSourceFileResolver");
    }
}
internal static class ReflLoggingStrongNameProvider
{
    public static Func<ImmutableArray<string>, object, string, DesktopStrongNameProvider> ctor;
    static ReflLoggingStrongNameProvider()
    {
        ReflectionHelper.InitializeWrapper(typeof(ReflLoggingStrongNameProvider), Refl.Assembly_Common, "Microsoft.CodeAnalysis.CommonCompiler+LoggingStrongNameProvider");
    }
}

internal static class ReflExistingReferencesResolver
{
    public static Func<MetadataReferenceResolver, ImmutableArray<MetadataReference>, MetadataReferenceResolver> ctor;
    static ReflExistingReferencesResolver()
    {
        ReflectionHelper.InitializeWrapper(typeof(ReflExistingReferencesResolver), Refl.Assembly_Common, "Microsoft.CodeAnalysis.CommonCompiler+ExistingReferencesResolver");
    }
}



internal static class ReflTouchedFileLogger
{
    public static Action<object, string> AddRead;
    static ReflTouchedFileLogger()
    {
        ReflectionHelper.InitializeWrapper(typeof(ReflTouchedFileLogger), Refl.Assembly_Common, "Microsoft.CodeAnalysis.TouchedFileLogger");
    }
}


internal static class ReflSyntaxTree
{
    public static FileLinePositionSpan GetMappedLineSpanAndVisibility(
    SyntaxTree st,
    TextSpan span,
    out bool isHiddenPosition){

        var m = typeof(SyntaxTree).GetMethod("GetMappedLineSpanAndVisibility", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        var parameters = new object[2];
        parameters[0] = span;
        var r = m.Invoke(st, parameters);
        isHiddenPosition = (bool)parameters[1];
        return (FileLinePositionSpan)r;
    }
    
}


internal delegate object CreateAndAttachToCompilationDelegate(
    Compilation compilation,
    ImmutableArray<DiagnosticAnalyzer> analyzers,
    AnalyzerOptions options,
    object analyzerManager,
    Action<Diagnostic> addExceptionDiagnostic,
    bool reportAnalyzer,
    out Compilation newCompilation,
    CancellationToken cancellationToken);
    /*Action<Exception, DiagnosticAnalyzer, Diagnostic> onAnalyzerException,
    Func<Exception, bool> analyzerExceptionFilter,
    bool reportAnalyzer,
    out Compilation newCompilation,
    CancellationToken cancellationToken);*/

internal static class ReflAnalyzerDriver
{
    public static CreateAndAttachToCompilationDelegate CreateAndAttachToCompilation;
    static ReflAnalyzerDriver()
    {
        ReflectionHelper.InitializeWrapper(typeof(ReflAnalyzerDriver), typeof(Compilation));
    }
}

#pragma warning restore CS0649

