using System;
using System.Security.Cryptography;
using System.Text;

namespace FileTracker;

internal class Node
{
    public string Name { get; set; }
    public Dictionary<string, string> Items { get; set; }
    public Dictionary<string, string> Files { get; set; }
}

public struct TrackerEnv
{
    public string ToolsPath { get; set; }
    public string CachePath { get; set; }
    public string InputPath { get; set; }
    public string OutputPath { get; set; }
}

public class Tracker
{
    private Dictionary<string, string> _paths = new();
    private Dictionary<string, Node> _nodes = new();

    public Tracker(TrackerEnv env)
    {
        SetEnv(env);
    }

    public void SetEnv(TrackerEnv env)
    {
        _paths.Clear();
        _paths.Add("tools.path:", env.ToolsPath);
        _paths.Add("input.path:", env.InputPath);
        _paths.Add("cache.path:", env.CachePath);
        _paths.Add("output.path:", env.OutputPath);
    }

    public void Save(string filepath)
    {
        // Save the array of nodes to a file

    }

    public void Load(string filepath)
    {
        // Load the array of nodes from a file

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

    public void Update()
    {
        // For each node compute a hash of the files and items
        // Then update the Items["hash"] with the hash value

        foreach (var node in _nodes.Values)
        {
            var hash = new StringBuilder();

            List<string> sortedFilenames = new(node.Files.Keys);
            foreach (var file in _nodes.Keys)
            {
                sortedFilenames.Add(file);
            }
            sortedFilenames.Sort();
            foreach (var file in sortedFilenames)
            {
                var n = _nodes[file];
                hash.Append(node.Name);
                hash.Append(file);
            }

            // Note: We do not include the item "hash" here since we
            //       are recomputing it.
            List<string> sortedItems = new(node.Items.Keys);
            foreach (var item in node.Items)
            {
                if (item.Key == "hash") continue;
                sortedItems.Add(item.Key);
            }
            foreach (var item in sortedItems)
            {
                var value = node.Items[item];
                hash.Append(item);
                hash.Append(value);
            }

            // Generate a SHA1 hash from the build up string
            using var sha1 = SHA1.Create();
            var hashBytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(hash.ToString()));

            node.Items["hash"] = hash.ToString();
        }

    }

}
