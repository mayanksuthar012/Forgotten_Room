using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class InteractionRaycaster : MonoBehaviour
{
    [Header("Raycast Settings")]
    [SerializeField] private Camera PlayerCamera;
    [SerializeField] private float InteractionDistance = 4.5f;
    [SerializeField] private LayerMask interactionLayer = ~0;

    [Header("Prompt UI")]
    [SerializeField] private TMP_Text PromptText;

    [Header("Crosshair UI")]
    [Tooltip("The UI Image in your Canvas representing the center crosshair dot")]
    [SerializeField] private Image crosshairImage;
    [SerializeField] private Color defaultCrosshairColor = new Color(1f, 1f, 1f, 0.45f);
    [SerializeField] private Color interactCrosshairColor = new Color(0.2f, 0.85f, 1f, 1f);
    [SerializeField] private float defaultScale = 1f;
    [SerializeField] private float interactScale = 1.45f;
    [SerializeField] private float transitionSpeed = 12f;
    [SerializeField] private bool hideCrosshairInUI = true;

    [Header("Optional Crosshair Sprites")]
    [Tooltip("Default dot sprite (optional)")]
    [SerializeField] private Sprite defaultSprite;
    [Tooltip("Sprite to show when aiming at an interactable object (optional)")]
    [SerializeField] private Sprite interactSprite;

    private IInteractable currentTarget;
    private RectTransform crosshairRect;

    private void Awake()
    {
        if (PlayerCamera == null)
            PlayerCamera = Camera.main;

        if (PlayerCamera == null)
            PlayerCamera = GetComponentInChildren<Camera>();

        // Auto-find PromptText if not assigned in Inspector
        if (PromptText == null)
        {
            var texts = FindObjectsByType<TMP_Text>(FindObjectsInactive.Include);
            foreach (var t in texts)
            {
                if (t.name.ToLower().Contains("prompt"))
                {
                    PromptText = t;
                    break;
                }
            }
        }

        // Auto-find crosshairImage if not assigned in Inspector
        if (crosshairImage == null)
        {
            var images = FindObjectsByType<Image>(FindObjectsInactive.Include);
            foreach (var img in images)
            {
                if (img.name.ToLower().Contains("crosshair"))
                {
                    crosshairImage = img;
                    break;
                }
            }
        }

        // Procedural fallback: Create center crosshair dot if canvas exists and no crosshair found
        if (crosshairImage == null)
        {
            Canvas mainCanvas = FindAnyObjectByType<Canvas>();
            if (mainCanvas != null)
            {
                GameObject dotObj = new GameObject("CenterCrosshairDot");
                dotObj.transform.SetParent(mainCanvas.transform, false);
                crosshairImage = dotObj.AddComponent<Image>();
                crosshairRect = dotObj.GetComponent<RectTransform>();
                crosshairRect.sizeDelta = new Vector2(8f, 8f);
                crosshairRect.anchoredPosition = Vector2.zero;
                crosshairImage.color = defaultCrosshairColor;
            }
        }

        if (crosshairImage != null)
        {
            crosshairRect = crosshairImage.rectTransform;
            crosshairImage.color = defaultCrosshairColor;
            if (defaultSprite != null)
                crosshairImage.sprite = defaultSprite;
        }
    }

    private void Update()
    {
        // When driving in a car, hide crosshair and clear prompt so windshield view is unobstructed
        if (Car1980.Instance != null && Car1980.Instance.IsDriving)
        {
            if (crosshairImage != null && crosshairImage.enabled) crosshairImage.enabled = false;
            if (PromptText != null && !string.IsNullOrEmpty(PromptText.text)) PromptText.text = "";
            return;
        }

        // When active in VR with motion controllers, VR hand lasers handle interactions
        if (VRDeviceDetection.AreVRControllersActive())
        {
            if (crosshairImage != null) crosshairImage.enabled = false;
            if (PromptText != null) PromptText.text = "";
            return;
        }

        PerformRaycast();
        UpdatePromptUI();
        UpdateCrosshairUI();
        HandleInteractionInput();
    }

    private void PerformRaycast()
    {
        currentTarget = null;

        // If player is in UI mode with keypad open, don't raycast from camera center
        if (DoorKeypad.IsAnyKeypadOpen || (PuzzleManager.Instance != null && PuzzleManager.Instance.IsAnyPanelOpen))
            return;

        if (PlayerCamera == null)
            PlayerCamera = Camera.main;

        if (PlayerCamera == null)
            PlayerCamera = GetComponentInChildren<Camera>();

        if (PlayerCamera == null)
            return;

        Ray ray = new Ray(PlayerCamera.transform.position, PlayerCamera.transform.forward);
        RaycastHit[] hits = Physics.RaycastAll(ray, InteractionDistance, interactionLayer);

        if (hits != null && hits.Length > 0)
        {
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (var hit in hits)
            {
                if (hit.collider == null) continue;

                // Ignore player's own CharacterController and rig
                if (hit.collider.transform.root == transform.root) continue;

                // Priority 1: KeypadButton
                KeypadButton kb = hit.collider.GetComponent<KeypadButton>() ?? hit.collider.GetComponentInParent<KeypadButton>();
                if (kb != null)
                {
                    currentTarget = kb;
                    break;
                }

                // Priority 2: General IInteractable (ExitDoor, BigDoor, ClueObject, BigDoorKey, Car)
                IInteractable ii = hit.collider.GetComponentInParent<IInteractable>();
                if (ii != null)
                {
                    currentTarget = ii;
                    break;
                }
            }
        }
    }

    private void UpdatePromptUI()
    {
        if (PromptText != null)
        {
            PromptText.text = currentTarget != null ? currentTarget.interactionText : "";
        }
    }

    private void UpdateCrosshairUI()
    {
        if (crosshairImage == null) return;

        // Hide crosshair when UI keypad or panel is open so player only sees the mouse pointer
        if (hideCrosshairInUI && (DoorKeypad.IsAnyKeypadOpen || (PuzzleManager.Instance != null && PuzzleManager.Instance.IsAnyPanelOpen)))
        {
            crosshairImage.enabled = false;
            return;
        }

        crosshairImage.enabled = true;

        bool isTargeting = currentTarget != null;

        // Determine target color and scale
        Color targetColor = isTargeting ? interactCrosshairColor : defaultCrosshairColor;
        Vector3 targetScale = Vector3.one * (isTargeting ? interactScale : defaultScale);

        // Smooth visual transitions
        crosshairImage.color = Color.Lerp(crosshairImage.color, targetColor, Time.deltaTime * transitionSpeed);

        if (crosshairRect != null)
        {
            crosshairRect.localScale = Vector3.Lerp(crosshairRect.localScale, targetScale, Time.deltaTime * transitionSpeed);
        }

        // Optional sprite swap
        if (isTargeting && interactSprite != null)
        {
            crosshairImage.sprite = interactSprite;
        }
        else if (!isTargeting && defaultSprite != null)
        {
            crosshairImage.sprite = defaultSprite;
        }
    }

    private void HandleInteractionInput()
    {
        if (currentTarget == null) return;

        if (Input.GetKeyDown(KeyCode.E) || (Input.GetMouseButtonDown(0) && Cursor.lockState == CursorLockMode.Locked))
        {
            if (Time.frameCount != VRHandController.LastInteractFrame)
            {
                VRHandController.LastInteractFrame = Time.frameCount;
                currentTarget.Interact();
            }
        }
    }
}
