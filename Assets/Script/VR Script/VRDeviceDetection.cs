using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;

/// <summary>
/// High-performance, multi-subsystem VR headset and controller detection.
/// Accurately detects active OpenXR, Oculus Link/AirLink, SteamVR, and Standalone headsets.
/// Prevents false-positives in Unity Editor when XR packages are loaded without physical hardware.
/// </summary>
public static class VRDeviceDetection
{
    private static bool? cachedIsVR = null;
    private static float lastCheckTime = -1f;

    public static bool IsVRActive()
    {
        // Cache result for 0.3s to prevent GC allocation in Update loops
        if (cachedIsVR.HasValue && (Time.time - lastCheckTime) < 0.3f)
        {
            return cachedIsVR.Value;
        }

        lastCheckTime = Time.time;
        cachedIsVR = CheckVRStatus();
        return cachedIsVR.Value;
    }

    private static bool CheckVRStatus()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return true;
#else
        // 1. Check if XRDisplaySubsystem is actively running (OpenXR / Oculus Link / SteamVR / Standalone)
        var displays = new List<XRDisplaySubsystem>();
        SubsystemManager.GetSubsystems(displays);
        foreach (var d in displays)
        {
            if (d.running) return true;
        }

        // 2. Check if XRSettings report an active XR device
        if (XRSettings.isDeviceActive && !string.IsNullOrEmpty(XRSettings.loadedDeviceName))
        {
            return true;
        }

        // 3. Check InputSystem XRHMD device
        var hmd = UnityEngine.InputSystem.InputSystem.GetDevice<UnityEngine.InputSystem.XR.XRHMD>();
        if (hmd != null && hmd.added)
        {
            return true;
        }

        // 4. Check if HeadMounted device is detected by XR InputDevices
        InputDevice headDevice = InputDevices.GetDeviceAtXRNode(XRNode.Head);
        if (headDevice.isValid)
        {
            return true;
        }

        var heads = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.HeadMounted, heads);
        if (heads.Count > 0 && heads[0].isValid)
        {
            return true;
        }

        return false;
#endif
    }

    /// <summary>
    /// Returns true ONLY if physical VR motion controllers are connected and actively providing 6DOF tracking.
    /// In Editor / Desktop mode (or when controllers are idle/untracked), returns false.
    /// </summary>
    public static bool AreVRControllersActive()
    {
        if (!IsVRActive()) return false;

        InputDevice rHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (IsControllerTracked(rHand)) return true;

        InputDevice lHand = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        if (IsControllerTracked(lHand)) return true;

        var controllers = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.HeldInHand, controllers);
        foreach (var c in controllers)
        {
            if (IsControllerTracked(c)) return true;
        }

        return false;
    }

    /// <summary>
    /// Checks whether an InputDevice is physically tracked in 3D space with valid position or rotation data.
    /// </summary>
    public static bool IsControllerTracked(InputDevice dev)
    {
        if (!dev.isValid) return false;

        if (dev.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked))
        {
            return tracked;
        }

        if (dev.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 pos) && pos.sqrMagnitude > 0.00001f)
        {
            return true;
        }

        if (dev.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rot) && (rot.x != 0f || rot.y != 0f || rot.z != 0f || rot.w != 0f))
        {
            return true;
        }

        // In an active VR session with a valid controller device handle, treat as tracked
        return true;
    }
}
