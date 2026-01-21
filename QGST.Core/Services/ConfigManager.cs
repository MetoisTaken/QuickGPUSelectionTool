using System;
using System.IO;
using System.Text.Json;
using QGST.Core.Models;

namespace QGST.Core.Services;

/// <summary>
/// Manages all QGST configuration, state, and data files.
/// All files are stored under a single base directory.
/// </summary>
public class ConfigManager
{
    private readonly string _basePath;
    private readonly string _configDir;
    private readonly string _stateDir;
    private readonly string _cacheDir;
    private readonly string _logsDir;
    private readonly string _backupDir;
    private readonly string _localesDir;

    private readonly string _settingsPath;
    private readonly string _mappingsPath;
    private readonly string _appliedPrefsPath;
    private readonly string _pendingRevertPath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public QGSTSettings Settings { get; private set; } = new();
    public List<AppMapping> Mappings { get; private set; } = new();
    public List<AppliedPreference> AppliedPreferences { get; private set; } = new();

    /// <summary>
    /// Create ConfigManager with default path (%LOCALAPPDATA%\QGST)
    /// </summary>
    public ConfigManager() : this(null) { }

    /// <summary>
    /// Create ConfigManager with custom base path (for portable mode)
    /// </summary>
    public ConfigManager(string? customBasePath)
    {
        if (!string.IsNullOrEmpty(customBasePath))
        {
            _basePath = customBasePath;
        }
        else
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _basePath = Path.Combine(localAppData, "QGST");
        }

        // Define all directory paths
        _configDir = Path.Combine(_basePath, "config");
        _stateDir = Path.Combine(_basePath, "state");
        _cacheDir = Path.Combine(_basePath, "cache");
        _logsDir = Path.Combine(_basePath, "logs");
        _backupDir = Path.Combine(_basePath, "backup");
        _localesDir = Path.Combine(_basePath, "locales");

        // Define file paths
        _settingsPath = Path.Combine(_configDir, "settings.json");
        _mappingsPath = Path.Combine(_configDir, "mappings.json");
        _appliedPrefsPath = Path.Combine(_stateDir, "applied_prefs.json");
        _pendingRevertPath = Path.Combine(_stateDir, "pending_revert.json");

