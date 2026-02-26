using Mediapipe.Net.Util;

namespace FaceFinderDemo.FaceDetection;

/// <summary>
/// Provides MediaPipe TFLite model files to the native runtime and downloads any
/// missing models from the Google MediaPipe assets CDN on first use.
/// </summary>
/// <remarks>
/// Only one instance may exist per process because the base <see cref="ResourceManager"/>
/// registers a global native callback. Attempting to create a second instance throws
/// <see cref="InvalidOperationException"/>.
/// </remarks>
public class MediaPipeResourceManager : ResourceManager
{
    private static bool _created;
    private static readonly object _lock = new();

    private readonly string _modelDir;

    private static readonly string[] RequiredModels =
    [
        "mediapipe/modules/face_detection/face_detection_short_range.tflite",
        "mediapipe/modules/face_landmark/face_landmark_with_attention.tflite",
        "mediapipe/modules/iris_landmark/iris_landmark.tflite",
    ];

    private const string ModelBaseUrl = "https://storage.googleapis.com/mediapipe-assets/";

    /// <summary>
    /// Initialises the resource manager and resolves the local model cache directory
    /// to <c>&lt;app base&gt;/mediapipe-models/</c>.
    /// Throws <see cref="InvalidOperationException"/> if an instance already exists.
    /// </summary>
    public MediaPipeResourceManager()
    {
        lock (_lock)
        {
            if (_created)
                throw new InvalidOperationException("MediaPipeResourceManager can only be created once.");
            _created = true;
        }
        _modelDir = Path.Combine(AppContext.BaseDirectory, "mediapipe-models");
    }

    /// <summary>Returns the path unchanged (MediaPipe virtual paths map 1-to-1 to local paths).</summary>
    public override PathResolver ResolvePath => path => path;

    /// <summary>
    /// Reads the requested model file from the local cache directory.
    /// Throws <see cref="FileNotFoundException"/> if the file has not been downloaded yet.
    /// </summary>
    public override ResourceProvider ProvideResource => path =>
    {
        var localPath = GetLocalPath(path);
        if (!File.Exists(localPath))
            throw new FileNotFoundException($"MediaPipe model not found: {localPath}");
        return File.ReadAllBytes(localPath);
    };

    /// <summary>
    /// Ensures all required TFLite models are present in the local cache, downloading
    /// any that are missing from the Google MediaPipe assets CDN.
    /// </summary>
    /// <param name="onStatus">
    /// Optional callback invoked with progress messages such as
    /// <c>"Downloading face_detection_short_range.tflite..."</c>.
    /// </param>
    public async Task EnsureModelsDownloadedAsync(Action<string>? onStatus = null)
    {
        Directory.CreateDirectory(_modelDir);
        using var http = new HttpClient();

        foreach (var modelPath in RequiredModels)
        {
            var localPath = GetLocalPath(modelPath);
            if (File.Exists(localPath))
                continue;

            var fileName = Path.GetFileName(modelPath);
            onStatus?.Invoke($"Downloading {fileName}...");

            Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
            var url = ModelBaseUrl + fileName;
            var bytes = await http.GetByteArrayAsync(url);
            File.WriteAllBytes(localPath, bytes);

            onStatus?.Invoke($"Downloaded {fileName}");
        }
    }

    /// <summary>
    /// Converts a MediaPipe-style forward-slash asset path to an absolute local file path
    /// under the model cache directory, using the OS path separator.
    /// </summary>
    private string GetLocalPath(string mediaPath) =>
        Path.Combine(_modelDir, mediaPath.Replace('/', Path.DirectorySeparatorChar));
}
