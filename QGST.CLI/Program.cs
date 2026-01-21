using System;
using System.Text.Json;
using System.IO;
using System.Threading.Tasks;
using QGST.Core.Services;
using QGST.Core.Models;

namespace QGST.CLI;

class Program
{
    // Lazy-loaded services - only initialized when needed
    private static ConfigManager? _config;
    private static LocalizationService? _loc;
    private static GpuInventoryService? _gpuInventory;
    private static TargetResolver? _resolver;
    private static PreferenceStoreAdapter? _prefAdapter;
    private static ProcessLauncher? _launcher;
    private static ResetService? _resetService;

    // Lazy accessors
    private static ConfigManager Config => _config ??= new ConfigManager();
    private static LocalizationService Loc => _loc ??= InitLocalization();
    private static GpuInventoryService GpuInventory => _gpuInventory ??= new GpuInventoryService(Config.GetBasePath());
    private static TargetResolver Resolver => _resolver ??= new TargetResolver(Config);
    private static PreferenceStoreAdapter PrefAdapter => _prefAdapter ??= new PreferenceStoreAdapter();
    private static ProcessLauncher Launcher => _launcher ??= new ProcessLauncher(PrefAdapter, Config, GpuInventory);
    private static ResetService ResetSvc => _resetService ??= InitResetService();

    private static LocalizationService InitLocalization()
    {
        var loc = new LocalizationService(Config.GetBasePath());
        loc.LoadLanguage(Config.Settings.Language);
        return loc;
    }

    private static ResetService InitResetService()
    {
        var cliExePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
        if (cliExePath.EndsWith(".dll")) cliExePath = cliExePath.Replace(".dll", ".exe");
        return new ResetService(Config, PrefAdapter, Launcher, cliExePath);
    }

    static async Task<int> Main(string[] args)
    {
        // Parse arguments first - don't initialize anything yet
        return await ParseAndExecute(args);
    }

    static async Task<int> ParseAndExecute(string[] args)
    {
        if (args.Length == 0)
        {
            ShowHelp();
            return 0;
        }

        var command = args[0].ToLowerInvariant();
        var options = ParseOptions(args.Skip(1).ToArray());

        try
        {
            return command switch
            {
                "list-gpus" => ListGpus(options),
                "resolve" => Resolve(options),
                "run" => await Run(options),
                "set-default" => SetDefault(options),
                "reset" => Reset(options),
                "export-backup" => ExportBackup(options),
                "import-backup" => ImportBackup(options),
                "register-context-menu" => RegisterContextMenu(options),
                "unregister-context-menu" => UnregisterContextMenu(),
                "diagnostics" => Diagnostics(options),
                "help" or "--help" or "-h" => ShowHelp(),
                "version" or "--version" or "-v" => ShowVersion(),
                _ => UnknownCommand(command)
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    static Dictionary<string, string?> ParseOptions(string[] args)
    {
        var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith("--"))
            {
                var key = arg.Substring(2);
                string? value = null;
                
                // Check if next arg is a value (not another option)
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                {
                    value = args[i + 1];
                    i++;
                }
                
                options[key] = value;
            }
        }
        
        return options;
    }

    static int ShowHelp()
    {
        Console.WriteLine("QGST - Quick GPU Selector Tool CLI");
        Console.WriteLine();
        Console.WriteLine("Usage: qgst <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  list-gpus           List available GPUs in the system");
        Console.WriteLine("    --json            Output in JSON format");
        Console.WriteLine("    --refresh         Force refresh GPU cache");
        Console.WriteLine();
        Console.WriteLine("  resolve             Resolve a target file to its executable");
        Console.WriteLine("    --target <path>   Target file path (required)");
        Console.WriteLine("    --json            Output in JSON format");
        Console.WriteLine();
        Console.WriteLine("  run                 Run an application with GPU preference");
        Console.WriteLine("    --target <path>   Target file path (required)");
        Console.WriteLine("    --gpu <id>        GPU ID or preference value (required)");
        Console.WriteLine("    --args <args>     Arguments to pass to the application");
        Console.WriteLine("    --one-time        Revert preference after application exits");
        Console.WriteLine();
        Console.WriteLine("  set-default         Set default GPU for an application");
        Console.WriteLine("    --target <path>   Target file path (required)");
        Console.WriteLine("    --gpu <id>        GPU ID or preference value (required)");
        Console.WriteLine();
        Console.WriteLine("  reset               Reset QGST settings and preferences");
        Console.WriteLine("    --target <path>   Reset preference for a specific application");
        Console.WriteLine("    --all             Reset everything");
        Console.WriteLine("    --prefs           Reset GPU preferences only");
        Console.WriteLine("    --contextmenu     Remove context menu only");
        Console.WriteLine("    --data            Clear data and cache only");
        Console.WriteLine("    --no-backup       Skip creating backup");
        Console.WriteLine();
        Console.WriteLine("  export-backup       Export settings backup");
        Console.WriteLine("    --out <path>      Output folder path");
        Console.WriteLine();
        Console.WriteLine("  import-backup       Import settings from backup");
        Console.WriteLine("    --in <path>       Backup folder path (required)");
        Console.WriteLine();
        Console.WriteLine("  register-context-menu    Register Explorer context menu");
        Console.WriteLine("    --ui-path <path>  Path to QGST.UI.exe");
        Console.WriteLine();
        Console.WriteLine("  unregister-context-menu  Remove Explorer context menu");
        Console.WriteLine();
        Console.WriteLine("  diagnostics         Export system diagnostics");
        Console.WriteLine("    --out <path>      Output file path");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  qgst list-gpus --json");
        Console.WriteLine("  qgst run --target \"C:\\Games\\Game.exe\" --gpu 2 --one-time");
        Console.WriteLine("  qgst set-default --target \"C:\\Apps\\App.exe\" --gpu gpu-1");
        Console.WriteLine("  qgst reset --all");
        return 0;
    }

    static int ShowVersion()
    {
        Console.WriteLine("QGST CLI v1.0.0");
        return 0;
    }

    static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        Console.Error.WriteLine("Use 'qgst help' for a list of commands.");
        return 1;
    }

