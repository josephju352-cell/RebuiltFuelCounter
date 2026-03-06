using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;
using System.Linq;

[RequireComponent(typeof(VideoPlayer))]
[DefaultExecutionOrder(-100)]
public class VideoInputManager : MonoBehaviour
{
    public enum SourceMode { WebCam, Video, Recording }

    [Header("Output Settings")]
    [SerializeField] private RenderTexture outputRenderTexture;

    [Header("Image Adjustments")]
    [SerializeField] private Shader processingShader;
    [Range(-1f, 1f)] public float brightness = 0f;
    [Range(0.5f, 3f)] public float contrast = 1f;

    [Header("Video Assets")]
    [SerializeField] private List<VideoClip> videos = new List<VideoClip>();

    public UnityEngine.Events.UnityEvent onCameraChanged;
    public UnityEngine.Events.UnityEvent onRecordingFinished;
    public event Action<RenderTexture> onFrameAvailable;

    private WebCamTexture _webCamTexture;
    private VideoPlayer _videoPlayer;
    private Material _processingMaterial;
    private string _currentDeviceName;
    private SourceMode _sourceMode = SourceMode.WebCam;
    private bool _didUpdateThisFrame;
    private long _lastFrameBeforeStep = -1;
    private int _lastUpdateFrame = -1;

    // Recording State
    private List<Texture2D> _recordedFrames = new List<Texture2D>();
    private List<float> _recordedTimestamps = new List<float>();
    private bool _isRecording = false;
    private const int MAX_RECORDED_FRAMES = 300;
    private int _playbackFrameIndex = 0;
    private bool _isRecordingPaused = false;
    private string _recordedInputName = "Unknown";
    private float _recordingStartTime;
    private float _playbackTime;

    public WebCamTexture CurrentWebCamTexture => _webCamTexture;
    public RenderTexture OutputRenderTexture => outputRenderTexture;
    public string CurrentDeviceName => _currentDeviceName;
    public SourceMode CurrentSourceMode => _sourceMode;
    public bool IsPlaying => _sourceMode switch {
        SourceMode.WebCam => _webCamTexture != null && _webCamTexture.isPlaying,
        SourceMode.Video => _videoPlayer != null && _videoPlayer.isPlaying,
        SourceMode.Recording => !_isRecordingPaused && _recordedFrames.Count > 0,
        _ => false
    };
    public bool IsVideo => _sourceMode == SourceMode.Video;
    public bool IsRecordingAvailable => _recordedFrames.Count > 0;
    public bool IsRecordingInProgress => _isRecording;
    public float RecordingDuration => _recordedTimestamps.Count > 0 ? _recordedTimestamps.Last() : 0;
    public bool IsPaused => _sourceMode switch {
        SourceMode.Video => _videoPlayer && !_videoPlayer.isPlaying,
        SourceMode.Recording => _isRecordingPaused,
        _ => false
    };
    public bool DidUpdateThisFrame => _didUpdateThisFrame;

    public long CurrentFrame
    {
        get
        {
            if (_sourceMode == SourceMode.Video && _videoPlayer) return _videoPlayer.frame;
            if (_sourceMode == SourceMode.Recording) return _playbackFrameIndex;
            return -1;
        }
    }

    public bool IsReady
    {
        get
        {
            return _sourceMode switch {
                SourceMode.Video => _videoPlayer && _videoPlayer.texture && _videoPlayer.texture.width > 16,
                SourceMode.WebCam => _webCamTexture && _webCamTexture.width > 16,
                SourceMode.Recording => _recordedFrames.Count > 0,
                _ => false
            };
        }
    }

    public float SourceAspectRatio
    {
        get
        {
            if (_sourceMode == SourceMode.Video && _videoPlayer && _videoPlayer.texture)
            {
                return (float)_videoPlayer.texture.width / _videoPlayer.texture.height;
            }
            else if (_sourceMode == SourceMode.WebCam && _webCamTexture)
            {
                return (float)_webCamTexture.width / _webCamTexture.height;
            }
            else if (_sourceMode == SourceMode.Recording && _recordedFrames.Count > 0)
            {
                return (float)_recordedFrames[0].width / _recordedFrames[0].height;
            }
            
            if (outputRenderTexture)
            {
                return (float)outputRenderTexture.width / outputRenderTexture.height;
            }

            return 1f;
        }
    }

    private const string PREF_CAMERA_NAME = "VideoInput_DeviceName";

