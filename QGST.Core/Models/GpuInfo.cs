using System.Text.Json.Serialization;

namespace QGST.Core.Models;

/// <summary>
/// Represents a GPU adapter with identification and display information.
/// </summary>
public class GpuInfo
{
    /// <summary>Unique ID combining LUID + PnP Instance ID</summary>
    public string Id { get; set; } = string.Empty;
    
    /// <summary>Friendly name from driver (e.g., "NVIDIA GeForce RTX 4090")</summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>Vendor identifier: NVIDIA, AMD, INTEL, or GENERIC</summary>
    public string Vendor { get; set; } = string.Empty;
    
    /// <summary>Dedicated video memory in bytes</summary>
    public ulong VramBytes { get; set; }
    
    /// <summary>PCI location (Bus:Device.Function) or slot info</summary>
    public string LocationInfo { get; set; } = string.Empty;
    
    /// <summary>DXGI Adapter index for preference mapping</summary>
    public int AdapterIndex { get; set; } = -1;
    
    /// <summary>PnP Device Instance ID</summary>
    public string PnpInstanceId { get; set; } = string.Empty;
    
    /// <summary>DXGI Adapter LUID (Locally Unique Identifier)</summary>
    public long AdapterLuid { get; set; }
    
    /// <summary>Vendor ID (hex)</summary>
    public string VendorId { get; set; } = string.Empty;
    
    /// <summary>Device ID (hex)</summary>
    public string DeviceId { get; set; } = string.Empty;
    
    /// <summary>Whether this is an integrated GPU</summary>
    public bool IsIntegrated { get; set; }
    
    /// <summary>Duplicate index when same model exists (for disambiguation)</summary>
    public int DuplicateIndex { get; set; } = 0;

    /// <summary>VRAM in GB for display</summary>
    [JsonIgnore]
    public double VramGB => Math.Round(VramBytes / (1024.0 * 1024.0 * 1024.0), 1);

    /// <summary>User-friendly display name with disambiguation</summary>
    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            var baseName = DuplicateIndex > 0 ? $"{Name} #{DuplicateIndex}" : Name;
            return $"{baseName} ({VramGB}GB, {LocationInfo})";
        }
    }

    /// <summary>Short display name for UI lists</summary>
    [JsonIgnore]
    public string ShortDisplayName
    {
        get
        {
            var baseName = DuplicateIndex > 0 ? $"{Name} #{DuplicateIndex}" : Name;
            return baseName;
        }
    }

    /// <summary>GPU preference value for Windows registry (1=PowerSaving, 2=HighPerformance)</summary>
    [JsonIgnore]
    public int PreferenceValue => IsIntegrated ? 1 : 2;

    /// <summary>Registry preference string for this GPU (basic, without LUID)</summary>
    [JsonIgnore]
    public string PreferenceString => $"GpuPreference={PreferenceValue};";

    /// <summary>
    /// Registry preference string with LUID for specific GPU selection.
    /// Use this when multiple GPUs have the same PreferenceValue (e.g., 2 discrete GPUs).
    /// </summary>
    [JsonIgnore]
    public string PreferenceStringWithLuid
    {
        get
        {
            if (AdapterLuid == 0)
                return PreferenceString;
            
            var luidHigh = (int)(AdapterLuid >> 32);
            var luidLow = (int)(AdapterLuid & 0xFFFFFFFF);
            return $"GpuPreference={PreferenceValue};AdapterLuid=0x{luidHigh:X8},0x{luidLow:X8};";
        }
    }

    /// <summary>
    /// Registry preference string with PnP Device ID for specific GPU selection.
    /// Uses Windows native format: "SpecificAdapter=VendorID&DeviceID&SubsysID;GpuPreference=1073741824;"
    /// </summary>
    [JsonIgnore]
    public string PreferenceStringWithDeviceId
    {
        get
        {
            if (string.IsNullOrEmpty(PnpInstanceId))
                return PreferenceString;
            
            // Extract VendorID, DeviceID, and SubsysID from PnP ID
            var match = System.Text.RegularExpressions.Regex.Match(
                PnpInstanceId, 
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
            
            return PreferenceString;
        }
    }
}