    static int ListGpus(Dictionary<string, string?> options)
    {
        var json = options.ContainsKey("json");
        var refresh = options.ContainsKey("refresh");
        
        var gpus = refresh ? GpuInventory.RefreshGpus() : GpuInventory.GetGpus();
        
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(gpus, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            if (gpus.Count == 0)
            {
                Console.WriteLine("No GPUs found.");
                return 0;
            }

            Console.WriteLine($"Found {gpus.Count} GPU(s):\n");
            
            foreach (var gpu in gpus)
            {
                var typeLabel = gpu.IsIntegrated ? "[iGPU]" : "[dGPU]";
                Console.WriteLine($"  [{gpu.AdapterIndex}] {gpu.DisplayName} {typeLabel}");
                Console.WriteLine($"      Vendor: {gpu.Vendor}");
                Console.WriteLine($"      ID: {gpu.Id}");
                Console.WriteLine($"      Preference: {gpu.PreferenceValue} ({(gpu.IsIntegrated ? "Power Saving" : "High Performance")})");
                Console.WriteLine();
            }
        }
        
        return 0;
    }

    static int Resolve(Dictionary<string, string?> options)
    {
        if (!options.TryGetValue("target", out var target) || string.IsNullOrEmpty(target))
        {
            Console.Error.WriteLine("Error: --target is required");
            return 1;
        }
        
        var json = options.ContainsKey("json");
        var result = Resolver.ResolveDetailed(target);
        
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            if (result.Success)
            {
                Console.WriteLine($"Resolved: {result.ResolvedPath}");
                if (!string.IsNullOrEmpty(result.Arguments))
                    Console.WriteLine($"Arguments: {result.Arguments}");
                if (!string.IsNullOrEmpty(result.WorkingDirectory))
                    Console.WriteLine($"Working Dir: {result.WorkingDirectory}");
            }
            else if (result.Candidates.Count > 0)
            {
                Console.WriteLine($"Multiple candidates found in {target}:");
                foreach (var candidate in result.Candidates)
                {
                    Console.WriteLine($"  - {candidate}");
                }
            }
            else
            {
                Console.Error.WriteLine($"Error: {result.ErrorMessage}");
                return 1;
            }
        }
        
