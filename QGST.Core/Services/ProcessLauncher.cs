using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Runtime.Versioning;
using QGST.Core.Models;

namespace QGST.Core.Services;

/// <summary>
/// Service for launching applications with GPU preferences.
/// Uses multiple methods for broad API compatibility:
/// - Windows GPU Preference Store (DirectX 12)
/// - Environment variables (OpenGL, Vulkan, older DirectX)
/// - Vendor-specific settings
/// </summary>
[SupportedOSPlatform("windows")]
public class ProcessLauncher
{
    private readonly PreferenceStoreAdapter _prefAdapter;
    private readonly ConfigManager _configManager;
    private readonly GpuInventoryService _gpuInventory;
    private readonly object _lock = new();

    public ProcessLauncher(PreferenceStoreAdapter prefAdapter, ConfigManager configManager, GpuInventoryService? gpuInventory = null)
    {
        _prefAdapter = prefAdapter;
        _configManager = configManager;
        _gpuInventory = gpuInventory ?? new GpuInventoryService();
    }

    // Legacy constructor for compatibility
    public ProcessLauncher(PreferenceStoreAdapter prefAdapter, ConfigManager configManager) 
        : this(prefAdapter, configManager, null)
    {
    }

    /// <summary>
    /// Run application with GPU preference, reverting after process exits.
    /// Uses multiple methods for maximum compatibility across all graphics APIs.
    /// </summary>
    public async Task<int> RunOneTimeAsync(string exePath, GpuInfo gpu, string arguments = "", string? workingDir = null)
    {
        if (!File.Exists(exePath))
            throw new FileNotFoundException("Executable not found", exePath);

        exePath = Path.GetFullPath(exePath);
        
        // Use PnP Device ID for specific GPU selection (most reliable on Windows 10/11)
        // This allows selecting between multiple discrete GPUs
        var prefString = !string.IsNullOrEmpty(gpu.PnpInstanceId)
            ? gpu.PreferenceStringWithDeviceId
            : PreferenceStoreAdapter.BuildPreferenceString(gpu.PreferenceValue);

        // Get current preference for revert
        var currentPref = _prefAdapter.GetPreference(exePath);

        var revertRecord = new PendingRevert
        {
            ExePath = exePath,
            OriginalPreference = string.IsNullOrEmpty(currentPref) ? "NONE" : currentPref,
            Timestamp = DateTime.UtcNow,
            ProcessId = 0
        };

        lock (_lock)
        {
            _configManager.AddPendingRevert(revertRecord);
        }

        _configManager.Log($"One-time run: {exePath} with GPU={gpu.DisplayName}");

        Process? process = null;
        try
        {
            // Set Windows GPU Preference Store (for DX12 apps)
            _prefAdapter.SetPreference(exePath, prefString);

            // Build process with environment variables for all APIs
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                UseShellExecute = false,
                WorkingDirectory = workingDir ?? Path.GetDirectoryName(exePath) ?? ""
            };

            // Set environment variables for GPU selection across all APIs
            SetGpuEnvironmentVariables(psi, gpu);

            process = Process.Start(psi);

            if (process == null)
                throw new InvalidOperationException("Failed to start process");

            revertRecord.ProcessId = process.Id;
            lock (_lock)
            {
                var reverts = _configManager.LoadPendingReverts();
                var existing = reverts.Find(r => r.ExePath == exePath && r.Timestamp == revertRecord.Timestamp);
                if (existing != null)
                {
                    existing.ProcessId = process.Id;
                    _configManager.SavePendingReverts(reverts);
                }
            }

            _configManager.Log($"Started process PID={process.Id}");

            await process.WaitForExitAsync();
            var exitCode = process.ExitCode;
            _configManager.Log($"Process exited with code {exitCode}");

            return exitCode;
        }
        finally
        {
            try { Revert(revertRecord); }
            catch (Exception ex) { _configManager.LogError($"Failed to revert: {ex.Message}"); }

            lock (_lock)
            {
                _configManager.RemovePendingRevert(revertRecord.ExePath, revertRecord.Timestamp);
            }

            process?.Dispose();
        }
    }

    /// <summary>
    /// Overload using GPU index instead of GpuInfo
    /// </summary>
    public async Task<int> RunOneTimeAsync(string exePath, int gpuIndex, string arguments = "", string? workingDir = null)
    {
        // Find GPU by adapter index first (most common case from context menu)
        var gpus = _gpuInventory.GetGpus();
        var gpu = gpus.FirstOrDefault(g => g.AdapterIndex == gpuIndex)
                  ?? gpus.FirstOrDefault(g => g.PreferenceValue == gpuIndex)
                  ?? gpus.FirstOrDefault();

        if (gpu == null)
        {
            // Fallback to simple registry-only method
            return await RunOneTimeAsyncSimple(exePath, gpuIndex, arguments, workingDir);
        }

        return await RunOneTimeAsync(exePath, gpu, arguments, workingDir);
    }

    private async Task<int> RunOneTimeAsyncSimple(string exePath, int gpuPreferenceValue, string arguments, string? workingDir)
    {
        exePath = Path.GetFullPath(exePath);
        var prefString = PreferenceStoreAdapter.BuildPreferenceString(gpuPreferenceValue);
        var currentPref = _prefAdapter.GetPreference(exePath);

        var revertRecord = new PendingRevert
        {
            ExePath = exePath,
            OriginalPreference = string.IsNullOrEmpty(currentPref) ? "NONE" : currentPref,
            Timestamp = DateTime.UtcNow
        };

        lock (_lock) { _configManager.AddPendingRevert(revertRecord); }

        try
        {
            _prefAdapter.SetPreference(exePath, prefString);

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                UseShellExecute = false,
                WorkingDirectory = workingDir ?? Path.GetDirectoryName(exePath) ?? ""
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                revertRecord.ProcessId = process.Id;
                await process.WaitForExitAsync();
                return process.ExitCode;
            }
            return -1;
        }
        finally
        {
            try { Revert(revertRecord); } catch { }
            lock (_lock) { _configManager.RemovePendingRevert(revertRecord.ExePath, revertRecord.Timestamp); }
        }
    }

    /// <summary>
    /// Set environment variables for GPU selection across all graphics APIs.
    /// </summary>
    private void SetGpuEnvironmentVariables(ProcessStartInfo psi, GpuInfo gpu)
    {
        // Copy current environment
        foreach (System.Collections.DictionaryEntry env in Environment.GetEnvironmentVariables())
        {
            psi.EnvironmentVariables[env.Key?.ToString() ?? ""] = env.Value?.ToString() ?? "";
        }

        switch (gpu.Vendor.ToUpperInvariant())
        {
            case "NVIDIA":
                // NVIDIA: Use adapter index for OpenGL
                // CUDA_VISIBLE_DEVICES for CUDA/compute
                // __NV_PRIME_RENDER_OFFLOAD for render offload (Linux-style but works on some drivers)
                // __GLX_VENDOR_LIBRARY_NAME not used on Windows
                
                // For NVIDIA on Windows, the main method is via GPU Preference Store
                // But we can hint via adapter index
                psi.EnvironmentVariables["CUDA_VISIBLE_DEVICES"] = gpu.AdapterIndex.ToString();
                
                // Some OpenGL applications check DRI_PRIME on Windows too
                psi.EnvironmentVariables["DRI_PRIME"] = gpu.AdapterIndex.ToString();
                break;

            case "AMD":
                // AMD: Use adapter index
                // DRI_PRIME is primarily Linux but some apps check it
                psi.EnvironmentVariables["DRI_PRIME"] = gpu.AdapterIndex.ToString();
                
                // AMD GPU profiler hint
                psi.EnvironmentVariables["GPU_DEVICE_ORDINAL"] = gpu.AdapterIndex.ToString();
                
                // For Vulkan, we can set device selection
                psi.EnvironmentVariables["AMD_VULKAN_ICD"] = gpu.AdapterIndex.ToString();
                break;

            case "INTEL":
                // Intel: Typically index 0 for integrated
                psi.EnvironmentVariables["INTEL_GPU_INDEX"] = gpu.AdapterIndex.ToString();
                break;
        }

        // Generic Vulkan device selection (works for all vendors)
        // VK_ICD_FILENAMES can be used but requires knowing ICD paths
        // Instead, set a hint that some engines respect
        psi.EnvironmentVariables["VULKAN_DEVICE_INDEX"] = gpu.AdapterIndex.ToString();
        
        // OpenGL multi-GPU hint (some drivers respect this)
        psi.EnvironmentVariables["OPENGL_GPU_INDEX"] = gpu.AdapterIndex.ToString();

        // DXVK (for Wine/Proton games on Windows - edge case)
        psi.EnvironmentVariables["DXVK_FILTER_DEVICE_NAME"] = gpu.Name;

        _configManager.Log($"Set GPU env vars: Vendor={gpu.Vendor}, Index={gpu.AdapterIndex}");
    }

    /// <summary>
    /// Run with default GPU (persistent, launches and exits).
    /// </summary>
    public Process? RunWithDefaultGpu(string exePath, GpuInfo gpu, string arguments = "", string? workingDir = null)
    {
        if (!File.Exists(exePath))
            throw new FileNotFoundException("Executable not found", exePath);

        exePath = Path.GetFullPath(exePath);
        
        // Use PnP Device ID for specific GPU selection (most reliable on Windows 10/11)
        var prefString = !string.IsNullOrEmpty(gpu.PnpInstanceId)
            ? gpu.PreferenceStringWithDeviceId
            : PreferenceStoreAdapter.BuildPreferenceString(gpu.PreferenceValue);
        var currentPref = _prefAdapter.GetPreference(exePath);

        _prefAdapter.SetPreference(exePath, prefString);
        _configManager.TrackAppliedPreference(exePath, currentPref, prefString, "set-default");
        _configManager.Log($"Set default GPU: {exePath} -> {gpu.DisplayName} (PnP={gpu.PnpInstanceId})");

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = arguments,
            UseShellExecute = false,
            WorkingDirectory = workingDir ?? Path.GetDirectoryName(exePath) ?? ""
        };

        SetGpuEnvironmentVariables(psi, gpu);

        return Process.Start(psi);
    }

    public Process? RunWithDefaultGpu(string exePath, int gpuPreferenceValue, string arguments = "", string? workingDir = null)
    {
        var gpus = _gpuInventory.GetGpus();
        var gpu = gpus.FirstOrDefault(g => g.PreferenceValue == gpuPreferenceValue)
                  ?? gpus.FirstOrDefault(g => g.AdapterIndex == gpuPreferenceValue)
                  ?? gpus.FirstOrDefault();

        if (gpu != null)
            return RunWithDefaultGpu(exePath, gpu, arguments, workingDir);

        // Fallback
        exePath = Path.GetFullPath(exePath);
        var prefString = PreferenceStoreAdapter.BuildPreferenceString(gpuPreferenceValue);
        _prefAdapter.SetPreference(exePath, prefString);

        return Process.Start(new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = arguments,
            UseShellExecute = true,
            WorkingDirectory = workingDir ?? Path.GetDirectoryName(exePath) ?? ""
        });
    }

    /// <summary>
    /// Set default GPU without running the application.
    /// Uses PnP Device ID for specific GPU targeting on multi-GPU systems.
    /// </summary>
    public void SetDefaultGpu(string exePath, GpuInfo gpu)
    {
        if (!File.Exists(exePath))
            throw new FileNotFoundException("Executable not found", exePath);

        exePath = Path.GetFullPath(exePath);
        
        // Use PnP Device ID for specific GPU selection (most reliable)
        var prefString = !string.IsNullOrEmpty(gpu.PnpInstanceId)
            ? gpu.PreferenceStringWithDeviceId
            : PreferenceStoreAdapter.BuildPreferenceString(gpu.PreferenceValue);
        var currentPref = _prefAdapter.GetPreference(exePath);

        _prefAdapter.SetPreference(exePath, prefString);
        _configManager.TrackAppliedPreference(exePath, currentPref, prefString, "set-default");
        _configManager.Log($"Set default GPU (no run): {exePath} -> {gpu.DisplayName}");
    }

    public void SetDefaultGpu(string exePath, int gpuPreferenceValue)
    {
        var gpus = _gpuInventory.GetGpus();
        var gpu = gpus.FirstOrDefault(g => g.PreferenceValue == gpuPreferenceValue)
                  ?? gpus.FirstOrDefault(g => g.AdapterIndex == gpuPreferenceValue);

        if (gpu != null)
        {
            SetDefaultGpu(exePath, gpu);
            return;
        }

        // Fallback
        exePath = Path.GetFullPath(exePath);
        var prefString = PreferenceStoreAdapter.BuildPreferenceString(gpuPreferenceValue);
        var currentPref = _prefAdapter.GetPreference(exePath);

        _prefAdapter.SetPreference(exePath, prefString);
        _configManager.TrackAppliedPreference(exePath, currentPref, prefString, "set-default");
    }

    public int CleanupPendingReverts()
    {
        var reverts = _configManager.LoadPendingReverts();
        if (reverts.Count == 0) return 0;

        _configManager.Log($"Found {reverts.Count} pending reverts to cleanup");

        int cleaned = 0;
        foreach (var revert in reverts.ToList())
        {
            bool processRunning = false;
            if (revert.ProcessId > 0)
            {
                try
                {
                    var process = Process.GetProcessById(revert.ProcessId);
                    processRunning = !process.HasExited;
                }
                catch { }
            }

            if (!processRunning)
            {
                try
                {
                    Revert(revert);
                    cleaned++;
                }
                catch (Exception ex)
                {
                    _configManager.LogError($"Failed to revert {revert.ExePath}: {ex.Message}");
                }
            }
        }

        _configManager.ClearPendingReverts();
        return cleaned;
    }

    private void Revert(PendingRevert record)
    {
        if (record.OriginalPreference == "NONE")
            _prefAdapter.RemovePreference(record.ExePath);
        else
            _prefAdapter.SetPreference(record.ExePath, record.OriginalPreference);
    }

    public int ResetAllAppliedPreferences()
    {
        var applied = _configManager.AppliedPreferences.ToList();
        int count = 0;

        foreach (var pref in applied)
        {
            try
            {
                if (string.IsNullOrEmpty(pref.OriginalPreference))
                    _prefAdapter.RemovePreference(pref.ExePath);
                else
                    _prefAdapter.SetPreference(pref.ExePath, pref.OriginalPreference);
                count++;
            }
            catch { }
        }

        _configManager.ClearState();
        return count;
    }
}
