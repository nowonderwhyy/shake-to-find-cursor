# Shake to Find Cursor

A high-performance Windows utility that replicates the macOS shake-to-find cursor feature with native OS-level integration and fluid physics.

## Overview

Shake to Find Cursor is a lightweight, background application for Windows that helps you locate your mouse pointer by rapidly shaking it. The cursor grows in proportion to how hard you shake and smoothly returns to normal once you stop — the same continuous behavior as macOS — making it immediately visible on high-resolution displays or multi-monitor setups.

## Core Features

### Native System Integration
Unlike typical Windows overlays that can be hidden by the Taskbar, Action Center, or full-screen applications, this utility modifies the native Windows cursor stream via the User32 API. This ensures the enlarged cursor is always rendered at the topmost visual layer, above every other element on your screen.

### Continuous, macOS-style Motion
The cursor size is a live readout of how vigorously you are shaking, exactly like macOS:
- **Grows with the shake**: The harder and longer you shake, the larger the cursor grows, up to your configured maximum.
- **Smooth, no bounce**: The size eases toward its target every frame with no overshoot or spring wobble.
- **Shrinks when you stop**: As soon as the shaking stops, the cursor smoothly returns to normal — there is no fixed "hold" timer.

### Intelligent Shake Detection
The detector distinguishes a deliberate back-and-forth shake from normal fast movement by measuring oscillation (total path length versus net displacement) and counting sharp direction reversals within a sliding time window. Detection is automatically suppressed while a mouse button is held, so dragging and selecting never enlarge the cursor.

### High-DPI Support
The engine bypasses standard Windows pixel limits to extract high-resolution cursor assets directly from your active system theme. This ensures the enlarged cursor remains sharp and anti-aliased even at high magnification levels.

### Simple Settings
A clean, macOS-minimal settings interface. Changes apply instantly — there is no Apply button:
- **Sensitivity**: How easily a shake is triggered.
- **Maximum Size**: How large the cursor grows during a vigorous shake.
- **Disable in Fullscreen / Excluded Apps**: Automatically pause in games, videos, or specific applications.
- **Launch at Login**: Automatically start the utility when you log in to Windows.

## Technical Details

- **Built with**: .NET 10 (WPF) and Win32 Interop.
- **Low Resource Usage**: Uses a low-level mouse hook and efficient frame caching to ensure minimal CPU and memory impact.
- **No Dependencies**: Runs as a standalone executable without requiring external libraries or complex installations.

## Installation

1. Download the latest release.
2. Run `ShakeToFindCursor.exe`.
3. Locate the icon in your system tray to access settings or exit the application.
