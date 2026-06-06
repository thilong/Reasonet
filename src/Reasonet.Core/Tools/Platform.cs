using System.Runtime.InteropServices;

namespace Reasonet.Tools;

/// <summary>
/// Cross-platform helpers for tool implementations.
/// </summary>
public static class Platform
{
    /// <summary>True when running on Windows (cmd.exe, findstr).</summary>
    public static bool IsWindows { get; } =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary>True when running on macOS.</summary>
    public static bool IsMacOS { get; } =
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    /// <summary>True when running on Linux.</summary>
    public static bool IsLinux { get; } =
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    /// <summary>
    /// The default shell executable for the current platform.
    /// On Windows: cmd.exe ; on Unix: /bin/bash (or /bin/sh fallback).
    /// </summary>
    public static string Shell
    {
        get
        {
            if (IsWindows) return "cmd.exe";
            // Prefer bash if available, fall back to sh
            if (File.Exists("/bin/bash")) return "/bin/bash";
            return "/bin/sh";
        }
    }

    /// <summary>
    /// Shell argument: -c on Unix, /c on Windows.
    /// </summary>
    public static string ShellCommandArg => IsWindows ? "/c" : "-c";

    /// <summary>
    /// The primary grep command available on this platform.
    /// Windows: findstr ; Unix: rg (if available) then grep.
    /// </summary>
    public static string NativeGrepCommand
    {
        get
        {
            if (IsWindows) return "findstr";
            return "grep";
        }
    }

    /// <summary>
    /// Normalize line endings: convert \r\n to \n for consistent line counting.
    /// </summary>
    public static string NormalizeLineEndings(string text) =>
        text.Replace("\r\n", "\n");

    /// <summary>
    /// Count lines in text, handling \r\n or \n endings cross-platform.
    /// </summary>
    public static int CountLines(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var n = NormalizeLineEndings(text);
        var count = 1;
        foreach (var c in n)
            if (c == '\n') count++;
        return count;
    }

    /// <summary>
    /// Make a path relative to the current directory (preferred for display).
    /// </summary>
    public static string RelativePath(string fullPath)
    {
        var cwd = Directory.GetCurrentDirectory();
        if (fullPath.StartsWith(cwd, StringComparison.OrdinalIgnoreCase))
        {
            var rel = fullPath[cwd.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (rel.Length > 0) return rel;
        }
        return fullPath;
    }
}
