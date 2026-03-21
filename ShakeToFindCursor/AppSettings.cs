using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace ShakeToFindCursor;

public class AppSettings
{
    // Shake Detector
    public double DistanceThreshold { get; set; } = 1200;
    public int TimeWindowMs { get; set; } = 250;
    
    // Animation
    public double MagnificationFactor { get; set; } = 5.0;
    public int HoldDurationMs { get; set; } = 220;
    
    // Animation Spring Parameters (tuned for macOS feel)
    public double ExpandStiffness { get; set; } = 800.0;
    public double ExpandDamping { get; set; } = 45.0;
    public double ShrinkStiffness { get; set; } = 320.0;
    public double ShrinkDamping { get; set; } = 40.0;
    public double FinalStiffness { get; set; } = 180.0;
    public double FinalDamping { get; set; } = 28.0;

    // Animation Timing
    public double ReleaseBlendMs { get; set; } = 180.0;
    public double ReleaseCurvePower { get; set; } = 2.8;

    // Preset
    public string AnimationPreset { get; set; } = "macOS Classic";

    // App Exclusions
    public List<string> ExcludedProcesses { get; set; } = new();
    public bool DisableInFullscreen { get; set; } = true;
    
    // System
    public bool RunOnStartup { get; set; } = false;

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
        "ShakeToFindCursor", 
        "settings.json");

    public static AppSettings GetPreset(string presetName)
    {
        var settings = new AppSettings { AnimationPreset = presetName };
        
        switch (presetName)
        {
            case "macOS Classic":
                // Tuned for that satisfying Apple feel
                settings.MagnificationFactor = 5.0;
                settings.HoldDurationMs = 220;
                settings.ExpandStiffness = 800.0;
                settings.ExpandDamping = 45.0;
                settings.ShrinkStiffness = 320.0;
                settings.ShrinkDamping = 40.0;
                settings.FinalStiffness = 180.0;
                settings.FinalDamping = 28.0;
                settings.ReleaseBlendMs = 180.0;
                settings.ReleaseCurvePower = 2.8;
                break;
                
            case "Subtle":
                settings.MagnificationFactor = 3.0;
                settings.HoldDurationMs = 160;
                settings.ExpandStiffness = 900.0;
                settings.ExpandDamping = 55.0;
                settings.ShrinkStiffness = 400.0;
                settings.ShrinkDamping = 50.0;
                settings.FinalStiffness = 220.0;
                settings.FinalDamping = 35.0;
                settings.ReleaseBlendMs = 140.0;
                settings.ReleaseCurvePower = 2.2;
                break;
                
            case "Dramatic":
                settings.MagnificationFactor = 7.0;
                settings.HoldDurationMs = 350;
                settings.ExpandStiffness = 550.0;
                settings.ExpandDamping = 35.0;
                settings.ShrinkStiffness = 200.0;
                settings.ShrinkDamping = 30.0;
                settings.FinalStiffness = 130.0;
                settings.FinalDamping = 22.0;
                settings.ReleaseBlendMs = 260.0;
                settings.ReleaseCurvePower = 3.0;
                break;
                
            case "Snappy":
                settings.MagnificationFactor = 4.5;
                settings.HoldDurationMs = 180;
                settings.ExpandStiffness = 1200.0;
                settings.ExpandDamping = 75.0;
                settings.ShrinkStiffness = 550.0;
                settings.ShrinkDamping = 65.0;
                settings.FinalStiffness = 350.0;
                settings.FinalDamping = 50.0;
                settings.ReleaseBlendMs = 100.0;
                settings.ReleaseCurvePower = 2.0;
                break;
                
            case "Smooth":
                settings.MagnificationFactor = 5.5;
                settings.HoldDurationMs = 300;
                settings.ExpandStiffness = 450.0;
                settings.ExpandDamping = 38.0;
                settings.ShrinkStiffness = 200.0;
                settings.ShrinkDamping = 32.0;
                settings.FinalStiffness = 110.0;
                settings.FinalDamping = 20.0;
                settings.ReleaseBlendMs = 250.0;
                settings.ReleaseCurvePower = 3.2;
                break;
        }
        
        return settings;
    }

    public static AppSettings Load()
    {
        if (File.Exists(SettingsPath))
        {
            try {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            } catch { }
        }
        return new AppSettings();
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
