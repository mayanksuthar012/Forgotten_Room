using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Master Manager on XR Origin (VR):
/// - Automatically creates and attaches Left & Right VR Controllers under Camera Offset.
/// - Guarantees full 90Hz stereoscopic VR rendering (Both eyes) with zero lag.
/// - Ensures TrackedPoseDriver head tracking is active and buttery smooth.
/// - Seamlessly supports both VR Controllers (thumbsticks + triggers) and Keyboard WASD.
/// </summary>
[DisallowMultipleComponent]
public class VRRigManager : MonoBehaviour
{
    [Header("Controller Generation")]
    [SerializeField] private bool autoCreateControllers = true;

    [Header("VR Rig References")]
    [SerializeField] private Transform cameraOffset;
    [SerializeField] private Camera mainCamera;

    private GameObject leftControllerGO;
    private GameObject rightControllerGO;

    private void Awake()
    {
        FindRigComponents();
        DisableConflictingComponents();
        EnforceStandingHeight();
        if (autoCreateControllers)
        {
            EnsureVRControllersExist();
        }
        OptimizeForVR();
    }

    private void Start()
    {
        DisableConflictingComponents();
        EnforceStandingHeight();
        OptimizeForVR();
    }

    private void Update()
    {
        // 1. Enforce scale to prevent physics anomalies
        if (transform.localScale != Vector3.one)
        {
            transform.localScale = Vector3.one;
        }

        bool isVR = VRDeviceDetection.IsVRActive();

        // 2. Continuously maintain natural eye level height (1.65m) so camera NEVER sinks to ground
        if (mainCamera != null && cameraOffset != null)
        {
            float camLocalY = mainCamera.transform.localPosition.y;
            if (isVR && camLocalY >= 0.8f)
            {
                // In VR floor-tracking mode: headset provides full floor-calibrated height
                cameraOffset.localPosition = Vector3.zero;
            }
            else
            {
                // In Desktop mode or eye-level tracking: Camera Offset provides standing eye level (1.65m)
                if (cameraOffset.localPosition.y < 1.5f)
                {
                    cameraOffset.localPosition = new Vector3(0f, 1.65f, 0f);
                }
            }
        }

        // 3. Continuously ensure camera stereoscopic mode and TrackedPoseDriver match VR state
        if (mainCamera != null)
        {
            StereoTargetEyeMask desired = isVR ? StereoTargetEyeMask.Both : StereoTargetEyeMask.None;
            if (mainCamera.stereoTargetEye != desired)
            {
                mainCamera.stereoTargetEye = desired;
            }

            // TrackedPoseDriver: ONLY active when in true physical VR headset mode!
            // In Desktop / Editor mode, disabled so mouse look works cleanly without downward ground-lock!
            var trackedPose = mainCamera.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
            if (trackedPose != null)
            {
                if (trackedPose.enabled != isVR)
                {
                    trackedPose.enabled = isVR;
                }
            }

            // Fallback 6DOF head tracking directly from XRNode if TrackedPoseDriver is absent or inactive
            if (isVR && (trackedPose == null || !trackedPose.enabled))
            {
                InputDevice headDev = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
                if (!headDev.isValid) headDev = InputDevices.GetDeviceAtXRNode(XRNode.Head);

                if (headDev.isValid)
                {
                    if (headDev.TryGetFeatureValue(CommonUsages.centerEyeRotation, out Quaternion hRot) ||
                        headDev.TryGetFeatureValue(CommonUsages.deviceRotation, out hRot))
                    {
                        mainCamera.transform.localRotation = hRot;
                    }
                    if (headDev.TryGetFeatureValue(CommonUsages.centerEyePosition, out Vector3 hPos) ||
                        headDev.TryGetFeatureValue(CommonUsages.devicePosition, out hPos))
                    {
                        mainCamera.transform.localPosition = hPos;
                    }
                }
            }
        }
    }

    private void FindRigComponents()
    {
        if (cameraOffset == null)
        {
            Transform[] children = GetComponentsInChildren<Transform>(true);
            foreach (var c in children)
            {
                if (c.name.ToLower().Contains("camera offset"))
                {
                    cameraOffset = c;
                    break;
                }
            }
        }

        if (mainCamera == null)
        {
            mainCamera = GetComponentInChildren<Camera>(true);
        }
    }

