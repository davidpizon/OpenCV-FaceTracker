# AGENTS.md — AI Agent Guide for OpenCV-FaceTracker

This file is the authoritative orientation document for AI coding agents (GitHub Copilot,
Codex, etc.) working inside this repository. Read it fully before making any changes.

---

## 1. Repository Purpose

**OpenCV-FaceTracker** is a real-time face-mesh overlay application built with:

| Layer | Technology |
|---|---|
| UI | Avalonia UI (cross-platform XAML, MVVM) |
| Camera capture | OpenCvSharp (`VideoCapture`) |
| Face landmark detection | MediaPipe.Net (`FaceLandmarkFrontCpu` graph) |
| Frame rendering | SkiaSharp / Avalonia bitmap pipeline |
| Runtime | .NET 10 |

The application opens a webcam (or video file / image), runs every frame through a
MediaPipe Face Mesh graph that returns 468–478 facial landmarks, draws the mesh overlay
directly onto the `Mat`, and displays the result in the Avalonia window.

---

## 2. Repository Layout

```
OpenCV-FaceTracker/
├── AGENTS.md                        ← you are here
├── README.md
├── docs/
│   ├── architecture.md
│   ├── pipeline.md
│   ├── face-detection.md
│   ├── camera-devices.md
│   ├── api-reference.md
│   └── contributing.md
└── FaceTracker/                     ← single C# project
	├── FaceTracker.csproj
	├── Program.cs
	├── App.axaml / App.axaml.cs
	├── MainWindow.axaml / .cs
	├── MainWindowViewModel.cs
	├── Camera/
	│   ├── CameraDevice.cs          ← webcam source node
	│   ├── VideoFileDevice.cs       ← video-file source node
	│   ├── ImageDevice.cs           ← static-image source node
	│   └── DeviceEnumerator.cs      ← enumerates OS cameras
	├── FaceDetection/
	│   ├── FaceMeshDevice.cs        ← MediaPipe processor node
	│   ├── FaceMeshConnections.cs   ← static landmark topology data
	│   ├── MediaPipeNative.cs       ← P/Invoke declarations
	│   └── MediaPipeResourceManager.cs ← TFLite model download
	├── ImageProcessing/
	│   ├── ImageProcessor.cs        ← abstract pipeline base class
	│   ├── ImageAvailableEventArgs.cs
	│   └── OpenCvAvaloniaHelper.cs  ← Mat → Avalonia Bitmap conversion
	├── Resources/
	│   └── haarcascades/            ← OpenCV XML classifiers (legacy)
	├── connections.txt              ← raw MediaPipe connection data
	├── gen_connections.py           ← code-generator: connections.txt → C#
	└── gen_cs.py                    ← supplementary code-generator
```

---

## 3. Build & Run

### Prerequisites

| Tool | Minimum version |
|---|---|
| .NET SDK | 10.0 |
| Windows | 10 x64 (Win32 Avalonia backend) |
| Webcam | Any DirectShow-compatible device |

### Build

```bash
cd FaceTracker
dotnet build
```

### Run

```bash
dotnet run --project FaceTracker
```

On first run `MediaPipeResourceManager` will download the required TFLite models into the
application's local data directory. Progress is surfaced via `MainWindowViewModel.ModelStatus`.

---

## 4. Key Architecture Constraints

### 4.1 Pipeline Node Contract

Every `ImageProcessor` subclass **must** follow these rules or native crashes will result:

- **Do not dispose** the `Mat` received in `OnImageReceived`. The frame is owned by the
  upstream node. Call `.Clone()` if you need to retain it beyond the callback.
- **Do not pass a raw pointer** from a locally-scoped `Mat` to any native API unless you
  can guarantee the pointer lifetime spans the entire native call. Use `Marshal.Copy` to
  copy pixel data into a managed `byte[]` first (see `FaceMeshDevice.OnImageReceived`).
- Always call `OnImageAvailable(image)` at the end of `OnImageReceived` to forward the
  (possibly annotated) frame downstream.

