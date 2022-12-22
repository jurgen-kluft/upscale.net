using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vars;
using Vars = Vars.Vars;

namespace Config
{
    public class TextureSettings
    {
        [JsonPropertyName("vars")]
        public IReadOnlyDictionary<string, string> Vars { get; set; } = new Dictionary<string, string>();

        public TextureSettings()
        {
        }

        private static readonly TextureSettings _default = new TextureSettings();
        public static bool ReadJson(string path, out TextureSettings settings)
        {
            if (File.Exists(path))
            {
                try
                {
                    var jsonString = File.ReadAllText(path);
                    settings = JsonSerializer.Deserialize<TextureSettings>(jsonString) ?? _default;
                    return true;
                }
                catch (Exception)
                {
                }
            }
            settings = _default;
            return false;
        }

        public void MergeIntoVars(global::Vars.Vars vars, bool overwrite)
        {
            foreach (var item in Vars)
            {
                vars.Add(item.Key, item.Value, overwrite);
            }
        }
    }

    public class ProcessDescriptor
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;
        [JsonPropertyName("executable")]
        public string Executable { get; set; } = string.Empty;
        [JsonPropertyName("package")]
        public IReadOnlyList<string> Package { get; set; } = new List<string>();
        [JsonPropertyName("vars")]
        public IReadOnlyDictionary<string, string> Vars { get; set; } = new Dictionary<string, string>();

        public string ProcessDepFilePath => "{cache.path}/process." + Name + ".dep.json";
        public string ProcessNodeFilePath => "{cache.path}/process." + Name + ".node.json";

        private static IEnumerable<string> Glob(string path)
        {
            // 'path' may contain a glob pattern, so we need to expand it
            // For example: 'path/to/**/*.png' will expand to all the png files in the 'path/to' folder and all subfolders

            // If the path does not contain a glob pattern, then just return the path
            if (path.Contains('*') == false)
            {
                return new List<string>() { path };
            }

            // If the path contains a glob pattern, then expand it
            var dir = Path.GetDirectoryName(path) ?? "";
            var pattern = Path.GetFileName(path) ?? "";

            // Deal with '**' in 'dir'
            var recursive = false;
            if (dir.EndsWith("**"))
            {
                recursive = true;
                dir = dir[..^2];
            }

            // Make sure the directory exists before trying to glob
            if (Directory.Exists(dir) == false)
            {
                return new List<string>();
            }

            // Enumerate all the files
            return Directory.EnumerateFiles(dir, pattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
        }

        public void ExpandPackagePaths(global::Vars.Vars vars)
        {
            var resolved = vars.TryResolvePath("{tools.path}", out var toolsPath);

            List<string> packageFiles = new();
            foreach (var relativePath in Package)
            {
                var fullPath = Path.Join(toolsPath, relativePath);
                vars.TryResolvePath(fullPath, out var resolvedFullPath);

                // Glob all files in 'fullPath'
                foreach (var filepath in Glob(resolvedFullPath))
                {
                    var relativeFilepath = filepath[(toolsPath.Length + 1)..];
                    relativeFilepath = "{tools.path}/" + relativeFilepath;
                    packageFiles.Add(relativeFilepath);
                }
            }
            Package = packageFiles;
        }
    }

    public class ProcessesDescriptor
    {
        [JsonPropertyName("processes")]
        public IReadOnlyList<ProcessDescriptor> ProcessList { get; set; } = new List<ProcessDescriptor>();

        [JsonIgnore]
        public Dictionary<string, ProcessDescriptor> ProcessMap { get; private set; } = new();

        internal bool GetProcessByName(string name, [MaybeNullWhen(false)] out ProcessDescriptor process)
        {
            return ProcessMap.TryGetValue(name, out process);
        }

        public static bool ReadJson(string path, global::Vars.Vars vars, [MaybeNullWhen(false)] out ProcessesDescriptor processesDescriptor)
        {
            if (File.Exists(path) == false)
            {
                Console.WriteLine($"ERROR: Processes file '{path}' does not exist");
                processesDescriptor = null;
                return false;
            }
            using var r = new StreamReader(path);
            var jsonString = r.ReadToEnd();
            var options = new JsonSerializerOptions(JsonSerializerDefaults.General)
            {
                PropertyNameCaseInsensitive = true
            };
            var jsonModel = JsonSerializer.Deserialize<ProcessesDescriptor>(jsonString, options);
            if (jsonModel == null)
            {
                processesDescriptor = null;
                return false;
            }

            // Build Process Map using Process.Name as the key
            jsonModel.ProcessMap = new Dictionary<string, ProcessDescriptor>();
            foreach (var process in jsonModel.ProcessList)
            {
                // Each process contains a list of 'paths' that can contain glob patterns, we need to expand those
                process.ExpandPackagePaths(vars);
                jsonModel.ProcessMap[process.Name] = process;
            }
            processesDescriptor = jsonModel;
            return true;
        }

