using Mediapipe.Net.Util;

namespace FaceFinderDemo.FaceDetection;

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

    public override PathResolver ResolvePath => path => path;

    public override ResourceProvider ProvideResource => path =>
    {
        var localPath = GetLocalPath(path);
        if (!File.Exists(localPath))
            throw new FileNotFoundException($"MediaPipe model not found: {localPath}");
        return File.ReadAllBytes(localPath);
    };

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

    private string GetLocalPath(string mediaPath) =>
        Path.Combine(_modelDir, mediaPath.Replace('/', Path.DirectorySeparatorChar));
}
