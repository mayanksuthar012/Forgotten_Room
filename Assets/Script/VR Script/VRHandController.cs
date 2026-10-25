using UnityEngine;
using UnityEngine.XR;
using TMPro;

/// <summary>
/// Controls a single VR hand (Left or Right controller):
/// - Fully supports DUAL ray controllers: BOTH hands show rays, visuals, and interact!
/// - In VR mode: Tracks 6DOF position and rotation from Meta Quest / OpenXR / SteamVR controllers.
/// - In Desktop / Editor mode: Positions BOTH controllers in front of the camera (Left on the left,
///   Right on the right), both pointing their laser rays forward to the camera aim point.
/// - Triggers (Left Click / [E] / [Q] / VR Trigger) interact with doors, keypads, clues, keys, and car.
/// - Grips (Right Click / [Esc] / VR Grip) close open clue panels or door keypads.
/// - Left Controller thumbstick: Smooth walking locomotion (in VR).
/// - Right Controller thumbstick: 45° snap turning (in VR).
/// </summary>
public class VRHandController : MonoBehaviour
{
    public enum HandType { LeftHand, RightHand }

    [Header("Hand Setup")]
    [SerializeField] private HandType hand = HandType.RightHand;
    [SerializeField] private float rayDistance = 6.0f;
    [SerializeField] private LayerMask raycastMask = ~0;

    [Header("Laser Visuals")]
    [SerializeField] private Color normalColor = new Color(0.3f, 0.85f, 1f, 0.65f);
    [SerializeField] private Color hoverColor = new Color(0.1f, 1f, 0.9f, 1f);
    [SerializeField] private float rayStartWidth = 0.012f;
    [SerializeField] private float rayEndWidth = 0.005f;

    [Header("Locomotion (Left=Move, Right=Turn in VR)")]
    [SerializeField] private float moveSpeed = 4.0f;
    [SerializeField] private float snapTurnAngle = 45f;
    [SerializeField] private float snapTurnCooldown = 0.35f;

    public static int LastInteractFrame = -1;
    private static VRHandController activePromptOwner = null;

    private XRNode xrNode;
    private InputDevice device;
    private LineRenderer lineRenderer;
    private CharacterController characterController;
    private Transform hmdCamera;
    private IInteractable hoveredInteractable;
    private KeypadButton currentHoveredButton;
    private TMP_Text promptText;
    private GameObject visualModel;
    private GameObject hitDot;
    private Renderer hitDotRenderer;

    private bool wasTriggerPressed = false;
    private bool wasGripPressed = false;
    private float nextTurnTime = 0f;
    private static float verticalVelocity = 0f;

    private void Awake()
    {
        xrNode = (hand == HandType.LeftHand) ? XRNode.LeftHand : XRNode.RightHand;
        characterController = GetComponentInParent<CharacterController>();

        FindCamera();
        FindPromptText();
        SetupHandVisual();
        SetupLaserLine();
        SetupHitDot();
    }

    private void FindCamera()
    {
        if (Camera.main != null)
        {
            hmdCamera = Camera.main.transform;
            return;
        }

        Camera cam = GetComponentInParent<Camera>() ?? FindAnyObjectByType<Camera>();
        if (cam != null)
        {
            hmdCamera = cam.transform;
        }
    }

    private void FindPromptText()
    {
        if (promptText != null) return;
        var texts = FindObjectsByType<TMP_Text>(FindObjectsInactive.Include);
        foreach (var t in texts)
        {
            if (t.name.ToLower().Contains("prompt"))
            {
                promptText = t;
                break;
            }
        }
    }

    private void SetupLaserLine()
    {
        lineRenderer = GetComponent<LineRenderer>();
        if (lineRenderer == null)
            lineRenderer = gameObject.AddComponent<LineRenderer>();

        lineRenderer.useWorldSpace = true;
        lineRenderer.startWidth = rayStartWidth;
        lineRenderer.endWidth = rayEndWidth;

        Shader lineShader = Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Sprites/Default")
                         ?? Shader.Find("Unlit/Color");

        if (lineShader != null)
        {
            Material mat = new Material(lineShader);
            SetMaterialColor(mat, normalColor);
            lineRenderer.material = mat;
        }

        lineRenderer.positionCount = 2;
        lineRenderer.enabled = true;
    }

