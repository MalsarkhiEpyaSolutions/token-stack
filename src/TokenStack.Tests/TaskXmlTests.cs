using TokenStack.Core.Windows;
using Xunit;

namespace TokenStack.Tests;

public class TaskXmlTests
{
    [Fact]
    public void Render_ContainsAllSpecMandatedSettings()
    {
        var xml = TaskXml.Render(
            pythonwPath: @"C:\ts\venv\Scripts\pythonw.exe",
            scriptPath: @"C:\ts\run_proxy.py",
            workingDir: @"C:\ts",
            userId: @"DOMAIN\user");

        Assert.Contains(@"<Command>C:\ts\venv\Scripts\pythonw.exe</Command>", xml);
        Assert.Contains(@"<Arguments>""C:\ts\run_proxy.py""</Arguments>", xml);
        Assert.Contains(@"<WorkingDirectory>C:\ts</WorkingDirectory>", xml);
        Assert.Contains("<LogonTrigger>", xml);                          // at logon
        Assert.Contains("<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>", xml); // no time limit
        Assert.Contains("<RestartOnFailure>", xml);
        Assert.Contains("<Count>3</Count>", xml);                        // RestartCount=3
        Assert.Contains("<Interval>PT1M</Interval>", xml);               // RestartInterval=1m
        Assert.Contains("<MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>", xml);
        Assert.Contains("<Hidden>true</Hidden>", xml);
        Assert.Contains(@"<UserId>DOMAIN\user</UserId>", xml);
        Assert.Contains("<RunLevel>LeastPrivilege</RunLevel>", xml);     // no admin
    }

    [Fact]
    public void Render_EscapesXmlSpecials()
    {
        var xml = TaskXml.Render(@"C:\a&b\pythonw.exe", @"C:\a&b\run.py", @"C:\a&b", "u");
        Assert.Contains("a&amp;b", xml);
        Assert.DoesNotContain(@"a&b\pythonw", xml);
    }
}
