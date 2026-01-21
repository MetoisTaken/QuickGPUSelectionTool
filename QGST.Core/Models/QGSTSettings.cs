using System.Text.Json.Serialization;

namespace QGST.Core.Models;

/// <summary>
/// Application settings persisted in settings.json
/// </summary>
public class QGSTSettings
{
    /// <summary>Language code: "auto", "tr", "en"</summary>
    public string Language { get; set; } = "auto";
    
    /// <summary>Last selected GPU ID for quick selection</summary>
    public string LastSelectedGpuId { get; set; } = string.Empty;
    
    /// <summary>Enable automatic update checking</summary>
    public bool CheckUpdates { get; set; } = true;
    
    /// <summary>Theme preference: "dark", "light", "system"</summary>
    public string Theme { get; set; } = "dark";
    
    /// <summary>Mask user paths in logs for privacy</summary>
    public bool MaskPathsInLogs { get; set; } = true;
    
    /// <summary>Default mode: "one-time" or "set-default"</summary>
    public string DefaultMode { get; set; } = "one-time";
    
    /// <summary>Recent targets (last 20)</summary>
    public List<RecentTarget> RecentTargets { get; set; } = new();
    
    /// <summary>Favorite GPU IDs for quick access</summary>
    public List<string> FavoriteGpuIds { get; set; } = new();
}

/// <summary>
/// Mapping from wrapper file (bat/lnk) to resolved exe path
/// </summary>
public class AppMapping
{
    /// <summary>Path to .bat, .cmd, or .lnk file</summary>
    public string WrapperPath { get; set; } = string.Empty;
    
    /// <summary>Resolved .exe path</summary>
    public string TargetExePath { get; set; } = string.Empty;
    
    /// <summary>When this mapping was created</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Record of a recent target for history
/// </summary>
public class RecentTarget
{
    public string TargetPath { get; set; } = string.Empty;
    public string ResolvedExePath { get; set; } = string.Empty;
    public string LastGpuId { get; set; } = string.Empty;
    public string Mode { get; set; } = "one-time";
    public DateTime LastUsed { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Record of an applied GPU preference (for reset tracking)
/// </summary>
public class AppliedPreference
{
    /// <summary>Path to the executable</summary>
    public string ExePath { get; set; } = string.Empty;
    
    /// <summary>Original preference before QGST modified it (empty if none existed)</summary>
    public string OriginalPreference { get; set; } = string.Empty;
    
    /// <summary>Preference value set by QGST</summary>
    public string AppliedValue { get; set; } = string.Empty;
    
    /// <summary>When this preference was applied</summary>
    public DateTime AppliedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>Type of application: "set-default" or "one-time"</summary>
    public string Mode { get; set; } = "set-default";
}

/// <summary>
/// Pending revert record for crash-safe one-time execution
/// </summary>
public class PendingRevert
{
    public string ExePath { get; set; } = string.Empty;
    
    /// <summary>"NONE" if no previous preference existed</summary>
    public string OriginalPreference { get; set; } = string.Empty;
    
    public DateTime Timestamp { get; set; }
    
    /// <summary>Process ID of the launched application</summary>
    public int ProcessId { get; set; }
}