        public int Validate(global::Vars.Vars vars)
        {
            // we can validate a couple of points:
            // - does the executable path point to a file
            // - does each process have an list of package files that exist?

            // for each process, add their variables to 'vars' since these
            // are variables that can be overriden and should be used in the
            // validation of 'TransformationsDescriptor'
            var result = 0;

            if (!vars.Get("tools.path", out var toolsPath))
            {
                Log.Error("{tools.path} is not defined as a variable on the command-line");
                result = -1;
            }

            if (!Directory.Exists(toolsPath))
            {
                Log.Error($"{{tools.path}} '{toolsPath}' does not exist");
                result = -1;
            }

            foreach (var process in ProcessList)
            {
                {
                    var resolved = vars.TryResolvePath("{tools.path}/" + process.Executable, out var executablePath);
                    // any issues are reported to Log and validation will continue
                    if (!resolved || File.Exists(executablePath) == false)
                    {
                        Log.Error("Process '{process.Name}' has a non-existing or invalid executable '{executablePath}'", process.Name, executablePath);
                        result = -1;
                    }
                }

                // check the package files
                foreach (var packageFile in process.Package)
                {
                    var resolved = vars.TryResolvePath(packageFile, out var packageFilePath);
                    if (!resolved || File.Exists(packageFilePath) == false)
                    {
                        Log.Error("Process '{process.Name}' has an non-existing or invalid package file '{packageFile}'", process.Name, packageFile);
                        result = -1;
                    }
                }

                // add the process variables to 'vars'
                foreach (var item in process.Vars)
                {
                    vars.Add(item.Key, item.Value, true);
                }
            }
            return result;
        }

        public void PrepareProcessDepFiles(global::Vars.Vars vars, FileTracker.FileTrackerCache fileTrackerCache)
        {
            // For each process write a 'process.{process.Name}.dep.json' in the cache directory containing content
            // that has an impact on change detection (e.g. the hash, executable path)
            foreach (var process in ProcessList)
            {
                // From 'cache.path' load 'processes.config.json.dep'
                var depFilename = process.ProcessDepFilePath;
                var oldTracker = FileTracker.FileTracker.FromFile(depFilename, vars);

                // Collect the names of all processes, they are the node names in the tracker
                var files = new List<string>();
                foreach (var path in process.Package)
                {
                    files.Add(path);
                }
                files.Sort();
                var newTracker = new FileTracker.FileTrackerBuilder(fileTrackerCache);
                var processNodeHash = newTracker.Add(vars, process.Name, files);

                // if the trackers are not identical then we need to update the cache
                var identical = oldTracker.IsIdentical(newTracker);
                if (identical == false)
                {
                    newTracker.Save(vars, depFilename);
                }

                var processNodeFilename = process.ProcessNodeFilePath;
                var resolvedProcessNodeFilename = vars.ResolvePath(processNodeFilename);
                if (!identical || (File.Exists(resolvedProcessNodeFilename) == false))
                {
                    var pathToMakeSureIsCreated = Path.GetDirectoryName(resolvedProcessNodeFilename);
                    if (!string.IsNullOrEmpty(pathToMakeSureIsCreated))
                    {
                        Directory.CreateDirectory(pathToMakeSureIsCreated);
                    }

                    // also save a file that can be used by this upscale execution to determine if a process has changed
                    // this file ('{cache.path}/process." + process.Name + ".node.json"') should be default contain
                    // the hash of the node.
                    using var w = new StreamWriter(resolvedProcessNodeFilename);
                    // write in JSON format
                    w.WriteLine("{");
                    w.WriteLine("    \"hash\": \"" + processNodeHash + "\",");
                    w.WriteLine("    \"exec\": \"" + process.Executable + "\"");
                    w.WriteLine("}");
                    w.Close();

                }
                fileTrackerCache.AddFile(vars, processNodeFilename, true);
            }
        }
    }

