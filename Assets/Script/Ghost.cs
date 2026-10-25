using System.Collections;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(CapsuleCollider))]
public class Ghost : MonoBehaviour
{
    public enum GhostState
    {
        Patrolling = 0,    // Ghost wanders the room looking for the player
        Chasing = 1,       // Ghost has line of sight and is actively chasing the player
        Searching = 2,     // Player hid behind a wall; ghost checks last known spot before returning to patrol
        Jumpscaring = 3,   // Ghost caught player: playing attack animation, scream & jumpscare!
        Investigating = 4  // Ghost heard a sound (like a door opening/unlocking) and is investigating!
    }

    [Header("Current State")]
    [SerializeField] private GhostState currentState = GhostState.Patrolling;

    [Header("References")]
    [Tooltip("Target for the ghost to chase (Player). Auto-found if null.")]
    [SerializeField] private Transform target;

    [Header("Speeds")]
    [SerializeField] private float patrolSpeed = 2.2f;
    [SerializeField] private float chaseSpeed = 4.8f;

    [Header("Vision & Detection (Line of Sight)")]
    [Tooltip("How far the ghost can see in front of it")]
    [SerializeField] private float viewDistance = 16f;
    [Tooltip("Field of view in front of the ghost (degrees)")]
    [SerializeField] private float viewAngle = 110f;
    [Tooltip("Height of the ghost's eyes from feet for raycasting")]
    [SerializeField] private float eyeHeight = 1.0f;
    [Tooltip("Hearing/close range: ghost senses player if they are this close, even from behind")]
    [SerializeField] private float closeSensingRange = 3.0f;
    [Tooltip("Layers that block the ghost's sight (e.g. Default, Walls, Obstacles)")]
    [SerializeField] private LayerMask obstacleMask = ~0;

    [Header("Hearing & Sound Detection (Door Opening & Noises)")]
    [Tooltip("Can the ghost listen to environment sounds like doors opening, keys turning, etc.?")]
    [SerializeField] private bool canHearSounds = true;
    [Tooltip("Maximum hearing distance in meters (doors opening can be heard from far away)")]
    [SerializeField] private float hearingDistance = 35f;
    [Tooltip("Movement speed when rushing to investigate a heard door sound")]
    [SerializeField] private float investigateSpeed = 4.0f;
    [Tooltip("How many seconds the ghost searches the door area before resuming patrol")]
    [SerializeField] private float investigateDuration = 4.5f;
    [Tooltip("Optional sound cue played by ghost when it hears a noise and gets alerted")]
    [SerializeField] private AudioClip soundHeardCue;

    [Header("Patrol Settings")]
    [Tooltip("Optional waypoints. If empty, ghost automatically wanders around random spots on the floor!")]
    [SerializeField] private Transform[] patrolWaypoints;
    [SerializeField] private float patrolWaitTime = 1.5f;
    [SerializeField] private float randomPatrolRadius = 8f;

    [Header("Attack Range & Limits")]
    [Tooltip("Distance at which the ghost actually catches the player (MUST be small, e.g. 1.8m)")]
    [SerializeField] private float attackRange = 1.8f;
    [Tooltip("How many seconds the ghost searches where it last saw you before giving up")]
    [SerializeField] private float searchDuration = 4.5f;

    [Header("Attack Animation")]
    [Tooltip("Animator component on the ghost (optional)")]
    [SerializeField] private Animator ghostAnimator;
    [Tooltip("Name of the trigger parameter in Animator to play attack (default: Attack)")]
    [SerializeField] private string attackTriggerName = "Attack";

    [Header("Jumpscare & Audio")]
    [Tooltip("AudioSource to play the scary scream")]
    [SerializeField] private AudioSource jumpscareAudio;
    [Tooltip("Terrifying scream / shock sound effect")]
    [SerializeField] private AudioClip jumpscareScreamSound;
    [Tooltip("Optional UI Image / Panel for Jumpscare (e.g. scary face or red flash on Canvas)")]
    [SerializeField] private GameObject jumpscareUI;
    [Tooltip("Optional Game Over Panel with Retry / Restart button")]
    [SerializeField] private GameObject gameOverPanel;
    [Tooltip("Duration of the jumpscare sequence in seconds")]
    [SerializeField] private float jumpscareDuration = 2.2f;
    [Tooltip("If true, restarts scene on death if no Game Over panel is assigned")]
    [SerializeField] private bool autoRestartOnDeath = true;

