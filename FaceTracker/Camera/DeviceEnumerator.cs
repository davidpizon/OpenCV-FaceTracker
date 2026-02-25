using System.Collections.Generic;
using OpenCvSharp;

namespace FaceFinderDemo.Camera;

/// <summary>
/// Cross-platform camera enumeration by probing VideoCapture indices.
/// </summary>
public static class DeviceEnumerator
{
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
