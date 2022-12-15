using System;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

namespace FileTracker;

internal class Node
{
    public string Name { get; set; }
    public Dictionary<string, string> Items { get; set; }
    public Dictionary<string, string> Files { get; set; }
}

public class Tracker
{
    private Dictionary<string, string> _paths = new();
    private Dictionary<string, Node> _nodes = new();

    private bool _hashContent = false;
    private static SHA1 _hashAlgorithm;

    public Tracker(Config.Paths paths, bool hashContent = false)
    {
        SetPaths(paths);
        _hashContent = hashContent;
        _hashAlgorithm = SHA1.Create();
    }

    private void SetPaths(Config.Paths paths)
    {
        _paths.Clear();
        _paths.Add("{tools.path}", paths.ToolsPath);
        _paths.Add("{input.path}", paths.InputPath);
        _paths.Add("{cache.path}", paths.CachePath);
        _paths.Add("{output.path}", paths.OutputPath);
    }

    private static string IncreaseIndent(string indent)
    {
        return string.Concat(indent, "    ");
    }
    private static string DecreaseIndent(string indent)
    {
        return indent.Substring(0, indent.Length - 4);
    }

    public void Save(string filepath)
    {
        if (File.Exists(filepath))
        {
            File.Delete(filepath);
        }

        using var w = new StreamWriter(filepath);
        string indent = "    ";
        w.WriteLine("{");
        w.WriteLine($"{indent}\"nodes\": [");
        indent = IncreaseIndent(indent);
        foreach (var node in _nodes.Values)
        {
            w.WriteLine($"{indent}{{");
            indent = IncreaseIndent(indent);
            w.WriteLine($"{indent}\"name\": \"{node.Name}\",");
            w.WriteLine($"{indent}\"items\": [");
            indent = IncreaseIndent(indent);
            foreach (var item in node.Items)
            {
                w.WriteLine($"{indent}{{");
                indent = IncreaseIndent(indent);
                w.WriteLine($"{indent}\"name\": \"{item.Key}\",");
                w.WriteLine($"{indent}\"hash\": \"{item.Value}\"");
                indent = DecreaseIndent(indent);
                w.WriteLine($"{indent}}},");
            }
            indent = DecreaseIndent(indent);
            w.WriteLine($"{indent}],");
            w.WriteLine($"{indent}\"files\": [");
            indent = IncreaseIndent(indent);
            foreach (var file in node.Files)
            {
                w.WriteLine($"{indent}{{");
                indent = IncreaseIndent(indent);
                w.WriteLine($"{indent}\"name\": \"{file.Key}\",");
                w.WriteLine($"{indent}\"hash\": \"{file.Value}\"");
                indent = DecreaseIndent(indent);
                w.WriteLine($"{indent}}},");
            }
            indent = DecreaseIndent(indent);
            w.WriteLine($"{indent}]");
            indent = DecreaseIndent(indent);
            w.WriteLine($"{indent}}},");
        }
        indent = DecreaseIndent(indent);
        w.WriteLine($"{indent}]");
        w.WriteLine("}");

        w.Flush();
        w.Close();     
    }

    public void Load(string filepath)
    {
        // Check if the file exists before trying to load and parse it
        if (!File.Exists(filepath))
        {
            return;
        }

        // Load the array of nodes from a file using System.Text.Json
        using var r = new StreamReader(filepath);
        var json = r.ReadToEnd();
        var nodes = JsonSerializer.Deserialize<List<Node>>(json);
        _nodes.Clear();
        foreach (var node in nodes)
        {
            _nodes.Add(node.Name, node);
        }
        r.Close();
    }

    internal Node FindNode(string name)
    {
        if (_nodes.ContainsKey(name))
        {
            return _nodes[name];
        }
        return null;
    }

    internal Node GetOrCreateNode(string name)
    {
        if (!_nodes.TryGetValue(name, out var node))
        {
            node = new Node
            {
                Name = name,
                Items = new Dictionary<string, string>(),
                Files = new Dictionary<string, string>()
            };
            _nodes.Add(name, node);
        }
        return node;
    }