    private void Awake()
    {
        _videoPlayer = GetComponent<VideoPlayer>();
        _videoPlayer.playOnAwake = false;
        _videoPlayer.isLooping = true;
        _videoPlayer.renderMode = VideoRenderMode.APIOnly;
        _videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
        _videoPlayer.skipOnDrop = true;
        _videoPlayer.sendFrameReadyEvents = true;
        _videoPlayer.frameReady += OnVideoFrameReady;

        if (!processingShader) processingShader = Shader.Find("Custom/BrightnessContrast");
        if (processingShader) _processingMaterial = new Material(processingShader);
        else Debug.LogError("[VideoInputManager] Processing shader not found!");
    }

    private void OnVideoFrameReady(VideoPlayer source, long frameIdx)
    {
        if (_sourceMode != SourceMode.Video) return;
        ProcessFrame(source.texture);
    }

    private void Start()
    {
        if (string.IsNullOrEmpty(_currentDeviceName))
        {
            string savedDevice = PlayerPrefs.GetString(PREF_CAMERA_NAME, "");

            #if !UNITY_EDITOR
            if (!string.IsNullOrEmpty(savedDevice) && savedDevice.StartsWith("[Video]")) savedDevice = "";
            #endif

            var devices = GetAvailableDevices();
            if (!string.IsNullOrEmpty(savedDevice) && devices.Contains(savedDevice))
            {
                StartInput(savedDevice);
            }
            else if (devices.Count > 0)
            {
                string firstDevice = devices.FirstOrDefault(d => !d.StartsWith("[Video]"));
                if (string.IsNullOrEmpty(firstDevice)) firstDevice = devices[0];
                StartInput(firstDevice);
            }
        }
    }

    private void Update()
    {
        if (Time.frameCount != _lastUpdateFrame)
        {
            _didUpdateThisFrame = false;
            _lastUpdateFrame = Time.frameCount;
        }

        if (_sourceMode == SourceMode.WebCam)
        {
            if (_webCamTexture && _webCamTexture.didUpdateThisFrame && IsReady)
            {
                ProcessFrame(_webCamTexture);
            }
        }
        else if (_sourceMode == SourceMode.Recording)
        {
            if (!_isRecordingPaused && _recordedFrames.Count > 0)
            {
                _playbackTime += Time.deltaTime;
                float totalDuration = _recordedTimestamps.Last();
                if (_playbackTime > totalDuration) _playbackTime = 0;

                int nextIndex = 0;
                for (int i = 0; i < _recordedTimestamps.Count; i++)
                {
                    if (_recordedTimestamps[i] <= _playbackTime) nextIndex = i;
                    else break;
                }

                if (nextIndex != _playbackFrameIndex)
                {
                    _playbackFrameIndex = nextIndex;
                    ProcessFrame(_recordedFrames[_playbackFrameIndex]);
                }
            }
        }
    }

    public void TogglePlayPause()
    {
        if (_sourceMode == SourceMode.Video && _videoPlayer)
        {
            if (_videoPlayer.isPlaying) _videoPlayer.Pause();
            else _videoPlayer.Play();
        }
        else if (_sourceMode == SourceMode.Recording)
        {
            _isRecordingPaused = !_isRecordingPaused;
            if (!_isRecordingPaused) _playbackTime = _recordedTimestamps[_playbackFrameIndex];
        }
    }

    public void EnsurePlaying()
    {
        if (_sourceMode == SourceMode.Video && _videoPlayer && !_videoPlayer.isPlaying) _videoPlayer.Play();
        else if (_sourceMode == SourceMode.WebCam && _webCamTexture && !_webCamTexture.isPlaying) _webCamTexture.Play();
        else if (_sourceMode == SourceMode.Recording) _isRecordingPaused = false;
    }

    public void StepFrame()
    {
        if (_sourceMode == SourceMode.Video && _videoPlayer && _videoPlayer.isPrepared)
        {
            if (_videoPlayer.isPlaying) _videoPlayer.Pause();
            long currentFrame = _videoPlayer.frame;
            long frameCount = (long)_videoPlayer.frameCount;
            bool isStuckAtEnd = currentFrame >= 0 && currentFrame == _lastFrameBeforeStep && currentFrame >= frameCount - 5;
            
            if (currentFrame >= frameCount - 1 || isStuckAtEnd) { _videoPlayer.frame = 0; _lastFrameBeforeStep = -1; }
            else { _lastFrameBeforeStep = currentFrame; if (_videoPlayer.canStep) _videoPlayer.StepForward(); else _videoPlayer.frame = currentFrame + 1; }
            
            ProcessFrame(_videoPlayer.texture);
        }
        else if (_sourceMode == SourceMode.Recording && _recordedFrames.Count > 0)
        {
            _isRecordingPaused = true;
            _playbackFrameIndex = (_playbackFrameIndex + 1) % _recordedFrames.Count;
            _playbackTime = _recordedTimestamps[_playbackFrameIndex];
            ProcessFrame(_recordedFrames[_playbackFrameIndex]);
        }
    }