    [Header("Wall Collision & Obstacle Settings")]
    [Tooltip("If true, automatically ensures NavMeshObstacles on all scene wall colliders so the NavMeshAgent cannot walk through walls")]
    [SerializeField] private bool autoCarveWallObstacles = true;
    [Tooltip("If true, actively pushes the ghost away from solid wall colliders to prevent clipping through walls")]
    [SerializeField] private bool preventWallClipping = true;
    [SerializeField] private float wallPushRadius = 0.45f;

    [Header("Collider Settings")]
    [SerializeField] private bool isGhostTrigger = true;

    [Header("Attack Restrictions")]
    [Tooltip("If true, the ghost is completely unable to attack the player until the exit door is unlocked!")]
    [SerializeField] private bool requireDoorUnlockedToAttack = false;
    [Tooltip("Reference to the ExitDoor in the scene (auto-detected if null)")]
    [SerializeField] private ExitDoor exitDoor;

    [Tooltip("Number of seconds the ghost must continuously look at the player before it can attack")]
    [SerializeField] private float requiredLookDuration = 0f;
    [SerializeField] private float currentLookTimer = 0f;

    public bool IsDoorUnlocked => exitDoor == null || !exitDoor.IsLocked;
    public float CurrentLookTimer => currentLookTimer;
    public float RequiredLookDuration => requiredLookDuration;
    public bool CanAttack => CanAttackPlayer();

    private NavMeshAgent agent;
    private CapsuleCollider ghostCollider;

    // State Variables
    private Vector3 lastKnownPosition;
    private float searchTimer = 0f;
    private float waitTimer = 0f;
    private bool isWaitingAtPatrolPoint = false;
    private int currentWaypointIndex = 0;
    private bool isJumpscaring = false;
    private Transform visualMeshChild;
    private Vector3 visualMeshOriginalPos;

    // Hearing & Sound Investigation Variables
    private Vector3 investigateSoundPosition;
    private float investigateTimer = 0f;
    private bool isLookingAroundAtSound = false;
    private float lookAroundTimer = 0f;
    private Quaternion lookAroundBaseRotation;

    // Static event broadcast for any sound in the world (doors opening, keys, horn, footsteps)
    public static event System.Action<Vector3, float, string> OnSoundEmitted;

    /// <summary>
    /// Broadcasts a sound into the game world (e.g. door opening, door unlocking, footsteps, horn).
    /// Any ghost in hearing range will listen to the sound, turn, and investigate!
    /// </summary>
    public static void EmitSound(Vector3 soundPosition, float soundRadius = 30f, string soundDescription = "Sound")
    {
        OnSoundEmitted?.Invoke(soundPosition, soundRadius, soundDescription);
    }

    private void OnEnable()
    {
        OnSoundEmitted += HandleSoundEmitted;
    }

    private void OnDisable()
    {
        OnSoundEmitted -= HandleSoundEmitted;
    }

    /// <summary>
    /// Callback when any sound is emitted in the scene.
    /// Checks if the ghost can hear the sound and begins investigation if within range.
    /// </summary>
    private void HandleSoundEmitted(Vector3 soundPos, float soundRadius, string soundDescription)
    {
        if (!canHearSounds || isJumpscaring) return;

        // If currently actively chasing with direct line of sight to player, maintain chase
        if (currentState == GhostState.Chasing && CanSeePlayer())
        {
            return;
        }

        float dist = Vector3.Distance(transform.position, soundPos);
        float effectiveRadius = Mathf.Max(soundRadius, hearingDistance);

        if (dist <= effectiveRadius)
        {
            HearSound(soundPos, soundDescription, dist);
        }
    }

    /// <summary>
    /// Alerts the ghost to a sound (e.g. door opening), immediately turning and routing to investigate!
    /// </summary>
    public void HearSound(Vector3 soundPos, string soundDescription, float dist)
    {
        Debug.Log($"<color=orange><b>[Ghost] 👂 HEARD SOUND: '{soundDescription}' at {soundPos} (Distance: {dist:F1}m)! Turning to investigate...</b></color>");

        if (soundHeardCue != null && jumpscareAudio != null)
        {
            jumpscareAudio.PlayOneShot(soundHeardCue, 0.7f);
        }

        investigateSoundPosition = soundPos;
        investigateTimer = 0f;
        isLookingAroundAtSound = false;
        lookAroundTimer = 0f;

        SmoothFaceTarget(soundPos);
        SetState(GhostState.Investigating);

        if (agent != null && agent.isOnNavMesh)
        {
            agent.isStopped = false;
            agent.speed = investigateSpeed;
            if (NavMesh.SamplePosition(soundPos, out NavMeshHit hit, 3.5f, NavMesh.AllAreas))
            {
                agent.SetDestination(hit.position);
            }
            else
            {
                agent.SetDestination(soundPos);
            }
        }
    }

