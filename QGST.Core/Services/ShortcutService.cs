using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using QGST.Core.Models;

namespace QGST.Core.Services;

/// <summary>
/// Service for creating GPU-specific shortcuts and batch file wrappers.
/// </summary>
[SupportedOSPlatform("windows")]
public class ShortcutService
{
    private readonly string _cliExePath;
    private readonly LocalizationService? _localization;

    public ShortcutService(string cliExePath, LocalizationService? localization = null)
    {
        _cliExePath = cliExePath;
        _localization = localization;
    }

    /// <summary>
    /// Create a desktop shortcut that runs an app with a specific GPU
    /// </summary>
    /// <param name="targetExe">Path to the target executable</param>
    /// <param name="gpu">GPU to use</param>
    /// <param name="oneTime">True for one-time run, false for set-default mode</param>
    /// <param name="outputPath">Optional custom path for shortcut</param>
    /// <returns>Path to created shortcut</returns>
    public string CreateShortcut(string targetExe, GpuInfo gpu, bool oneTime = true, string? outputPath = null)
    {
        if (!File.Exists(targetExe))
            throw new FileNotFoundException("Target executable not found", targetExe);

        // Determine shortcut path
        var targetName = Path.GetFileNameWithoutExtension(targetExe);
        var gpuSuffix = gpu.DuplicateIndex > 0 ? $" #{gpu.DuplicateIndex}" : "";
        var shortcutName = $"{targetName} ({gpu.Vendor}{gpuSuffix}).lnk";
        
        var lnkPath = outputPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            shortcutName);

        try
        {
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
                throw new InvalidOperationException("WScript.Shell not available");

            dynamic shell = Activator.CreateInstance(shellType)!;
            try
            {
                dynamic shortcut = shell.CreateShortcut(lnkPath);
                try
                {
                    // Set shortcut to run via qgstctl
                    shortcut.TargetPath = _cliExePath;
                    
                    var mode = oneTime ? "--one-time" : "";
                    shortcut.Arguments = $"run --gpu {gpu.PreferenceValue} --target \"{targetExe}\" {mode}".Trim();
                    
                    shortcut.WorkingDirectory = Path.GetDirectoryName(targetExe);
                    shortcut.IconLocation = targetExe + ",0"; // Use target's icon
                    shortcut.Description = $"Run {targetName} with {gpu.ShortDisplayName}";
                    
                    shortcut.Save();
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
            throw new InvalidOperationException($"Failed to create shortcut: {ex.Message}", ex);
        }

        return lnkPath;
    }

    /// <summary>
    /// Create a batch file wrapper that runs an app with a specific GPU
    /// </summary>
    /// <param name="targetExe">Path to the target executable</param>
    /// <param name="gpu">GPU to use</param>
    /// <param name="oneTime">True for one-time run, false for set-default mode</param>
    /// <param name="outputPath">Optional custom path for batch file</param>
    /// <returns>Path to created batch file</returns>
    public string CreateBatWrapper(string targetExe, GpuInfo gpu, bool oneTime = true, string? outputPath = null)
    {
        if (!File.Exists(targetExe))
            throw new FileNotFoundException("Target executable not found", targetExe);

        // Determine batch file path
        var targetName = Path.GetFileNameWithoutExtension(targetExe);
        var gpuSuffix = gpu.DuplicateIndex > 0 ? $"_{gpu.DuplicateIndex}" : "";
        var batName = $"{targetName}_{gpu.Vendor}{gpuSuffix}.bat";
        
        var batPath = outputPath ?? Path.Combine(
            Path.GetDirectoryName(targetExe) ?? Environment.CurrentDirectory,
            batName);

        var mode = oneTime ? "--one-time" : "";
        
        var batContent = $@"@echo off
REM QGST GPU Wrapper for {targetName}
REM GPU: {gpu.DisplayName}
REM Mode: {(oneTime ? "One-Time" : "Default")}
REM Created: {DateTime.Now:yyyy-MM-dd HH:mm:ss}

""{_cliExePath}"" run --gpu {gpu.PreferenceValue} --target ""{targetExe}"" {mode} --args %*
";

        File.WriteAllText(batPath, batContent);
        
        return batPath;
    }

    /// <summary>
    /// Create a PowerShell script wrapper (alternative to batch)
    /// </summary>
    public string CreatePsWrapper(string targetExe, GpuInfo gpu, bool oneTime = true, string? outputPath = null)
    {
        if (!File.Exists(targetExe))
            throw new FileNotFoundException("Target executable not found", targetExe);

        var targetName = Path.GetFileNameWithoutExtension(targetExe);
        var gpuSuffix = gpu.DuplicateIndex > 0 ? $"_{gpu.DuplicateIndex}" : "";
        var ps1Name = $"{targetName}_{gpu.Vendor}{gpuSuffix}.ps1";
        
        var ps1Path = outputPath ?? Path.Combine(
            Path.GetDirectoryName(targetExe) ?? Environment.CurrentDirectory,
            ps1Name);

        var mode = oneTime ? "--one-time" : "";
        
        var ps1Content = $@"# QGST GPU Wrapper for {targetName}
# GPU: {gpu.DisplayName}
# Mode: {(oneTime ? "One-Time" : "Default")}
# Created: {DateTime.Now:yyyy-MM-dd HH:mm:ss}

$qgstcli = ""{_cliExePath}""
$target = ""{targetExe}""
$gpu = ""{gpu.PreferenceValue}""

& $qgstcli run --gpu $gpu --target $target {mode} --args $args
";

        File.WriteAllText(ps1Path, ps1Content);
        
        return ps1Path;
    }

    /// <summary>
    /// Get suggested shortcut/wrapper names for a target
    /// </summary>
    public (string ShortcutName, string BatName) GetSuggestedNames(string targetExe, GpuInfo gpu)
    {
        var targetName = Path.GetFileNameWithoutExtension(targetExe);
        var gpuSuffix = gpu.DuplicateIndex > 0 ? $" #{gpu.DuplicateIndex}" : "";
        
        return (
            $"{targetName} ({gpu.Vendor}{gpuSuffix}).lnk",
            $"{targetName}_{gpu.Vendor}{(gpu.DuplicateIndex > 0 ? $"_{gpu.DuplicateIndex}" : "")}.bat"
        );
    }
}
