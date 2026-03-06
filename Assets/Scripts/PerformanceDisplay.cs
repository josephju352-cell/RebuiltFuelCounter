using UnityEngine;
using TMPro;

[RequireComponent(typeof(TextMeshProUGUI))]
public class PerformanceDisplay : MonoBehaviour
{
    private TextMeshProUGUI _text;
    private FrameTiming[] _frameTimings = new FrameTiming[1];
    private float _cpuAvg;
    private float _gpuAvg;

    private void Awake()
    {
        _text = GetComponent<TextMeshProUGUI>();
    }

    private void OnEnable()
    {
        _cpuAvg = 0;
        _gpuAvg = 0;
    }

    private float _nextUIUpdateTime;
    private const float UI_UPDATE_INTERVAL = 0.25f;

    private void Update()
    {
        FrameTimingManager.CaptureFrameTimings();
        uint count = FrameTimingManager.GetLatestTimings(1, _frameTimings);

        if (count > 0)
        {
            float cpu = (float)_frameTimings[0].cpuFrameTime;
            float gpu = (float)_frameTimings[0].gpuFrameTime;

            // Rolling smoothing,
            // skip erroneous times (caused by app pauses)
            if (cpu < 500f) _cpuAvg = Mathf.Lerp(_cpuAvg, cpu, 0.25f);
            if (gpu < 500f) _gpuAvg = Mathf.Lerp(_gpuAvg, gpu, 0.25f);

            if (Time.time >= _nextUIUpdateTime)
            {
                _text.SetText("CPU {0:1}ms | GPU {1:1}ms", _cpuAvg, _gpuAvg);
                _nextUIUpdateTime = Time.time + UI_UPDATE_INTERVAL;
            }
        }
        else if (Time.time >= _nextUIUpdateTime)
        {
            _text.SetText("Performance data unavailable");
            _nextUIUpdateTime = Time.time + UI_UPDATE_INTERVAL;
        }
    }
}
