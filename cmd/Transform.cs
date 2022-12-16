
namespace Transform;

internal class Stage
{
    public int _index; // The index of the stage in the pipeline
    public Config.TransformationStageDescriptor _config; // The stage configuration
    public Vars.Vars _inputVars; // The variables for this stage
    public List<Process> _processes; // The processes in this stage
    public Vars.Vars _outputVars; // The output variables for this stage (inputVars + all process vars)
}


internal class Process
{
    public Vars.Vars _vars; // The variables for this node/process (accumulation)
    public Config.TransformationProcessDescriptor ProcessDescriptorConfig; // The process configuration

    public Process(Config.TransformationProcessDescriptor processDescriptorConfig)
    {
        _vars = new Vars.Vars(processDescriptorConfig.Vars);
        ProcessDescriptorConfig = processDescriptorConfig;
    }

    public void Execute()
    {

        // Determine the validity of the command line?
        // Create a dependency node and add the input, process config hash, output and cmdline items
        // - check if the dependency node exists and if so, check if it is identical
        // - if identical, skip the process
        // - if not identical:
        //   - execute the process
        //   - update the dependency node
        // Done

    }

}

internal class Pipeline
{
    private Config.ProcessesDescriptor _processesDescriptor; // All the processes that are available
    private Config.TransformationDescriptor _transformationDescriptor; // The transform of this pipeline
    private Vars.Vars _vars; // The variables for this pipeline
    private Stage _stage; // The current stage of the pipeline
    private List<Stage> _stages; // The stages of the pipeline

    public Pipeline(Config.ProcessesDescriptor processesDescriptor, Config.TransformationDescriptor transformationDescriptor, Vars.Vars vars)
    {
        _processesDescriptor = processesDescriptor;
        _transformationDescriptor = transformationDescriptor;
        _vars = vars;
        _stages = new List<Stage>();
    }

    public Stage NewStage(Config.TransformationStageDescriptor stageDescriptorConfig)
    {
        var stage = new Stage
        {
            _index = _stages.Count,
            _config = stageDescriptorConfig,
            _inputVars = new Vars.Vars(),
            _processes = new List<Process>(),
            _outputVars = new Vars.Vars()
        };

        // Add the stage to the pipeline
        _stages.Add(stage);

        return stage;
    }

    public void Execute(string fp, bool dryrun)
    {
        // Prepare the variables for the pipeline
        _vars.Add("transform.input", fp);

        // Construct the pipeline stages
        for (int i = 0; i < _transformationDescriptor.Stages.Count; i++)
        {
            var stageConfig = _transformationDescriptor.Stages[i];
            var pipelineStage = NewStage(stageConfig);

            if (i == 0)
            {
                // For the first stage start by merging in the pipeline variables
                pipelineStage._inputVars.Merge(_vars);
            }
            else
            {
                // For all other stages start by merging in the output variables of the previous stage
                var previousStage = _stages[i - 1];
                pipelineStage._inputVars.Merge(previousStage._outputVars);
            }

            // Create the processes for this stage
            foreach (var transformProcess in pipelineStage._config.Processes)
            {
                var process = new Process(transformProcess);
                process._vars.Merge(pipelineStage._inputVars);
                pipelineStage._processes.Add(process);
            }

            // Collect the outputVars from the processes
            foreach (var process in pipelineStage._processes)
            {
                pipelineStage._outputVars.Merge(process._vars);
            }
        }

        // Execute the pipeline
        foreach (var stage in _stages)
        {
            foreach (var process in stage._processes)
            {
                process.Execute();
            }
        }
    }
}
