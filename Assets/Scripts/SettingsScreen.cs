using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Linq;

public class SettingsScreen : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private VideoInputManager videoInputManager;
    [SerializeField] private FuelCounter fuelCounter;
    [SerializeField] private FuelDetector.FuelDetectorManager fuelDetectorManager;
    [SerializeField] private GameObject mainScreen;
    [SerializeField] private TMP_Dropdown videoDropdown;
    [SerializeField] private RawImage videoDisplayImage;

    [Header("Sliders (Optional)")]
    [SerializeField] private Slider yellowSensitivitySlider;
    [SerializeField] private Slider motionSensitivitySlider;
    [SerializeField] private Slider brightnessThresholdSlider;
    [SerializeField] private TextMeshProUGUI brightnessThresholdValueText;

    [Header("Display Mode Toggles")]
    [SerializeField] private Toggle processedToggle;
    [SerializeField] private Toggle backgroundToggle;
    [SerializeField] private Toggle detectorToggle;

    [Header("UI Components")]
    [SerializeField] private TextMeshProUGUI trackedCountText;
    [SerializeField] private TextMeshProUGUI fuelCountText;
    [SerializeField] private Button calibrationButton;

    [Header("Playback Controls")]
    [SerializeField] private Button playPauseButton;
    [SerializeField] private Button stepFrameButton;
    [SerializeField] private Image playPauseIcon;
    [SerializeField] private Sprite playSprite;
    [SerializeField] private Sprite pauseSprite;
    [SerializeField] private Button recordButton;
    [SerializeField] private GameObject recordIcon;
    [SerializeField] private GameObject stopIcon;

    private bool _needsAspectRefresh;

    private void OnEnable()
    {
        if (mainScreen) mainScreen.SetActive(false);

        if (!videoInputManager)
        {
            Debug.LogError("[SettingsScreen] VideoInputManager reference is missing!");
            return;
        }

        InitializeSliders();
        InitializeVideoDropdown();
        InitializeDisplayToggles();
        InitializePlaybackButtons();
        InitializeCalibrationButton();
        FixDropdownScrollSpeed();
        
        videoInputManager.onCameraChanged.AddListener(UpdateVideoDropdown);
        videoInputManager.onRecordingFinished.AddListener(OnRecordingFinished);
        _needsAspectRefresh = true;
    }

    private void OnDisable()
    {
        if (mainScreen) mainScreen.SetActive(true);
        if (videoInputManager)
        {
            videoInputManager.onCameraChanged.RemoveListener(UpdateVideoDropdown);
            videoInputManager.onRecordingFinished.RemoveListener(OnRecordingFinished);
        }
    }

    private void OnRecordingFinished()
    {
        // Switch to the recording that just finished
        List<string> devices = videoInputManager.GetAvailableDevices();
        string recordingName = devices.FirstOrDefault(d => d.StartsWith("[Recording"));
        if (!string.IsNullOrEmpty(recordingName))
        {
            videoInputManager.StartInput(recordingName);
        }
    }

    private void OnRecordButtonClicked()
    {
        if (videoInputManager.IsRecordingInProgress)
        {
            videoInputManager.StopRecording();
        }
        else
        {
            videoInputManager.StartRecording();
        }
        UpdatePlaybackUI();
    }


    private void InitializePlaybackButtons()
    {
        if (recordButton)
        {
            recordButton.onClick.RemoveAllListeners();
            recordButton.onClick.AddListener(OnRecordButtonClicked);
        }
        if (playPauseButton)
        {
            playPauseButton.onClick.RemoveAllListeners();
            playPauseButton.onClick.AddListener(() => videoInputManager.TogglePlayPause());
        }
        if (stepFrameButton)
        {
            stepFrameButton.onClick.RemoveAllListeners();
            stepFrameButton.onClick.AddListener(() => videoInputManager.StepFrame());
        }
    }

    private void InitializeCalibrationButton()
    {
        if (calibrationButton && fuelDetectorManager)
        {
            calibrationButton.onClick.RemoveAllListeners();
            calibrationButton.onClick.AddListener(() => fuelDetectorManager.TriggerCalibration());
        }
    }

    private void FixDropdownScrollSpeed()
    {
        if (!videoDropdown) return;
        Transform template = videoDropdown.transform.Find("Template");
        if (template)
        {
            ScrollRect scrollRect = template.GetComponent<ScrollRect>();
            if (scrollRect) scrollRect.scrollSensitivity = 100f;
        }
    }

    private void InitializeDisplayToggles()
    {
        if (processedToggle) processedToggle.onValueChanged.AddListener((isOn) => { if (isOn) UpdateDisplayTexture(); });
        if (backgroundToggle) backgroundToggle.onValueChanged.AddListener((isOn) => { if (isOn) UpdateDisplayTexture(); });
        if (detectorToggle) detectorToggle.onValueChanged.AddListener((isOn) => { if (isOn) UpdateDisplayTexture(); });
        
        // Default to processed
        if (processedToggle) processedToggle.isOn = true;
    }

    private void Update()
    {
        UpdateDisplayTexture();

        if (fuelCountText && fuelCounter) fuelCountText.SetText("{0}", fuelCounter.TotalFuelCount);

        UpdatePlaybackUI();
        UpdateDebugShapesVisibility();
        UpdateSliderInteractivity();
        SyncSensitivitySliders();
        HandleAspectRatioRefresh();
    }

    private void UpdateSliderInteractivity()
    {
        bool isDetectorView = detectorToggle && detectorToggle.isOn;
        
        if (yellowSensitivitySlider) yellowSensitivitySlider.gameObject.SetActive(isDetectorView);
        if (motionSensitivitySlider) motionSensitivitySlider.gameObject.SetActive(isDetectorView);
        if (brightnessThresholdSlider) brightnessThresholdSlider.gameObject.SetActive(isDetectorView);
        if (brightnessThresholdValueText) brightnessThresholdValueText.gameObject.SetActive(isDetectorView);
    }

    private void SyncSensitivitySliders()
    {
        if (!fuelDetectorManager) return;

        if (yellowSensitivitySlider && !Mathf.Approximately(yellowSensitivitySlider.value, fuelDetectorManager.yellowSensitivity))
        {
            yellowSensitivitySlider.SetValueWithoutNotify(fuelDetectorManager.yellowSensitivity);
            TriggerRefreshIfPaused();
        }

        if (motionSensitivitySlider && !Mathf.Approximately(motionSensitivitySlider.value, fuelDetectorManager.motionSensitivity))
        {
            motionSensitivitySlider.SetValueWithoutNotify(fuelDetectorManager.motionSensitivity);
            TriggerRefreshIfPaused();
        }

        if (brightnessThresholdSlider && !Mathf.Approximately(brightnessThresholdSlider.value, fuelDetectorManager.brightnessThreshold))
        {
            brightnessThresholdSlider.SetValueWithoutNotify(fuelDetectorManager.brightnessThreshold);
            TriggerRefreshIfPaused();
        }

        if (brightnessThresholdValueText)
        {
            brightnessThresholdValueText.SetText("{0:2}", fuelDetectorManager.brightnessThreshold);
        }
    }

    private void TriggerRefreshIfPaused()
    {
        if (videoInputManager && videoInputManager.IsPaused && fuelDetectorManager)
        {
            fuelDetectorManager.RefreshDetector();
        }
    }

    private void UpdateDebugShapesVisibility()
    {
        if (!videoDropdown || !fuelDetectorManager) return;
        
        bool isDropdownOpen = videoDropdown.transform.Find("Dropdown List") != null;
        fuelDetectorManager.showDebugShapes = !isDropdownOpen;
    }

    private void UpdatePlaybackUI()
    {
        bool isRecording = videoInputManager.IsRecordingInProgress;
        if (recordIcon) recordIcon.SetActive(!isRecording);
        if (stopIcon) stopIcon.SetActive(isRecording);

        bool isRecordingSource = videoInputManager.CurrentSourceMode == VideoInputManager.SourceMode.Recording;
        if (recordButton)
        {
            // Allow stopping if already recording, otherwise only allow if not paused and not a recording source
            recordButton.interactable = !isRecordingSource && (isRecording || !videoInputManager.IsPaused);
        }

        bool isPlaybackMode = !isRecording && videoInputManager && (videoInputManager.IsVideo || videoInputManager.IsRecordingAvailable);
        
        if (playPauseButton) playPauseButton.interactable = isPlaybackMode;
        if (stepFrameButton) stepFrameButton.interactable = isPlaybackMode;

        if (isPlaybackMode && playPauseIcon)
        {
            playPauseIcon.sprite = videoInputManager.IsPaused ? playSprite : pauseSprite;
        }
    }

    private void UpdateDisplayTexture()
    {
        if (!videoDisplayImage || !fuelDetectorManager) return;

        bool showTrackedCount = false;
        bool showCalibration = false;

        if (processedToggle && processedToggle.isOn)
        {
            videoDisplayImage.texture = fuelDetectorManager.SyncedVideo ? fuelDetectorManager.SyncedVideo : videoInputManager.OutputRenderTexture;
            showTrackedCount = true;
        }
        else if (backgroundToggle && backgroundToggle.isOn)
        {
            videoDisplayImage.texture = fuelDetectorManager.Background;
            showCalibration = true;
        }
        else if (detectorToggle && detectorToggle.isOn)
        {
            videoDisplayImage.texture = fuelDetectorManager.SyncedDetector;
            showTrackedCount = true;
        }

        if (calibrationButton) calibrationButton.gameObject.SetActive(showCalibration);

        if (trackedCountText)
        {
            long currentFrame = videoInputManager.CurrentFrame;
            if (currentFrame != -1)
            {
                trackedCountText.SetText("Tracked: {0}      Frame: {1}", fuelDetectorManager.ActiveTrackCount, currentFrame);
            }
            else
            {
                trackedCountText.SetText("Tracked: {0}", fuelDetectorManager.ActiveTrackCount);
            }
            trackedCountText.gameObject.SetActive(showTrackedCount);
        }
    }

    private void InitializeVideoDropdown()
    {
        if (!videoDropdown) return;
        videoDropdown.onValueChanged.RemoveListener(OnVideoSourceChanged);
        UpdateVideoDropdown();
        videoDropdown.onValueChanged.AddListener(OnVideoSourceChanged);
    }

    private void UpdateVideoDropdown()
    {
        if (!videoDropdown) return;
        
        List<string> deviceNames = videoInputManager.GetAvailableDevices();

        // Only update if the list has actually changed (count or content)
        bool changed = videoDropdown.options.Count != deviceNames.Count;
        if (!changed)
        {
            for (int i = 0; i < deviceNames.Count; i++)
            {
                if (videoDropdown.options[i].text != deviceNames[i])
                {
                    changed = true;
                    break;
                }
            }
        }

        if (!changed)
        {
            // Even if the list didn't change, the selection might have.
            SyncDropdownSelection(deviceNames);
            return;
        }

        videoDropdown.ClearOptions();

        if (deviceNames.Count == 0)
        {
            videoDropdown.AddOptions(new List<string> { "No Input Found" });
            videoDropdown.interactable = false;
            return;
        }

        videoDropdown.interactable = true;
        videoDropdown.AddOptions(deviceNames);

        SyncDropdownSelection(deviceNames);
    }

    private void SyncDropdownSelection(List<string> deviceNames)
    {
        string currentName = videoInputManager.CurrentDeviceName;
        int index = -1;
        
        if (currentName != null && currentName.StartsWith("[Recording"))
        {
            index = deviceNames.FindIndex(d => d.StartsWith("[Recording"));
        }
        else
        {
            index = deviceNames.IndexOf(currentName);
        }

        if (index >= 0) videoDropdown.SetValueWithoutNotify(index);
        else if (videoDropdown.options.Count > 0) videoDropdown.SetValueWithoutNotify(0);
    }

    private void InitializeSliders()
    {
        if (yellowSensitivitySlider && fuelDetectorManager)
        {
            yellowSensitivitySlider.onValueChanged.RemoveListener(OnYellowSensitivityChanged);
            yellowSensitivitySlider.value = fuelDetectorManager.yellowSensitivity;
            yellowSensitivitySlider.onValueChanged.AddListener(OnYellowSensitivityChanged);
        }

        if (motionSensitivitySlider && fuelDetectorManager)
        {
            motionSensitivitySlider.onValueChanged.RemoveListener(OnMotionSensitivityChanged);
            motionSensitivitySlider.value = fuelDetectorManager.motionSensitivity;
            motionSensitivitySlider.onValueChanged.AddListener(OnMotionSensitivityChanged);
        }

        if (brightnessThresholdSlider && fuelDetectorManager)
        {
            brightnessThresholdSlider.onValueChanged.RemoveListener(OnBrightnessThresholdChanged);
            brightnessThresholdSlider.value = fuelDetectorManager.brightnessThreshold;
            brightnessThresholdSlider.onValueChanged.AddListener(OnBrightnessThresholdChanged);
        }
    }

    private void OnVideoSourceChanged(int index)
    {
        List<string> deviceNames = videoInputManager.GetAvailableDevices();
        if (index < 0 || index >= deviceNames.Count) return;

        string selectedDeviceName = deviceNames[index];
        videoInputManager.StartInput(selectedDeviceName);
        _needsAspectRefresh = true;
    }

    private void HandleAspectRatioRefresh()
    {
        if (!_needsAspectRefresh || !videoDisplayImage || !videoInputManager) return;

        if (videoInputManager.DidUpdateThisFrame)
        {
            float aspect = videoInputManager.SourceAspectRatio;
            var rectTransform = videoDisplayImage.rectTransform;
            rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, rectTransform.rect.height * aspect);
            _needsAspectRefresh = false;
        }
    }

    private void OnYellowSensitivityChanged(float value)
    {
        if (fuelDetectorManager)
        {
            fuelDetectorManager.yellowSensitivity = value;
            TriggerRefreshIfPaused();
        }
    }

    private void OnMotionSensitivityChanged(float value)
    {
        if (fuelDetectorManager)
        {
            fuelDetectorManager.motionSensitivity = value;
            TriggerRefreshIfPaused();
        }
    }

    private void OnBrightnessThresholdChanged(float value)
    {
        if (fuelDetectorManager)
        {
            fuelDetectorManager.brightnessThreshold = value;
            TriggerRefreshIfPaused();
        }
    }
}