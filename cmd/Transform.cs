using Config;

namespace Transform;

internal class Stage
{
    public TransformationStageDescriptor Config { get; init; } // The stage configuration
    public Vars.Vars InputVars { get; init; } // The variables for this stage
    public List<Process> Processes { get; init; } // The processes in this stage
    public Vars.Vars OutputVars { get; init; } // The output variables for this stage (inputVars + all process vars)
    public string Name => Config.Name;

    public Stage(TransformationStageDescriptor config, Vars.Vars inputVars)
    {
        Config = config;
        InputVars = inputVars;
        Processes = new List<Process>();
        OutputVars = new Vars.Vars();
    }

    public Stage(TransformationStageDescriptor config, Vars.Vars inputVars, List<Process> processes, Vars.Vars outputVars)
    {
        Config = config;
        InputVars = inputVars;
        Processes = processes;
        OutputVars = outputVars;
    }
}

internal class Process
{
    public Vars.Vars Vars { get; private set; } // The variables for this node/process (accumulation)
    public TransformationProcessDescriptor TransformProcessDescriptorConfig { get; set; } // The process configuration
    private ProcessDescriptor ProcessDescriptor { get; set; } // The process descriptor

    public string Name => TransformProcessDescriptorConfig.Name;
    public string ProcessNodeFilePath => ProcessDescriptor.ProcessNodeFilePath;

    public Process(TransformationProcessDescriptor transformProcessDescriptorConfig, ProcessDescriptor processDescriptor)
    {
        Vars = new Vars.Vars(transformProcessDescriptorConfig.Vars);
        TransformProcessDescriptorConfig = transformProcessDescriptorConfig;
        ProcessDescriptor = processDescriptor;
    }

    public int Execute(bool changed, string stageName, bool dryRun)
    {
        if (!changed)
        {
            Log.Information("Skipping process '{process}' of \"{stage}\" since no changes were detected", Name, stageName);
            return 0;
        }

        // execute the command line
        if (!Vars.TryResolveString(TransformProcessDescriptorConfig.Cmdline, out var cmdline))
        {
            Log.Error("Unable to resolve the command-line for process '{Name}' ('{cmdline}')", Name, cmdline);
            return -1;
        }

        if (dryRun)
        {
            Log.Information("Running process '{Name}' of \"{stageName}\" completed (dry-run)", Name, stageName);
            return 0;
        }

        if (ProcessDescriptor == null)
        {
            Log.Error("Process descriptor for process name '{Name}' not found", Name);
            return -1;
        }

        var process = new System.Diagnostics.Process();
        Vars.TryResolvePath(ProcessDescriptor.Executable, out var processFilename);
        process.StartInfo.FileName = processFilename;
        process.StartInfo.Arguments = $"{cmdline}";
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.Start();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            Log.Error("Running process '{Name}' of \"{stageName}\" failed with exit code {process.ExitCode}", Name, stageName, process.ExitCode);
            return process.ExitCode;
        }
        Log.Information("Running process '{Name}' of \"{stageName}\" completed", Name, stageName);
        return 0;
    }
}

internal class Pipeline
{
    private ProcessesDescriptor ProcessesDescriptor { get; init; } // All the processes that are available
    private TransformationDescriptor TransformationDescriptor { get; init; } // The transform of this pipeline
    private Vars.Vars Vars { get; init; } // The variables for this pipeline
    private List<Stage> Stages { get; set; } // The stages of the pipeline

    private string GetNodeName(Stage stage, Process process)
    {
        return TransformationDescriptor.Name + "." + stage.Name + "." + process.Name;
    }

    public Pipeline(ProcessesDescriptor processesDescriptor, TransformationDescriptor transformationDescriptor, Vars.Vars vars)
    {
        ProcessesDescriptor = processesDescriptor;
        TransformationDescriptor = transformationDescriptor;
        Vars = vars;
        Stages = new List<Stage>();
    }

