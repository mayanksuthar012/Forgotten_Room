using System.Collections;
using UnityEngine;

public class BigDoor : MonoBehaviour, IInteractable
{
    [Header("Door State")]
    [Tooltip("Starts locked, requiring the BigDoorKey to unlock")]
    [SerializeField] private bool isLocked = true;
    [Tooltip("Is the door currently open?")]
    [SerializeField] private bool isOpen = false;

    [Header("Door Mesh Transforms")]
    [Tooltip("Left door mesh that rotates (auto-finds child 'big_door_left' if null)")]
    [SerializeField] private Transform leftDoorTransform;
    [Tooltip("Right door mesh that rotates (auto-finds child 'big_door_right' if null)")]
    [SerializeField] private Transform rightDoorTransform;

    [Header("Open Angles & Speed")]
    [Tooltip("Rotation offset for left door when opened (e.g. 0, -90, 0)")]
    [SerializeField] private Vector3 leftOpenRotation = new Vector3(0f, -90f, 0f);
    [Tooltip("Rotation offset for right door when opened (e.g. 0, 90, 0)")]
    [SerializeField] private Vector3 rightOpenRotation = new Vector3(0f, 90f, 0f);
    [SerializeField] private float openSpeed = 2f;

    [Header("Key Injection & Animation")]
    [Tooltip("Transform position on the door representing the keyhole where the key will be injected/shown")]
    [SerializeField] private Transform keyholeTransform;
    [Tooltip("Optional Animator component to trigger the key insertion / unlock animation")]
    [SerializeField] private Animator doorAnimator;
    [Tooltip("Parameter name in the Animator to trigger key insertion")]
    [SerializeField] private string keyInsertTriggerName = "InsertKey";
    [Tooltip("Delay in seconds to allow the key injection animation to play before door swings open")]
    [SerializeField] private float keyInsertAnimationDuration = 1.0f;

    [Header("Audio (Optional)")]
    [SerializeField] private AudioSource doorAudioSource;
    [SerializeField] private AudioClip keyInsertSound;
    [SerializeField] private AudioClip unlockSound;
    [SerializeField] private AudioClip openSound;
    [SerializeField] private AudioClip closeSound;

    [Header("UI Feedback")]
    [SerializeField] private string lockedMessage = "The large double door is locked tight. It needs a physical key.";
    [SerializeField] private string unlockedMessage = "You turn the heavy key in the lock... Click! The big door unlocks.";

    private Quaternion leftClosedRotation;
    private Quaternion leftTargetOpenRotation;
    private Quaternion rightClosedRotation;
    private Quaternion rightTargetOpenRotation;
    private Coroutine doorCoroutine;
    private bool isUnlockingSequence = false;
    private BoxCollider doorwayBlocker;

    public bool IsLocked => isLocked;
    public bool IsOpen => isOpen;

    public static event System.Action OnBigDoorUnlocked;
    public static event System.Action OnKeyInjected;

