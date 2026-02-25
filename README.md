# OpenCV Face Tracker

Cross-platform face, eye, nose and mouth detection demo built with **.NET 10**, **Avalonia UI**, and **OpenCvSharp4**.

## Features

- Real-time webcam face detection
- Load image or video files for face detection
- Detection modes: Disabled, Periodic, All Frames, Manual
- Detects: face, eyes, nose, mouth
- Cross-platform: Windows, Linux, macOS

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- A webcam (optional, for live capture)

## Build and Run

```bash
cd FaceTracker
dotnet restore
dotnet run
```

Or build without running:
```bash
dotnet build
```

## Usage

1. **Webcam**: Select a camera from the dropdown, click **Start capturing**
2. **Load media**: Click **Load media** to open an image or video file (camera capture stops automatically)
3. **Stop**: Click **Stop capturing** to stop all input sources
4. **Advanced settings**: Enable the checkbox to configure detection mode, period, and drawing options

## Detection Modes

- **Disabled**: Show frames without detection
- **Periodic**: Detect faces every N milliseconds (configurable)
- **AllFrames**: Detect faces on every frame (CPU-intensive)
- **Manual**: Detect only when clicking "Detect face"
