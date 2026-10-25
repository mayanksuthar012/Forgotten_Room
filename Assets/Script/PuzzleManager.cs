using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class PuzzleManager : MonoBehaviour
{
    public static PuzzleManager Instance { get; private set; }

    [Header("Panels")]
    [SerializeField] private GameObject CluePanel;
    [SerializeField] private GameObject LockPanel;

    /// <summary>
    /// Returns true if CluePanel, LockPanel, or DoorKeypad is currently open.
    /// </summary>
    public bool IsAnyPanelOpen => (CluePanel != null && CluePanel.activeSelf) 
                               || (LockPanel != null && LockPanel.activeSelf) 
                               || (exitDoor != null && exitDoor.Keypad != null && exitDoor.Keypad.IsOpen);

    [Header("Clue UI")]
    [SerializeField] private TMP_Text ClueText;
    [SerializeField] private TMP_Text MessageText;

    [Header("Lock UI")]
    [SerializeField] private TMP_InputField codeInput;
    [SerializeField] private TMP_Text feedbackText;

    [Header("Puzzle Settings")]
    [Tooltip("If true, generates a brand-new random 4-digit PIN (1000-9999) every game session")]
    [SerializeField] private bool randomizeCode = true;
    [SerializeField] private string correctCode = "6115";
    [SerializeField] private ExitDoor exitDoor;

    [Header("Session Clue Randomization")]
    [Tooltip("If true, dynamically randomizes which objects hold the master clue, the 4 digit clues, and custom messages every session")]
    [SerializeField] private bool randomizeCluesEverySession = true;

    [Tooltip("If true, the master clue is chosen randomly from secret locations every session.")]
    [SerializeField] private bool randomizeMasterClue = true;

    [Header("Master Clue Secret Locations (Hidden Points)")]
    [Tooltip("Dedicated hidden Transform points where the full passcode clue is placed (e.g. hidden behind bookshelf, secret nook). The full passcode will ONLY appear at one of these secret points so players cannot easily stumble upon it!")]
    [SerializeField] private Transform[] masterClueSecretLocations;

    [Tooltip("The ClueObject to use as the Master Clue. If masterClueSecretLocations are assigned, this object will be moved to one of those secret points.")]
    [SerializeField] private ClueObject masterClueObject;

    [Tooltip("Optional alternative: Specific secret ClueObjects in the room (e.g. SecretNoteBehindBookshelf) that are eligible to hold the full passcode.")]
    [SerializeField] private ClueObject[] secretClueCandidates;

    [Tooltip("Optional: If assigned and randomizeMasterClue is false, this specific object will always hold the Master Clue.")]
    [SerializeField] private ClueObject designatedMasterClue;

    [Tooltip("If true, updates the displayed title of clue objects (e.g. 'Master Clue', 'Mysterious Note (Digit 1/4)'). If false, preserves the object's original title.")]
    [SerializeField] private bool updateClueTitles = false;

    [Header("Big Door Key Spawner (Prefab Support)")]
    [Tooltip("Drag your Key.prefab from Project Assets here. PuzzleManager will instantiate it at one of the scene spawn points!")]
    [SerializeField] private GameObject bigDoorKeyPrefab;
    [Tooltip("Drag scene Transform locations where the key can randomly spawn. (Because PuzzleManager is in the scene hierarchy, scene objects CAN be dragged here!)")]
    [SerializeField] private Transform[] keySpawnLocations;
    [Tooltip("Optional parent in scene containing key spawn points as children (e.g. 'KeySpawnLocations')")]
    [SerializeField] private Transform keySpawnPointsParent;

    [Header("Master Clue Template (Reveals Full 4-Digit Code)")]
    [SerializeField] private string masterClueTitle = "Mysterious Master Clue";
    [TextArea(2, 4)]
    [SerializeField] private string masterClueMessage = "A hidden master note reveals the complete lock code:\n<color=#FFE680><b>The door passcode is {CODE}</b></color>";

    [Header("Digit Clue Templates (Reveals 1 Digit Each)")]
    [SerializeField] private string digitClueTitle = "Mysterious Clue";
    [TextArea(2, 3)]
    [SerializeField] private string digit1Message = "A scratched inscription reads:\n'The 1st digit is <color=#70DBFF><b>{DIGIT_1}</b></color>'";
    [TextArea(2, 3)]
    [SerializeField] private string digit2Message = "A faint pencil mark on the wood:\n'The 2nd digit is <color=#70DBFF><b>{DIGIT_2}</b></color>'";
    [TextArea(2, 3)]
    [SerializeField] private string digit3Message = "Carved into the surface:\n'The 3rd digit is <color=#70DBFF><b>{DIGIT_3}</b></color>'";
    [TextArea(2, 3)]
    [SerializeField] private string digit4Message = "A torn scrap of paper reads:\n'The 4th digit is <color=#70DBFF><b>{DIGIT_4}</b></color>'";

    [Header("Ambient Custom Messages (For All Other Objects - NO Clue Code)")]
    [SerializeField] private string[] ambientMessages = new string[]
    {
        "Nothing here but dust and old memories.",
        "An antique piece of furniture. You search carefully, but find nothing useful.",
        "A broken vintage decoration. Nothing is written on it.",
        "You inspect it thoroughly, but there are no secret codes here.",
        "Just old wood covered in cobwebs. Nothing of interest.",
        "Faded scratches and worn varnish, but no numbers.",
        "An empty drawer with aged wallpaper behind it.",
        "Cold to the touch, but completely ordinary.",
        "You examine every angle, but find nothing helpful."
    };

    public string CorrectCode => correctCode;
    public ExitDoor ExitDoor => exitDoor;
    public bool IsDoorUnlocked => exitDoor != null && !exitDoor.IsLocked;

    public char GetDigit(int index)
    {
        if (index >= 0 && index < correctCode.Length)
            return correctCode[index];
        return '?';
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        // Generate a random 4-digit PIN every game session
        if (randomizeCode || correctCode == "6115")
        {
            GenerateRandomCode();
        }

        if (exitDoor == null)
        {
            exitDoor = FindAnyObjectByType<ExitDoor>();
        }
    }

    private void Start()
    {
        if (codeInput != null)
        {
            codeInput.onSubmit.AddListener(OnCodeInputSubmit);
        }

        // Distribute clue roles across the scene's objects for this session
        DistributeClues();

        // Spawn or place the Big Door Key at a random scene location
        SpawnBigDoorKey();
    }

    private void OnDestroy()
    {
        if (codeInput != null)
        {
            codeInput.onSubmit.RemoveListener(OnCodeInputSubmit);
        }
    }

    [ContextMenu("Generate New Random PIN")]
    public void GenerateRandomCode()
    {
        int randomPin = Random.Range(1000, 10000);
        correctCode = randomPin.ToString();
        Debug.Log($"<color=cyan><b>[PuzzleManager] 🎲 Generated New Random 4-Digit PIN: {correctCode}</b></color>");
    }

    [ContextMenu("Randomize Clues & Code (New Session)")]
    public void ResetSessionAndRandomize()
    {
        GenerateRandomCode();
        DistributeClues();
        SpawnBigDoorKey();
    }

    /// <summary>
    /// Spawns or relocates the physical BigDoorKey at one of the scene spawn points.
    /// Works with Key.prefab from Project Assets and scene Transforms on PuzzleManager!
    /// </summary>
    public void SpawnBigDoorKey()
    {
        List<Transform> validLocations = new List<Transform>();

        // 1. Check manually assigned locations on PuzzleManager
        if (keySpawnLocations != null && keySpawnLocations.Length > 0)
        {
            foreach (var loc in keySpawnLocations)
            {
                if (loc != null) validLocations.Add(loc);
            }
        }
        else if (keySpawnPointsParent != null && keySpawnPointsParent.childCount > 0)
        {
            foreach (Transform child in keySpawnPointsParent)
            {
                validLocations.Add(child);
            }
        }
        else
        {
            // Auto-detect a group object in the scene
            GameObject foundGroup = GameObject.Find("KeySpawnLocations") ?? GameObject.Find("KeySpawnPoints") ?? GameObject.Find("KeyLocations");
            if (foundGroup != null && foundGroup.transform.childCount > 0)
            {
                foreach (Transform child in foundGroup.transform)
                {
                    validLocations.Add(child);
                }
            }
        }

        if (validLocations.Count == 0)
        {
            return;
        }

        Transform chosenPoint = validLocations[Random.Range(0, validLocations.Count)];

        // Check if an instance of BigDoorKey already exists in the scene
        BigDoorKey existingKey = FindAnyObjectByType<BigDoorKey>();
        if (existingKey != null)
        {
            existingKey.transform.position = chosenPoint.position;
            existingKey.transform.rotation = chosenPoint.rotation;
            Debug.Log($"<color=yellow><b>[PuzzleManager] 🗝️ Relocated existing BigDoorKey to: '{chosenPoint.name}'</b></color>");
        }
        else if (bigDoorKeyPrefab != null)
        {
            GameObject newKey = Instantiate(bigDoorKeyPrefab, chosenPoint.position, chosenPoint.rotation);
            Debug.Log($"<color=yellow><b>[PuzzleManager] 🗝️ Spawned BigDoorKey prefab at: '{chosenPoint.name}'</b></color>");
        }
    }

    /// <summary>
    /// Randomizes clue distribution for the current game session:
    /// - Exactly ONE object is the Master Clue (reveals full code).
    /// - Exactly FOUR objects are Digit Clues (Digit 1, 2, 3, 4).
    /// - ALL other objects are Custom Messages (ambient lore/inspection, NO code).
    /// </summary>
    public void DistributeClues()
    {
        if (!randomizeCluesEverySession)
        {
            Debug.Log("[PuzzleManager] Clue randomization is disabled. Using scene defaults.");
            return;
        }

        // Find all active ClueObjects in the scene (modern Unity 6 API)
        ClueObject[] allClues = FindObjectsByType<ClueObject>(FindObjectsInactive.Exclude);
        List<ClueObject> cluePool = new List<ClueObject>();
        HashSet<GameObject> registeredObjects = new HashSet<GameObject>();

        foreach (var clue in allClues)
        {
            if (clue == null || !clue.gameObject.activeInHierarchy) continue;

            // Prevent duplicate components on the same GameObject from skewing clue distribution
            if (registeredObjects.Contains(clue.gameObject))
            {
                Debug.LogWarning($"[PuzzleManager] Duplicate ClueObject detected on '{clue.gameObject.name}'. Disabling extra component.");
                clue.enabled = false;
                continue;
            }

            registeredObjects.Add(clue.gameObject);
            cluePool.Add(clue);
        }

        if (cluePool.Count < 5)
        {
            Debug.LogWarning($"[PuzzleManager] Only found {cluePool.Count} unique ClueObjects. Need at least 5 (1 master + 4 digits) for full randomization.");
            return;
        }

        // Shuffle the clue pool randomly (Fisher-Yates)
        ShuffleList(cluePool);

        // 1. Assign Master Clue (reveals full code) - ONLY placed at designated secret points/candidates!
        ClueObject master = null;

        // Check if designated secret Transform points are assigned
        if (masterClueSecretLocations != null && masterClueSecretLocations.Length > 0)
        {
            // Pick a secret location at random
            int randSecretIdx = Random.Range(0, masterClueSecretLocations.Length);
            Transform secretPoint = masterClueSecretLocations[randSecretIdx];

            if (masterClueObject != null)
            {
                master = masterClueObject;
                cluePool.Remove(master);
            }
            else if (designatedMasterClue != null)
            {
                master = designatedMasterClue;
                cluePool.Remove(master);
            }
            else if (cluePool.Count > 0)
            {
                // Take one object to act as the hidden Master Clue
                master = cluePool[0];
                cluePool.RemoveAt(0);
            }

            if (master != null && secretPoint != null)
            {
                master.transform.position = secretPoint.position;
                master.transform.rotation = secretPoint.rotation;
                Debug.Log($"<color=yellow><b>[PuzzleManager] 👑 Master Clue (Full Passcode) placed at SECRET location: '{secretPoint.name}'!</b></color>");
            }
        }
        else if (secretClueCandidates != null && secretClueCandidates.Length > 0)
        {
            // Pick from specific secret clue candidates
            int randSecretIdx = Random.Range(0, secretClueCandidates.Length);
            master = secretClueCandidates[randSecretIdx];
            cluePool.Remove(master);
            Debug.Log($"<color=yellow><b>[PuzzleManager] 👑 Master Clue chosen from secret candidate: '{master.gameObject.name}'!</b></color>");
        }
        else if (!randomizeMasterClue && designatedMasterClue != null && cluePool.Contains(designatedMasterClue))
        {
            master = designatedMasterClue;
            cluePool.Remove(master);
        }
        else
        {
            // Fallback if no secret locations configured
            master = cluePool[0];
            cluePool.RemoveAt(0);
        }

        if (master != null)
        {
            master.SetSessionRole(
                ClueObject.ClueRevealType.RevealFullCode,
                updateClueTitles ? masterClueTitle : null,
                masterClueMessage
            );
            Debug.Log($"<color=yellow><b>[PuzzleManager] 👑 Master Clue assigned to: '{master.gameObject.name}' (Full Code: {correctCode})</b></color>");
        }

        // 2. Assign 4 Digit Clues (one digit each)
        ClueObject d1 = cluePool[0]; cluePool.RemoveAt(0);
        d1.SetSessionRole(
            ClueObject.ClueRevealType.RevealDigit1,
            updateClueTitles ? $"{digitClueTitle} (1/4)" : null,
            digit1Message
        );

        ClueObject d2 = cluePool[0]; cluePool.RemoveAt(0);
        d2.SetSessionRole(
            ClueObject.ClueRevealType.RevealDigit2,
            updateClueTitles ? $"{digitClueTitle} (2/4)" : null,
            digit2Message
        );

        ClueObject d3 = cluePool[0]; cluePool.RemoveAt(0);
        d3.SetSessionRole(
            ClueObject.ClueRevealType.RevealDigit3,
            updateClueTitles ? $"{digitClueTitle} (3/4)" : null,
            digit3Message
        );

        ClueObject d4 = cluePool[0]; cluePool.RemoveAt(0);
        d4.SetSessionRole(
            ClueObject.ClueRevealType.RevealDigit4,
            updateClueTitles ? $"{digitClueTitle} (4/4)" : null,
            digit4Message
        );

        Debug.Log($"<color=cyan>[PuzzleManager] 🔢 Clue Digit 1 -> '{d1.gameObject.name}' (Value: {GetDigit(0)})</color>");
        Debug.Log($"<color=cyan>[PuzzleManager] 🔢 Clue Digit 2 -> '{d2.gameObject.name}' (Value: {GetDigit(1)})</color>");
        Debug.Log($"<color=cyan>[PuzzleManager] 🔢 Clue Digit 3 -> '{d3.gameObject.name}' (Value: {GetDigit(2)})</color>");
        Debug.Log($"<color=cyan>[PuzzleManager] 🔢 Clue Digit 4 -> '{d4.gameObject.name}' (Value: {GetDigit(3)})</color>");

        // 3. Assign ALL remaining clues to CustomMessage (ambient lore / inspect, NO clue code)
        int ambientIdx = 0;
        foreach (var remaining in cluePool)
        {
            string ambientMsg = null;
            // If the message is empty or contains an old code placeholder, provide a fresh atmospheric message
            if (string.IsNullOrEmpty(remaining.Message) ||
                remaining.Message.Contains("{CODE}") ||
                remaining.Message.Contains("{PIN}") ||
                remaining.Message.Contains("passcode is"))
            {
                ambientMsg = ambientMessages.Length > 0 ? ambientMessages[ambientIdx % ambientMessages.Length] : "Nothing here but dust.";
                ambientIdx++;
            }

            remaining.SetSessionRole(ClueObject.ClueRevealType.CustomMessage, null, ambientMsg);
        }

        Debug.Log($"<color=grey>[PuzzleManager] 📦 Assigned {cluePool.Count} objects as Custom Message (no clue code).</color>");
    }

    private void ShuffleList<T>(List<T> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            int randIndex = Random.Range(i, list.Count);
            T temp = list[i];
            list[i] = list[randIndex];
            list[randIndex] = temp;
        }
    }

    public string GetMasterClueMessage()
    {
        return FormatClueMessage(masterClueMessage, ClueObject.ClueRevealType.RevealFullCode);
    }

    public string GetDigitClueMessage(int index)
    {
        switch (index)
        {
            case 0: return FormatClueMessage(digit1Message, ClueObject.ClueRevealType.RevealDigit1);
            case 1: return FormatClueMessage(digit2Message, ClueObject.ClueRevealType.RevealDigit2);
            case 2: return FormatClueMessage(digit3Message, ClueObject.ClueRevealType.RevealDigit3);
            case 3: return FormatClueMessage(digit4Message, ClueObject.ClueRevealType.RevealDigit4);
            default: return $"Digit: {GetDigit(index)}";
        }
    }

    public string GetFallbackAmbientMessage(string objectName = null)
    {
        if (ambientMessages != null && ambientMessages.Length > 0)
        {
            int randIdx = Random.Range(0, ambientMessages.Length);
            return ambientMessages[randIdx];
        }
        return "Nothing here but dust and silence.";
    }

    private void OnCodeInputSubmit(string text)
    {
        SubmitCode();
    }

    public void ShowClue(string title, string message)
    {
        ShowClue(title, message, ClueObject.ClueRevealType.CustomMessage);
    }

    public void ShowClue(string title, string message, ClueObject.ClueRevealType revealType)
    {
        ClosePanel();

        // Dynamically format message while strictly preventing CustomMessage from leaking the code
        string formattedMessage = FormatClueMessage(message, revealType);

        if (ClueText != null)
        {
            ClueText.text = title;
        }
        if (MessageText != null)
        {
            MessageText.text = formattedMessage;
        }
        if (CluePanel != null)
        {
            CluePanel.SetActive(true);
        }

        // Unlock cursor so player can read and close UI
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public string FormatClueMessage(string rawMessage, ClueObject.ClueRevealType revealType = ClueObject.ClueRevealType.RevealFullCode)
    {
        if (string.IsNullOrEmpty(rawMessage))
            return "";

        // CustomMessage must NEVER reveal the PIN or code
        if (revealType == ClueObject.ClueRevealType.CustomMessage)
        {
            if (rawMessage.Contains("{CODE}") || rawMessage.Contains("{PIN}") || rawMessage.Contains("passcode is"))
            {
                return GetFallbackAmbientMessage();
            }
            return rawMessage;
        }

        return rawMessage.Replace("{CODE}", correctCode)
                         .Replace("{PIN}", correctCode)
                         .Replace("{DIGIT_1}", correctCode.Length > 0 ? correctCode[0].ToString() : "?")
                         .Replace("{DIGIT_2}", correctCode.Length > 1 ? correctCode[1].ToString() : "?")
                         .Replace("{DIGIT_3}", correctCode.Length > 2 ? correctCode[2].ToString() : "?")
                         .Replace("{DIGIT_4}", correctCode.Length > 3 ? correctCode[3].ToString() : "?");
    }

    public void OpenLock()
    {
        ClosePanel();

        if (exitDoor == null)
        {
            exitDoor = FindAnyObjectByType<ExitDoor>();
        }

        // If ExitDoor has a World-Space DoorKeypad, open the num pad directly on the door!
        if (exitDoor != null && exitDoor.Keypad != null)
        {
            exitDoor.Keypad.OpenKeypad();
            return;
        }

        if (LockPanel != null)
        {
            LockPanel.SetActive(true);
        }
        if (codeInput != null)
        {
            codeInput.text = "";
            codeInput.Select();
            codeInput.ActivateInputField();
        }
        if (feedbackText != null)
        {
            feedbackText.text = "Enter four-digit Code";
        }

        // Unlock cursor so player can type in keypad
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void Update()
    {
        // When LockPanel is open, pressing Enter submits the code
        if (LockPanel != null && LockPanel.activeSelf)
        {
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                SubmitCode();
            }
        }

        // Close any open panel when Escape key is pressed (Desktop fallback)
        if (Input.GetKeyDown(KeyCode.Escape) && IsAnyPanelOpen)
        {
            ClosePanel();
        }
    }

    /// <summary>
    /// Verifies a PIN string (called from DoorKeypad). Returns true if correct and unlocks door.
    /// </summary>
    public bool CheckCode(string enteredCode)
    {
        enteredCode = (enteredCode ?? "").Trim();
        Debug.Log($"[PuzzleManager] Verifying PIN: '{enteredCode}' (Expected: '{correctCode}')");

        if (enteredCode == correctCode)
        {
            Debug.Log("<color=green>[PuzzleManager] PIN Correct! Unlocking door...</color>");
            UnlockExitDoor();
            return true;
        }
        else
        {
            Debug.LogWarning($"<color=orange>[PuzzleManager] Incorrect PIN: '{enteredCode}'</color>");
            return false;
        }
    }

    public void SubmitCode()
    {
        if (codeInput == null)
        {
            Debug.LogError("[PuzzleManager] codeInput reference is null!");
            return;
        }

        string enteredCode = codeInput.text.Trim();
        Debug.Log($"[PuzzleManager] Submitting code: '{enteredCode}' (Expected: '{correctCode}')");

        if (enteredCode == correctCode)
        {
            Debug.Log("<color=green>[PuzzleManager] Code Correct! Unlocking door...</color>");
            if (feedbackText != null)
            {
                feedbackText.text = "Code Success";
            }

            UnlockExitDoor();
        }
        else
        {
            Debug.LogWarning($"<color=orange>[PuzzleManager] Incorrect Code: '{enteredCode}'</color>");
            if (feedbackText != null)
            {
                feedbackText.text = "Incorrect Code";
            }
        }
    }

    public void ClosePanel()
    {
        if (CluePanel != null)
        {
            CluePanel.SetActive(false);
        }
        if (LockPanel != null)
        {
            LockPanel.SetActive(false);
        }
        if (exitDoor != null && exitDoor.Keypad != null && exitDoor.Keypad.IsOpen)
        {
            exitDoor.Keypad.CloseKeypad();
        }

        // Lock cursor back to gameplay
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void UnlockExitDoor()
    {
        if (exitDoor == null)
        {
            exitDoor = FindAnyObjectByType<ExitDoor>();
        }

        if (exitDoor != null)
        {
            exitDoor.Unlock();
            ClosePanel();
        }
    }

    public void CloseLockPanel()
    {
        if (LockPanel != null)
        {
            LockPanel.SetActive(false);
        }
        CheckCursorAfterClose();
    }

    public void CloseCluePanel()
    {
        if (CluePanel != null)
        {
            CluePanel.SetActive(false);
        }
        CheckCursorAfterClose();
    }

    private void CheckCursorAfterClose()
    {
        bool anyPanelOpen = (CluePanel != null && CluePanel.activeSelf) || (LockPanel != null && LockPanel.activeSelf);
        if (!anyPanelOpen)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
}
