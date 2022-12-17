using Serilog.Sinks.SystemConsole;
using Serilog.Sinks.File;

namespace Upscale;

// Parse command line arguments:
// - --dry-run: Dry run, don't actually run the process, only show in the console (and log what might be run)
// - --transforms FILE - Path to the transform file (e.g. "{tools.path}/transforms.config.json")
// - --processes FILE - Path to the processes file (e.g. "{tools.path}/processes.config.json")
// - -a, --vars <key=value> - List of variables separated by ;
// - -n, --nominator <nominator>: Work is split into N jobs, this is the nominator of the fraction of work to do
// - -d, --denominator <denominator>: Work is split into N jobs, this is the denominator of the fraction of work to do

class Program
{
    private static int Run(bool dryRun, string variables, string transformsFilePath, string processesFilePath, int job, int totalJobs)
    {
        Vars.Vars vars = new();
        foreach (var f in variables.Split(';'))
        {
            var kv = f.Trim().Split('=');
            if (kv.Length == 2)
            {
                var key = kv[0].Trim();
                var value = kv[1].Trim();
                vars.Add(key, value);
            }
        }

        var processesDepFilePath = processesFilePath.Replace("{tools.path}", "{cache.path}");
        processesDepFilePath = processesDepFilePath + ".dep";

        // Expand paths in the transforms and processes file paths
        transformsFilePath = vars.ResolvePath(transformsFilePath);
        processesFilePath = vars.ResolvePath(processesFilePath);
        processesDepFilePath = vars.ResolvePath(processesDepFilePath);

        // Load JSON 'process.config.json' file
        if (Config.ProcessesDescriptor.ReadJson(processesFilePath, vars, out var processes) == false)
        {
            return -1;
        }
        if (Config.TransformationsDescriptor.ReadJson(transformsFilePath, out var transforms) == false || transforms == null)
        {
            return -1;
        }

        // In the folder 'input.path' there is a 'global.config.json' file that contains the default 'settings' for each input file
        // This file is not optional, it must exist
        var globalTextureConfigFilepath = vars.ResolvePath("{input.path}/global.config.json");
        if (Config.TextureSettings.ReadJson(globalTextureConfigFilepath, out var globalTextureConfig) == false)
        {
            return -1;
        }

        // For the processes create a FileTracker and afterwards add 'process.{process name}.hash=hash' to vars
        processes.UpdateDependencyTracker(processesDepFilePath, vars);

        // Glob all the 'png' files in the input path, we should make this configurable, so we know which folders and
        // extensions to glob for images. We could put this information in '{input.path}/global.config.json'.

        var inputPath = vars.ResolvePath("{input.path}");
        var inputFiles = Directory.GetFiles(inputPath, "*.png", SearchOption.AllDirectories).ToList();
        inputFiles.Sort();

        // Divide the work (inputFiles) into N jobs
        var inputFilesPerJob = inputFiles.Count / totalJobs;
        var inputFilesStart = job * inputFilesPerJob;
        var inputFilesEnd = (job + 1) * inputFilesPerJob;
        if (job == totalJobs - 1)
        {
            inputFilesEnd = inputFiles.Count;
        }

        // for each input file, strip of the input path and add it to the 'input.files' variable
        for (var i = inputFilesStart; i < inputFilesEnd; i++)
        {
            var inputFile = inputFiles[i];
            inputFiles[i] = inputFile[(inputPath.Length + 1)..];
        }

        var inputFilesJob = inputFiles.Skip(inputFilesStart).Take(inputFilesEnd - inputFilesStart).ToList();
        foreach (var currentInputFilePath in inputFilesJob)
        {
            // Figure out if there is a 'currentInputFilePath'.json file next to the 'currentInputFilePath' file.
            // If so we need to load/parse that JSON file and use the data in it to override the default settings.
            Config.TextureSettings.ReadJson(Path.Join(inputPath, currentInputFilePath + ".json"), out var currentTextureConfig);

            Vars.Vars localVars = new(vars);
            globalTextureConfig.MergeIntoVars(localVars, false);
            currentTextureConfig.MergeIntoVars(localVars, true);

            var transform = localVars.ResolveString("{transform}");
            if (transforms.GetTransformByName(transform, out var transformConfig))
            {
                // Run the pipeline
                var pipeline = new Transform.Pipeline(processes, transformConfig, localVars);
                pipeline.Execute(currentInputFilePath, dryRun);
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
        var optionVars = new Option<string>(new[] { "-a", "--vars" }, "List of variables (key=value) separated by ';'");
        var optionTransforms = new Option<string>(new[] { "--transforms" }, getDefaultValue: () => "{tools.path}/transforms.config.json", "Path to the transform file (e.g. \"{tools.path}/transforms.config.json\")");
        var optionProcesses = new Option<string>(new[] { "--processes" }, getDefaultValue: () => "{tools.path}/processes.config.json", "Path to the processes file (e.g. \"{tools.path}/processes.config.json\")");

        var optionDenominator = new Option<int>(new[] { "-d", "--denominator" }, getDefaultValue: () => 1, "Work is split into N jobs, this is the denominator of the fraction of work to do");
        var optionNominator = new Option<int>(new[] { "-n", "--nominator" }, getDefaultValue: () => 0, "Work is split into N jobs, this is the nominator of the fraction of work to do");

        var rootCommand = new RootCommand
            {
                optionDryRun,
                optionVars,
                optionTransforms,
                optionProcesses,
                optionDenominator,
                optionNominator
            };

        rootCommand.SetHandler((dryRun, vars, transforms, processes, denominator, nominator) =>
        {
            var result = Run(dryRun, vars, transforms, processes, nominator, denominator);
            Environment.Exit(result);
        },
        optionDryRun, optionVars, optionTransforms, optionProcesses, optionDenominator, optionNominator);

        return rootCommand.InvokeAsync(args).Result;
    }
}