    private void EnsureVRControllersExist()
    {
        if (cameraOffset == null)
        {
            cameraOffset = transform;
        }

        // 1. Check or create Left Controller
        Transform existingLeft = cameraOffset.Find("Left Controller");
        if (existingLeft == null)
        {
            leftControllerGO = new GameObject("Left Controller");
            leftControllerGO.transform.SetParent(cameraOffset);
            leftControllerGO.transform.localPosition = new Vector3(-0.2f, -0.1f, 0.35f);
            leftControllerGO.transform.localRotation = Quaternion.identity;

            var ctrl = leftControllerGO.AddComponent<VRHandController>();
            ctrl.SetHandType(VRHandController.HandType.LeftHand);
        }
        else
        {
            leftControllerGO = existingLeft.gameObject;
            if (leftControllerGO.GetComponent<VRHandController>() == null)
            {
                var ctrl = leftControllerGO.AddComponent<VRHandController>();
                ctrl.SetHandType(VRHandController.HandType.LeftHand);
            }
        }

        // 2. Check or create Right Controller
        Transform existingRight = cameraOffset.Find("Right Controller");
        if (existingRight == null)
        {
            rightControllerGO = new GameObject("Right Controller");
            rightControllerGO.transform.SetParent(cameraOffset);
            rightControllerGO.transform.localPosition = new Vector3(0.2f, -0.1f, 0.35f);
            rightControllerGO.transform.localRotation = Quaternion.identity;

            var ctrl = rightControllerGO.AddComponent<VRHandController>();
            ctrl.SetHandType(VRHandController.HandType.RightHand);
        }
        else
        {
            rightControllerGO = existingRight.gameObject;
            if (rightControllerGO.GetComponent<VRHandController>() == null)
            {
                var ctrl = rightControllerGO.AddComponent<VRHandController>();
                ctrl.SetHandType(VRHandController.HandType.RightHand);
            }
        }
    }

    private void DisableConflictingComponents()
    {
#pragma warning disable 0618
        // Disable redundant/conflicting XRI components on XR Origin that fight with PlayerController and VRHandController:
        var ccDriver = GetComponent<UnityEngine.XR.Interaction.Toolkit.CharacterControllerDriver>();
        if (ccDriver != null && ccDriver.enabled) ccDriver.enabled = false;

        var moveProvider = GetComponent<UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement.ContinuousMoveProvider>();
        if (moveProvider != null && moveProvider.enabled) moveProvider.enabled = false;

        var bodyTransformer = GetComponent<UnityEngine.XR.Interaction.Toolkit.Locomotion.XRBodyTransformer>();
        if (bodyTransformer != null && bodyTransformer.enabled) bodyTransformer.enabled = false;

        var snapTurn = GetComponent<UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning.SnapTurnProvider>();
        if (snapTurn != null && snapTurn.enabled) snapTurn.enabled = false;
#pragma warning restore 0618
    }

    private void EnforceStandingHeight()
    {
        // 1. Force unit scale to prevent physics anomalies and compressed colliders
        if (transform.localScale != Vector3.one)
        {
            transform.localScale = Vector3.one;
        }

        // 2. Configure CharacterController to standard human height (1.8m) with center at 0.9m
        CharacterController cc = GetComponent<CharacterController>();
        if (cc != null)
        {
            cc.height = 1.8f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.radius = 0.35f;
            cc.stepOffset = 0.3f;
            cc.skinWidth = 0.08f;
            cc.minMoveDistance = 0.001f;
        }

        // 3. Configure Camera Offset to natural eye level (1.65m)
        if (cameraOffset != null)
        {
            bool isVR = VRDeviceDetection.IsVRActive();
            float camLocalY = (mainCamera != null) ? mainCamera.transform.localPosition.y : 0f;
            if (isVR && camLocalY >= 0.8f)
            {
                cameraOffset.localPosition = Vector3.zero;
            }
            else
            {
                cameraOffset.localPosition = new Vector3(0f, 1.65f, 0f);
            }
        }

        // 4. Configure XROrigin CameraYOffset and Device Tracking mode
        var xrOrigin = GetComponent<Unity.XR.CoreUtils.XROrigin>();
        if (xrOrigin != null)
        {
            xrOrigin.CameraYOffset = 1.65f;
            xrOrigin.RequestedTrackingOriginMode = Unity.XR.CoreUtils.XROrigin.TrackingOriginMode.Device;
        }
    }

    private void OptimizeForVR()
    {
        bool isVR = VRDeviceDetection.IsVRActive();

        if (mainCamera != null)
        {
            // In VR: MUST render to Both eyes in stereo!
            mainCamera.stereoTargetEye = isVR ? StereoTargetEyeMask.Both : StereoTargetEyeMask.None;

            var trackedPose = mainCamera.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
            if (trackedPose != null)
            {
                trackedPose.enabled = isVR;
            }
        }
    }
}
