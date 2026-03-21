using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ShakeToFindCursor;

public partial class SettingsWindow : Window, INotifyPropertyChanged
{
    private bool _isLoading = true;
    private bool _hasUnsavedChanges = false;
    private ObservableCollection<string> _excludedApps = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public SettingsWindow()
    {
        InitializeComponent();
        ListExcludedApps.ItemsSource = _excludedApps;
        LoadSettings();
        _isLoading = false;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void LoadSettings()
    {
        var settings = App.CurrentSettings;

        // General
        ToggleStartup.IsChecked = settings.RunOnStartup;
        SliderThreshold.Value = settings.DistanceThreshold;
        SliderTimeWindow.Value = settings.TimeWindowMs;

        // Animation
        SelectPreset(settings.AnimationPreset);
        SliderMagnification.Value = settings.MagnificationFactor;
        SliderHoldDuration.Value = settings.HoldDurationMs;
        SetExpandSpeedFromSettings(settings);
        SetShrinkSpeedFromSettings(settings);
        SetBounceFromSettings(settings);

        // Compatibility
        ToggleFullscreen.IsChecked = settings.DisableInFullscreen;
        _excludedApps.Clear();
        foreach (var app in settings.ExcludedProcesses)
        {
            _excludedApps.Add(app);
        }

        UpdateAllLabels();
    }

    private void SelectPreset(string presetName)
    {
        for (int i = 0; i < ComboPreset.Items.Count; i++)
        {
            if (ComboPreset.Items[i] is ComboBoxItem item &&
                item.Content?.ToString() == presetName)
            {
                ComboPreset.SelectedIndex = i;
                return;
            }
        }
        ComboPreset.SelectedIndex = 0;
    }

    private void SetExpandSpeedFromSettings(AppSettings settings)
    {
        if (settings.ExpandStiffness >= 1000) ComboExpandSpeed.SelectedIndex = 2;
        else if (settings.ExpandStiffness >= 600) ComboExpandSpeed.SelectedIndex = 1;
        else ComboExpandSpeed.SelectedIndex = 0;
    }

    private void SetShrinkSpeedFromSettings(AppSettings settings)
    {
        if (settings.ShrinkStiffness >= 400) ComboShrinkSpeed.SelectedIndex = 2;
        else if (settings.ShrinkStiffness >= 250) ComboShrinkSpeed.SelectedIndex = 1;
        else ComboShrinkSpeed.SelectedIndex = 0;
    }

    private void SetBounceFromSettings(AppSettings settings)
    {
        if (settings.ExpandDamping >= 60) ComboBounce.SelectedIndex = 0;
        else if (settings.ExpandDamping >= 45) ComboBounce.SelectedIndex = 1;
        else if (settings.ExpandDamping >= 35) ComboBounce.SelectedIndex = 2;
        else ComboBounce.SelectedIndex = 3;
    }

    private void UpdateAllLabels()
    {
        LabelThreshold.Text = $"{SliderThreshold.Value:0}";
        LabelTimeWindow.Text = $"{SliderTimeWindow.Value:0} ms";
        LabelMagnification.Text = $"{SliderMagnification.Value:0.0}×";
        LabelHoldDuration.Text = $"{SliderHoldDuration.Value:0} ms";
    }

    private string GetSelectedPreset()
    {
        return (ComboPreset.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "macOS Classic";
    }
    
    private void SwitchToCustomPreset()
    {
        if (_isLoading) return;
        for (int i = 0; i < ComboPreset.Items.Count; i++)
        {
            if (ComboPreset.Items[i] is ComboBoxItem item && item.Content?.ToString() == "Custom")
            {
                _isLoading = true;
                ComboPreset.SelectedIndex = i;
                _isLoading = false;
                break;
            }
        }
    }

    private void MarkDirty()
    {
        if (_isLoading) return;
        _hasUnsavedChanges = true;
    }

    #region Navigation

    private void NavCategory_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb) return;

        if (PanelGeneral != null) PanelGeneral.Visibility = Visibility.Collapsed;
        if (PanelAnimation != null) PanelAnimation.Visibility = Visibility.Collapsed;
        if (PanelCompatibility != null) PanelCompatibility.Visibility = Visibility.Collapsed;

        ScrollViewer? targetPanel = rb.Name switch
        {
            "NavGeneral" => PanelGeneral,
            "NavAnimation" => PanelAnimation,
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

    #region General Tab Events

    private void SliderThreshold_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (LabelThreshold != null) LabelThreshold.Text = $"{e.NewValue:0}";
        MarkDirty();
    }

    private void SliderTimeWindow_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (LabelTimeWindow != null) LabelTimeWindow.Text = $"{e.NewValue:0} ms";
        MarkDirty();
    }