    public class TransformationProcessDescriptor
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
        [JsonPropertyName("thread")]
        public string Thread { get; set; } = "main";
        [JsonPropertyName("vars")]
        public IReadOnlyDictionary<string, string> Vars { get; set; } = new Dictionary<string, string>();
        [JsonPropertyName("process")]
        public string Process { get; set; } = string.Empty;
        [JsonPropertyName("cmdline")]
        public string Cmdline { get; set; } = string.Empty;
    }

    public class TransformationStageDescriptor
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
        [JsonPropertyName("processes")]
        public IReadOnlyList<TransformationProcessDescriptor> Processes { get; set; } = new List<TransformationProcessDescriptor>();
    }

    public class TransformationDescriptor
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("stages")]
        public IReadOnlyList<TransformationStageDescriptor> Stages { get; set; } = new List<TransformationStageDescriptor>();
    }

    public class TransformationsDescriptor
    {
        [JsonPropertyName("transforms")]
        public IReadOnlyList<TransformationDescriptor> TransformsList { get; set; } = new List<TransformationDescriptor>();

        public bool GetTransformByName(string name, [MaybeNullWhen(false)] out TransformationDescriptor transformationDescriptor)
        {
            foreach (var t in TransformsList)
            {
                if (t.Name != name) continue;
                transformationDescriptor = t;
                return true;
            }
            transformationDescriptor = null;
            return false;
        }

        public bool ReadJson(string filepath, Config.ProcessesDescriptor processes, global::Vars.Vars vars, FileTracker.FileTrackerCache fileTrackerCache)
        {
            if (File.Exists(filepath) == false)
            {
                Console.WriteLine($"ERROR: Transforms file '{filepath}' does not exist");
                return false;
            }
            using var r = new StreamReader(filepath);
            var jsonString = r.ReadToEnd();
            var options = new JsonSerializerOptions(JsonSerializerDefaults.General)
            {
                PropertyNameCaseInsensitive = true
            };
            var jsonModel = JsonSerializer.Deserialize<TransformationsDescriptor>(jsonString, options);
            if (jsonModel == null)
            {
                Log.Error("Failed to parse JSON file '{path}'", filepath);
                return false;
            }
            TransformsList = jsonModel.TransformsList;

            // for each transformation process generate a 'file' that can be used to detect changes
            // this will be file that should be used as a 'content hashing' file.

            foreach (var transform in TransformsList)
            {
                foreach (var stage in transform.Stages)
                {
                    foreach (var process in stage.Processes)
                    {
                        var filename = "{cache.path}/" + transform.Name + "." + stage.Name + "." + process.Name + ".dep";
                        var resolvedFilename = vars.ResolvePath(filename);

                        // write anything that has an impact on the process execution
                        using var w = new StreamWriter(resolvedFilename);
                        w.WriteLine(process.Process);
                        w.WriteLine(process.Cmdline);
                        foreach (var (key, value) in process.Vars)
                        {
                            w.WriteLine(key + "=" + value);
                        }
                        w.Close();

                        fileTrackerCache.AddFile(vars, filename, true);
                    }
                }
            }

            return true;
        }

        public int Validate(global::Vars.Vars globalVars, ProcessesDescriptor processes)
        {
            // Make sure that each process has a cmdline that can be resolved
            global::Vars.Vars vars = new(globalVars);
            vars.Add("transform", "default");
            vars.Add("transform.source", "texture.png");

            // See if each var in the process of a pipeline stage can be resolved without leaving any variables in the result
            // Same for the command-line
            var result = 0;
            foreach (var transform in TransformsList)
            {
                global::Vars.Vars pipelineVars = new global::Vars.Vars(vars);
                foreach (var stage in transform.Stages)
                {
                    global::Vars.Vars stageVars = new global::Vars.Vars(pipelineVars);
                    foreach (var process in stage.Processes)
                    {
                        // Does the process descriptor exist?
                        if (processes.GetProcessByName(process.Process, out var processDescriptor) == false)
                        {
                            Log.Error("Transform '{transform.Name}' stage '{stage.Name}' process '{process.Name}' references unknown process '{process.Process}'", transform.Name, stage.Name, process.Name, process.Process);
                            result = 1;
                        }

                        foreach (var item in process.Vars)
                        {
                            var resolved = stageVars.TryResolveString(item.Value, out var value);
                            if (!resolved || global::Vars.Vars.ContainsVars(value))
                            {
                                Log.Error("Transform '{transform.Name}' stage '{stage.Name}' process '{process.Name}' has a var '{item.Key}' that cannot fully be resolved '{value}'", transform.Name, stage.Name, process.Name, item.Key, value);
                                result = -1;
                            }
                            stageVars.Add(item.Key, value);
                        }

                        {
                            var resolved = stageVars.TryResolveString(process.Cmdline, out var cmdline);
                            if (!resolved || global::Vars.Vars.ContainsVars(cmdline))
                            {
                                Log.Error("Transform '{transform.Name}' stage '{stage.Name}' process '{process.Name}' has a command-line that cannot fully be resolved '{cmdline}'", transform.Name, stage.Name, process.Name, cmdline);
                                result = -1;
                            }
                        }
                    }

                    pipelineVars.Add(stageVars);
                }
            }
            return result;
        }
    }
}
