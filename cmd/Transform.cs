
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
    public Config.TransformationProcessDescriptor ProcessDescriptorConfig { get; private set; } // The process configuration

    public string Name => ProcessDescriptorConfig.Name;

    public Process(Config.TransformationProcessDescriptor processDescriptorConfig)
    {
        Vars = new Vars.Vars(processDescriptorConfig.Vars);
        ProcessDescriptorConfig = processDescriptorConfig;
    }

    public int Execute(DependencyTracker.Tracker tracker, string nodeName, bool dryRun)
    {
        bool changed = tracker.UpdateItem(nodeName, "cmdline", ProcessDescriptorConfig.Cmdline);
        changed = tracker.UpdateNode(nodeName) || changed;

        int exitCode = 0;
        if (changed)
        {
            // execute the command line
            var cmdline = Vars.ResolveString(ProcessDescriptorConfig.Cmdline);
            if (!dryRun)
            {
                var process = new System.Diagnostics.Process();
                process.StartInfo.FileName = Vars.ResolvePath(ProcessDescriptorConfig.Process);
                process.StartInfo.Arguments = $"{cmdline}";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.Start();
                process.WaitForExit();
                exitCode = process.ExitCode;
            }
        }
        return exitCode;
    }

}

internal class Pipeline
{
    private Config.ProcessesDescriptor _processesDescriptor; // All the processes that are available
    private readonly Config.TransformationDescriptor _transformationDescriptor; // The transform of this pipeline
    private readonly Vars.Vars _vars; // The variables for this pipeline
    private readonly List<Stage> _stages; // The stages of the pipeline

    public Pipeline(Config.ProcessesDescriptor processesDescriptor, Config.TransformationDescriptor transformationDescriptor, Vars.Vars vars)
    {
        _processesDescriptor = processesDescriptor;
        _transformationDescriptor = transformationDescriptor;
        _vars = vars;
        _stages = new List<Stage>();
    }

    private Stage NewStage(Config.TransformationStageDescriptor stageDescriptorConfig)
    {
        var stage = new Stage(config: stageDescriptorConfig, inputVars: new Vars.Vars(), processes: new List<Process>(), outputVars: new Vars.Vars());

        // Add the stage to the pipeline
        _stages.Add(stage);

        return stage;
    }

    public void Execute(string filePath, bool dryRun)
    {
        // Prepare the variables for the pipeline
        _vars.Add("transform.input", filePath);

        // Load the dependency file belonging to the incoming file
        var tracker = new DependencyTracker.Tracker(_vars);
        tracker.Load(filePath + ".dep");

        // Construct the pipeline stages
        for (var i = 0; i < _transformationDescriptor.Stages.Count; i++)
        {
            var stageConfig = _transformationDescriptor.Stages[i];
            var pipelineStage = NewStage(stageConfig);

            if (i == 0)
            {
                // For the first stage start by merging in the pipeline variables
                pipelineStage.InputVars.Merge(_vars);
            }
            else
            {
                // For all other stages start by merging in the output variables of the previous stage
                var previousStage = _stages[i - 1];
                pipelineStage.InputVars.Merge(previousStage.OutputVars);
            }

            // Create the processes for this stage
            foreach (var transformProcess in pipelineStage.Config.Processes)
            {
                var process = new Process(transformProcess);
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
        foreach (var stage in _stages)
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
        foreach (var stage in _stages)
        {
            foreach (var process in stage.Processes)
            {
                var nodeName = $"stage.{stage.Name}.process.{process.Name}";
                var exitCode = process.Execute(tracker, nodeName, dryRun);

                // do something with the exit code
            }
        }

        tracker.Save(filePath + ".dep");
    }
}
