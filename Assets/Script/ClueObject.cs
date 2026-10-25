using UnityEngine;

public class ClueObject : MonoBehaviour, IInteractable
{
    public enum ClueRevealType
    {
        RevealFullCode = 0,   // Master Clue: Automatically reveals the complete 4-digit code (e.g. "The door code is 4829")
        RevealDigit1 = 1,     // Automatically reveals the 1st digit
        RevealDigit2 = 2,     // Automatically reveals the 2nd digit
        RevealDigit3 = 3,     // Automatically reveals the 3rd digit
        RevealDigit4 = 4,     // Automatically reveals the 4th digit
        CustomMessage = 5     // Regular inspection object: Shows custom text or ambient lore (NO clue code)
    }

    [Header("Clue Details")]
    [SerializeField] private string title = "Mysterious Note";
    [Tooltip("Choose whether this clue reveals the full code, a specific digit, or custom text. Note: When randomizeCluesEverySession is enabled in PuzzleManager, roles are assigned dynamically every session.")]
    [SerializeField] private ClueRevealType revealType = ClueRevealType.CustomMessage;

    [Header("Custom Message (Used if type is CustomMessage)")]
    [TextArea(2, 5)]
    [Tooltip("Write your custom lore/ambient message here. Note: CustomMessage will never reveal the door passcode.")]
    [SerializeField] private string message = "Nothing here but dust and old memories.";

    [Header("Optional Clue Inscription Override")]
    [TextArea(2, 4)]
    [Tooltip("Optional custom text when this object reveals a clue. If empty, PuzzleManager's standard template is used.")]
    [SerializeField] private string customClueTemplate = "";

    // Runtime session overrides assigned dynamically by PuzzleManager
    private string sessionAssignedTitle = null;
    private string sessionAssignedMessage = null;

    public string interactionText => "Investigate";
    public string Title => !string.IsNullOrEmpty(sessionAssignedTitle) ? sessionAssignedTitle : title;
    public ClueRevealType RevealType => revealType;
    public string Message => message;

    private void Awake()
    {
        // Guard against duplicate ClueObjects accidentally attached to the same GameObject
        ClueObject[] allOnThisObj = GetComponents<ClueObject>();
        if (allOnThisObj.Length > 1 && allOnThisObj[0] != this)
        {
            Debug.LogWarning($"[ClueObject] Duplicate ClueObject detected on '{gameObject.name}'. Disabling component #{System.Array.IndexOf(allOnThisObj, this)}.");
            enabled = false;
        }
    }

    /// <summary>
    /// Configures this clue's role, title, and message dynamically for this game session.
    /// </summary>
    public void SetSessionRole(ClueRevealType newType, string assignedTitle = null, string assignedMessage = null)
    {
        revealType = newType;
        sessionAssignedTitle = assignedTitle;
        sessionAssignedMessage = assignedMessage;
    }

    public void Interact()
    {
        string currentTitle = Title;
        Debug.Log($"[ClueObject] Investigating: '{currentTitle}' ({revealType}) on GameObject '{gameObject.name}'");

        if (PuzzleManager.Instance != null)
        {
            string messageToShow = GetMessageForRevealType();
            PuzzleManager.Instance.ShowClue(currentTitle, messageToShow, revealType);
        }
        else
        {
            Debug.LogError("[ClueObject] PuzzleManager.Instance is null! Ensure PuzzleManager is in the scene.");
        }
    }

    public string GetMessageForRevealType()
    {
        if (PuzzleManager.Instance == null)
            return GetSanitizedCustomMessage();

        // 1. If assigned a session-specific message by PuzzleManager, format and return it
        if (!string.IsNullOrEmpty(sessionAssignedMessage))
        {
            return PuzzleManager.Instance.FormatClueMessage(sessionAssignedMessage, revealType);
        }

        // 2. If this object has a custom clue template override, format and return it
        if (!string.IsNullOrEmpty(customClueTemplate))
        {
            return PuzzleManager.Instance.FormatClueMessage(customClueTemplate, revealType);
        }

        // 3. Otherwise, use PuzzleManager's standard templates according to revealType
        switch (revealType)
        {
            case ClueRevealType.RevealFullCode:
                return PuzzleManager.Instance.GetMasterClueMessage();

            case ClueRevealType.RevealDigit1:
                return PuzzleManager.Instance.GetDigitClueMessage(0);

            case ClueRevealType.RevealDigit2:
                return PuzzleManager.Instance.GetDigitClueMessage(1);

            case ClueRevealType.RevealDigit3:
                return PuzzleManager.Instance.GetDigitClueMessage(2);

            case ClueRevealType.RevealDigit4:
                return PuzzleManager.Instance.GetDigitClueMessage(3);

            case ClueRevealType.CustomMessage:
            default:
                return GetSanitizedCustomMessage();
        }
    }

    /// <summary>
    /// Returns the custom message, ensuring that ordinary objects never leak code or old default templates.
    /// </summary>
    public string GetSanitizedCustomMessage()
    {
        if (string.IsNullOrEmpty(message) || message.Contains("{CODE}") || message.Contains("{PIN}") || message.Contains("passcode is"))
        {
            if (PuzzleManager.Instance != null)
            {
                return PuzzleManager.Instance.GetFallbackAmbientMessage(gameObject.name);
            }
            return "Nothing here but dust and silence.";
        }
        return message;
    }
}
