using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using Unity.Collections;
using Shapes;

namespace FuelDetector
{
    public class FuelDetectorManager : ImmediateModeShapeDrawer
    {
        [Header("Detection Settings")]
        public Rect regionOfInterest = new Rect(0, 0, 1, 1);
        public bool useDownscaling = true;
        public Vector2Int downscaleResolution = new Vector2Int(256, 144);

        private const string PREF_YELLOW_SENS = "FuelDetector_YellowSensitivity";
        private const string PREF_MOTION_SENS = "FuelDetector_MotionSensitivity";
        private const string PREF_BRIGHTNESS_THRESH = "FuelDetector_BrightnessThreshold";

        [Range(0, 1)] [SerializeField] private float _yellowSensitivity = 0.8f;
        public float yellowSensitivity
        {
            get => _yellowSensitivity;
            set { _yellowSensitivity = value; PlayerPrefs.SetFloat(PREF_YELLOW_SENS, value); PlayerPrefs.Save(); }
        }

        [Range(0, 1)] [SerializeField] private float _motionSensitivity = 0.3f;
        public float motionSensitivity
        {
            get => _motionSensitivity;
            set { _motionSensitivity = value; PlayerPrefs.SetFloat(PREF_MOTION_SENS, value); PlayerPrefs.Save(); }
        }

        [Range(0, 1)] [SerializeField] private float _brightnessThreshold = 0.7f;
        public float brightnessThreshold
        {
            get => _brightnessThreshold;
            set { _brightnessThreshold = value; PlayerPrefs.SetFloat(PREF_BRIGHTNESS_THRESH, value); PlayerPrefs.Save(); }
        }
        [Tooltip("Mid-line position relative to the ROI (0 = Bottom, 1 = Top)")]
        [Range(0, 1)] public float midline = 0.55f;
        public float minBlobArea = 0.03f;
        public float maxMatchDistance = 0.5f;
        public bool showDebugShapes = true;

        [Header("References")]
        [SerializeField] private VideoInputManager videoInputManager;
        [SerializeField] private FuelCounter fuelCounter;
        [SerializeField] private RawImage videoDisplay;
        [SerializeField] private GameObject calibrationIndicator;

        [Header("Shaders")]
        [SerializeField] private Shader backgroundAccumShader;
        [SerializeField] private Shader yellowDetectorShader;

        private Material _accumMaterial;
        private Material _detectorMaterial;
        private RenderTexture _backgroundRT;
        private RenderTexture _detectorRT;
        private RenderTexture _downscaleRT;

        // Synchronized Previews
        private RenderTexture _syncedVideoRT;
        private RenderTexture _syncedDetectorRT;
        private RenderTexture _pendingVideoRT;
        private bool _isReadbackPending = false;
        private bool _isRefreshOnly = false;

        public Texture SyncedVideo => _syncedVideoRT;
        public Texture SyncedDetector => _syncedDetectorRT;
        public Texture Background => _backgroundRT;

        private bool _isCalibrating = false;
        private int _calibrationFrames = 0;
        private const int MAX_CALIBRATION_FRAMES = 60;

        private BlobProcessor _blobProcessor = new BlobProcessor();
        private FuelTracker _fuelTracker = new FuelTracker();
        
        public int ActiveTrackCount => _fuelTracker.TrackedItems.Count;

        private readonly Color[] _debugColors = new Color[] {
            Color.cyan, Color.magenta, Color.green, Color.white,
            new(1f, 0.6f, 0.2f), // light orange
            new(0.7f, 0.3f, 0.1f), // violet
        };

        private Vector3[] _corners = new Vector3[4];

        private void Awake()
        {
            if (videoInputManager)
            {
                videoInputManager.onCameraChanged.AddListener(TriggerCalibration);
            }
        }

