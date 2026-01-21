using System.Text.Json;
using System.Globalization;

namespace QGST.Core.Services;

/// <summary>
/// Service for loading and providing localized strings.
/// Supports TR/EN with automatic detection and manual override.
/// </summary>
public class LocalizationService
{
    private Dictionary<string, string> _strings = new();
    private string _currentLanguage = "en";
    private readonly List<string> _searchPaths = new();

    /// <summary>
    /// Event fired when language changes
    /// </summary>
    public event Action? LanguageChanged;

    /// <summary>
    /// Currently loaded language code
    /// </summary>
    public string CurrentLanguage => _currentLanguage;

    /// <summary>
    /// Available language codes
    /// </summary>
    public static readonly string[] AvailableLanguages = { "en", "tr" };

    public LocalizationService(string? basePath = null)
    {
        // Add search paths in priority order
        
        // 1. App data locales (user can override)
        if (!string.IsNullOrEmpty(basePath))
        {
            _searchPaths.Add(Path.Combine(basePath, "locales"));
        }

        // 2. Application directory locales
        var appDir = AppContext.BaseDirectory;
        _searchPaths.Add(Path.Combine(appDir, "locales"));
        _searchPaths.Add(Path.Combine(appDir, "Data", "locales"));

        // 3. Development paths (for debugging)
        var devPath = Path.Combine(appDir, "..", "..", "..", "Data", "locales");
        if (Directory.Exists(Path.GetFullPath(devPath)))
        {
            _searchPaths.Add(Path.GetFullPath(devPath));
        }
    }

    /// <summary>
    /// Load language strings. Pass "auto" to detect from system.
    /// </summary>
    public void LoadLanguage(string languageCode)
    {
        if (languageCode == "auto")
        {
            languageCode = DetectSystemLanguage();
        }

        // Normalize language code
        languageCode = NormalizeLanguageCode(languageCode);

        // Try to load the language file
        var loaded = TryLoadLanguageFile(languageCode);
        
        // Fall back to English if requested language not found
        if (!loaded && languageCode != "en")
        {
            loaded = TryLoadLanguageFile("en");
            languageCode = "en";
        }

        // If still not loaded, use embedded defaults
        if (!loaded)
        {
            LoadDefaultStrings();
            languageCode = "en";
        }

        _currentLanguage = languageCode;
        LanguageChanged?.Invoke();
    }

    /// <summary>
    /// Get a localized string by key. Returns [key] if not found.
    /// </summary>
    public string GetString(string key)
    {
        return _strings.TryGetValue(key, out var value) ? value : $"[{key}]";
    }

    /// <summary>
    /// Get a localized string with format arguments.
    /// </summary>
    public string GetString(string key, params object[] args)
    {
        var template = GetString(key);
        try
        {
            return string.Format(template, args);
        }
        catch
        {
            return template;
        }
    }

    /// <summary>
    /// Shorthand for GetString
    /// </summary>
    public string this[string key] => GetString(key);

    /// <summary>
    /// Check if a key exists in the current language
    /// </summary>
    public bool HasKey(string key) => _strings.ContainsKey(key);

    /// <summary>
    /// Get all loaded string keys
    /// </summary>
    public IEnumerable<string> GetAllKeys() => _strings.Keys;

    private string DetectSystemLanguage()
    {
        var culture = CultureInfo.CurrentUICulture;
        var code = culture.TwoLetterISOLanguageName.ToLowerInvariant();
        
        // Map to supported languages
        return code switch
        {
            "tr" => "tr",
            _ => "en" // Default to English
        };
    }

    private string NormalizeLanguageCode(string code)
    {
        if (string.IsNullOrEmpty(code)) return "en";
        
        // Handle full culture codes (en-US, tr-TR)
        code = code.ToLowerInvariant();
        if (code.Contains('-'))
        {
            code = code.Split('-')[0];
        }

        return AvailableLanguages.Contains(code) ? code : "en";
    }

    private bool TryLoadLanguageFile(string languageCode)
    {
        foreach (var searchPath in _searchPaths)
        {
            var filePath = Path.Combine(searchPath, $"{languageCode}.json");
            if (File.Exists(filePath))
            {
                try
                {
                    var json = File.ReadAllText(filePath);
                    var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (loaded != null && loaded.Count > 0)
                    {
                        _strings = loaded;
                        return true;
                    }
                }
                catch
                {
                    // Continue to next path
                }
            }
        }
        return false;
    }

    private void LoadDefaultStrings()
    {
        // Embedded English defaults as fallback
        _strings = new Dictionary<string, string>
        {
            ["app_title"] = "QGST - Quick GPU Selector",
            ["run_with_gpu"] = "Run with GPU...",
            ["set_default_gpu"] = "Set Default GPU...",
            ["one_time_run"] = "One-Time Run",
            ["set_default"] = "Set as Default",
            ["run_apply"] = "Run / Apply",
            ["cancel"] = "Cancel",
            ["settings"] = "Settings",
            ["reset_all"] = "Reset All QGST Settings",
            ["reset_preferences"] = "Reset GPU Preferences",
            ["reset_context_menu"] = "Reset Context Menu",
            ["reset_data"] = "Reset Data & Cache",
            ["confirm_reset"] = "Are you sure you want to reset?",
            ["gpu_list_header"] = "Select GPU",
            ["target_header"] = "Target Application",
            ["no_target"] = "No file selected",
            ["drag_drop_hint"] = "Drag & drop or use right-click menu",
            ["error"] = "Error",
            ["success"] = "Success",
            ["gpu_set_success"] = "Default GPU set successfully",
            ["language"] = "Language",
            ["theme"] = "Theme",
            ["about"] = "About QGST",
            ["version"] = "Version",
            ["gpu_info_vram"] = "{0} GB VRAM",
            ["gpu_info_location"] = "Location: {0}",
            ["integrated_gpu"] = "Integrated",
            ["discrete_gpu"] = "Discrete",
            ["power_saving"] = "Power Saving",
            ["high_performance"] = "High Performance",
            ["unsupported_os"] = "GPU preferences not supported on this Windows version",
            ["process_running"] = "Application is running...",
            ["process_exited"] = "Application exited",
            ["backup_created"] = "Backup created: {0}",
            ["backup_restored"] = "Backup restored successfully",
            ["recent_targets"] = "Recent",
            ["favorites"] = "Favorites",
            ["refresh_gpus"] = "Refresh GPU List",
            ["create_shortcut"] = "Create Shortcut",
            ["create_wrapper"] = "Create BAT Wrapper",
            ["context_menu_registered"] = "Context menu registered",
            ["context_menu_unregistered"] = "Context menu removed"
        };
    }

    /// <summary>
    /// Export all strings to a file (for creating new translations)
    /// </summary>
    public void ExportStrings(string filePath)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(_strings, options);
        File.WriteAllText(filePath, json);
    }
}
