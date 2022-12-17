using Xunit;

namespace Vars;

public class VarsTests
{
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
        Assert.True(vars.Exists("a"));
        Assert.False(vars.Exists("b"));
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
        Assert.True(vars.Find("a", out var value));
        Assert.Equal("1", value);
        Assert.False(vars.Find("b", out value));
        Assert.Null(value);
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
        Assert.True(vars.Exists("a"));
        Assert.True(vars.Exists("b"));
    }

    [Fact]
    public void TestMergeDictionary()
    {
        var vars = new Vars();
        vars.Add("a", "1");
        var vars2 = new Dictionary<string, string>();
        vars2.Add("b", "2");
        vars.Merge(vars2);
        Assert.True(vars.Exists("a"));
        Assert.True(vars.Exists("b"));
    }

    [Fact]
    public void TestResolveString()
    {
        var vars = new Vars();
        vars.Add("a", "1");
        vars.Add("b", "2");
        vars.Add("c", "3");
        Assert.Equal("1", vars.ResolveString("{a}"));
        Assert.Equal("1.1", vars.ResolveString("{a}.{a}"));
        Assert.Equal("1.2", vars.ResolveString("{a}.{b}"));
        Assert.Equal("1.2.3", vars.ResolveString("{a}.{b}.{c}"));
    }

    [Fact]
    public void TestResolveStringNested()
    {
        var vars = new Vars();
        vars.Add("a", "1");
        vars.Add("b", "2");
        vars.Add("c", "b");

        Assert.Equal("1.2.2", vars.ResolveString("{a}.{b}.{{c}}"));
    }
}