    private void Awake()
    {
        // Auto-detect double doors from standard prefab hierarchy
        if (leftDoorTransform == null)
        {
            Transform left = transform.Find("big_door_left");
            if (left != null) leftDoorTransform = left;
        }

        if (rightDoorTransform == null)
        {
            Transform right = transform.Find("big_door_right");
            if (right != null) rightDoorTransform = right;
        }

        // Fallback: If only a single child exists and neither left nor right was found
        if (leftDoorTransform == null && rightDoorTransform == null)
        {
            Transform singleDoor = transform.Find("big_door");
            if (singleDoor != null) leftDoorTransform = singleDoor;
        }

        // Cache closed rotations and calculate open targets
        if (leftDoorTransform != null)
        {
            leftClosedRotation = leftDoorTransform.localRotation;
            leftTargetOpenRotation = leftClosedRotation * Quaternion.Euler(leftOpenRotation);
        }

        if (rightDoorTransform != null)
        {
            rightClosedRotation = rightDoorTransform.localRotation;
            rightTargetOpenRotation = rightClosedRotation * Quaternion.Euler(rightOpenRotation);
        }

        if (doorAudioSource == null)
            doorAudioSource = GetComponent<AudioSource>();

        // 1. Remove any static non-convex MeshCollider from the rotating doors
        if (leftDoorTransform != null)
        {
            MeshCollider mc = leftDoorTransform.GetComponent<MeshCollider>();
            if (mc != null) Destroy(mc);
        }
        if (rightDoorTransform != null)
        {
            MeshCollider mc = rightDoorTransform.GetComponent<MeshCollider>();
            if (mc != null) Destroy(mc);
        }

        // 2. Ensure solid doorway barrier exists so player cannot pass through while closed
        Transform blockerChild = transform.Find("BigDoorwayPhysicalBarrier");
        if (blockerChild == null)
        {
            GameObject blockerObj = new GameObject("BigDoorwayPhysicalBarrier");
            blockerObj.transform.SetParent(transform, false);
            blockerObj.transform.localPosition = new Vector3(0f, 1.5f, 0f);
            blockerObj.transform.localRotation = Quaternion.identity;
            blockerChild = blockerObj.transform;
        }

        doorwayBlocker = blockerChild.GetComponent<BoxCollider>();
        if (doorwayBlocker == null)
        {
            doorwayBlocker = blockerChild.gameObject.AddComponent<BoxCollider>();
        }
        doorwayBlocker.size = new Vector3(2.5f, 3.0f, 0.15f); // 15cm thick, fits doorway frame
        doorwayBlocker.isTrigger = false;
        doorwayBlocker.enabled = !isOpen;

        // 3. Ensure trigger collider exists for player interaction raycaster
        BoxCollider interactCol = GetComponent<BoxCollider>();
        if (interactCol == null)
        {
            interactCol = gameObject.AddComponent<BoxCollider>();
        }
        interactCol.isTrigger = true;
        interactCol.size = new Vector3(2.5f, 3.0f, 0.20f);
        interactCol.center = new Vector3(0f, 1.5f, 0f);
    }

    private void Start()
    {
        ApplyInstantState(isOpen);
    }

    public string interactionText
    {
        get
        {
            if (isUnlockingSequence)
                return "Unlocking...";

            if (isLocked)
            {
                if (BigDoorKey.HasKey)
                    return "[E] Unlock Big Door (Use Key)";
                else
                    return "[E] Big Door (Locked - Needs Key)";
            }

            return isOpen ? "[E] Close Door" : "[E] Open Door";
        }
    }

    public void Interact()
    {
        if (isUnlockingSequence) return;

        if (isLocked)
        {
            if (BigDoorKey.HasKey)
            {
                StartCoroutine(UnlockWithKeySequence());
            }
            else
            {
                Debug.Log("[BigDoor] Locked! Player does not possess the BigDoorKey.");
                if (PuzzleManager.Instance != null)
                {
                    PuzzleManager.Instance.ShowClue("Big Door", lockedMessage);
                }
            }
        }
        else
        {
            // Toggle open / close when unlocked
            if (isOpen)
                CloseDoor();
            else
                OpenDoor();
        }
    }

    /// <summary>
    /// Key injection sequence:
    /// 1. Plays key insert audio.
    /// 2. Attaches key to keyhole and triggers Animator hook (for your upcoming injection animation).
    /// 3. Waits for injection animation duration.
    /// 4. Unlocks and opens the big double doors.
    /// </summary>
    public IEnumerator UnlockWithKeySequence()
    {
        isUnlockingSequence = true;
        Debug.Log("<color=cyan><b>[BigDoor] 🗝️ Injecting key into door keyhole...</b></color>");

        // 1. Play insert sound
        PlaySound(keyInsertSound);

        // 2. Attach physical key to keyhole if assigned
        if (BigDoorKey.Instance != null && keyholeTransform != null)
        {
            BigDoorKey.Instance.AttachToKeyhole(keyholeTransform);
        }

        // 3. Trigger Animator injection animation
        if (doorAnimator != null && !string.IsNullOrEmpty(keyInsertTriggerName))
        {
            doorAnimator.SetTrigger(keyInsertTriggerName);
            Debug.Log($"[BigDoor] Triggered key animation trigger '{keyInsertTriggerName}'");
        }

        OnKeyInjected?.Invoke();

        // 4. Wait for key injection animation to complete
        if (keyInsertAnimationDuration > 0f)
        {
            yield return new WaitForSeconds(keyInsertAnimationDuration);
        }

        // 5. Unlock the door
        isLocked = false;
        PlaySound(unlockSound);
        Ghost.EmitSound(transform.position, 30f, "Big Door Unlock Sound");
        OnBigDoorUnlocked?.Invoke();
        Debug.Log("<color=green><b>[BigDoor] 🔓 Big Door Unlocked Successfully!</b></color>");

        if (PuzzleManager.Instance != null)
        {
            PuzzleManager.Instance.ShowClue("Big Door Unlocked", unlockedMessage);
        }

        isUnlockingSequence = false;

        // 6. Swing open the doors
        OpenDoor();
    }

