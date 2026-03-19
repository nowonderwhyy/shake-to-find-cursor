# Shake to Find Cursor

A high-performance Windows utility that replicates the macOS shake-to-find cursor feature with native OS-level integration and fluid physics.

## Overview

Shake to Find Cursor is a lightweight, background application for Windows that helps you locate your mouse pointer by rapidly shaking it. When a shake is detected, the cursor momentarily enlarges using a smooth, physics-based animation, making it immediately visible on high-resolution displays or multi-monitor setups.

## Core Features

### Native System Integration
Unlike typical Windows overlays that can be hidden by the Taskbar, Action Center, or full-screen applications, this utility modifies the native Windows cursor stream via the User32 API. This ensures the enlarged cursor is always rendered at the topmost visual layer, above every other element on your screen.

### Fluid Physics Engine
The application uses a sophisticated spring-physics model to drive its animations, providing a premium, natural feel:
- **Snappy Expansion**: The cursor grows quickly with a subtle, realistic bounce.
- **Natural Landing**: Based on Apple's Fluid Interface principles, the return to normal size follows a multi-phase deceleration curve (similar to a car coming to a stop) with a soft, critically-damped landing approach.
- **Interruptible Motion**: If you shake the mouse again while the cursor is shrinking, the animation seamlessly redirects back to the enlarged state, preserving its current velocity for continuous, fluid motion.

### Intelligent Shake Detection
The detection algorithm distinguishes between rapid intentional shakes and normal high-speed mouse movements. It calculates kinetic displacement and net deviation within a precise sliding time window to prevent false triggers.

### High-DPI Support
The engine bypasses standard Windows pixel limits to extract high-resolution cursor assets directly from your active system theme. This ensures the enlarged cursor remains sharp and anti-aliased even at high magnification levels.

### Customizable Settings
A dedicated settings interface provides full control over the experience:
- **Size when shaken**: Adjust the maximum magnification level.
- **Stay enlarged for**: Set the duration the cursor remains at peak size.
- **Detection Sensitivity**: Fine-tune how easily the shake is triggered.
- **Run at Startup**: Option to automatically start the utility when you log in to Windows.

## Technical Details

- **Built with**: .NET 10 (WPF) and Win32 Interop.
- **Low Resource Usage**: Uses a low-level mouse hook and efficient frame caching to ensure minimal CPU and memory impact.
- **No Dependencies**: Runs as a standalone executable without requiring external libraries or complex installations.

## Installation

1. Download the latest release.
2. Run `ShakeToFindCursor.exe`.
3. Locate the icon in your system tray to access settings or exit the application.
