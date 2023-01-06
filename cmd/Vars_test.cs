using Xunit;

namespace Vars;

public class VarsTests
{
    [Fact]
    public void TestStringConstructor()
    {
        var vars = new Vars("a=1;b=2;c=3");

        Assert.True(vars.Get("a", out var value));
        Assert.Equal("1", value);
        Assert.True(vars.Get("b", out value));
        Assert.Equal("2", value);
        Assert.True(vars.Get("c", out value));
        Assert.Equal("3", value);
    }

    [Fact]
    public void TestContainsVars()
    {
        Assert.True(Vars.ContainsVars("{a}"));
        Assert.True(Vars.ContainsVars("{a} {b}"));
        Assert.True(Vars.ContainsVars("{a} {b.{c}.d}"));
    }

    [Fact]
    public void TestExists()
    {
        var vars = new Vars();
        vars.Add("a", "1");
        Assert.True(vars.Get("a", out _));
        Assert.False(vars.Get("b", out var _));
    }

    [Fact]
    public void TestGet()
    {
        var vars = new Vars();
        vars.Add("a", "1");
        Assert.True(vars.Get("a", out var value));
        Assert.Equal("1", value);
        Assert.False(vars.Get("b", out value));
        Assert.Equal(string.Empty, value);
    }

    [Fact]
    public void TestFind()
    {
        var vars = new Vars();
        vars.Add("a", "1");
        Assert.True(vars.Get("a", out var value));
        Assert.Equal("1", value);
        Assert.False(vars.Get("b", out value));
        Assert.Equal(value, string.Empty);
    }

    [Fact]
    public void TestAdd()
    {
        var vars = new Vars();
        Assert.True(vars.Add("a", "1"));
        Assert.False(vars.Add("a", "2"));
    }

    [Fact]
    public void TestMerge()
    {
        var vars = new Vars();
        vars.Add("a", "1");
        var vars2 = new Vars();
        vars2.Add("b", "2");
        vars.Merge(vars2);
        Assert.True(vars.Get("a", out var str));
        Assert.True(vars.Get("b", out str));
    }

    [Fact]
    public void TestResolveString()
    {
        var vars = new Vars();
        vars.Add("a", "1");
        vars.Add("b", "2");
        vars.Add("c", "3");

        Assert.True(vars.TryResolveString("{a}", out var str));
        Assert.Equal("1", str);

        Assert.True(vars.TryResolveString("{a}.{a}", out str));
        Assert.Equal("1.1", str);

        Assert.True(vars.TryResolveString("{a}.{b}", out str));
        Assert.Equal("1.2", str);

        Assert.True(vars.TryResolveString("{a}.{b}.{c}", out str));
        Assert.Equal("1.2.3", str);
    }

    [Fact]
    public void TestResolveStringNested()
    {
        var vars = new Vars();
        vars.Add("a", "1");
        vars.Add("b", "2");
        vars.Add("c", "b");

        Assert.True(vars.TryResolveString("{a}.{b}.{{c}}", out var str));
        Assert.Equal("1.2.2", str);
    }

    [Fact]
    public void TestResolveStringNested2()
    {
        var vars = new Vars();
        vars.Add("a", "1");
        vars.Add("b", "2");
        vars.Add("c", "d");
        vars.Add("ddd", "3");

        Assert.True(vars.TryResolveString("{a}.{b}.{d{c}d}", out var str));
        Assert.Equal("1.2.3", str);
    }

}