    private void SetupHitDot()
    {
        if (hitDot == null)
        {
            hitDot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            hitDot.name = hand == HandType.LeftHand ? "LeftLaserDot" : "RightLaserDot";

            Collider col = hitDot.GetComponent<Collider>();
            if (col != null) DestroyImmediate(col);

            hitDot.transform.localScale = Vector3.one * 0.024f; // 2.4 cm dot

            Shader s = Shader.Find("Universal Render Pipeline/Unlit")
                    ?? Shader.Find("Sprites/Default")
                    ?? Shader.Find("Unlit/Color");

            if (s != null)
            {
                Material m = new Material(s);
                SetMaterialColor(m, normalColor);
                hitDotRenderer = hitDot.GetComponent<Renderer>();
                if (hitDotRenderer != null) hitDotRenderer.material = m;
            }

            hitDot.SetActive(false);
        }
    }

    private void SetupHandVisual()
    {
        if (visualModel != null) return;

        Transform existing = transform.Find(hand == HandType.LeftHand ? "LeftHandVisual" : "RightHandVisual");
        if (existing != null)
        {
            visualModel = existing.gameObject;
            return;
        }

        visualModel = new GameObject(hand == HandType.LeftHand ? "LeftHandVisual" : "RightHandVisual");
        visualModel.transform.SetParent(transform, false);
        visualModel.transform.localPosition = Vector3.zero;
        visualModel.transform.localRotation = Quaternion.identity;

        // 1. Controller grip body
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        body.transform.SetParent(visualModel.transform, false);
        Collider bodyCol = body.GetComponent<Collider>();
        if (bodyCol != null) DestroyImmediate(bodyCol);
        body.transform.localScale = new Vector3(0.045f, 0.065f, 0.045f);
        body.transform.localPosition = new Vector3(0f, -0.025f, -0.04f);
        body.transform.localRotation = Quaternion.Euler(20f, 0f, 0f);

        // 2. Controller pointer nozzle / emitter pointing forward along +Z
        GameObject nozzle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        nozzle.name = "Emitter";
        nozzle.transform.SetParent(visualModel.transform, false);
        Collider nozzleCol = nozzle.GetComponent<Collider>();
        if (nozzleCol != null) DestroyImmediate(nozzleCol);
        nozzle.transform.localScale = new Vector3(0.020f, 0.030f, 0.020f);
        nozzle.transform.localPosition = new Vector3(0f, 0.005f, 0.015f);
        nozzle.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        // Apply dark sleek materials with distinct glowing accents for Left (electric blue) and Right (cyan)
        Shader litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (litShader != null)
        {
            Material bodyMat = new Material(litShader);
            SetMaterialColor(bodyMat, new Color(0.12f, 0.14f, 0.18f, 1f));
            var rend = body.GetComponent<Renderer>();
            if (rend != null) rend.material = bodyMat;

            Material nozzleMat = new Material(litShader);
            Color accentCol = (hand == HandType.RightHand) ? new Color(0.1f, 1f, 0.9f, 1f) : new Color(0.2f, 0.75f, 1f, 1f);
            SetMaterialColor(nozzleMat, accentCol);
            var nRend = nozzle.GetComponent<Renderer>();
            if (nRend != null) nRend.material = nozzleMat;
        }
    }

    private static void SetMaterialColor(Material mat, Color col)
    {
        if (mat == null) return;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", col);
        if (mat.HasProperty("_TintColor")) mat.SetColor("_TintColor", col);
    }

    public bool IsPhysicalVRTracking()
    {
        if (!VRDeviceDetection.IsVRActive()) return false;
        EnsureDeviceResolved();
        return device.isValid && VRDeviceDetection.IsControllerTracked(device);
    }

    private void EnsureDeviceResolved()
    {
        if (!device.isValid)
        {
            device = InputDevices.GetDeviceAtXRNode(xrNode);
            if (!device.isValid)
            {
                var characteristics = (hand == HandType.LeftHand)
                    ? (InputDeviceCharacteristics.HeldInHand | InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.Left)
                    : (InputDeviceCharacteristics.HeldInHand | InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.Right);

                var list = new System.Collections.Generic.List<InputDevice>();
                InputDevices.GetDevicesWithCharacteristics(characteristics, list);
                if (list.Count > 0)
                {
                    device = list[0];
                }
            }
        }
    }

