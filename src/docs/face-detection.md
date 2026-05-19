# Face Detection — MediaPipe Face Mesh

## Overview

`FaceMeshDevice` is the pipeline processor node that integrates Google's
**MediaPipe Face Mesh** model. It accepts a raw BGR `Mat`, runs the
`FaceLandmarkFrontCpu` calculator graph, receives 468–478 normalised face landmarks, and
draws a colour-coded mesh overlay directly onto the frame before forwarding it downstream.

---

## Initialisation

```csharp
var faceMesh = new FaceMeshDevice();
faceMesh.OnStatus = msg => Console.WriteLine(msg); // optional progress messages
await faceMesh.InitializeAsync();
```

`InitializeAsync` performs the following steps in order:

1. Calls `MediaPipeResourceManager.EnsureModelsDownloadedAsync` — downloads any missing
   TFLite model files to the application's local data directory.
2. Constructs a `CalculatorGraph` from the embedded graph configuration string.
3. Allocates a `GCHandle` (`GCHandleType.Normal`) to keep the native callback delegate
   alive for the lifetime of the graph.
4. Registers `OnLandmarks` as the observer for the `multi_face_landmarks` output stream.
5. Calls `_graph.StartRun()`.

It is safe to call `InitializeAsync` multiple times — subsequent calls return immediately
if already initialised.

---

## MediaPipe Graph Configuration

```
input_stream:  "input_video"
output_stream: "multi_face_landmarks"
output_stream: "face_rects_from_landmarks"
output_stream: "face_detections"
output_stream: "face_rects_from_detections"
```

The graph uses two `ConstantSidePacketCalculator` nodes to inject:

| Side packet | Value | Effect |
|---|---|---|
| `num_faces` | `1` | Detect at most one face per frame |
| `with_attention` | `true` | Use the 478-landmark attention model (adds iris points) |

The core node is `FaceLandmarkFrontCpu` which outputs normalised `[0, 1]` landmark
coordinates relative to the frame dimensions.

---

## Frame Processing — `OnImageReceived`

```
BGR Mat received
	│
	▼
CvtColor BGR → RGB         (MediaPipe expects Srgb / RGB)
	│
	▼
Marshal.Copy into byte[]   (safe managed buffer — avoids use-after-free)
	│
	▼
new ImageFrame(Srgb, ...)  (wraps the managed byte[])
	│
	▼
ImageFramePacket + Timestamp
	│
	▼
graph.AddPacketToInputStream("input_video", packet)
	│
	▼
graph.WaitUntilIdle()      (blocks; OnLandmarks callback fires synchronously here)
	│
	▼
OnImageAvailable(image)    (annotated BGR Mat forwarded downstream)
```

> **Why copy to `byte[]`?**  
> Passing `rgbMat.Data` (a raw unmanaged pointer) directly to `ImageFrame` caused
> `System.ExecutionEngineException` because MediaPipe held the pointer past the point
> where `rgbMat` was disposed. Copying into a managed `byte[]` gives the native side a
> stable buffer whose lifetime is controlled by the GC, not the `using` block.

---

## Landmark Callback — `OnLandmarks`

Called by the native graph (on the graph's internal thread, marshalled by
`Mediapipe.Net`) for every frame that contains at least one detected face.

1. Acquires `_frameLock` to read `_currentFrame`.
2. Calls `packet.Get()` to deserialise the `NormalizedLandmarkList` vector.
3. Calls `DrawFaceMesh` for each face in the result.

---

## Drawing Subsystem

`DrawFaceMesh` renders the mesh in a single pass over the connection tables in
`FaceMeshConnections`. All coordinates are normalised `[0, 1]` and scaled to pixel space
by multiplying by `frame.Width` / `frame.Height`.

### Colour scheme

| Feature | Colour (BGR) | Thickness |
|---|---|---|
| Tessellation | Dim green (depth-modulated) | 1 px |
| Face oval | Light white `(220, 220, 220)` | 1 px |
| Lips | Red-blue `(80, 80, 220)` | 1 px |
| Left eye | Cyan `(220, 220, 0)` | 1 px |
| Right eye | Cyan `(220, 220, 0)` | 1 px |
| Left eyebrow | Yellow `(0, 220, 220)` | 1 px |
| Right eyebrow | Yellow `(0, 220, 220)` | 1 px |
| Nose | Light grey `(200, 200, 200)` | 1 px |
| Irises (478 only) | Cyan `(0, 255, 255)` | 1 px |

### Depth shading

Tessellation lines are shaded by the Z-coordinate of their start landmark:

```
depth = clamp((z + 0.15) / 0.30, 0, 1)
value = (int)(80 * (0.3 + 0.7 * depth))
color = (0, value, 0)   // dim to bright green
```

### Head tracking mode

| `TrackHead` | Behaviour |
|---|---|
| `true` (default) | Mesh follows face position in the frame |
| `false` | Mesh is de-rotated and centred; head orientation is removed using a 3-D orthonormal basis derived from eye and chin landmarks |

---

## `FaceMeshConnections` — Topology Data

**Namespace:** `FaceFinderDemo.FaceDetection`

Provides static read-only arrays of `(int a, int b)` tuples representing connections
between landmark indices for each facial feature region.

| Member | Landmark pairs |
|---|---|
| `Tesselation` | Full 468-point mesh triangulation |
| `FaceOval` | Outer face boundary ring |
| `Lips` | Lip contour connections |
| `LeftEye` / `RightEye` | Eye contour connections |
| `LeftEyebrow` / `RightEyebrow` | Eyebrow contour connections |
| `Nose` | Nose bridge and tip connections |
| `LeftIris` / `RightIris` | Iris ring connections (478-landmark model) |
| `IrisCenterLeft` / `IrisCenterRight` | Index of each iris centre landmark |

> **Note:** `FaceMeshConnections.cs` is **generated** from `connections.txt` by
> `gen_connections.py`. Do not edit it by hand — your changes will be overwritten.
> Edit `connections.txt` and re-run the generator.

---

## `MediaPipeResourceManager` — Model Download

**Namespace:** `FaceFinderDemo.FaceDetection`

Ensures the required TFLite model files exist on disk before the graph is constructed.

```csharp
var mgr = new MediaPipeResourceManager();
await mgr.EnsureModelsDownloadedAsync(statusCallback);
```

| Behaviour | Detail |
|---|---|
| Model storage location | Application local data directory |
| Already-present models | Skipped (no re-download) |
| Progress reporting | `Action<string>?` callback |

---

## Disposal

`FaceMeshDevice` implements `IDisposable`. The disposal sequence is:

1. Sets `_disposed = true` to stop `OnImageReceived` from accepting new frames.
2. Calls `_graph.CloseAllPacketSources()` then `_graph.WaitUntilDone()` — ensures the
   graph has finished all in-flight work before freeing the callback handle.
3. Frees the `GCHandle` (`_callbackHandle.Free()`).
4. Disposes the `CalculatorGraph`.

**Always dispose `FaceMeshDevice` before the source node stops dispatching events** to
avoid a callback arriving on a partially-disposed object.
