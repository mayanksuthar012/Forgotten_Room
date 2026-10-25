using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// A sleek, interactive World-Space Num Pad mounted directly on the exit door.
/// Displays an LED screen with 4-digit PIN slots, 12 interactive buttons (0-9, CLEAR, OK),
/// audio feedback beeps, and visual status indicators (GRANTED / DENIED).
/// </summary>
public class DoorKeypad : MonoBehaviour
{
    [Header("Door & Target")]
    [SerializeField] private ExitDoor exitDoor;

    [Header("Placement Settings")]
    [Tooltip("Local offset on the door frame/handle at eye level")]
    [SerializeField] private Vector3 defaultLocalOffset = new Vector3(-0.60f, 1.35f, 0f);
    [SerializeField] private float worldScale = 0.0012f;
    [SerializeField] private float autoCloseDistance = 6.5f;

    [Header("Audio Feedback (Optional Custom Clips)")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip beepClip;
    [SerializeField] private AudioClip grantedClip;
    [SerializeField] private AudioClip deniedClip;

    [Header("Colors & Styling")]
    [SerializeField] private Color casingColor = new Color(0.08f, 0.10f, 0.15f, 0.95f);
    [SerializeField] private Color screenBgColor = new Color(0.03f, 0.05f, 0.08f, 1.0f);
    [SerializeField] private Color buttonNormalColor = new Color(0.12f, 0.16f, 0.23f, 1.0f);
    [SerializeField] private Color buttonHoverColor = new Color(0.02f, 0.52f, 0.78f, 1.0f);
    [SerializeField] private Color clearBtnColor = new Color(0.35f, 0.12f, 0.12f, 1.0f);
    [SerializeField] private Color clearHoverColor = new Color(0.85f, 0.25f, 0.25f, 1.0f);
    [SerializeField] private Color okBtnColor = new Color(0.10f, 0.32f, 0.18f, 1.0f);
    [SerializeField] private Color okHoverColor = new Color(0.15f, 0.70f, 0.35f, 1.0f);

    private const int MAX_DIGITS = 4;
    private string currentInput = "";
    private bool isOpen = false;
    private bool isProcessing = false;
    private Canvas canvas;
    private TMP_Text statusText;
    private TMP_Text codeDisplayText;
    private List<KeypadButton> buttons = new List<KeypadButton>();
    private Transform playerTransform;

    public bool IsOpen => isOpen;
    public static bool IsAnyKeypadOpen { get; private set; } = false;

    public static void CloseAnyActiveKeypad()
    {
        DoorKeypad[] all = FindObjectsByType<DoorKeypad>(FindObjectsInactive.Exclude);
        foreach (var kp in all)
        {
            if (kp != null && kp.IsOpen)
            {
                kp.CloseKeypad();
            }
        }
    }

    private void Awake()
    {
        if (exitDoor == null)
            exitDoor = GetComponentInParent<ExitDoor>();

        SetupAudio();
        BuildKeypadUI();

        // Start closed by default
        if (canvas != null)
        {
            canvas.gameObject.SetActive(false);
        }
        isOpen = false;
        IsAnyKeypadOpen = false;
    }

    private void OnDisable()
    {
        isOpen = false;
        IsAnyKeypadOpen = false;
    }

    private void Start()
    {
        if (Camera.main != null)
        {
            playerTransform = Camera.main.transform;
        }
    }

    private void Update()
    {
        if (!isOpen) return;

        // Auto close if player walks away
        if (playerTransform != null && Vector3.Distance(transform.position, playerTransform.position) > autoCloseDistance)
        {
            CloseKeypad();
            return;
        }

        if (isProcessing) return;

        // Desktop Keyboard Support (0-9, Backspace, Enter, Esc)
        HandleKeyboardInput();

        // Desktop Mouse Click Support
        HandleMouseClickInput();
    }

    private void HandleKeyboardInput()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            CloseKeypad();
            return;
        }

