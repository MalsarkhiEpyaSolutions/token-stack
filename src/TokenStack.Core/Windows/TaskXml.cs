using System.Security;

namespace TokenStack.Core.Windows;

/// <summary>Renders the HeadroomProxy scheduled-task definition. Full XML (not schtasks
/// flags) is the only way to express RestartOnFailure + IgnoreNew + Hidden with fidelity.</summary>
public static class TaskXml
{
    public static string Render(string pythonwPath, string scriptPath, string workingDir, string userId)
    {
        string E(string s) => SecurityElement.Escape(s);
        return $"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo>
            <Description>token-stack: Headroom API-compression proxy (managed by token-stack.exe)</Description>
          </RegistrationInfo>
          <Triggers>
            <LogonTrigger>
              <Enabled>true</Enabled>
              <UserId>{E(userId)}</UserId>
            </LogonTrigger>
          </Triggers>
          <Principals>
            <Principal id="Author">
              <UserId>{E(userId)}</UserId>
              <LogonType>InteractiveToken</LogonType>
              <RunLevel>LeastPrivilege</RunLevel>
            </Principal>
          </Principals>
          <Settings>
            <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
            <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
            <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
            <AllowHardTerminate>true</AllowHardTerminate>
            <StartWhenAvailable>true</StartWhenAvailable>
            <AllowStartOnDemand>true</AllowStartOnDemand>
            <Enabled>true</Enabled>
            <Hidden>true</Hidden>
            <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
            <RestartOnFailure>
              <Interval>PT1M</Interval>
              <Count>3</Count>
            </RestartOnFailure>
          </Settings>
          <Actions Context="Author">
            <Exec>
              <Command>{E(pythonwPath)}</Command>
              <Arguments>"{E(scriptPath)}"</Arguments>
              <WorkingDirectory>{E(workingDir)}</WorkingDirectory>
            </Exec>
          </Actions>
        </Task>
        """;
    }
}
