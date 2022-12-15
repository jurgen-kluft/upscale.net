
namespace Transform;

internal class Stage
{
    public int _index; // The index of the stage in the pipeline
    public Config.TransformStage _config; // The stage configuration
    public Vars.Vars _inputVars; // The variables for this stage
    public List<Process> _processes; // The processes in this stage
    public Vars.Vars _outputVars; // The output variables for this stage (inputVars + all process vars)
}


internal class Process
{
    public Vars.Vars _vars; // The variables for this node/process (accumulation)
    public Config.TransformProcess _processConfig; // The process configuration

    public Process(Config.TransformProcess processConfig)
    {
        _vars = new Vars.Vars();
        _processConfig = processConfig;
    }

    public void Execute()
    {

        // Determine the validity of the command line
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
    private Config.Paths _folders;
    private Config.Processes _processes; // All the processes that are available
    private Config.Transform _transform; // The transform of this pipeline
    private Vars.Vars _vars; // The variables for this pipeline
    private Stage _stage; // The current stage of the pipeline
    private List<Stage> _stages; // The stages of the pipeline

    public Pipeline(Config.Paths folders, Config.Processes processes, Config.Transform transform, Vars.Vars vars)
    {
        _folders = folders;
        _processes = processes;
        _transform = transform;
        _vars = vars;
        _stages = new List<Stage>();
    }

    public Stage NewStage(Config.TransformStage stageConfig)
    {
        Stage stage = new Stage();
        stage._index = _stages.Count;
        stage._config = stageConfig;
        stage._inputVars = new Vars.Vars();
        stage._processes = new List<Process>();
        stage._outputVars = new Vars.Vars();

        // Add the stage to the pipeline
        _stages.Add(stage);

        return stage;
    }

    public void Execute(string fp)
    {

        // Prepare the variables for the pipeline
        _vars.Add("input.path", _folders.InputPath);
        _vars.Add("output.path", _folders.OutputPath);
        _vars.Add("cache.path", _folders.CachePath);
        _vars.Add("tools.path", _folders.ToolsPath);

        _vars.Add("transform.input", "{inputpath}/" + fp);
        _vars.Add("transform.output", "{outputpath}/" + fp);

        var fpath = Path.GetDirectoryName(fp);
        var fname = Path.GetFileName(fp);
        var fext = Path.GetExtension(fp);
        _vars.Add("transform.input.filename", fname);
        _vars.Add("transform.input.filename.ext", fext);
        _vars.Add("transform.input.subpath", fpath);

        _vars.Add("transform.output.filename", fname);
        _vars.Add("transform.output.filename.ext", fext);
        _vars.Add("transform.output.subpath", fpath);

        // Construct the pipeline stages
        for (int i = 0; i < _transform.Stages.Count; i++)
        {
            var stageConfig = _transform.Stages[i];
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
