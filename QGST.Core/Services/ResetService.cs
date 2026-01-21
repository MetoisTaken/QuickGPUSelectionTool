using System;
using System.IO;
using System.Runtime.Versioning;

namespace QGST.Core.Services;

/// <summary>
/// Result of a reset operation
/// </summary>
public class ResetResult
{
    public bool Success { get; set; }
    public int PreferencesReset { get; set; }
    public bool ContextMenuRemoved { get; set; }
    public bool DataCleared { get; set; }
    public bool LogsCleared { get; set; }
    public string? BackupPath { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Options for reset operation
/// </summary>
[Flags]
public enum ResetOptions
{
    None = 0,
    Preferences = 1,
    ContextMenu = 2,
    Config = 4,
    Cache = 8,
    Logs = 16,
    State = 32,
    All = Preferences | ContextMenu | Config | Cache | Logs | State
}

/// <summary>
/// Service for resetting QGST changes and restoring original state.
/// </summary>
[SupportedOSPlatform("windows")]
public class ResetService
{
    private readonly ConfigManager _configManager;
    private readonly PreferenceStoreAdapter _prefAdapter;
    private readonly ProcessLauncher _processLauncher;
    private readonly ContextMenuService _contextMenuService;

    public ResetService(
        ConfigManager configManager,
        PreferenceStoreAdapter prefAdapter,
        ProcessLauncher processLauncher,
        string uiExePath)
    {
        _configManager = configManager;
        _prefAdapter = prefAdapter;
        _processLauncher = processLauncher;
        _contextMenuService = new ContextMenuService(uiExePath);
    }

    /// <summary>
    /// Reset all QGST changes with automatic backup
    /// </summary>
    public ResetResult ResetAll(bool createBackup = true)
    {
        return Reset(ResetOptions.All, createBackup);
    }

    /// <summary>
    /// Reset with specific options
    /// </summary>
    public ResetResult Reset(ResetOptions options, bool createBackup = true)
    {
        var result = new ResetResult { Success = true };

        try
        {
            // Create backup before reset
            if (createBackup)
            {
                result.BackupPath = _configManager.CreateBackup($"pre-reset-{DateTime.Now:yyyyMMdd-HHmmss}");
                _configManager.Log($"Created backup before reset: {result.BackupPath}");
            }

            // Reset GPU preferences
            if (options.HasFlag(ResetOptions.Preferences))
            {
                result.PreferencesReset = ResetPreferences();
            }

            // Remove context menu
            if (options.HasFlag(ResetOptions.ContextMenu))
            {
                try
                {
                    _contextMenuService.Unregister();
                    result.ContextMenuRemoved = true;
                    _configManager.Log("Context menu unregistered");
                }
                catch (Exception ex)
                {
                    _configManager.LogError($"Failed to remove context menu: {ex.Message}");
                }
            }

            // Clear state (applied prefs tracking, pending reverts)
            if (options.HasFlag(ResetOptions.State))
            {
                _configManager.ClearState();
            }

            // Clear config
            if (options.HasFlag(ResetOptions.Config))
            {
                _configManager.ClearConfig();
                result.DataCleared = true;
            }

            // Clear cache
            if (options.HasFlag(ResetOptions.Cache))
            {
                _configManager.ClearCache();
            }

            // Clear logs
            if (options.HasFlag(ResetOptions.Logs))
            {
                _configManager.ClearLogs();
                result.LogsCleared = true;
            }

            _configManager.Log($"Reset completed: Options={options}");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            _configManager.LogError($"Reset failed: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Reset only GPU preferences
    /// </summary>
    public int ResetPreferences()
    {
        int count = 0;

        // First, cleanup any pending reverts
        _processLauncher.CleanupPendingReverts();

        // Then reset all tracked applied preferences
        foreach (var pref in _configManager.AppliedPreferences.ToList())
        {
            try
            {
                if (string.IsNullOrEmpty(pref.OriginalPreference))
                {
                    // No original preference - remove the entry
                    _prefAdapter.RemovePreference(pref.ExePath);
                }
                else
                {
                    // Restore original preference
                    _prefAdapter.SetPreference(pref.ExePath, pref.OriginalPreference);
                }
                count++;
                _configManager.Log($"Reset preference for: {pref.ExePath}");
            }
            catch (Exception ex)
            {
                _configManager.LogError($"Failed to reset preference for {pref.ExePath}: {ex.Message}");
            }
        }

        // Clear the tracking
        _configManager.ClearState();

        return count;
    }

    /// <summary>
    /// Reset preference for a specific application
    /// </summary>
    public bool ResetPreferenceForApp(string exePath)
    {
        var pref = _configManager.GetAppliedPreference(exePath);
        if (pref == null)
        {
            // Check if there's any preference set
            var currentPref = _prefAdapter.GetPreference(exePath);
            if (!string.IsNullOrEmpty(currentPref))
            {
                _prefAdapter.RemovePreference(exePath);
                return true;
            }
            return false;
        }

        try
        {
            if (string.IsNullOrEmpty(pref.OriginalPreference))
            {
                _prefAdapter.RemovePreference(exePath);
            }
            else
            {
                _prefAdapter.SetPreference(exePath, pref.OriginalPreference);
            }

            _configManager.RemoveAppliedPreference(exePath);
            _configManager.Log($"Reset individual preference: {exePath}");
            return true;
        }
        catch (Exception ex)
        {
            _configManager.LogError($"Failed to reset preference for {exePath}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Get list of all applications with QGST-applied preferences
    /// </summary>
    public List<string> GetAppliedPreferencesList()
    {
        return _configManager.AppliedPreferences.Select(p => p.ExePath).ToList();
    }

    /// <summary>
    /// Export diagnostics for troubleshooting
    /// </summary>
    public string ExportDiagnostics(string? outputPath = null)
    {
        var diagPath = outputPath ?? Path.Combine(
            _configManager.GetBasePath(),
            "diagnostics",
            $"qgst-diag-{DateTime.Now:yyyyMMdd-HHmmss}.json");

        Directory.CreateDirectory(Path.GetDirectoryName(diagPath)!);

        var diagnostics = new Dictionary<string, object>
        {
            ["Timestamp"] = DateTime.UtcNow,
            ["OSVersion"] = Environment.OSVersion.VersionString,
            ["Is64BitOS"] = Environment.Is64BitOperatingSystem,
            ["MachineName"] = "[MASKED]", // Privacy
            ["PreferenceStoreSupported"] = _prefAdapter.IsSupported(),
            ["PreferenceStoreDiagnostics"] = _prefAdapter.GetDiagnostics(),
            ["AppliedPreferencesCount"] = _configManager.AppliedPreferences.Count,
            ["MappingsCount"] = _configManager.Mappings.Count,
            ["ContextMenuRegistered"] = _contextMenuService.IsRegistered(),
            ["DataPath"] = _configManager.GetBasePath()
        };

        var json = System.Text.Json.JsonSerializer.Serialize(diagnostics, 
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(diagPath, json);

        _configManager.Log($"Diagnostics exported: {diagPath}");
        return diagPath;
    }
}
