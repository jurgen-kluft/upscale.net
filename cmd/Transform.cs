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
    private TransformationProcessDescriptor TransformationProcessDescriptorConfig { get; set; } // The process configuration
    private ProcessDescriptor ProcessDescriptor { get; set; } // The process descriptor

    public string Name => TransformationProcessDescriptorConfig.Name;

    public Process(TransformationProcessDescriptor processDescriptorConfig, ProcessesDescriptor processes)
    {
        Vars = new Vars.Vars(processDescriptorConfig.Vars);
        TransformationProcessDescriptorConfig = processDescriptorConfig;
        ProcessDescriptor = processes.GetProcessByName(Name);
    }

    public int Execute(DependencyTracker.Tracker tracker, string nodeName, bool dryRun)
    {
        var changed = tracker.UpdateItem(nodeName, "cmdline", TransformationProcessDescriptorConfig.Cmdline);
        changed = tracker.UpdateNode(nodeName) || changed;

        var exitCode = 0;
        if (changed)
        {
            // execute the command line
            var cmdline = Vars.ResolveString(TransformationProcessDescriptorConfig.Cmdline);
            if (!dryRun)
            {
                var process = new System.Diagnostics.Process();
                process.StartInfo.FileName = Vars.ResolvePath(ProcessDescriptor.Executable);
                process.StartInfo.Arguments = $"{cmdline}";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.Start();
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    Log.Error("Running process '{process}' of \"{node}\" failed with exit code {exitCode}", Name, nodeName, process.ExitCode);
                    exitCode = process.ExitCode;
                }
                else
                {
                   Log.Information("Running process '{process}' of \"{node}\" completed", Name, nodeName);
                }
            }
            else
            {
                Log.Information("Running process '{process}' of \"{node}\" completed (dry-run)", Name, nodeName);
            }
        }
        else
        {
            Log.Information("Skipping process '{process}' of \"{node}\" since no changes were detected", Name, nodeName);
        }
        return exitCode;
    }
}

internal class Pipeline
{
    private ProcessesDescriptor ProcessesDescriptor { get; init; } // All the processes that are available
    private TransformationDescriptor TransformationDescriptor { get; init; } // The transform of this pipeline
    private Vars.Vars Vars { get; init; } // The variables for this pipeline
    private List<Stage> Stages { get; set; } // The stages of the pipeline

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

    public int Execute(string filePath, bool dryRun)
    {
        // Prepare the variables for the pipeline
        Vars.Add("transform.input", filePath);

        // Load the dependency file belonging to the incoming file
        var tracker = new DependencyTracker.Tracker(Vars);
        tracker.Load(filePath + ".dep");

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
                var process = new Process(transformProcess, ProcessesDescriptor);
                process.Vars.Merge(pipelineStage.InputVars);
                pipelineStage.Processes.Add(process);
            }

            // Collect the outputVars from the processes
            foreach (var process in pipelineStage.Processes)
            {
                pipelineStage.OutputVars.Merge(process.Vars);
            }
        }
        // Collect all the 'stage.processName' strings, they serve as node names in the dependency tracker
        // For each node of the dependency tracker (process), collect all the input and output files
        var nodeNames = new HashSet<string>();
        Dictionary<string, HashSet<string>> nodeFiles = new();
        foreach (var stage in Stages)
        {
            foreach (var process in stage.Processes)
            {
                var nodeName = $"stage.{stage.Name}.process.{process.Name}";
                nodeNames.Add(nodeName);

                // Collect the files that are used as input and output for this process
                var files = new HashSet<string>();
                process.Vars.GetInputs(files);
                process.Vars.GetOutputs(files);
                nodeFiles.Add(nodeName, files);
            }
        }

        // Register the node names with the dependency tracker
        tracker.SetNodes(nodeNames);
        foreach (var (nodeName, files) in nodeFiles)
        {
            tracker.SetFiles(nodeName, files);
        }

        // Execute each stage in the pipeline and its processes
        // TODO: parallelize this using process.ProcessDescriptorConfig.Thread to separate the process into 'threads'
        var exitCode = 0;
        foreach (var stage in Stages)
        {
            foreach (var process in stage.Processes)
            {
                var nodeName = $"stage.{stage.Name}.process.{process.Name}";
                var result = process.Execute(tracker, nodeName, dryRun);
                if (result != 0)
                {
                    Log.Error("Error executing process '{process}' of stage '{stage}' for '{filePath}'", process.Name, stage.Name, filePath);
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
        tracker.Save(filePath + ".dep");

        return exitCode;
    }
}
