# API Reference

All public types in the `FaceTracker` assembly (`FaceFinderDemo` namespace).

---

## Namespace: `FaceFinderDemo`

### `Program`

**File:** `FaceTracker/Program.cs`

Application entry point.

| Member | Signature | Description |
|---|---|---|
| `Main` | `static void Main(string[] args)` | Builds the Avalonia application and starts the classic desktop lifetime. Decorated with `[STAThread]`. |

---

### `App`

**File:** `FaceTracker/App.axaml.cs`  
**Base:** `Avalonia.Application`

Avalonia application host. Loads `App.axaml` (which references the Fluent theme and
`MainWindow` data template) and initialises the Avalonia framework.

| Member | Signature | Description |
|---|---|---|
| `OnFrameworkInitializationCompleted` | `override void` | Creates and shows `MainWindow` when the desktop lifetime is ready. |

---

### `MainWindow`

**File:** `FaceTracker/MainWindow.axaml.cs`  
**Base:** `Avalonia.Controls.Window`

Root application window. Owns all pipeline objects and wires their lifetimes to the
window open/close events.

| Member | Signature | Description |
|---|---|---|
| `ViewModel` | `MainWindowViewModel` | Bound to `DataContext`; drives all UI state. |
| *(constructor)* | `MainWindow()` | Enumerates cameras, creates pipeline nodes, binds view model. |

**Pipeline wiring performed in the constructor:**

```
DeviceEnumerator → ViewModel.AvailableCameras
CameraDevice ──AttachSource──► FaceMeshDevice ──ImageAvailable──► DisplayHandler
```

---

### `MainWindowViewModel`

**File:** `FaceTracker/MainWindowViewModel.cs`  
**Implements:** `INotifyPropertyChanged`

All bindable UI state. Changes raise `PropertyChanged` so Avalonia data-binding
updates the view automatically.

| Property | Type | Default | Description |
|---|---|---|---|
| `AvailableCameras` | `List<string>` | `[]` | Display names for the camera combo box. |
| `SelectedCameraIndex` | `int` | `-1` | Zero-based index of the selected camera; `-1` = none. |
| `IsCapturing` | `bool` | `false` | `true` while the pipeline is running. Controls Start/Stop button states. |
| `ImagePath` | `string` | `""` | Human-readable label for the current source (e.g. `"Source: camera"`). |
| `ModelStatus` | `string` | `""` | MediaPipe model download / init status message. |
| `TrackHead` | `bool` | `true` | When `true`, mesh follows face; when `false`, mesh is de-rotated and centred. |

---

## Namespace: `FaceFinderDemo.Camera`

### `CameraDevice`

**File:** `FaceTracker/Camera/CameraDevice.cs`  
**Base:** `ImageProcessor`  
**Implements:** `IDisposable`

