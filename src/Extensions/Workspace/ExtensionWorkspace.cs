using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NauAssist.Common.Configuration;

namespace NauAssist.Extensions.Workspace;

public sealed class ExtensionWorkspace : IExtensionWorkspace
{
    private readonly IPathResolver _paths;
    private readonly IExtensionAuditLog _audit;

    public ExtensionWorkspace(IPathResolver paths, IExtensionAuditLog audit)
    {
        _paths = paths;
        _audit = audit;
    }

    public string Root => _paths.ExtensionsRoot;

    public async Task WriteFileAsync(string relativePath, ReadOnlyMemory<byte> content, string actor, CancellationToken cancellationToken = default)
    {
        var target = Resolve(relativePath, mustNotEscape: true, allowParentSymlinks: false);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllBytesAsync(target, content.ToArray(), cancellationToken).ConfigureAwait(false);
        _audit.Append(actor, "WriteFile", target);
    }

    public Task WriteTextAsync(string relativePath, string content, string actor, CancellationToken cancellationToken = default)
    {
        return WriteFileAsync(relativePath, System.Text.Encoding.UTF8.GetBytes(content), actor, cancellationToken);
    }

    public void CreateDirectory(string relativePath, string actor)
    {
        var target = Resolve(relativePath, mustNotEscape: true, allowParentSymlinks: false);
        Directory.CreateDirectory(target);
        _audit.Append(actor, "CreateDirectory", target);
    }

    public void DeleteFile(string relativePath, string actor)
    {
        var target = Resolve(relativePath, mustNotEscape: true, allowParentSymlinks: false);
        if (File.Exists(target))
        {
            File.Delete(target);
        }

        _audit.Append(actor, "DeleteFile", target);
    }

    public bool Exists(string relativePath)
    {
        var target = Resolve(relativePath, mustNotEscape: true, allowParentSymlinks: true);
        return File.Exists(target) || Directory.Exists(target);
    }

    public string ReadAllText(string relativePath)
    {
        var target = Resolve(relativePath, mustNotEscape: true, allowParentSymlinks: true);
        return File.ReadAllText(target);
    }

    /// <summary>
    /// Kanonisiert <paramref name="relativePath"/>, prüft Eindämmung in
    /// <see cref="IPathResolver.ExtensionsRoot"/> und stellt sicher, dass
    /// kein Symlink (Pfadbestandteil oder Ziel) aus der Erweiterungs-Welt
    /// herausführt. Liegt der Pfad innerhalb der Kernwelt, wird hart
    /// verweigert — auch dann, wenn er gleichzeitig als Erweiterungs-Pfad
    /// angegeben wurde.
    /// </summary>
    private string Resolve(string relativePath, bool mustNotEscape, bool allowParentSymlinks)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ExtensionBoundaryViolation(relativePath ?? "<null>", "leerer Pfad");
        }

        if (Path.IsPathRooted(relativePath))
        {
            throw new ExtensionBoundaryViolation(relativePath, "absolute Pfade sind nicht erlaubt");
        }

        var combined = Path.Combine(Root, relativePath);
        var canonical = Path.GetFullPath(combined);

        if (mustNotEscape && !IsContainedIn(canonical, Root))
        {
            throw new ExtensionBoundaryViolation(canonical, $"liegt nicht unterhalb von {Root}");
        }

        var coreRoot = _paths.CoreRoot;
        if (IsContainedIn(canonical, coreRoot))
        {
            throw new ExtensionBoundaryViolation(canonical, $"Kernwelt ({coreRoot}) ist read-only");
        }

        if (!allowParentSymlinks)
        {
            EnsureNoSymlinkEscape(canonical);
        }

        return canonical;
    }

    private void EnsureNoSymlinkEscape(string canonicalPath)
    {
        var current = canonicalPath;
        while (!string.IsNullOrEmpty(current) && !PathsEqual(current, Root))
        {
            FileSystemInfo? info = Directory.Exists(current)
                ? new DirectoryInfo(current)
                : File.Exists(current) ? new FileInfo(current) : null;

            if (info is not null)
            {
                var target = info.ResolveLinkTarget(returnFinalTarget: true);
                if (target is not null)
                {
                    var resolved = Path.GetFullPath(target.FullName);
                    if (!IsContainedIn(resolved, Root))
                    {
                        throw new ExtensionBoundaryViolation(canonicalPath,
                            $"Symlink-Ziel '{resolved}' liegt außerhalb der Erweiterungs-Welt");
                    }
                }
            }

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || PathsEqual(parent, current))
            {
                break;
            }

            current = parent;
        }
    }

    private static bool IsContainedIn(string candidate, string root)
    {
        var normalizedCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));

        if (PathsEqual(normalizedCandidate, normalizedRoot))
        {
            return true;
        }

        var withSeparator = normalizedRoot + Path.DirectorySeparatorChar;
        return normalizedCandidate.StartsWith(withSeparator, StringComparison.Ordinal);
    }

    private static bool PathsEqual(string a, string b)
    {
        return string.Equals(
            Path.TrimEndingDirectorySeparator(a),
            Path.TrimEndingDirectorySeparator(b),
            StringComparison.Ordinal);
    }
}
