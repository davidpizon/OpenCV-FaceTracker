# Architecture Overview

## Summary

OpenCV-FaceTracker is structured as a **linear image-processing pipeline** of composable
nodes. Each node receives a frame, optionally transforms it, and emits the result to the
next node. The Avalonia UI sits at the end of the pipeline and renders whatever the last
node emits.

---

## Component Diagram

```
┌─────────────────────────────────────────────────────────────────────┐
│  Media Sources  (FaceFinderDemo.Camera)                             │
│                                                                     │
│  ┌───────────────┐  ┌──────────────────┐  ┌─────────────────────┐  │
│  │  CameraDevice │  │ VideoFileDevice  │  │   ImageDevice       │  │
│  │  (webcam)     │  │ (video file)     │  │  (static image)     │  │
│  └──────┬────────┘  └────────┬─────────┘  └──────────┬──────────┘  │
│         │                   │                        │             │
└─────────┼───────────────────┼────────────────────────┼─────────────┘
		  │  ImageAvailable   │                        │
		  ▼                   ▼                        ▼
┌─────────────────────────────────────────────────────────────────────┐
│  Processing Nodes  (FaceFinderDemo.FaceDetection)                   │
│                                                                     │
│  ┌──────────────────────────────────────────────────────────────┐   │
│  │  FaceMeshDevice                                              │   │
│  │  • BGR→RGB conversion                                        │   │
│  │  • MediaPipe CalculatorGraph (FaceLandmarkFrontCpu)          │   │
│  │  • 468 / 478 landmark overlay drawn back onto the Mat        │   │
│  └──────────────────────────────┬───────────────────────────────┘   │
│                                 │                                   │
└─────────────────────────────────┼───────────────────────────────────┘
								  │  ImageAvailable
								  ▼
┌─────────────────────────────────────────────────────────────────────┐
│  UI / Display  (FaceFinderDemo)                                     │
│                                                                     │
│  MainWindow.axaml.cs                                                │
│  └─► OpenCvAvaloniaHelper.MatToAvaloniaBitmap()                     │
│       └─► Avalonia Image control (data-bound)                       │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

---

## Layer Responsibilities

| Layer | Namespace | Responsibility |
|---|---|---|
| Media Sources | `FaceFinderDemo.Camera` | Produce raw `Mat` frames on a background thread |
| Pipeline Base | `FaceFinderDemo.ImageProcessing` | Node contract, event wiring, frame-ownership rules |
| Face Detection | `FaceFinderDemo.FaceDetection` | Run MediaPipe, annotate frames with mesh overlay |
| UI | `FaceFinderDemo` | Avalonia window, MVVM view-model, bitmap conversion |

---

## Dependency Graph

```
Program.cs
 └── App (Avalonia application host)
	  └── MainWindow
		   ├── MainWindowViewModel  (INotifyPropertyChanged)
		   ├── DeviceEnumerator     (camera discovery)
		   ├── CameraDevice         (ImageProcessor)
		   │    └── ImageProcessor  (abstract base)
		   ├── FaceMeshDevice       (ImageProcessor)
		   │    ├── ImageProcessor
		   │    ├── FaceMeshConnections  (static topology data)
		   │    ├── MediaPipeNative      (P/Invoke)
		   │    └── MediaPipeResourceManager (model download)
		   └── OpenCvAvaloniaHelper (Mat → Bitmap)
```

---

## Third-Party Dependencies

| Package | Role |
|---|---|
| `OpenCvSharp4` | `Mat`, `VideoCapture`, colour conversion, drawing primitives |
| `Mediapipe.Net` | Managed wrapper around the MediaPipe C++ calculator graph |
| `Avalonia` | Cross-platform UI framework (Win32 backend used) |
| `SkiaSharp` | 2-D rendering backend for Avalonia |
| `Google.Protobuf` | Serialisation of MediaPipe side-packets and options |

---

## Data Flow — One Frame

```
1.  CameraDevice.CaptureLoop           reads Mat from VideoCapture
2.  frame.Clone()                      ownership transferred to pipeline
3.  OnImageAvailable(clone)            fires ImageAvailable event
4.  FaceMeshDevice.OnImageReceived     receives Mat
5.    BGR→RGB → managed byte[] copy    safe native handoff
6.    CalculatorGraph.AddPacket        pushes frame into MediaPipe
7.    WaitUntilIdle                    blocks until graph is done
8.    OnLandmarks callback             draws mesh onto the original Mat
9.  OnImageAvailable(image)            forwards annotated Mat downstream
10. MainWindow handler                 converts Mat → WriteableBitmap
11. Avalonia Image control             displays bitmap on screen
```

---

## Key Design Decisions

### Why a linear event chain instead of a queue?

Simplicity. The MediaPipe graph runs synchronously (`WaitUntilIdle`), so there is at most
one frame in flight at a time. A queue would add latency without throughput benefit.

### Why `GCHandleType.Normal` for the native callback?

Delegates contain managed object references and cannot be pinned on .NET 9+. `Normal`
keeps the delegate reachable (preventing GC collection) without requiring the object to
be immovable. See [AGENTS.md §4.2](../AGENTS.md#42-native-interop--gchandle-for-callbacks).

### Why copy pixel data before handing it to `ImageFrame`?

MediaPipe's native `ImageFrame` may hold a raw pointer to the supplied buffer. If the
source `Mat` is disposed before the graph finishes accessing it, a use-after-free occurs
and the CLR raises `ExecutionEngineException`. Copying into a managed `byte[]` gives the
native side a stable, GC-tracked buffer.
