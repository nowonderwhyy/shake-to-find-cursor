using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WinForms = System.Windows.Forms;

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
        ToggleEnabled.IsChecked = true; // App is running, so it's enabled
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

        // Appearance
        ComboRenderer.SelectedIndex = settings.UseOverlayRenderer ? 0 : 1;
        UpdateColorPreview(settings.OverlayColor);
        SliderRingOpacity.Value = settings.OverlayRingOpacity;
        SliderRingThickness.Value = settings.OverlayRingThickness;
        ToggleSpotlight.IsChecked = settings.ShowSpotlight;

        // Compatibility
        ToggleFullscreen.IsChecked = settings.DisableInFullscreen;
        _excludedApps.Clear();
        foreach (var app in settings.ExcludedProcesses)
        {
            _excludedApps.Add(app);
        }

        // Update labels
        UpdateAllLabels();
        UpdateCustomControlsEnabled();
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
        LabelMagnification.Text = $"{SliderMagnification.Value:0.0}x";
        LabelHoldDuration.Text = $"{SliderHoldDuration.Value:0} ms";
        LabelRingOpacity.Text = $"{SliderRingOpacity.Value:P0}";
        LabelRingThickness.Text = $"{SliderRingThickness.Value:0.0} px";
    }

    private void UpdateColorPreview(string colorHex)
    {
        try
        {
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorHex);
            ColorPreview.Background = new SolidColorBrush(color);
        }
        catch
        {
            ColorPreview.Background = new SolidColorBrush(Colors.Gray);
        }
    }

    private void UpdateCustomControlsEnabled()
    {
        bool isCustom = GetSelectedPreset() == "Custom";
        
        SliderMagnification.IsEnabled = isCustom;
        SliderHoldDuration.IsEnabled = isCustom;
        ComboExpandSpeed.IsEnabled = isCustom;
        ComboShrinkSpeed.IsEnabled = isCustom;
        ComboBounce.IsEnabled = isCustom;
    }

    private string GetSelectedPreset()
    {
        return (ComboPreset.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "macOS Classic";
    }

    private void MarkDirty()
    {
        if (_isLoading) return;
        _hasUnsavedChanges = true;
    }

    #region Navigation

    private void NavCategory_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.RadioButton rb) return;

        if (PanelGeneral != null) PanelGeneral.Visibility = Visibility.Collapsed;
        if (PanelAnimation != null) PanelAnimation.Visibility = Visibility.Collapsed;
        if (PanelAppearance != null) PanelAppearance.Visibility = Visibility.Collapsed;
        if (PanelCompatibility != null) PanelCompatibility.Visibility = Visibility.Collapsed;

        ScrollViewer? targetPanel = rb.Name switch
        {
            "NavGeneral" => PanelGeneral,
            "NavAnimation" => PanelAnimation,
            "NavAppearance" => PanelAppearance,
            "NavCompatibility" => PanelCompatibility,
            _ => PanelGeneral
        };

        if (targetPanel != null)
        {
            targetPanel.Visibility = Visibility.Visible;
            targetPanel.Opacity = 0;
            
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150))
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
        var result = System.Windows.MessageBox.Show(
            "This will reset all settings to their default values. Continue?",
            "Reset Settings",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _isLoading = true;
            var newSettings = new AppSettings();
            CopySettingsFrom(newSettings);
            LoadSettings();
            _isLoading = false;
            MarkDirty();
        }
    }

    private void CopySettingsFrom(AppSettings source)
    {
        var target = App.CurrentSettings;
        target.DistanceThreshold = source.DistanceThreshold;
        target.TimeWindowMs = source.TimeWindowMs;
        target.MagnificationFactor = source.MagnificationFactor;
        target.HoldDurationMs = source.HoldDurationMs;
        target.ExpandStiffness = source.ExpandStiffness;
        target.ExpandDamping = source.ExpandDamping;
        target.ShrinkStiffness = source.ShrinkStiffness;
        target.ShrinkDamping = source.ShrinkDamping;
        target.FinalStiffness = source.FinalStiffness;
        target.FinalDamping = source.FinalDamping;
        target.ReleaseBlendMs = source.ReleaseBlendMs;
        target.ReleaseCurvePower = source.ReleaseCurvePower;
        target.AnimationPreset = source.AnimationPreset;
        target.UseOverlayRenderer = source.UseOverlayRenderer;
        target.OverlayRingOpacity = source.OverlayRingOpacity;
        target.OverlayColor = source.OverlayColor;
        target.OverlayRingThickness = source.OverlayRingThickness;
        target.ShowSpotlight = source.ShowSpotlight;
        target.DisableInFullscreen = source.DisableInFullscreen;
        target.RunOnStartup = source.RunOnStartup;
        target.ExcludedProcesses.Clear();
        foreach (var p in source.ExcludedProcesses)
            target.ExcludedProcesses.Add(p);
    }

    #endregion

    #region Animation Tab Events

    private void ComboPreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || ComboPreset.SelectedItem == null) return;

        string presetName = GetSelectedPreset();
        UpdateCustomControlsEnabled();

        if (presetName != "Custom")
        {
            var preset = AppSettings.GetPreset(presetName);
            _isLoading = true;
            SliderMagnification.Value = preset.MagnificationFactor;
            SliderHoldDuration.Value = preset.HoldDurationMs;
            SetExpandSpeedFromSettings(preset);
            SetShrinkSpeedFromSettings(preset);
            SetBounceFromSettings(preset);
            SliderRingOpacity.Value = preset.OverlayRingOpacity;
            SliderRingThickness.Value = preset.OverlayRingThickness;
            ToggleSpotlight.IsChecked = preset.ShowSpotlight;
            UpdateAllLabels();
            _isLoading = false;
        }

        MarkDirty();
    }

    private void SliderMagnification_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (LabelMagnification != null) LabelMagnification.Text = $"{e.NewValue:0.0}x";
        MarkDirty();
    }

    private void SliderHoldDuration_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (LabelHoldDuration != null) LabelHoldDuration.Text = $"{e.NewValue:0} ms";
        MarkDirty();
    }

    private void ComboExpandSpeed_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        MarkDirty();
    }

    private void ComboShrinkSpeed_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        MarkDirty();
    }

    private void ComboBounce_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        MarkDirty();
    }

    private async void BtnTest_Click(object sender, RoutedEventArgs e)
    {
        BtnTest.IsEnabled = false;

        try
        {
            // Build temp settings from current UI values
            var tempSettings = BuildSettingsFromUI();
            
            await System.Threading.Tasks.Task.Run(() =>
            {
                CursorHelper.InitCaches(tempSettings.MagnificationFactor);
            });

            App.Animator?.UpdateSettings(tempSettings);
            App.Animator?.Excite(0.6);
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
            HoldDurationMs = (int)SliderHoldDuration.Value,
            UseOverlayRenderer = ComboRenderer.SelectedIndex == 0,
            OverlayRingOpacity = SliderRingOpacity.Value,
            OverlayRingThickness = SliderRingThickness.Value,
            ShowSpotlight = ToggleSpotlight.IsChecked == true,
            OverlayColor = (ColorPreview.Background is SolidColorBrush brush) 
                ? $"#{brush.Color.R:X2}{brush.Color.G:X2}{brush.Color.B:X2}"
                : "#808080"
        };
        
        ApplySpringSettings(settings);
        
        return settings;
    }

    #endregion

    #region Appearance Tab Events

    private void ComboRenderer_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        MarkDirty();
    }

    private void BtnColorPicker_Click(object sender, RoutedEventArgs e)
    {
        var colorDialog = new WinForms.ColorDialog
        {
            FullOpen = true,
            Color = System.Drawing.ColorTranslator.FromHtml(App.CurrentSettings.OverlayColor)
        };

        if (colorDialog.ShowDialog() == WinForms.DialogResult.OK)
        {
            var color = colorDialog.Color;
            string hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            UpdateColorPreview(hex);
            MarkDirty();
        }
    }

    private void SliderRingOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (LabelRingOpacity != null) LabelRingOpacity.Text = $"{e.NewValue:P0}";
        MarkDirty();
    }

    private void SliderRingThickness_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (LabelRingThickness != null) LabelRingThickness.Text = $"{e.NewValue:0.0} px";
        MarkDirty();
    }

    #endregion

    #region Compatibility Tab Events

    private void BtnAddCurrentApp_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var (processName, _, windowTitle) = FullscreenDetector.GetForegroundInfo();
            
            if (!string.IsNullOrEmpty(processName) && 
                !processName.Equals("ShakeToFindCursor", StringComparison.OrdinalIgnoreCase) &&
                !_excludedApps.Contains(processName))
            {
                _excludedApps.Add(processName);
                MarkDirty();
                
                ShowSaveIndicator($"Added: {processName}");
            }
            else if (processName.Equals("ShakeToFindCursor", StringComparison.OrdinalIgnoreCase))
            {
                System.Windows.MessageBox.Show("Cannot exclude this application.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else if (_excludedApps.Contains(processName))
            {
                System.Windows.MessageBox.Show($"{processName} is already in the list.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Could not detect foreground application: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnAddApp_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new InputDialog("Add Application", "Enter the process name (without .exe):");
        dialog.Owner = this;
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.ResponseText))
        {
            string processName = dialog.ResponseText.Trim();
            if (processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                processName = processName[..^4];
            }

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

            // Appearance
            settings.UseOverlayRenderer = ComboRenderer.SelectedIndex == 0;
            settings.OverlayColor = GetColorFromPreview();
            settings.OverlayRingOpacity = SliderRingOpacity.Value;
            settings.OverlayRingThickness = SliderRingThickness.Value;
            settings.ShowSpotlight = ToggleSpotlight.IsChecked == true;

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
                System.Windows.MessageBox.Show(
                    "Failed to enable 'Run on Startup'. This is usually caused by restricted permissions or antivirus software blocking registry edits.",
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
        settings.ExpandStiffness = ComboExpandSpeed.SelectedIndex switch
        {
            0 => 400.0,
            1 => 700.0,
            2 => 1200.0,
            _ => 700.0
        };

        settings.ShrinkStiffness = ComboShrinkSpeed.SelectedIndex switch
        {
            0 => 180.0,
            1 => 280.0,
            2 => 500.0,
            _ => 280.0
        };

        (settings.ExpandDamping, settings.ShrinkDamping) = ComboBounce.SelectedIndex switch
        {
            0 => (70.0, 60.0),
            1 => (50.0, 45.0),
            2 => (42.0, 38.0),
            3 => (30.0, 28.0),
            _ => (42.0, 38.0)
        };

        settings.FinalStiffness = settings.ShrinkStiffness * 0.5;
        settings.FinalDamping = settings.ShrinkDamping * 0.7;
    }

    private string GetColorFromPreview()
    {
        if (ColorPreview.Background is SolidColorBrush brush)
        {
            var color = brush.Color;
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }
        return "#808080";
    }

    private void ShowSaveIndicator(string? customMessage = null)
    {
        SaveIndicator.Text = customMessage ?? "● Settings saved";
        
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
        var hold = new DoubleAnimation(1, 1, TimeSpan.FromSeconds(2));
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(500));

        var storyboard = new Storyboard();
        fadeIn.BeginTime = TimeSpan.Zero;
        hold.BeginTime = TimeSpan.FromMilliseconds(200);
        fadeOut.BeginTime = TimeSpan.FromMilliseconds(2200);

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
            var result = System.Windows.MessageBox.Show(
                "You have unsaved changes. Do you want to apply them before closing?",
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

/// <summary>
/// Simple input dialog for adding process names
/// </summary>
public class InputDialog : Window
{
    private System.Windows.Controls.TextBox _textBox;
    public string ResponseText => _textBox.Text;

    public InputDialog(string title, string prompt)
    {
        Title = title;
        Width = 350;
        Height = 150;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1C1C1C"));
        ResizeMode = ResizeMode.NoResize;

        var grid = new Grid { Margin = new Thickness(20) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new TextBlock
        {
            Text = prompt,
            Foreground = new SolidColorBrush(Colors.White),
            Margin = new Thickness(0, 0, 0, 10)
        };
        Grid.SetRow(label, 0);

        _textBox = new System.Windows.Controls.TextBox
        {
            Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2A2A2A")),
            Foreground = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3D3D3D")),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 15)
        };
        Grid.SetRow(_textBox, 1);

        var buttonPanel = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };

        var okButton = new System.Windows.Controls.Button
        {
            Content = "OK",
            Width = 80,
            Padding = new Thickness(0, 6, 0, 6),
            Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#4EB3FF")),
            Foreground = new SolidColorBrush(Colors.Black),
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 0, 8, 0)
        };
        okButton.Click += (s, e) => { DialogResult = true; Close(); };

        var cancelButton = new System.Windows.Controls.Button
        {
            Content = "Cancel",
            Width = 80,
            Padding = new Thickness(0, 6, 0, 6),
            Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2A2A2A")),
            Foreground = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3D3D3D"))
        };
        cancelButton.Click += (s, e) => { DialogResult = false; Close(); };

        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);
        Grid.SetRow(buttonPanel, 2);

        grid.Children.Add(label);
        grid.Children.Add(_textBox);
        grid.Children.Add(buttonPanel);

        Content = grid;

        _textBox.Focus();
    }
}