    public void StartRecording()
    {
        if (_isRecording || _sourceMode == SourceMode.Recording || IsPaused) return;
        
        ClearRecording();
        _recordedInputName = _currentDeviceName ?? "Camera";
        _recordingStartTime = Time.realtimeSinceStartup;
        _isRecording = true;
        Debug.Log($"[VideoInputManager] Recording started for {_recordedInputName}...");
    }

    public void StopRecording()
    {
        if (!_isRecording) return;
        _isRecording = false;
        Debug.Log($"[VideoInputManager] Recording stopped. Captured {_recordedFrames.Count} frames.");
        
        if (onCameraChanged != null) onCameraChanged.Invoke();
        if (onRecordingFinished != null) onRecordingFinished.Invoke();
    }

    private void ClearRecording()
    {
        foreach (var tex in _recordedFrames) if (tex) Destroy(tex);
        _recordedFrames.Clear();
        _recordedTimestamps.Clear();
        _playbackFrameIndex = 0;
        _playbackTime = 0;
    }

    private void CaptureFrame()
    {
        if (!outputRenderTexture || _recordedFrames.Count >= MAX_RECORDED_FRAMES)
        {
            if (_recordedFrames.Count >= MAX_RECORDED_FRAMES) StopRecording();
            return;
        }

        Texture2D frame = new Texture2D(outputRenderTexture.width, outputRenderTexture.height, TextureFormat.RGB24, false);
        frame.name = $"RecordedFrame_{_recordedFrames.Count}";
        
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = outputRenderTexture;
        frame.ReadPixels(new Rect(0, 0, outputRenderTexture.width, outputRenderTexture.height), 0, 0);
        frame.Apply();
        RenderTexture.active = prev;

        frame.Compress(false);

        _recordedFrames.Add(frame);
        _recordedTimestamps.Add(Time.realtimeSinceStartup - _recordingStartTime);
    }

    private void ProcessFrame(Texture source)
    {
        if (!_processingMaterial || !outputRenderTexture || !source) return;

        _processingMaterial.SetFloat("_Brightness", brightness);
        _processingMaterial.SetFloat("_Contrast", contrast);

        Graphics.Blit(source, outputRenderTexture, _processingMaterial);
        _didUpdateThisFrame = true;

        if (_isRecording) CaptureFrame();
        
        onFrameAvailable?.Invoke(outputRenderTexture);
    }

    public List<string> GetAvailableDevices()
    {
        List<string> devices = new List<string>();
        foreach (var device in WebCamTexture.devices) devices.Add(device.name);
        foreach (var clip in videos) if (clip) devices.Add($"[Video] {clip.name}");
        if (_recordedFrames.Count > 0)
        {
            string cleanName = _recordedInputName;
            if (cleanName.StartsWith("[") && cleanName.Contains("]"))
            {
                int closeBracket = cleanName.IndexOf(']');
                cleanName = cleanName.Substring(closeBracket + 1).Trim();
            }
            devices.Add($"[Recording] {cleanName} ({RecordingDuration:F1}s)");
        }
        return devices;
    }

    public void StartInput(string deviceName)
    {
        StopInput();

        if (deviceName.StartsWith("[Recording]"))
        {
            _sourceMode = SourceMode.Recording;
            _playbackFrameIndex = 0;
            _playbackTime = 0;
            _isRecordingPaused = false;
            Debug.Log("[VideoInputManager] Started Recording playback");
        }
        else
        {
            VideoClip clip = videos.FirstOrDefault(c => c && $"[Video] {c.name}" == deviceName);
            if (clip)
            {
                _sourceMode = SourceMode.Video;
                _videoPlayer.clip = clip;
                _videoPlayer.Play();
                Debug.Log($"[VideoInputManager] Started Video: {deviceName}");
            }
            else
            {
                _sourceMode = SourceMode.WebCam;
                _webCamTexture = new WebCamTexture(deviceName, 1280, 720, 60);
                _webCamTexture.Play();
                Debug.Log($"[VideoInputManager] Started Webcam: {deviceName}");
            }
        }

        _currentDeviceName = deviceName;
        PlayerPrefs.SetString(PREF_CAMERA_NAME, deviceName);
        PlayerPrefs.Save();
        if (onCameraChanged != null) onCameraChanged.Invoke();
    }

    public void StopInput()
    {
        if (_webCamTexture) { _webCamTexture.Stop(); _webCamTexture = null; }
        if (_videoPlayer) _videoPlayer.Stop();
        _sourceMode = SourceMode.WebCam;
    }

    private void OnDestroy()
    {
        StopInput();
        ClearRecording();
        if (_processingMaterial) Destroy(_processingMaterial);
    }
}