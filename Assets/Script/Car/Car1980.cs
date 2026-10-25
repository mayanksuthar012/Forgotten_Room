using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR;

[RequireComponent(typeof(Rigidbody))]
public class Car1980 : MonoBehaviour, IInteractable
{
    public static Car1980 Instance { get; private set; }

    public enum DriveType
    {
        AllWheelDrive,
        RearWheelDrive,
        FrontWheelDrive
    }

    [Header("Car Specifications (1950s/1980s Classic)")]
    [SerializeField] private string carName = "1950s Classic Sedan";
    [SerializeField] private DriveType driveTrain = DriveType.AllWheelDrive;
    [Tooltip("Maximum cruising speed forward in m/s (~70 km/h)")]
    [SerializeField] private float maxForwardSpeed = 20f;
    [Tooltip("Maximum reverse speed in m/s (~28 km/h)")]
    [SerializeField] private float maxReverseSpeed = 8f;
    [Tooltip("Motor torque applied to wheels (Nm)")]
    [SerializeField] private float motorTorque = 1250f;
    [Tooltip("Foot brake torque (Nm)")]
    [SerializeField] private float brakeTorque = 2600f;
    [Tooltip("Handbrake torque (Nm)")]
    [SerializeField] private float handbrakeTorque = 4200f;
    [Tooltip("Coasting drag torque when releasing throttle")]
    [SerializeField] private float coastDragTorque = 140f;
    [Tooltip("Maximum steering angle for front wheels (degrees)")]
    [SerializeField] private float maxSteerAngle = 32f;
    [SerializeField] private float steerResponseSpeed = 8f;
    [SerializeField] private float downforce = 80f;
    [SerializeField] private Vector3 centerOfMassOffset = new Vector3(0f, -0.4f, 0.08f);

    [Header("Wheel Colliders (Uses Assigned WheelCollider Physics Settings Directly)")]
    [Tooltip("Front Left WheelCollider. Uses physics settings already configured in the Inspector!")]
    [SerializeField] private WheelCollider frontLeftCollider;
    [Tooltip("Front Right WheelCollider. Uses physics settings already configured in the Inspector!")]
    [SerializeField] private WheelCollider frontRightCollider;
    [Tooltip("Rear Left WheelCollider. Uses physics settings already configured in the Inspector!")]
    [SerializeField] private WheelCollider rearLeftCollider;
    [Tooltip("Rear Right WheelCollider. Uses physics settings already configured in the Inspector!")]
    [SerializeField] private WheelCollider rearRightCollider;
    [Tooltip("Optional anti-roll bar stabilizer force (set to 0 to disable)")]
    [SerializeField] private float antiRollBarForce = 3500f;

    [Header("Visual Wheel Meshes (Updated with GetWorldPose)")]
    [SerializeField] private Transform frontLeftWheel;
    [SerializeField] private Transform frontRightWheel;
    [SerializeField] private Transform rearLeftWheel;
    [SerializeField] private Transform rearRightWheel;
    [SerializeField] private Transform steeringWheelMesh;
    [SerializeField] private float maxSteeringWheelTurn = 160f;

    [Header("Camera Height & Position Settings (Customizable)")]
    [Tooltip("Height of the camera pivot above the car. Adjust this slider to view the car from higher or lower!")]
    [Range(0.5f, 6.0f)]
    [SerializeField] private float cameraHeight = 2.2f;

    [Tooltip("Distance of the camera behind the car in third-person chase view")]
    [Range(2.0f, 15.0f)]
    [SerializeField] private float cameraDistance = 5.2f;

    [Tooltip("Vertical height on the car that the camera aims / looks at")]
    [Range(0.0f, 4.0f)]
    [SerializeField] private float cameraLookTargetHeight = 1.0f;

    [Tooltip("First-person cockpit driver eye height adjustment")]
    [Range(-0.4f, 0.6f)]
    [SerializeField] private float cockpitEyeHeight = 0.0f;

    [Tooltip("Allow adjusting camera height live during gameplay with [Z]/[X], PageUp/PageDown, or Shift+Scroll")]
    [SerializeField] private bool allowInGameHeightAdjust = true;
    [SerializeField] private float heightAdjustSpeed = 2.0f;
    [SerializeField] private float minCameraHeight = 0.6f;
    [SerializeField] private float maxCameraHeight = 6.0f;

    [Header("Camera Orbit & Controls")]
    [SerializeField] private Camera carCamera;
    [SerializeField] private float cameraSmoothSpeed = 10f;
    [SerializeField] private float mouseSensitivity = 2.5f;
    [SerializeField] private float minPitch = -15f;
    [SerializeField] private float maxPitch = 65f;
    [SerializeField] private float minCockpitPitch = -30f;
    [SerializeField] private float maxCockpitPitch = 40f;
    [SerializeField] private float maxCockpitYaw = 85f;
    [SerializeField] private bool autoRecenter = true;
    [SerializeField] private float recenterDelay = 1.6f;
    [SerializeField] private float recenterSpeed = 3.0f;
    [SerializeField] private bool enableZoom = true;
    [SerializeField] private float minCameraDistance = 3.0f;
    [SerializeField] private float maxCameraDistance = 12.0f;
    [SerializeField] private float zoomSpeed = 2.5f;
    [SerializeField] private bool cameraWallCollision = true;
    [SerializeField] private LayerMask cameraCollisionMask = ~0;

    [Header("Control Mode & Testing")]
    [Tooltip("If true, the car is drivable immediately when you press Play in the Editor!")]
    [SerializeField] private bool driveImmediatelyOnStart = true;
    [SerializeField] private bool requireKeyToStart = false;
    [SerializeField] private bool hasKey = false;
    [SerializeField] private string lockedMessage = "[E] The ignition is locked - You need the Car Key!";
    [SerializeField] private string enterPrompt = "[E] Drive Classic Car";
    [SerializeField] private string exitPrompt = "[E] Exit Car";
    [SerializeField] private bool showHUD = true;

    [Header("Player Positions")]
    [SerializeField] private Transform exitPoint;
    [SerializeField] private Transform driverSeatPoint;

    [Header("Lights & Electrics")]
    [SerializeField] private Light[] headlights;
    [SerializeField] private Light[] taillights;
    [SerializeField] private bool headlightsOnAtStart = false;

