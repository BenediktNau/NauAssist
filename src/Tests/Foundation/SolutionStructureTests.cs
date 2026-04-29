using System.IO;

namespace NauAssist.Tests.Foundation;

public class SolutionStructureTests
{
    [Fact]
    public void ExtensionsDirectoryLayout_E0_1()
    {
        var extensions = Path.Combine(RepoLayout.RepoRoot, "extensions");
        Assert.True(Directory.Exists(extensions),
            $"Erweiterungs-Wurzel fehlt: {extensions}");

        var subfolders = new[]
        {
            "tools",
            "specifications",
            "changelog",
            "weakness_log",
            "usage_logs",
            "phase2_jobs",
        };

        foreach (var sub in subfolders)
        {
            var path = Path.Combine(extensions, sub);
            Assert.True(Directory.Exists(path),
                $"Erweiterungs-Unterordner fehlt: extensions/{sub}");
        }
    }

    [Fact]
    public void SrcContainsExpectedProjects_E0_1()
    {
        var expectedProjects = new[]
        {
            "Common",
            "AICore",
            "Memory",
            "Tools",
            "Voice",
            "Extensions",
            "Api",
            "Tests",
        };

        foreach (var project in expectedProjects)
        {
            var csproj = Path.Combine(RepoLayout.SolutionDir, project, $"{project}.csproj");
            Assert.True(File.Exists(csproj),
                $"Projekt-Datei fehlt: {csproj}");
        }
    }

    [Fact]
    public void RepoRoot_HasEditorConfig_E0_1()
    {
        var editorConfig = Path.Combine(RepoLayout.RepoRoot, ".editorconfig");
        Assert.True(File.Exists(editorConfig),
            $".editorconfig fehlt im Repo-Root: {editorConfig}");
    }
}
