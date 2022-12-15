using System.Text.Json;
using System.Text.Json.Serialization;

namespace Config
{
    public struct Paths
    {
        public string ToolsPath { get; set; }
        public string CachePath { get; set; }
        public string InputPath { get; set; }
        public string OutputPath { get; set; }
    }

    public class TextureConfig
    {
        public class ProcessConfig
        {
            [JsonPropertyName("name")]
            public string Name { get; set; }

            [JsonPropertyName("vars")]
            public Dictionary<string, string> Vars { get; set; } = new Dictionary<string, string>();
        }

        [JsonPropertyName("vars")]
        public Dictionary<string, string> Vars { get; set; } = new Dictionary<string, string>();
        [JsonPropertyName("processes")]
        public List<ProcessConfig> Processes { get; set; } = new List<ProcessConfig>();

        public TextureConfig()
        {
        }

        private static readonly TextureConfig _default = new TextureConfig();
        public static bool ReadJson(string path, out TextureConfig config)
        {
            if (File.Exists(path))
            {
                try
                {
                    var jsonString = File.ReadAllText(path);
                    config = JsonSerializer.Deserialize<TextureConfig>(jsonString);
                    return true;
                }
                catch (Exception)
                {
                }
            }
            config = _default;
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
        public string Path { get; set; }
        [JsonPropertyName("package")]
        public IReadOnlyList<string> Package { get; set; } = new List<string>();
        [JsonPropertyName("vars")]
        public IReadOnlyDictionary<string, string> Vars { get; set; } = new Dictionary<string, string>();
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

        public static bool ReadJson(string path, out Processes processes)
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
        [JsonPropertyName("processes")]
        public IReadOnlyList<TransformProcess> Processes { get; } = new List<TransformProcess>();

        [JsonPropertyName("name")]
        public string Name { get; set; }
    }

    public class Transform
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("stages")]
        public IReadOnlyList<TransformStage> Stages { get; } = new List<TransformStage>();
    }

}