    [Header("Audio (Optional)")]
    [SerializeField] private AudioSource engineAudio;
    [SerializeField] private AudioClip engineStartClip;
    [SerializeField] private AudioClip hornClip;
    [SerializeField] private float minAudioPitch = 0.75f;
    [SerializeField] private float maxAudioPitch = 2.1f;

    [Header("Escape / Story Event")]
    public UnityEvent OnCarEscaped;

    // Public State Accessors
    public bool IsDriving => isDriving;
    public bool HasKey => hasKey;
    public float CurrentSpeedKmH => currentSpeed * 3.6f;
    public float CameraHeight => cameraHeight;

    // Internal State
    private Rigidbody rb;
    private PlayerController activePlayer;
    private Camera playerCamera;
    private bool isDriving = false;
    private bool areHeadlightsOn = false;
    private float moveInput = 0f;
    private float steerInput = 0f;
    private bool isBraking = false;
    private float currentSteerAngle = 0f;
    private float currentSpeed = 0f;
    private bool isCockpitView = false;

    // Camera Orbit State
    private float orbitYaw = 0f;
    private float orbitPitch = 14f;
    private float currentCameraDistance = 5.2f;
    private float lastMouseMoveTime = 0f;
    private float cockpitYaw = 0f;
    private float cockpitPitch = 0f;

    // Steering wheel initial rotation
    private Quaternion steerWheelInitialRot;

