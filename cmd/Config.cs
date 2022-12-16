using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
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
                    settings = JsonSerializer.Deserialize<TextureSettings>(jsonString);
                    return true;
                }
                catch (Exception)
                {
                }
            }
            settings = _default;
            return false;
        }

        public void MergeIntoVars(Vars.Vars vars, bool overwrite)
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
        [JsonPropertyName("path")]
        public string ExecutablePath { get; set; } = string.Empty;
        [JsonPropertyName("package")]
        public IReadOnlyList<string> Package { get; set; } = new List<string>();
        [JsonPropertyName("vars")]
        public IReadOnlyDictionary<string, string> Vars { get; set; } = new Dictionary<string, string>();

        private static IEnumerable<string> Glob( string path)
        {
            // 'path' may contain a glob pattern, so we need to expand it
            // For example: 'path/to/**/*.png' will expand to all the png files in the 'path/to' folder and all subfolders

            // If the path does not contain a glob pattern, then just return the path
            if (path.Contains('*') == false)
            {
                return new List<string>() { path };
            }

            // If the path contains a glob pattern, then expand it
            var dir = Path.GetDirectoryName(path);
            var pattern = Path.GetFileName(path);

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

        public void ExpandPackagePaths(Vars.Vars vars)
        {
            var toolsPath = vars.ResolvePath("{tools.path}");

            List<string> packageFiles = new();
            foreach (var relativePath in Package)
            {
                var fullPath = Path.Join(toolsPath, relativePath);
                fullPath = vars.ResolvePath(fullPath);

                // Glob all files in 'fullPath'
                foreach (var filepath in Glob(fullPath))
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

        bool GetProcessByName(string name, out ProcessDescriptor processDescriptor)
        {
            return ProcessMap.TryGetValue(name, out processDescriptor);
        }

        public static bool ReadJson(string path, Vars.Vars vars, out ProcessesDescriptor processesDescriptor)
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

            // Build Process Map using Process.Name as the key
            jsonModel.ProcessMap = new Dictionary<string, ProcessDescriptor>();
            foreach (var process in jsonModel.ProcessList)
            {
                // Each process contains a list of 'paths' that can contain glob patterns, we need to expand those
                process.ExpandPackagePaths(vars);
                jsonModel.ProcessMap.Add(process.Name, process);
            }
            processesDescriptor = jsonModel;
            return true;
        }

        public void ObtainHashes(string filepath, Vars.Vars vars)
        {
            // From 'cache.path' load 'processes.config.json.dep'
            var tracker = new FileTracker.Tracker(vars);
            tracker.Load(filepath);
            // Collect the names of all processes, they are the node names in the tracker
            var processNames = new HashSet<string>();
            foreach (var process in ProcessList)
            {
                processNames.Add(process.Name);
            }
            tracker.SetNodes(processNames);
            // Collect the files of each process and set them on the tracker as the node's dependencies
            foreach (var process in ProcessList)
            {
                var files = new HashSet<string>();
                foreach (var path in process.Package)
                {
                    files.Add(path);
                }
                tracker.SetFiles(process.Name, files);
            }
            // Now update the tracker, it will compute the hash of each node
            Dictionary<string, string> nodeHashes = new();
            tracker.Update(nodeHashes);
            // Add these node hashes as vars
            foreach (var item in nodeHashes)
            {
                vars.Add($"process.{item.Key}.hash", item.Value, true);
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

    public class TransformationsDescriptor
    {
        [JsonPropertyName("transforms")]
        public IReadOnlyList<TransformationDescriptor> TransformsList { get; set; } = new List<TransformationDescriptor>();

        public bool GetTransformByName(string name, out TransformationDescriptor transformationDescriptor)
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

        public static bool ReadJson(string path, out TransformationsDescriptor transformationsDescriptor)
        {
            if (File.Exists(path) == false)
            {
                Console.WriteLine($"ERROR: Transforms file '{path}' does not exist");
                transformationsDescriptor = null;
                return false;
            }
            using var r = new StreamReader(path);
            var jsonString = r.ReadToEnd();
            var options = new JsonSerializerOptions(JsonSerializerDefaults.General)
            {
                PropertyNameCaseInsensitive = true
            };
            var jsonModel = JsonSerializer.Deserialize<TransformationsDescriptor>(jsonString, options);

            transformationsDescriptor = jsonModel;
            return true;
        }
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

}
