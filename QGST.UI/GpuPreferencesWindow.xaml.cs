using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using QGST.Core.Services;

namespace QGST.UI;

public partial class GpuPreferencesWindow : Window
{
    private readonly PreferenceStoreAdapter _prefAdapter;
    private readonly LocalizationService _loc;
    private readonly ConfigManager _config;
    private readonly GpuInventoryService _inventory;

    public GpuPreferencesWindow(PreferenceStoreAdapter prefAdapter, LocalizationService loc, 
                                 ConfigManager config, GpuInventoryService inventory)
    {
        InitializeComponent();
        _prefAdapter = prefAdapter;
        _loc = loc;
        _config = config;
        _inventory = inventory;
        
        LoadPreferences();
    }

    private void LoadPreferences()
    {
        var allPrefs = _prefAdapter.GetAllPreferences();
        var gpus = _inventory.GetGpus();
        
        var items = new List<PreferenceItem>();
        
        foreach (var pref in allPrefs)
        {
            var item = new PreferenceItem
            {
                AppPath = pref.Key,
                AppName = Path.GetFileName(pref.Key),
                RawPreference = pref.Value,
                GpuPreferenceDisplay = ParsePreferenceDisplay(pref.Value, gpus),
                Source = DetermineSource(pref.Value),
                SourceColor = GetSourceColor(DetermineSource(pref.Value))
            };
            items.Add(item);
        }
        
        // Sort: QGST first, then Windows, then others
        items = items.OrderBy(x => x.Source == "QGST" ? 0 : x.Source == "Windows" ? 1 : 2)
                     .ThenBy(x => x.AppName)
                     .ToList();
        
        ListPreferences.ItemsSource = items;
        
        // Update count
        TxtCount.Text = $"{items.Count} {(items.Count == 1 ? "preference" : "preferences")}";
        
        // Show empty state if needed
        EmptyState.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ListPreferences.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private string ParsePreferenceDisplay(string prefString, List<Core.Models.GpuInfo> gpus)
    {
        // Check for specific adapter format
        if (prefString.Contains("SpecificAdapter="))
        {
            // Try to find matching GPU
            foreach (var gpu in gpus)
            {
                if (!string.IsNullOrEmpty(gpu.PnpInstanceId))
                {
                    var vendorId = ExtractId(gpu.PnpInstanceId, "VEN_");
                    var deviceId = ExtractId(gpu.PnpInstanceId, "DEV_");
                    
                    if (!string.IsNullOrEmpty(vendorId) && prefString.Contains(vendorId))
                    {
                        return gpu.ShortDisplayName;
                    }
                }
            }
            return "Specific GPU";
        }
        
        // Parse GpuPreference value
        var prefValue = PreferenceStoreAdapter.ParsePreferenceValue(prefString);
        
        return prefValue switch
        {
            0 => "Windows Default",
            1 => "Power Saving",
            2 => "High Performance",
            _ => prefString.Length > 30 ? prefString.Substring(0, 30) + "..." : prefString
        };
    }

    private string ExtractId(string pnpId, string prefix)
    {
        var idx = pnpId.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            var start = idx + prefix.Length;
            var end = pnpId.IndexOf('&', start);
            if (end < 0) end = pnpId.Length;
            return pnpId.Substring(start, Math.Min(4, end - start));
        }
        return string.Empty;
    }

    private string DetermineSource(string prefString)
    {
        // QGST uses SpecificAdapter format with GpuPreference=1073741824
        if (prefString.Contains("SpecificAdapter=") && prefString.Contains("1073741824"))
        {
            return "QGST";
        }
        
        // Windows Graphics Settings uses simple GpuPreference=X format
        if (prefString == "GpuPreference=1;" || prefString == "GpuPreference=2;")
        {
            return "Windows";
        }
        
        return "Other";
    }

    private Brush GetSourceColor(string source)
    {
        return source switch
        {
            "QGST" => new SolidColorBrush(Color.FromRgb(59, 130, 246)),   // Blue
            "Windows" => new SolidColorBrush(Color.FromRgb(245, 158, 11)), // Yellow
            _ => new SolidColorBrush(Color.FromRgb(107, 114, 128))        // Gray
        };
    }

    private void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string appPath)
        {
            var result = MessageBox.Show(
                $"Remove GPU preference for:\n{Path.GetFileName(appPath)}?",
                "Confirm Removal",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    _prefAdapter.RemovePreference(appPath);
                    _config.Log($"Removed preference for: {appPath}");
                    LoadPreferences(); // Refresh list
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to remove preference: {ex.Message}", 
                                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

public class PreferenceItem
{
    public string AppPath { get; set; } = string.Empty;
    public string AppName { get; set; } = string.Empty;
    public string RawPreference { get; set; } = string.Empty;
    public string GpuPreferenceDisplay { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public Brush SourceColor { get; set; } = Brushes.Gray;
}
