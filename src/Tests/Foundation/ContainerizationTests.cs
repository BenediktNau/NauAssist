using System.IO;

namespace NauAssist.Tests.Foundation;

public class ContainerizationTests
{
    [Fact]
    public void Dockerfile_ExistsAtRepoRoot_E0_3()
    {
        var path = Path.Combine(RepoLayout.RepoRoot, "Dockerfile");
        Assert.True(File.Exists(path), $"Dockerfile fehlt: {path}");

        var content = File.ReadAllText(path);
        Assert.Contains("AS build", content);
        Assert.Contains("AS runtime", content);
        Assert.Contains("USER nauassist", content);
        Assert.Contains("HEALTHCHECK", content);
    }

    [Fact]
    public void Compose_ExistsAtRepoRoot_E0_3()
    {
        var path = Path.Combine(RepoLayout.RepoRoot, "compose.yml");
        Assert.True(File.Exists(path), $"compose.yml fehlt: {path}");

        var content = File.ReadAllText(path);
        Assert.Contains("services:", content);
        Assert.Contains("agent:", content);
        Assert.Contains("host.docker.internal:host-gateway", content);
        Assert.Contains("/var/nauassist/extensions", content);
    }

    [Fact]
    public void DockerIgnore_Exists_E0_3()
    {
        var path = Path.Combine(RepoLayout.RepoRoot, ".dockerignore");
        Assert.True(File.Exists(path), $".dockerignore fehlt: {path}");
    }

    [Fact]
    public void SmokeScript_ExistsAndIsExecutable_E0_3()
    {
        var path = Path.Combine(RepoLayout.RepoRoot, "scripts", "smoke.sh");
        Assert.True(File.Exists(path), $"scripts/smoke.sh fehlt: {path}");

        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var mode = File.GetUnixFileMode(path);
        Assert.True(
            (mode & UnixFileMode.UserExecute) == UnixFileMode.UserExecute,
            $"scripts/smoke.sh ist nicht executable (mode={Convert.ToString((int)mode, 8)})");
    }
}
