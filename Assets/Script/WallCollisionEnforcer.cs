using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Automatically enforces physical solid colliders on all scene walls, doorways, and rooms.
/// Prevents the player and ghost from walking or clipping through walls and doors into other rooms.
/// Runs with [DefaultExecutionOrder(-500)] before any other script executes.
/// </summary>
[DefaultExecutionOrder(-500)]
public class WallCollisionEnforcer : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoInitialize()
    {
        if (FindAnyObjectByType<WallCollisionEnforcer>() == null)
        {
            GameObject enforcerObj = new GameObject("WallCollisionEnforcer");
            enforcerObj.AddComponent<WallCollisionEnforcer>();
        }
    }

    private void Awake()
    {
        EnforceWallColliders();
        EnforceDoorwayBarriers();
        ConfigureCharacterControllers();
    }

    /// <summary>
    /// Scans all MeshFilter objects in the scene. Any wall, corner, window, or room mesh
    /// lacking a collider gets a solid MeshCollider using its exact 3D geometry.
    /// </summary>
    public static void EnforceWallColliders()
    {
        MeshFilter[] allMeshes = FindObjectsByType<MeshFilter>(FindObjectsInactive.Include);
        int addedCount = 0;

        foreach (var mf in allMeshes)
        {
            if (mf == null || mf.sharedMesh == null) continue;

            // If it already has a solid collider, skip
            Collider existing = mf.GetComponent<Collider>();
            if (existing != null && !existing.isTrigger) continue;

            string goName = mf.gameObject.name.ToLower();
            string parentName = mf.transform.parent != null ? mf.transform.parent.name.ToLower() : "";
            string grandParentName = (mf.transform.parent != null && mf.transform.parent.parent != null) 
                ? mf.transform.parent.parent.name.ToLower() : "";

            // Exclude rotating door leaf meshes so they don't get static non-convex MeshColliders that freeze in the doorway
            if (goName.Contains("small_door") || goName.Contains("big_door_left") || goName.Contains("big_door_right")
                || goName.Contains("door_left") || goName.Contains("door_right"))
            {
                if (existing is MeshCollider)
                {
                    Destroy(existing);
                }
                continue;
            }

            bool isWallOrStructure = goName.Contains("wall") || goName.Contains("room") || goName.Contains("corner")
                                  || goName.Contains("window") || goName.Contains("frame") || goName.Contains("ceiling")
                                  || goName.Contains("floor") || goName.Contains("pillar")
                                  || parentName.Contains("wall") || parentName.Contains("room")
                                  || grandParentName.Contains("wall") || grandParentName.Contains("room");

            if (isWallOrStructure)
            {
                if (existing == null)
                {
                    MeshCollider mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    mc.convex = false; // Exact static polygonal mesh collision
                    addedCount++;
                }
                else if (existing.isTrigger)
                {
                    // If an existing collider was accidentally marked as trigger, make it solid
                    existing.isTrigger = false;
                    addedCount++;
                }
            }
        }

        Debug.Log($"<color=cyan><b>[WallCollisionEnforcer] 🧱 Enforced solid physical MeshColliders on {addedCount} wall/room meshes!</b></color>");
    }

    /// <summary>
    /// Ensures every doorway in the scene (small doors and big double doors) has a solid barrier
    /// while closed, preventing the player from passing through into other rooms without unlocking.
    /// Also ensures every door has an active interactive script (ExitDoor / BigDoor) and VRInteractableBridge.
    /// </summary>
    public static void EnforceDoorwayBarriers()
    {
        // 1. Enforce small door barriers (wall_with_small_door)
        GameObject[] allGos = FindObjectsByType<GameObject>(FindObjectsInactive.Include);
        int doorBarrierCount = 0;

        foreach (var go in allGos)
        {
            if (go == null) continue;
            string name = go.name.ToLower();

            // Check if this is a small door structure
            if (name.Contains("wall_with_small_door") || name == "small_door")
            {
                Transform doorRoot = name == "small_door" ? go.transform.parent : go.transform;
                if (doorRoot == null) continue;

                Transform barrier = doorRoot.Find("DoorwayPhysicalBarrier");
                if (barrier == null)
                {
                    GameObject bObj = new GameObject("DoorwayPhysicalBarrier");
                    bObj.transform.SetParent(doorRoot, false);
                    bObj.transform.localPosition = new Vector3(-0.6f, 1.1f, 0f);
                    bObj.transform.localRotation = Quaternion.identity;

                    BoxCollider bc = bObj.AddComponent<BoxCollider>();
                    bc.size = new Vector3(1.1f, 2.3f, 0.12f); // 12cm thick, fits doorway frame perfectly
                    bc.isTrigger = false;
                    doorBarrierCount++;
                }

                // Ensure ExitDoor exists on the door root so the player can interact with it
                if (doorRoot.GetComponent<ExitDoor>() == null)
                {
                    doorRoot.gameObject.AddComponent<ExitDoor>();
                }
                if (doorRoot.GetComponent<VRInteractableBridge>() == null)
                {
                    doorRoot.gameObject.AddComponent<VRInteractableBridge>();
                }
            }
            // Check if this is a big door structure
            else if (name.Contains("wall_with_big_door") || name.Contains("big_door"))
            {
                Transform doorRoot = go.transform;
                if (name.Contains("big_door_left") || name.Contains("big_door_right") || name.Contains("big_door_frame"))
                {
                    doorRoot = go.transform.parent;
                }
                if (doorRoot == null) continue;

                Transform barrier = doorRoot.Find("BigDoorwayPhysicalBarrier");
                if (barrier == null)
                {
                    GameObject bObj = new GameObject("BigDoorwayPhysicalBarrier");
                    bObj.transform.SetParent(doorRoot, false);
                    bObj.transform.localPosition = new Vector3(0f, 1.5f, 0f);
                    bObj.transform.localRotation = Quaternion.identity;

                    BoxCollider bc = bObj.AddComponent<BoxCollider>();
                    bc.size = new Vector3(2.5f, 3.0f, 0.15f); // 15cm thick, fits double door frame
                    bc.isTrigger = false;
                    doorBarrierCount++;
                }

                // Ensure BigDoor exists on the big door root so player can interact with it
                if (doorRoot.GetComponent<BigDoor>() == null)
                {
                    doorRoot.gameObject.AddComponent<BigDoor>();
                }
                if (doorRoot.GetComponent<VRInteractableBridge>() == null)
                {
                    doorRoot.gameObject.AddComponent<VRInteractableBridge>();
                }
            }
        }

        Debug.Log($"<color=cyan><b>[WallCollisionEnforcer] 🚪 Enforced physical doorway barriers on {doorBarrierCount} closed doors!</b></color>");
    }

    /// <summary>
    /// Configures CharacterController parameters to prevent wall tunneling or slipping.
    /// </summary>
    public static void ConfigureCharacterControllers()
    {
        CharacterController[] controllers = FindObjectsByType<CharacterController>(FindObjectsInactive.Include);
        foreach (var cc in controllers)
        {
            if (cc == null) continue;
            cc.skinWidth = 0.08f;
            cc.minMoveDistance = 0.001f;
            cc.stepOffset = 0.25f; // Cannot step over walls
            cc.slopeLimit = 45f;
        }
    }
}