    private Stage NewStage(TransformationStageDescriptor stageDescriptorConfig)
    {
        var stage = new Stage(config: stageDescriptorConfig, inputVars: new Vars.Vars(), processes: new List<Process>(), outputVars: new Vars.Vars());
        Stages.Add(stage);
        return stage;
    }

    public int Execute(string filePath, bool dryRun, FileTracker.FileTrackerCache fileTrackerCache)
    {
        // Prepare the variables for the pipeline
        Vars.Add("transform.source", filePath);

        // Load the file tracker for the incoming file
        var oldTracker = FileTracker.FileTracker.FromFile("{cache.path}/" + filePath + ".dep.json", Vars);

        // Construct the pipeline stages
        for (var i = 0; i < TransformationDescriptor.Stages.Count; i++)
        {
            var stageConfig = TransformationDescriptor.Stages[i];
            var pipelineStage = NewStage(stageConfig);

            if (i == 0)
            {
                // For the first stage start by merging in the pipeline variables
                pipelineStage.InputVars.Merge(Vars);
            }
            else
            {
                // For all other stages start by merging in the output variables of the previous stage
                var previousStage = Stages[i - 1];
                pipelineStage.InputVars.Merge(previousStage.OutputVars);
            }

            // Create the processes for this stage
            foreach (var transformProcess in pipelineStage.Config.Processes)
            {
                if (ProcessesDescriptor.GetProcessByName(transformProcess.Process, out var processDescriptor))
                {
                    var process = new Process(transformProcess, processDescriptor);
                    process.Vars.Merge(pipelineStage.InputVars);
                    pipelineStage.Processes.Add(process);
                }
                else
                {
                    // log an error
                }
            }

            // Collect the outputVars from the processes
            foreach (var process in pipelineStage.Processes)
            {
                pipelineStage.OutputVars.Merge(process.Vars);
            }
        }

        // Collect all the 'stage.processName' strings, they serve as node names in the dependency tracker
        // For each node of the dependency tracker (process), collect all the input and output files
        // Build a new tracker
        var newTracker = new FileTracker.FileTrackerBuilder(fileTrackerCache);
        foreach (var stage in Stages)
        {
            foreach (var process in stage.Processes)
            {
                var nodeName = GetNodeName(stage, process);

                // This is the dependency file for this process
                var processNodeFilename = process.ProcessNodeFilePath;

                // Collect the files that are used as input and output for this process
                var unique = new HashSet<string>();
                var files = new List<string>() { processNodeFilename };

                // Each process has defined its input and output files in 'vars', all of them should end in '.input' or '.output'
                foreach (var (key, value) in process.TransformProcessDescriptorConfig.Vars)
                {
                    if (key.EndsWith(".input") || key.EndsWith(".output"))
                    {
                        var resolved = Vars.ResolvePath(value);
                        if (unique.Contains(resolved))
                        {
                            continue;
                        }
                        files.Add(value);
                        unique.Add(resolved);
                    }
                }

                newTracker.Add(process.Vars, nodeName, files);
            }
        }

        // Execute each stage in the pipeline and its processes
        var exitCode = 0;
        foreach (var stage in Stages)
        {
            foreach (var process in stage.Processes)
            {
                var nodeName = GetNodeName(stage, process);
                var identical = oldTracker.IsIdentical(nodeName, newTracker);
                var result = process.Execute(!identical, stage.Name, dryRun);
                if (result != 0)
                {
                    Log.Error("Error executing process '{process.Name}' of stage '{stage.Name}' for '{filePath}'", process.Name, stage.Name, filePath);
                    exitCode = result;
                    break;
                }
            }
            if (exitCode != 0)
            {
                // Since there was an error, stop executing any further stages
                break;
            }
        }

        Log.Information("Saving dependency file for '{filePath}'", filePath);
        newTracker.Save(Vars, "{cache.path}/" + filePath + ".dep.json");

        return exitCode;
    }
}
