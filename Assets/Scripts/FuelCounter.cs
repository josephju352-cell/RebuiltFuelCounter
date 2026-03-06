using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class FuelCounter : MonoBehaviour
{
    public static FuelCounter Instance { get; private set; }

    [Header("Simulation Settings")]
    public bool useExternalCounter = false;
    [SerializeField] private float minInterval = 1.0f;
    [SerializeField] private float maxInterval = 5.0f;
    [SerializeField] private int fuelPerTick = 10;

    [Header("UGUI Display")]
    [SerializeField] private TextMeshProUGUI scoreText;
    [SerializeField] private TextMeshProUGUI rateText;
    [SerializeField] private TextMeshProUGUI unitsText;
    [SerializeField] private TextMeshProUGUI timerText;

    [Header("UGUI Buttons")]
    [SerializeField] private Button resetCountButton;
    [SerializeField] private Button resetTimerButton;
    [SerializeField] private Button rateToggleButton;
    [SerializeField] private Button muteButton;
    [SerializeField] private Image muteIconImage;

    [Header("Mute Icons")]
    [SerializeField] private Sprite soundOnSprite;
    [SerializeField] private Sprite soundOffSprite;

    [Header("Sound Effects")]
    [SerializeField] private AudioSource[] melodicSources;

    public int TotalFuelCount { get; private set; }
    public int StartFuelCount { get; private set; }
    public DateTime StartTime { get; private set; }
    public bool DisplayPerMinute { get; private set; } = true;
    public bool IsMuted { get; private set; } = false;

    private bool _timerStarted = false;
    private int _noteIndex = 0;
    private const string PREF_MUTE = "FuelCounter_Muted";

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        IsMuted = PlayerPrefs.GetInt(PREF_MUTE, 0) == 1;
    }

    private void Start()
    {
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
        ResetCount(); // This will also call ResetTimer
        
        // Apply initial mute state
        SetMute(IsMuted);

        if (resetCountButton) resetCountButton.onClick.AddListener(ResetCount);
        if (resetTimerButton) resetTimerButton.onClick.AddListener(ResetTimer);
        if (rateToggleButton) rateToggleButton.onClick.AddListener(ToggleUnits);
        if (muteButton) muteButton.onClick.AddListener(ToggleMute);

        if (!useExternalCounter)
        {
            StartCoroutine(SimulateFuelRoutine());
        }
    }

    public void ToggleMute()
    {
        SetMute(!IsMuted);
    }

    public void SetMute(bool mute)
    {
        IsMuted = mute;
    }

    private float _nextUIUpdateTime;
    private const float UI_UPDATE_INTERVAL = 0.1f;

    private void Update()
    {
        // Apply mute state on Main Thread
        float targetVolume = IsMuted ? 0f : 1f;
        if (!Mathf.Approximately(AudioListener.volume, targetVolume))
        {
            AudioListener.volume = targetVolume;
            PlayerPrefs.SetInt(PREF_MUTE, IsMuted ? 1 : 0);
            PlayerPrefs.Save();
        }

        if (Time.time >= _nextUIUpdateTime)
        {
            UpdateUI();
            _nextUIUpdateTime = Time.time + UI_UPDATE_INTERVAL;
        }
    }

    public void ToggleUnits()
    {
        DisplayPerMinute = !DisplayPerMinute;
        UpdateUI();
    }

    public void IncrementCount(int amount = 1)
    {
        if (!_timerStarted)
        {
            _timerStarted = true;
            StartTime = DateTime.Now;
        }

        TotalFuelCount += amount;
        
        if (melodicSources != null && melodicSources.Length > 0)
        {
            AudioSource source = melodicSources[_noteIndex];
            if (source)
            {
                source.PlayOneShot(source.clip);
            }
            _noteIndex = (_noteIndex + 1) % melodicSources.Length;
        }
    }

    private IEnumerator SimulateFuelRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(UnityEngine.Random.Range(minInterval, maxInterval));
            IncrementCount(fuelPerTick);
        }
    }

    public void ResetCount()
    {
        TotalFuelCount = 0;
        _noteIndex = 0;
        ResetTimer();
        Debug.Log("[FuelCounter] Count and Timer Reset");
    }

    public void ResetTimer()
    {
        _timerStarted = false;
        StartFuelCount = TotalFuelCount;
        Debug.Log($"[FuelCounter] Timer Reset. Start Fuel: {StartFuelCount}");
    }

    public float GetRate()
    {
        if (!_timerStarted) return 0f;

        float elapsedSeconds = (float)(DateTime.Now - StartTime).TotalSeconds;
        // Ensure at least 0.5 seconds have passed to avoid huge spikes or divide by zero
        if (elapsedSeconds < 0.5f) return 0f;

        int fuelDelta = TotalFuelCount - StartFuelCount;
        float ratePerSecond = fuelDelta / elapsedSeconds;

        return DisplayPerMinute ? ratePerSecond * 60f : ratePerSecond;
    }

    public float GetRatePerMinute()
    {
        if (!_timerStarted) return 0f;

        float elapsedMinutes = (float)(DateTime.Now - StartTime).TotalMinutes;
        // Ensure at least some time has passed (0.5 seconds / 60)
        if (elapsedMinutes < (0.5f / 60f)) return 0f;

        int fuelDelta = TotalFuelCount - StartFuelCount;
        return fuelDelta / elapsedMinutes;
    }

    public string GetElapsedTime()
    {
        if (!_timerStarted) return "00:00:00";

        TimeSpan span = DateTime.Now - StartTime;
        return string.Format("{0:D2}:{1:D2}:{2:D2}", (int)span.TotalHours, span.Minutes, span.Seconds);
    }

    private void UpdateUI()
    {
        if (scoreText) scoreText.SetText("{0}", TotalFuelCount);
        if (rateText) rateText.SetText(DisplayPerMinute ? "{0:1}" : "{0:2}", GetRate());
        if (unitsText) unitsText.SetText(DisplayPerMinute ? "/min" : "/sec");
        if (timerText) timerText.SetText(GetElapsedTime());

        if (muteIconImage)
        {
            muteIconImage.sprite = IsMuted ? soundOffSprite : soundOnSprite;
        }
    }
}
