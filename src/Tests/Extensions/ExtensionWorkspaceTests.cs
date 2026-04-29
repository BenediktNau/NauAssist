using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NauAssist.Common.Configuration;
using NauAssist.Extensions.Workspace;

namespace NauAssist.Tests.Extensions;

public sealed class ExtensionWorkspaceTests : IDisposable
{
    private readonly string _tmp;
    private readonly string _coreRoot;
    private readonly string _extensionsRoot;
    private readonly ExtensionWorkspace _workspace;
    private readonly RecordingAuditLog _audit;

    public ExtensionWorkspaceTests()
    {
        _tmp = Path.Combine(Path.GetTempPath(), "nauassist-ws-" + Guid.NewGuid().ToString("N"));
        _coreRoot = Path.Combine(_tmp, "src");
        _extensionsRoot = Path.Combine(_tmp, "extensions");
        Directory.CreateDirectory(_coreRoot);
        Directory.CreateDirectory(_extensionsRoot);

        var resolver = new PathResolver(Options.Create(new PathOptions
        {
            BaseDirectory = _tmp,
        }));
        _audit = new RecordingAuditLog();
        _workspace = new ExtensionWorkspace(resolver, _audit);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tmp))
        {
            Directory.Delete(_tmp, recursive: true);
        }
    }

    [Fact]
    public async Task WriteFile_Plain_Succeeds_E0_6()
    {
        await _workspace.WriteTextAsync("tools/demo/hello.txt", "Hallo Welt", actor: "system");

        var path = Path.Combine(_extensionsRoot, "tools", "demo", "hello.txt");
        Assert.True(File.Exists(path));
        Assert.Equal("Hallo Welt", File.ReadAllText(path));
        Assert.Single(_audit.Entries);
        Assert.Equal("WriteFile", _audit.Entries[0].Operation);
    }

    [Fact]
    public async Task WriteFile_DotDotTraversal_Throws_E0_6()
    {
        var ex = await Assert.ThrowsAsync<ExtensionBoundaryViolation>(() =>
            _workspace.WriteTextAsync("../escape.txt", "x", actor: "agent"));

        Assert.Contains(_extensionsRoot, ex.Reason, StringComparison.Ordinal);
        Assert.Empty(_audit.Entries);
    }

    [Fact]
    public async Task WriteFile_AbsolutePathOutsideRoot_Throws_E0_6()
    {
        var absolute = Path.Combine(_tmp, "outside.txt");

        var ex = await Assert.ThrowsAsync<ExtensionBoundaryViolation>(() =>
            _workspace.WriteTextAsync(absolute, "x", actor: "agent"));

        Assert.Contains("absolute", ex.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WriteFile_PathInsideCoreRoot_Throws_E0_6()
    {
        // konstruiert einen relativen Pfad, dessen kanonisches Ziel in src/ landet
        var rel = Path.Combine("..", "src", "evil.cs");

        await Assert.ThrowsAsync<ExtensionBoundaryViolation>(() =>
            _workspace.WriteTextAsync(rel, "x", actor: "agent"));
    }

    [Fact]
    public async Task WriteFile_ParentDirectorySymlinkPointingOutside_Throws_E0_6()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // Symlinks brauchen Adminrechte auf Windows; Test gilt für Linux/Container
        }

        var outside = Path.Combine(_tmp, "outside-target");
        Directory.CreateDirectory(outside);

        var symlinkParent = Path.Combine(_extensionsRoot, "rogue");
        Directory.CreateSymbolicLink(symlinkParent, outside);

        await Assert.ThrowsAsync<ExtensionBoundaryViolation>(() =>
            _workspace.WriteTextAsync("rogue/payload.txt", "x", actor: "agent"));
    }

    [Fact]
    public async Task DeleteFile_OnlyAffectsExtensionsRoot_E0_6()
    {
        await _workspace.WriteTextAsync("tools/x.txt", "x", actor: "system");
        _workspace.DeleteFile("tools/x.txt", actor: "system");

        Assert.False(File.Exists(Path.Combine(_extensionsRoot, "tools", "x.txt")));
        Assert.Equal(2, _audit.Entries.Count);
        Assert.Equal("DeleteFile", _audit.Entries[1].Operation);
    }

    [Fact]
    public async Task ReadAllText_AllowsReadInsideRoot_E0_6()
    {
        await _workspace.WriteTextAsync("specifications/spec.md", "# Spec", actor: "system");
        var content = _workspace.ReadAllText("specifications/spec.md");
        Assert.Equal("# Spec", content);
    }

    private sealed class RecordingAuditLog : IExtensionAuditLog
    {
        public List<(string Actor, string Operation, string Path)> Entries { get; } = new();

        public void Append(string actor, string operation, string canonicalPath, IReadOnlyDictionary<string, string>? metadata = null)
        {
            Entries.Add((actor, operation, canonicalPath));
        }
    }
}
