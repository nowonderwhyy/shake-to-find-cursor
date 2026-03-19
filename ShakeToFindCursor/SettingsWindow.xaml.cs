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

            bool startupSetSuccessfully = settings.Save();

            if (settings.RunOnStartup && !startupSetSuccessfully)
            {
                System.Windows.MessageBox.Show("Failed to enable 'Start automatically with Windows'. This is usually caused by restricted permissions or Antivirus software blocking registry edits.",
                                "Permission Error",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                CheckStartup.IsChecked = false;
                settings.RunOnStartup = false;
                settings.Save(); // Save again with it disabled
            }

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
