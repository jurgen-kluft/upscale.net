using System.Text.Json;
using System.Text.Json.Serialization;

namespace Config
{
    public class Process
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }
        [JsonPropertyName("description")]
        public string Description { get; set; }
        [JsonPropertyName("path")]
        public string Path { get; set; }
        [JsonPropertyName("package")]
        public List<string> Package { get; set; } = new ();

        [JsonPropertyName("vars")] public Dictionary<string, string> Vars { get; set; } = new();
    }

    public class Processes
    {
        [JsonPropertyName("processes")]
        public List<Process> ProcessList { get; set; } = new();

        [JsonIgnore]
        public Dictionary<string, Process> ProcessMap { get; private set; }

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
        public Dictionary<string, string> Vars { get; set; } = new();

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
        public List<Transform> TransformsList { get; set; } = new ();

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
        public List<TransformProcess> Processes { get; } = new ();

        [JsonPropertyName("name")]
        public string Name { get; set; }
    }

    public class Transform
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("stages")]
        public List<TransformStage> Stages { get; } = new ();
    }

}
