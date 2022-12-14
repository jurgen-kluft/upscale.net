using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Vars;

public class Vars
{
    private readonly Dictionary<string, string> _vars = new();

    public Vars()
    {
    }

    public Vars(Vars vars)
    {
        foreach (var item in vars._vars)
        {
            _vars.Add(item.Key, item.Value);
        }
    }
    public Vars(IReadOnlyDictionary<string,string> vars)
    {
        foreach (var item in vars)
        {
            _vars.Add(item.Key, item.Value);
        }
    }

    public Vars(string delimitedKeyValues)
    {
        foreach (var kv in delimitedKeyValues.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = kv.Split('=');
            if (parts.Length != 2) continue;
            var key = parts[0].Trim();
            var value = parts[1].Trim();
            _vars[key] = value;
        }
    }

    public bool Get(string name, out string value)
    {
        if (_vars.TryGetValue(name, out var v))
        {
            value = v;
            return true;
        }

        value = string.Empty;
        return false;
    }

    public void Add(Vars vars)
    {
        foreach (var item in vars._vars)
        {
            Add(item.Key, item.Value);
        }
    }

    public bool Add(string name, string value, bool overwrite = false)
    {
        value = Environment.ExpandEnvironmentVariables(value);

        var exists = _vars.ContainsKey(name);
        switch (overwrite)
        {
            case false when !exists:
                _vars.Add(name, value);
                break;
            case true:
                _vars[name] = value;
                break;
        }
        return !exists || overwrite;
    }

    public void Merge(Vars vars, bool overwrite = false)
    {
        foreach (var v in vars._vars)
        {
            Add(v.Key, v.Value, overwrite);
        }
    }

    public static bool ContainsVars(string text)
    {
        var ctx = new Context(text);
        var key = ctx.Search();
        return key.Valid;
    }

    public bool TryResolveString(string text, out string result)
    {
        while (true)
        {
            var vars = ExtractAllVars(text);
            if (vars.Count == 0) break;

            foreach (var varName in vars)
            {
                if (_vars.TryGetValue(varName, out var varValue))
                {
                    text = text.Replace($"{{{varName}}}", varValue);
                }
                else
                {
                    result = Environment.ExpandEnvironmentVariables(text);
                    return false;
                }
            }
        }
        result = Environment.ExpandEnvironmentVariables(text);
        return true;
    }

    public bool TryResolvePath(string path, out string result)
    {
        var ok = TryResolveString(path, out var p);
        result = Environment.ExpandEnvironmentVariables(p);
        return ok;
    }

    public string ResolvePath(string path)
    {
        var ok = TryResolveString(path, out var p);
        var result = Environment.ExpandEnvironmentVariables(p);
        return result;
    }

    private static List<string> ExtractAllVars(string text)
    {
        // Extract all '{var}' variables from text
        var vars = new List<string>();
        var ctx = new Context(text);

        var key = ctx.Search();
        while (key.Valid)
        {
            vars.Add(ctx.GetKey(key));
            key = ctx.Search();
        }
        return vars;
    }

    // Below are internal functions

    private struct Slice
    {
        public int Start, End;
        public bool Valid => Start >= 0 && End >= 0 && Start < End;
    }

    private struct Context
    {
        private readonly int[] _stack = new int[8];
        private int _stackTop;
        private int _cursor;
        private readonly string _text;

        public Context(string str)
        {
            _text = str;
            _cursor = 0;
            _stackTop = 0;
        }

        public string GetKey(Slice s)
        {
            return _text[s.Start..s.End];
        }

        public Slice Search()
        {
            while (_cursor < _text.Length)
            {
                switch (_text[_cursor])
                {
                    case '}':
                        {
                            var keyEnd = _cursor;
                            _cursor++;
                            switch (_stackTop)
                            {
                                case 0: // this situation is not possible (we have a '}' without a '{')
                                    return new Slice { Start = -1, End = -1 };
                                case > 0:
                                    var keyBegin = _stack[--_stackTop];
                                    if (keyBegin >= 0)
                                    {
                                        return new Slice { Start = keyBegin + 1, End = keyEnd };
                                    }

                                    break;
                            }

                            break;
                        }
                    case '{':
                        // invalid the outer one
                        if (_stackTop > 0)
                        {
                            _stack[_stackTop - 1] = -1;
                        }
                        _stack[_stackTop++] = _cursor;
                        break;
                }

                _cursor++;
            }

            return new Slice { Start = -1, End = -1 };
        }
    }

}
