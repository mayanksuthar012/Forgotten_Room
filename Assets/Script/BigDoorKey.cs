using System.Collections.Generic;
using UnityEngine;

public class BigDoorKey : MonoBehaviour, IInteractable
{
    public static BigDoorKey Instance { get; private set; }
    public static bool HasKey { get; private set; } = false;

    [Header("Key Info")]
    [SerializeField] private string keyName = "Big Door Key";
    [SerializeField] private string pickupPrompt = "[E] Pick up Key";
    [TextArea(2, 4)]
    [SerializeField] private string pickupMessage = "You collected the heavy iron key! It looks like it fits the large door.";

    [Header("Spawn Locations (Randomized Each Session)")]
    [Tooltip("List of candidate Transform locations where this key can spawn. On session start, it will randomly pick one.")]
    [SerializeField] private Transform[] candidateSpawnLocations;
    [Tooltip("If true, randomly places the key at one of the candidate locations at Start")]
    [SerializeField] private bool randomizeSpawnOnStart = true;

    [Header("Audio (Optional)")]
    [SerializeField] private AudioSource keyAudioSource;
    [SerializeField] private AudioClip pickupSound;

    [Header("Visual & Colliders")]
    [Tooltip("Mesh renderers to hide when picked up (auto-detected if empty)")]
    [SerializeField] private Renderer[] renderersToHide;
    [Tooltip("Colliders to disable when picked up (auto-detected if empty)")]
    [SerializeField] private Collider[] collidersToDisable;

    public bool IsCollected => HasKey;
    public string interactionText => pickupPrompt;

    public static event System.Action OnKeyPickedUp;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        HasKey = false;

        if (keyAudioSource == null)
            keyAudioSource = GetComponent<AudioSource>();

        if (renderersToHide == null || renderersToHide.Length == 0)
            renderersToHide = GetComponentsInChildren<Renderer>();

        if (collidersToDisable == null || collidersToDisable.Length == 0)
            collidersToDisable = GetComponentsInChildren<Collider>();

        // Ensure at least one collider exists so player's raycast can interact
        if ((collidersToDisable == null || collidersToDisable.Length == 0) && GetComponent<Collider>() == null)
        {
            SphereCollider sc = gameObject.AddComponent<SphereCollider>();
            sc.radius = 0.35f;
            collidersToDisable = new Collider[] { sc };
        }
    }

    private void Start()
    {
        if (randomizeSpawnOnStart)
        {
            SpawnAtRandomLocation();
        }
    }

    /// <summary>
    /// Randomly positions and rotates the key at one of the candidate spawn points.
    /// If candidateSpawnLocations is empty (e.g. when used as a project prefab asset),
    /// it automatically finds spawn points in the scene by parent name or tag!
    /// </summary>
    public void SpawnAtRandomLocation()
    {
        // 1. Use manually assigned candidate locations if available
        List<Transform> points = new List<Transform>();
        if (candidateSpawnLocations != null && candidateSpawnLocations.Length > 0)
        {
            foreach (var t in candidateSpawnLocations)
            {
                if (t != null) points.Add(t);
            }
        }

        // 2. If empty (e.g. prefab asset cannot reference scene objects), auto-detect in scene!
        if (points.Count == 0)
        {
            // Check for a group GameObject in the scene named 'KeySpawnLocations' or 'KeySpawnPoints'
            GameObject group = GameObject.Find("KeySpawnLocations") ?? GameObject.Find("KeySpawnPoints") ?? GameObject.Find("KeyLocations");
            if (group != null && group.transform.childCount > 0)
            {
                foreach (Transform child in group.transform)
                {
                    points.Add(child);
                }
            }

            // Also check for any objects tagged with 'KeySpawnPoint'
            try
            {
                GameObject[] tagged = GameObject.FindGameObjectsWithTag("KeySpawnPoint");
                if (tagged != null && tagged.Length > 0)
                {
                    foreach (var g in tagged)
                    {
                        if (g != null && !points.Contains(g.transform))
                            points.Add(g.transform);
                    }
                }
            }
            catch
            {
                // Tag may not be defined in ProjectSettings
            }
        }

        // 3. Move the key to one of the discovered spawn points
        if (points.Count > 0)
        {
            int randomIndex = Random.Range(0, points.Count);
            Transform chosenPoint = points[randomIndex];
            if (chosenPoint != null)
            {
                transform.position = chosenPoint.position;
                transform.rotation = chosenPoint.rotation;
                Debug.Log($"<color=yellow><b>[BigDoorKey] 🗝️ Physical Key placed at spawn location: '{chosenPoint.name}'</b></color>");
            }
        }
        else
        {
            Debug.LogWarning("[BigDoorKey] No spawn locations found. You can assign them in the scene, create an empty GameObject named 'KeySpawnLocations' with child points, or let PuzzleManager spawn the prefab.");
        }
    }

    public void Interact()
    {
        if (HasKey) return;

        CollectKey();
    }

    public void CollectKey()
    {
        HasKey = true;
        Debug.Log($"<color=green><b>[BigDoorKey] 🗝️ Player picked up '{keyName}'!</b></color>");

        if (keyAudioSource != null && pickupSound != null)
        {
            AudioSource.PlayClipAtPoint(pickupSound, transform.position);
        }

        // Show UI feedback via PuzzleManager CluePanel
        if (PuzzleManager.Instance != null)
        {
            PuzzleManager.Instance.ShowClue(keyName, pickupMessage);
        }

        OnKeyPickedUp?.Invoke();

        // Hide mesh & collider while keeping GameObject alive so it can be animated into the door keyhole later
        SetKeyVisible(false);
    }

    public void SetKeyVisible(bool visible)
    {
        if (renderersToHide != null)
        {
            foreach (var r in renderersToHide)
            {
                if (r != null) r.enabled = visible;
            }
        }

        if (collidersToDisable != null)
        {
            foreach (var c in collidersToDisable)
            {
                if (c != null) c.enabled = visible;
            }
        }
    }

    /// <summary>
    /// Future animation hook: Positions and shows the physical key at the door keyhole
    /// when the player injects / inserts the key to unlock the door.
    /// </summary>
    public void AttachToKeyhole(Transform keyholeTransform)
    {
        if (keyholeTransform == null) return;

        transform.SetParent(keyholeTransform);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        SetKeyVisible(true);
    }

    [ContextMenu("Test Collect Key")]
    public void TestCollect() => CollectKey();

    [ContextMenu("Test Respawn Key")]
    public void TestRespawn() => SpawnAtRandomLocation();
}