### 4.2 Native Interop — `GCHandle` for Callbacks

`FaceMeshDevice` pins its MediaPipe callback with `GCHandleType.Normal` (not `Pinned`).
Delegates contain managed object references and **cannot** be pinned on .NET 9+. The
handle must be kept alive for the full lifetime of the `CalculatorGraph`.

Do **not** call `_callbackHandle.Free()` while the graph is still running — free it only
after `_graph.WaitUntilDone()` completes (as done in `FaceMeshDevice.Dispose`).

### 4.3 Threading Model

| Thread | Name | Responsibility |
|---|---|---|
| Main (STA) | — | Avalonia UI message loop |
| Background | `CameraCapture` | `VideoCapture.Read` + event dispatch |
| ThreadPool | various | MediaPipe async work, .NET Timer |

`_isCapturing` in `CameraDevice` is `volatile`; no additional synchronisation is needed
for that flag. `FaceMeshDevice._currentFrame` is guarded by `_frameLock`.

### 4.4 `Mat` Memory Leak — Known Issue

`CameraDevice.CaptureLoop` calls `frame.Clone()` before raising `OnImageAvailable`. That
clone is never explicitly disposed anywhere in the current pipeline. Downstream nodes that
no longer need the frame should call `.Dispose()` on it, or a finalisation wrapper should
be added to `ImageAvailableEventArgs`.

---

## 5. Coding Conventions

- **Namespace:** `FaceFinderDemo` (matches assembly name `FaceTracker`).
- **Nullable:** enabled project-wide — annotate all reference types.
- **XML doc comments:** required on every `public` and `protected` member.
- **`unsafe` blocks:** restrict to the smallest possible scope; never leave raw pointers
  escaping into managed heap lifetime.
- **Avalonia MVVM:** state lives in `MainWindowViewModel`; the code-behind (`MainWindow.axaml.cs`)
  wires events and delegates display logic to helpers.
- **Python generators:** `FaceMeshConnections.cs` is generated from `connections.txt` via
  `gen_connections.py`. Edit the source data, then re-run the generator — do not hand-edit
  the generated C# file.

---

## 6. Adding a New Pipeline Node

1. Create a class in the appropriate namespace that extends `ImageProcessor` (and
   `IDisposable` if it holds native resources).
2. Override `OnImageReceived(Mat image)`: process the frame, then call
   `OnImageAvailable(image)` (or a new clone if you replaced the frame).
3. Wire it in `MainWindow.axaml.cs` by calling `newNode.AttachSource(previousNode)`.
4. Add XML doc comments and update `docs/api-reference.md`.

---

## 7. PR / Commit Checklist

- [ ] All public/protected members have XML doc comments.
- [ ] No raw `Mat` pointers are passed to native APIs without a managed-buffer copy.
- [ ] No `GCHandle` is freed before the native graph is idle.
- [ ] Any new pipeline node disposes its `Mat` clones.
- [ ] `docs/api-reference.md` updated for new/changed public surface.
- [ ] `README.md` updated if user-facing behaviour changed.
- [ ] `dotnet build` passes with zero warnings.

---

## 8. Common Pitfalls

| Pitfall | Consequence | Mitigation |
|---|---|---|
| Passing `rgbMat.Data` raw pointer to `ImageFrame` | `ExecutionEngineException` (use-after-free) | Copy to `byte[]` via `Marshal.Copy` first |
| Forgetting `GCHandle.Alloc` for native delegate | Delegate collected mid-run → crash | Always store handle as a field |
| Calling `StopCamera` from the capture thread | Deadlock on `Thread.Join` | Only call from UI / external thread |
| Editing `FaceMeshConnections.cs` by hand | Overwritten on next codegen run | Edit `connections.txt`, re-run `gen_connections.py` |
| Disposing upstream `Mat` in a downstream node | Access violation in the next upstream read | Never dispose; clone if retention needed |
