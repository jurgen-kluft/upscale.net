namespace Vars;

public class Vars
{
    private readonly Dictionary<string, string> _vars = new();

    public Vars(Vars vars = null)
    {
        if (vars != null)
        {
            foreach (var item in vars._vars)
            {
                _vars.Add(item.Key, item.Value);
            }
        }
    }

    public bool Exists(string name)
    {
        return _vars.ContainsKey(name);
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

    public bool Find(string name, out string value)
    {
        if (_vars.TryGetValue(name, out value)) return true;
        value = string.Empty;
        return false;
    }

    public bool Add(string name, string value, bool overwrite = false)
    {
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

    public void Merge(IReadOnlyDictionary<string, string> vars, bool overwrite = false)
    {
        foreach (var v in vars)
        {
            Add (v.Key, v.Value, overwrite);
        }
    }

    public static bool ContainsVars(string text)
    {
        var ctx = new Context(text);
        var key = ctx.Search();
        return key.Valid;
    }

    public string ResolveString(string text)
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
            }
        }

        return text;
    }

    public string ResolvePath(string path)
    {
        var p = ResolveString(path);
        p = Environment.ExpandEnvironmentVariables(p);
        return p;
    }

    public static List<string> ExtractAllVars(string text)
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
            // a key is delimited by '{' and '}' and can be nested
            // e.g. "uprez.esr.model={esrgan.model.{input.filename}}"

            var keyBegin = -1;
            var keyEnd = -1;

            while (_cursor < _text.Length)
            {
                switch (_text[_cursor])
                {
                    case '}':
                        {
                            keyEnd = _cursor;
                            _cursor++;
                            switch (_stackTop)
                            {
                                case 0: // this situation is not possible (we have a '}' without a '{')
                                    return new Slice { Start = -1, End = -1 };
                                case > 0:
                                    keyBegin = _stack[--_stackTop];
                                    return new Slice { Start = keyBegin + 1, End = keyEnd };
                            }

                            break;
                        }
                    case '{':
                        _stack[_stackTop++] = _cursor;
                        break;
                }

                _cursor++;
            }

            return new Slice { Start = -1, End = -1 };
        }
    }
}
