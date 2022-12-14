namespace Vars;

public class Vars
{
    private readonly Dictionary<string, string> _vars = new();

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

    public bool Add(string name, string value)
    {
        if (_vars.ContainsKey(name)) return false;
        _vars.Add(name, value);
        return true;
    }

    public void Merge(Vars vars)
    {
        foreach (var v in vars._vars)
            if (_vars.ContainsKey(v.Key) == false)
                _vars.Add(v.Key, v.Value);
    }

    public void Merge(Dictionary<string, string> vars)
    {
        foreach (var v in vars)
            if (_vars.ContainsKey(v.Key) == false)
                _vars.Add(v.Key, v.Value);
    }

    public static bool ContainsVars(string text)
    {
        var ctx = new Context(text);
        var key = ctx.Search();
        return key.IsValid();
    }

    public string ResolveString(string text)
    {
        while (true)
        {
            var vars = ExtractAllVars(text);
            if (vars.Count == 0) break;

            foreach (var varName in vars)
                if (_vars.TryGetValue(varName, out var varValue))
                    text = text.Replace($"{{{varName}}}", varValue);
        }

        return text;
    }

    public static List<string> ExtractAllVars(string text)
    {
        // Extract all variables from text using 'SearchInnerMostKey' method
        var vars = new List<string>();
        var ctx = new Context(text);

        var key = ctx.Search();
        while (key.IsValid())
        {
            vars.Add(ctx.GetKey(key));
            key = ctx.Search();
        }

        return vars;
    }

    // Below are internal functions

    private struct Slice
    {
        public int Start;
        public int End;

        public bool IsValid()
        {
            return Start >= 0 && End >= 0 && Start < End;
        }
    }

    private struct Context
    {
        private readonly int[] _stack = new int[8];
        private int _stack_top;
        private int _cursor;
        private readonly string _text;

        public Context(string str)
        {
            _text = str;
            _cursor = 0;
            _stack_top = 0;
        }

        public string GetKey(Slice s)
        {
            return _text.Substring(s.Start, s.End - s.Start);
        }

        public Slice Search()
        {
            // a key is delimited by '{' and '}' and can be nested
            // e.g. "uprez.esr.model={esrgan.model.{input.filename}}"

            var key_begin = -1;
            var key_end = -1;

            while (_cursor < _text.Length)
            {
                if (_text[_cursor] == '}')
                {
                    key_end = _cursor;
                    _cursor++;
                    if (_stack_top == 0)
                        // this situation is not possible (we have a '}' without a '{')
                        return new Slice { Start = -1, End = -1 };

                    if (_stack_top > 0)
                    {
                        key_begin = _stack[--_stack_top];
                        return new Slice { Start = key_begin + 1, End = key_end };
                    }
                }
                else if (_text[_cursor] == '{')
                {
                    _stack[_stack_top++] = _cursor;
                }

                _cursor++;
            }

            if (key_begin == -1) return new Slice { Start = -1, End = -1 };

            return new Slice { Start = key_begin, End = key_end };
        }
    }
}
