using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace QGST.Core.Services;

/// <summary>
/// Result of target resolution
/// </summary>
public class ResolveResult
{
    public string OriginalPath { get; set; } = "";
    public string ResolvedPath { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public bool Success { get; set; }
    public List<string> Candidates { get; set; } = new();
    public string ErrorMessage { get; set; } = "";
    public string OriginalType { get; set; } = "";
    
    /// <summary>Steam App ID if this is a Steam game</summary>
    public string? SteamAppId { get; set; }
    
    /// <summary>Whether this requires special Steam handling</summary>
    public bool IsSteamGame { get; set; }
}

/// <summary>
/// Service to resolve various file types and protocols to executable paths.
/// Handles .exe, .lnk, .bat, .cmd, and Steam URLs.
/// </summary>
[SupportedOSPlatform("windows")]
public class TargetResolver
{
    private readonly ConfigManager? _configManager;

    public TargetResolver(ConfigManager? configManager = null)
    {
        _configManager = configManager;
    }

    /// <summary>
    /// Resolve a path/URL to an executable.
    /// </summary>
    public string Resolve(string path)
    {
        var result = ResolveDetailed(path);
        return result.Success ? result.ResolvedPath : path;
    }

    /// <summary>
    /// Resolve with detailed information.
    /// </summary>
    public ResolveResult ResolveDetailed(string path)
    {
        var result = new ResolveResult
        {
            OriginalPath = path,
            ResolvedPath = path
        };

        if (string.IsNullOrEmpty(path))
        {
            result.ErrorMessage = "Empty path";
            return result;
        }

        // Check for Steam URL
        if (path.StartsWith("steam://", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveSteamUrl(path);
        }

        // Check for Steam shortcut (.url file with steam:// link)
        if (path.EndsWith(".url", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
        {
            var steamResult = TryResolveSteamUrlFile(path);
            if (steamResult != null) return steamResult;
        }

        if (!File.Exists(path))
        {
            result.ErrorMessage = $"File not found: {path}";
            return result;
        }

        var ext = Path.GetExtension(path).ToLowerInvariant();
        result.OriginalType = ext;

        try
        {
            switch (ext)
            {
                case ".exe":
                    result.ResolvedPath = Path.GetFullPath(path);
                    result.WorkingDirectory = Path.GetDirectoryName(result.ResolvedPath) ?? "";
                    result.Success = true;
                    break;

                case ".lnk":
                    result = ResolveLnk(path);
                    break;

                case ".bat":
                case ".cmd":
                    result = ResolveBatch(path);
                    break;

                case ".url":
                    result = ResolveUrlFile(path);
                    break;

                default:
                    result.ErrorMessage = $"Unsupported file type: {ext}";
                    break;
            }
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// Resolve a Steam URL (steam://rungameid/XXXXX) to the game executable.
    /// </summary>
    private ResolveResult ResolveSteamUrl(string steamUrl)
    {
        var result = new ResolveResult
        {
            OriginalPath = steamUrl,
            OriginalType = "steam",
            IsSteamGame = true
        };

        // Extract app ID from steam://rungameid/XXXXX or steam://run/XXXXX
        var match = Regex.Match(steamUrl, @"steam://(rungameid|run)/(\d+)", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            result.ErrorMessage = "Invalid Steam URL format";
            return result;
        }

        var appId = match.Groups[2].Value;
        result.SteamAppId = appId;

        // Try to find the game's executable
        var steamPath = GetSteamPath();
        if (string.IsNullOrEmpty(steamPath))
        {
            result.ErrorMessage = "Steam installation not found";
            // Still mark as success for Steam handling
            result.Success = true;
            return result;
        }

        // Look up the game in Steam's libraryfolders.vdf or appmanifest
        var gamePath = FindSteamGameExecutable(steamPath, appId);
        
        if (!string.IsNullOrEmpty(gamePath))
        {
            result.ResolvedPath = gamePath;
            result.WorkingDirectory = Path.GetDirectoryName(gamePath) ?? "";
            result.Success = true;
        }
        else
        {
            // Can't find executable, but we have the app ID for Steam launch
            result.ErrorMessage = "Game executable not found. Will use Steam launch.";
            result.Success = true; // Steam can still launch it
        }

        return result;
    }

    private string GetSteamPath()
    {
        try
        {
            // Check registry for Steam install path
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            var path = key?.GetValue("SteamPath")?.ToString();
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                return path;

            // Common locations
            var commonPaths = new[]
            {
                @"C:\Program Files (x86)\Steam",
                @"C:\Program Files\Steam",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam")
            };

            foreach (var p in commonPaths)
            {
                if (Directory.Exists(p)) return p;
            }
        }
        catch { }

        return "";
    }

    private string? FindSteamGameExecutable(string steamPath, string appId)
    {
        try
        {
            // First, find the library folder containing this game
            var libraryFolders = GetSteamLibraryFolders(steamPath);
            
            foreach (var libraryPath in libraryFolders)
            {
                var steamAppsPath = Path.Combine(libraryPath, "steamapps");
                var manifestPath = Path.Combine(steamAppsPath, $"appmanifest_{appId}.acf");

                if (!File.Exists(manifestPath)) continue;

                // Parse the manifest to get install directory
                var manifestContent = File.ReadAllText(manifestPath);
                var installDirMatch = Regex.Match(manifestContent, @"""installdir""\s+""([^""]+)""");
                
                if (!installDirMatch.Success) continue;

                var installDir = installDirMatch.Groups[1].Value;
                var gamePath = Path.Combine(steamAppsPath, "common", installDir);

                if (!Directory.Exists(gamePath)) continue;

                // Try to find the main executable
                var executable = FindMainExecutable(gamePath, appId);
                if (!string.IsNullOrEmpty(executable))
                    return executable;
            }
        }
        catch { }

        return null;
    }

    private List<string> GetSteamLibraryFolders(string steamPath)
    {
        var folders = new List<string> { steamPath };

        try
        {
            var vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdfPath)) return folders;

            var content = File.ReadAllText(vdfPath);
            
            // Match "path" entries
            var matches = Regex.Matches(content, @"""path""\s+""([^""]+)""");
            foreach (Match match in matches)
            {
                var path = match.Groups[1].Value.Replace("\\\\", "\\");
                if (Directory.Exists(path) && !folders.Contains(path, StringComparer.OrdinalIgnoreCase))
                    folders.Add(path);
            }
        }
        catch { }

        return folders;
    }

    private string? FindMainExecutable(string gamePath, string appId)
    {
        // Check for cached mapping
        var cached = _configManager?.GetMappedTarget($"steam://{appId}");
        if (!string.IsNullOrEmpty(cached) && File.Exists(cached))
            return cached;

        // Look for common executable patterns
        var exeFiles = Directory.GetFiles(gamePath, "*.exe", SearchOption.AllDirectories);
        
        // Filter out known non-game executables
        var filtered = exeFiles.Where(e =>
        {
            var name = Path.GetFileName(e).ToLowerInvariant();
            return !name.Contains("unins") &&
                   !name.Contains("setup") &&
                   !name.Contains("config") &&
                   !name.Contains("crash") &&
                   !name.Contains("report") &&
                   !name.Contains("launcher") && // Could be launcher, might need to include
                   !name.Contains("update") &&
                   !name.Contains("vc_redist") &&
                   !name.Contains("directx") &&
                   !name.Contains("dotnet") &&
                   !name.StartsWith("ue4") && // Unreal tools
                   !name.StartsWith("ue5");
        }).ToList();

        if (filtered.Count == 1)
            return filtered[0];

        // If multiple, prefer ones in root directory
        var rootExes = filtered.Where(e => 
            Path.GetDirectoryName(e)?.Equals(gamePath, StringComparison.OrdinalIgnoreCase) == true).ToList();

        if (rootExes.Count == 1)
            return rootExes[0];

        // Prefer ones with the game folder name
        var gameName = Path.GetFileName(gamePath).ToLowerInvariant();
        var matchingName = filtered.FirstOrDefault(e => 
            Path.GetFileNameWithoutExtension(e).ToLowerInvariant().Contains(gameName));
        
        if (matchingName != null)
            return matchingName;

        // Return first found or null
        return filtered.FirstOrDefault();
    }

    private ResolveResult? TryResolveSteamUrlFile(string urlFilePath)
    {
        try
        {
            var content = File.ReadAllText(urlFilePath);
            var match = Regex.Match(content, @"URL\s*=\s*(steam://[^\r\n]+)", RegexOptions.IgnoreCase);
            
            if (match.Success)
            {
                var steamUrl = match.Groups[1].Value.Trim();
                return ResolveSteamUrl(steamUrl);
            }
        }
        catch { }

        return null;
    }

    private ResolveResult ResolveUrlFile(string urlPath)
    {
        var result = new ResolveResult
        {
            OriginalPath = urlPath,
            OriginalType = ".url"
        };

        try
        {
            var content = File.ReadAllText(urlPath);
            
            // Check for Steam URL first
            var steamMatch = Regex.Match(content, @"URL\s*=\s*(steam://[^\r\n]+)", RegexOptions.IgnoreCase);
            if (steamMatch.Success)
            {
                return ResolveSteamUrl(steamMatch.Groups[1].Value.Trim());
            }

            // Regular URL file
            var urlMatch = Regex.Match(content, @"URL\s*=\s*([^\r\n]+)", RegexOptions.IgnoreCase);
            if (urlMatch.Success)
            {
                result.ResolvedPath = urlMatch.Groups[1].Value.Trim();
                result.Success = true;
            }
            else
            {
                result.ErrorMessage = "No URL found in file";
            }
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    private ResolveResult ResolveLnk(string lnkPath)
    {
        var result = new ResolveResult
        {
            OriginalPath = lnkPath,
            OriginalType = ".lnk"
        };

        try
        {
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
            {
                result.ErrorMessage = "WScript.Shell not available";
                return result;
            }

            dynamic shell = Activator.CreateInstance(shellType)!;
            try
            {
                dynamic shortcut = shell.CreateShortcut(lnkPath);
                try
                {
                    string targetPath = shortcut.TargetPath;
                    string arguments = shortcut.Arguments ?? "";
                    string workingDir = shortcut.WorkingDirectory ?? "";

                    // Check if it's a Steam shortcut
                    if (targetPath.Contains("steam.exe", StringComparison.OrdinalIgnoreCase) &&
                        arguments.Contains("steam://", StringComparison.OrdinalIgnoreCase))
                    {
                        var steamUrlMatch = Regex.Match(arguments, @"(steam://[^\s""]+)");
                        if (steamUrlMatch.Success)
                        {
                            return ResolveSteamUrl(steamUrlMatch.Groups[1].Value);
                        }
                    }

                    if (!string.IsNullOrEmpty(targetPath))
                    {
                        if (!Path.IsPathRooted(targetPath))
                        {
                            var lnkDir = Path.GetDirectoryName(lnkPath);
                            targetPath = Path.GetFullPath(Path.Combine(lnkDir ?? "", targetPath));
                        }

                        if (File.Exists(targetPath))
                        {
                            result.ResolvedPath = targetPath;
                            result.Arguments = arguments;
                            result.WorkingDirectory = !string.IsNullOrEmpty(workingDir)
                                ? workingDir
                                : Path.GetDirectoryName(targetPath) ?? "";
                            result.Success = true;
                        }
                        else
                        {
                            result.ErrorMessage = $"Shortcut target not found: {targetPath}";
                        }
                    }
                    else
                    {
                        result.ErrorMessage = "Shortcut has no target path";
                    }
                }
                finally
                {
                    Marshal.FinalReleaseComObject(shortcut);
                }
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
        catch (Exception ex)
        {
            result.ErrorMessage = $"Failed to resolve shortcut: {ex.Message}";
        }

        return result;
    }

    private ResolveResult ResolveBatch(string batPath)
    {
        var result = new ResolveResult
        {
            OriginalPath = batPath,
            OriginalType = Path.GetExtension(batPath).ToLowerInvariant()
        };

        // Check cached mapping
        var cached = _configManager?.GetMappedTarget(batPath);
        if (!string.IsNullOrEmpty(cached) && File.Exists(cached))
        {
            result.ResolvedPath = cached;
            result.WorkingDirectory = Path.GetDirectoryName(cached) ?? "";
            result.Success = true;
            return result;
        }

        try
        {
            var batDir = Path.GetDirectoryName(batPath) ?? "";
            var lines = File.ReadAllLines(batPath);
            var candidates = new List<string>();

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();

                // Skip comments and empty lines
                if (string.IsNullOrEmpty(line)) continue;
                if (line.StartsWith("REM ", StringComparison.OrdinalIgnoreCase)) continue;
                if (line.StartsWith("::")) continue;
                if (line.StartsWith("@REM ", StringComparison.OrdinalIgnoreCase)) continue;

                // Remove @ prefix
                if (line.StartsWith("@"))
                    line = line.Substring(1).TrimStart();

                // Extract exe paths
                var exePaths = ExtractExePaths(line, batDir);
                foreach (var exePath in exePaths)
                {
                    if (File.Exists(exePath) && !candidates.Contains(exePath, StringComparer.OrdinalIgnoreCase))
                        candidates.Add(exePath);
                }
            }

            if (candidates.Count == 1)
            {
                result.ResolvedPath = candidates[0];
                result.WorkingDirectory = Path.GetDirectoryName(candidates[0]) ?? "";
                result.Success = true;
                _configManager?.AddMapping(batPath, candidates[0]);
            }
            else if (candidates.Count > 1)
            {
                result.Candidates = candidates;
                result.ErrorMessage = "Multiple executables found in batch file";
            }
            else
            {
                result.ErrorMessage = "No executable found in batch file";
            }
        }
        catch (Exception ex)
        {
            result.ErrorMessage = $"Failed to parse batch file: {ex.Message}";
        }

        return result;
    }

    private List<string> ExtractExePaths(string line, string batDir)
    {
        var paths = new List<string>();

        // Quoted paths
        foreach (Match match in Regex.Matches(line, @"""([^""]+\.exe)""", RegexOptions.IgnoreCase))
        {
            var path = ResolveVariables(match.Groups[1].Value, batDir);
            if (!string.IsNullOrEmpty(path)) paths.Add(path);
        }

        // Absolute paths (unquoted)
        foreach (Match match in Regex.Matches(line, @"([A-Za-z]:\\[^\s""<>|]+\.exe)", RegexOptions.IgnoreCase))
        {
            var path = match.Groups[1].Value;
            if (!paths.Contains(path, StringComparer.OrdinalIgnoreCase))
                paths.Add(path);
        }

        // %~dp0 paths
        foreach (Match match in Regex.Matches(line, @"%~dp0\\?([^\s""<>|]+\.exe)", RegexOptions.IgnoreCase))
        {
            var relativePath = match.Groups[1].Value;
            var fullPath = Path.GetFullPath(Path.Combine(batDir, relativePath));
            if (!paths.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
                paths.Add(fullPath);
        }

        return paths;
    }

    private string ResolveVariables(string path, string batDir)
    {
        if (string.IsNullOrEmpty(path)) return "";

        // Replace %~dp0
        path = Regex.Replace(path, @"%~dp0\\?", batDir + "\\", RegexOptions.IgnoreCase);
        
        // Expand environment variables
        path = Environment.ExpandEnvironmentVariables(path);

        // Handle relative paths
        if (!Path.IsPathRooted(path))
            path = Path.GetFullPath(Path.Combine(batDir, path));

        return path;
    }

    /// <summary>
    /// Save mapping for batch/wrapper files.
    /// </summary>
    public void SaveMapping(string wrapperPath, string targetExe)
    {
        _configManager?.AddMapping(wrapperPath, targetExe);
    }

    /// <summary>
    /// Save Steam game mapping.
    /// </summary>
    public void SaveSteamMapping(string appId, string exePath)
    {
        _configManager?.AddMapping($"steam://{appId}", exePath);
    }
}
