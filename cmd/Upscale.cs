using System.CommandLine;

namespace Upscale
{
    // Parse command line arguments, the following args are supported:
    // - -d, --dryrun: Dry run, don't actually run the process, only show in the console (and log) what might be run)
    // - -f, --folders <folders> - List of folders to search for files separated by ;
    // - -t, --transform <transform> - Path to the transform file (e.g. "{tools.path}/transforms.config.json")
    // - -p, --processes <processes> - Path to the processes file (e.g. "{tools.path}/processes.config.json")


    class Program
    {
        private static string ExpandPaths(string path, Dictionary<string, string> paths)
        {
            foreach (var item in paths)
            {
                if (path.Contains(item.Key))
                {
                    path = path.Replace($"{item.Key}", item.Value);
                    path = Environment.ExpandEnvironmentVariables(path);
                    path = Path.GetFullPath(path);
                    break;
                }
            }
            return path;
        }

        private static int Run(bool dryrun, string folders, string transformsFilepath, string processesFilepath, int nrPipelinesInParallel, int job, int totaljobs)
        {
            Vars.Vars vars = new();
            Config.Paths paths = new();
            foreach (var f in folders.Split(';'))
            {
                var kv = f.Trim().Split('=');
                if (kv.Length == 2)
                {
                    var key = kv[0].Trim();
                    var value = kv[1].Trim();
                    vars.Add(key, value);
                    switch (key)
                    {
                        case "tools.path":
                            paths.ToolsPath = value;
                            break;
                        case "input.path":
                            paths.InputPath = value;
                            break;
                        case "output.path":
                            paths.OutputPath = value;
                            break;
                        case "cache.path":
                            paths.CachePath = value;
                            break;
                    }
                }
            }

            // Expand paths in the transforms and processes filepaths
            transformsFilepath = vars.ResolveString(transformsFilepath);
            processesFilepath = vars.ResolveString(processesFilepath);

            // Load JSON 'process.config.json' file
            if (Config.Processes.ReadJson(processesFilepath, out var processes) == false)
            {
                return -1;
            }
            if (Config.Transforms.ReadJson(transformsFilepath, out var transforms) == false)
            {
                return -1;
            }

            // In the 'input.path' there is a 'global.config.json' file that contains the default 'settings' for each input file
            // This file is not optional, it must exist
            var globalTextureConfigFilepath = vars.ResolveString("{input.path}/global.config.json");
            if (Config.TextureConfig.ReadJson(globalTextureConfigFilepath, out var globalTextureConfig) == false)
            {
                return -1;
            }

            // Glob all the 'png' files in the input path
            var inputFiles = Directory.GetFiles(paths.InputPath, "*.png", SearchOption.AllDirectories).ToList();
            inputFiles.Sort();

            // Divide the work (inputFiles) into N jobs
            var inputFilesPerJob = inputFiles.Count / totaljobs;
            var inputFilesStart = job * inputFilesPerJob;
            var inputFilesEnd = (job + 1) * inputFilesPerJob;
            if (job == totaljobs - 1)
            {
                inputFilesEnd = inputFiles.Count;
            }

            var inputFilesJob = inputFiles.Skip(inputFilesStart).Take(inputFilesEnd - inputFilesStart).ToList();

            foreach (var currentInputFilePath in inputFilesJob)
            {
                // Figure out if there is a 'currentInputFilePath'.json file next to the 'currentInputFilePath' file.
                // If so we need to load/parse that JSON file and use the data in it to override the default settings.
                if (Config.TextureConfig.ReadJson(currentInputFilePath+".json", out var currentTextureConfig) == false)
                {
                    return -1;
                }

                Vars.Vars localVars = new(vars);
                globalTextureConfig.MergeIntoVars(localVars, false);
                currentTextureConfig.MergeIntoVars(localVars, true);

                string transform = localVars.ResolveString("transform");
                transforms.GetTransformByName(transform, out var transformConfig);

                // Run the pipeline
                var pipeline = new Transform.Pipeline(paths, processes, transformConfig, localVars);
                pipeline.Execute(currentInputFilePath, dryrun);
            }

            return 0;
        }

        public static int Main(string[] args)
        {
            var optionDryrun = new Option<bool>(new[] { "--dryrun" }, "Dry run, don't actually run the process, only show in the console (and log) what might be run)");
            var optionFolders = new Option<string>(new[] { "-f", "--folders" }, "List of folders to search for files separated by ;");
            var optionTransforms = new Option<string>(new[] { "--transforms" }, getDefaultValue: () => "{tools.path}/transforms.config.json", "Path to the transform file (e.g. \"{tools.path}/transforms.config.json\")");
            var optionProcesses = new Option<string>(new[] { "--processes" }, getDefaultValue: () => "{tools.path}/processes.config.json", "Path to the processes file (e.g. \"{tools.path}/processes.config.json\")");
            var optionParallel = new Option<int>(new[] { "-p", "--parallel" }, getDefaultValue: () => 1, "Number of pipelines to run in parallel");
            var optionDenominator = new Option<int>(new[] { "-d", "--denominator" }, getDefaultValue: () => 1, "Work is split into N jobs, this is the denominator of the fraction of work to do");
            var optionNominator = new Option<int>(new[] { "-n", "--nominator" }, getDefaultValue: () => 1, "Work is split into N jobs, this is the nominator of the fraction of work to do");

            var rootCommand = new RootCommand
            {
                optionDryrun,
                optionFolders,
                optionTransforms,
                optionProcesses,
                optionParallel,
                optionDenominator,
                optionNominator
            };

            rootCommand.SetHandler((dryrun, folders, transforms, processes, parallel, denominator, nominator) =>
            {
                Run(dryrun, folders, transforms, processes, parallel, nominator, denominator);
            },
            optionDryrun, optionFolders, optionTransforms, optionProcesses, optionParallel, optionDenominator, optionNominator);

            return rootCommand.InvokeAsync(args).Result;
        }
    }
}
