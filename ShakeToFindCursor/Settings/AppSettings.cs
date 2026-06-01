using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace ShakeToFindCursor;

public class AppSettings
{
    // Shake detection sensitivity: 1 = must shake hard to trigger, 10 = very easy.
    public double Sensitivity { get; set; } = 5.0;

    // Maximum cursor magnification reached during a vigorous shake.
    public double MagnificationFactor { get; set; } = 5.0;

    // App Exclusions
    public List<string> ExcludedProcesses { get; set; } = new();
    public bool DisableInFullscreen { get; set; } = true;

    // System
    public bool RunOnStartup { get; set; } = false;

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ShakeToFindCursor",
        "settings.json");

    /// <summary>Creates an independent copy (deep-copies the exclusion list).</summary>
    public AppSettings Clone()
    {
        var copy = (AppSettings)MemberwiseClone();
        copy.ExcludedProcesses = new List<string>(ExcludedProcesses);
        return copy;
    }

    public static AppSettings Load()
    {
        if (File.Exists(SettingsPath))
        {
            try
            {
                var json = File.ReadAllText(SettingsPath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                loaded.Clamp();
                return loaded;
            }
            catch (Exception ex)
            {
                LogLoadError(ex);
            }
        }
        return new AppSettings();
    }

    /// <summary>
    /// Clamps deserialized values to the ranges the UI can produce, so a hand-edited or
    /// corrupt settings file can't push the app into a bad state (e.g. a huge magnification
    /// factor blowing up the cursor frame cache).
    /// </summary>
    private void Clamp()
    {
        Sensitivity = Math.Clamp(Sensitivity, 1.0, 10.0);
        MagnificationFactor = Math.Clamp(MagnificationFactor, 2.0, 10.0);
    }

    private static void LogLoadError(Exception ex)
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath);
            if (dir != null)
            {
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "settings_error.log"), $"{DateTime.Now}\n{ex}\n\n");
            }
        }
        catch { }
    }

    public bool Save()
    {
        var dir = Path.GetDirectoryName(SettingsPath);
        if (dir != null && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);

        return ApplyStartupSettings();
    }

    private bool ApplyStartupSettings()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            if (key != null)
            {
                if (RunOnStartup)
                {
                    string? exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        key.SetValue("ShakeToFindCursor", $"\"{exePath}\"");
                    }
                }
                else
                {
                    key.DeleteValue("ShakeToFindCursor", false);
                }
                return true;
            }
            return false;
        }
        catch
        {
            return false; // Failed due to permissions/AV
        }
    }
}
