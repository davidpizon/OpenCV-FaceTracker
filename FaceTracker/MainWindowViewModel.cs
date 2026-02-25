using System;
using System.Collections.Generic;
using System.ComponentModel;
using FaceFinderDemo.FaceDetection;

namespace FaceFinderDemo;

public class MainWindowViewModel : INotifyPropertyChanged
{
    public List<string> AvailableCameras
    {
        get => _availableCameras;
        set { _availableCameras = value; OnPropertyChanged(nameof(AvailableCameras)); }
    }

    public int SelectedCameraIndex
    {
        get => _selectedCameraIndex;
        set { _selectedCameraIndex = value; OnPropertyChanged(nameof(SelectedCameraIndex)); }
    }

    public bool IsCapturing
    {
        get => _isCapturing;
        set { _isCapturing = value; UpdateState(); OnPropertyChanged(nameof(IsCapturing)); }
    }

    public void UpdateState()
    {
        ManualDetectionEnabled = SelectedDetectionMode == FaceDetectorDevice.DetectionModes.Manual && IsCapturing;
        PeriodDetectionEnabled = SelectedDetectionMode == FaceDetectorDevice.DetectionModes.Periodic;
    }

    public Array AvailableDetectionModes => Enum.GetValues(typeof(FaceDetectorDevice.DetectionModes));

    public FaceDetectorDevice.DetectionModes SelectedDetectionMode { get; set; }
    public int DetectionPeriod { get; set; }
    public bool DrawDetection { get; set; }
    public bool DrawProbableAreas { get; set; }

    public bool ManualDetectionEnabled
    {
        get => _manualDetectionEnabled;
        set { _manualDetectionEnabled = value; OnPropertyChanged(nameof(ManualDetectionEnabled)); }
    }

    public bool PeriodDetectionEnabled
    {
        get => _periodDetectionEnabled;
        set { _periodDetectionEnabled = value; OnPropertyChanged(nameof(PeriodDetectionEnabled)); }
    }

    public bool CurrentlyDetecting
    {
        get => _currentlyDetecting;
        set { _currentlyDetecting = value; OnPropertyChanged(nameof(CurrentlyDetecting)); }
    }

    public string LastDetection
    {
        get => _lastDetection;
        set { _lastDetection = value; OnPropertyChanged(nameof(LastDetection)); }
    }

    public string ImagePath
    {
        get => _imagePath;
        set { _imagePath = value; OnPropertyChanged(nameof(ImagePath)); }
    }

    List<string> _availableCameras = new();
    int _selectedCameraIndex = -1;
    bool _isCapturing;
    bool _manualDetectionEnabled;
    bool _periodDetectionEnabled;
    bool _currentlyDetecting;
    string _lastDetection = "";
    string _imagePath = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
