using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WpfMessageBox = System.Windows.MessageBox;
using WpfRadioButton = System.Windows.Controls.RadioButton;

namespace ShakeToFindCursor;

public partial class SettingsWindow : Window
{
    private bool _isLoading = true;
    private readonly ObservableCollection<string> _excludedApps = new();

    // Changes apply live; the cursor-cache rebuild and disk save are debounced so we
    // don't rebuild 15×64 cursor frames on every slider tick during a drag.
    private readonly DispatcherTimer _commitTimer;
    private bool _magnificationDirty;

    public SettingsWindow()
    {
        InitializeComponent();
        ListExcludedApps.ItemsSource = _excludedApps;

        _commitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _commitTimer.Tick += async (_, _) =>
        {
            _commitTimer.Stop();
            await CommitAsync();
        };

        LoadSettings();
        _isLoading = false;
    }

    private void LoadSettings()
    {
        var s = App.CurrentSettings;

        ToggleStartup.IsChecked = s.RunOnStartup;
        SliderSensitivity.Value = s.Sensitivity;
        SliderMagnification.Value = s.MagnificationFactor;
        ToggleFullscreen.IsChecked = s.DisableInFullscreen;

        _excludedApps.Clear();
        foreach (var app in s.ExcludedProcesses)
            _excludedApps.Add(app);

        UpdateAllLabels();
    }

    private void UpdateAllLabels()
    {
        LabelSensitivity.Text = $"{SliderSensitivity.Value:0}";
        LabelMagnification.Text = $"{SliderMagnification.Value:0.0}×";
    }

    #region Live apply

    // Push the current UI state into CurrentSettings and the running detector, then
    // schedule a debounced commit (cursor-cache rebuild if the size changed + disk save).
    private void ApplyLive(bool magnificationChanged = false)
    {
        if (_isLoading) return;

        var s = App.CurrentSettings;
        s.RunOnStartup = ToggleStartup.IsChecked == true;
        s.Sensitivity = SliderSensitivity.Value;
        s.MagnificationFactor = SliderMagnification.Value;
        s.DisableInFullscreen = ToggleFullscreen.IsChecked == true;
        s.ExcludedProcesses.Clear();
        foreach (var app in _excludedApps)
            s.ExcludedProcesses.Add(app);

        // Sensitivity is cheap to apply immediately; the size change needs a cache rebuild,
        // which the debounced commit does so we aren't rebuilding on every slider increment.
        App.Detector?.UpdateSettings(s);
        if (magnificationChanged) _magnificationDirty = true;

        _commitTimer.Stop();
        _commitTimer.Start();
    }

    private async Task CommitAsync()
    {
        var s = App.CurrentSettings;

        if (_magnificationDirty)
        {
            _magnificationDirty = false;
            await Task.Run(() => CursorHelper.InitCaches(s.MagnificationFactor));
            App.Animator?.UpdateSettings(s);
        }

        bool startupOk = s.Save();
        if (s.RunOnStartup && !startupOk)
        {
            WpfMessageBox.Show(
                "Failed to enable 'Launch at Login'. This may be blocked by antivirus or restricted permissions.",
                "Permission Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            _isLoading = true;
            ToggleStartup.IsChecked = false;
            _isLoading = false;
            s.RunOnStartup = false;
            s.Save();
        }

        ShowSaveIndicator();
    }

    #endregion

    #region Navigation

    private void NavCategory_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfRadioButton rb) return;

        if (PanelGeneral != null) PanelGeneral.Visibility = Visibility.Collapsed;
        if (PanelCompatibility != null) PanelCompatibility.Visibility = Visibility.Collapsed;

        ScrollViewer? targetPanel = rb.Name switch
        {
            "NavGeneral" => PanelGeneral,
            "NavCompatibility" => PanelCompatibility,
            _ => PanelGeneral
        };

        if (targetPanel != null)
        {
            targetPanel.Visibility = Visibility.Visible;
            targetPanel.Opacity = 0;

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            targetPanel.BeginAnimation(OpacityProperty, fadeIn);
        }
    }

    #endregion

    #region Settings events

    private void Toggle_Changed(object sender, RoutedEventArgs e) => ApplyLive();

    private void SliderSensitivity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (LabelSensitivity != null) LabelSensitivity.Text = $"{e.NewValue:0}";
        ApplyLive();
    }

    private void SliderMagnification_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (LabelMagnification != null) LabelMagnification.Text = $"{e.NewValue:0.0}×";
        ApplyLive(magnificationChanged: true);
    }

    private void BtnReset_Click(object sender, RoutedEventArgs e)
    {
        var result = WpfMessageBox.Show(
            "Reset all settings to their default values?",
            "Reset Settings",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        var defaults = new AppSettings();
        var current = App.CurrentSettings;
        current.Sensitivity = defaults.Sensitivity;
        current.MagnificationFactor = defaults.MagnificationFactor;
        current.DisableInFullscreen = defaults.DisableInFullscreen;
        current.RunOnStartup = defaults.RunOnStartup;
        current.ExcludedProcesses.Clear();

        _isLoading = true;
        LoadSettings();
        _isLoading = false;

        ApplyLive(magnificationChanged: true);
    }

    private void BtnAddCurrentApp_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var (processName, _, _) = FullscreenDetector.GetForegroundInfo();

            if (!string.IsNullOrEmpty(processName) &&
                !processName.Equals("ShakeToFindCursor", StringComparison.OrdinalIgnoreCase) &&
                !_excludedApps.Contains(processName))
            {
                _excludedApps.Add(processName);
                ApplyLive();
                ShowSaveIndicator($"Added {processName}");
            }
            else if (_excludedApps.Contains(processName))
            {
                WpfMessageBox.Show($"{processName} is already excluded.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"Could not detect foreground app: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnAddApp_Click(object sender, RoutedEventArgs e)
    {
        var picker = new ProcessPickerWindow { Owner = this };
        if (picker.ShowDialog() == true && !string.IsNullOrWhiteSpace(picker.SelectedProcessName))
        {
            string processName = picker.SelectedProcessName;
            if (!_excludedApps.Contains(processName))
            {
                _excludedApps.Add(processName);
                ApplyLive();
            }
        }
    }

    private void BtnRemoveApp_Click(object sender, RoutedEventArgs e)
    {
        if (ListExcludedApps.SelectedItem is string selected)
        {
            _excludedApps.Remove(selected);
            ApplyLive();
        }
    }

    #endregion

    #region Window chrome

    private void ShowSaveIndicator(string? customMessage = null)
    {
        SaveIndicator.Text = customMessage ?? "✓ Saved";

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)) { BeginTime = TimeSpan.Zero };
        var hold = new DoubleAnimation(1, 1, TimeSpan.FromSeconds(1.5)) { BeginTime = TimeSpan.FromMilliseconds(150) };
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(400)) { BeginTime = TimeSpan.FromMilliseconds(1650) };

        var storyboard = new Storyboard();
        foreach (var anim in new[] { fadeIn, hold, fadeOut })
        {
            Storyboard.SetTarget(anim, SaveIndicator);
            Storyboard.SetTargetProperty(anim, new PropertyPath(OpacityProperty));
            storyboard.Children.Add(anim);
        }
        storyboard.Begin();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else
            DragMove();
    }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void BtnCloseWindow_Click(object sender, RoutedEventArgs e) => Close();

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    #endregion
}
