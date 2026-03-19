using System;
using System.Windows;

namespace ShakeToFindCursor;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        LoadSettings();
    }

    private void LoadSettings()
    {
        var settings = App.CurrentSettings;
        SliderMagnification.Value = settings.MagnificationFactor;
        SliderHold.Value = settings.HoldDurationMs;
        SliderThreshold.Value = settings.DistanceThreshold;
        SliderTime.Value = settings.TimeWindowMs;
        CheckStartup.IsChecked = settings.RunOnStartup;

        TxtMagnificationValue.Text = $"{settings.MagnificationFactor:0.0}x";
        TxtHoldValue.Text = $"{settings.HoldDurationMs} ms";
    }

    private void SliderMagnification_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtMagnificationValue != null)
            TxtMagnificationValue.Text = $"{SliderMagnification.Value:0.0}x";
    }

    private void SliderHold_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtHoldValue != null)
            TxtHoldValue.Text = $"{(int)SliderHold.Value} ms";
    }

    private async void BtnApply_Click(object sender, RoutedEventArgs e)
    {
        BtnApply.IsEnabled = false;

        try
        {
            var settings = App.CurrentSettings;
            settings.MagnificationFactor = SliderMagnification.Value;
            settings.HoldDurationMs = (int)SliderHold.Value;
            settings.DistanceThreshold = SliderThreshold.Value;
            settings.TimeWindowMs = (int)SliderTime.Value;
            settings.RunOnStartup = CheckStartup.IsChecked == true;

            settings.Save();

            await System.Threading.Tasks.Task.Run(() =>
            {
                CursorHelper.InitCaches(settings.MagnificationFactor);
            });

            App.Animator?.UpdateSettings(settings.MagnificationFactor, settings.HoldDurationMs);
        }
        finally
        {
            BtnApply.IsEnabled = true;
        }
    }

    private void BtnTest_Click(object sender, RoutedEventArgs e)
    {
        App.Animator?.Excite(1.0);
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
