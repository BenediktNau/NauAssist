using System;
using System.IO;

namespace NauAssist.Tests.Foundation;

internal static class RepoLayout
{
    public static string SolutionDir { get; } = FindSolutionDir();

    public static string RepoRoot { get; } = Directory.GetParent(SolutionDir)!.FullName;

    private static string FindSolutionDir()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (current.GetFiles("NauAssist.slnx").Length > 0)
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException(
            $"NauAssist.slnx ausgehend von {AppContext.BaseDirectory} nicht gefunden");
    }
}
