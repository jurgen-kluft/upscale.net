using FileTracker;

namespace Upscale;

// Parse command line arguments:
// - --dry-run: Dry run, don't actually run the process, only show in the console (and log what might be run)
// - --transforms FILE - Path to the transform file (e.g. "{tools.path}/transforms.config.json")
// - --processes FILE - Path to the processes file (e.g. "{tools.path}/processes.config.json")
// - -a, --vars <key=value> - List of variables separated by ;
// - -n, --nominator <nominator>: Work is split into N jobs, this is the nominator of the fraction of work to do
// - -d, --denominator <denominator>: Work is split into N jobs, this is the denominator of the fraction of work to do

// Notes:
// If multiple upscale processes are running on the same machine they individually point to a different '{cache.path}'.
// A '{cache.path}' should never be shared.

internal static class Program
{
    private static int Run(bool dryRun, bool validate, string variables, string transformsFilePath, string processesFilePath, int job, int totalJobs)
    {
        Vars.Vars vars = new(variables);

        var processesDepFilePath = processesFilePath.Replace("{tools.path}", "{cache.path}") + ".dep";

        // Expand paths in the transforms and processes file paths
        transformsFilePath = vars.ResolvePath(transformsFilePath);
        processesFilePath = vars.ResolvePath(processesFilePath);

        // Load and parse JSON 'process.config.json' file
        if (Config.ProcessesDescriptor.ReadJson(processesFilePath, vars, out var processes) == false)
        {
            Log.Error("Failed to read processes file '{processesFilePath}'", processesFilePath);
            return -1;
        }

        var fileTrackerCache = new FileTrackerCache();

        // Load and parse JSON 'transforms.config.json' file
        var transforms = new Config.TransformationsDescriptor();
        if (transforms.ReadJson(transformsFilePath, processes, vars, fileTrackerCache) == false)
        {
            Log.Error("Failed to read transforms file '{transformsFilePath}'", transformsFilePath);
            return -1;
        }

        if (validate)
        {
            // Validate the processes and transforms
            if (processes.Validate(vars) < 0)
            {
                Log.Error("Failed to validate processes file '{processesFilePath}'", processesFilePath);
                return -1;
            }
            if (transforms.Validate(vars, processes) < 0)
            {
                Log.Error("Failed to validate transforms file '{transformsFilePath}'", transformsFilePath);
                return -1;
            }

            return 0;
        }

        // In the 'input.path' there is a 'global.config.json' file that contains the default 'settings' for each input file
        // This file is not optional, it must exist
        // TODO: Add the image scanning patterns (e.g. *.jpg, *.png, /sub/**/*.jpg, etc.) to the global.config.json file
        // TODO: We could tag transform pipelines with the image extension (default.png, default.jpg) to deal with different image types.
        vars.TryResolvePath("{input.path}/global.config.json", out var globalTextureConfigFilepath);
        if (Config.TextureSettings.ReadJson(globalTextureConfigFilepath, out var globalTextureConfig) == false)
        {
            Log.Error("Failed to read global texture config file '{globalTextureConfigFilepath}'", globalTextureConfigFilepath);
            return -1;
        }

        // For each process descriptor write 'process.{process.Name}.dep.json' in the cache folder
        processes.PrepareProcessDepFiles(vars, fileTrackerCache);

        // Glob all the image files in the input path.
        vars.TryResolvePath("{input.path}", out var inputPath);
        var inputFiles = GlobFiles(inputPath, "*.png");
        inputFiles.Sort();

        // Divide the work (inputFiles) into N jobs
        var inputFilesPerJob = inputFiles.Count / totalJobs;
        var inputFilesStart = job * inputFilesPerJob;
        var inputFilesEnd = (job + 1) * inputFilesPerJob;
        if (job == totalJobs - 1)
        {
            inputFilesEnd = inputFiles.Count;
        }

        // Iterate over all input files (images)
        // - prepare the variables (main variables merged with global and per texture settings)
        // - get the transform descriptor and construct a pipeline
        // - execute the pipeline
        var inputFilesJob = inputFiles.Skip(inputFilesStart).Take(inputFilesEnd - inputFilesStart).ToList();
        foreach (var currentInputFilePath in inputFilesJob)
        {
            // Figure out if there is a 'currentInputFilePath'.json file next to the 'currentInputFilePath' file.
            // If so we need to load/parse that JSON file and use the data in it to override the default settings.
            Config.TextureSettings.ReadJson(Path.Join(inputPath, currentInputFilePath + ".json"), out var currentTextureConfig);

            // Combine the main, global texture settings and per texture settings variables
            Vars.Vars localVars = new(vars);
            globalTextureConfig.MergeIntoVars(localVars, false);
            currentTextureConfig.MergeIntoVars(localVars, true);

            var resolved = localVars.TryResolveString("{transform}", out var transform);
            if (resolved && transforms.GetTransformByName(transform, out var transformConfig))
            {
                // Run the pipeline
                Log.Information("Running pipeline '{transform}' on \"{currentInputFilePath}\"", transform, currentInputFilePath);
                var pipeline = new Transform.Pipeline(processes, transformConfig, localVars);
                pipeline.Execute(currentInputFilePath, dryRun, fileTrackerCache);
            }
            else
            {
                // log: warning (use serilog)
                Log.Warning("Transform '{transform}' not found, skipping file \"{currentInputFilePath}\"", transform, currentInputFilePath);
            }
        }

        return 0;
    }