    public void SetNodes(HashSet<string> nodes)
    {
        var keys = new List<string>(_nodes.Keys);
        foreach (var key in keys)
        {
            if (!nodes.Contains(key))
            {
                _nodes.Remove(key);
            }
        }
        foreach (var key in nodes)
        {
            if (!_nodes.ContainsKey(key))
            {
                _nodes.Add(key, new Node
                {
                    Name = key,
                    Items = new Dictionary<string, string>(),
                    Files = new Dictionary<string, string>()
                });
            }
        }
    }

    private string ExpandVars(string filepath)
    {
        foreach(var e in _paths)
        {
            if (filepath.StartsWith(e.Key))
            {
                return string.Concat(e.Value, filepath.AsSpan(e.Key.Length));
            }
        }
        return filepath;
    }

    public void SetFiles(string node, HashSet<string> files)
    {
        HashSet<string> newFiles = new(files.Count);
        foreach(var file in files)
        {
            string newFile = ExpandVars(file);
            newFiles.Add(newFile);
        }

        var n = GetOrCreateNode(node);
        var keys = new List<string>(n.Files.Keys);
        foreach (var key in keys)
        {
            if (!newFiles.Contains(key))
            {
                n.Files.Remove(key);
            }
        }
        foreach (var key in newFiles)
        {
            if (!n.Files.ContainsKey(key))
            {
                n.Files.Add(key, key);
            }
        }
    }

    private static string ComputeHashOfFile(string filepath)
    {
        // Compute a SHA1 hash of some of the file properties:
        // - file size
        // - file last write time

        FileInfo fi = new(filepath);
        if (!fi.Exists)
        {
            return "0000000000000000000000000000000000000000";
        }

        var bytes = Encoding.UTF8.GetBytes(string.Concat(fi.Length, fi.LastWriteTimeUtc));
        var hash = _hashAlgorithm.ComputeHash(bytes);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    private static string ComputeHashOfFileContent(string filepath)
    {
        // Compute a SHA1 hash of the file content
        FileInfo fi = new(filepath);
        if (!fi.Exists)
        {
            return "0000000000000000000000000000000000000000";
        }

        using (var stream = File.OpenRead(filepath))
        {
            var hash = _hashAlgorithm.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }

    // Update; update all the nodes and its files and items and return the list of node (names) that have changed
    public List<string> Update()
    {
        // For each node compute a hash of the files and items
        // Then update the Items["hash"] with the hash value

        List<string> changedNodes = new();

        foreach (var node in _nodes.Values)
        {
            var nodeHash = new StringBuilder();

            List<string> sortedFilenames = new(node.Files.Keys);
            foreach (var file in node.Files)
            {
                sortedFilenames.Add(file.Key);
            }
            sortedFilenames.Sort();
            foreach (var file in sortedFilenames)
            {
                if (_hashContent)
                {
                    string contentHash = ComputeHashOfFileContent(file);
                }
                else
                {
                    string propertiesHash = ComputeHashOfFile(file);
                }
            }

            foreach (var fileName in sortedFilenames)
            {
                var fileHash = node.Files[fileName];
                nodeHash.Append(fileName);
                nodeHash.Append(fileHash);
            }

            // Note: We do not include the item "node.hash" here since we are recomputing it.
            List<string> sortedItems = new(node.Items.Keys);
            foreach (var item in node.Items)
            {
                if (item.Key == "node.hash") continue;
                sortedItems.Add(item.Key);
            }
            foreach (var item in sortedItems)
            {
                var value = node.Items[item];
                nodeHash.Append(item);
                nodeHash.Append(value);
            }

            // Generate a SHA1 hash from the build up string
            using var sha1 = SHA1.Create();
            var hashBytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(nodeHash.ToString()));

            if (!node.Items.TryGetValue("node.hash", out string oldNodeHashStr))
            {
               oldNodeHashStr = "0000000000000000000000000000000000000000";
            }
            string newNodeHashStr = nodeHash.ToString();
            if (newNodeHashStr != oldNodeHashStr)
            {
                node.Items["node.hash"] = newNodeHashStr;
                changedNodes.Add(node.Name);
            }
        }
        return changedNodes;
    }
}
