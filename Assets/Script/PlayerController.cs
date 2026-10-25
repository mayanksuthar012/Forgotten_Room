using UnityEngine;
using UnityEngine.EventSystems;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float playerSpeed = 5f;
    [SerializeField] private float gravity = -20f;

    [Header("Mouse Look")]
    [SerializeField] private float mouseSens = 100f;
    [SerializeField] private float touchSens = 0.15f;
    [SerializeField] private float minPitch = -80f;
    [SerializeField] private float maxPitch = 80f;

    [Header("References")]
    [SerializeField] private Transform cameraTransform;

    private CharacterController controller;
    private Vector2 mobileMove;
    private float verticalVelocity;
    private float pitch;
    private bool canControl = true;

    public bool CanControl => canControl;

    public void SetControlActive(bool active)
    {
        canControl = active;
        if (!active)
        {
            mobileMove = Vector2.zero;
        }
    }

    public void LookAtTarget(Vector3 worldPosition)
    {
        Vector3 direction = (worldPosition - transform.position).normalized;
        direction.y = 0;
        if (direction.sqrMagnitude > 0.001f)
        {
            transform.rotation = Quaternion.LookRotation(direction);
        }

        if (cameraTransform != null)
        {
            Vector3 camDir = (worldPosition - cameraTransform.position).normalized;
            float targetPitch = -Mathf.Asin(camDir.y) * Mathf.Rad2Deg;
            pitch = Mathf.Clamp(targetPitch, minPitch, maxPitch);
            cameraTransform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }
    }

    private void Awake()
    {
        controller = GetComponent<CharacterController>();

        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;

        if (cameraTransform == null)
        {
            Camera childCam = GetComponentInChildren<Camera>();
            if (childCam != null) cameraTransform = childCam.transform;
        }

        // When testing in Editor / Desktop mode without a VR headset active,
        // disable TrackedPoseDriver so it doesn't overwrite mouse look rotation!
        if (cameraTransform != null)
        {
            var trackedPoseDriver = cameraTransform.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
            if (trackedPoseDriver != null)
            {
                trackedPoseDriver.enabled = VRDeviceDetection.IsVRActive();
            }
        }
    }

    private void Start()
    {
        // Auto-lock cursor in Editor/desktop play mode for immediate first-person controls
        if (!VRDeviceDetection.IsVRActive())
        {
            SetCursorState(true);
            pitch = 0f;
            if (cameraTransform != null)
            {
                cameraTransform.localRotation = Quaternion.Euler(0f, 0f, 0f);
            }
        }
    }

    private void Update()
    {
        bool isVR = VRDeviceDetection.IsVRActive();
        if (isVR)
        {
            // In VR mode:
            // Head orientation is driven exclusively by the VR Headset (TrackedPoseDriver / OpenXR).
            // Mouse look must NEVER overwrite cameraTransform.localRotation in VR!
            // Locomotion is driven by VRHandController thumbsticks or keyboard WASD.
            HandleMovement();
            return;
        }

        HandleMouseInput();
        HandleMovement();
    }

    private void HandleMouseInput()
    {
        if (!canControl)
            return;

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            SetCursorState(false);
        }

        if (DoorKeypad.IsAnyKeypadOpen || (PuzzleManager.Instance != null && PuzzleManager.Instance.IsAnyPanelOpen))
        {
            // Keep cursor unlocked and allow free clicking on door num pad buttons and UI panels
            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            if (EventSystem.current == null ||
                !EventSystem.current.IsPointerOverGameObject())
            {
                SetCursorState(true);
            }
        }

        if (Cursor.lockState == CursorLockMode.Locked)
        {
            float mouseX =
                Input.GetAxis("Mouse X") * mouseSens * Time.deltaTime;

            float mouseY =
                Input.GetAxis("Mouse Y") * mouseSens * Time.deltaTime;

            Look(mouseX, mouseY);
        }
    }

    private void HandleMovement()
    {
        if (!canControl)
            return;

        // Don't move if player is interacting with keypad or UI menu
        if (DoorKeypad.IsAnyKeypadOpen || (PuzzleManager.Instance != null && PuzzleManager.Instance.IsAnyPanelOpen))
            return;

        // In desktop mode, don't move if cursor is unlocked
        if (!VRDeviceDetection.IsVRActive() && Cursor.lockState != CursorLockMode.Locked)
            return;

        Vector2 input = new Vector2(
            Input.GetAxisRaw("Horizontal"),
            Input.GetAxisRaw("Vertical")
        );

        if (mobileMove.sqrMagnitude > 0.01f)
            input = mobileMove;

        input = Vector2.ClampMagnitude(input, 1f);

        // Use the camera direction, ignoring camera pitch.
        Transform reference = cameraTransform != null
            ? cameraTransform
            : transform;

        Vector3 forward = Vector3.ProjectOnPlane(
            reference.forward, Vector3.up).normalized;

        Vector3 right = Vector3.ProjectOnPlane(
            reference.right, Vector3.up).normalized;

        Vector3 direction = forward * input.y + right * input.x;
        direction = Vector3.ClampMagnitude(direction, 1f);

        if (controller.isGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f;

        verticalVelocity += gravity * Time.deltaTime;
        verticalVelocity = Mathf.Max(verticalVelocity, -25f);

        Vector3 velocity =
            direction * playerSpeed +
            Vector3.up * verticalVelocity;

        controller.Move(velocity * Time.deltaTime);
    }

    private void Look(float yaw, float lookY)
    {
        // Rotate the player horizontally.
        transform.Rotate(Vector3.up * yaw);

        // Rotate the camera vertically.
        pitch -= lookY;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        if (cameraTransform != null)
            cameraTransform.localRotation =
                Quaternion.Euler(pitch, 0f, 0f);
    }

    public void SetMobileMove(Vector2 input)
    {
        mobileMove = Vector2.ClampMagnitude(input, 1f);
    }

    public void LookTouch(Vector2 delta)
    {
        Look(delta.x * touchSens, delta.y * touchSens);
    }

    public void SetCursorState(bool locked)
    {
        Cursor.lockState = locked
            ? CursorLockMode.Locked
            : CursorLockMode.None;

        Cursor.visible = !locked;
    }
}