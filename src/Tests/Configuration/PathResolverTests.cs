using System;
using System.IO;
using Microsoft.Extensions.Options;
using NauAssist.Common.Configuration;

namespace NauAssist.Tests.Configuration;

public sealed class PathResolverTests : IDisposable
{
    private readonly string _tmp;

    public PathResolverTests()
    {
        _tmp = Path.Combine(Path.GetTempPath(), "nauassist-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_tmp, "src"));
        Directory.CreateDirectory(Path.Combine(_tmp, "extensions"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tmp))
        {
            Directory.Delete(_tmp, recursive: true);
        }
    }

    [Fact]
    public void Resolves_AllRoots_FromOptions_E0_2()
    {
        var resolver = NewResolver(new PathOptions { BaseDirectory = _tmp });

        Assert.Equal(Path.Combine(_tmp, "src"), resolver.CoreRoot);
        Assert.Equal(Path.Combine(_tmp, "extensions"), resolver.ExtensionsRoot);
        Assert.Equal(Path.Combine(_tmp, "data"), resolver.DataRoot);
        Assert.Equal(Path.Combine(_tmp, "data", "logs"), resolver.LogsRoot);
        Assert.Equal(Path.Combine(_tmp, "models"), resolver.ModelsRoot);
    }

    [Fact]
    public void CanonicalizesRelativePaths_E0_2()
    {
        var resolver = NewResolver(new PathOptions
        {
            BaseDirectory = _tmp,
            CoreRoot = "src",
            ExtensionsRoot = "./extensions",
            DataRoot = "./data/../data",
            LogsRoot = "data/logs",
            ModelsRoot = "models",
        });

        Assert.Equal(Path.Combine(_tmp, "src"), resolver.CoreRoot);
        Assert.Equal(Path.Combine(_tmp, "extensions"), resolver.ExtensionsRoot);
        Assert.Equal(Path.Combine(_tmp, "data"), resolver.DataRoot);
    }

    [Fact]
    public void CreatesMissingDataDirectoriesLazily_E0_2()
    {
        var resolver = NewResolver(new PathOptions { BaseDirectory = _tmp });

        Assert.False(Directory.Exists(Path.Combine(_tmp, "data")),
            "DataRoot darf vor erstem Zugriff nicht existieren");

        var dataPath = resolver.DataRoot;

        Assert.True(Directory.Exists(dataPath));
        Assert.True(Directory.Exists(resolver.LogsRoot));
        Assert.True(Directory.Exists(resolver.ModelsRoot));
    }

    [Fact]
    public void Throws_WhenCoreRootMissing_E0_2()
    {
        Directory.Delete(Path.Combine(_tmp, "src"));

        var ex = Assert.Throws<DirectoryNotFoundException>(() =>
            NewResolver(new PathOptions { BaseDirectory = _tmp }));

        Assert.Contains("CoreRoot", ex.Message);
    }

    [Fact]
    public void AbsolutePath_OverridesBaseDirectory_E0_2()
    {
        var elsewhere = Path.Combine(Path.GetTempPath(), "nauassist-elsewhere-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(elsewhere, "extensions"));

        try
        {
            var resolver = NewResolver(new PathOptions
            {
                BaseDirectory = _tmp,
                ExtensionsRoot = Path.Combine(elsewhere, "extensions"),
            });

            Assert.Equal(Path.Combine(elsewhere, "extensions"), resolver.ExtensionsRoot);
        }
        finally
        {
            Directory.Delete(elsewhere, recursive: true);
        }
    }

    private static PathResolver NewResolver(PathOptions options)
    {
        return new PathResolver(Options.Create(options));
    }
}
