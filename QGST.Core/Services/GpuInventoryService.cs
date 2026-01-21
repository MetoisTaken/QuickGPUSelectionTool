using System;
using System.Collections.Generic;
using System.Management;
using System.Linq;
using System.Text.Json;
using System.IO;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using QGST.Core.Models;

namespace QGST.Core.Services;

/// <summary>
/// Service to enumerate and cache GPU information from the system.
/// Uses WMI and Registry for accurate VRAM and PCI slot detection.
/// </summary>
[SupportedOSPlatform("windows")]
public class GpuInventoryService
{
    private readonly string? _cachePath;
    private List<GpuInfo>? _cachedGpus;

    // DXGI for accurate enumeration
    [DllImport("dxgi.dll", SetLastError = true)]
    private static extern int CreateDXGIFactory1([In] ref Guid riid, [Out] out IntPtr ppFactory);

    private static readonly Guid IID_IDXGIFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");

    public GpuInventoryService(string? basePath = null)
    {
        if (!string.IsNullOrEmpty(basePath))
        {
            _cachePath = Path.Combine(basePath, "cache", "gpu_inventory.json");
        }
    }

    /// <summary>
    /// Get list of all GPUs in the system.
    /// </summary>
    public List<GpuInfo> GetGpus(bool forceRefresh = false)
    {
        if (!forceRefresh && _cachedGpus != null)
            return _cachedGpus;

        if (!forceRefresh && _cachePath != null && File.Exists(_cachePath))
        {
            try
            {
                var cacheJson = File.ReadAllText(_cachePath);
                var cached = JsonSerializer.Deserialize<List<GpuInfo>>(cacheJson);
                if (cached != null && cached.Count > 0)
                {
                    _cachedGpus = cached;
                    return _cachedGpus;
                }
            }
            catch { }
        }

        var gpus = EnumerateGpus();
        _cachedGpus = gpus;

        if (_cachePath != null)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
                var json = JsonSerializer.Serialize(gpus, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_cachePath, json);
            }
            catch { }
        }

        return gpus;
    }

    public List<GpuInfo> RefreshGpus() => GetGpus(forceRefresh: true);

    public GpuInfo? FindById(string id)
    {
        return GetGpus().FirstOrDefault(g =>
            g.Id.Equals(id, StringComparison.OrdinalIgnoreCase) ||
            g.AdapterIndex.ToString() == id);
    }

    private List<GpuInfo> EnumerateGpus()
    {
        var gpus = new List<GpuInfo>();

        try
        {
            // Get GPU info from display registry class
            var displayDevices = GetDisplayDevicesFromRegistry();

            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
            using var collection = searcher.Get();

            int index = 0;
            foreach (ManagementObject obj in collection)
            {
                var gpu = new GpuInfo();
                gpu.Name = obj["Name"]?.ToString() ?? "Unknown GPU";
                gpu.PnpInstanceId = obj["PNPDeviceID"]?.ToString() ?? $"GPU_{index}";
                gpu.AdapterIndex = index;
                gpu.Id = $"gpu-{index}";

                // Parse Vendor
                gpu.Vendor = DetermineVendor(gpu.Name, gpu.PnpInstanceId);
                ExtractVendorDeviceIds(gpu.PnpInstanceId, out string vendorId, out string deviceId);
                gpu.VendorId = vendorId;
                gpu.DeviceId = deviceId;

                // Get accurate VRAM from registry (display class)
                // Match by driver description (name) for accuracy
                var displayInfo = displayDevices.FirstOrDefault(d => 
                    d.DriverDesc.Equals(gpu.Name, StringComparison.OrdinalIgnoreCase) ||
                    MatchPnpId(d.DeviceId, gpu.PnpInstanceId));
                
                if (displayInfo != null)
                {
                    gpu.VramBytes = displayInfo.VramBytes;
                }
                
                // Always get location from PnP ID (more reliable than registry)
                gpu.LocationInfo = GetLocationFromPnpId(gpu.PnpInstanceId);
                
                // Fallback to WMI AdapterRAM if no VRAM from registry (capped at 4GB)
                if (gpu.VramBytes == 0)
                {
                    if (ulong.TryParse(obj["AdapterRAM"]?.ToString(), out ulong ram))
                        gpu.VramBytes = ram;
                }

                // If still no VRAM, try registry lookup by driver key
                if (gpu.VramBytes == 0 || gpu.VramBytes == 0xFFFFFFFF)
                {
                    gpu.VramBytes = GetVramFromDriverKey(obj);
                }

                gpu.IsIntegrated = DetermineIfIntegrated(gpu);

                gpus.Add(gpu);
                index++;
            }

            AssignDuplicateIndices(gpus);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error enumerating GPUs: {ex.Message}");
        }

        return gpus;
    }

    private class DisplayDeviceInfo
    {
        public string DeviceId { get; set; } = "";
        public string DriverDesc { get; set; } = "";
        public ulong VramBytes { get; set; }
        public string LocationInfo { get; set; } = "";
    }

    private List<DisplayDeviceInfo> GetDisplayDevicesFromRegistry()
    {
        var devices = new List<DisplayDeviceInfo>();

        try
        {
            // GPU class GUID: {4d36e968-e325-11ce-bfc1-08002be10318}
            using var classKey = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");

            if (classKey == null) return devices;

            foreach (var subKeyName in classKey.GetSubKeyNames())
            {
                if (!int.TryParse(subKeyName, out _)) continue;

                try
                {
                    using var subKey = classKey.OpenSubKey(subKeyName);
                    if (subKey == null) continue;

                    var matchingId = subKey.GetValue("MatchingDeviceId")?.ToString() ?? "";
                    var driverDesc = subKey.GetValue("DriverDesc")?.ToString() ?? "";

                    var info = new DisplayDeviceInfo 
                    { 
                        DeviceId = matchingId,
                        DriverDesc = driverDesc
                    };

                    // Get VRAM - try qwMemorySize first (64-bit QWORD), then MemorySize (32-bit)
                    var qwMemSize = subKey.GetValue("HardwareInformation.qwMemorySize");
                    if (qwMemSize != null)
                    {
                        // Registry QWORD values come as long or byte[]
                        if (qwMemSize is long longVal)
                            info.VramBytes = (ulong)longVal;
                        else if (qwMemSize is ulong ulongVal)
                            info.VramBytes = ulongVal;
                        else if (qwMemSize is byte[] bytes && bytes.Length >= 8)
                            info.VramBytes = BitConverter.ToUInt64(bytes, 0);
                        else if (qwMemSize is int intVal)
                            info.VramBytes = (ulong)intVal;
                    }
                    
                    if (info.VramBytes == 0)
                    {
                        var memSize = subKey.GetValue("HardwareInformation.MemorySize");
                        if (memSize is int intVal)
                            info.VramBytes = (ulong)intVal;
                        else if (memSize is byte[] bytes && bytes.Length >= 4)
                            info.VramBytes = BitConverter.ToUInt32(bytes, 0);
                    }

                    // Get PCI location from Device Parameters or LocationInformation
                    info.LocationInfo = GetLocationFromRegistryKey(subKey);

                    if (!string.IsNullOrEmpty(driverDesc))
                        devices.Add(info);
                }
                catch { }
            }
        }
        catch { }

        return devices;
    }

    private string GetLocationFromRegistryKey(RegistryKey subKey)
    {
        try
        {
            // Try to get location from device parameters
            using var devParams = subKey.OpenSubKey("Device Parameters");
            if (devParams != null)
            {
                var locInfo = devParams.GetValue("LocationInformation")?.ToString();
                if (!string.IsNullOrEmpty(locInfo))
                {
                    // Parse "PCI bus X, device Y, function Z"
                    var match = Regex.Match(locInfo, @"PCI bus (\d+), device (\d+), function (\d+)");
                    if (match.Success)
                    {
                        int bus = int.Parse(match.Groups[1].Value);
                        int device = int.Parse(match.Groups[2].Value);
                        int func = int.Parse(match.Groups[3].Value);
                        return $"{bus:X2}:{device:X2}.{func}";
                    }
                    return locInfo;
                }
            }

            // Try MatchingDeviceId for PCI slot hint
            var matchingId = subKey.GetValue("MatchingDeviceId")?.ToString();
            if (!string.IsNullOrEmpty(matchingId))
            {
                return GetLocationFromPnpId(matchingId);
            }
        }
        catch { }

        return "";
    }

    private string GetLocationFromPnpId(string pnpId)
    {
        if (string.IsNullOrEmpty(pnpId)) return "";

        try
        {
            // PnP ID format: PCI\VEN_XXXX&DEV_YYYY&SUBSYS_ZZZZ&REV_RR\A&BBBB&C&DDDDDDDD
            // The last segment after the final \ contains location info
            var parts = pnpId.Split('\\');
            if (parts.Length >= 3)
            {
                var instancePart = parts.Last();
                // Instance format: X&YYYYYYYY&Z&DDDDDDDD
                var segments = instancePart.Split('&');
                if (segments.Length >= 4)
                {
                    var deviceStr = segments[^1]; // Last element - contains slot info
                    
                    // Parse device number (can be various formats like 08, 00E4, 00000008)
                    if (int.TryParse(deviceStr, System.Globalization.NumberStyles.HexNumber, null, out int rawValue))
                    {
                        // For extended device numbers (like 00E4, 00000008)
                        // Extract the meaningful slot/device part
                        if (rawValue > 0xFF)
                        {
                            // High byte typically indicates PCIe slot
                            int pciSlot = (rawValue >> 3) & 0x1F; // Device number in PCI terms
                            if (pciSlot > 0)
                                return $"PCIe x{pciSlot}";
                            return $"Bus {rawValue:X2}";
                        }
                        else if (rawValue > 0)
                        {
                            // Simple slot number
                            return $"Slot {rawValue}";
                        }
                    }
                }
            }
        }
        catch { }

        return "";
    }

    private bool MatchPnpId(string registryId, string wmiPnpId)
    {
        if (string.IsNullOrEmpty(registryId) || string.IsNullOrEmpty(wmiPnpId))
            return false;

        // Registry has lowercase, WMI has original case
        // Match on VEN_XXXX&DEV_YYYY portion
        var regMatch = Regex.Match(registryId, @"VEN_([0-9A-Fa-f]{4})&DEV_([0-9A-Fa-f]{4})", RegexOptions.IgnoreCase);
        var wmiMatch = Regex.Match(wmiPnpId, @"VEN_([0-9A-Fa-f]{4})&DEV_([0-9A-Fa-f]{4})", RegexOptions.IgnoreCase);

        if (regMatch.Success && wmiMatch.Success)
        {
            return regMatch.Groups[1].Value.Equals(wmiMatch.Groups[1].Value, StringComparison.OrdinalIgnoreCase) &&
                   regMatch.Groups[2].Value.Equals(wmiMatch.Groups[2].Value, StringComparison.OrdinalIgnoreCase);
        }

        return registryId.Contains(wmiPnpId, StringComparison.OrdinalIgnoreCase) ||
               wmiPnpId.Contains(registryId, StringComparison.OrdinalIgnoreCase);
    }

    private ulong GetVramFromDriverKey(ManagementObject obj)
    {
        try
        {
            // Get driver key from WMI
            var adapterRam = obj["AdapterRAM"];
            if (adapterRam != null)
            {
                // WMI returns unsigned 32-bit, but high values wrap
                var ram = Convert.ToUInt64(adapterRam);
                // Check if it's the max uint32 (means >4GB or error)
                if (ram < 0xFFFFFFFF && ram > 0)
                    return ram;
            }

            // Try to get via ConfigManagerUserConfig
            var driverKey = obj["InfSection"]?.ToString();
            if (!string.IsNullOrEmpty(driverKey))
            {
                // Would need to look up in registry
            }
        }
        catch { }

        return 0;
    }

    private string DetermineVendor(string name, string pnpId)
    {
        if (pnpId.Contains("VEN_10DE", StringComparison.OrdinalIgnoreCase)) return "NVIDIA";
        if (pnpId.Contains("VEN_1002", StringComparison.OrdinalIgnoreCase)) return "AMD";
        if (pnpId.Contains("VEN_8086", StringComparison.OrdinalIgnoreCase)) return "Intel";

        if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("RTX", StringComparison.OrdinalIgnoreCase))
            return "NVIDIA";

        if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
            return "AMD";

        if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("UHD", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Iris", StringComparison.OrdinalIgnoreCase))
            return "Intel";

        return "Other";
    }

    private void ExtractVendorDeviceIds(string pnpId, out string vendorId, out string deviceId)
    {
        vendorId = "";
        deviceId = "";

        var venMatch = Regex.Match(pnpId, @"VEN_([0-9A-Fa-f]{4})", RegexOptions.IgnoreCase);
        if (venMatch.Success) vendorId = venMatch.Groups[1].Value.ToUpperInvariant();

        var devMatch = Regex.Match(pnpId, @"DEV_([0-9A-Fa-f]{4})", RegexOptions.IgnoreCase);
        if (devMatch.Success) deviceId = devMatch.Groups[1].Value.ToUpperInvariant();
    }

    private bool DetermineIfIntegrated(GpuInfo gpu)
    {
        // Intel UHD/HD/Iris are integrated (except Arc)
        if (gpu.Vendor == "Intel")
        {
            if (gpu.Name.Contains("Arc", StringComparison.OrdinalIgnoreCase))
                return false; // Arc is discrete
            if (gpu.Name.Contains("UHD", StringComparison.OrdinalIgnoreCase) ||
                gpu.Name.Contains("HD Graphics", StringComparison.OrdinalIgnoreCase) ||
                gpu.Name.Contains("Iris", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // AMD APU graphics (Vega Graphics, Radeon Graphics in APUs)
        if (gpu.Vendor == "AMD")
        {
            if (gpu.Name.Contains("Vega", StringComparison.OrdinalIgnoreCase) &&
                gpu.Name.Contains("Graphics", StringComparison.OrdinalIgnoreCase))
                return true;
            if (gpu.Name.Contains("Radeon(TM) Graphics", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Very low VRAM suggests integrated
        if (gpu.VramBytes > 0 && gpu.VramBytes < 512 * 1024 * 1024)
            return true;

        return false;
    }

    private void AssignDuplicateIndices(List<GpuInfo> gpus)
    {
        var groups = gpus.GroupBy(g => g.Name).Where(g => g.Count() > 1);
        foreach (var group in groups)
        {
            int dupIndex = 1;
            foreach (var gpu in group.OrderBy(g => g.AdapterIndex))
            {
                gpu.DuplicateIndex = dupIndex++;
            }
        }
    }

    public void ClearCache()
    {
        _cachedGpus = null;
        if (_cachePath != null && File.Exists(_cachePath))
        {
            try { File.Delete(_cachePath); } catch { }
        }
    }
}
