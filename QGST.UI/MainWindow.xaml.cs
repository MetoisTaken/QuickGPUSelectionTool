using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using QGST.Core.Models;
using QGST.Core.Services;

namespace QGST.UI;

public partial class MainWindow : Window
{
    private readonly ConfigManager _config;
    private readonly GpuInventoryService _inventory;
    private readonly PreferenceStoreAdapter _prefAdapter;
    private readonly ProcessLauncher _launcher;
    private readonly LocalizationService _loc;
    private readonly TargetResolver _resolver;
    private readonly ResetService _resetService;
    private readonly Core.Services.ContextMenuService _contextMenu;
    private readonly DispatcherTimer _toastTimer;

    private string _originalTargetPath = string.Empty;
    private string _resolvedTarget = string.Empty;
    private string _targetArguments = string.Empty;
    private string _targetWorkingDir = string.Empty;
    private GpuInfo? _selectedGpu = null;
    private bool _isUpdatingContextMenu = false;

    public MainWindow()
    {
        InitializeComponent();

        // Initialize services
        _config = new ConfigManager();
        _loc = new LocalizationService(_config.GetBasePath());
        _loc.LoadLanguage(_config.Settings.Language);
        _loc.LanguageChanged += ApplyLocalization;

        _inventory = new GpuInventoryService(_config.GetBasePath());
        _prefAdapter = new PreferenceStoreAdapter();
        _launcher = new ProcessLauncher(_prefAdapter, _config);
        _resolver = new TargetResolver(_config);
        
        var cliPath = Path.Combine(_config.GetBasePath(), "qgst.exe");
        _contextMenu = new Core.Services.ContextMenuService(cliPath, _loc, _inventory);
        
        var uiExePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
        if (uiExePath.EndsWith(".dll")) uiExePath = uiExePath.Replace(".dll", ".exe");
        _resetService = new ResetService(_config, _prefAdapter, _launcher, uiExePath);
        
        // Toast timer
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _toastTimer.Tick += (s, e) => { _toastTimer.Stop(); ToastNotification.Visibility = Visibility.Collapsed; };

        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Cleanup any pending reverts from previous crashes
        var cleanedUp = _launcher.CleanupPendingReverts();
        if (cleanedUp > 0)
        {
            _config.Log($"Cleaned up {cleanedUp} pending reverts on startup");
        }

        // Load GPUs
        LoadGpus();
        
        // Process command line arguments
        ProcessArgs();
        
        // Apply localization
        ApplyLocalization();

        // Set initial mode based on settings
        if (_config.Settings.DefaultMode == "set-default")
        {
            ModeDefault.IsChecked = true;
        }
        
        // Check context menu status
        _isUpdatingContextMenu = true;
        ChkContextMenu.IsChecked = _contextMenu.IsRegistered();
        _isUpdatingContextMenu = false;

        // Focus GPU list for keyboard navigation
        ListGpus.Focus();
    }

    private void ProcessArgs()
    {
        string target = App.TargetFile;
        string mode = App.Mode;

        if (!string.IsNullOrEmpty(mode))
        {
            if (mode == "one-time")
                ModeOneTime.IsChecked = true;
            else if (mode == "set-default")
                ModeDefault.IsChecked = true;
        }

        if (!string.IsNullOrEmpty(target))
        {
            SetTarget(target);
        }

        // Select GPU if specified
        if (!string.IsNullOrEmpty(App.GpuId))
        {
            SelectGpuById(App.GpuId);
        }
        else if (!string.IsNullOrEmpty(_config.Settings.LastSelectedGpuId))
        {
            SelectGpuById(_config.Settings.LastSelectedGpuId);
        }
    }