    private void Awake()
    {
        // Clamp eyeHeight if serialized higher than wall top (2.8m) to avoid shooting rays over walls
        if (eyeHeight > 1.2f)
        {
            eyeHeight = 0.95f;
        }

        ghostCollider = GetComponent<CapsuleCollider>();
        if (ghostCollider != null)
        {
            ghostCollider.isTrigger = isGhostTrigger;
            ghostCollider.height = 2f;
            ghostCollider.radius = 0.5f;
            ghostCollider.center = new Vector3(0f, 1f, 0f);
        }

        if (ghostAnimator == null)
            ghostAnimator = GetComponentInChildren<Animator>();

        if (jumpscareAudio == null)
            jumpscareAudio = GetComponent<AudioSource>();

        // Cache first child mesh for gentle floating effect
        if (transform.childCount > 0)
        {
            visualMeshChild = transform.GetChild(0);
            visualMeshOriginalPos = visualMeshChild.localPosition;
        }

        if (exitDoor == null)
        {
            exitDoor = FindAnyObjectByType<ExitDoor>();
        }

        if (autoCarveWallObstacles)
        {
            EnsureWallNavMeshObstacles();
        }
    }

    private void Start()
    {
        if (exitDoor == null && PuzzleManager.Instance != null)
        {
            exitDoor = PuzzleManager.Instance.ExitDoor;
        }

        agent = GetComponent<NavMeshAgent>();
        if (agent == null)
        {
            Debug.LogError("Ghost requires a NavMeshAgent component.");
            enabled = false;
            return;
        }

        // Configure agent properties
        agent.speed = patrolSpeed;
        agent.acceleration = 10f;
        agent.angularSpeed = 450f;
        agent.stoppingDistance = 0.5f;
        agent.autoBraking = true;

        // Hide Jumpscare UI at start
        if (jumpscareUI != null)
            jumpscareUI.SetActive(false);
        if (gameOverPanel != null)
            gameOverPanel.SetActive(false);

        // Start in patrolling mode
        SetState(GhostState.Patrolling);
        MoveToNextPatrolDestination();
    }

    private void Update()
    {
        if (isJumpscaring)
            return;

        // Resolve any physical wall penetration so ghost cannot clip through walls
        if (preventWallClipping)
        {
            ResolveWallCollisions();
        }

        // 1. Ensure target is active and valid (auto-locates active Player / VR Rig if null or inactive)
        if (target == null || !target.gameObject.activeInHierarchy)
        {
            target = FindActivePlayerTransform();
            if (target == null) return;
        }

        // 2. Check Line of Sight (strictly checked against wall colliders)
        bool canSee = CanSeePlayer();

        // 3. Update Look Timer: Increments when looking at player, smoothly decays if sight briefly flickers
        if (canSee)
        {
            currentLookTimer += Time.deltaTime;
        }
        else
        {
            // Smooth decay instead of instant wipe (prevents single-frame slips from canceling attack lock-on)
            currentLookTimer = Mathf.Max(0f, currentLookTimer - Time.deltaTime * 1.5f);
        }

        // 4. Gentle ghostly floating bob (confined to small amplitude)
        if (visualMeshChild != null && currentState != GhostState.Jumpscaring)
        {
            float bob = Mathf.Sin(Time.time * 3f) * 0.08f;
            visualMeshChild.localPosition = visualMeshOriginalPos + new Vector3(0f, bob, 0f);
        }

        // 5. Update Animator floats if controller has them
        if (ghostAnimator != null)
        {
            ghostAnimator.SetFloat("Speed", agent.velocity.magnitude);
            ghostAnimator.SetBool("isChasing", currentState == GhostState.Chasing);
        }

        // 6. State Machine Update
        switch (currentState)
        {
            case GhostState.Patrolling:
                UpdatePatrol(canSee);
                break;

            case GhostState.Investigating:
                UpdateInvestigating(canSee);
                break;

            case GhostState.Chasing:
                UpdateChase(canSee);
                break;

            case GhostState.Searching:
                UpdateSearch(canSee);
                break;
        }
    }

