using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Ensures the HUD Canvas (Crosshair, Prompts, Panels) renders as ScreenSpaceOverlay in the Editor/Desktop,
/// and as ScreenSpaceCamera attached in front of the VR camera in VR.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(Canvas))]
public class VRCanvasHelper : MonoBehaviour
{
    private Canvas canvas;

    private void Awake()
    {
        canvas = GetComponent<Canvas>();
        SyncCanvasMode();
    }

    private void OnEnable()
    {
        SyncCanvasMode();
    }

    private void Update()
    {
        SyncCanvasMode();
    }

    private void SyncCanvasMode()
    {
        if (canvas == null) canvas = GetComponent<Canvas>();
        if (canvas == null) return;

        if (VRDeviceDetection.IsVRActive())
        {
            if (canvas.renderMode != RenderMode.ScreenSpaceCamera)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.planeDistance = 1.0f;
            }
            if (canvas.worldCamera == null && Camera.main != null)
            {
                canvas.worldCamera = Camera.main;
            }
        }
        else
        {
            if (canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }
        }
    }
}