    private void SetTarget(string path)
    {
        // Clear folder mode when single file is selected
        _folderExeFiles.Clear();
        _selectedFolder = string.Empty;
        ModeOneTime.IsEnabled = true;
        
        _originalTargetPath = path;
        
        var result = _resolver.ResolveDetailed(path);
        
        if (result.Success)
        {
            _resolvedTarget = result.ResolvedPath;
            _targetArguments = result.Arguments;
            _targetWorkingDir = result.WorkingDirectory;
            
            var fileName = Path.GetFileName(_resolvedTarget);
            TxtTarget.Text = fileName;
            TxtTarget.ToolTip = _resolvedTarget;
            BtnClearTarget.Visibility = Visibility.Visible;
        }
        else if (result.Candidates.Count > 1)
        {
            // Multiple candidates - show selection dialog
            ShowExeSelectionDialog(path, result.Candidates);
        }
        else
        {
            TxtTarget.Text = Path.GetFileName(path);
            TxtTarget.ToolTip = result.ErrorMessage;
            _resolvedTarget = path; // Use original as fallback
            BtnClearTarget.Visibility = Visibility.Visible;
        }

        UpdateRunButtonState();
    }

    private void ShowExeSelectionDialog(string wrapperPath, System.Collections.Generic.List<string> candidates)
    {
        // Simple selection - use first candidate for now
        // TODO: Create a proper selection dialog
        if (candidates.Any())
        {
            _resolvedTarget = candidates[0];
            _resolver.SaveMapping(wrapperPath, candidates[0]);
            
            var fileName = Path.GetFileName(_resolvedTarget);
            TxtTarget.Text = fileName;
            TxtTarget.ToolTip = _resolvedTarget;
        }
    }

