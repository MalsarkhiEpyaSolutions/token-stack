using TokenStack.Core.Windows;
using Xunit;

namespace TokenStack.Tests;

public class UserPathTests
{
    [Fact]
    public void EnsureContains_Appends_WhenMissing()
    {
        var env = new FakeEnv();
        env.User["Path"] = @"C:\one;C:\two";
        Assert.True(UserPath.EnsureContains(env, @"C:\ts\rtk"));
        Assert.Equal(@"C:\one;C:\two;C:\ts\rtk", env.User["Path"]);
    }

    [Fact]
    public void EnsureContains_NoDuplicate_CaseAndTrailingSlashInsensitive()
    {
        var env = new FakeEnv();
        env.User["Path"] = @"C:\TS\RTK\;C:\two";
        Assert.False(UserPath.EnsureContains(env, @"C:\ts\rtk"));
        Assert.Equal(@"C:\TS\RTK\;C:\two", env.User["Path"]);
    }

    [Fact]
    public void EnsureContains_CreatesPath_WhenAbsent()
    {
        var env = new FakeEnv();
        Assert.True(UserPath.EnsureContains(env, @"C:\ts\rtk"));
        Assert.Equal(@"C:\ts\rtk", env.User["Path"]);
    }

    [Fact]
    public void Remove_StripsEntry_PreservingOthers()
    {
        var env = new FakeEnv();
        env.User["Path"] = @"C:\one;C:\ts\rtk;C:\two";
        Assert.True(UserPath.Remove(env, @"C:\ts\rtk\"));
        Assert.Equal(@"C:\one;C:\two", env.User["Path"]);
    }
}
