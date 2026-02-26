using OpenCvSharp;

namespace FaceFinderDemo.Camera;

/// <summary>
/// Cross-platform camera enumeration by probing VideoCapture indices.
/// </summary>
public static class DeviceEnumerator
{
    /// <summary>
    /// Probes VideoCapture indices 0–9 and returns display names for all usable cameras.
    /// Stops at the first index that cannot be opened, so the returned list reflects only
    /// consecutively available devices.
    /// </summary>
    /// <returns>
    /// A list of friendly names such as <c>["Camera 0", "Camera 1"]</c>.
    /// Returns an empty list when no cameras are available.
    /// </returns>
    public static List<string> GetDeviceNames()
    {
        var devices = new List<string>();
        for (int i = 0; i < 10; i++)
        {
            using var cap = new VideoCapture(i);
            if (!cap.IsOpened()) break;
            devices.Add($"Camera {i}");
        }
        return devices;
    }
}
