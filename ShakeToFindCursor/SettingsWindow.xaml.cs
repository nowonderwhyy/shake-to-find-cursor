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

    private async void BtnTest_Click(object sender, RoutedEventArgs e)
    {
        BtnTest.IsEnabled = false;

        try
        {
            // Ensure cache is updated with the current slider value before testing
            double currentMagnification = SliderMagnification.Value;
            await System.Threading.Tasks.Task.Run(() =>
            {
                CursorHelper.InitCaches(currentMagnification);
            });

            // Now test with the updated cache using moderate intensity (0.6 = 60%)
            // This is more representative of actual user shaking than 100%
            App.Animator?.UpdateSettings(currentMagnification, (int)SliderHold.Value);
            App.Animator?.Excite(0.6);
        }
        finally
        {
            BtnTest.IsEnabled = true;
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
