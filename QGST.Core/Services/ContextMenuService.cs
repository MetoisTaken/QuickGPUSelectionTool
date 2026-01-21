using Microsoft.Win32;
using System.IO;
using System.Runtime.Versioning;

namespace QGST.Core.Services;

/// <summary>
/// Service for registering cascading Windows Explorer context menu with GPU selection.
/// Creates a single "QGST" menu with sub-items for each GPU.
/// </summary>
[SupportedOSPlatform("windows")]
public class ContextMenuService
{
    private readonly string _cliExePath;
    private readonly LocalizationService? _localization;
    private readonly GpuInventoryService _gpuInventory;

    private static readonly string[] SupportedExtensions = { ".exe", ".lnk", ".bat", ".cmd", ".url" };

    private static readonly Dictionary<string, string> ExtensionProgIds = new()
    {
        [".exe"] = "exefile",
        [".lnk"] = "lnkfile",
        [".bat"] = "batfile",
        [".cmd"] = "cmdfile",
        [".url"] = "InternetShortcut"
    };

    // Main menu key
    private const string QGSTMenuKey = "QGST";

    public ContextMenuService(string cliExePath, LocalizationService? localization = null, GpuInventoryService? gpuInventory = null)
    {
        _cliExePath = cliExePath;
        _localization = localization;
        _gpuInventory = gpuInventory ?? new GpuInventoryService();
    }

    /// <summary>
    /// Register cascading context menu with GPU options for current user.
    /// </summary>
    public void Register()
    {
        // Get CLI path - prefer qgst.exe next to UI
        var cliPath = GetCliPath();
        if (string.IsNullOrEmpty(cliPath))
        {
            throw new FileNotFoundException("CLI executable not found. Expected qgst.exe next to UI.");
        }

        var gpus = _gpuInventory.GetGpus();

        foreach (var ext in SupportedExtensions)
        {
            RegisterForExtension(ext, Registry.CurrentUser, gpus, cliPath);
        }
    }

    /// <summary>
    /// Register for all users (requires admin).
    /// </summary>
    public void RegisterForAllUsers()
    {
        var cliPath = GetCliPath();
        if (string.IsNullOrEmpty(cliPath))
            throw new FileNotFoundException("CLI executable not found.");

        var gpus = _gpuInventory.GetGpus();

        foreach (var ext in SupportedExtensions)
        {
            RegisterForExtension(ext, Registry.LocalMachine, gpus, cliPath);
        }
    }

    /// <summary>
    /// Unregister context menu for current user.
    /// </summary>
    public void Unregister()
    {
        foreach (var ext in SupportedExtensions)
        {
            UnregisterForExtension(ext, Registry.CurrentUser);
        }
    }

    public void UnregisterForAllUsers()
    {
        foreach (var ext in SupportedExtensions)
        {
            UnregisterForExtension(ext, Registry.LocalMachine);
        }
    }