        // Digits 0 to 9
        for (int i = 0; i <= 9; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha0 + i) || Input.GetKeyDown(KeyCode.Keypad0 + i))
            {
                OnButtonPressed(i.ToString());
                return;
            }
        }

        // Backspace / Delete
        if (Input.GetKeyDown(KeyCode.Backspace) || Input.GetKeyDown(KeyCode.Delete))
        {
            OnButtonPressed("CLEAR");
            return;
        }

        // Enter / Return
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            OnButtonPressed("OK");
            return;
        }
    }

    private KeypadButton mouseHoveredButton = null;

    private void HandleMouseClickInput()
    {
        if (VRDeviceDetection.AreVRControllersActive()) return;

        Camera cam = Camera.main;
        if (cam == null) cam = FindAnyObjectByType<Camera>();
        if (cam == null) return;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        RaycastHit[] hits = Physics.RaycastAll(ray, 6.0f);
        KeypadButton bestBtn = null;
        float closestDist = float.MaxValue;
        foreach (var hit in hits)
        {
            KeypadButton btn = hit.collider.GetComponent<KeypadButton>() ?? hit.collider.GetComponentInParent<KeypadButton>();
            if (btn != null && hit.distance < closestDist)
            {
                bestBtn = btn;
                closestDist = hit.distance;
            }
        }

        // Manage mouse hover visual glow
        if (mouseHoveredButton != bestBtn)
        {
            if (mouseHoveredButton != null) mouseHoveredButton.SetHover(false);
            mouseHoveredButton = bestBtn;
            if (mouseHoveredButton != null) mouseHoveredButton.SetHover(true);
        }

        // On left click down
        if (Input.GetMouseButtonDown(0) && bestBtn != null)
        {
            bestBtn.Interact();
        }
    }

    /// <summary>
    /// Opens the door num pad: positions it facing the player at eye level on the door,
    /// illuminates the LED screen, and resets the PIN display.
    /// </summary>
    public void OpenKeypad()
    {
        isOpen = true;
        IsAnyKeypadOpen = true;
        isProcessing = false;
        currentInput = "";

        if (exitDoor != null)
        {
            exitDoor.SetKeypadOpen(true);
        }

        if (playerTransform == null)
        {
            Camera cam = Camera.main;
            if (cam == null) cam = FindAnyObjectByType<Camera>();
            if (cam != null) playerTransform = cam.transform;
        }

        AlignToPlayerFacing();

        if (canvas != null)
        {
            if (canvas.worldCamera == null)
            {
                Camera cam = Camera.main;
                if (cam == null) cam = FindAnyObjectByType<Camera>();
                canvas.worldCamera = cam;
            }
            canvas.sortingOrder = 50;
            canvas.gameObject.SetActive(true);
        }

        UpdateDisplay("ENTER 4-DIGIT PIN", FormatPinDisplay(""), new Color(0.22f, 0.85f, 1.0f));
        PlaySound(beepClip, 1200f, 0.05f);

        // Unlock mouse cursor if playing on Desktop
        if (!VRDeviceDetection.IsVRActive())
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        Debug.Log("<color=cyan>[DoorKeypad] Num pad opened on door.</color>");
    }

    /// <summary>
    /// Closes and hides the door num pad, restoring cursor lock.
    /// </summary>
    public void CloseKeypad()
    {
        if (!isOpen) return;

        isOpen = false;
        IsAnyKeypadOpen = false;
        isProcessing = false;

        if (exitDoor != null)
        {
            exitDoor.SetKeypadOpen(false);
        }

        if (canvas != null)
        {
            canvas.gameObject.SetActive(false);
        }

        if (mouseHoveredButton != null)
        {
            mouseHoveredButton.SetHover(false);
            mouseHoveredButton = null;
        }

        // Restore cursor lock if on desktop
        if (!VRDeviceDetection.IsVRActive())
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        Debug.Log("[DoorKeypad] Num pad closed.");
    }

    /// <summary>
    /// Positions and orients the keypad cleanly in front of the door lock, facing the player directly.
    /// In Unity, a World-Space Canvas displays its front on the local -Z axis.
    /// Setting transform.forward to -toCamHorizontal ensures the front face points directly at the player,
    /// and local +X matches camera right (100% un-mirrored and crystal clear).
    /// </summary>
    private void AlignToPlayerFacing()
    {
        Camera cam = Camera.main;
        if (cam == null) cam = FindAnyObjectByType<Camera>();
        if (cam == null) return;

        Transform parentTransform = transform.parent != null ? transform.parent : transform;

        // Base door lock position in world space
        Vector3 doorLockPoint = parentTransform.TransformPoint(new Vector3(defaultLocalOffset.x, defaultLocalOffset.y, 0f));

        Vector3 toCam = cam.transform.position - doorLockPoint;
        Vector3 toCamHorizontal = new Vector3(toCam.x, 0f, toCam.z).normalized;

        if (toCamHorizontal.sqrMagnitude < 0.001f)
            toCamHorizontal = parentTransform.forward;

        // Position the keypad 18 cm in front of the door surface towards the player
        // This guarantees the keypad is completely in clear open space and never occluded by door/wall colliders
        transform.position = doorLockPoint + toCamHorizontal * 0.18f;

        // In Unity, a World Space Canvas renders on its local -Z face.
        // Therefore, pointing forward AWAY from the camera (-toCamHorizontal) makes the front face directly point AT the camera!
        transform.rotation = Quaternion.LookRotation(-toCamHorizontal, Vector3.up);

        // Compensate for parent transform lossyScale so world scale is exactly worldScale (0.0012f)
        Vector3 pScale = parentTransform.lossyScale;
        float sx = pScale.x > 0.0001f ? worldScale / pScale.x : worldScale;
        float sy = pScale.y > 0.0001f ? worldScale / pScale.y : worldScale;
        float sz = pScale.z > 0.0001f ? worldScale / pScale.z : worldScale;
        transform.localScale = new Vector3(sx, sy, sz);
    }

    /// <summary>
    /// Called when any button on the num pad is pressed (via VR laser, mouse click, or keyboard).
    /// </summary>
    public void OnButtonPressed(string value)
    {
        if (isProcessing) return;

        if (value == "CLEAR")
        {
            PlaySound(beepClip, 800f, 0.06f);
            if (currentInput.Length > 0)
            {
                currentInput = currentInput.Substring(0, currentInput.Length - 1);
            }
            UpdateDisplay("ENTER 4-DIGIT PIN", FormatPinDisplay(currentInput), new Color(0.22f, 0.85f, 1.0f));
            return;
        }

        if (value == "OK")
        {
            if (currentInput.Length == MAX_DIGITS)
            {
                StartCoroutine(SubmitPinRoutine());
            }
            else
            {
                PlaySound(deniedClip, 280f, 0.18f);
                UpdateDisplay("ENTER ALL 4 DIGITS", FormatPinDisplay(currentInput), new Color(0.95f, 0.5f, 0.2f));
            }
            return;
        }

        // Numerical Digits (0 - 9)
        if (currentInput.Length < MAX_DIGITS)
        {
            currentInput += value;
            PlaySound(beepClip, 1100f + (currentInput.Length * 120f), 0.05f);
            UpdateDisplay("ENTER 4-DIGIT PIN", FormatPinDisplay(currentInput), new Color(0.22f, 0.85f, 1.0f));

            // Auto-submit smoothly when all 4 digits are entered
            if (currentInput.Length == MAX_DIGITS)
            {
                StartCoroutine(SubmitPinRoutine());
            }
        }
    }

    private string FormatPinDisplay(string input)
    {
        string display = "";
        for (int i = 0; i < MAX_DIGITS; i++)
        {
            if (i < input.Length)
            {
                display += $"{input[i]}  ";
            }
            else
            {
                display += "_  ";
            }
        }
        return display.TrimEnd();
    }

    private void UpdateDisplay(string status, string code, Color statusColor)
    {
        if (statusText != null)
        {
            statusText.text = status;
            statusText.color = statusColor;
        }
        if (codeDisplayText != null)
        {
            codeDisplayText.text = code;
        }
    }

    private IEnumerator SubmitPinRoutine()
    {
        isProcessing = true;
        UpdateDisplay("VERIFYING...", FormatPinDisplay(currentInput), Color.yellow);
        yield return new WaitForSeconds(0.35f);

        bool isCorrect = false;

        if (PuzzleManager.Instance != null)
        {
            isCorrect = PuzzleManager.Instance.CheckCode(currentInput);
        }
        else
        {
            isCorrect = (currentInput == "6115");
        }

        if (isCorrect)
        {
            // ACCESS GRANTED
            UpdateDisplay("ACCESS GRANTED", "<color=#22C55E>UNLOCKED</color>", new Color(0.15f, 0.85f, 0.35f));
            PlaySound(grantedClip, 1400f, 0.35f);

            // Alert ghost to the keypad unlock sound
            Ghost.EmitSound(transform.position, 25f, "Door Keypad Unlock");

            yield return new WaitForSeconds(0.65f);

            if (exitDoor != null)
            {
                exitDoor.Unlock();
            }

            CloseKeypad();
        }
        else
        {
            // ACCESS DENIED
            UpdateDisplay("ACCESS DENIED", "<color=#EF4444>ERROR</color>", new Color(0.95f, 0.25f, 0.25f));
            PlaySound(deniedClip, 240f, 0.30f);

            yield return new WaitForSeconds(1.1f);

            currentInput = "";
            UpdateDisplay("ENTER 4-DIGIT PIN", FormatPinDisplay(""), new Color(0.22f, 0.85f, 1.0f));
            isProcessing = false;
        }
    }

    private void SetupAudio()
    {
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1.0f; // 3D sound on the door
            audioSource.maxDistance = 15f;
            audioSource.rolloffMode = AudioRolloffMode.Linear;
        }

        // Procedural fallback clips if none assigned
        if (beepClip == null) beepClip = GenerateToneClip(1200f, 0.05f);
        if (grantedClip == null) grantedClip = GenerateToneClip(1500f, 0.3f);
        if (deniedClip == null) deniedClip = GenerateToneClip(250f, 0.35f);
    }

    private void PlaySound(AudioClip clip, float fallbackFreq, float fallbackDur)
    {
        if (audioSource == null) return;

        if (clip != null)
        {
            audioSource.PlayOneShot(clip);
        }
        else
        {
            audioSource.PlayOneShot(GenerateToneClip(fallbackFreq, fallbackDur));
        }
    }

    private AudioClip GenerateToneClip(float frequency, float duration)
    {
        int sampleRate = 44100;
        int sampleCount = (int)(sampleRate * duration);
        float[] samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / sampleRate;
            float envelope = Mathf.Sin((float)i / sampleCount * Mathf.PI);
            samples[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * envelope * 0.35f;
        }

        AudioClip clip = AudioClip.Create("KeypadTone", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    /// <summary>
    /// Procedurally constructs a modern, high-res World-Space Canvas num pad on the door.
    /// </summary>
    private void BuildKeypadUI()
    {
        // 1. Root World Space Canvas
        GameObject canvasObj = new GameObject("KeypadCanvas");
        canvasObj.transform.SetParent(transform, false);
        canvasObj.transform.localPosition = Vector3.zero;
        canvasObj.transform.localRotation = Quaternion.identity;
        canvasObj.transform.localScale = Vector3.one;

        canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        Camera cam = Camera.main;
        if (cam == null) cam = FindAnyObjectByType<Camera>();
        canvas.worldCamera = cam;
        canvas.sortingOrder = 50;
        canvasObj.AddComponent<GraphicRaycaster>();

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10;

        RectTransform canvasRect = canvasObj.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(360f, 490f);

        // 2. Main Casing Panel
        GameObject casingObj = new GameObject("Casing");
        casingObj.transform.SetParent(canvasObj.transform, false);
        RectTransform casingRect = casingObj.AddComponent<RectTransform>();
        casingRect.anchorMin = Vector2.zero;
        casingRect.anchorMax = Vector2.one;
        casingRect.sizeDelta = Vector2.zero;

        Image casingImage = casingObj.AddComponent<Image>();
        casingImage.color = casingColor;

        // 3. Header Text
        CreateText(casingObj.transform, "SECURITY ACCESS", new Vector2(0f, 212f), new Vector2(320f, 30f), 13f, FontStyles.Bold, new Color(0.55f, 0.65f, 0.78f));

        // 4. LED Display Screen Panel
        GameObject screenObj = new GameObject("DisplayScreen");
        screenObj.transform.SetParent(casingObj.transform, false);
        RectTransform screenRect = screenObj.AddComponent<RectTransform>();
        screenRect.anchoredPosition = new Vector2(0f, 150f);
        screenRect.sizeDelta = new Vector2(310f, 75f);

        Image screenImage = screenObj.AddComponent<Image>();
        screenImage.color = screenBgColor;

        // Status Label (e.g. "ENTER 4-DIGIT PIN")
        statusText = CreateText(screenObj.transform, "ENTER 4-DIGIT PIN", new Vector2(0f, 20f), new Vector2(290f, 22f), 12f, FontStyles.Normal, new Color(0.22f, 0.85f, 1.0f));

        // PIN Slot Digits (e.g. "_  _  _  _")
        codeDisplayText = CreateText(screenObj.transform, "_  _  _  _", new Vector2(0f, -12f), new Vector2(290f, 36f), 28f, FontStyles.Bold, Color.white);

        // 5. 12 Numpad Buttons (3x4 Grid)
        string[,] grid = new string[,]
        {
            { "1", "2", "3" },
            { "4", "5", "6" },
            { "7", "8", "9" },
            { "CLEAR", "0", "OK" }
        };

        float startX = -96f;
        float startY = 70f;
        float spacingX = 96f;
        float spacingY = 72f;

        for (int r = 0; r < 4; r++)
        {
            for (int c = 0; c < 3; c++)
            {
                string label = grid[r, c];
                Vector2 pos = new Vector2(startX + (c * spacingX), startY - (r * spacingY));

                Color bg = buttonNormalColor;
                Color hover = buttonHoverColor;

                if (label == "CLEAR")
                {
                    bg = clearBtnColor;
                    hover = clearHoverColor;
                }
                else if (label == "OK")
                {
                    bg = okBtnColor;
                    hover = okHoverColor;
                }

                CreateKeypadButton(casingObj.transform, label, pos, new Vector2(84f, 58f), bg, hover);
            }
        }

        // 6. Footer Hint
        CreateText(casingObj.transform, "Squeeze Grip / ESC to Exit", new Vector2(0f, -225f), new Vector2(320f, 24f), 11f, FontStyles.Normal, new Color(0.40f, 0.48f, 0.58f));
    }

    private void CreateKeypadButton(Transform parent, string label, Vector2 pos, Vector2 size, Color bgColor, Color hoverColor)
    {
        GameObject btnObj = new GameObject($"Btn_{label}");
        btnObj.transform.SetParent(parent, false);

        RectTransform rect = btnObj.AddComponent<RectTransform>();
        rect.anchoredPosition = pos;
        rect.sizeDelta = size;

        Image img = btnObj.AddComponent<Image>();
        img.color = bgColor;

        // Add BoxCollider so Physics Raycast hits it directly in VR & Desktop
        BoxCollider collider = btnObj.AddComponent<BoxCollider>();
        collider.size = new Vector3(size.x, size.y, 30.0f);
        collider.center = new Vector3(0f, 0f, -10.0f);
        collider.isTrigger = false;

        // Add KeypadButton script (implements IInteractable)
        KeypadButton btnScript = btnObj.AddComponent<KeypadButton>();
        btnScript.Setup(this, label, bgColor, hoverColor);
        buttons.Add(btnScript);

        // Button Text Label
        float fontSize = (label == "CLEAR" || label == "OK") ? 14f : 24f;
        Color textColor = (label == "CLEAR") ? new Color(0.98f, 0.45f, 0.45f) : ((label == "OK") ? new Color(0.35f, 0.95f, 0.55f) : Color.white);
        CreateText(btnObj.transform, label, Vector2.zero, size, fontSize, FontStyles.Bold, textColor);
    }

    private TMP_Text CreateText(Transform parent, string content, Vector2 pos, Vector2 size, float fontSize, FontStyles style, Color color)
    {
        GameObject textObj = new GameObject("Label");
        textObj.transform.SetParent(parent, false);

        RectTransform rect = textObj.AddComponent<RectTransform>();
        rect.anchoredPosition = pos;
        rect.sizeDelta = size;

        TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.text = content;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;

        return tmp;
    }
}