        EnsureDirectories();
        LoadAll();
    }

    private void EnsureDirectories()
    {
        Directory.CreateDirectory(_configDir);
        Directory.CreateDirectory(_stateDir);
        Directory.CreateDirectory(_cacheDir);
        Directory.CreateDirectory(_logsDir);
        Directory.CreateDirectory(_backupDir);
        Directory.CreateDirectory(_localesDir);
    }

    private void LoadAll()
    {
        Settings = LoadJson<QGSTSettings>(_settingsPath) ?? new QGSTSettings();
        Mappings = LoadJson<List<AppMapping>>(_mappingsPath) ?? new List<AppMapping>();
        AppliedPreferences = LoadJson<List<AppliedPreference>>(_appliedPrefsPath) ?? new List<AppliedPreference>();
    }

    private T? LoadJson<T>(string path) where T : class
    {
        if (!File.Exists(path)) return null;
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private void SaveJson<T>(string path, T data)
    {
        try
        {
            var json = JsonSerializer.Serialize(data, JsonOptions);
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            LogError($"Failed to save {path}: {ex.Message}");
        }
    }

    #region Settings

    public void SaveSettings()
    {
        SaveJson(_settingsPath, Settings);
    }

    public void UpdateLanguage(string language)
    {
        Settings.Language = language;
        SaveSettings();
    }

    #endregion

    #region Mappings

    public void SaveMappings()
    {
        SaveJson(_mappingsPath, Mappings);
    }

    public void AddMapping(string wrapperPath, string targetExe)
    {
        // Remove existing mapping for this wrapper
        Mappings.RemoveAll(m => m.WrapperPath.Equals(wrapperPath, StringComparison.OrdinalIgnoreCase));
        
        Mappings.Add(new AppMapping
        {
            WrapperPath = wrapperPath,
            TargetExePath = targetExe,
            CreatedAt = DateTime.UtcNow
        });
        SaveMappings();
    }

    public string? GetMappedTarget(string wrapperPath)
    {
        var mapping = Mappings.FirstOrDefault(m => 
            m.WrapperPath.Equals(wrapperPath, StringComparison.OrdinalIgnoreCase));
        return mapping?.TargetExePath;
    }

    #endregion

    #region Applied Preferences Tracking

    public void SaveAppliedPreferences()
    {
        SaveJson(_appliedPrefsPath, AppliedPreferences);
    }

    public void TrackAppliedPreference(string exePath, string originalPref, string appliedValue, string mode)
    {
        // Remove existing entry for this exe
        AppliedPreferences.RemoveAll(p => p.ExePath.Equals(exePath, StringComparison.OrdinalIgnoreCase));
        
        AppliedPreferences.Add(new AppliedPreference
        {
            ExePath = exePath,
            OriginalPreference = originalPref,
            AppliedValue = appliedValue,
            AppliedAt = DateTime.UtcNow,
            Mode = mode
        });
        SaveAppliedPreferences();
    }

    public void RemoveAppliedPreference(string exePath)
    {
        AppliedPreferences.RemoveAll(p => p.ExePath.Equals(exePath, StringComparison.OrdinalIgnoreCase));
        SaveAppliedPreferences();
    }

    public AppliedPreference? GetAppliedPreference(string exePath)
    {
        return AppliedPreferences.FirstOrDefault(p => 
            p.ExePath.Equals(exePath, StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region Pending Reverts

    public List<PendingRevert> LoadPendingReverts()
    {
        return LoadJson<List<PendingRevert>>(_pendingRevertPath) ?? new List<PendingRevert>();
    }

    public void SavePendingReverts(List<PendingRevert> reverts)
    {
        SaveJson(_pendingRevertPath, reverts);
    }

    public void AddPendingRevert(PendingRevert revert)
    {
        var reverts = LoadPendingReverts();
        reverts.Add(revert);
        SavePendingReverts(reverts);
    }

    public void RemovePendingRevert(string exePath, DateTime timestamp)
    {
        var reverts = LoadPendingReverts();
        reverts.RemoveAll(r => r.ExePath == exePath && r.Timestamp == timestamp);
        SavePendingReverts(reverts);
    }

    public void ClearPendingReverts()
    {
        SavePendingReverts(new List<PendingRevert>());
    }

    #endregion

    #region Recent Targets

    public void AddRecentTarget(string targetPath, string resolvedExe, string gpuId, string mode)
    {
        // Remove existing entry for this target
        Settings.RecentTargets.RemoveAll(r => 
            r.TargetPath.Equals(targetPath, StringComparison.OrdinalIgnoreCase));
        
        // Add to beginning
        Settings.RecentTargets.Insert(0, new RecentTarget
        {
            TargetPath = targetPath,
            ResolvedExePath = resolvedExe,
            LastGpuId = gpuId,
            Mode = mode,
            LastUsed = DateTime.UtcNow
        });

        // Keep only last 20
        if (Settings.RecentTargets.Count > 20)
        {
            Settings.RecentTargets = Settings.RecentTargets.Take(20).ToList();
        }

        SaveSettings();
    }

    #endregion

    #region Backup & Export

    public string CreateBackup(string? customName = null)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmm");
        var backupName = customName ?? timestamp;
        var backupPath = Path.Combine(_backupDir, backupName);
        
        Directory.CreateDirectory(backupPath);

        // Copy all config and state files
        CopyIfExists(_settingsPath, Path.Combine(backupPath, "settings.json"));
        CopyIfExists(_mappingsPath, Path.Combine(backupPath, "mappings.json"));
        CopyIfExists(_appliedPrefsPath, Path.Combine(backupPath, "applied_prefs.json"));

        return backupPath;
    }

    public void RestoreBackup(string backupPath)
    {
        if (!Directory.Exists(backupPath))
            throw new DirectoryNotFoundException($"Backup not found: {backupPath}");

        CopyIfExists(Path.Combine(backupPath, "settings.json"), _settingsPath);
        CopyIfExists(Path.Combine(backupPath, "mappings.json"), _mappingsPath);
        CopyIfExists(Path.Combine(backupPath, "applied_prefs.json"), _appliedPrefsPath);

        LoadAll(); // Reload after restore
    }

    private void CopyIfExists(string source, string dest)
    {
        if (File.Exists(source))
        {
            File.Copy(source, dest, overwrite: true);
        }
    }

    #endregion

    #region Logging

    public void Log(string message, string level = "INFO")
    {
        var logFile = Path.Combine(_logsDir, $"qgst-{DateTime.Now:yyyyMMdd}.log");
        var logLine = $"[{DateTime.Now:HH:mm:ss}] [{level}] {MaskPaths(message)}";
        
        try
        {
            File.AppendAllText(logFile, logLine + Environment.NewLine);
        }
        catch { }
    }

    public void LogError(string message) => Log(message, "ERROR");
    public void LogWarning(string message) => Log(message, "WARN");

    private string MaskPaths(string message)
    {
        if (!Settings.MaskPathsInLogs) return message;
        
        // Mask username in paths
        var username = Environment.UserName;
        return message.Replace(username, "[USER]");
    }

    #endregion

    #region Reset

    public void ClearConfig()
    {
        Settings = new QGSTSettings();
        Mappings = new List<AppMapping>();
        SaveSettings();
        SaveMappings();
    }

    public void ClearState()
    {
        AppliedPreferences = new List<AppliedPreference>();
        SaveAppliedPreferences();
        ClearPendingReverts();
    }

    public void ClearCache()
    {
        try
        {
            foreach (var file in Directory.GetFiles(_cacheDir))
            {
                File.Delete(file);
            }
        }
        catch { }
    }

    public void ClearLogs()
    {
        try
        {
            foreach (var file in Directory.GetFiles(_logsDir))
            {
                File.Delete(file);
            }
        }
        catch { }
    }

    public void ClearAll()
    {
        ClearConfig();
        ClearState();
        ClearCache();
        ClearLogs();
    }

    #endregion

    #region Path Accessors

    public string GetBasePath() => _basePath;
    public string GetConfigDir() => _configDir;
    public string GetStateDir() => _stateDir;
    public string GetCacheDir() => _cacheDir;
    public string GetLogsDir() => _logsDir;
    public string GetBackupDir() => _backupDir;
    public string GetLocalesDir() => _localesDir;
    public string GetPendingRevertPath() => _pendingRevertPath;

    #endregion
}