    public bool IsRegistered()
    {
        try
        {
            using var shellKey = Registry.CurrentUser.OpenSubKey(@"Software\Classes\exefile\shell");
            if (shellKey == null) return false;
            return shellKey.GetSubKeyNames().Any(n => n.StartsWith("QGST", StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    /// <summary>
    /// Refresh menu with current GPU list (re-register).
    /// </summary>
    public void Refresh()
    {
        _gpuInventory.RefreshGpus();
        Unregister();
        Register();
    }

    private void RegisterForExtension(string ext, RegistryKey rootKey, List<Models.GpuInfo> gpus, string cliPath)
    {
        if (!ExtensionProgIds.TryGetValue(ext, out var progId)) return;

        try
        {
            var shellPath = $@"Software\Classes\{progId}\shell";

            // Create main QGST menu with cascading sub-items
            var mainMenuPath = $@"{shellPath}\QGST";
            using var mainMenuKey = rootKey.CreateSubKey(mainMenuPath);
            if (mainMenuKey == null) return;

            mainMenuKey.SetValue("MUIVerb", "QGST - GPU Selector");
            mainMenuKey.SetValue("Icon", cliPath);
            mainMenuKey.SetValue("SubCommands", ""); // Empty = look for shell subkey

            // Create shell subkey for sub-items
            using var subShellKey = mainMenuKey.CreateSubKey("shell");
            if (subShellKey == null) return;

            int order = 0;

            // === Run with GPU section (one-time) ===
            foreach (var gpu in gpus)
            {
                var runKeyName = $"A_Run_{order:D2}";
                using var runKey = subShellKey.CreateSubKey(runKeyName);
                if (runKey != null)
                {
                    var label = GetLocalizedString("run_with", "Run with");
                    runKey.SetValue("MUIVerb", $"▶ {label}: {GetGpuMenuDisplayName(gpu)}");
                    runKey.SetValue("Icon", cliPath);
                    
                    using var cmdKey = runKey.CreateSubKey("command");
                    cmdKey?.SetValue("", $"\"{cliPath}\" run --target \"%1\" --gpu {gpu.AdapterIndex} --one-time");
                }
                order++;
            }

            // === Set Default section (permanent) ===
            foreach (var gpu in gpus)
            {
                var defaultKeyName = $"B_Default_{order:D2}";
                using var defaultKey = subShellKey.CreateSubKey(defaultKeyName);
                if (defaultKey != null)
                {
                    var label = GetLocalizedString("set_default_for", "Set default");
                    defaultKey.SetValue("MUIVerb", $"📌 {label}: {GetGpuMenuDisplayName(gpu)}");
                    defaultKey.SetValue("Icon", cliPath);
                    
                    using var cmdKey = defaultKey.CreateSubKey("command");
                    cmdKey?.SetValue("", $"\"{cliPath}\" set-default --target \"%1\" --gpu {gpu.AdapterIndex}");
                }
                order++;
            }

            // === Reset ===
            var resetKeyName = $"C_Reset_{order:D2}";
            using var resetKey = subShellKey.CreateSubKey(resetKeyName);
            if (resetKey != null)
            {
                resetKey.SetValue("MUIVerb", "🔄 " + GetLocalizedString("reset_preference", "Reset GPU Preference"));
                resetKey.SetValue("Icon", cliPath);
                
                using var cmdKey = resetKey.CreateSubKey("command");
                cmdKey?.SetValue("", $"\"{cliPath}\" reset --target \"%1\"");
            }
        }
        catch
        {
            // Ignore individual extension failures
        }
    }

    private string GetGpuMenuDisplayName(Models.GpuInfo gpu)
    {
        var typeLabel = gpu.IsIntegrated ? "[iGPU]" : "[dGPU]";
        
        // Short name for menu
        var shortName = gpu.Name;
        
        // Trim common prefixes
        shortName = shortName.Replace("NVIDIA ", "").Replace("AMD ", "").Replace("Intel(R) ", "Intel ");
        
        if (gpu.DuplicateIndex > 0)
            return $"{shortName} #{gpu.DuplicateIndex} {typeLabel}";
        
        return $"{shortName} {typeLabel}";
    }

    private void UnregisterForExtension(string ext, RegistryKey rootKey)
    {
        if (!ExtensionProgIds.TryGetValue(ext, out var progId)) return;

        try
        {
            var shellPath = $@"Software\Classes\{progId}\shell";
            using var shellKey = rootKey.OpenSubKey(shellPath, writable: true);
            if (shellKey == null) return;

            // Delete all QGST-prefixed keys (flat menu structure)
            foreach (var subKeyName in shellKey.GetSubKeyNames())
            {
                if (subKeyName.StartsWith("QGST", StringComparison.OrdinalIgnoreCase))
                {
                    SafeDeleteSubKeyTree(rootKey, $@"{shellPath}\{subKeyName}");
                }
            }
        }
        catch { }
    }

    private void SafeDeleteSubKeyTree(RegistryKey rootKey, string keyPath)
    {
        try
        {
            rootKey.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
        }
        catch { }
    }

    private string GetCliPath()
    {
        // If CLI path was provided directly
        if (!string.IsNullOrEmpty(_cliExePath) && File.Exists(_cliExePath))
            return _cliExePath;

        // Look for qgst.exe next to the provided path (which might be UI)
        if (!string.IsNullOrEmpty(_cliExePath))
        {
            var dir = Path.GetDirectoryName(_cliExePath);
            if (dir != null)
            {
                var cliPath = Path.Combine(dir, "qgst.exe");
                if (File.Exists(cliPath)) return cliPath;
            }
        }

        // Try current directory
        var currentDir = AppDomain.CurrentDomain.BaseDirectory;
        var localCli = Path.Combine(currentDir, "qgst.exe");
        if (File.Exists(localCli)) return localCli;

        return "";
    }

    private string GetLocalizedString(string key, string fallback)
    {
        if (_localization != null && _localization.HasKey(key))
            return _localization.GetString(key);
        return fallback;
    }

    public List<string> GetRegistryKeysPreview()
    {
        var keys = new List<string>();
        foreach (var ext in SupportedExtensions)
        {
            if (ExtensionProgIds.TryGetValue(ext, out var progId))
            {
                keys.Add($@"HKCU\Software\Classes\{progId}\shell\{QGSTMenuKey}");
            }
        }
        return keys;
    }
}