        private void Start()
        {
            Application.targetFrameRate = 60;

            _yellowSensitivity = PlayerPrefs.GetFloat(PREF_YELLOW_SENS, _yellowSensitivity);
            _motionSensitivity = PlayerPrefs.GetFloat(PREF_MOTION_SENS, _motionSensitivity);
            _brightnessThreshold = PlayerPrefs.GetFloat(PREF_BRIGHTNESS_THRESH, _brightnessThreshold);

            if (!backgroundAccumShader) backgroundAccumShader = Shader.Find("Hidden/FuelDetector/BackgroundAccumulator");
            if (!yellowDetectorShader) yellowDetectorShader = Shader.Find("Hidden/FuelDetector/SmartYellowDetector");

            _accumMaterial = new Material(backgroundAccumShader);
            _detectorMaterial = new Material(yellowDetectorShader);

            if (fuelCounter)
            {
                fuelCounter.useExternalCounter = true;
            }

            // Only hide if we aren't already calibrating (e.g. triggered by videoInputManager in its Start)
            if (calibrationIndicator && !_isCalibrating) calibrationIndicator.SetActive(false);

            // Trigger initial calibration if camera is already running
            if (videoInputManager && videoInputManager.IsPlaying)
            {
                TriggerCalibration();
            }
        }

        public override void OnEnable()
        {
            base.OnEnable();
            if (videoInputManager) videoInputManager.onFrameAvailable += OnFrameAvailable;
        }

        public override void OnDisable()
        {
            base.OnDisable();
            if (videoInputManager) videoInputManager.onFrameAvailable -= OnFrameAvailable;
        }

        private void OnFrameAvailable(RenderTexture source)
        {
            ProcessFrame(source);
        }

        public void RefreshDetector()
        {
            if (!videoInputManager || !videoInputManager.OutputRenderTexture) return;
            if (_isReadbackPending) return;

            _isRefreshOnly = true;
            ProcessFrame(videoInputManager.OutputRenderTexture);
        }

        private void ProcessFrame(Texture source)
        {
            int width = useDownscaling ? downscaleResolution.x : source.width;
            int height = useDownscaling ? downscaleResolution.y : source.height;

            EnsureTextures(width, height);

            // 1. Downscale
            if (useDownscaling)
            {
                Graphics.Blit(source, _downscaleRT);
                source = _downscaleRT;
            }

            // 2. Accumulate Background
            if (_isCalibrating)
            {
                if (_calibrationFrames == 0)
                {
                    // First frame: direct copy to initialize color and alpha (1.0)
                    // Use material with weight 1.0 to ensure consistent color space handling
                    _accumMaterial.SetFloat("_Blend", 1.0f);
                    Graphics.Blit(source, _backgroundRT, _accumMaterial);
                }
                else
                {
                    // Subsequent frames: Iterative Mean (1/N weight)
                    float weight = 1.0f / (_calibrationFrames + 1);
                    _accumMaterial.SetFloat("_Blend", weight);
                    Graphics.Blit(source, _backgroundRT, _accumMaterial);
                }

                _calibrationFrames++;

                if (_calibrationFrames >= MAX_CALIBRATION_FRAMES)
                {
                    _isCalibrating = false;
                    if (calibrationIndicator != null) calibrationIndicator.SetActive(false);
                    Debug.Log($"[FuelDetector] Calibration Complete at frame {_calibrationFrames} (Time: {Time.time:F2}s)");
                }
            }

            // 4. Readback & Process (Only if not already pending)
            if (!_isReadbackPending)
            {
                _isReadbackPending = true;

                // 3. Detect
                _detectorMaterial.SetTexture("_BackTex", _backgroundRT);
                _detectorMaterial.SetFloat("_YellowSens", yellowSensitivity);
                _detectorMaterial.SetFloat("_MotionSens", motionSensitivity);
                _detectorMaterial.SetFloat("_MinBrightness", brightnessThreshold);
                _detectorMaterial.SetVector("_ROI", new Vector4(regionOfInterest.xMin, regionOfInterest.yMin, regionOfInterest.xMax, regionOfInterest.yMax));

                Graphics.Blit(source, _detectorRT, _detectorMaterial);

                // Capture state for synchronization
                Graphics.CopyTexture(source, _pendingVideoRT);

                AsyncGPUReadback.Request(_detectorRT, 0, TextureFormat.R8, OnReadbackComplete);
            }
        }

