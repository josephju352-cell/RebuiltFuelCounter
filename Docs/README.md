# Rebuilt Fuel Counter (FRC 2026)

_**Mercer Island Robotics - Team 5937**_

An open-source, high-performance fuel (yellow ball) counter designed for the FRC 2026 "Rebuilt" season. This application turns a low-cost Android device into a smart sensor for tracking game piece counts and scoring rates. Requires no additional sensors or wires, only a phone.

## Key Features

*   **Easy Setup:** Don't need to code, just install the APK, mount the phone, run the app and you're counting fuel.
*   **GPU-Accelerated Detection:** Uses custom shaders for all heavy vision processing. No dependency on bulky libraries like OpenCV.
*   **Low-Cost Hardware:** Optimized to run smoothly on ~$40 Android devices (Moto G Play, Galaxy A15, etc).
*   **Remote Monitoring:** Built-in web server and API for remote score tracking and management.
*   **mDNS Discovery:** Easily find the device on your network at `http://FuelCounter.local:8080`.
*   **Robust Tracking:** Handles bouncing balls that go out of frame and varying lighting conditions via background calibration.
*   **Diagnostic Tools:** Includes recording/playback for vision tuning, real-time performance metrics (CPU/GPU), and visual debug overlays.

## How Detection Works

The system is designed for maximum efficiency by offloading the majority of the "vision" work to the GPU:

1.  **Calibration:** The app captures a series of frames to build a static "Background Model."
2.  **Smart Shader:** A custom GPU shader compares the live feed against the background, specifically looking for "Yellow" objects that are moving or different from the calibrated scene.
3.  **Blob Processing:** The resulting mask is read back from the GPU. A fast CPU-based flood-fill algorithm identifies individual "blobs" (balls), calculating their position, area, and velocity.
4.  **Temporal Tracking:** A tracking layer associates blobs across frames. It counts a score only when a ball passes through a defined "mid-line" while moving in a downward trajectory. This effectively filters out "bouncing" balls that have already been counted.

### Accuracy Note
While accuracy is good, the vision system may miss points if two balls fall at the exact same time with one occluding the other (one in front of the camera relative to the other). In practice, this is uncommon for most intake/scoring setups, but vision-based detection is not be recommended for high-volume, concurrent shooters.

## Requirements

*   **Platform:** Android (Tested and Supported). iOS support is architected but currently untested.
*   **Hardware:** Any Android device with a functional camera and GPU (OpenGLES 3.0+).
*   **Development:** Unity 6000.0+ (recommended).

## Installing the APK (Sideloading)

You do **not** need to be a developer to install and use this application. You can "sideload" the pre-built APK onto any compatible Android device.

### For Non-Programmers (Easy Method)
1.  **Download/Transfer:** Copy the `RebuiltFuelCounter.apk` file from the `Builds/` folder to your Android device (via USB cable, Google Drive, or local network).
2.  **Enable Unknown Sources:** On your Android device, go to **Settings > Apps > Special app access > Install unknown apps**. Select the app you are using to open the file (e.g., "Files" or "Chrome") and toggle **Allow from this source**.
3.  **Install:** Locate the APK file on your device using a file manager app and tap it to begin the installation.

### For Developers (Command Line)
If you have the Android SDK platform-tools installed, you can install the APK directly via USB:
```bash
adb install Builds/RebuiltFuelCounter.apk
```

## Setup & Usage

1.  **Mounting:** Mount the device, centered on one side of the hub, with a clear view of the inside (drill a hole for the phone's camera), just below the hub funnel (see photos from 5937's field).
2.  **Calibration:** Ensure no balls are in the frame and tap **Calibrate**. The app will average several frames to "learn" the background.
3.  **Region of Interest (ROI):** Adjust the ROI and mid-line in the settings to match your specific scoring geometry. The ROI can be resized by dragging its top or bottom edges directly on the video display. Changes are saved automatically.
4.  **Sensitivity:** Fine-tune the Brightness Threshold sensitivity slider in the Settings screen while watching the "Detector" view.
5.  **Remote Access:** Connect to the device's IP (or `FuelCounter.local`) on port `8080` to view the web dashboard. Make sure to use http, not https, since it's a LAN (local) only, insecure connection.

## Advanced Tuning

For deep detector optimization, several parameters can be adjusted via the **FuelDetectorManager** object in the Unity Inspector. These settings are not available in the runtime UI and require a re-build:

*   **Midline (0-1):** Sets the vertical position of the scoring threshold within the Region of Interest. Balls must cross this line in a downward direction to count.
*   **Min Blob Area:** The minimum pixel size required for an object to be recognized. Increase this to filter out background noise or small debris.
*   **Max Match Distance:** Controls how far the tracker "looks" for a ball between frames. Higher values help with very fast-moving balls but may cause confusion if balls are tightly clustered.
*   **Yellow Sensitivity:** Adjusts the color-matching threshold for the fuel's specific yellow hue.
*   **Motion Sensitivity:** Determines how aggressively the shader ignores static background pixels in favor of moving ones.

## Contributing

This is an open-source project for the FRC community. Contributions in the form of bug reports, feature requests, or pull requests are welcome.

## License

This project is released under the [MIT License](LICENSE).


image_1950.png
FrameAnalysis2.png
FrameAnalysis1.png