    // --- Line of Sight & Target Helpers ---
    public Transform FindActivePlayerTransform()
    {
        // 1. Check all GameObjects tagged "Player" that are active in scene (e.g. XR Origin (VR))
        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
        foreach (var p in players)
        {
            if (p != null && p.activeInHierarchy)
            {
                return p.transform;
            }
        }

        // 2. Check active PlayerController
        PlayerController[] pcs = FindObjectsByType<PlayerController>(FindObjectsInactive.Exclude);
        if (pcs != null && pcs.Length > 0 && pcs[0].gameObject.activeInHierarchy)
        {
            return pcs[0].transform;
        }

        // 3. Check active Camera.main root
        if (Camera.main != null && Camera.main.gameObject.activeInHierarchy)
        {
            return Camera.main.transform.root;
        }

        return null;
    }

    public Vector3 GetPlayerCenter()
    {
        if (target == null) return transform.position + transform.forward * 2f;
        Collider col = target.GetComponent<Collider>();
        if (col != null)
        {
            return col.bounds.center;
        }
        return target.position + Vector3.up * 0.9f;
    }

    public Vector3 GetPlayerHead()
    {
        if (target == null) return transform.position + transform.forward * 2f;
        Collider col = target.GetComponent<Collider>();
        if (col != null)
        {
            return col.bounds.center + Vector3.up * (col.bounds.extents.y * 0.65f);
        }
        return target.position + Vector3.up * 1.35f;
    }

    public Vector3 GetGhostEyePos()
    {
        return transform.position + Vector3.up * Mathf.Clamp(eyeHeight, 0.5f, 1.2f);
    }

    public bool CanSeePlayer()
    {
        if (target == null) return false;

        Vector3 eyePos = GetGhostEyePos();
        Vector3 playerCenter = GetPlayerCenter();
        Vector3 playerHead = GetPlayerHead();

        float distance = Vector3.Distance(eyePos, playerCenter);

        // Outside maximum view distance
        if (distance > viewDistance) return false;

        // Check if player is within close hearing/sensing range (bypasses forward angle)
        bool inVisionCone = false;
        Vector3 dirToTarget = (playerCenter - eyePos).normalized;
        float angle = Vector3.Angle(transform.forward, dirToTarget);

        if (distance <= closeSensingRange)
        {
            inVisionCone = true; // Sensed nearby from any direction
        }
        else if (angle <= viewAngle / 2f)
        {
            inVisionCone = true; // Inside forward visual cone
        }

        if (!inVisionCone) return false;

        // Check line of sight: test center, head, and chest
        bool centerClear = HasClearLineOfSight(eyePos, playerCenter, distance);
        bool headClear = HasClearLineOfSight(eyePos, playerHead, distance);
        Vector3 ghostChest = transform.position + Vector3.up * (eyeHeight * 0.55f);
        bool chestClear = HasClearLineOfSight(ghostChest, playerCenter, distance);

        return centerClear || headClear || chestClear;
    }

    public bool HasClearLineOfSight(Vector3 from, Vector3 to, float distance)
    {
        if (target == null) return false;

        Vector3 direction = (to - from).normalized;
        float rayDistance = Mathf.Min(distance, viewDistance) + 0.3f;

        // Cast ray ignoring trigger volumes (so Box Volume, clue triggers do not falsely block or interfere)
        RaycastHit[] hits = Physics.RaycastAll(from, direction, rayDistance, obstacleMask, QueryTriggerInteraction.Ignore);

        if (hits == null || hits.Length == 0)
        {
            return false;
        }

        // Sort hits by distance from ray origin
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var hit in hits)
        {
            // Ignore ghost's own colliders
            if (hit.transform == transform || hit.transform.IsChildOf(transform))
            {
                continue;
            }

            // If the solid collider hit is the Player -> Clear line of sight!
            if (hit.transform == target || hit.transform.IsChildOf(target) || hit.collider.GetComponentInParent<PlayerController>() != null)
            {
                return true;
            }

            // Ray struck a wall, door, or furniture collider before reaching player -> BLOCKED BY WALL!
            return false;
        }