        private void OnReadbackComplete(AsyncGPUReadbackRequest request)
        {
            if (request.hasError || !this) 
            {
                _isReadbackPending = false;
                _isRefreshOnly = false;
                return;
            }

            // Move pending to synced
            Graphics.CopyTexture(_pendingVideoRT, _syncedVideoRT);
            Graphics.CopyTexture(_detectorRT, _syncedDetectorRT);

            if (_isRefreshOnly)
            {
                _isReadbackPending = false;
                _isRefreshOnly = false;
                return;
            }

            var data = request.GetData<byte>();
            int width = request.width;
            int height = request.height;
            float sourceAspect = videoInputManager ? videoInputManager.SourceAspectRatio : (float)width / height;

            var blobs = _blobProcessor.Process(data, width, height, sourceAspect, minBlobArea);
            
            _fuelTracker.MaxMatchDistance = maxMatchDistance;
            float midlineY = regionOfInterest.yMin + regionOfInterest.height * midline;
            int scoringCount = _fuelTracker.UpdateTracks(blobs, midlineY);

            if (scoringCount > 0 && fuelCounter)
            {
                fuelCounter.IncrementCount(scoringCount);
            }

            _isReadbackPending = false;
        }

        private void EnsureTextures(int width, int height)
        {
            // Use UNORM for everything in Gamma mode, or SRGB if in Linear mode for color textures
            GraphicsFormat colorFormat = QualitySettings.activeColorSpace == ColorSpace.Linear 
                ? GraphicsFormat.R8G8B8A8_SRGB 
                : GraphicsFormat.R8G8B8A8_UNorm;
            
            GraphicsFormat maskFormat = GraphicsFormat.R8_UNorm;

            void CheckRT(ref RenderTexture rt, int w, int h, GraphicsFormat format, string name)
            {
                if (!rt || rt.width != w || rt.height != h || rt.graphicsFormat != format)
                {
                    if (rt) rt.Release();
                    RenderTextureDescriptor desc = new RenderTextureDescriptor(w, h, format, 0);
                    rt = new RenderTexture(desc);
                    rt.name = name;
                }
            }

            CheckRT(ref _downscaleRT, width, height, colorFormat, "Downscale");
            CheckRT(ref _detectorRT, width, height, maskFormat, "Detector Mask");
            CheckRT(ref _pendingVideoRT, width, height, colorFormat, "Pending Video");
            CheckRT(ref _syncedVideoRT, width, height, colorFormat, "Synced Video");
            CheckRT(ref _syncedDetectorRT, width, height, maskFormat, "Synced Detector");

            if (!_backgroundRT || _backgroundRT.width != width || _backgroundRT.height != height || _backgroundRT.graphicsFormat != colorFormat)
            {
                if (_backgroundRT) _backgroundRT.Release();
                RenderTextureDescriptor desc = new RenderTextureDescriptor(width, height, colorFormat, 0);
                _backgroundRT = new RenderTexture(desc);
                _backgroundRT.name = "Static Background Capture";
                Graphics.Blit(Texture2D.blackTexture, _backgroundRT);
                
                // If we are calibrating, we must reset the frame count because 
                // we just cleared the background we were accumulating into!
                if (_isCalibrating) _calibrationFrames = 0;
            }
        }

        public void TriggerCalibration()
        {
            // Avoid redundant resets if already calibrating and just started.
            // This prevents double-triggers from Start() and onCameraChanged events.
            if (_isCalibrating && _calibrationFrames < 10)
            {
                Debug.Log($"[FuelDetector] Calibration already in progress (frame {_calibrationFrames}), skipping reset.");
                return;
            }

            _isCalibrating = true;
            _calibrationFrames = 0;
            if (calibrationIndicator) calibrationIndicator.SetActive(true);
            
            if (videoInputManager)
            {
                videoInputManager.EnsurePlaying();
            }

            Debug.Log("[FuelDetector] Calibration Triggered");
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (Application.isPlaying && videoInputManager && videoInputManager.IsPaused)
            {
                RefreshDetector();
            }
        }
#endif