    public void OpenDoor()
    {
        if (isOpen) return;
        isOpen = true;

        if (doorwayBlocker != null)
            doorwayBlocker.enabled = false;

        Transform barrier = transform.Find("BigDoorwayPhysicalBarrier");
        if (barrier != null)
        {
            Collider c = barrier.GetComponent<Collider>();
            if (c != null) c.enabled = false;
        }

        PlaySound(openSound);

        // Emit sound so ghost hears the heavy double doors swinging open!
        Ghost.EmitSound(transform.position, 40f, "Big Door Open Sound");

        if (doorCoroutine != null)
            StopCoroutine(doorCoroutine);

        doorCoroutine = StartCoroutine(AnimateDoors(leftTargetOpenRotation, rightTargetOpenRotation));
    }

    public void CloseDoor()
    {
        if (!isOpen) return;
        isOpen = false;

        if (doorwayBlocker != null)
            doorwayBlocker.enabled = true;

        Transform barrier = transform.Find("BigDoorwayPhysicalBarrier");
        if (barrier != null)
        {
            Collider c = barrier.GetComponent<Collider>();
            if (c != null) c.enabled = true;
        }

        PlaySound(closeSound);

        // Emit sound so ghost hears the heavy double doors slamming/closing!
        Ghost.EmitSound(transform.position, 35f, "Big Door Close Sound");

        if (doorCoroutine != null)
            StopCoroutine(doorCoroutine);

        doorCoroutine = StartCoroutine(AnimateDoors(leftClosedRotation, rightClosedRotation));
    }

    private IEnumerator AnimateDoors(Quaternion targetLeft, Quaternion targetRight)
    {
        Quaternion startLeft = leftDoorTransform != null ? leftDoorTransform.localRotation : Quaternion.identity;
        Quaternion startRight = rightDoorTransform != null ? rightDoorTransform.localRotation : Quaternion.identity;

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime * openSpeed;
            float smooth = Mathf.SmoothStep(0f, 1f, t);

            if (leftDoorTransform != null)
                leftDoorTransform.localRotation = Quaternion.Slerp(startLeft, targetLeft, smooth);

            if (rightDoorTransform != null)
                rightDoorTransform.localRotation = Quaternion.Slerp(startRight, targetRight, smooth);

            yield return null;
        }

        if (leftDoorTransform != null) leftDoorTransform.localRotation = targetLeft;
        if (rightDoorTransform != null) rightDoorTransform.localRotation = targetRight;
    }

    private void ApplyInstantState(bool open)
    {
        if (doorwayBlocker != null)
            doorwayBlocker.enabled = !open;

        Transform barrier = transform.Find("BigDoorwayPhysicalBarrier");
        if (barrier != null)
        {
            Collider c = barrier.GetComponent<Collider>();
            if (c != null) c.enabled = !open;
        }

        if (leftDoorTransform != null)
            leftDoorTransform.localRotation = open ? leftTargetOpenRotation : leftClosedRotation;

        if (rightDoorTransform != null)
            rightDoorTransform.localRotation = open ? rightTargetOpenRotation : rightClosedRotation;
    }

    private void PlaySound(AudioClip clip)
    {
        if (doorAudioSource != null && clip != null)
        {
            doorAudioSource.PlayOneShot(clip);
        }
    }

    [ContextMenu("Test Unlock Door Now")]
    public void TestUnlock()
    {
        StartCoroutine(UnlockWithKeySequence());
    }
}
