using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using QGST.Core.Services;

// Use alias to resolve ambiguity with System.Windows.Controls.ContextMenuService
using QGSTContextMenuService = QGST.Core.Services.ContextMenuService;

namespace QGST.UI;

public partial class SettingsWindow : Window
{
    private readonly ConfigManager _config;
    private readonly LocalizationService _loc;
    private readonly ResetService _resetService;
    private readonly QGSTContextMenuService _contextMenuService;
    
    /// <summary>
    /// Event fired when settings are reset (so MainWindow can refresh)
    /// </summary>
    public event Action? SettingsReset;

    public SettingsWindow(ConfigManager config, LocalizationService loc, ResetService resetService)
    {
        InitializeComponent();
        
        _config = config;
        _loc = loc;
        _resetService = resetService;

        // Get CLI path for context menu registration
        var uiExePath = Assembly.GetExecutingAssembly().Location;
        if (uiExePath.EndsWith(".dll")) uiExePath = uiExePath.Replace(".dll", ".exe");
        var cliExePath = Path.Combine(Path.GetDirectoryName(uiExePath) ?? "", "qgst.exe");
        _contextMenuService = new QGSTContextMenuService(cliExePath, loc);

        InitializeSettings();
    }

    private void InitializeSettings()
    {
        // Set current language
        var langCode = _config.Settings.Language;
        foreach (ComboBoxItem item in CmbLanguage.Items)
        {
            if (item.Tag?.ToString() == langCode)
            {
                CmbLanguage.SelectedItem = item;
                break;
            }
        }

        // Set version
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        TxtVersion.Text = $"Version {version?.Major ?? 1}.{version?.Minor ?? 0}.{version?.Build ?? 0}";

        // Update context menu button states
        UpdateContextMenuButtons();
        
        // Apply localization
        ApplyLocalization();
    }

    private void ApplyLocalization()
    {
        Title = _loc["settings"];
        // Additional localization can be applied here
    }

    private void UpdateContextMenuButtons()
    {
        var isRegistered = _contextMenuService.IsRegistered();
        BtnRegisterMenu.IsEnabled = !isRegistered;
        BtnUnregisterMenu.IsEnabled = isRegistered;
    }

    private void CmbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbLanguage.SelectedItem is ComboBoxItem item && item.Tag is string langCode)
        {
            _config.UpdateLanguage(langCode);
            _loc.LoadLanguage(langCode);
            ApplyLocalization();
        }
    }

    private void BtnRegisterMenu_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _contextMenuService.Register();
            UpdateContextMenuButtons();
            MessageBox.Show(_loc["context_menu_registered"], _loc["success"], 
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, _loc["error"], MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnUnregisterMenu_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _contextMenuService.Unregister();
            UpdateContextMenuButtons();
            MessageBox.Show(_loc["context_menu_unregistered"], _loc["success"], 
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, _loc["error"], MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnBackup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var backupPath = _config.CreateBackup();
            MessageBox.Show(
                string.Format(_loc["backup_created"], backupPath), 
                _loc["success"], 
                MessageBoxButton.OK, 
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, _loc["error"], MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var diagPath = _resetService.ExportDiagnostics();
            MessageBox.Show(
                string.Format(_loc["diagnostics_exported"], diagPath), 
                _loc["success"], 
                MessageBoxButton.OK, 
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, _loc["error"], MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start("explorer.exe", _config.GetBasePath());
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, _loc["error"], MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnResetPrefs_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            _loc["confirm_reset"],
            _loc["reset_preferences"],
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            try
            {
                var resetResult = _resetService.Reset(ResetOptions.Preferences, createBackup: true);
                MessageBox.Show(
                    string.Format(_loc["reset_complete"], resetResult.PreferencesReset),
                    _loc["success"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                
                SettingsReset?.Invoke();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, _loc["error"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void BtnResetAll_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            _loc["confirm_reset_all"],
            _loc["reset_all"],
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            try
            {
                var resetResult = _resetService.ResetAll(createBackup: true);
                
                // Build detailed message
                var messages = new System.Collections.Generic.List<string>();
                messages.Add(string.Format(_loc["reset_complete"], resetResult.PreferencesReset));
                if (resetResult.ContextMenuRemoved)
                    messages.Add(_loc["context_menu_unregistered"]);
                if (resetResult.DataCleared)
                    messages.Add(_loc["reset_data"]);
                if (resetResult.LogsCleared)
                    messages.Add(_loc["reset_logs"]);
                
                MessageBox.Show(
                    string.Join("\n", messages),
                    _loc["success"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                
                SettingsReset?.Invoke();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, _loc["error"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
            e.Handled = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, _loc["error"], MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