        public override void DrawShapes(Camera cam)
        {
            if (!showDebugShapes || !videoDisplay || !videoDisplay.gameObject.activeInHierarchy) return;

            using (Draw.Command(cam))
            {
                RectTransform rt = videoDisplay.rectTransform;
                rt.GetWorldCorners(_corners);
                float sourceAspect = videoInputManager ? videoInputManager.SourceAspectRatio : 1f;

                // Helper to map Aspect-Corrected Space (X: 0..sourceAspect, Y: 0..1) to World Position
                // _corners is clockwise from bottom-left.
                Vector3 AspectSpaceToWorld(Vector2 pos)
                {
                    float tx = pos.x / sourceAspect;
                    float ty = pos.y;
                    Vector3 bottom = Vector3.LerpUnclamped(_corners[0], _corners[3], tx);
                    Vector3 top = Vector3.LerpUnclamped(_corners[1], _corners[2], tx);
                    return Vector3.LerpUnclamped(bottom, top, ty);
                }

                Draw.LineGeometry = LineGeometry.Volumetric3D;
                Draw.ThicknessSpace = ThicknessSpace.Pixels;
                Draw.Thickness = 2;
                Draw.Matrix = Matrix4x4.identity; // Draw in World Space

                // 1. Draw ROI (ROI is in UV space, so we convert to Aspect Space)
                float roiXMin = regionOfInterest.xMin * sourceAspect;
                float roiXMax = regionOfInterest.xMax * sourceAspect;
                float roiYMin = regionOfInterest.yMin;
                float roiYMax = regionOfInterest.yMax;

                Vector3 p0 = AspectSpaceToWorld(new Vector2(roiXMin, roiYMin));
                Vector3 p1 = AspectSpaceToWorld(new Vector2(roiXMin, roiYMax));
                Vector3 p2 = AspectSpaceToWorld(new Vector2(roiXMax, roiYMax));
                Vector3 p3 = AspectSpaceToWorld(new Vector2(roiXMax, roiYMin));
                
                Draw.Line(p0, p1, Color.yellow);
                Draw.Line(p1, p2, Color.yellow);
                Draw.Line(p2, p3, Color.yellow);
                Draw.Line(p3, p0, Color.yellow);

                // 2. Draw Mid-line (Dashed)
                float midlineY = regionOfInterest.yMin + regionOfInterest.height * midline;
                Vector3 midLeft = AspectSpaceToWorld(new Vector2(roiXMin, midlineY));
                Vector3 midRight = AspectSpaceToWorld(new Vector2(roiXMax, midlineY));
                
                using(Draw.DashedScope(DashStyle.RelativeDashes(DashType.Basic, 5f, 5f)))
                {
                    Draw.Line(midLeft, midRight, new Color(1f, 1f, 0f, 0.25f));
                }

                // 3. Draw Tracked Objects
                foreach (var item in _fuelTracker.TrackedItems)
                {
                    Color col = _debugColors[item.ID % _debugColors.Length];
                    if (item.FramesSinceSeen > 0) col.a *= 0.4f;
                    Vector3 worldPos = AspectSpaceToWorld(item.Position);

                    // Area is already in UV space (0-1)
                    float radiusUV = Mathf.Sqrt(item.Area / Mathf.PI);
                    radiusUV = Mathf.Max(radiusUV, 0.02f);

                    // Convert UV radius to World units (Radius is essentially a Y distance in UV space)
                    Vector3 worldPosPlusRadius = AspectSpaceToWorld(item.Position + new Vector2(0, radiusUV));
                    float worldRadius = Vector3.Distance(worldPos, worldPosPlusRadius);

                    // Draw Blob
                    Draw.Ring(worldPos, worldRadius, 3f, col);
                    if (item.FramesSinceSeen > 0)
                        Draw.Ring(worldPos, worldRadius * 0.70f, 3f, col);

                    // Draw Velocity Vector
                    Vector3 worldTip = AspectSpaceToWorld(item.Position + item.Velocity);

                    Draw.Line(worldPos, worldTip, 3f, col);
                }
            }
        }

        private void OnDestroy()
        {
            if (_backgroundRT) _backgroundRT.Release();
            if (_detectorRT) _detectorRT.Release();
            if (_downscaleRT) _downscaleRT.Release();
            if (_pendingVideoRT) _pendingVideoRT.Release();
            if (_syncedVideoRT) _syncedVideoRT.Release();
            if (_syncedDetectorRT) _syncedDetectorRT.Release();
            if (_accumMaterial) Destroy(_accumMaterial);
            if (_detectorMaterial) Destroy(_detectorMaterial);
        }
    }
}
