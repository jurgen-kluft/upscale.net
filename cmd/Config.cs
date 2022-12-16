using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
namespace Config
{
    public class TextureSettings
    {
        public class ProcessSettings
        {
            [JsonPropertyName("name")]
            public string Name { get; set; }

            [JsonPropertyName("vars")]
            public Dictionary<string, string> Vars { get; set; } = new Dictionary<string, string>();
        }

        [JsonPropertyName("vars")]
        public Dictionary<string, string> Vars { get; set; } = new Dictionary<string, string>();
        [JsonPropertyName("processes")]
        public List<ProcessSettings> Processes { get; set; } = new List<ProcessSettings>();

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

        public void MergeIntoProcessVars(string processName, Vars.Vars vars, bool overwrite)
        {
            foreach (var process in Processes)
            {
                if (process.Name == processName)
                {
                    foreach (var item in process.Vars)
                    {
                        vars.Add(item.Key, item.Value, overwrite);
                    }
                }
            }
        }
    }

    public class Process
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }
        [JsonPropertyName("description")]
        public string Description { get; set; }
        [JsonPropertyName("path")]
        public string ExecutablePath { get; set; }
        [JsonPropertyName("package")]
        public IReadOnlyList<string> Package { get; set; } = new List<string>();
        [JsonPropertyName("vars")]
        public IReadOnlyDictionary<string, string> Vars { get; set; } = new Dictionary<string, string>();

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
            var dir = Path.GetDirectoryName(path);
            var pattern = Path.GetFileName(path);

            // Deal with '**' in 'dir'
            bool recursive = false;
            if (dir.EndsWith("**"))
            {
                recursive = true;
                dir = dir.Substring(0, dir.Length - 2);
            }

            // Make sure the directory exists before trying to glob
            if (Directory.Exists(dir) == false)
            {
                return new List<string>();
            }

            // Enumerate all the files
            var files = Directory.EnumerateFiles(dir, pattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

            // Return the files
            return files;
        }

        public void ExpandPackagePaths(Vars.Vars vars)
        {
            List<string> packageFiles = new();
            foreach (var path in Package)
            {
                // Glob all files in 'path'
                foreach (var filepath in Glob(vars.ResolvePath(path)))
                {
                    packageFiles.Add(filepath);
                }
            }

            Package = packageFiles;
        }
    }

    public class Processes
    {
        [JsonPropertyName("processes")]
        public IReadOnlyList<Process> ProcessList { get; set; } = new List<Process>();

        [JsonIgnore]
        public Dictionary<string, Process> ProcessMap { get; private set; } = new();

        bool GetProcessByName(string name, out Process process)
        {
            return ProcessMap.TryGetValue(name, out process);
        }

        public static bool ReadJson(string path, Vars.Vars vars, out Processes processes)
        {
            if (File.Exists(path) == false)
            {
                Console.WriteLine($"ERROR: Processes file '{path}' does not exist");
                processes = null;
                return false;
            }
            using var r = new StreamReader(path);
            var jsonString = r.ReadToEnd();
            var options = new JsonSerializerOptions(JsonSerializerDefaults.General)
            {
                PropertyNameCaseInsensitive = true
            };
            var jsonModel = JsonSerializer.Deserialize<Processes>(jsonString, options);

            // Build Process Map using Process.Name as the key
            jsonModel.ProcessMap = new Dictionary<string, Process>();
            foreach (var process in jsonModel.ProcessList)
            {
                // Each process contains a list of 'paths' that can contain glob patterns, we need to expand those
                process.ExpandPackagePaths(vars);
                jsonModel.ProcessMap.Add(process.Name, process);
            }
            processes = jsonModel;
            return true;
        }

    }

    public class TransformProcess
    {
        [JsonPropertyName("thread")]
        public string Thread { get; set; }

        [JsonPropertyName("vars")]
        public IReadOnlyDictionary<string, string> Vars { get; set; } = new Dictionary<string, string>();

        [JsonPropertyName("process")]
        public string Process { get; set; }

        [JsonPropertyName("cmdline")]
        public string Cmdline { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }
    }

    public class Transforms
    {
        [JsonPropertyName("transforms")]
        public IReadOnlyList<Transform> TransformsList { get; set; } = new List<Transform>();

        public bool GetTransformByName(string name, out Transform transform)
        {
            foreach (var t in TransformsList)
            {
                if (t.Name != name) continue;
                transform = t;
                return true;
            }
            transform = null;
            return false;
        }

        public static bool ReadJson(string path, out Transforms transforms)
        {
            if (File.Exists(path) == false)
            {
                Console.WriteLine($"ERROR: Transforms file '{path}' does not exist");
                transforms = null;
                return false;
            }
            using var r = new StreamReader(path);
            var jsonString = r.ReadToEnd();
            var options = new JsonSerializerOptions(JsonSerializerDefaults.General)
            {
                PropertyNameCaseInsensitive = true
            };
            var jsonModel = JsonSerializer.Deserialize<Transforms>(jsonString, options);

            transforms = jsonModel;
            return true;
        }

    }

    public class TransformStage
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }
        [JsonPropertyName("processes")]
        public IReadOnlyList<TransformProcess> Processes { get; set; } = new List<TransformProcess>();
    }

    public class Transform
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("stages")]
        public IReadOnlyList<TransformStage> Stages { get; set; } = new List<TransformStage>();
    }

}