    private void Update()
    {
        // 1. Resolve XR device handle
        EnsureDeviceResolved();

        // 2. While driving in a car, car controller handles inputs and driving
        if (Car1980.Instance != null && Car1980.Instance.IsDriving)
        {
            if (currentHoveredButton != null)
            {
                currentHoveredButton.SetHover(false);
                currentHoveredButton = null;
            }
            hoveredInteractable = null;
            if (activePromptOwner == this)
            {
                activePromptOwner = null;
                if (promptText != null) promptText.text = "";
            }
            return;
        }

        // 3. Handle Inputs for this controller in Update
        HandleTriggerInput();
        HandleGripInput();

        // 4. Thumbstick locomotion (only when physical VR controllers are actively tracking)
        if (IsPhysicalVRTracking())
        {
            HandleThumbstickLocomotion();
        }
    }

    private void LateUpdate()
    {
        if (hmdCamera == null)
        {
            FindCamera();
        }

        bool isPhysicalVR = IsPhysicalVRTracking();
        bool isDriving = (Car1980.Instance != null && Car1980.Instance.IsDriving);

        if (isDriving)
        {
            if (lineRenderer != null && lineRenderer.enabled) lineRenderer.enabled = false;
            if (hitDot != null && hitDot.activeSelf) hitDot.SetActive(false);

            if (isPhysicalVR)
            {
                if (device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 pos))
                {
                    transform.localPosition = pos;
                }
                if (device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rot))
                {
                    transform.localRotation = rot * Quaternion.Euler(15f, 0f, 0f);
                }
            }
            else
            {
                if (visualModel != null && visualModel.activeSelf) visualModel.SetActive(false);
            }

