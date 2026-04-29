using System;
using System.IO;
using Microsoft.Extensions.Options;
using NauAssist.Common.Configuration;
using NauAssist.Extensions.Workspace;

namespace NauAssist.Tests.Extensions;

public sealed class FileExtensionAuditLogTests : IDisposable
{
    private readonly string _tmp;
    private readonly IPathResolver _paths;

    public FileExtensionAuditLogTests()
    {
        _tmp = Path.Combine(Path.GetTempPath(), "nauassist-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_tmp, "src"));
        Directory.CreateDirectory(Path.Combine(_tmp, "extensions"));

        _paths = new PathResolver(Options.Create(new PathOptions { BaseDirectory = _tmp }));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tmp))
        {
            Directory.Delete(_tmp, recursive: true);
        }
    }

    [Fact]
    public void AppendsJsonLine_PerOperation_E0_6()
    {
        var fixedTime = new DateTimeOffset(2026, 4, 29, 14, 30, 0, TimeSpan.Zero);
        var log = new FileExtensionAuditLog(_paths, new FixedClock(fixedTime));

        log.Append("system", "WriteFile", "/abs/path/a.txt");

        var changelogFile = Path.Combine(_paths.ExtensionsRoot, "changelog", "2026-04-29.jsonl");
        Assert.True(File.Exists(changelogFile));

        var content = File.ReadAllText(changelogFile).Trim();
        Assert.Single(content.Split('\n'));
        Assert.Contains("\"Actor\":\"system\"", content);
        Assert.Contains("\"Operation\":\"WriteFile\"", content);
        Assert.Contains("\"Path\":\"/abs/path/a.txt\"", content);
    }

    [Fact]
    public void MultipleWritesSameDay_AppendToSameFile_E0_6()
    {
        var fixedTime = new DateTimeOffset(2026, 4, 29, 14, 30, 0, TimeSpan.Zero);
        var log = new FileExtensionAuditLog(_paths, new FixedClock(fixedTime));

        log.Append("system", "WriteFile", "/a");
        log.Append("agent:tool-builder", "CreateDirectory", "/b");
        log.Append("system", "DeleteFile", "/a");

        var changelogFile = Path.Combine(_paths.ExtensionsRoot, "changelog", "2026-04-29.jsonl");
        var lines = File.ReadAllLines(changelogFile);
        Assert.Equal(3, lines.Length);
        Assert.Contains("agent:tool-builder", lines[1]);
    }

    [Fact]
    public void DifferentDays_WriteToSeparateFiles_E0_6()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 4, 29, 23, 59, 0, TimeSpan.Zero));
        var log = new FileExtensionAuditLog(_paths, clock);
        log.Append("system", "WriteFile", "/a");

        clock.Advance(TimeSpan.FromMinutes(2));
        log.Append("system", "WriteFile", "/b");

        var changelogDir = Path.Combine(_paths.ExtensionsRoot, "changelog");
        Assert.True(File.Exists(Path.Combine(changelogDir, "2026-04-29.jsonl")));
        Assert.True(File.Exists(Path.Combine(changelogDir, "2026-04-30.jsonl")));
    }

    private sealed class FixedClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan span) => _now = _now.Add(span);
    }
}
