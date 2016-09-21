using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Shaman.Runtime
{
    public static class ProcessUtils
    {
        public static ProcessStartInfo CreateProcessStartInfo(string workingDirectory, string command, params object[] args)
        {
            var psi = new ProcessStartInfo();
            //if (!File.Exists(command) && !command.Contains("/") && !command.Contains("\\"))
            //{
            //    var path = Environment.GetEnvironmentVariable("PATH");
            //    var pathext = Environment.GetEnvironmentVariable("PATHEXT");
            //    if (path != null && pathext != null)
            //    {
            //        var paths = 
            //    }

            //}
            psi.FileName = command;
            psi.WorkingDirectory = workingDirectory;
            psi.Arguments = GetArguments(args);
            psi.UseShellExecute = false;
            return psi;
        }

        private static bool ContainsSpecialCharacters(string str)
        {
            return str.Any(y => !(char.IsLetterOrDigit(y) || y == '\\' || y == '|' || y == '/' || y == '.' || y == ':' || y == '-'));
        }



        public static string GetArguments(params object[] arguments)
        {
            return string.Join(" ", arguments.Select(x =>
            {
                if (x is CommandLineNamedArgument) return x.ToString();
                if (x is RawCommandLineArgument) return x.ToString();
                var str = Convert.ToString(x, CultureInfo.InvariantCulture);
                if (ContainsSpecialCharacters(str) || string.IsNullOrEmpty(str)) return "\"" + str + "\"";
                return str;
            }).ToArray());
        }

        public static string GetCommandLine(string file, params object[] arguments)
        {
            return "\"" + file + "\" " + GetArguments(arguments);
        }

        public static string RunFrom(string workingDirectory, string command, params object[] args)
        {
            var psi = CreateProcessStartInfo(workingDirectory, command, args);
            psi.RedirectStandardError = true;
            psi.RedirectStandardOutput = true;

            using (var p = System.Diagnostics.Process.Start(psi))
            {
                var output = p.StandardOutput.ReadToEnd();
                var err = p.StandardError.ReadToEnd();
                p.WaitForExit();

                if (p.ExitCode != 0)
                    throw new ProcessException(p.ExitCode, err, string.Format("Execution of {0} with arguments {1} failed with exit code {2}: {3}", psi.FileName, psi.Arguments, p.ExitCode, err));

                return output;
            }
        }



        public static void RunPassThroughFrom(string workingDirectory, string command, params object[] args)
        {
            var psi = CreateProcessStartInfo(workingDirectory, command, args);

            using (var p = System.Diagnostics.Process.Start(psi))
            {
                p.WaitForExit();

                if (p.ExitCode != 0)
                    throw new ProcessException(p.ExitCode, null, string.Format("Execution of {0} with arguments {1} failed with exit code {2}.", psi.FileName, psi.Arguments, p.ExitCode));

            }
        }

        public static string Run(string command, params object[] args)
        {
            return RunFrom(Environment.GetEnvironmentVariable("SystemRoot") ?? "/", command, args);
        }

        public static void RunPassThrough(string command, params object[] args)
        {
            RunPassThroughFrom(Environment.GetEnvironmentVariable("SystemRoot") ?? "/", command, args);
        }

        public static CommandLineNamedArgument NamedArgument(string name, object value)
        {
            return new CommandLineNamedArgument(name, value);
        }


        public class RawCommandLineArgument
        {
            public string Value { get; private set; }
            public RawCommandLineArgument(string value)
            {
                this.Value = value;
            }

            public override string ToString()
            {
                return Value;
            }
        }

        public class CommandLineNamedArgument
        {

            public CommandLineNamedArgument(string name, object value)
            {
                this.Name = name;
                this.Value = value;
            }

            public string Name { get; private set; }
            public object Value { get; private set; }
            public override string ToString()
            {
                var v = Convert.ToString(Value, CultureInfo.InvariantCulture);
                return Name + (ContainsSpecialCharacters(v) ? "\"" + v + "\"" : v);
            }
        }
    }



    public class ProcessException : Exception
    {
        public int ExitCode { get; private set; }
        public string ErrorText { get; private set; }

        public ProcessException(int exitCode)
        {
            this.ExitCode = exitCode;
        }

        public ProcessException(int exitCode, string errorText, string message)
            : base(message)
        {
            this.ExitCode = exitCode;
            this.ErrorText = errorText;
        }
    }


}