    public static int Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .WriteTo.File("upscale-.log", rollingInterval: RollingInterval.Day)
            .CreateLogger();

        var optionDryRun = new Option<bool>(new[] { "--dry-run" }, "Dry run, don't actually run the process, only show in the console (and log) what might be run)");
        var optionValidate = new Option<bool>(new[] { "--validate" }, "Validate some of the settings (e.g. check if paths exist and variables can be resolved)");
        var optionVars = new Option<string>(new[] { "-a", "--vars" }, "List of variables (key=value) separated by ';'");
        var optionTransforms = new Option<string>(new[] { "--transforms" }, getDefaultValue: () => "{tools.path}/transforms.config.json", "Path to the transform file (e.g. \"{tools.path}/transforms.config.json\")");
        var optionProcesses = new Option<string>(new[] { "--processes" }, getDefaultValue: () => "{tools.path}/processes.config.json", "Path to the processes file (e.g. \"{tools.path}/processes.config.json\")");

        var optionDenominator = new Option<int>(new[] { "-d", "--denominator" }, getDefaultValue: () => 1, "Work is split into N jobs, this is the denominator of the fraction of work to do");
        var optionNominator = new Option<int>(new[] { "-n", "--nominator" }, getDefaultValue: () => 0, "Work is split into N jobs, this is the nominator of the fraction of work to do");

        var rootCommand = new RootCommand
            {
                optionDryRun,
                optionValidate,
                optionVars,
                optionTransforms,
                optionProcesses,
                optionDenominator,
                optionNominator
            };

        rootCommand.SetHandler((dryRun, validate, vars, transforms, processes, denominator, nominator) =>
        {
            var result = Run(dryRun, validate, vars, transforms, processes, nominator, denominator);
            if (result == 0) return;
            Log.Error("An error occurred, exiting with code {result}", result);
            Environment.ExitCode = result;
        },
        optionDryRun, optionValidate, optionVars, optionTransforms, optionProcesses, optionDenominator, optionNominator);

        var exitCode = rootCommand.InvokeAsync(args).Result;

        Log.CloseAndFlush();
        return exitCode;
    }


    private static List<string> GlobFiles(string rootPath, string searchPattern)
    {
        List<string> filePaths = new();

        // Get all matching files in the root directory and its subdirectories
        foreach (var file in Directory.EnumerateFiles(rootPath, searchPattern, SearchOption.AllDirectories))
        {
            // Get the relative path of the file to the root path
            var relativePath = file.Replace(rootPath, "").TrimStart(Path.DirectorySeparatorChar);
            filePaths.Add(relativePath);
        }

        return filePaths;
    }
}