    private void BtnReset_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Reset all settings to their default values?",
            "Reset Settings",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _isLoading = true;
            App.CurrentSettings = new AppSettings();
            LoadSettings();
            _isLoading = false;
            MarkDirty();
        }
    }

    #endregion

    #region Animation Tab Events

    private void ComboPreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || ComboPreset.SelectedItem == null) return;

        string presetName = GetSelectedPreset();

        if (presetName != "Custom")
        {
            var preset = AppSettings.GetPreset(presetName);
            _isLoading = true;
            SliderMagnification.Value = preset.MagnificationFactor;
            SliderHoldDuration.Value = preset.HoldDurationMs;
            SetExpandSpeedFromSettings(preset);
            SetShrinkSpeedFromSettings(preset);
            SetBounceFromSettings(preset);
            UpdateAllLabels();
            _isLoading = false;
        }

        MarkDirty();
    }

    private void SliderMagnification_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (LabelMagnification != null) LabelMagnification.Text = $"{e.NewValue:0.0}×";
        SwitchToCustomPreset();
        MarkDirty();
    }

    private void SliderHoldDuration_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (LabelHoldDuration != null) LabelHoldDuration.Text = $"{e.NewValue:0} ms";
        SwitchToCustomPreset();
        MarkDirty();
    }

    private void ComboExpandSpeed_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SwitchToCustomPreset();
        MarkDirty();
    }

    private void ComboShrinkSpeed_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SwitchToCustomPreset();
        MarkDirty();
    }

    private void ComboBounce_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SwitchToCustomPreset();
        MarkDirty();
    }

    private async void BtnTest_Click(object sender, RoutedEventArgs e)
    {
        BtnTest.IsEnabled = false;

        try
        {
            var tempSettings = BuildSettingsFromUI();

            await System.Threading.Tasks.Task.Run(() =>
            {
                CursorHelper.InitCaches(tempSettings.MagnificationFactor);
            });

            App.Animator?.UpdateSettings(tempSettings);
            App.Animator?.Excite(0.7);
        }
        finally
        {
            BtnTest.IsEnabled = true;
        }
    }
    
    private AppSettings BuildSettingsFromUI()
    {
        var settings = new AppSettings
        {
            MagnificationFactor = SliderMagnification.Value,
            HoldDurationMs = (int)SliderHoldDuration.Value
        };
        
        ApplySpringSettings(settings);
        
        return settings;
    }

    #endregion

    #region Compatibility Tab Events

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
                MarkDirty();
                ShowSaveIndicator($"Added {processName}");
            }
            else if (_excludedApps.Contains(processName))
            {
                MessageBox.Show($"{processName} is already excluded.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not detect foreground app: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnAddApp_Click(object sender, RoutedEventArgs e)
    {
        var picker = new ProcessPickerWindow();
        picker.Owner = this;
        if (picker.ShowDialog() == true && !string.IsNullOrWhiteSpace(picker.SelectedProcessName))
        {
            string processName = picker.SelectedProcessName;
            if (!_excludedApps.Contains(processName))
            {
                _excludedApps.Add(processName);
                MarkDirty();
            }
        }
    }

    private void BtnRemoveApp_Click(object sender, RoutedEventArgs e)
    {
        if (ListExcludedApps.SelectedItem is string selected)
        {
            _excludedApps.Remove(selected);
            MarkDirty();
        }
    }

    #endregion

    #region Bottom Bar

    private async void BtnApply_Click(object sender, RoutedEventArgs e)
    {
        BtnApply.IsEnabled = false;

        try
        {
            var settings = App.CurrentSettings;

            // General
            settings.RunOnStartup = ToggleStartup.IsChecked == true;
            settings.DistanceThreshold = SliderThreshold.Value;
            settings.TimeWindowMs = (int)SliderTimeWindow.Value;

            // Animation
            settings.AnimationPreset = GetSelectedPreset();
            settings.MagnificationFactor = SliderMagnification.Value;
            settings.HoldDurationMs = (int)SliderHoldDuration.Value;
            ApplySpringSettings(settings);

            // Compatibility
            settings.DisableInFullscreen = ToggleFullscreen.IsChecked == true;
            settings.ExcludedProcesses.Clear();
            foreach (var app in _excludedApps)
            {
                settings.ExcludedProcesses.Add(app);
            }

            bool startupSetSuccessfully = settings.Save();

            if (settings.RunOnStartup && !startupSetSuccessfully)
            {
                MessageBox.Show(
                    "Failed to enable 'Launch at Login'. This may be blocked by antivirus or restricted permissions.",
                    "Permission Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                ToggleStartup.IsChecked = false;
                settings.RunOnStartup = false;
                settings.Save();
            }

            await System.Threading.Tasks.Task.Run(() =>
            {
                CursorHelper.InitCaches(settings.MagnificationFactor);
            });

            App.Animator?.UpdateSettings(settings);

            _hasUnsavedChanges = false;
            ShowSaveIndicator();
        }
        finally
        {
            BtnApply.IsEnabled = true;
        }
    }

    private void ApplySpringSettings(AppSettings settings)
    {
        // Expand stiffness based on speed selection
        settings.ExpandStiffness = ComboExpandSpeed.SelectedIndex switch
        {
            0 => 450.0,   // Slow
            1 => 800.0,   // Medium
            2 => 1200.0,  // Fast
            _ => 800.0
        };

        // Shrink stiffness
        settings.ShrinkStiffness = ComboShrinkSpeed.SelectedIndex switch
        {
            0 => 200.0,   // Slow
            1 => 320.0,   // Medium
            2 => 550.0,   // Fast
            _ => 320.0
        };

        // Bounce (affects damping - lower = more bounce)
        (settings.ExpandDamping, settings.ShrinkDamping) = ComboBounce.SelectedIndex switch
        {
            0 => (75.0, 65.0),   // None - critically damped
            1 => (50.0, 45.0),   // Subtle
            2 => (40.0, 38.0),   // Medium
            3 => (28.0, 26.0),   // Bouncy
            _ => (45.0, 40.0)
        };

        // Final approach is always smoother
        settings.FinalStiffness = settings.ShrinkStiffness * 0.55;
        settings.FinalDamping = settings.ShrinkDamping * 0.7;
    }

    private void ShowSaveIndicator(string? customMessage = null)
    {
        SaveIndicator.Text = customMessage ?? "✓ Saved";

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
        var hold = new DoubleAnimation(1, 1, TimeSpan.FromSeconds(1.5));
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(400));

        var storyboard = new Storyboard();
        fadeIn.BeginTime = TimeSpan.Zero;
        hold.BeginTime = TimeSpan.FromMilliseconds(150);
        fadeOut.BeginTime = TimeSpan.FromMilliseconds(1650);

        Storyboard.SetTarget(fadeIn, SaveIndicator);
        Storyboard.SetTargetProperty(fadeIn, new PropertyPath(OpacityProperty));
        Storyboard.SetTarget(hold, SaveIndicator);
        Storyboard.SetTargetProperty(hold, new PropertyPath(OpacityProperty));
        Storyboard.SetTarget(fadeOut, SaveIndicator);
        Storyboard.SetTargetProperty(fadeOut, new PropertyPath(OpacityProperty));

        storyboard.Children.Add(fadeIn);
        storyboard.Children.Add(hold);
        storyboard.Children.Add(fadeOut);
        storyboard.Begin();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        if (_hasUnsavedChanges)
        {
            var result = MessageBox.Show(
                "Apply changes before closing?",
                "Unsaved Changes",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                BtnApply_Click(sender, e);
            }
            else if (result == MessageBoxResult.Cancel)
            {
                return;
            }
        }

        Close();
    }

    #endregion
}
