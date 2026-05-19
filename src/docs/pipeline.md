# Image-Processing Pipeline

## Overview

The pipeline is a singly-linked chain of `ImageProcessor` nodes. Each node subscribes to
its upstream neighbour via an `EventHandler<ImageAvailableEventArgs>` and re-emits a
(possibly modified) frame to its own subscribers when processing is complete.

---

## Node Contract

### Implementing a node

```csharp
public class MyNode : ImageProcessor, IDisposable
{
	protected override void OnImageReceived(Mat image)
	{
		// 1. Process `image` in-place, OR create a new Mat.
		// 2. Forward downstream — always call this, even if nothing changed.
		OnImageAvailable(image);
	}

	public void Dispose() { /* release any native resources */ }
}
```

### Wiring nodes together

```csharp
var camera    = new CameraDevice();
var faceMesh  = new FaceMeshDevice();

faceMesh.AttachSource(camera);   // faceMesh now receives every frame camera emits
```

Calling `AttachSource` a second time automatically detaches from the previous source
first, so re-wiring is safe at any point.

---

## Frame Ownership Rules

These rules are critical. Violating them causes native memory corruption or crashes.

| Rule | Reason |
|---|---|
| **Do not dispose** the `Mat` received in `OnImageReceived` | The frame is owned by the upstream node which reuses the buffer |
| **Clone before retaining** beyond the callback scope | The upstream buffer may be overwritten on the next capture tick |
| **Do not pass `Mat.Data` raw pointer to native APIs** | The pointer can dangle once `Mat` is disposed; copy via `Marshal.Copy` into a `byte[]` first |
| **Always call `OnImageAvailable`** at the end of processing | Failing to do so silently breaks all downstream nodes |

---

## `ImageProcessor` — Abstract Base Class

**Namespace:** `FaceFinderDemo.ImageProcessing`

### Fields & Events

| Member | Type | Description |
|---|---|---|
| `ImageAvailable` | `EventHandler<ImageAvailableEventArgs>?` | Raised when this node has a frame ready |

### Methods

| Method | Description |
|---|---|
| `AttachSource(ImageProcessor source)` | Subscribes to `source.ImageAvailable`; detaches any existing source first |
| `DetachSource()` | Unsubscribes from the current upstream source |
| `OnImageAvailable(Mat image)` | Fires `ImageAvailable` to deliver a frame to all downstream subscribers |
| `OnImageReceived(Mat image)` *(abstract)* | Override to implement per-node processing |

---

## `ImageAvailableEventArgs`

**Namespace:** `FaceFinderDemo.ImageProcessing`

Carries a single `Mat` frame through the pipeline via the `ImageAvailable` event.

| Property | Type | Description |
|---|---|---|
| `Image` | `Mat` | The frame. **Do not dispose.** Clone if you need to retain it. |

---

## Source Nodes

Source nodes originate frames rather than receiving them from upstream. They implement
`OnImageReceived` as a no-op and call `OnImageAvailable` directly from their own capture
loops.

| Class | Source | Thread |
|---|---|---|
| `CameraDevice` | Physical webcam via `VideoCapture` | Dedicated `CameraCapture` background thread |
| `VideoFileDevice` | Video file via `VideoCapture` | Dedicated background thread |
| `ImageDevice` | Single static image file | Fires once on the calling thread |

---

## Processor Nodes

| Class | Input | Output |
|---|---|---|
| `FaceMeshDevice` | Raw BGR `Mat` | Same `Mat` with face mesh drawn on top |

---

## Threading Considerations

- `CameraDevice` reads and fires events on its own `CameraCapture` background thread.
- `FaceMeshDevice.OnImageReceived` runs on whichever thread dispatched the event (i.e.,
  the `CameraCapture` thread). MediaPipe's `WaitUntilIdle` blocks that thread until the
  graph completes — this is intentional and provides natural back-pressure.
- The Avalonia UI handler receives frames on the `CameraCapture` thread and must
  marshal bitmap updates to the UI thread (e.g., via `Dispatcher.UIThread.Post`).

---

## Pipeline Teardown

1. Call the source node's stop method (e.g., `CameraDevice.StopCamera()`).  
2. The `_isCapturing` flag causes the capture loop to exit cleanly.  
3. `StopCamera` joins the background thread before disposing the `VideoCapture`.  
4. Processor nodes (e.g., `FaceMeshDevice`) should be disposed after the source is stopped
   to ensure no callbacks arrive on a partially-torn-down graph.
5. Call `DetachSource()` on each node or dispose them in reverse pipeline order.
