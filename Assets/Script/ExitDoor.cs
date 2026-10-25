using System.Collections;
using UnityEngine;


public class ExitDoor : MonoBehaviour, IInteractable
{
    [Header("Door State")]
    [Tooltip("By default, the door starts locked and requires the passcode.")]
    [SerializeField] private bool isLocked = true;
    [Tooltip("Is the door currently open? (false = closed)")]
    [SerializeField] private bool isOpen = false;

    public static event System.Action OnDoorUnlocked;

    [Header("Door Movement")]
    [Tooltip("The moving child door mesh that rotates (e.g. small_door)")]
    [SerializeField] private Transform doorTransform;
    [Tooltip("Local rotation offset when door is opened (e.g. 0, 90, 0)")]
    [SerializeField] private Vector3 openRotation = new Vector3(0f, 90f, 0f);
    [SerializeField] private float openSpeed = 2f;

    [Header("Door Keypad (Num Pad)")]
    [Tooltip("World-space interactive num pad on the door")]
    [SerializeField] private DoorKeypad doorKeypad;
    public DoorKeypad Keypad => doorKeypad;

    [Header("Audio (Optional)")]
    [SerializeField] private AudioSource doorAudioSource;
    [SerializeField] private AudioClip unlockSound;
    [SerializeField] private AudioClip openSound;
    [SerializeField] private AudioClip closeSound;

    // Physical doorway blocker and rotation state
    private Quaternion closedRotation;
    private Quaternion targetOpenRotation;
    private Coroutine doorCoroutine;
    private BoxCollider doorwayBlocker;

    private void Awake()
    {
        // Automatically find 'small_door' child if not assigned
        if (doorTransform == null)
        {
            Transform child = transform.Find("small_door");
            doorTransform = child != null ? child : transform;
        }

        // Remove any static non-convex MeshCollider from the rotating door mesh so it can swing open without PhysX lock
        if (doorTransform != null && doorTransform != transform)
        {
            MeshCollider mc = doorTransform.GetComponent<MeshCollider>();
            if (mc != null)
            {
                Destroy(mc);
            }
        }

        // 1. Ensure ExitDoor has an active BoxCollider for VR and desktop raycast interactions
        BoxCollider interactCol = GetComponent<BoxCollider>();
        if (interactCol == null)
        {
            interactCol = gameObject.AddComponent<BoxCollider>();
        }
        interactCol.enabled = true;
        interactCol.isTrigger = true; // Trigger so raycasts detect it, but it doesn't push the physics body
        interactCol.center = new Vector3(-0.6f, 1.1f, 0f);
        interactCol.size = new Vector3(1.1f, 2.3f, 0.15f); // 15cm thickness matching door frame

        // 2. Physical barrier to completely prevent player from walking through the doorway when closed
        Transform blockerChild = transform.Find("DoorwayPhysicalBarrier");
        if (blockerChild == null)
        {
            GameObject blockerObj = new GameObject("DoorwayPhysicalBarrier");
            blockerObj.transform.SetParent(transform, false);
            blockerObj.transform.localPosition = new Vector3(-0.6f, 1.1f, 0f);
            blockerObj.transform.localRotation = Quaternion.identity;
            blockerChild = blockerObj.transform;
        }

        doorwayBlocker = blockerChild.GetComponent<BoxCollider>();
        if (doorwayBlocker == null)
        {
            doorwayBlocker = blockerChild.gameObject.AddComponent<BoxCollider>();
        }
        doorwayBlocker.size = new Vector3(1.1f, 2.3f, 0.12f); // 12cm thick, fits perfectly inside the doorway frame
        doorwayBlocker.isTrigger = false; // SOLID barrier
        doorwayBlocker.enabled = !isOpen; // Active whenever closed

        // 3. Auto-find or create DoorKeypad on the door
        if (doorKeypad == null)
        {
            doorKeypad = GetComponentInChildren<DoorKeypad>();
            if (doorKeypad == null && isLocked)
            {
                GameObject kpObj = new GameObject("DoorKeypad");
                kpObj.transform.SetParent(transform, false);
                doorKeypad = kpObj.AddComponent<DoorKeypad>();
            }
        }

        // 4. closedRotation is the initial position
        closedRotation = doorTransform.localRotation;
        // 5. targetOpenRotation is offset by openRotation
        targetOpenRotation = closedRotation * Quaternion.Euler(openRotation);
    }

    /// <summary>
    /// Temporarily disables the general door trigger while the keypad is open so button raycasts aren't intercepted.
    /// </summary>
    public void SetKeypadOpen(bool open)
    {
        BoxCollider col = GetComponent<BoxCollider>();
        if (col != null)
        {
            col.enabled = !open;
        }
    }

    private void Start()
    {
        // Ensure initial rotation matches isOpen state
        doorTransform.localRotation = isOpen ? targetOpenRotation : closedRotation;
    }

    // 1. Interaction text shown on screen when player aims at the door
    public string interactionText
    {
        get
        {
            if (isLocked)
            {
                if (doorKeypad != null && doorKeypad.IsOpen)
                    return ""; // Keypad already open on door
                return "[E] Enter PIN (Num Pad)";
            }
            else if (isOpen)
            {
                return "[E] Close Door";
            }
            else
            {
                return "[E] Open Door";
            }
        }
    }

