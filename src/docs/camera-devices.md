# Camera & Media Source Devices

## Overview

All media sources live in the `FaceFinderDemo.Camera` namespace and extend
`ImageProcessor`. They are **source nodes** — they produce frames rather than receiving
them from upstream. Each source:

- Implements `OnImageReceived` as a no-op (source nodes have no upstream input).
- Calls `OnImageAvailable(frame)` to push frames into the pipeline.
- Manages its own background thread for continuous capture where applicable.
- Implements `IDisposable` to release native OpenCV handles.

---

## `CameraDevice`

**File:** `FaceTracker/Camera/CameraDevice.cs`

Captures frames from a physical webcam using OpenCvSharp's `VideoCapture`.

### Threading Model

```
UI Thread                    CameraCapture Thread
─────────────────            ────────────────────────────────────────────
StartCamera(index)  ──────►  new Thread(CaptureLoop) { IsBackground = true }
							 └─► while (_isCapturing)
								  VideoCapture.Read(frame)
								  frame.Clone()
								  OnImageAvailable(clone)

StopCamera()        ──────►  _isCapturing = false  (volatile write)
					◄──────  Thread.Join()          (blocks until loop exits)
							 VideoCapture.Dispose()
```

`_isCapturing` is declared `volatile`. This is the **only** cross-thread field —
no `lock` is needed for it. The `volatile` keyword ensures the capture thread sees the
UI thread's write without a memory-barrier stall.

> ⚠️ **Never call `StopCamera` from inside the `CameraCapture` thread.** The
> `Thread.Join` call will deadlock because the thread is waiting for itself.

### Key Members

| Member | Description |
|---|---|
| `StartCamera(int cameraIndex)` | Opens `VideoCapture(cameraIndex)`, spawns `CameraCapture` thread |
| `StopCamera()` | Sets `_isCapturing = false`, joins thread, disposes `VideoCapture` |
| `Dispose()` | Calls `StopCamera()` if still running; idempotent |
| `OnImageReceived(Mat)` | No-op — `CameraDevice` is a source node |

### Capture Loop Detail

```csharp
using var frame = new Mat();          // single reusable read buffer — NOT shared
while (_isCapturing)
{
	if (_capture == null || !_capture.IsOpened()) break;
	if (_capture.Read(frame) && !frame.Empty())
		OnImageAvailable(frame.Clone()); // clone transfers ownership downstream
	else
		Thread.Sleep(1);               // brief yield when camera not ready
}
```

The `using var frame` buffer is local to the loop and is reused each iteration.
`frame.Clone()` creates an independent copy that the downstream pipeline owns.

> **Known issue:** The cloned `Mat` is never explicitly disposed anywhere in the current
> pipeline. Each frame leaks a small amount of native OpenCV memory until the GC finaliser
> runs. See [AGENTS.md §4.4](../AGENTS.md#44-mat-memory-leak--known-issue).

---

## `VideoFileDevice`

**File:** `FaceTracker/Camera/VideoFileDevice.cs`

Plays back a video file frame-by-frame using `VideoCapture`, following the same
pattern as `CameraDevice` with a dedicated background thread.

### Differences from `CameraDevice`

| Aspect | `CameraDevice` | `VideoFileDevice` |
|---|---|---|
| `VideoCapture` constructor | `new VideoCapture(int index)` | `new VideoCapture(string filePath)` |
| End-of-stream behaviour | Camera never ends | Loop or stop when `Read` returns `false` |
| Source identifier | Camera index | File path string |

### Usage

```csharp
var video = new VideoFileDevice();
video.AttachSource(/* no upstream — it is a source node */);
// Wire downstream first, then start:
faceMesh.AttachSource(video);
video.StartVideo("path/to/video.mp4");
```

---

## `ImageDevice`

**File:** `FaceTracker/Camera/ImageDevice.cs`

Loads a single static image from disk and emits it as a one-shot frame. Useful for
testing the pipeline without a camera or video file.

### Behaviour

- Reads the file with `Cv2.ImRead`.
- Calls `OnImageAvailable(mat)` once on the calling thread.
- Does **not** start a background thread.
- The caller is responsible for deciding whether to re-emit (e.g., on a timer).

### Usage

```csharp
var img = new ImageDevice();
faceMesh.AttachSource(img);
img.LoadImage("path/to/photo.jpg");
```

---

## `DeviceEnumerator`

**File:** `FaceTracker/Camera/DeviceEnumerator.cs`

Discovers available video capture devices on the host system and returns display-friendly
names for population of the UI camera selection combo box.

### How it works

OpenCV does not provide a native device-listing API. `DeviceEnumerator` probes sequential
camera indices (0, 1, 2, …) by attempting to open each with `VideoCapture` and checking
`IsOpened()`. The probe loop stops at the first index that fails to open.

### Method

```csharp
public static IReadOnlyList<string> GetDeviceNames()
```

Returns a list such as `["Camera 0", "Camera 1"]`. The zero-based list index directly
corresponds to the `cameraIndex` parameter of `CameraDevice.StartCamera`.

### Usage in `MainWindow`

```csharp
ViewModel.AvailableCameras = DeviceEnumerator.GetDeviceNames().ToList();
```

The result populates `MainWindowViewModel.AvailableCameras`, which is data-bound to the
camera selection `ComboBox` in `MainWindow.axaml`.

> **Note:** The probe can be slow if many indices are tried. It is called once at startup
> and the result is cached in the view model.

---

## Choosing a Source Node

```
Has a physical webcam?       → CameraDevice
Replaying a recorded video?  → VideoFileDevice
Testing with a still image?  → ImageDevice
```

All three expose the same `ImageProcessor` interface downstream, so the rest of the
pipeline — including `FaceMeshDevice` and the Avalonia display layer — is completely
source-agnostic.
