using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using Shaman.Runtime;
class Program
{
    static void CopyTo(string source, string destinationFolder)
    {
        File.Copy(source, Path.Combine(destinationFolder, Path.GetFileName(source)), true);
    }
    static void TryDeleteDir(string path)
    {
        if (Directory.Exists(path))
        {
            try
            {
                Directory.Delete(path, true);
            }
            catch (System.Exception ex)
            {
                foreach (var file in Directory.EnumerateFiles(path))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch
                    {
                    }
                }
                foreach (var folder in Directory.EnumerateDirectories(path))
                {
                    TryDeleteDir(folder);
                }
                System.Console.WriteLine("Warning: cannot remove folder: " + ex.Message);
            }
        }
    }

    static int Main(string[] args)
    {
        try
        {
            return MainInternal(args);
        }
        catch (System.Exception ex)
        {
            System.Console.WriteLine(ex);
            return 1;
        }
    }
    static int MainInternal(string[] args)
    {
        if (args.Contains("--create-package"))
        {
            var repoDir = Path.GetDirectoryName(typeof(Program).Assembly.Location);
            while (!File.Exists(Path.Combine(repoDir, "RoslynLinqRewrite.sln")))
            {
                repoDir = Path.GetDirectoryName(repoDir);
                if (repoDir == null) throw new Exception();
            }
            repoDir = Path.GetDirectoryName(repoDir);

            var xpackages = XDocument.Parse(File.ReadAllText(Path.Combine(repoDir, "RoslynLinqRewrite/RoslynLinqRewrite/packages.config")));

            var outputDir = Path.Combine(repoDir, "RoslynLinqRewrite/Shaman.Roslyn.LinqRewrite.Distrib/roslyn-linq-rewrite");
            TryDeleteDir(outputDir);
            Directory.CreateDirectory(outputDir);
            File.Delete(Path.Combine(outputDir, "installed"));
            var project = new JObject();

            var dependencies = new JObject();

            foreach (var item in xpackages.Descendants("package"))
            {
                var name = item.Attribute("id").Value;
                dependencies[name] = item.Attribute("version").Value;
            }
            dependencies["Microsoft.DiaSymReader.native"] = "1.5.0-beta1";

            var frameworks = new JObject();
            frameworks["net462"] = new JObject();
            project["dependencies"] = dependencies;
            project["frameworks"] = frameworks;
            File.WriteAllText(Path.Combine(outputDir, "project-dependencies.json"), project.ToString(Newtonsoft.Json.Formatting.Indented), Encoding.UTF8);
            File.Copy(typeof(Program).Assembly.Location, Path.Combine(outputDir, "Shaman.Roslyn.LinqRewrite.Initialization.dll"), true);
            CopyTo(Path.Combine(Path.GetDirectoryName(typeof(Program).Assembly.Location), "Newtonsoft.Json.dll"), outputDir);

            var toolPath = Path.Combine(repoDir, "RoslynLinqRewrite/dotnet-compile-csc-linq-rewrite");
            ProcessUtils.RunPassThroughFrom(toolPath, "dotnet", "restore");
            ProcessUtils.RunPassThroughFrom(toolPath, "dotnet", "build", "-c", "Release");
            File.Copy(Path.Combine(toolPath, "bin/Release/net46/dotnet-compile-csc-linq-rewrite.exe"), Path.Combine(outputDir, "roslyn-linq-rewrite.exe"), true);
            File.Copy(Path.Combine(repoDir, "RoslynLinqRewrite/RoslynLinqRewrite/bin/Release/roslyn-linq-rewrite.exe.config"), Path.Combine(outputDir, "roslyn-linq-rewrite.exe.config"), true);
            return 0;
        }

        if (args.Contains("--install"))
        {

            var folder = Path.GetDirectoryName(typeof(Program).Assembly.Location);

            if (args.Contains("--dev"))
            {
                folder = Path.GetFullPath("roslyn-linq-rewrite");
            }
            else
            {
                if (folder.Replace("\\", "/").Contains("/bin/Debug")) throw new InvalidOperationException("Must not be run from here.");
            }
            var dependenciesPath = Path.Combine(folder, "project-dependencies.json");
            File.Copy(dependenciesPath, Path.Combine(folder, "project.json"), true);
            var binFolder = Path.Combine(folder, "bin");


            TryDeleteDir(binFolder);
            try
            {
                ProcessUtils.RunPassThroughFrom(folder, "dotnet", "restore");
            }
            catch (Exception ex) when (!(ex is ProcessException))
            {
                try
                {
                    ProcessUtils.RunPassThrough(folder, "dotnet", "--version");
                }
                catch
                {
                    System.Console.WriteLine("This tool requires the .NET CLI. You can install it from here: http://github.com/dotnet/cli/");
                    return 1;
                }
                throw ex;
            }

            ProcessUtils.RunPassThroughFrom(folder, "dotnet", "publish");

            var nuget = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget/packages");

            CopyTo(Path.Combine(nuget, "microsoft.diasymreader.native/1.5.0-beta1/runtimes/win/native/Microsoft.DiaSymReader.Native.amd64.dll"), folder);
            CopyTo(Path.Combine(nuget, "microsoft.diasymreader.native/1.5.0-beta1/runtimes/win/native/Microsoft.DiaSymReader.Native.x86.dll"), folder);


            var files = Directory.EnumerateFiles(binFolder, "*.dll", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                if (Path.GetFileName(file) == "Newtonsoft.Json.dll") continue;
                CopyTo(file, folder);
            }
            try
            {
                Directory.Delete(binFolder, true);
            }
            catch (System.Exception ex)
            {
                System.Console.WriteLine("Warning: cannot remove bin folder: " + ex.Message);
            }

            var dependencies = ((JObject)JObject.Parse(File.ReadAllText(dependenciesPath))["dependencies"]).Properties().ToDictionary(x => x.Name, x => x.Value.Value<string>());
            var preferences = new[] { "net462", "net461", "net46", "net45", "netstandard1.5", "net463" };
            foreach (var dep in dependencies)
            {
                var s = Path.Combine(nuget, dep.Key.ToLower(), dep.Value, "lib");
                foreach (var pref in preferences)
                {
                    var q = Path.Combine(s, pref);
                    if (Directory.Exists(q))
                    {
                        var dll = Directory.EnumerateFiles(q, "*.dll").FirstOrDefault();
                        if (dll != null)
                        {
                            var name = Path.GetFileName(dll);
                            if (!File.Exists(Path.Combine(folder, name)))
                            {
                                CopyTo(dll, folder);
                            }
                            break;
                        }
                    }
                }
            }

            File.Copy(Path.Combine(folder, "roslyn-linq-rewrite.exe"), Path.Combine(folder, "csc.exe"), true);
            File.Copy(Path.Combine(folder, "roslyn-linq-rewrite.exe.config"), Path.Combine(folder, "csc.exe.config"), true);

            File.Delete(Path.Combine(folder, "project.json"));
            File.Delete(Path.Combine(folder, "project.lock.json"));
            File.WriteAllText(Path.Combine(folder, "installed"), "1", Encoding.UTF8);
            return 0;
        }
        System.Console.WriteLine("Invalid arguments.");
        return 1;
    }
}