    private void LoadGpus()
    {
        var gpus = _inventory.GetGpus();
        ListGpus.ItemsSource = gpus;
        
        if (gpus.Any() && _selectedGpu == null)
        {
            _selectedGpu = gpus[0];
        }

        UpdateRunButtonState();
        
        // Delay card selection visual update until items are rendered
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, UpdateCardSelection);
    }

    private void SelectGpuById(string id)
    {
        var gpu = _inventory.FindById(id);
        if (gpu != null)
        {
            SelectGpu(gpu);
        }
    }

    private void SelectGpu(GpuInfo gpu)
    {
        _selectedGpu = gpu;
        UpdateCardSelection();
        UpdateRunButtonState();
        UpdateRunButtonText();
    }

    private void UpdateCardSelection()
    {
        // Find all GPU cards and update their borders
        foreach (var item in ListGpus.Items)
        {
            var container = ListGpus.ItemContainerGenerator.ContainerFromItem(item) as ContentPresenter;
            if (container == null) continue;
            
            var border = FindVisualChild<Border>(container, "GpuCard");
            if (border != null)
            {
                var gpu = item as GpuInfo;
                bool isSelected = gpu != null && _selectedGpu?.Id == gpu.Id;
                border.BorderBrush = isSelected 
                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(59, 130, 246)) 
                    : System.Windows.Media.Brushes.Transparent;
                border.Background = isSelected
                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 58, 138))
                    : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(31, 31, 35));
            }
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T element && element.Name == name)
                return element;
            
            var result = FindVisualChild<T>(child, name);
            if (result != null)
                return result;
        }
        return null;
    }

    private void GpuCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is GpuInfo gpu)
        {
            SelectGpu(gpu);
        }
    }

    private void ApplyLocalization()
    {
        Title = _loc["app_title_short"];
        LblTargetHeader.Text = _loc["target_header"].ToUpperInvariant();
        LblGpuHeader.Text = _loc["gpu_list_header"].ToUpperInvariant();
        LblContextMenu.Text = _loc["explorer_context_menu"];
        
        ModeOneTime.Content = "⚡ " + _loc["one_time_run"];
        ModeDefault.Content = "📌 " + _loc["set_default"];
        
        BtnBrowse.ToolTip = _loc["browse_file"];
        BtnClearTarget.ToolTip = _loc["clear_target"];
        BtnReset.Content = _loc["reset_all"];
        
        // Update folder button tooltip based on mode
        UpdateFolderButtonTooltip();
        
        // Update target text if empty
        if (string.IsNullOrEmpty(_resolvedTarget) && _folderExeFiles.Count == 0)
        {
            TxtTarget.Text = _loc["no_target_hint"];
        }
        
        UpdateRunButtonText();
    }

    private void UpdateRunButtonText()
    {
        if (BtnRun == null || ModeOneTime == null) return;
        
        if (ModeOneTime.IsChecked == true)
        {
            BtnRun.Content = _loc["run"];
        }
        else
        {
            BtnRun.Content = _loc["apply"];
        }
    }

    private void Mode_Changed(object sender, RoutedEventArgs e)
    {
        // Guard against calls during XAML initialization
        if (BtnBrowseFolder == null || ModeDefault == null) return;
        
        // Update folder button state based on mode
        // Folder selection only available in Set Default mode
        BtnBrowseFolder.IsEnabled = ModeDefault.IsChecked == true;
        
        // Update folder button tooltip
        UpdateFolderButtonTooltip();
        
        UpdateRunButtonText();
    }

    private void UpdateFolderButtonTooltip()
    {
        if (BtnBrowseFolder == null) return;
        
        if (ModeOneTime.IsChecked == true)
        {
            BtnBrowseFolder.ToolTip = _loc["folder_disabled_onetime"];
        }
        else
        {
            BtnBrowseFolder.ToolTip = _loc["browse_folder"];
        }
    }

    private void UpdateRunButtonState()
    {
        bool hasTarget = !string.IsNullOrEmpty(_resolvedTarget) || _folderExeFiles.Count > 0;
        BtnRun.IsEnabled = hasTarget && _selectedGpu != null;
    }

    private async void BtnRun_Click(object sender, RoutedEventArgs e)
    {
        // Handle folder mode
        if (_folderExeFiles.Count > 0)
        {
            await ApplyGpuToFolder();
            return;
        }

        if (string.IsNullOrEmpty(_resolvedTarget))
        {
            MessageBox.Show(_loc["no_target"], _loc["error"], MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var selectedGpu = _selectedGpu;
        if (selectedGpu == null)
        {
            MessageBox.Show(_loc["gpu_list_header"], _loc["error"], MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Check if preference store is supported
        if (!_prefAdapter.IsSupported())
        {
            MessageBox.Show(_loc["unsupported_os"], _loc["warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Save selections
        _config.Settings.LastSelectedGpuId = selectedGpu.Id;
        _config.Settings.DefaultMode = ModeOneTime.IsChecked == true ? "one-time" : "set-default";
        _config.SaveSettings();

        // Add to recent targets
        _config.AddRecentTarget(_originalTargetPath, _resolvedTarget, selectedGpu.Id, 
            ModeOneTime.IsChecked == true ? "one-time" : "set-default");

        bool isOneTime = ModeOneTime.IsChecked == true;

        if (isOneTime)
        {
            // One-time mode: show loading, wait for process, then close
            ShowLoading(true);
            
            try
            {
                _config.Log($"One-time run: {_resolvedTarget} with GPU {selectedGpu.DisplayName}");
                
                // Use GpuInfo object directly for accurate GPU selection (not just PreferenceValue)
                var exitCode = await _launcher.RunOneTimeAsync(
                    _resolvedTarget, 
                    selectedGpu, 
                    _targetArguments, 
                    _targetWorkingDir);
                
                _config.Log($"Process exited with code {exitCode}");
                Close();
            }
            catch (Exception ex)
            {
                ShowLoading(false);
                _config.LogError($"Error running: {ex.Message}");
                MessageBox.Show(
                    string.Format(_loc["process_start_failed"], ex.Message), 
                    _loc["error"], 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Error);
            }
        }
        else
        {
            // Set Default mode
            try
            {
                // Use GpuInfo object directly for accurate GPU selection (not just PreferenceValue)
                _launcher.SetDefaultGpu(_resolvedTarget, selectedGpu);
                
                ShowToast("✅", string.Format(_loc["gpu_set_success_detail"], selectedGpu.ShortDisplayName), true);
            }
            catch (Exception ex)
            {
                _config.LogError($"Error setting default: {ex.Message}");
                ShowToast("❌", ex.Message, false);
            }
        }
    }

    private void ShowLoading(bool show)
    {
        LoadingOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show)
        {
            TxtLoading.Text = _loc["process_running"];
            TxtLoadingDetail.Text = "";
        }
        IsEnabled = !show;
    }

    private async Task ApplyGpuToFolder()
    {
        var selectedGpu = _selectedGpu;
        if (selectedGpu == null) return;

        var result = MessageBox.Show(
            string.Format(_loc["confirm_folder_apply"], _folderExeFiles.Count, selectedGpu.ShortDisplayName),
            _loc["confirm_folder_title"],
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        int success = 0;
        int failed = 0;

        foreach (var exePath in _folderExeFiles)
        {
            try
            {
                // Use GpuInfo object directly for accurate GPU selection (not just PreferenceValue)
                _launcher.SetDefaultGpu(exePath, selectedGpu);
                success++;
            }
            catch
            {
                failed++;
            }
        }

        // Show result
        var message = string.Format(_loc["folder_apply_result"], success, failed);
        ShowToast(failed == 0 ? "✅" : "⚠️", message, failed == 0);

        // Reset folder mode
        ClearFolderMode();
    }

    private void ClearFolderMode()
    {
        _folderExeFiles.Clear();
        _selectedFolder = string.Empty;
        ModeOneTime.IsEnabled = true;
        TxtTarget.Text = _loc["no_target_hint"];
        TxtTarget.ToolTip = null;
        BtnClearTarget.Visibility = Visibility.Collapsed;
        UpdateRunButtonState();
    }

    private void BtnReset_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            _loc["confirm_reset"],
            _loc["reset_all"],
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            try
            {
                // Reset preferences AND context menu
                var resetResult = _resetService.Reset(
                    ResetOptions.Preferences | ResetOptions.ContextMenu, 
                    createBackup: true);
                
                // Update context menu checkbox state
                _isUpdatingContextMenu = true;
                ChkContextMenu.IsChecked = _contextMenu.IsRegistered();
                _isUpdatingContextMenu = false;
                
                // Build result message
                var messages = new System.Collections.Generic.List<string>();
                if (resetResult.PreferencesReset > 0)
                    messages.Add(string.Format(_loc["reset_complete"], resetResult.PreferencesReset));
                if (resetResult.ContextMenuRemoved)
                    messages.Add(_loc["context_menu_unregistered"]);
                
                var finalMessage = messages.Count > 0 
                    ? string.Join("\n", messages) 
                    : _loc["success"];
                
                ShowToast("✅", finalMessage, true);
            }
            catch (Exception ex)
            {
                ShowToast("❌", _loc["error"] + ": " + ex.Message, false);
            }
        }
    }

    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Executables (*.exe)|*.exe|Shortcuts (*.lnk)|*.lnk|Batch Files (*.bat;*.cmd)|*.bat;*.cmd|All Files (*.*)|*.*",
            Title = _loc["browse"]
        };

        if (dialog.ShowDialog() == true)
        {
            SetTarget(dialog.FileName);
        }
    }

    private void BtnBrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = _loc["select_folder"],
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            SetFolderTarget(dialog.SelectedPath);
        }
    }

    private void BtnClearTarget_Click(object sender, RoutedEventArgs e)
    {
        // Clear single file target
        _resolvedTarget = string.Empty;
        _originalTargetPath = string.Empty;
        _targetArguments = string.Empty;
        _targetWorkingDir = string.Empty;
        
        // Clear folder mode too
        ClearFolderMode();
    }

    private List<string> _folderExeFiles = new();
    private string _selectedFolder = string.Empty;

    private void SetFolderTarget(string folderPath)
    {
        _selectedFolder = folderPath;
        
        // Find all exe files in folder recursively
        var exeFiles = Directory.GetFiles(folderPath, "*.exe", SearchOption.AllDirectories).ToList();
        
        if (exeFiles.Count == 0)
        {
            ShowToast("⚠️", _loc["no_exe_in_folder"], false);
            return;
        }

        _folderExeFiles = exeFiles;
        _resolvedTarget = string.Empty; // Clear single target
        
        var folderName = Path.GetFileName(folderPath);
        TxtTarget.Text = string.Format(_loc["folder_target_display"], folderName, exeFiles.Count);
        TxtTarget.ToolTip = string.Join("\n", exeFiles.Select(f => Path.GetRelativePath(folderPath, f)));
        BtnClearTarget.Visibility = Visibility.Visible;
        
        // Force Set Default mode for folders
        ModeDefault.IsChecked = true;
        ModeOneTime.IsEnabled = false;
        
        UpdateRunButtonState();
        UpdateRunButtonText();
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        var selectedId = _selectedGpu?.Id;
        
        _inventory.ClearCache();
        LoadGpus();
        
        if (!string.IsNullOrEmpty(selectedId))
        {
            SelectGpuById(selectedId);
        }
    }

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow(_config, _loc, _resetService);
        settingsWindow.Owner = this;
        settingsWindow.SettingsReset += RefreshContextMenuState;
        settingsWindow.ShowDialog();
        
        // Reload settings if changed
        ApplyLocalization();
    }

    private void RefreshContextMenuState()
    {
        _isUpdatingContextMenu = true;
        ChkContextMenu.IsChecked = _contextMenu.IsRegistered();
        _isUpdatingContextMenu = false;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files?.Length > 0)
            {
                SetTarget(files[0]);
            }
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter when BtnRun.IsEnabled:
                BtnRun_Click(sender, e);
                e.Handled = true;
                break;
            
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
            
            case Key.F5:
                BtnRefresh_Click(sender, e);
                e.Handled = true;
                break;
        }
    }

    private void ChkContextMenu_Changed(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingContextMenu) return;
        
        try
        {
            if (ChkContextMenu.IsChecked == true)
            {
                _contextMenu.Register();
                ShowToast("✅", _loc["context_menu_registered"], true);
            }
            else
            {
                _contextMenu.Unregister();
                ShowToast("✅", _loc["context_menu_unregistered"], true);
            }
        }
        catch (Exception ex)
        {
            _config.Log($"Context menu error: {ex.Message}");
            ShowToast("❌", _loc["error"] + ": " + ex.Message, false);
            
            // Revert checkbox state
            _isUpdatingContextMenu = true;
            ChkContextMenu.IsChecked = !ChkContextMenu.IsChecked;
            _isUpdatingContextMenu = false;
        }
    }

    private void ShowToast(string icon, string message, bool isSuccess)
    {
        ToastIcon.Text = icon;
        ToastMessage.Text = message;
        ToastBorder.Background = isSuccess 
            ? new SolidColorBrush(Color.FromRgb(20, 83, 45))   // Green background
            : new SolidColorBrush(Color.FromRgb(127, 29, 29)); // Red background
        
        // Animate in
        ToastNotification.Visibility = Visibility.Visible;
        var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
        var slideIn = new System.Windows.Media.Animation.DoubleAnimation(20, 0, TimeSpan.FromMilliseconds(250))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        };
        
        ToastNotification.BeginAnimation(OpacityProperty, fadeIn);
        ToastTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, slideIn);
        
        _toastTimer.Stop();
        _toastTimer.Tick -= ToastTimer_HideTick;
        _toastTimer.Tick += ToastTimer_HideTick;
        _toastTimer.Start();
    }

    private void ToastTimer_HideTick(object? sender, EventArgs e)
    {
        _toastTimer.Stop();
        
        // Animate out
        var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
        var slideOut = new System.Windows.Media.Animation.DoubleAnimation(0, -10, TimeSpan.FromMilliseconds(200));
        
        fadeOut.Completed += (s, args) => ToastNotification.Visibility = Visibility.Collapsed;
        
        ToastNotification.BeginAnimation(OpacityProperty, fadeOut);
        ToastTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, slideOut);
    }

    private void BtnViewPrefs_Click(object sender, RoutedEventArgs e)
    {
        var prefsWindow = new GpuPreferencesWindow(_prefAdapter, _loc, _config, _inventory);
        prefsWindow.Owner = this;
        prefsWindow.ShowDialog();
    }
}