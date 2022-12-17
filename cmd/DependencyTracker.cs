namespace DependencyTracker;

internal class Node
{
    public Node(string name, Dictionary<string, string> items, Dictionary<string, string> files)
    {
        Name = name;
        Items = items;
        Files = files;
    }

    public string Name { get; init; }
    public Dictionary<string, string> Items { get; init; }
    public Dictionary<string, string> Files { get; init; }
}

public class Tracker
{
    private Vars.Vars Vars { get; init; }
    private Dictionary<string, Node> Nodes { get; init; }

    private bool HashContent { get; init; } = false;

    public Tracker(Vars.Vars vars, bool hashContent = false)
    {
        Vars = new Vars.Vars(vars);
        Nodes = new Dictionary<string, Node>();
        HashContent = hashContent;
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
        var indent = "    ";
        w.WriteLine("{");
        w.WriteLine($"{indent}\"nodes\": [");
        indent = IncreaseIndent(indent);
        foreach (var node in Nodes.Values)
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
        Nodes.Clear();

        // Check if the file exists before trying to load and parse it
        if (!File.Exists(filepath))
        {
            return;
        }

        // Load the array of nodes from a file using System.Text.Json
        using var r = new StreamReader(filepath);
        var json = r.ReadToEnd();
        var nodes = JsonSerializer.Deserialize<List<Node>>(json);
        if (nodes != null)
        {
            foreach (var node in nodes)
            {
                Nodes.Add(node.Name, node);
            }
        }
        r.Close();
    }

    private bool FindNode(string name, out Node? node)
    {
        if (Nodes.ContainsKey(name))
        {
            node =  Nodes[name];
            return true;
        }

        node = null;
        return false;
    }

    private Node GetOrCreateNode(string name)
    {
        if (!Nodes.TryGetValue(name, out var node))
        {
            node = new Node(name: name, items: new Dictionary<string, string>(), files: new Dictionary<string, string>());
            Nodes.Add(name, node);
        }
        return node;
    }

    public void SetNodes(HashSet<string> nodes)
    {
        var keys = new List<string>(Nodes.Keys);
        foreach (var key in keys)
        {
            if (!nodes.Contains(key))
            {
                Nodes.Remove(key);
            }
        }
        foreach (var key in nodes)
        {
            if (!Nodes.ContainsKey(key))
            {
                Nodes.Add(key, new Node(name: key, items: new Dictionary<string, string>(), files: new Dictionary<string, string>()));
            }
        }
    }

    public void SetFiles(string node, HashSet<string> files)
    {
        var n = GetOrCreateNode(node);
        var keys = new List<string>(n.Files.Keys);
        foreach (var key in keys)
        {
            if (!files.Contains(key))
            {
                n.Files.Remove(key);
            }
        }
        foreach (var key in files)
        {
            if (!n.Files.ContainsKey(key))
            {
                n.Files.Add(key, "0000000000000000000000000000000000000000");
            }
        }
    }

    public bool UpdateItem(string node, string item, string value)
    {
        var n = GetOrCreateNode(node);
        if (n.Items.ContainsKey(item))
        {
            var oldItem = n.Items[item];
            n.Items[item] = value;
            return oldItem != value;
        }
        else
        {
            n.Items.Add(item, value);
            return true;
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

        var hashAlgorithm = SHA1.Create();
        List<byte> bytes = new(BitConverter.GetBytes(fi.Length));
        bytes.AddRange(BitConverter.GetBytes(fi.LastWriteTimeUtc.Ticks));
        var hash = hashAlgorithm.ComputeHash(bytes.ToArray());
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
            var hashAlgorithm = SHA1.Create();
            var hash = hashAlgorithm.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }

    public bool UpdateNode(string nodeName)
    {
        if (!FindNode(nodeName, out var node))
        {
            return false;
        }

        if (node == null) return false;

        var nodeHash = new StringBuilder();

        List<string> sortedFilePaths = new(node.Files.Keys);
        sortedFilePaths.Sort();
        foreach (var filePath in sortedFilePaths)
        {
            var fullFilePath = Vars.ResolvePath(filePath);
            if (HashContent)
            {
                var contentHash = ComputeHashOfFileContent(fullFilePath);
                node.Files[filePath] = contentHash;
            }
            else
            {
                var propertiesHash = ComputeHashOfFile(fullFilePath);
                node.Files[filePath] = propertiesHash;
            }
        }

        foreach (var filePath in sortedFilePaths)
        {
            var fileHash = node.Files[filePath];
            nodeHash.Append(filePath);
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

        if (!node.Items.TryGetValue("node.hash", out var oldNodeHashStr))
        {
            oldNodeHashStr = "0000000000000000000000000000000000000000";
        }
        var newNodeHashStr = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        var changed = oldNodeHashStr != newNodeHashStr;
        node.Items["node.hash"] = newNodeHashStr;
        return changed;

    }
    // Update; update all the nodes and its files and items and return the list of node (names) that have changed
    public void Update(Dictionary<string, string> nodeHashes)
    {
        // For each node compute a hash of the files and items
        // Then update the Items["hash"] with the hash value
        foreach (var node in Nodes.Values)
        {
            var nodeHash = new StringBuilder();

            List<string> sortedFilePaths = new(node.Files.Keys);
            sortedFilePaths.Sort();
            foreach (var filePath in sortedFilePaths)
            {
                var fullFilePath = Vars.ResolvePath(filePath);
                if (HashContent)
                {
                    var contentHash = ComputeHashOfFileContent(fullFilePath);
                    node.Files[filePath] = contentHash;
                }
                else
                {
                    var propertiesHash = ComputeHashOfFile(fullFilePath);
                    node.Files[filePath] = propertiesHash;
                }
            }

            foreach (var filePath in sortedFilePaths)
            {
                var fileHash = node.Files[filePath];
                nodeHash.Append(filePath);
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

            if (!node.Items.TryGetValue("node.hash", out var oldNodeHashStr))
            {
                oldNodeHashStr = "0000000000000000000000000000000000000000";
            }
            var newNodeHashStr = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            node.Items["node.hash"] = newNodeHashStr;
            nodeHashes.Add(node.Name, newNodeHashStr);
        }
    }
}
