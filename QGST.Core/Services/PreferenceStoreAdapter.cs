using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Runtime.Versioning;

namespace QGST.Core.Services;

/// <summary>
/// Adapter for Windows GPU Preference Store (DirectX UserGpuPreferences).
/// Handles reading, writing, and removing GPU preferences for applications.
/// </summary>
[SupportedOSPlatform("windows")]
public class PreferenceStoreAdapter
{
    // Windows GPU Preference Registry Key
    private const string UserGpuPreferencesKey = @"Software\Microsoft\DirectX\UserGpuPreferences";
    
    // Alternative paths for older Windows versions or specific scenarios
    private const string GraphicsSettingsKey = @"Software\Microsoft\DirectX\GraphicsSettings";

    /// <summary>
    /// GPU Preference values used by Windows
    /// </summary>
    public static class GpuPreferenceValue
    {
        /// <summary>Let Windows decide</summary>
        public const int Default = 0;
        /// <summary>Power saving (typically integrated GPU)</summary>
        public const int PowerSaving = 1;
        /// <summary>High performance (typically discrete GPU)</summary>
        public const int HighPerformance = 2;
    }

    /// <summary>
    /// Check if GPU preference store is supported on this Windows version
    /// </summary>
    public bool IsSupported()
    {
        try
        {
            // Check Windows version - GPU preferences added in Windows 10 1803+
            var osVersion = Environment.OSVersion.Version;
            if (osVersion.Major < 10) return false;

            // Try to open the key
            using var key = Registry.CurrentUser.OpenSubKey(UserGpuPreferencesKey);
            return true; // Key exists or can be created
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Get the current GPU preference for an application
    /// </summary>
    /// <param name="exePath">Full path to the executable</param>
    /// <returns>Preference string or empty if none set</returns>
    public string GetPreference(string exePath)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UserGpuPreferencesKey);
            if (key != null)
            {
                var val = key.GetValue(exePath);
                return val?.ToString() ?? string.Empty;
            }
        }
        catch { }
        return string.Empty;
    }

    /// <summary>
    /// Set GPU preference for an application
    /// </summary>
    /// <param name="exePath">Full path to the executable</param>
    /// <param name="preferenceString">Preference string (e.g., "GpuPreference=2;")</param>
    public void SetPreference(string exePath, string preferenceString)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(UserGpuPreferencesKey);
            key?.SetValue(exePath, preferenceString, RegistryValueKind.String);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to set GPU preference for {exePath}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Set GPU preference using numeric value
    /// </summary>
    /// <param name="exePath">Full path to the executable</param>
    /// <param name="preference">0=Default, 1=PowerSaving, 2=HighPerformance</param>
    public void SetPreference(string exePath, int preference)
    {
        SetPreference(exePath, $"GpuPreference={preference};");
    }

    /// <summary>
    /// Remove GPU preference for an application
    /// </summary>
    /// <param name="exePath">Full path to the executable</param>
    /// <returns>True if removed, false if didn't exist</returns>
    public bool RemovePreference(string exePath)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UserGpuPreferencesKey, writable: true);
            if (key != null && key.GetValue(exePath) != null)
            {
                key.DeleteValue(exePath);
                return true;
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Get all applications that have GPU preferences set
    /// </summary>
    public Dictionary<string, string> GetAllPreferences()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UserGpuPreferencesKey);
            if (key != null)
            {
                foreach (var valueName in key.GetValueNames())
                {
                    var value = key.GetValue(valueName)?.ToString();
                    if (!string.IsNullOrEmpty(value))
                    {
                        result[valueName] = value;
                    }
                }
            }
        }
        catch { }

        return result;
    }

    /// <summary>
    /// Parse the GPU preference value from a preference string
    /// </summary>
    public static int ParsePreferenceValue(string preferenceString)
    {
        // Format: "GpuPreference=X;" where X is 0, 1, or 2
        if (string.IsNullOrEmpty(preferenceString)) return GpuPreferenceValue.Default;
        
        var match = System.Text.RegularExpressions.Regex.Match(
            preferenceString, @"GpuPreference=(\d+)");
        
        if (match.Success && int.TryParse(match.Groups[1].Value, out int value))
        {
            return value;
        }
        
        return GpuPreferenceValue.Default;
    }

    /// <summary>
    /// Build preference string from value
    /// </summary>
    public static string BuildPreferenceString(int preferenceValue)
    {
        return $"GpuPreference={preferenceValue};";
    }

    /// <summary>
    /// Build preference string with specific GPU LUID for multi-GPU systems.
    /// This allows selecting a specific GPU even when multiple discrete GPUs exist.
    /// Format: "GpuPreference=2;AdapterLuid=XXXX;" where XXXX is the GPU's LUID in hex.
    /// </summary>
    public static string BuildPreferenceStringWithLuid(int preferenceValue, long adapterLuid)
    {
        if (adapterLuid == 0)
        {
            return BuildPreferenceString(preferenceValue);
        }
        
        // LUID format: high:low as hex (e.g., "0x00000000,0x0000D77C")
        var luidHigh = (int)(adapterLuid >> 32);
        var luidLow = (int)(adapterLuid & 0xFFFFFFFF);
        
        return $"GpuPreference={preferenceValue};AdapterLuid=0x{luidHigh:X8},0x{luidLow:X8};";
    }

    /// <summary>
    /// Build preference string with specific GPU using PnP Device ID.
    /// This is the most reliable method for Windows 10/11 multi-GPU selection.
    /// Windows uses format: "SpecificAdapter=VendorID&DeviceID&SubsysID;GpuPreference=1073741824;"
    /// where 1073741824 (0x40000000) means "use specific GPU"
    /// </summary>
    public static string BuildPreferenceStringWithDeviceId(int preferenceValue, string pnpDeviceId)
    {
        if (string.IsNullOrEmpty(pnpDeviceId))
        {
            return BuildPreferenceString(preferenceValue);
        }
        
        // Extract VendorID, DeviceID, and SubsysID from PnP ID
        // Format: PCI\VEN_10DE&DEV_1B87&SUBSYS_123710DE&REV_A1\...
        var match = System.Text.RegularExpressions.Regex.Match(
            pnpDeviceId, 
            @"VEN_([0-9A-F]+)&DEV_([0-9A-F]+)&SUBSYS_([0-9A-F]+)", 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        if (match.Success)
        {
            var vendorId = match.Groups[1].Value;
            var deviceId = match.Groups[2].Value;
            var subsysId = match.Groups[3].Value;
            
            // Windows uses 0x40000000 (1073741824) for "specific GPU"
            return $"SpecificAdapter={vendorId}&{deviceId}&{subsysId};GpuPreference=1073741824;";
        }
        
        // Fallback to basic preference
        return BuildPreferenceString(preferenceValue);
    }

    /// <summary>
    /// Get human-readable description of preference
    /// </summary>
    public static string GetPreferenceDescription(int preferenceValue)
    {
        return preferenceValue switch
        {
            GpuPreferenceValue.PowerSaving => "Power Saving (Integrated)",
            GpuPreferenceValue.HighPerformance => "High Performance (Discrete)",
            _ => "Let Windows Decide"
        };
    }

    /// <summary>
    /// Analyze current state for diagnostics
    /// </summary>
    public Dictionary<string, object> GetDiagnostics()
    {
        var diag = new Dictionary<string, object>
        {
            ["IsSupported"] = IsSupported(),
            ["OSVersion"] = Environment.OSVersion.VersionString,
            ["PreferenceCount"] = GetAllPreferences().Count
        };

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UserGpuPreferencesKey);
            diag["RegistryKeyExists"] = key != null;
        }
        catch (Exception ex)
        {
            diag["RegistryError"] = ex.Message;
        }

        return diag;
    }
}