    // 2. What happens when the player presses 'E'
    public void Interact()
    {
        if (isLocked)
        {
            // Door is locked: show the num pad on the door!
            Debug.Log("<color=cyan>[ExitDoor] Door is locked. Showing num pad on door...</color>");
            if (doorKeypad == null)
            {
                doorKeypad = GetComponentInChildren<DoorKeypad>();
                if (doorKeypad == null)
                {
                    GameObject kpObj = new GameObject("DoorKeypad");
                    kpObj.transform.SetParent(transform, false);
                    doorKeypad = kpObj.AddComponent<DoorKeypad>();
                }
            }

            if (doorKeypad != null)
            {
                doorKeypad.OpenKeypad();
            }
            else if (PuzzleManager.Instance != null)
            {
                PuzzleManager.Instance.OpenLock();
            }
        }
        else
        {
            // Door is unlocked: toggle between Open and Close!
            if (isOpen)
            {
                CloseDoor();
            }
            else
            {
                OpenDoor();
            }
        }
    }

    // Called by PuzzleManager when the correct random PIN is entered
    public void Unlock()
    {
        Debug.Log("<color=green>[ExitDoor] Correct code entered! Door unlocked!</color>");
        isLocked = false;

        if (doorKeypad != null && doorKeypad.IsOpen)
        {
            doorKeypad.CloseKeypad();
        }

        OnDoorUnlocked?.Invoke();

        if (doorAudioSource != null && unlockSound != null)
        {
            doorAudioSource.PlayOneShot(unlockSound);
        }

        // Emit sound so ghost hears the keypad unlock beep/click
        Ghost.EmitSound(transform.position, 25f, "Door Keypad Unlock Sound");

        // Automatically open the door upon unlocking
        OpenDoor();
    }

    public void OpenDoor()
    {
        if (isOpen) return;
        isOpen = true;

        if (doorwayBlocker != null)
            doorwayBlocker.enabled = false;

        Transform barrier = transform.Find("DoorwayPhysicalBarrier");
        if (barrier != null)
        {
            Collider c = barrier.GetComponent<Collider>();
            if (c != null) c.enabled = false;
        }

        PlaySound(openSound);

        // Emit sound so ghost hears the door creaking/swinging open!
        Ghost.EmitSound(transform.position, 35f, "Door Open Sound");

        if (doorCoroutine != null)
            StopCoroutine(doorCoroutine);

        doorCoroutine = StartCoroutine(AnimateDoor(targetOpenRotation, "opened"));
    }

    public void CloseDoor()
    {
        if (!isOpen) return;
        isOpen = false;

        if (doorwayBlocker != null)
            doorwayBlocker.enabled = true;

        Transform barrier = transform.Find("DoorwayPhysicalBarrier");
        if (barrier != null)
        {
            Collider c = barrier.GetComponent<Collider>();
            if (c != null) c.enabled = true;
        }

        PlaySound(closeSound);

        // Emit sound so ghost hears the door slamming/shutting!
        Ghost.EmitSound(transform.position, 28f, "Door Close Sound");

        if (doorCoroutine != null)
            StopCoroutine(doorCoroutine);

        // Smoothly rotate back to the original closedRotation!
        doorCoroutine = StartCoroutine(AnimateDoor(closedRotation, "closed"));
    }

    // Single unified smooth animation coroutine for both opening and closing
    private IEnumerator AnimateDoor(Quaternion targetRot, string stateName)
    {
        Debug.Log($"<color=green>[ExitDoor] Door animating to {stateName}...</color>");
        Quaternion startRotation = doorTransform.localRotation;
        float duration = Mathf.Max(0.2f, 2f / openSpeed);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            t = Mathf.SmoothStep(0f, 1f, t);

            doorTransform.localRotation = Quaternion.Slerp(startRotation, targetRot, t);
            yield return null;
        }

        doorTransform.localRotation = targetRot;
        Debug.Log($"<color=green>[ExitDoor] Door fully {stateName}!</color>");
    }

    private void PlaySound(AudioClip clip)
    {
        if (doorAudioSource != null && clip != null)
        {
            doorAudioSource.PlayOneShot(clip);
        }
    }

    // Trigger escape when player walks through the open door!
    private void OnTriggerEnter(Collider other)
    {
        if (!isLocked && isOpen)
        {
            if (other.CompareTag("Player") || other.GetComponent<PlayerController>() != null)
            {
                TriggerEscape();
            }
        }
    }

    private void TriggerEscape()
    {
        Debug.Log("<color=cyan><b>[VICTORY!] You walked through the door and escaped the Forgotten Room!</b></color>");
    }

    // --- Testing Tools in Unity Inspector (Right-click script header) ---
    [ContextMenu("Test Toggle Door")]
    public void TestToggleDoor()
    {
        if (isOpen) CloseDoor();
        else OpenDoor();
    }

    [ContextMenu("Test Open Door")]
    public void TestOpenDoor()
    {
        isLocked = false;
        OpenDoor();
    }

    [ContextMenu("Test Close Door")]
    public void TestCloseDoor()
    {
        CloseDoor();
    }

    [ContextMenu("Test Reset to Locked")]
    public void TestResetToLocked()
    {
        isLocked = true;
        isOpen = false;
        if (doorTransform != null)
            doorTransform.localRotation = closedRotation;
        Debug.Log("[ExitDoor] Reset to Locked and Closed.");
    }

    // Public getters
    public bool IsLocked => isLocked;
    public bool IsOpen => isOpen;
}
