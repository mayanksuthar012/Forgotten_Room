using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Bridges any existing IInteractable (ExitDoor, BigDoor, ClueObject, BigDoorKey, Car1980)
/// with the Unity XR Interaction Toolkit in VR.
/// 
/// Attach this component to any interactive object in your scene:
/// - Automatically adds an XRSimpleInteractable if one doesn't exist.
/// - When a VR controller points and pulls the Trigger (select or activate),
///   it executes the object's Interact() method!
/// </summary>
[DisallowMultipleComponent]
public class VRInteractableBridge : MonoBehaviour
{
    [Tooltip("The interactable script on this object. Auto-detected if null.")]
    [SerializeField] private MonoBehaviour interactableTarget;

    private IInteractable targetInteractable;
    private XRSimpleInteractable xrInteractable;

    private void Awake()
    {
        // 1. Find IInteractable target
        if (interactableTarget != null && interactableTarget is IInteractable explicitTarget)
        {
            targetInteractable = explicitTarget;
        }
        else
        {
            targetInteractable = GetComponent<IInteractable>() ?? GetComponentInChildren<IInteractable>() ?? GetComponentInParent<IInteractable>();
        }

        // 2. Ensure XRSimpleInteractable is attached
        xrInteractable = GetComponent<XRSimpleInteractable>();
        if (xrInteractable == null)
        {
            xrInteractable = gameObject.AddComponent<XRSimpleInteractable>();
        }

        // 3. Ensure a Collider exists so VR Ray/Direct interactor can target this object
        if (GetComponent<Collider>() == null && GetComponentInChildren<Collider>() == null)
        {
            BoxCollider bc = gameObject.AddComponent<BoxCollider>();
            bc.size = Vector3.one;
        }

        // 4. Hook up VR controller events (Trigger pull / Grip pull)
        xrInteractable.selectEntered.AddListener(OnVRSelectEntered);
        xrInteractable.activated.AddListener(OnVRActivated);
    }

    private void OnDestroy()
    {
        if (xrInteractable != null)
        {
            xrInteractable.selectEntered.RemoveListener(OnVRSelectEntered);
            xrInteractable.activated.RemoveListener(OnVRActivated);
        }
    }

    private void OnVRSelectEntered(SelectEnterEventArgs args)
    {
        TriggerInteraction(args.interactorObject);
    }

    private void OnVRActivated(ActivateEventArgs args)
    {
        TriggerInteraction(args.interactorObject);
    }

    private void TriggerInteraction(IXRInteractor interactor)
    {
        if (targetInteractable != null)
        {
            Debug.Log($"<color=cyan>[VRInteractableBridge] 🥽 VR Interactor triggered '{name}': {targetInteractable.interactionText}</color>");
            targetInteractable.Interact();
        }
        else
        {
            Debug.LogWarning($"[VRInteractableBridge] No IInteractable found on '{name}'!");
        }
    }
}
