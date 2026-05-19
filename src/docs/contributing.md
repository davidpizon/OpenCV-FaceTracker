# Contributing to OpenCV-FaceTracker

Thank you for contributing. Please read this document fully before opening a PR.

---

## Table of Contents

1. [Development Environment](#1-development-environment)
2. [Branching Strategy](#2-branching-strategy)
3. [Coding Standards](#3-coding-standards)
4. [Commit Messages](#4-commit-messages)
5. [Pull Request Process](#5-pull-request-process)
6. [Testing Guidelines](#6-testing-guidelines)
7. [Documentation Requirements](#7-documentation-requirements)
8. [Native Interop Safety Rules](#8-native-interop-safety-rules)
9. [Code Generation](#9-code-generation)
10. [Review Checklist](#10-review-checklist)

---

## 1. Development Environment

| Requirement | Version |
|---|---|
| .NET SDK | 10.0+ |
| Visual Studio | 2022 v18+ (or Rider 2024+) |
| Windows | 10 x64 (required for Win32 Avalonia backend) |
| Python | 3.9+ (for running code generators only) |

Clone and build:

```bash
git clone https://github.com/<org>/OpenCV-FaceTracker.git
cd OpenCV-FaceTracker
dotnet build FaceTracker
dotnet run --project FaceTracker
```

---

## 2. Branching Strategy

```
main                    ← production; protected; requires PR + review
  └─ feature/<name>     ← new features or non-trivial improvements
  └─ fix/<issue-id>     ← bug fixes referencing a GitHub issue
  └─ docs/<topic>       ← documentation-only changes
  └─ chore/<topic>      ← dependency bumps, tooling, formatting
```

- Branch off `main`.
- Keep branches short-lived — rebase onto `main` if they diverge significantly.
- Delete the branch after the PR is merged.

---

## 3. Coding Standards

### General

- Follow [Microsoft C# Coding Conventions](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions).
- Enable `<Nullable>enable</Nullable>` — annotate all reference-type parameters and
  return values. Do not use `!` (null-forgiving) to silence warnings without a
  clear documented reason.
- Prefer `using` declarations (`using var x = ...`) over `using` statements where the
  scope is the whole method body.
- Prefer `is null` / `is not null` over `== null` / `!= null`.

### Naming

| Element | Convention | Example |
|---|---|---|
| Classes, methods, properties | PascalCase | `CameraDevice`, `StartCamera` |
| Private fields | `_camelCase` | `_isCapturing` |
| Local variables, parameters | camelCase | `frameTimestamp` |
| Constants | PascalCase | `GraphConfig` |
| Async methods | Suffix `Async` | `InitializeAsync` |

### Namespaces

All types belong to `FaceFinderDemo` or a sub-namespace thereof:

```
FaceFinderDemo                  ← app host, window, view model
FaceFinderDemo.Camera           ← media source nodes
FaceFinderDemo.ImageProcessing  ← pipeline base classes and helpers
FaceFinderDemo.FaceDetection    ← face mesh node, topology data, native interop
```

### `unsafe` Code

- Restrict `unsafe` blocks to the **smallest possible scope**.
- Never let a raw pointer escape the `unsafe` block into a field or return value.
- Always accompany `unsafe` code with a comment explaining why it is necessary
  and what lifetime invariant it relies on.

---

## 4. Commit Messages

Follow the [Conventional Commits](https://www.conventionalcommits.org/) spec:

```
<type>(<scope>): <short summary>

[optional body]

[optional footer: Closes #<issue>]
```

### Types

| Type | When to use |
|---|---|
| `feat` | A new user-visible feature |
| `fix` | A bug fix |
| `docs` | Documentation only |
| `refactor` | Code change with no behaviour change |
| `perf` | Performance improvement |
| `test` | Adding or fixing tests |
| `chore` | Tooling, dependencies, CI |

### Examples

```
feat(face-detection): add multi-face support via num_faces side packet

fix(camera): prevent StopCamera deadlock when called from capture thread
Closes #42

docs(pipeline): document Mat frame-ownership rules

chore: bump OpenCvSharp4 to 4.10.0
```

- Summary line: ≤ 72 characters, imperative mood, no trailing period.
- Body: wrap at 80 characters; explain *why*, not just *what*.

---

## 5. Pull Request Process

1. Ensure `dotnet build` passes with **zero warnings**.
2. Self-review your diff against the [Review Checklist](#10-review-checklist).
3. Fill in the PR template (description, motivation, testing done).
4. Request at least one reviewer.
5. Address all review comments before merging.
6. Squash-merge into `main` — the squash commit message should follow Conventional
   Commits using the PR title.

---

## 6. Testing Guidelines

There is currently no automated test project. Until one is added:

- Manually verify all affected pipeline paths with a live webcam.
- Test `VideoFileDevice` with a short `.mp4` clip.
- Test `ImageDevice` with a still JPEG.
- Confirm the `FaceMeshDevice` processes frames without crashing when:
  - No face is present in frame.
  - Multiple faces are in frame (only first is processed — `num_faces: 1`).
  - `TrackHead` is toggled at runtime.
- Confirm `StopCamera` / `Dispose` does not deadlock under rapid start/stop cycles.

When adding a new feature, **add a test project** (`FaceTracker.Tests`) with:

- Unit tests for pure logic (e.g., `DeviceEnumerator`, `OpenCvAvaloniaHelper`).
- Integration tests that mock `VideoCapture` for pipeline flow.

---

## 7. Documentation Requirements

Every PR must maintain or improve documentation quality:

| Requirement | Detail |
|---|---|
| XML doc comments | Required on every `public` and `protected` member |
| `<param>` tags | Required for all parameters |
| `<returns>` tags | Required for non-void methods |
| `<exception>` tags | Document any exception that callers must handle |
| `/docs` updates | Update the relevant file in `docs/` for any public API change |
| `AGENTS.md` | Update if conventions, pitfalls, or architecture changes |
| `README.md` | Update if user-visible features or requirements change |

### XML doc example

```csharp
/// <summary>
/// Opens the camera at <paramref name="cameraIndex"/> and starts the capture
/// loop on a background thread. Does nothing if capture is already running.
/// </summary>
/// <param name="cameraIndex">
/// Zero-based OpenCV camera index corresponding to the position in the list
/// returned by <see cref="DeviceEnumerator.GetDeviceNames"/>.
/// </param>
/// <exception cref="InvalidOperationException">
/// Thrown if the camera cannot be opened at the given index.
/// </exception>
public void StartCamera(int cameraIndex) { ... }
```

---

## 8. Native Interop Safety Rules

These are **non-negotiable** — violations cause `ExecutionEngineException` or silent
memory corruption:

1. **Never pass a raw `Mat.Data` pointer to a native API** without first copying the
   pixel data into a managed `byte[]` via `Marshal.Copy`. The native side may hold the
   pointer beyond the `Mat`'s lifetime.

2. **Always store `GCHandle` as a field** on the owning object. A handle stored in a
   local variable will be collected by the GC, causing the native delegate to dangle.

3. **Free `GCHandle` only after `WaitUntilDone`** (or equivalent). The handle keeps the
   delegate alive; freeing it early lets the GC collect the delegate while native code
   still holds a function pointer to it.

4. **Do not call `StopCamera` from the `CameraCapture` thread** — `Thread.Join` will
   deadlock.

5. **Do not dispose a `Mat` received in `OnImageReceived`** — the upstream node owns it.

---

## 9. Code Generation

`FaceMeshConnections.cs` is generated from raw topology data, not hand-authored.

```
connections.txt          ← source of truth for landmark connection data
gen_connections.py       ← generator: reads connections.txt, writes FaceMeshConnections.cs
gen_cs.py                ← supplementary generator for other C# boilerplate
```

### Workflow

```bash
# 1. Edit connections.txt with updated/new topology data
# 2. Regenerate:
python gen_connections.py
# 3. Build to verify:
dotnet build FaceTracker
# 4. Commit both connections.txt AND the regenerated .cs file
```

**Never commit a manual edit to `FaceMeshConnections.cs` alone.** If the generator is
re-run later, your changes will be silently overwritten.

---

## 10. Review Checklist

Reviewers and authors should verify all of the following before approving a PR:

### Correctness
- [ ] `dotnet build` passes with zero warnings
- [ ] No raw `Mat.Data` pointer passed to native APIs
- [ ] No `GCHandle` freed before the native graph is idle
- [ ] No upstream `Mat` disposed inside a downstream `OnImageReceived`
- [ ] `OnImageAvailable` called at the end of every non-trivial `OnImageReceived` path

### Code Quality
- [ ] All `public` / `protected` members have complete XML doc comments
- [ ] `unsafe` blocks are minimal in scope and commented
- [ ] No nullable warnings suppressed without explanation
- [ ] New types placed in the correct namespace
- [ ] Async methods suffixed with `Async`

### Threading
- [ ] Cross-thread fields are either `volatile`, `Interlocked`, or `lock`-protected
- [ ] No `Thread.Join` called from the thread being joined
- [ ] UI updates marshalled to `Dispatcher.UIThread`

### Documentation
- [ ] `docs/api-reference.md` updated for new/changed public surface
- [ ] Relevant `docs/` page updated for behavioural changes
- [ ] `AGENTS.md` updated if conventions or architecture changed
- [ ] `README.md` updated if user-facing behaviour changed

### Code Generation
- [ ] If topology data changed: both `connections.txt` and regenerated `FaceMeshConnections.cs` committed together