        return 0;
    }

    static async Task<int> Run(Dictionary<string, string?> options)
    {
        if (!options.TryGetValue("target", out var target) || string.IsNullOrEmpty(target))
        {
            Console.Error.WriteLine("Error: --target is required");
            return 1;
        }
        
        if (!options.TryGetValue("gpu", out var gpu) || string.IsNullOrEmpty(gpu))
        {
            Console.Error.WriteLine("Error: --gpu is required");
            return 1;
        }
        
        options.TryGetValue("args", out var appArgs);
        var oneTime = options.ContainsKey("one-time");
        
        var result = Resolver.ResolveDetailed(target);
        if (!result.Success && !result.IsSteamGame)
        {
            Console.Error.WriteLine($"Error resolving target: {result.ErrorMessage}");
            return 1;
        }

        // Handle Steam games that need Steam to launch
        if (result.IsSteamGame && string.IsNullOrEmpty(result.ResolvedPath))
        {
            Console.Error.WriteLine("Steam game executable not found. Please launch through Steam.");
            Console.Error.WriteLine($"Steam App ID: {result.SteamAppId}");
            return 1;
        }

        // Find GPU info
        var gpus = GpuInventory.GetGpus();
        GpuInfo? gpuInfo = null;
        
        if (int.TryParse(gpu, out var gpuIndex))
        {
            gpuInfo = gpus.FirstOrDefault(g => g.AdapterIndex == gpuIndex);
        }
        
        // Fallback to preference value
        int prefValue = ParseGpuPreference(gpu);
        
        Console.WriteLine($"Running: {result.ResolvedPath}");
        Console.WriteLine($"GPU: {gpuInfo?.DisplayName ?? "Unknown"} (Preference: {prefValue})");
        Console.WriteLine($"Mode: {(oneTime ? "One-Time" : "Persistent")}");

        if (oneTime)
        {
            Console.WriteLine("\nWaiting for application to exit...");
            var exitCode = await Launcher.RunOneTimeAsync(
                result.ResolvedPath,
                gpuInfo,
                appArgs ?? result.Arguments,
                result.WorkingDirectory);
            Console.WriteLine($"Application exited with code {exitCode}. Preference reverted.");
            return exitCode;
        }
        else
        {
            var process = Launcher.RunWithDefaultGpu(
                result.ResolvedPath,
                gpuInfo,
                appArgs ?? result.Arguments,
                result.WorkingDirectory);
            
            Console.WriteLine($"Application started (PID: {process?.Id ?? -1}). Preference set permanently.");
            return 0;
        }
    }

    static int SetDefault(Dictionary<string, string?> options)
    {
        if (!options.TryGetValue("target", out var target) || string.IsNullOrEmpty(target))
        {
            Console.Error.WriteLine("Error: --target is required");
            return 1;
        }
        
        if (!options.TryGetValue("gpu", out var gpu) || string.IsNullOrEmpty(gpu))
        {
            Console.Error.WriteLine("Error: --gpu is required");
            return 1;
        }
        
        var result = Resolver.ResolveDetailed(target);
        if (!result.Success)
        {
            Console.Error.WriteLine($"Error resolving target: {result.ErrorMessage}");
            return 1;
        }

        // Find GPU info - this provides LUID for specific GPU selection
        var gpus = GpuInventory.GetGpus();
        GpuInfo? gpuInfo = null;
        
        if (int.TryParse(gpu, out var gpuIndex))
        {
            gpuInfo = gpus.FirstOrDefault(g => g.AdapterIndex == gpuIndex);
        }
        else
        {
            gpuInfo = GpuInventory.FindById(gpu);
        }

        if (gpuInfo == null)
        {
            Console.Error.WriteLine($"Error: GPU not found: {gpu}");
            return 1;
        }

        // Use GpuInfo overload to get LUID-based preference (for multi-dGPU systems)
        Launcher.SetDefaultGpu(result.ResolvedPath, gpuInfo);
        
        Console.WriteLine($"Set GPU preference for: {result.ResolvedPath}");
        Console.WriteLine($"GPU: {gpuInfo.DisplayName}");
        Console.WriteLine($"Preference: {gpuInfo.PreferenceValue} ({PreferenceStoreAdapter.GetPreferenceDescription(gpuInfo.PreferenceValue)})");
        if (!string.IsNullOrEmpty(gpuInfo.PnpInstanceId))
        {
            Console.WriteLine($"Device ID: {gpuInfo.PnpInstanceId} (specific GPU targeting)");
        }
        
        return 0;
    }

    static int Reset(Dictionary<string, string?> options)
    {
        // Check if resetting preference for a specific target
        if (options.TryGetValue("target", out var target) && !string.IsNullOrEmpty(target))
        {
            var result = Resolver.ResolveDetailed(target);
            if (!result.Success)
            {
                Console.Error.WriteLine($"Error resolving target: {result.ErrorMessage}");
                return 1;
            }

            // Reset preference for this specific target
            PrefAdapter.RemovePreference(result.ResolvedPath);
            Console.WriteLine($"GPU preference reset for: {result.ResolvedPath}");
            return 0;
        }

        ResetOptions resetOpts = ResetOptions.None;
        
        if (options.ContainsKey("all"))
        {
            resetOpts = ResetOptions.All;
        }
        else
        {
            if (options.ContainsKey("prefs")) resetOpts |= ResetOptions.Preferences | ResetOptions.State;
            if (options.ContainsKey("contextmenu")) resetOpts |= ResetOptions.ContextMenu;
            if (options.ContainsKey("data")) resetOpts |= ResetOptions.Config | ResetOptions.Cache | ResetOptions.Logs;
        }

        if (resetOpts == ResetOptions.None)
        {
            Console.WriteLine("No reset options specified. Use --all, --prefs, --contextmenu, --data, or --target <path>.");
            return 0;
        }

        var noBackup = options.ContainsKey("no-backup");
        var resetResult = ResetSvc.Reset(resetOpts, createBackup: !noBackup);
        
        Console.WriteLine("Reset completed:");
        if (resetResult.PreferencesReset > 0)
            Console.WriteLine($"  - {resetResult.PreferencesReset} GPU preferences reset");
        if (resetResult.ContextMenuRemoved)
            Console.WriteLine("  - Context menu removed");
        if (resetResult.DataCleared)
            Console.WriteLine("  - Configuration cleared");
        if (resetResult.LogsCleared)
            Console.WriteLine("  - Logs cleared");
        if (!string.IsNullOrEmpty(resetResult.BackupPath))
            Console.WriteLine($"  - Backup created: {resetResult.BackupPath}");
        
        return 0;
    }

    static int ExportBackup(Dictionary<string, string?> options)
    {
        options.TryGetValue("out", out var outPath);
        var backupPath = Config.CreateBackup(outPath);
        Console.WriteLine($"Backup created: {backupPath}");
        return 0;
    }

    static int ImportBackup(Dictionary<string, string?> options)
    {
        if (!options.TryGetValue("in", out var inPath) || string.IsNullOrEmpty(inPath))
        {
            Console.Error.WriteLine("Error: --in is required");
            return 1;
        }
        
                Config.RestoreBackup(inPath);
        Console.WriteLine("Backup restored successfully.");
        return 0;
    }

    static int RegisterContextMenu(Dictionary<string, string?> options)
    {
        // Get CLI executable path (qgst.exe)
        var cliExePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
        if (cliExePath.EndsWith(".dll"))
            cliExePath = cliExePath.Replace(".dll", ".exe");

        if (string.IsNullOrEmpty(cliExePath) || !File.Exists(cliExePath))
        {
            Console.Error.WriteLine("Could not find CLI executable.");
            return 1;
        }

        var cms = new ContextMenuService(cliExePath, Loc, GpuInventory);
        cms.Register();
        Console.WriteLine($"Context menu registered using CLI: {cliExePath}");
        return 0;
    }

    static int UnregisterContextMenu()
    {
        var cms = new ContextMenuService("");
        cms.Unregister();
        Console.WriteLine("Context menu items removed.");
        return 0;
    }

    static int Diagnostics(Dictionary<string, string?> options)
    {
        options.TryGetValue("out", out var outPath);
        var diagPath = ResetSvc.ExportDiagnostics(outPath);
        Console.WriteLine($"Diagnostics exported: {diagPath}");
        return 0;
    }

    static int ParseGpuPreference(string gpu)
    {
        // Try parse as number - treat as AdapterIndex
        if (int.TryParse(gpu, out int adapterIndex))
        {
            // Find GPU by adapter index and return its PreferenceValue
            var foundByIndex = GpuInventory.GetGpus().FirstOrDefault(g => g.AdapterIndex == adapterIndex);
            if (foundByIndex != null)
            {
                return foundByIndex.PreferenceValue;
            }
            // Fallback: if not found, return the index clamped (legacy behavior)
            return Math.Clamp(adapterIndex, 0, 2);
        }

        // Try find GPU by ID (e.g., "gpu-0", "gpu-1")
        var foundGpu = GpuInventory.FindById(gpu);
        if (foundGpu != null)
        {
            return foundGpu.PreferenceValue;
        }

        // Named values
        return gpu.ToLowerInvariant() switch
        {
            "high" or "highperf" or "highperformance" or "discrete" => 2,
            "power" or "powersaving" or "integrated" => 1,
            "default" or "auto" => 0,
            _ => 2 // Default to high performance
        };
    }
}
