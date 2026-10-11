using System.Text;
using API.Services.Implementations;

namespace API.Tests.ServerSetup;

public class SshCommandLineTests
{
    private static string Decode(string commandLine)
    {
        const string prefix = "powershell -EncodedCommand ";
        Assert.StartsWith(prefix, commandLine);
        return Encoding.Unicode.GetString(Convert.FromBase64String(commandLine[prefix.Length..]));
    }

    [Fact]
    public void EncodedCommandLine_RoundTripsTheScript()
    {
        const string script = "Write-Output 'DV_RESULT: ok ✓'\nexit 0";

        Assert.Equal(script, Decode(SshService.BuildEncodedCommandLine(script)));
    }

    [Fact]
    public void ShortScripts_StayInline()
    {
        var script = new string('a', 2000);

        Assert.True(SshService.BuildEncodedCommandLine(script).Length <= SshService.MaxInlineCommandLength);
    }

    [Fact]
    public void LongScripts_ExceedTheInlineLimit()
    {
        // A readiness-sized script: UTF-16 + Base64 makes the command line ~2.7x the script length
        var script = new string('a', 5000);

        Assert.True(SshService.BuildEncodedCommandLine(script).Length > SshService.MaxInlineCommandLength);
    }

    [Fact]
    public void ScriptFileRunner_IsShortAndReadsDeletesThenRunsTheFile()
    {
        const string path = @"C:\Windows\Temp\dv-0123456789abcdef0123456789abcdef.ps1";

        var runner = SshService.BuildScriptFileRunner(path);
        var commandLine = SshService.BuildEncodedCommandLine(runner);

        Assert.True(commandLine.Length <= SshService.MaxInlineCommandLength);
        Assert.Contains($"$dvScriptPath = '{path}'", runner);
        Assert.True(runner.IndexOf("Remove-Item", StringComparison.Ordinal) < runner.IndexOf(". ([scriptblock]::Create", StringComparison.Ordinal));
    }

    [Fact]
    public void ScriptFileRunner_EscapesQuotesInThePath()
    {
        var runner = SshService.BuildScriptFileRunner(@"C:\Temp\it's.ps1");

        Assert.Contains(@"$dvScriptPath = 'C:\Temp\it''s.ps1'", runner);
    }
}
