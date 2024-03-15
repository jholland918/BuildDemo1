using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Assets.Editor
{
    /// <summary>
    /// Build script used for CI/CD automated builds
    /// </summary>
    /// <remarks>
    /// The initial version of this script was copied from the example at https://raw.githubusercontent.com/game-ci/documentation/main/example/BuildScript.cs
    /// This script is referenced in our release.yml file as `buildMethod: Assets.Editor.CiBuilder.Build`. Any refactors/renames must also be made in the related Github Actions yml files.
    /// </remarks>
    public static class CiBuilder
    {
        private static readonly string Eol = Environment.NewLine;

        private static readonly string[] Secrets = {"androidKeystorePass", "androidKeyaliasName", "androidKeyaliasPass"};

        public static void Build()
        {
            // Gather values from args
            Dictionary<string, string> options = GetValidatedOptions();

            // Set version for this build
            PlayerSettings.bundleVersion = options["buildVersion"];
            PlayerSettings.macOS.buildNumber = options["buildVersion"];
            PlayerSettings.Android.bundleVersionCode = int.Parse(options["androidVersionCode"]);

            // Determine subtarget
            int buildSubtarget = 0;

            if (!options.TryGetValue("standaloneBuildSubtarget", out var subtargetValue) || !Enum.TryParse(subtargetValue, out StandaloneBuildSubtarget buildSubtargetValue))
            {
                buildSubtargetValue = default;
            }
            buildSubtarget = (int)buildSubtargetValue;

            string[] scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(s => s.path).ToArray();

            // Custom build
            string customBuildPath = options["customBuildPath"];

            BuildReport serverReport = BuildServer(customBuildPath, scenes);
            if (serverReport.summary.result != BuildResult.Succeeded)
            {
                ExitWithResult(serverReport.summary.result);
                return;
            }

            BuildReport clientReport = BuildClient(customBuildPath, scenes);
            if (clientReport.summary.result != BuildResult.Succeeded)
            {
                ExitWithResult(clientReport.summary.result);
                return;
            }
        }

        private static Dictionary<string, string> GetValidatedOptions()
        {
            ParseCommandLineArguments(out Dictionary<string, string> validatedOptions);

            if (!validatedOptions.TryGetValue("projectPath", out string _))
            {
                Console.WriteLine("Missing argument -projectPath");
                EditorApplication.Exit(110);
            }

            if (!validatedOptions.TryGetValue("buildTarget", out string buildTarget))
            {
                Console.WriteLine("Missing argument -buildTarget");
                EditorApplication.Exit(120);
            }

            if (!Enum.IsDefined(typeof(BuildTarget), buildTarget ?? string.Empty))
            {
                Console.WriteLine($"{buildTarget} is not a defined {nameof(BuildTarget)}");
                EditorApplication.Exit(121);
            }

            if (!validatedOptions.TryGetValue("customBuildPath", out string _))
            {
                Console.WriteLine("Missing argument -customBuildPath");
                EditorApplication.Exit(130);
            }

            const string defaultCustomBuildName = "TestBuild";
            if (!validatedOptions.TryGetValue("customBuildName", out string customBuildName))
            {
                Console.WriteLine($"Missing argument -customBuildName, defaulting to {defaultCustomBuildName}.");
                validatedOptions.Add("customBuildName", defaultCustomBuildName);
            }
            else if (customBuildName == "")
            {
                Console.WriteLine($"Invalid argument -customBuildName, defaulting to {defaultCustomBuildName}.");
                validatedOptions.Add("customBuildName", defaultCustomBuildName);
            }

            return validatedOptions;
        }

        private static void ParseCommandLineArguments(out Dictionary<string, string> providedArguments)
        {
            providedArguments = new Dictionary<string, string>();
            string[] args = Environment.GetCommandLineArgs();

            Console.WriteLine(
                $"{Eol}" +
                $"###########################{Eol}" +
                $"#    Parsing settings     #{Eol}" +
                $"###########################{Eol}" +
                $"{Eol}"
            );

            // Extract flags with optional values
            for (int current = 0, next = 1; current < args.Length; current++, next++)
            {
                // Parse flag
                bool isFlag = args[current].StartsWith("-");
                if (!isFlag) continue;
                string flag = args[current].TrimStart('-');

                // Parse optional value
                bool flagHasValue = next < args.Length && !args[next].StartsWith("-");
                string value = flagHasValue ? args[next].TrimStart('-') : "";
                bool secret = Secrets.Contains(flag);
                string displayValue = secret ? "*HIDDEN*" : "\"" + value + "\"";

                // Assign
                Console.WriteLine($"Found flag \"{flag}\" with value {displayValue}.");
                providedArguments.Add(flag, value);
            }
        }

        public static BuildReport BuildServer(string filePath, string[] scenes)
        {
            string locationPathName = Path.Combine(Path.GetDirectoryName(filePath), "Server", Path.GetFileName(filePath));

            var buildPlayerOptions = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = locationPathName,
                target = BuildTarget.StandaloneWindows64,
                // BuildOptions.CleanBuildCache is needed for IProcessSceneWithReport to run every time.
                options = BuildOptions.ShowBuiltPlayer | BuildOptions.Development | BuildOptions.CleanBuildCache | BuildOptions.AutoRunPlayer,
                // Not building headless for now since there's a bug with our aiming system
                //subtarget = (int)StandaloneBuildSubtarget.Server
            };

            BuildReport report = BuildPipeline.BuildPlayer(buildPlayerOptions);

            //////if (summary.result == BuildResult.Succeeded)
            //////{
            //////    var appConfig = new AppConfig
            //////    {
            //////        Name = "Project Cardium Server",
            //////        Version = "1.0.0",
            //////        IsServer = true,
            //////    };

            //////    string appConfigContents = JsonUtility.ToJson(appConfig, true);
            //////    File.WriteAllText(Path.Combine(buildFolder, "appConfig.json"), appConfigContents);
            //////}

            ReportSummary(report.summary);

            return report;
        }

        public static BuildReport BuildClient(string filePath, string[] scenes)
        {
            string locationPathName = Path.Combine(Path.GetDirectoryName(filePath), "Client", Path.GetFileName(filePath));

            var buildPlayerOptions = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = locationPathName,
                target = BuildTarget.StandaloneWindows64,
                // BuildOptions.CleanBuildCache is needed for IProcessSceneWithReport to run every time.
                options = BuildOptions.ShowBuiltPlayer | BuildOptions.Development | BuildOptions.CleanBuildCache,
                subtarget = (int)StandaloneBuildSubtarget.Player
            };

            BuildReport report = BuildPipeline.BuildPlayer(buildPlayerOptions);
            BuildSummary summary = report.summary;

            //////if (report.summary.result == BuildResult.Succeeded)
            //////{
            //////    var appConfig = new AppConfig
            //////    {
            //////        Name = "Project Cardium Client",
            //////        Version = "1.0.0",
            //////        IsClient = true,
            //////        ClientTransportAddress = CLIENT_TRANSPORT_ADDRESS,
            //////        ClientTransportPort = CLIENT_TRANSPORT_PORT,
            //////    };

            //////    string appConfigContents = JsonUtility.ToJson(appConfig, true);
            //////    File.WriteAllText(Path.Combine(buildFolder, "appConfig.json"), appConfigContents);
            //////}

            ReportSummary(report.summary);

            return report;
        }

        private static void ReportSummary(BuildSummary summary)
        {
            Console.WriteLine(
                $"{Eol}" +
                $"###########################{Eol}" +
                $"#      Build results      #{Eol}" +
                $"###########################{Eol}" +
                $"{Eol}" +
                $"Duration: {summary.totalTime.ToString()}{Eol}" +
                $"Warnings: {summary.totalWarnings.ToString()}{Eol}" +
                $"Errors: {summary.totalErrors.ToString()}{Eol}" +
                $"Size: {summary.totalSize.ToString()} bytes{Eol}" +
                $"{Eol}"
            );
        }

        private static void ExitWithResult(BuildResult result)
        {
            switch (result)
            {
                case BuildResult.Succeeded:
                    Console.WriteLine("Build succeeded!");
                    EditorApplication.Exit(0);
                    break;
                case BuildResult.Failed:
                    Console.WriteLine("Build failed!");
                    EditorApplication.Exit(101);
                    break;
                case BuildResult.Cancelled:
                    Console.WriteLine("Build cancelled!");
                    EditorApplication.Exit(102);
                    break;
                case BuildResult.Unknown:
                default:
                    Console.WriteLine("Build result is unknown!");
                    EditorApplication.Exit(103);
                    break;
            }
        }
    }
}

