using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace RoomTwin.AR
{
    /// <summary>
    /// Requests horizontal and vertical plane detection.
    /// Detected planes are visualization candidates only — not wall truth.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ARPlaneManager))]
    public sealed class ArPlaneDetectionController : MonoBehaviour
    {
        [SerializeField]
        ARPlaneManager planeManager;

        void Awake()
        {
            if (planeManager == null)
            {
                planeManager = GetComponent<ARPlaneManager>();
            }
        }

        void OnEnable()
        {
            if (planeManager == null)
            {
                return;
            }

            planeManager.requestedDetectionMode =
                PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;
        }
    }
}
