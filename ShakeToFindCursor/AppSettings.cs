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
    
    // UI Elements (legacy/basic)
    public double MagnificationFactor { get; set; } = 6.0;
    public int HoldDurationMs { get; set; } = 240;
    
    // Animation Spring Parameters
    public double ExpandStiffness { get; set; } = 700.0;
    public double ExpandDamping { get; set; } = 42.0;
    public double ShrinkStiffness { get; set; } = 280.0;
    public double ShrinkDamping { get; set; } = 38.0;
    public double FinalStiffness { get; set; } = 160.0;
    public double FinalDamping { get; set; } = 26.0;

    // Animation Timing
    public double ReleaseBlendMs { get; set; } = 200.0;
    public double ReleaseCurvePower { get; set; } = 2.6;

    // Visual Style
    public string AnimationPreset { get; set; } = "macOS Classic";
    public bool UseOverlayRenderer { get; set; } = true;
    public double OverlayRingOpacity { get; set; } = 0.4;
    public string OverlayColor { get; set; } = "#808080";
    public double OverlayRingThickness { get; set; } = 3.0;
    public bool ShowSpotlight { get; set; } = true;

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
                // Default values - faithful to Apple's implementation
                break;
                
            case "Subtle":
                settings.MagnificationFactor = 3.0;
                settings.HoldDurationMs = 180;
                settings.ExpandStiffness = 800.0;
                settings.ExpandDamping = 50.0;
                settings.ShrinkStiffness = 350.0;
                settings.ShrinkDamping = 45.0;
                settings.FinalStiffness = 200.0;
                settings.FinalDamping = 32.0;
                settings.ReleaseBlendMs = 150.0;
                settings.ReleaseCurvePower = 2.2;
                settings.OverlayRingOpacity = 0.3;
                settings.OverlayRingThickness = 2.0;
                settings.ShowSpotlight = false;
                break;
                
            case "Dramatic":
                settings.MagnificationFactor = 8.0;
                settings.HoldDurationMs = 400;
                settings.ExpandStiffness = 500.0;
                settings.ExpandDamping = 32.0;
                settings.ShrinkStiffness = 180.0;
                settings.ShrinkDamping = 28.0;
                settings.FinalStiffness = 120.0;
                settings.FinalDamping = 20.0;
                settings.ReleaseBlendMs = 300.0;
                settings.ReleaseCurvePower = 3.0;
                settings.OverlayRingOpacity = 0.5;
                settings.OverlayRingThickness = 4.0;
                settings.ShowSpotlight = true;
                break;
                
            case "Snappy":
                settings.MagnificationFactor = 5.0;
                settings.HoldDurationMs = 200;
                settings.ExpandStiffness = 1200.0;
                settings.ExpandDamping = 70.0;
                settings.ShrinkStiffness = 500.0;
                settings.ShrinkDamping = 60.0;
                settings.FinalStiffness = 300.0;
                settings.FinalDamping = 45.0;
                settings.ReleaseBlendMs = 100.0;
                settings.ReleaseCurvePower = 2.0;
                settings.OverlayRingOpacity = 0.4;
                settings.OverlayRingThickness = 3.0;
                settings.ShowSpotlight = true;
                break;
                
            case "Smooth":
                settings.MagnificationFactor = 5.5;
                settings.HoldDurationMs = 320;
                settings.ExpandStiffness = 400.0;
                settings.ExpandDamping = 35.0;
                settings.ShrinkStiffness = 180.0;
                settings.ShrinkDamping = 30.0;
                settings.FinalStiffness = 100.0;
                settings.FinalDamping = 18.0;
                settings.ReleaseBlendMs = 280.0;
                settings.ReleaseCurvePower = 3.2;
                settings.OverlayRingOpacity = 0.35;
                settings.OverlayRingThickness = 3.5;
                settings.ShowSpotlight = true;
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