    // Interface IInteractable
    public string interactionText
    {
        get
        {
            if (isDriving) return exitPrompt;
            if (requireKeyToStart && !hasKey) return lockedMessage;
            return enterPrompt;
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        rb = GetComponent<Rigidbody>();
        SetupRigidbody();
        AutoFindWheelMeshes();
        FindAssignedWheelColliders();
        DisableDuplicateColliderMeshRenderers();
        ApplyFrictionlessMaterialToChassisColliders();
        SetupCameraAndPoints();

        if (steeringWheelMesh != null)
            steerWheelInitialRot = steeringWheelMesh.localRotation;

        if (engineAudio == null)
            engineAudio = GetComponent<AudioSource>();

        SetHeadlights(headlightsOnAtStart);
    }

    private void Start()
    {
        currentCameraDistance = Mathf.Clamp(cameraDistance, minCameraDistance, maxCameraDistance);

        // Auto-enter driving mode immediately on start for instant testing!
        if (driveImmediatelyOnStart && !isDriving)
        {
            PlayerController pc = FindAnyObjectByType<PlayerController>();
            if (pc != null)
            {
                EnterCar(pc);
            }
            else
            {
                isDriving = true;
                if (carCamera != null)
                {
                    carCamera.gameObject.SetActive(true);
                }
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                StartEngine();
                Debug.Log("<color=cyan><b>[Car1980] 🚗 Physics-Based WheelCollider Driving Active! Use [W/S] Drive, [A/D] Steer, [Space] Brake, [Z/X or Shift+Scroll] Camera Height, [C] Cockpit View, [R] Reset Car.</b></color>");
            }
        }
    }

    private void SetupRigidbody()
    {
        rb.isKinematic = false;
        rb.useGravity = true;
        rb.constraints = RigidbodyConstraints.None;
        // Keep Inspector mass if already configured by user (> 50 kg), otherwise default to 1500 kg
        if (rb.mass < 50f) rb.mass = 1500f;
        rb.linearDamping = 0.05f;
        rb.angularDamping = 3.0f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.centerOfMass = centerOfMassOffset;
    }

    private void AutoFindWheelMeshes()
    {
        Transform[] allChildren = GetComponentsInChildren<Transform>();

        foreach (var t in allChildren)
        {
            string n = t.name.ToLower();
            if (frontLeftWheel == null && (n.Contains("flwheel") || n.Contains("wheel_fl") || n.Contains("front_left")))
                frontLeftWheel = t;
            else if (frontRightWheel == null && (n.Contains("frwheel") || n.Contains("wheel_fr") || n.Contains("front_right")))
                frontRightWheel = t;
            else if (rearLeftWheel == null && (n.Contains("rlwheel") || n.Contains("wheel_rl") || n.Contains("rear_left")))
                rearLeftWheel = t;
            else if (rearRightWheel == null && (n.Contains("rrwheel") || n.Contains("wheel_rr") || n.Contains("rear_right")))
                rearRightWheel = t;
            else if (steeringWheelMesh == null && (n.Contains("steeringwheel") || n.Contains("steering_wheel") || n.Contains("steer")))
                steeringWheelMesh = t;
        }
    }

    /// <summary>
    /// Uses the WheelColliders attached to the car wheel transforms.
    /// Does NOT override any physics properties (radius, suspension spring/damper, friction curves, mass, center)
    /// so the user's custom properties configured in the Inspector are fully respected!
    /// </summary>
    private void FindAssignedWheelColliders()
    {
        // 1. If assigned in Inspector, leave them completely as-is!
        // 2. If any are unassigned, safely search the children:
        if (frontLeftCollider == null && frontLeftWheel != null)
            frontLeftCollider = frontLeftWheel.GetComponent<WheelCollider>() ?? frontLeftWheel.GetComponentInChildren<WheelCollider>();
        if (frontRightCollider == null && frontRightWheel != null)
            frontRightCollider = frontRightWheel.GetComponent<WheelCollider>() ?? frontRightWheel.GetComponentInChildren<WheelCollider>();
        if (rearLeftCollider == null && rearLeftWheel != null)
            rearLeftCollider = rearLeftWheel.GetComponent<WheelCollider>() ?? rearLeftWheel.GetComponentInChildren<WheelCollider>();
        if (rearRightCollider == null && rearRightWheel != null)
            rearRightCollider = rearRightWheel.GetComponent<WheelCollider>() ?? rearRightWheel.GetComponentInChildren<WheelCollider>();

        // Fallback search across all child WheelColliders
        if (frontLeftCollider == null || frontRightCollider == null || rearLeftCollider == null || rearRightCollider == null)
        {
            WheelCollider[] allCols = GetComponentsInChildren<WheelCollider>();
            foreach (var wc in allCols)
            {
                string n = wc.name.ToLower();
                if (frontLeftCollider == null && (n.Contains("fl") || n.Contains("front_left") || n.Contains("frontleft")))
                    frontLeftCollider = wc;
                else if (frontRightCollider == null && (n.Contains("fr") || n.Contains("front_right") || n.Contains("frontright")))
                    frontRightCollider = wc;
                else if (rearLeftCollider == null && (n.Contains("rl") || n.Contains("rear_left") || n.Contains("rearleft")))
                    rearLeftCollider = wc;
                else if (rearRightCollider == null && (n.Contains("rr") || n.Contains("rear_right") || n.Contains("rearright")))
                    rearRightCollider = wc;
            }
        }
    }

    /// <summary>
    /// If the WheelCollider is on a duplicated wheel transform that still has a MeshRenderer,
    /// disable that MeshRenderer so it doesn't render frozen in place while the animated visual wheel spins.
    /// Does not alter any WheelCollider physics settings.
    /// </summary>
    private void DisableDuplicateColliderMeshRenderers()
    {
        DisableColliderMeshRendererIfSeparate(frontLeftCollider, frontLeftWheel);
        DisableColliderMeshRendererIfSeparate(frontRightCollider, frontRightWheel);
        DisableColliderMeshRendererIfSeparate(rearLeftCollider, rearLeftWheel);
        DisableColliderMeshRendererIfSeparate(rearRightCollider, rearRightWheel);
    }

    private void DisableColliderMeshRendererIfSeparate(WheelCollider col, Transform visualMesh)
    {
        if (col != null && visualMesh != null && col.transform != visualMesh)
        {
            MeshRenderer mr = col.GetComponent<MeshRenderer>();
            if (mr != null && mr.enabled)
            {
                mr.enabled = false;
            }
        }
    }

    /// <summary>
    /// Applies frictionless PhysicMaterial to chassis colliders so walls and bumps never stick.
    /// Does not touch WheelColliders.
    /// </summary>
    private void ApplyFrictionlessMaterialToChassisColliders()
    {
        PhysicsMaterial mat = new PhysicsMaterial("CarChassisFrictionless")
        {
            dynamicFriction = 0.05f,
            staticFriction = 0.05f,
            frictionCombine = PhysicsMaterialCombine.Minimum,
            bounciness = 0.0f
        };

        Collider[] carColliders = GetComponentsInChildren<Collider>();
        foreach (var col in carColliders)
        {
            if (col != null && !col.isTrigger && !(col is WheelCollider))
            {
                col.sharedMaterial = mat;
            }
        }
    }

    private void SetupCameraAndPoints()
    {
        if (exitPoint == null)
        {
            GameObject ep = new GameObject("CarExitPoint");
            ep.transform.SetParent(transform);
            ep.transform.localPosition = new Vector3(-1.85f, 0.1f, 0.2f);
            ep.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
            exitPoint = ep.transform;
        }

        if (driverSeatPoint == null)
        {
            GameObject dsp = new GameObject("DriverSeatPoint");
            dsp.transform.SetParent(transform);
            dsp.transform.localPosition = new Vector3(-0.42f, 0.78f, 0.15f);
            dsp.transform.localRotation = Quaternion.identity;
            driverSeatPoint = dsp.transform;
        }

        currentCameraDistance = Mathf.Clamp(cameraDistance, minCameraDistance, maxCameraDistance);

        // Safeguard: If carCamera was assigned to the Main Camera, do NOT hijack or disable it!
        if (carCamera != null && (carCamera.CompareTag("MainCamera") || carCamera == Camera.main))
        {
            carCamera = null;
        }

        if (carCamera == null)
        {
            GameObject camGo = new GameObject("CarFollowCamera");
            camGo.transform.SetParent(null);
            carCamera = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            carCamera.gameObject.SetActive(false);
        }
        else
        {
            if (carCamera.transform.parent != null)
            {
                carCamera.transform.SetParent(null);
            }
            carCamera.gameObject.SetActive(false);
        }
    }

    public void Interact()
    {
        if (isDriving)
        {
            ExitCar();
        }
        else
        {
            if (requireKeyToStart && !hasKey)
            {
                Debug.Log($"<color=orange>[Car1980] 🗝️ {lockedMessage}</color>");
                return;
            }

            PlayerController pc = FindAnyObjectByType<PlayerController>();
            if (pc != null)
            {
                EnterCar(pc);
            }
            else
            {
                isDriving = true;
                if (carCamera != null) carCamera.gameObject.SetActive(true);
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                StartEngine();
            }
        }
    }

    // Controller button states for edge detection
    private bool wasExitDown = false;
    private bool wasFlashDown = false;
    private bool wasCamChangeDown = false;
    private bool wasHornDown = false;
    private bool wasResetDown = false;
    private Coroutine flashRoutine = null;

    public void EnterCar(PlayerController player)
    {
        if (player == null) return;

        activePlayer = player;
        isDriving = true;

        if (Camera.main != null && Camera.main != carCamera)
            playerCamera = Camera.main;

        activePlayer.SetControlActive(false);

        // Disable CharacterController physics while driving so player collider doesn't push the car
        CharacterController cc = activePlayer.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        // In VR: Keep XR Origin active and parent to car so 6DOF head and hand tracking continues!
        if (VRDeviceDetection.IsVRActive())
        {
            if (driverSeatPoint != null)
            {
                activePlayer.transform.SetParent(transform);
                activePlayer.transform.localPosition = driverSeatPoint.localPosition;
                activePlayer.transform.localRotation = Quaternion.identity;
            }
        }
        else
        {
            activePlayer.gameObject.SetActive(false);
            if (carCamera != null)
            {
                carCamera.gameObject.SetActive(true);
                Vector3 initCamPos = transform.position + Vector3.up * cameraHeight - (transform.forward * currentCameraDistance);
                carCamera.transform.position = initCamPos;
                carCamera.transform.LookAt(transform.position + Vector3.up * cameraLookTargetHeight);
            }
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        orbitYaw = 0f;
        orbitPitch = 14f;
        cockpitYaw = 0f;
        cockpitPitch = 0f;
        currentCameraDistance = Mathf.Clamp(cameraDistance, minCameraDistance, maxCameraDistance);
        lastMouseMoveTime = Time.time;

        wasExitDown = true;
        wasFlashDown = false;
        wasCamChangeDown = false;
        wasHornDown = false;
        wasResetDown = false;

        StartEngine();
        Debug.Log("<color=green><b>[Car1980] 🚗 Entered car! Controller: [A] Camera View | [X] Flash Lights | [B/Y/Grip] Exit | Triggers/Sticks: Drive</b></color>");
    }

    public void ExitCar()
    {
        if (!isDriving) return;

        isDriving = false;

        if (flashRoutine != null)
        {
            StopCoroutine(flashRoutine);
            flashRoutine = null;
        }
        wasExitDown = false;
        wasFlashDown = false;
        wasCamChangeDown = false;
        wasHornDown = false;
        wasResetDown = false;

        if (activePlayer != null)
        {
            if (!activePlayer.gameObject.activeSelf)
                activePlayer.gameObject.SetActive(true);

            activePlayer.transform.SetParent(null);
            activePlayer.transform.localScale = Vector3.one;

            if (exitPoint != null)
            {
                activePlayer.transform.position = exitPoint.position;
                activePlayer.transform.rotation = exitPoint.rotation;
            }

            CharacterController cc = activePlayer.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = true;

            activePlayer.SetControlActive(true);
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        if (carCamera != null)
        {
            carCamera.gameObject.SetActive(false);
        }

        StopEngine();
        ApplyParkingBrakes();

        Debug.Log("<color=yellow><b>[Car1980] 🚪 Player exited the car.</b></color>");
    }

    private void Update()
    {
        // Toggle driving mode with [E] or [Enter]
        if (!isDriving && (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Return)))
        {
            PlayerController pc = FindAnyObjectByType<PlayerController>();
            if (pc != null)
            {
                float dist = Vector3.Distance(pc.transform.position, transform.position);
                if (dist < 4.5f) EnterCar(pc);
            }
            else
            {
                isDriving = true;
                if (carCamera != null) carCamera.gameObject.SetActive(true);
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                StartEngine();
            }
        }

        if (isDriving)
        {
            HandleDrivingInput();
            HandleCameraInput();
            HandleControllerInputs();
            UpdateCamera();
        }

        // Synchronize 3D wheel meshes to exact WheelCollider world poses every frame
        UpdateWheelVisuals();
        UpdateAudio();
    }

    private void HandleDrivingInput()
    {
        // 1. Throttle input from Keyboard
        float vAxis = Input.GetAxis("Vertical");
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
            vAxis = Mathf.Max(vAxis, 1f);
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))
            vAxis = Mathf.Min(vAxis, -1f);

        // 2. Steering input from Keyboard
        float hAxis = Input.GetAxis("Horizontal");
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
            hAxis = Mathf.Max(hAxis, 1f);
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))
            hAxis = Mathf.Min(hAxis, -1f);

        // 3. Handbrake
        bool handbrake = Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.JoystickButton5); // Space or Gamepad RB

        // 4. VR Motion Controllers / Gamepad Driving Inputs
        InputDevice leftDev = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        InputDevice rightDev = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

        // Steering via Thumbstick
        if (leftDev.isValid && leftDev.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 lStick) && Mathf.Abs(lStick.x) > 0.08f)
        {
            hAxis = lStick.x;
            if (Mathf.Abs(lStick.y) > 0.12f) vAxis = lStick.y;
        }
        else if (rightDev.isValid && rightDev.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 rStick) && Mathf.Abs(rStick.x) > 0.08f)
        {
            hAxis = rStick.x;
        }

        // Throttle & Brake via VR Triggers (Right Trigger = Accelerate, Left Trigger = Brake / Reverse)
        if (rightDev.isValid && rightDev.TryGetFeatureValue(CommonUsages.trigger, out float rTrigger) && rTrigger > 0.05f)
        {
            vAxis = Mathf.Max(vAxis, rTrigger);
        }
        if (leftDev.isValid && leftDev.TryGetFeatureValue(CommonUsages.trigger, out float lTrigger) && lTrigger > 0.05f)
        {
            if (currentSpeed > 0.5f)
            {
                handbrake = true;
            }
            else
            {
                vAxis = Mathf.Min(vAxis, -lTrigger);
            }
        }

        moveInput = vAxis;
        steerInput = hAxis;
        isBraking = handbrake;

        // Smooth steering angle
        float targetAngle = steerInput * maxSteerAngle;
        currentSteerAngle = Mathf.MoveTowards(currentSteerAngle, targetAngle, steerResponseSpeed * maxSteerAngle * Time.deltaTime);
    }

    private void HandleCameraInput()
    {
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        if (Mathf.Abs(mouseX) > 0.005f || Mathf.Abs(mouseY) > 0.005f)
        {
            lastMouseMoveTime = Time.time;

            if (isCockpitView)
            {
                cockpitYaw = Mathf.Clamp(cockpitYaw + mouseX, -maxCockpitYaw, maxCockpitYaw);
                cockpitPitch = Mathf.Clamp(cockpitPitch - mouseY, minCockpitPitch, maxCockpitPitch);
            }
            else
            {
                orbitYaw += mouseX;
                orbitPitch = Mathf.Clamp(orbitPitch - mouseY, minPitch, maxPitch);
            }
        }

        // Camera Height Live In-Game Adjustments
        if (allowInGameHeightAdjust && !isCockpitView)
        {
            // Method 1: [X] or [PageUp] raises camera, [Z] or [PageDown] lowers camera
            if (Input.GetKey(KeyCode.X) || Input.GetKey(KeyCode.PageUp))
            {
                cameraHeight = Mathf.Clamp(cameraHeight + heightAdjustSpeed * Time.deltaTime, minCameraHeight, maxCameraHeight);
            }
            if (Input.GetKey(KeyCode.Z) || Input.GetKey(KeyCode.PageDown))
            {
                cameraHeight = Mathf.Clamp(cameraHeight - heightAdjustSpeed * Time.deltaTime, minCameraHeight, maxCameraHeight);
            }

            // Method 2: Shift + Mouse ScrollWheel raises or lowers camera height
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            {
                float scroll = Input.GetAxis("Mouse ScrollWheel");
                if (Mathf.Abs(scroll) > 0.005f)
                {
                    cameraHeight = Mathf.Clamp(cameraHeight + scroll * heightAdjustSpeed * 2.5f, minCameraHeight, maxCameraHeight);
                }
            }
            else if (enableZoom)
            {
                // Regular scroll zooms distance
                float scroll = Input.GetAxis("Mouse ScrollWheel");
                if (Mathf.Abs(scroll) > 0.005f)
                {
                    currentCameraDistance = Mathf.Clamp(currentCameraDistance - scroll * zoomSpeed * 4f, minCameraDistance, maxCameraDistance);
                }
            }
        }
        else if (enableZoom && !isCockpitView)
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.005f)
            {
                currentCameraDistance = Mathf.Clamp(currentCameraDistance - scroll * zoomSpeed * 4f, minCameraDistance, maxCameraDistance);
            }
        }

        // Look-behind
        if (Input.GetKey(KeyCode.B) || Input.GetMouseButton(2))
        {
            orbitYaw = Mathf.LerpAngle(orbitYaw, 180f, Time.deltaTime * 12f);
        }
    }

    /// <summary>
    /// Processes all controller button inputs for:
    /// - Car Exit: VR B button, Y button, or Grip; Gamepad B/Y; Keyboard [E]/[Esc]
    /// - Flash / Headlights: VR X button, Left stick click; Gamepad X/LB; Keyboard [F]/[L]
    /// - Camera Change: VR A button, Right stick click; Gamepad A/R3/View; Keyboard [C]/[V]/[Tab]
    /// </summary>
    private void HandleControllerInputs()
    {
        bool isExitDown = false;
        bool isFlashDown = false;
        bool isCamChangeDown = false;
        bool isHornDown = false;
        bool isResetDown = false;

        // 1. Read VR Motion Controllers (OpenXR / Oculus / SteamVR)
        InputDevice rightDev = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        InputDevice leftDev = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);

        // --- CAR EXIT BUTTON ---
        // VR: B button (Right secondary), Y button (Left secondary), or Grip on either controller
        if (rightDev.isValid)
        {
            if (rightDev.TryGetFeatureValue(CommonUsages.secondaryButton, out bool rSec) && rSec) isExitDown = true;
            if (rightDev.TryGetFeatureValue(CommonUsages.gripButton, out bool rGrip) && rGrip) isExitDown = true;
            if (rightDev.TryGetFeatureValue(CommonUsages.grip, out float rGripVal) && rGripVal > 0.6f) isExitDown = true;
        }
        if (leftDev.isValid)
        {
            if (leftDev.TryGetFeatureValue(CommonUsages.secondaryButton, out bool lSec) && lSec) isExitDown = true;
            if (leftDev.TryGetFeatureValue(CommonUsages.gripButton, out bool lGrip) && lGrip) isExitDown = true;
            if (leftDev.TryGetFeatureValue(CommonUsages.grip, out float lGripVal) && lGripVal > 0.6f) isExitDown = true;
        }

        // --- FLASH / HEADLIGHTS BUTTON ---
        // VR: X button (Left primary) or Left thumbstick click
        if (leftDev.isValid)
        {
            if (leftDev.TryGetFeatureValue(CommonUsages.primaryButton, out bool lPrim) && lPrim) isFlashDown = true;
            if (leftDev.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out bool lClick) && lClick) isFlashDown = true;
        }

        // --- CAMERA CHANGE BUTTON ---
        // VR: A button (Right primary) or Right thumbstick click
        if (rightDev.isValid)
        {
            if (rightDev.TryGetFeatureValue(CommonUsages.primaryButton, out bool rPrim) && rPrim) isCamChangeDown = true;
            if (rightDev.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out bool rClick) && rClick) isCamChangeDown = true;
        }

        // 2. Read Gamepad / Joystick Controller (Xbox / PlayStation)
        // Exit: B button (JoystickButton1) or Y button (JoystickButton3)
        if (Input.GetKey(KeyCode.JoystickButton1) || Input.GetKey(KeyCode.JoystickButton3))
            isExitDown = true;

        // Flash / Headlights: X button (JoystickButton2) or LB (JoystickButton4)
        if (Input.GetKey(KeyCode.JoystickButton2) || Input.GetKey(KeyCode.JoystickButton4))
            isFlashDown = true;

        // Camera Change: A button (JoystickButton0) or R3 (JoystickButton9) or View (JoystickButton6)
        if (Input.GetKey(KeyCode.JoystickButton0) || Input.GetKey(KeyCode.JoystickButton9) || Input.GetKey(KeyCode.JoystickButton6))
            isCamChangeDown = true;

        // Horn: L3 (JoystickButton8)
        if (Input.GetKey(KeyCode.JoystickButton8))
            isHornDown = true;

        // 3. Read Keyboard Hotkeys
        if (Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.Escape))
            isExitDown = true;
        if (Input.GetKey(KeyCode.F) || Input.GetKey(KeyCode.L))
            isFlashDown = true;
        if (Input.GetKey(KeyCode.C) || Input.GetKey(KeyCode.V) || Input.GetKey(KeyCode.Tab))
            isCamChangeDown = true;
        if (Input.GetKey(KeyCode.H))
            isHornDown = true;
        if (Input.GetKey(KeyCode.R))
            isResetDown = true;

        // --- EXECUTE ACTIONS ON BUTTON DOWN (RISING EDGE) ---

        // Action 1: CAR EXIT
        if (isExitDown && !wasExitDown)
        {
            Debug.Log("<color=yellow>[Car1980] 🚪 Controller button pressed: Exiting car...</color>");
            ExitCar();
            return;
        }
        wasExitDown = isExitDown;

        // Action 2: FLASH / HEADLIGHTS TOGGLE & FLASH
        if (isFlashDown && !wasFlashDown)
        {
            FlashHeadlights();
            Debug.Log($"<color=yellow>[Car1980] 💡 Controller button pressed: Headlights Flashed / Toggled ({(areHeadlightsOn ? "ON" : "OFF")})</color>");
        }
        wasFlashDown = isFlashDown;

        // Action 3: CAMERA CHANGE (COCKPIT <-> CHASE)
        if (isCamChangeDown && !wasCamChangeDown)
        {
            ToggleCameraView();
            Debug.Log($"<color=cyan>[Car1980] 🎥 Controller button pressed: Camera switched to {(isCockpitView ? "Cockpit (Interior)" : "Chase Orbit")}</color>");
        }
        wasCamChangeDown = isCamChangeDown;

        // Action 4: HORN
        if (isHornDown && !wasHornDown)
        {
            PlayHorn();
        }
        wasHornDown = isHornDown;

        // Action 5: RESET CAR
        if (isResetDown && !wasResetDown)
        {
            ResetCarUpright();
        }
        wasResetDown = isResetDown;
    }

    private void FixedUpdate()
    {
        currentSpeed = Vector3.Dot(rb.linearVelocity, transform.forward);

        if (isDriving)
        {
            ApplyWheelColliderForces();
            ApplyAntiRollBars();
            ApplyDownforce();
        }
        else
        {
            ApplyParkingBrakes();
        }
    }

    /// <summary>
    /// Pure physics-based driving via the attached Unity WheelColliders!
    /// Applies real motor torque, foot brakes, handbrake, and steering angles using the WheelCollider settings.
    /// </summary>
    private void ApplyWheelColliderForces()
    {
        if (frontLeftCollider == null || frontRightCollider == null || rearLeftCollider == null || rearRightCollider == null)
            return;

        float fwdSpeed = currentSpeed;
        float appliedMotorTorque = 0f;
        float appliedFootBrake = 0f;

        // 1. Foot Brake vs Reverse Logic (Driver Pedal)
        if (moveInput > 0.05f)
        {
            // Moving or accelerating forward
            if (fwdSpeed < -0.5f)
            {
                // Car is rolling backward, apply foot brake to stop reverse roll first
                appliedFootBrake = brakeTorque;
            }
            else
            {
                // Forward Drive
                float speedLimitRatio = Mathf.Clamp01(1f - (fwdSpeed / maxForwardSpeed));
                appliedMotorTorque = moveInput * motorTorque * speedLimitRatio;
            }
        }
        else if (moveInput < -0.05f)
        {
            // Player wants to reverse or brake
            if (fwdSpeed > 0.5f)
            {
                // Car is moving forward, apply foot brake to stop!
                appliedFootBrake = brakeTorque;
            }
            else
            {
                // Reverse Drive
                float speedLimitRatio = Mathf.Clamp01(1f - (Mathf.Abs(fwdSpeed) / maxReverseSpeed));
                appliedMotorTorque = moveInput * (motorTorque * 0.75f) * speedLimitRatio;
            }
        }
        else
        {
            // Coasting natural engine drag
            appliedFootBrake = coastDragTorque;
        }

        // 2. Handbrake
        if (isBraking)
        {
            appliedMotorTorque = 0f;
            appliedFootBrake = Mathf.Max(appliedFootBrake, handbrakeTorque);
        }

        // 3. Torque Distribution across Wheels
        float frontMotor = 0f;
        float rearMotor = 0f;

        switch (driveTrain)
        {
            case DriveType.AllWheelDrive:
                frontMotor = appliedMotorTorque * 0.4f;
                rearMotor = appliedMotorTorque * 0.6f;
                break;
            case DriveType.RearWheelDrive:
                rearMotor = appliedMotorTorque;
                break;
            case DriveType.FrontWheelDrive:
                frontMotor = appliedMotorTorque;
                break;
        }

        // Apply Motor Torque
        frontLeftCollider.motorTorque = frontMotor;
        frontRightCollider.motorTorque = frontMotor;
        rearLeftCollider.motorTorque = rearMotor;
        rearRightCollider.motorTorque = rearMotor;

        // Apply Brakes
        float frontBrake = isBraking ? (appliedFootBrake * 0.4f) : (appliedFootBrake * 0.5f);
        float rearBrake = appliedFootBrake;

        frontLeftCollider.brakeTorque = frontBrake;
        frontRightCollider.brakeTorque = frontBrake;
        rearLeftCollider.brakeTorque = rearBrake;
        rearRightCollider.brakeTorque = rearBrake;

        // Apply Steering Angle
        frontLeftCollider.steerAngle = currentSteerAngle;
        frontRightCollider.steerAngle = currentSteerAngle;
    }

    /// <summary>
    /// Realistic Anti-Roll Stabilizer Bars. Reads wheel travel directly from the WheelColliders
    /// without modifying any of their physics settings.
    /// </summary>
    private void ApplyAntiRollBars()
    {
        if (antiRollBarForce <= 0f) return;
        ApplyAntiRollBarPair(frontLeftCollider, frontRightCollider);
        ApplyAntiRollBarPair(rearLeftCollider, rearRightCollider);
    }

    private void ApplyAntiRollBarPair(WheelCollider wl, WheelCollider wr)
    {
        if (wl == null || wr == null) return;

        float travelL = 1.0f;
        float travelR = 1.0f;

        bool groundedL = wl.GetGroundHit(out WheelHit hitL);
        if (groundedL && wl.suspensionDistance > 0.001f)
            travelL = (-wl.transform.InverseTransformPoint(hitL.point).y - wl.radius) / wl.suspensionDistance;

        bool groundedR = wr.GetGroundHit(out WheelHit hitR);
        if (groundedR && wr.suspensionDistance > 0.001f)
            travelR = (-wr.transform.InverseTransformPoint(hitR.point).y - wr.radius) / wr.suspensionDistance;

        float antiRollForce = (travelL - travelR) * antiRollBarForce;

        if (groundedL)
            rb.AddForceAtPosition(wl.transform.up * -antiRollForce, wl.transform.position);
        if (groundedR)
            rb.AddForceAtPosition(wr.transform.up * antiRollForce, wr.transform.position);
    }

    private void ApplyDownforce()
    {
        float speedSquared = rb.linearVelocity.sqrMagnitude;
        rb.AddForce(Vector3.down * (downforce + speedSquared * 0.15f), ForceMode.Force);
    }

    private void ApplyParkingBrakes()
    {
        if (frontLeftCollider != null) { frontLeftCollider.motorTorque = 0f; frontLeftCollider.brakeTorque = handbrakeTorque; }
        if (frontRightCollider != null) { frontRightCollider.motorTorque = 0f; frontRightCollider.brakeTorque = handbrakeTorque; }
        if (rearLeftCollider != null) { rearLeftCollider.motorTorque = 0f; rearLeftCollider.brakeTorque = handbrakeTorque; }
        if (rearRightCollider != null) { rearRightCollider.motorTorque = 0f; rearRightCollider.brakeTorque = handbrakeTorque; }

        if (rb.linearVelocity.sqrMagnitude > 0.01f)
        {
            rb.linearVelocity = Vector3.MoveTowards(rb.linearVelocity, Vector3.zero, Time.fixedDeltaTime * 4f);
        }
    }

    public void ResetCarUpright()
    {
        transform.position += Vector3.up * 1.0f;
        transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        Debug.Log("<color=cyan>[Car1980] 🔄 Car reset upright onto the road!</color>");
    }

    /// <summary>
    /// Synchronizes the visual wheel meshes to the exact WheelCollider world poses.
    /// If the visual mesh is separate from the WheelCollider transform, GetWorldPose sets its position/rotation.
    /// </summary>
    private void UpdateWheelVisuals()
    {
        SyncWheelPose(frontLeftCollider, frontLeftWheel);
        SyncWheelPose(frontRightCollider, frontRightWheel);
        SyncWheelPose(rearLeftCollider, rearLeftWheel);
        SyncWheelPose(rearRightCollider, rearRightWheel);

        // Interior Steering Wheel
        if (steeringWheelMesh != null)
        {
            float steerWheelAngle = (currentSteerAngle / maxSteerAngle) * maxSteeringWheelTurn;
            steeringWheelMesh.localRotation = steerWheelInitialRot * Quaternion.Euler(0f, 0f, -steerWheelAngle);
        }

        UpdateTaillights();
    }

    private void SyncWheelPose(WheelCollider col, Transform visualMesh)
    {
        if (col == null || visualMesh == null) return;
        // Only update if visual mesh is a separate transform from the WheelCollider
        if (visualMesh != col.transform)
        {
            col.GetWorldPose(out Vector3 worldPos, out Quaternion worldRot);
            visualMesh.position = worldPos;
            visualMesh.rotation = worldRot;
        }
    }

    private void UpdateCamera()
    {
        // 1. In VR mode: Position XR Origin (activePlayer) inside cockpit or at exterior chase position
        if (VRDeviceDetection.IsVRActive() && activePlayer != null)
        {
            if (isCockpitView)
            {
                if (driverSeatPoint != null)
                {
                    activePlayer.transform.localPosition = driverSeatPoint.localPosition + Vector3.up * cockpitEyeHeight;
                }
                else
                {
                    activePlayer.transform.localPosition = new Vector3(-0.42f, 0.78f + cockpitEyeHeight, 0.15f);
                }
                activePlayer.transform.localRotation = Quaternion.identity;
            }
            else
            {
                Vector3 chaseOffset = Vector3.up * cameraHeight - (Vector3.forward * currentCameraDistance);
                activePlayer.transform.localPosition = chaseOffset;
                activePlayer.transform.localRotation = Quaternion.identity;
            }
            return;
        }

        if (carCamera == null) return;

        if (isCockpitView && driverSeatPoint != null)
        {
            // Interior first-person view
            if (autoRecenter && (Time.time - lastMouseMoveTime > recenterDelay))
            {
                cockpitYaw = Mathf.Lerp(cockpitYaw, 0f, recenterSpeed * Time.deltaTime);
                cockpitPitch = Mathf.Lerp(cockpitPitch, 0f, recenterSpeed * Time.deltaTime);
            }

            carCamera.transform.position = driverSeatPoint.position + Vector3.up * cockpitEyeHeight;
            Quaternion headRot = driverSeatPoint.rotation * Quaternion.Euler(cockpitPitch, cockpitYaw, 0f);
            carCamera.transform.rotation = Quaternion.Slerp(carCamera.transform.rotation, headRot, Time.deltaTime * 24f);
        }
        else
        {
            // Auto-recenter behind the car if moving forward/reverse and user stopped rotating mouse
            if (autoRecenter && (Time.time - lastMouseMoveTime > recenterDelay) && Mathf.Abs(currentSpeed) > 0.4f)
            {
                float targetYaw = currentSpeed < -1f ? 180f : 0f;
                orbitYaw = Mathf.LerpAngle(orbitYaw, targetYaw, recenterSpeed * Time.deltaTime);
                orbitPitch = Mathf.Lerp(orbitPitch, 14f, recenterSpeed * Time.deltaTime);
            }

            // Pivot point on car elevated by user's customizable cameraHeight
            Vector3 pivotPoint = transform.position + Vector3.up * cameraHeight;

            // Calculate rotation: car yaw + orbit mouse rotation
            Quaternion carYawRot = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            Quaternion totalRot = carYawRot * Quaternion.Euler(orbitPitch, orbitYaw, 0f);

            // Compute ideal camera position
            float dist = Mathf.Max(currentCameraDistance, 2.5f);
            Vector3 targetCamPos = pivotPoint - (totalRot * Vector3.forward * dist);

            // Wall Collision check: prevent camera from clipping inside walls or obstacles
            if (cameraWallCollision)
            {
                Vector3 rayDir = targetCamPos - pivotPoint;
                float rayDist = rayDir.magnitude;
                if (Physics.Raycast(pivotPoint, rayDir.normalized, out RaycastHit hit, rayDist, cameraCollisionMask, QueryTriggerInteraction.Ignore))
                {
                    if (hit.transform != transform && !hit.transform.IsChildOf(transform))
                    {
                        targetCamPos = hit.point + hit.normal * 0.3f;
                    }
                }
            }

            // Smoothly move camera
            carCamera.transform.position = Vector3.Lerp(carCamera.transform.position, targetCamPos, cameraSmoothSpeed * Time.deltaTime);

            // Look directly at the car's configurable target height
            Vector3 lookTarget = transform.position + Vector3.up * cameraLookTargetHeight + (carYawRot * Vector3.forward * 0.8f);
            carCamera.transform.LookAt(lookTarget);
        }
    }

    private void UpdateTaillights()
    {
        if (taillights == null || taillights.Length == 0) return;

        bool brakingOrReversing = isBraking || (isDriving && moveInput < 0f && currentSpeed > 0.2f);
        float intensity = brakingOrReversing ? 2.5f : (areHeadlightsOn ? 0.6f : 0f);

        foreach (var light in taillights)
        {
            if (light != null)
            {
                light.enabled = intensity > 0.01f;
                light.intensity = intensity;
            }
        }
    }

    private void UpdateAudio()
    {
        if (engineAudio == null) return;

        if (isDriving)
        {
            if (!engineAudio.isPlaying) engineAudio.Play();

            float speedRatio = Mathf.Abs(currentSpeed) / maxForwardSpeed;
            engineAudio.pitch = Mathf.Lerp(minAudioPitch, maxAudioPitch, speedRatio);
        }
        else
        {
            if (engineAudio.isPlaying && engineAudio.volume > 0.01f)
            {
                engineAudio.volume = Mathf.MoveTowards(engineAudio.volume, 0f, Time.deltaTime * 2f);
                if (engineAudio.volume <= 0.01f) engineAudio.Stop();
            }
        }
    }

    public void StartEngine()
    {
        if (engineAudio != null)
        {
            if (engineStartClip != null)
                engineAudio.PlayOneShot(engineStartClip);

            engineAudio.volume = 1f;
            engineAudio.pitch = minAudioPitch;
            engineAudio.loop = true;
            engineAudio.Play();
        }
    }

    public void StopEngine()
    {
        if (engineAudio != null)
        {
            engineAudio.volume = 0f;
            engineAudio.Stop();
        }
    }

    public void ToggleCameraView()
    {
        isCockpitView = !isCockpitView;
        cockpitYaw = 0f;
        cockpitPitch = 0f;
        orbitYaw = 0f;
        orbitPitch = 14f;
        UpdateCamera();
    }

    public void FlashHeadlights()
    {
        if (flashRoutine != null)
            StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(FlashHeadlightsRoutine());
    }

    private IEnumerator FlashHeadlightsRoutine()
    {
        bool original = areHeadlightsOn;
        // Double flash effect
        SetHeadlights(true);
        yield return new WaitForSeconds(0.12f);
        SetHeadlights(false);
        yield return new WaitForSeconds(0.10f);
        SetHeadlights(true);
        yield return new WaitForSeconds(0.12f);
        SetHeadlights(!original);
        flashRoutine = null;
    }

    public void ToggleHeadlights()
    {
        SetHeadlights(!areHeadlightsOn);
    }

    public void SetHeadlights(bool on)
    {
        areHeadlightsOn = on;
        if (headlights != null)
        {
            foreach (var h in headlights)
            {
                if (h != null) h.enabled = areHeadlightsOn;
            }
        }
    }

    public void PlayHorn()
    {
        if (engineAudio != null && hornClip != null)
        {
            engineAudio.PlayOneShot(hornClip);
        }
        Ghost.EmitSound(transform.position, 55f, "Car Horn Sound");
    }

    public void SetHasKey(bool collected)
    {
        hasKey = collected;
        Debug.Log($"<color=cyan>[Car1980] Car Key Status Updated: hasKey = {hasKey}</color>");
    }

    public void TriggerEscape()
    {
        Debug.Log("<color=green><b>[Car1980] 🏆 ESCAPE SUCCESSFUL! The classic car sped away into the night!</b></color>");
        OnCarEscaped?.Invoke();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (isDriving && (other.CompareTag("Finish") || other.name.ToLower().Contains("escape") || other.name.ToLower().Contains("exit_gate")))
        {
            TriggerEscape();
        }
    }

    private void OnGUI()
    {
        if (!showHUD) return;

        GUIStyle boxStyle = new GUIStyle(GUI.skin.box);
        boxStyle.fontSize = 14;
        boxStyle.alignment = TextAnchor.UpperLeft;
        boxStyle.normal.textColor = Color.white;

        if (isDriving)
        {
            float speedKmh = CurrentSpeedKmH;
            string gear = isBraking ? "HANDBRAKE" : (moveInput > 0.05f ? "DRIVE (D)" : (moveInput < -0.05f ? "REVERSE (R)" : "NEUTRAL (N)"));
            string viewName = isCockpitView ? "Cockpit View (Interior)" : $"Chase Orbit (Height: {cameraHeight:F1}m, Dist: {currentCameraDistance:F1}m)";

            // Check if wheels are grounded
            int wheelsGrounded = 0;
            if (frontLeftCollider != null && frontLeftCollider.isGrounded) wheelsGrounded++;
            if (frontRightCollider != null && frontRightCollider.isGrounded) wheelsGrounded++;
            if (rearLeftCollider != null && rearLeftCollider.isGrounded) wheelsGrounded++;
            if (rearRightCollider != null && rearRightCollider.isGrounded) wheelsGrounded++;

            GUILayout.BeginArea(new Rect(20, Screen.height - 230, 440, 215), GUI.skin.box);
            GUILayout.Label($"<b>🚗 {carName}</b> (WheelCollider Physics)", GUILayout.Height(22));
            GUILayout.Label($"<b>Speed:</b> {Mathf.Abs(speedKmh):F1} km/h  |  <b>Gear:</b> {gear}  |  <b>Tires on Road:</b> {wheelsGrounded}/4");
            GUILayout.Label($"<b>Camera:</b> {viewName}");
            GUILayout.Label($"<b>VR Controller Buttons:</b>");
            GUILayout.Label($"  • <b>[A Button / R-Stick Click]:</b> Camera Change (Cockpit <-> Chase)");
            GUILayout.Label($"  • <b>[X Button / L-Stick Click]:</b> Flash Headlights");
            GUILayout.Label($"  • <b>[B / Y / Grip Buttons]:</b> Exit Car");
            GUILayout.Label($"  • <b>[Triggers / Sticks]:</b> Gas (R), Brake (L), Steer");
            GUILayout.Label($"<b>Keyboard / Gamepad:</b> [WASD] Drive | [C] Camera | [F] Flash | [E] Exit | [H] Horn | [R] Reset");
            GUILayout.EndArea();
        }
        else
        {
            GUILayout.BeginArea(new Rect(20, Screen.height - 70, 260, 50), GUI.skin.box);
            GUILayout.Label($"<b>🚗 {carName}</b>");
            GUILayout.Label("Press <b>[E]</b> or <b>[Enter]</b> to Drive!");
            GUILayout.EndArea();
        }
    }
}
