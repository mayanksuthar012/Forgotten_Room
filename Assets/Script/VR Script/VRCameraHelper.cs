using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Ensures the VR Camera renders properly both in VR headsets and in the Unity Editor / standard display.
/// Automatically fixes the "Display 1: No cameras rendering" issue when running without an active VR headset.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(Camera))]
public class VRCameraHelper : MonoBehaviour
{
    private Camera cam;

    private void Awake()
    {
        cam = GetComponent<Camera>();
        UpdateTargetEye();
    }

    private void OnEnable()
    {
        cam = GetComponent<Camera>();
        UpdateTargetEye();
    }

    private void Update()
    {
        UpdateTargetEye();
    }

    private void UpdateTargetEye()
    {
        if (cam == null) cam = GetComponent<Camera>();
        if (cam == null) return;

        // When a VR headset is connected and active, render stereo to Both eyes.
        // When testing in the Unity Editor or standard display without an active VR headset,
        // render to None (Display 1) so Game view always renders properly.
        bool isVR = VRDeviceDetection.IsVRActive();
        StereoTargetEyeMask desired = isVR ? StereoTargetEyeMask.Both : StereoTargetEyeMask.None;

        if (cam.stereoTargetEye != desired)
        {
            cam.stereoTargetEye = desired;
        }
    }
}
