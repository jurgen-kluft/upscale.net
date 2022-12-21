using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FileTracker;

internal class FileGroup
{
    public FileGroup(string name, Dictionary<string, string> files)
    {
        Name = name;
        Hash = "";
        Files = files;
    }

    [JsonPropertyName("name")]
    public string Name { get; set; }
    [JsonPropertyName("hash")]
    public string Hash { get; set; }
    [JsonPropertyName("files")]
    public Dictionary<string, string> Files { get; set; }
}

// FileTrackerCache
// This class is responsible for caching the computed hash of a file
public class FileTrackerCache
{
    private Dictionary<string, string> FileHashes { get; init; }
    private SHA1 HashAlgorithm { get; init; }

    public FileTrackerCache()
    {
        FileHashes = new();
        HashAlgorithm = SHA1.Create();
    }

    public string AddFile(Vars.Vars vars, string filePath, bool hashContent = false)
    {
        var resolvedFilePath = vars.ResolvePath(filePath);
        if (FileHashes.ContainsKey(resolvedFilePath))
        {
            return FileHashes[resolvedFilePath];
        }

        if (hashContent)
        {
            var hashStr = ComputeHashOfFileContent(resolvedFilePath);
            FileHashes.Add(resolvedFilePath, hashStr);
            return hashStr;
        }
        else
        {
            var hashStr = ComputeHashOfFile(resolvedFilePath);
            FileHashes.Add(resolvedFilePath, hashStr);
            return hashStr;
        }
    }

    private string ComputeHashOfFile(string filepath)
    {
        FileInfo fi = new(filepath);
        if (!fi.Exists)
        {
            return "0000000000000000000000000000000000000000";
        }

        // Compute a SHA1 hash of some of the file properties:
        // - file size
        // - file last write time
        List<byte> bytes = new(BitConverter.GetBytes(fi.Length));
        bytes.AddRange(BitConverter.GetBytes(fi.LastWriteTimeUtc.Ticks));
        var hash = HashAlgorithm.ComputeHash(bytes.ToArray());
        var hashStr = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        return hashStr;
    }

    private string ComputeHashOfFileContent(string filepath)
    {
        FileInfo fi = new(filepath);
        if (!fi.Exists)
        {
            return "0000000000000000000000000000000000000000";
        }

        // Compute a SHA1 hash of the file content
        using var stream = fi.OpenRead();
        var hash = HashAlgorithm.ComputeHash(stream);
        var hashStr = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        return hashStr;
    }
}

public class FileTrackerBuilder
{
    private Vars.Vars Vars { get; init; }
    internal Dictionary<string, FileGroup> FileGroups { get; init; }
    private FileTrackerCache FileTrackerCache { get; init; }

    private bool HashContent { get; init; }
    private SHA1 HashAlgorithm { get; init; }

    public FileTrackerBuilder(Vars.Vars vars, FileTrackerCache fileTrackerCache, bool hashContent = false)
    {
        Vars = vars;
        FileGroups = new Dictionary<string, FileGroup>();
        FileTrackerCache = fileTrackerCache;
        HashContent = hashContent;
        HashAlgorithm = SHA1.Create();
    }

    public string Add(Vars.Vars vars, string fileGroup, List<string> files)
    {
        // add a file group and hash them
        if (!FileGroups.TryGetValue(fileGroup, out var fg))
        {
            fg = new FileGroup(fileGroup, new Dictionary<string, string>());
            FileGroups[fileGroup] = fg;

            StringBuilder sb = new();
            foreach (var file in files)
            {
                var hash = FileTrackerCache.AddFile(vars, file, HashContent);
                fg.Files[file] = hash;
                sb.Append(hash);
                sb.Append(';');
            }

            // compute a hash of all the hashes
            var hashBytes = HashAlgorithm.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
            fg.Hash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            return fg.Hash;
        }
        else
        {
            // file group already exists, so just return the hash
            return fg.Hash;
        }
    }

    public void Save(string filepath)
    {
        filepath = Vars.ResolvePath(filepath);
        if (File.Exists(filepath))
        {
            File.Delete(filepath);
        }
        else
        {
            var dir = Path.GetDirectoryName(filepath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
        }

        var options = new JsonSerializerOptions(JsonSerializerDefaults.General)
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        List<FileGroup> nodes = new(FileGroups.Values);
        var jsonText = JsonSerializer.Serialize<List<FileGroup>>(nodes, options);

        using var w = new StreamWriter(filepath);
        w.Write(jsonText);
        w.Flush();
        w.Close();
    }

}

public class FileTracker
{
    private Dictionary<string, FileGroup> FileGroups { get; init; }

    private FileTracker()
    {
        FileGroups = new Dictionary<string, FileGroup>();
    }

    public bool IsIdentical(FileTrackerBuilder builder)
    {
        if (FileGroups.Count != builder.FileGroups.Count)
        {
            return false;
        }

        foreach (var (fileGroupName, fileGroup) in FileGroups)
        {
            if (!builder.FileGroups.TryGetValue(fileGroupName, out var builderFileGroup))
            {
                return false;
            }

            if (fileGroup.Hash != builderFileGroup.Hash)
            {
                return false;
            }
        }

        return true;
    }

    public bool IsIdentical(string nodeName, FileTrackerBuilder builder)
    {
        // For a specific node check if they are identical
        if (!FileGroups.TryGetValue(nodeName, out var fileGroup))
        {
            return false;
        }

        if (!builder.FileGroups.TryGetValue(nodeName, out var builderFileGroup))
        {
            return false;
        }

        return fileGroup.Hash == builderFileGroup.Hash;
    }

    public static FileTracker FromFile(string filepath, Vars.Vars vars)
    {
        var ft = new FileTracker();

        // Check if the file exists before trying to load and parse it
        filepath = vars.ResolvePath(filepath);
        if (!File.Exists(filepath))
        {
            return ft;
        }

        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.General)
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        var json = string.Empty;
        try
        {
            using var r = new StreamReader(filepath);
            json = r.ReadToEnd();
            r.Close();
        }
        catch (Exception e)
        {
            Log.Error("Error reading from file '{filepath}': {msg}", filepath, e.Message);
            return ft;
        }

        try
        {
            var nodes = JsonSerializer.Deserialize<List<FileGroup>>(json, jsonOptions);
            if (nodes != null)
            {
                foreach (var node in nodes)
                {
                    ft.FileGroups.Add(node.Name, node);
                }
            }
        }
        catch (Exception e)
        {
            Log.Error("Error parsing JSON from file '{filepath}': {msg}", filepath, e.Message);
        }

        return ft;
    }

}