            if (currentHoveredButton != null)
            {
                currentHoveredButton.SetHover(false);
                currentHoveredButton = null;
            }
            hoveredInteractable = null;
            return;
        }

        // Ensure visuals and laser are active for BOTH controllers
        if (visualModel != null && !visualModel.activeSelf) visualModel.SetActive(true);
        if (lineRenderer != null && !lineRenderer.enabled) lineRenderer.enabled = true;

        if (!isPhysicalVR)
        {
            // --- DESKTOP / EDITOR PLAY MODE (DUAL RAY CONTROLLERS) ---
            if (hmdCamera != null)
            {
                // Position controller in front of camera (Left Hand on left, Right Hand on right)
                float sideOffset = (hand == HandType.RightHand) ? 0.22f : -0.22f;
                Vector3 handPos = hmdCamera.position + (hmdCamera.right * sideOffset) - (hmdCamera.up * 0.16f) + (hmdCamera.forward * 0.40f);

                // Both controllers point STRAIGHT FORWARD parallel to the camera view!
                // NEVER angle inward towards each other or cross rays!
                transform.position = handPos;
                transform.rotation = Quaternion.LookRotation(hmdCamera.forward, hmdCamera.up);
            }
        }
        else
        {
            // --- VR 6DOF MOTION CONTROLLER MODE ---
            if (device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 pos))
            {
                transform.localPosition = pos;
            }
            if (device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rot))
            {
                // Subtle 15° forward-down tilt matches natural pointing angle
                transform.localRotation = rot * Quaternion.Euler(15f, 0f, 0f);
            }

            // In VR, keep CharacterController cylinder horizontally centered with HMD camera without collapsing height
            if (hand == HandType.LeftHand && characterController != null && hmdCamera != null)
            {
                Vector3 hmdLocal = characterController.transform.InverseTransformPoint(hmdCamera.position);
                characterController.center = new Vector3(hmdLocal.x, 0.9f, hmdLocal.z);
            }
        }

        // 3. Process Interaction Ray for this hand
        UpdateInteractionRay();
    }

    private void OnDisable()
    {
        if (currentHoveredButton != null)
        {
            currentHoveredButton.SetHover(false);
            currentHoveredButton = null;
        }
        if (lineRenderer != null)
        {
            lineRenderer.enabled = false;
        }
        if (hitDot != null)
        {
            hitDot.SetActive(false);
        }
        if (activePromptOwner == this)
        {
            activePromptOwner = null;
            if (promptText != null) promptText.text = "";
        }
    }

    private void UpdateInteractionRay()
    {
        Vector3 rayOrigin = transform.position + transform.forward * 0.05f; // Emitter nozzle tip
        Vector3 rayDirection = transform.forward;

        Ray ray = new Ray(rayOrigin, rayDirection);
        Vector3 endPoint = rayOrigin + rayDirection * rayDistance;
        hoveredInteractable = null;
        bool didHitSurface = false;

        RaycastHit[] hits = Physics.RaycastAll(ray, rayDistance, raycastMask);
        if (hits != null && hits.Length > 0)
        {
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            KeypadButton priorityBtn = null;
            float btnDist = float.MaxValue;
            Vector3 btnPoint = endPoint;

            IInteractable closestInteractable = null;
            float closestInteractableDist = float.MaxValue;
            Vector3 interactablePoint = endPoint;

            float closestSurfaceDist = float.MaxValue;
            Vector3 surfacePoint = endPoint;

            foreach (var hit in hits)
            {
                if (hit.collider == null) continue;

                // Ignore player body colliders
                if (characterController != null && hit.collider == characterController) continue;
                if (hit.collider.transform.root == transform.root) continue;

                // Track closest physical surface
                if (hit.distance < closestSurfaceDist)
                {
                    closestSurfaceDist = hit.distance;
                    surfacePoint = hit.point;
                    didHitSurface = true;
                }

                KeypadButton kb = hit.collider.GetComponent<KeypadButton>() ?? hit.collider.GetComponentInParent<KeypadButton>();
                if (kb != null && hit.distance < btnDist)
                {
                    priorityBtn = kb;
                    btnDist = hit.distance;
                    btnPoint = hit.point;
                    break;
                }

                IInteractable ii = hit.collider.GetComponentInParent<IInteractable>();
                if (ii != null && hit.distance < closestInteractableDist)
                {
                    closestInteractable = ii;
                    closestInteractableDist = hit.distance;
                    interactablePoint = hit.point;
                }
            }

            if (priorityBtn != null)
            {
                hoveredInteractable = priorityBtn;
                endPoint = btnPoint;
            }
            else if (closestInteractable != null)
            {
                hoveredInteractable = closestInteractable;
                endPoint = interactablePoint;
            }
            else if (closestSurfaceDist < float.MaxValue)
            {
                endPoint = surfacePoint;
            }
        }

        // Highlight keypad buttons when laser aims at them
        KeypadButton hitBtn = hoveredInteractable as KeypadButton;
        if (currentHoveredButton != hitBtn)
        {
            if (currentHoveredButton != null) currentHoveredButton.SetHover(false);
            currentHoveredButton = hitBtn;
            if (currentHoveredButton != null) currentHoveredButton.SetHover(true);
        }

        // Coordinated UI Prompt Text display
        UpdatePromptDisplay();

        // Update Laser Visuals
        Color targetColor = (hoveredInteractable != null) ? hoverColor : normalColor;

        if (lineRenderer != null)
        {
            if (!lineRenderer.enabled) lineRenderer.enabled = true;
            lineRenderer.SetPosition(0, rayOrigin);
            lineRenderer.SetPosition(1, endPoint);

            if (lineRenderer.material != null)
            {
                SetMaterialColor(lineRenderer.material, targetColor);
            }
        }

        // Update Impact Reticle Dot
        if (hitDot != null)
        {
            if (didHitSurface || hoveredInteractable != null)
            {
                if (!hitDot.activeSelf) hitDot.SetActive(true);
                hitDot.transform.position = endPoint;
                if (hitDotRenderer != null && hitDotRenderer.material != null)
                {
                    SetMaterialColor(hitDotRenderer.material, targetColor);
                }
                float scale = (hoveredInteractable != null) ? 0.036f : 0.024f;
                hitDot.transform.localScale = Vector3.one * scale;
            }
            else
            {
                if (hitDot.activeSelf) hitDot.SetActive(false);
            }
        }
    }

    private void UpdatePromptDisplay()
    {
        if (promptText == null) return;

        if (hoveredInteractable != null)
        {
            activePromptOwner = this;
            promptText.text = hoveredInteractable.interactionText;
        }
        else if (activePromptOwner == this)
        {
            activePromptOwner = null;
            promptText.text = "";
        }
    }

    private void HandleTriggerInput()
    {
        bool isTriggerPressed = false;

        if (IsPhysicalVRTracking())
        {
            // VR Motion Controller: Read this hand's physical trigger
            if (device.TryGetFeatureValue(CommonUsages.triggerButton, out bool tb) && tb)
            {
                isTriggerPressed = true;
            }
            else if (device.TryGetFeatureValue(CommonUsages.trigger, out float tv) && tv > 0.35f)
            {
                isTriggerPressed = true;
            }
            else if (device.TryGetFeatureValue(CommonUsages.primaryButton, out bool pb) && pb)
            {
                isTriggerPressed = true;
            }
            else if (device.TryGetFeatureValue(CommonUsages.secondaryButton, out bool sb) && sb)
            {
                isTriggerPressed = true;
            }
        }
        else
        {
            // Desktop / Editor Mode:
            // Right Hand triggers on Left Click or [E]
            // Left Hand triggers on [Q], [F], or Left Click if aiming at an interactable
            if (hand == HandType.RightHand)
            {
                if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.E))
                {
                    isTriggerPressed = true;
                }
            }
            else if (hand == HandType.LeftHand)
            {
                if (Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.F))
                {
                    isTriggerPressed = true;
                }
                else if (Input.GetMouseButtonDown(0) && hoveredInteractable != null)
                {
                    isTriggerPressed = true;
                }
            }
        }

        // On Trigger Click Down
        if (isTriggerPressed && !wasTriggerPressed)
        {
            if (hoveredInteractable != null && Time.frameCount != LastInteractFrame)
            {
                LastInteractFrame = Time.frameCount;
                hoveredInteractable.Interact();
            }
        }

        wasTriggerPressed = isTriggerPressed;
    }

    private void HandleGripInput()
    {
        bool isGripPressed = false;

        if (IsPhysicalVRTracking())
        {
            if (device.TryGetFeatureValue(CommonUsages.gripButton, out bool gb) && gb)
            {
                isGripPressed = true;
            }
            else if (device.TryGetFeatureValue(CommonUsages.grip, out float gv) && gv > 0.45f)
            {
                isGripPressed = true;
            }
        }
        else
        {
            // Desktop / Editor mode: Right Click or [Escape] acts as grip (closes open menus/keypad)
            if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape))
            {
                isGripPressed = true;
            }
        }

        if (isGripPressed && !wasGripPressed)
        {
            if (PuzzleManager.Instance != null && PuzzleManager.Instance.IsAnyPanelOpen)
            {
                PuzzleManager.Instance.ClosePanel();
                Debug.Log($"<color=cyan>[VRHandController] Grip pressed ({hand}): Closed active menu panel.</color>");
            }
            else if (DoorKeypad.IsAnyKeypadOpen)
            {
                DoorKeypad.CloseAnyActiveKeypad();
                Debug.Log($"<color=cyan>[VRHandController] Grip pressed ({hand}): Closed active door keypad.</color>");
            }
        }

        wasGripPressed = isGripPressed;
    }

    private void HandleThumbstickLocomotion()
    {
        if (hand == HandType.LeftHand && characterController != null)
        {
            if (characterController.isGrounded)
            {
                verticalVelocity = -2.5f;
            }
            else
            {
                verticalVelocity += Physics.gravity.y * Time.deltaTime;
            }

            Vector3 moveVelocity = Vector3.zero;

            if (device.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 stick))
            {
                if (stick.sqrMagnitude > 0.04f)
                {
                    Transform dirRef = (hmdCamera != null) ? hmdCamera : transform;
                    Vector3 fwd = Vector3.ProjectOnPlane(dirRef.forward, Vector3.up).normalized;
                    Vector3 rgt = Vector3.ProjectOnPlane(dirRef.right, Vector3.up).normalized;
                    Vector3 moveDir = fwd * stick.y + rgt * stick.x;
                    moveVelocity = moveDir * moveSpeed;
                }
            }

            moveVelocity.y = verticalVelocity;
            characterController.Move(moveVelocity * Time.deltaTime);
        }
        else if (hand == HandType.RightHand)
        {
            if (device.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 stick))
            {
                // Right Thumbstick: Snap Turn 45°
                if (Mathf.Abs(stick.x) > 0.55f && Time.time >= nextTurnTime)
                {
                    float angle = (stick.x > 0f) ? snapTurnAngle : -snapTurnAngle;
                    Transform rigTransform = characterController != null ? characterController.transform : transform.parent;
                    if (rigTransform != null)
                    {
                        rigTransform.Rotate(Vector3.up * angle);
                    }
                    nextTurnTime = Time.time + snapTurnCooldown;
                }
            }
        }
    }

    public void SetHandType(HandType type)
    {
        hand = type;
        xrNode = (hand == HandType.LeftHand) ? XRNode.LeftHand : XRNode.RightHand;
    }
}