Webcam source node. See [camera-devices.md](camera-devices.md#cameradevice) for full detail.

| Member | Signature | Description |
|---|---|---|
| `StartCamera` | `void StartCamera(int cameraIndex)` | Opens the webcam and starts the `CameraCapture` background thread. No-op if already capturing. |
| `StopCamera` | `void StopCamera()` | Signals the loop to stop, joins the thread, disposes `VideoCapture`. No-op if not capturing. |
| `Dispose` | `void Dispose()` | Calls `StopCamera()`; idempotent. |
| `OnImageReceived` | `protected override void OnImageReceived(Mat)` | No-op — source node. |

---

### `VideoFileDevice`

**File:** `FaceTracker/Camera/VideoFileDevice.cs`  
**Base:** `ImageProcessor`  
**Implements:** `IDisposable`

Video-file source node. Mirrors `CameraDevice` but opens a file path instead of a
device index.

| Member | Signature | Description |
|---|---|---|
| `StartVideo` | `void StartVideo(string filePath)` | Opens the video file and starts the background capture thread. |
| `StopVideo` | `void StopVideo()` | Signals stop, joins thread, disposes `VideoCapture`. |
| `Dispose` | `void Dispose()` | Calls `StopVideo()`; idempotent. |
| `OnImageReceived` | `protected override void OnImageReceived(Mat)` | No-op — source node. |

---

### `ImageDevice`

**File:** `FaceTracker/Camera/ImageDevice.cs`  
**Base:** `ImageProcessor`

Static-image source node. Emits one frame synchronously on the calling thread.

| Member | Signature | Description |
|---|---|---|
| `LoadImage` | `void LoadImage(string filePath)` | Reads the image with `Cv2.ImRead` and immediately calls `OnImageAvailable`. |
| `OnImageReceived` | `protected override void OnImageReceived(Mat)` | No-op — source node. |

---

### `DeviceEnumerator`

**File:** `FaceTracker/Camera/DeviceEnumerator.cs`

Static utility class. Probes OpenCV camera indices to discover available devices.

| Member | Signature | Description |
|---|---|---|
| `GetDeviceNames` | `static IReadOnlyList<string> GetDeviceNames()` | Returns `["Camera 0", "Camera 1", …]` for each successfully opened index. |

---

## Namespace: `FaceFinderDemo.ImageProcessing`

### `ImageProcessor`

**File:** `FaceTracker/ImageProcessing/ImageProcessor.cs`  
**Abstract**

Base class for all pipeline nodes. See [pipeline.md](pipeline.md) for full contract.

| Member | Signature | Description |
|---|---|---|
| `ImageAvailable` | `EventHandler<ImageAvailableEventArgs>?` | Raised when this node produces a frame. |
| `AttachSource` | `void AttachSource(ImageProcessor source)` | Subscribes to `source.ImageAvailable`; detaches any previous source first. |
| `DetachSource` | `void DetachSource()` | Unsubscribes from the current upstream source. Safe to call when no source is attached. |
| `OnImageAvailable` | `protected void OnImageAvailable(Mat image)` | Fires `ImageAvailable` to deliver `image` to all downstream subscribers. |
| `OnImageReceived` | `protected abstract void OnImageReceived(Mat image)` | Called for each frame from upstream. Override to implement processing. |

---

### `ImageAvailableEventArgs`

**File:** `FaceTracker/ImageProcessing/ImageAvailableEventArgs.cs`  
**Base:** `EventArgs`

Event argument carrying a single `Mat` through the pipeline.

| Member | Type | Description |
|---|---|---|
| `Image` | `Mat` | The frame. **Do not dispose.** Call `.Clone()` to retain beyond the callback scope. |

---

### `OpenCvAvaloniaHelper`

**File:** `FaceTracker/ImageProcessing/OpenCvAvaloniaHelper.cs`

Static utility class bridging OpenCvSharp `Mat` objects to Avalonia's bitmap types for
display in the UI.

| Member | Signature | Description |
|---|---|---|
| `MatToAvaloniaBitmap` | `static WriteableBitmap MatToAvaloniaBitmap(Mat mat)` | Converts a BGR `Mat` to an Avalonia `WriteableBitmap` suitable for binding to an `Image` control. Handles pixel format mapping and row stride. |

**Conversion notes:**
- Input is assumed to be a BGR 8-bit-per-channel `Mat` (standard OpenCV format).
- Output is `PixelFormat.Bgra8888` — alpha channel is set to `0xFF` (fully opaque).
- The pixel data is copied — the returned bitmap does not hold a reference into the `Mat` buffer.

---

## Namespace: `FaceFinderDemo.FaceDetection`

### `FaceMeshDevice`

**File:** `FaceTracker/FaceDetection/FaceMeshDevice.cs`  
**Base:** `ImageProcessor`  
**Implements:** `IDisposable`

MediaPipe face-mesh processor node. See [face-detection.md](face-detection.md) for full detail.

| Member | Signature | Description |
|---|---|---|
| `OnStatus` | `Action<string>?` | Optional callback for model download and init status messages. Set before calling `InitializeAsync`. |
| `IsInitialized` | `bool` | `true` once `InitializeAsync` has completed successfully. |
| `TrackHead` | `bool` | `true` (default): mesh follows face. `false`: mesh is de-rotated and centred. |
| `InitializeAsync` | `Task InitializeAsync()` | Downloads models, constructs graph, registers callback, starts run. Idempotent. |
| `OnImageReceived` | `protected override void OnImageReceived(Mat image)` | Converts BGR→RGB, copies pixels to managed buffer, feeds MediaPipe graph, draws landmark overlay. |
| `Dispose` | `void Dispose()` | Closes graph, frees `GCHandle`, disposes `CalculatorGraph`. |

---

### `FaceMeshConnections`

**File:** `FaceTracker/FaceDetection/FaceMeshConnections.cs`  
**Static class — generated from `connections.txt`**

> ⚠️ Do not edit this file manually. Run `gen_connections.py` to regenerate it.

Provides static arrays of `(int, int)` landmark index pairs for each facial feature.

| Member | Type | Description |
|---|---|---|
| `Tesselation` | `(int, int)[]` | Full face mesh triangulation connections |
| `FaceOval` | `(int, int)[]` | Outer face boundary ring |
| `Lips` | `(int, int)[]` | Lip contour connections |
| `LeftEye` | `(int, int)[]` | Left eye contour connections |
| `RightEye` | `(int, int)[]` | Right eye contour connections |
| `LeftEyebrow` | `(int, int)[]` | Left eyebrow contour connections |
| `RightEyebrow` | `(int, int)[]` | Right eyebrow contour connections |
| `Nose` | `(int, int)[]` | Nose bridge and tip connections |
| `LeftIris` | `(int, int)[]` | Left iris boundary ring (478-landmark model only) |
| `RightIris` | `(int, int)[]` | Right iris boundary ring (478-landmark model only) |
| `IrisCenterLeft` | `int` | Landmark index for the left iris centre point |
| `IrisCenterRight` | `int` | Landmark index for the right iris centre point |

---

### `MediaPipeNative`

**File:** `FaceTracker/FaceDetection/MediaPipeNative.cs`

P/Invoke declarations for any supplementary native MediaPipe entry points not already
covered by `Mediapipe.Net`. Consumed internally by `FaceMeshDevice`.

---

### `MediaPipeResourceManager`

**File:** `FaceTracker/FaceDetection/MediaPipeResourceManager.cs`

Manages the TFLite model files required by the MediaPipe graph.

| Member | Signature | Description |
|---|---|---|
| `EnsureModelsDownloadedAsync` | `Task EnsureModelsDownloadedAsync(Action<string>? onStatus)` | Downloads any missing model files to the application's local data directory. Skips files that are already present. Reports progress via `onStatus`. |