        return false;
    }

    // --- State: Patrolling ---
    private void UpdatePatrol(bool canSeePlayer)
    {
        if (canSeePlayer)
        {
            SmoothFaceTarget(target.position);

            if (requireDoorUnlockedToAttack && !IsDoorUnlocked)
            {
                if (agent.isOnNavMesh)
                {
                    agent.isStopped = true;
                    agent.velocity = Vector3.zero;
                }
                return;
            }

            // Immediately engage chase like Granny!
            SetState(GhostState.Chasing);
            return;
        }

        // Normal patrol movement when player is not in sight
        ExecutePatrolStep();
    }

    private void ExecutePatrolStep()
    {
        // If currently waiting at a destination
        if (isWaitingAtPatrolPoint)
        {
            waitTimer += Time.deltaTime;
            if (waitTimer >= patrolWaitTime)
            {
                isWaitingAtPatrolPoint = false;
                waitTimer = 0f;
                MoveToNextPatrolDestination();
            }
            return;
        }

        // Check if agent reached destination (Only check when agent HAS an active path)
        if (agent.hasPath && !agent.pathPending)
        {
            if (agent.remainingDistance <= agent.stoppingDistance + 0.3f)
            {
                isWaitingAtPatrolPoint = true;
                waitTimer = 0f;
            }
        }
        else if (!agent.hasPath && !agent.pathPending)
        {
            // Agent has no path -> immediately find a new spot!
            MoveToNextPatrolDestination();
        }
    }

    private void MoveToNextPatrolDestination()
    {
        if (agent == null || !agent.isOnNavMesh) return;

        agent.isStopped = false;
        agent.speed = patrolSpeed;

        // 1. If user provided custom waypoints, cycle through them
        if (patrolWaypoints != null && patrolWaypoints.Length > 0)
        {
            Transform wp = patrolWaypoints[currentWaypointIndex];
            if (wp != null)
                agent.SetDestination(wp.position);

            currentWaypointIndex = (currentWaypointIndex + 1) % patrolWaypoints.Length;
        }
        else
        {
            // 2. Otherwise pick a random point around the ghost's CURRENT position
            Vector2 circle = Random.insideUnitCircle.normalized * Random.Range(3f, randomPatrolRadius);
            Vector3 randomTarget = transform.position + new Vector3(circle.x, 0f, circle.y);

            if (NavMesh.SamplePosition(randomTarget, out NavMeshHit hit, randomPatrolRadius * 2f, NavMesh.AllAreas))
            {
                agent.SetDestination(hit.position);
            }
        }
    }

    // --- State: Chasing ---
    private void UpdateChase(bool canSeePlayer)
    {
        // If door is locked, ghost cannot attack or chase
        if (requireDoorUnlockedToAttack && !IsDoorUnlocked)
        {
            SetState(GhostState.Patrolling);
            return;
        }

        if (canSeePlayer)
        {
            agent.isStopped = false;
            agent.speed = chaseSpeed;
            lastKnownPosition = target.position;
            agent.SetDestination(target.position);

            // ONLY ATTACK when physically close AND CanAttackPlayer() is true!
            float distanceToPlayer = Vector3.Distance(transform.position, target.position);
            float catchDist = Mathf.Max(attackRange, 2.0f);

            if (distanceToPlayer <= catchDist && CanAttackPlayer())
            {
                OnPlayerCaught();
            }
        }
        else
        {
            // Player hid behind cover! Transition to Search
            Debug.Log("<color=yellow>[Ghost] Player hid behind cover! Checking last known position...</color>");
            currentLookTimer = 0f;
            SetState(GhostState.Searching);
        }
    }

    // --- State: Searching ---
    private void UpdateSearch(bool canSeePlayer)
    {
        if (requireDoorUnlockedToAttack && !IsDoorUnlocked)
        {
            SetState(GhostState.Patrolling);
            return;
        }

        if (canSeePlayer)
        {
            SmoothFaceTarget(target.position);
            SetState(GhostState.Chasing);
            return;
        }

        agent.speed = investigateSpeed;
        agent.SetDestination(lastKnownPosition);

        // If arrived at last known spot, look around left and right
        if (agent.hasPath && !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.6f)
        {
            float scanAngle = Mathf.Sin(searchTimer * 3.5f) * 45f;
            transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y + scanAngle * Time.deltaTime, 0f);
        }

        searchTimer += Time.deltaTime;
        if (searchTimer >= searchDuration)
        {
            Debug.Log("<color=cyan>[Ghost] Finished searching last known position. Resuming patrol.</color>");
            SetState(GhostState.Patrolling);
            MoveToNextPatrolDestination();
        }
    }

    // --- State: Investigating Heard Sound ---
    private void UpdateInvestigating(bool canSeePlayer)
    {
        if (canSeePlayer)
        {
            SmoothFaceTarget(target.position);

            if (requireDoorUnlockedToAttack && !IsDoorUnlocked)
            {
                if (agent.isOnNavMesh)
                {
                    agent.isStopped = true;
                    agent.velocity = Vector3.zero;
                }
                return;
            }

            SetState(GhostState.Chasing);
            return;
        }

        if (!isLookingAroundAtSound)
        {
            agent.speed = investigateSpeed;

            if (agent.hasPath && !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.8f)
            {
                isLookingAroundAtSound = true;
                lookAroundBaseRotation = transform.rotation;
                lookAroundTimer = 0f;
                investigateTimer = 0f;
                if (agent.isOnNavMesh)
                {
                    agent.isStopped = true;
                    agent.velocity = Vector3.zero;
                }
                Debug.Log("<color=yellow>[Ghost] Arrived at sound location. Looking around for the noise source...</color>");
            }
            else if (!agent.hasPath && !agent.pathPending)
            {
                agent.SetDestination(investigateSoundPosition);
            }
        }
        else
        {
            lookAroundTimer += Time.deltaTime;
            float scanAngle = Mathf.Sin(lookAroundTimer * 2.5f) * 50f;
            transform.rotation = lookAroundBaseRotation * Quaternion.Euler(0f, scanAngle, 0f);

            investigateTimer += Time.deltaTime;
            if (investigateTimer >= investigateDuration)
            {
                Debug.Log("<color=cyan>[Ghost] Sound investigation complete. Nothing found. Resuming patrol.</color>");
                SetState(GhostState.Patrolling);
                MoveToNextPatrolDestination();
            }
        }
    }

    private void SmoothFaceTarget(Vector3 targetPos)
    {
        Vector3 lookDir = (targetPos - transform.position).normalized;
        lookDir.y = 0;
        if (lookDir.sqrMagnitude > 0.001f)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(lookDir), Time.deltaTime * 6f);
        }
    }

    public bool CanAttackPlayer()
    {
        if (isJumpscaring) return false;

        // Condition 1: Until door is unlocked, ghost CANNOT attack (if restriction enabled)
        if (requireDoorUnlockedToAttack && !IsDoorUnlocked)
        {
            return false;
        }

        // Condition 2: Solid wall must not be separating ghost from player
        if (target != null)
        {
            float dist = Vector3.Distance(transform.position, target.position);
            Vector3 ghostPos = GetGhostEyePos();
            Vector3 targetPos = GetPlayerCenter();

            if (!HasClearLineOfSight(ghostPos, targetPos, dist + 0.6f))
            {
                return false;
            }
        }

        return true;
    }

    private void SetState(GhostState newState)
    {
        currentState = newState;
        searchTimer = 0f;
        isWaitingAtPatrolPoint = false;
        waitTimer = 0f;

        if (newState == GhostState.Chasing)
        {
            if (agent.isOnNavMesh)
            {
                agent.isStopped = false;
                agent.speed = chaseSpeed;
            }
            Debug.Log("<color=red><b>[Ghost] 👻 SPOTTED PLAYER! CHASING AND ATTACKING!</b></color>");
        }
        else if (newState == GhostState.Patrolling)
        {
            if (agent.isOnNavMesh)
            {
                agent.isStopped = false;
                agent.speed = patrolSpeed;
            }
        }
        else if (newState == GhostState.Investigating)
        {
            if (agent.isOnNavMesh)
            {
                agent.isStopped = false;
                agent.speed = investigateSpeed;
            }
            isLookingAroundAtSound = false;
            investigateTimer = 0f;
            lookAroundTimer = 0f;
        }
    }

    // --- Collisions / Caught ---
    private void OnTriggerEnter(Collider other)
    {
        if (isJumpscaring) return;

        if (other.CompareTag("Player") || other.GetComponent<PlayerController>() != null || other.transform == target || other.transform.IsChildOf(target))
        {
            if (!CanAttackPlayer())
            {
                return;
            }

            // Trigger catch if within reach
            float dist = target != null ? Vector3.Distance(transform.position, target.position) : 999f;
            float catchDist = Mathf.Max(attackRange, 2.2f);
            if (dist <= catchDist)
            {
                OnPlayerCaught();
            }
        }
    }

    public void OnPlayerCaught()
    {
        if (isJumpscaring) return;

        if (!CanAttackPlayer())
        {
            return;
        }

        // Final fail-safe: Ensure no solid wall separates Ghost and Player
        if (target != null)
        {
            float dist = Vector3.Distance(transform.position, target.position);
            Vector3 ghostPos = GetGhostEyePos();
            Vector3 targetPos = GetPlayerCenter();
            if (!HasClearLineOfSight(ghostPos, targetPos, dist + 0.6f))
            {
                Debug.LogWarning("[Ghost] Attack aborted: Wall collider is between Ghost and Player!");
                return;
            }
        }

        isJumpscaring = true;
        SetState(GhostState.Jumpscaring);
        StartCoroutine(JumpscareRoutine());
    }

    // --- THE JUMPSCARE SEQUENCE ---
    private IEnumerator JumpscareRoutine()
    {
        Debug.Log("<color=red><b>[Ghost] 👻 ATTACK & JUMPSCARE TRIGGERED!</b></color>");

        // 1. Stop NavMesh movement
        if (agent != null && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.velocity = Vector3.zero;
        }

        // 2. Freeze player movement & force-look at the ghost
        PlayerController playerController = null;
        Transform playerCameraTransform = null;
        if (target != null)
        {
            playerController = target.GetComponent<PlayerController>();
            if (playerController != null)
            {
                playerController.SetControlActive(false);
                playerController.LookAtTarget(transform.position + Vector3.up * eyeHeight);
            }

            CharacterController cc = target.GetComponent<CharacterController>() ?? target.GetComponentInChildren<CharacterController>();
            if (cc != null) cc.enabled = false;

            Camera mainCam = Camera.main;
            if (mainCam != null)
                playerCameraTransform = mainCam.transform;
        }

        // 3. Make Ghost face player
        Vector3 targetFacePos = playerCameraTransform != null ? playerCameraTransform.position : target.position;
        Vector3 lookDir = (targetFacePos - transform.position).normalized;
        lookDir.y = 0;
        if (lookDir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(lookDir);

        // 4. Play Attack Animation on Ghost Animator ONLY NOW!
        if (ghostAnimator != null)
        {
            ghostAnimator.SetTrigger(attackTriggerName);
            Debug.Log($"[Ghost] Played animation trigger: '{attackTriggerName}'");
        }

        // 5. Play Scary Scream Audio
        if (jumpscareAudio != null && jumpscareScreamSound != null)
        {
            jumpscareAudio.PlayOneShot(jumpscareScreamSound);
        }

        // 6. Fast Lunge towards the camera for instant terror
        Vector3 startGhostPos = transform.position;
        Vector3 camPos = playerCameraTransform != null ? playerCameraTransform.position : target.position + Vector3.up * 1.2f;
        Vector3 targetLungePos = camPos + (transform.position - camPos).normalized * 0.7f;
        float lungeDuration = 0.22f;
        float lungeElapsed = 0f;

        while (lungeElapsed < lungeDuration)
        {
            lungeElapsed += Time.deltaTime;
            float t = lungeElapsed / lungeDuration;
            transform.position = Vector3.Lerp(startGhostPos, targetLungePos, Mathf.SmoothStep(0f, 1f, t));
            yield return null;
        }

        // 7. Show Jumpscare UI / Flash
        if (jumpscareUI != null)
        {
            jumpscareUI.SetActive(true);
        }

        // 8. Intense Camera Shake
        if (playerCameraTransform != null)
        {
            Vector3 camOriginalPos = playerCameraTransform.localPosition;
            float shakeDuration = 0.8f;
            float shakeElapsed = 0f;
            float shakeAmount = 0.08f;

            while (shakeElapsed < shakeDuration)
            {
                shakeElapsed += Time.deltaTime;
                playerCameraTransform.localPosition = camOriginalPos + Random.insideUnitSphere * shakeAmount;
                yield return null;
            }
            playerCameraTransform.localPosition = camOriginalPos;
        }

        // 9. Wait for remaining jumpscare duration
        yield return new WaitForSeconds(Mathf.Max(0.5f, jumpscareDuration - 1.0f));

        // 10. Hide jumpscare overlay & Show Game Over / Restart
        if (jumpscareUI != null)
            jumpscareUI.SetActive(false);

        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Debug.Log("<color=red><b>[Ghost] Game Over Panel activated.</b></color>");
        }
        else if (autoRestartOnDeath)
        {
            Debug.Log("<color=orange>[Ghost] Restarting scene...</color>");
            UnityEngine.SceneManagement.SceneManager.LoadScene(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex
            );
        }
    }

    /// <summary>
    /// Finds all wall colliders in the scene and ensures they have a NavMeshObstacle with carving enabled.
    /// This prevents the NavMeshAgent from walking straight through walls!
    /// </summary>
    public void EnsureWallNavMeshObstacles()
    {
        BoxCollider[] allBoxes = FindObjectsByType<BoxCollider>(FindObjectsInactive.Exclude);
        int carvedCount = 0;

        foreach (var box in allBoxes)
        {
            if (box == null || box.isTrigger) continue;

            string name = box.gameObject.name.ToLower();
            bool isWall = name.Contains("wall") || name.Contains("door") || name.Contains("pillar");
            bool isTallObstacle = box.size.y >= 1.4f;

            if (isWall || isTallObstacle)
            {
                NavMeshObstacle obstacle = box.GetComponent<NavMeshObstacle>();
                if (obstacle == null)
                {
                    obstacle = box.gameObject.AddComponent<NavMeshObstacle>();
                }

                obstacle.carving = true;
                obstacle.carveOnlyStationary = false;
                obstacle.shape = NavMeshObstacleShape.Box;
                obstacle.size = box.size;
                obstacle.center = box.center;
                carvedCount++;
            }
        }

        Debug.Log($"<color=cyan><b>[Ghost] 🧱 Configured {carvedCount} Wall NavMeshObstacles (Carving = ON) to block ghost from passing through walls!</b></color>");
    }

    /// <summary>
    /// Actively resolves any wall penetration so the ghost physically slides along wall colliders instead of clipping into or through them.
    /// </summary>
    private void ResolveWallCollisions()
    {
        Vector3 point1 = transform.position + Vector3.up * 0.2f;
        Vector3 point2 = transform.position + Vector3.up * 1.5f;

        Collider[] overlaps = Physics.OverlapCapsule(point1, point2, wallPushRadius, obstacleMask, QueryTriggerInteraction.Ignore);
        foreach (var col in overlaps)
        {
            if (col == null || col.isTrigger || col.transform == transform || col.transform.IsChildOf(transform))
                continue;

            if (target != null && (col.transform == target || col.transform.IsChildOf(target)))
                continue;

            Vector3 ghostCenter = transform.position + Vector3.up * (eyeHeight * 0.8f);
            Vector3 closestPoint = col.ClosestPoint(ghostCenter);
            Vector3 pushDir = ghostCenter - closestPoint;
            pushDir.y = 0f;
            float dist = pushDir.magnitude;

            if (dist < wallPushRadius && dist > 0.0001f)
            {
                float penetration = wallPushRadius - dist;
                Vector3 correction = pushDir.normalized * (penetration + 0.03f);
                transform.position += correction;

                if (agent != null && agent.isOnNavMesh)
                {
                    agent.nextPosition = transform.position;
                }
            }
        }
    }

    // --- Scene View Gizmos (Visual Debugging) ---
    private void OnDrawGizmosSelected()
    {
        Vector3 eyePos = GetGhostEyePos();

        // Draw Detection Circle
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, viewDistance);

        // Draw Close Sensing Circle
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, closeSensingRange);

        // Draw Field of View Cone
        Gizmos.color = Color.cyan;
        Vector3 leftRayDirection = Quaternion.Euler(0, -viewAngle / 2f, 0) * transform.forward;
        Vector3 rightRayDirection = Quaternion.Euler(0, viewAngle / 2f, 0) * transform.forward;
        Gizmos.DrawRay(eyePos, leftRayDirection * viewDistance);
        Gizmos.DrawRay(eyePos, rightRayDirection * viewDistance);

        // Draw Line to Player (Green if visible, Red if blocked by wall)
        if (target != null)
        {
            Vector3 playerCenter = GetPlayerCenter();
            bool visible = CanSeePlayer();
            Gizmos.color = visible ? Color.green : Color.red;
            Gizmos.DrawLine(eyePos, playerCenter);
        }
    }

    // Public getters
    public GhostState CurrentState => currentState;
    public bool IsJumpscaring => isJumpscaring;
}
