using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace RoomTwin.AR
{
    /// <summary>
    /// Thin outlines for detected vertical planes. They are not architectural wall edges.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ArScanWallOutlines : MonoBehaviour
    {
        static readonly Color Idle = new Color(0.75f, 0.9f, 1f, 0.35f);
        static readonly Color Selected = new Color(1f, 0.72f, 0.25f, 0.9f);
        static readonly Color Partner = new Color(0.45f, 0.95f, 0.7f, 0.8f);

        readonly Dictionary<TrackableId, LineRenderer> lines = new Dictionary<TrackableId, LineRenderer>();
        readonly List<TrackableId> stale = new List<TrackableId>();
        readonly List<Vector3> positions = new List<Vector3>(32);

        [SerializeField]
        Material lineMaterial;

        Material runtimeMaterial;

        public void Sync(
            ARPlaneManager manager,
            string selectedId,
            string partnerId,
            bool emphasizeSelected,
            bool hideAll,
            ICollection<string> visibleIds)
        {
            if (manager == null)
            {
                return;
            }

            if (hideAll)
            {
                foreach (KeyValuePair<TrackableId, LineRenderer> pair in lines)
                {
                    if (pair.Value != null)
                    {
                        pair.Value.enabled = false;
                    }
                }

                return;
            }

            Material material = LineMaterial();
            foreach (ARPlane plane in manager.trackables)
            {
                if (plane.alignment != PlaneAlignment.Vertical)
                {
                    continue;
                }

                string id = plane.trackableId.ToString();
                bool shown = visibleIds != null && (visibleIds.Contains(id) || id == selectedId || id == partnerId);
                if (!shown)
                {
                    if (lines.TryGetValue(plane.trackableId, out LineRenderer hidden) && hidden != null)
                    {
                        hidden.enabled = false;
                    }

                    continue;
                }

                LineRenderer line = GetLine(plane.trackableId, material);
                line.enabled = true;
                line.transform.SetPositionAndRotation(plane.transform.position, plane.transform.rotation);
                WriteBoundary(line, plane);
                bool selected = id == selectedId;
                line.startColor = line.endColor = selected ? Selected : id == partnerId ? Partner : Idle;
                line.startWidth = line.endWidth = selected && emphasizeSelected ? 0.02f : 0.008f;
            }

            stale.Clear();
            foreach (KeyValuePair<TrackableId, LineRenderer> pair in lines)
            {
                ARPlane plane = manager.GetPlane(pair.Key);
                if (plane == null || plane.alignment != PlaneAlignment.Vertical)
                {
                    stale.Add(pair.Key);
                }
            }

            for (int i = 0; i < stale.Count; i++)
            {
                if (lines.TryGetValue(stale[i], out LineRenderer line) && line != null)
                {
                    Destroy(line.gameObject);
                }

                lines.Remove(stale[i]);
            }
        }

        LineRenderer GetLine(TrackableId id, Material material)
        {
            if (lines.TryGetValue(id, out LineRenderer existing) && existing != null)
            {
                return existing;
            }

            var lineObject = new GameObject("WallOutline");
            lineObject.transform.SetParent(transform, false);
            var line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = material;
            line.numCapVertices = 0;
            line.numCornerVertices = 0;
            lines.Add(id, line);
            return line;
        }

        void WriteBoundary(LineRenderer line, ARPlane plane)
        {
            positions.Clear();
            if (plane.boundary.IsCreated && plane.boundary.Length >= 3)
            {
                for (int i = 0; i < plane.boundary.Length; i++)
                {
                    Vector2 point = plane.boundary[i];
                    positions.Add(new Vector3(point.x, 0.01f, point.y));
                }
            }
            else
            {
                Vector2 center = plane.centerInPlaneSpace;
                Vector2 extents = plane.extents;
                positions.Add(new Vector3(center.x - extents.x, 0.01f, center.y - extents.y));
                positions.Add(new Vector3(center.x + extents.x, 0.01f, center.y - extents.y));
                positions.Add(new Vector3(center.x + extents.x, 0.01f, center.y + extents.y));
                positions.Add(new Vector3(center.x - extents.x, 0.01f, center.y + extents.y));
            }

            line.positionCount = positions.Count;
            for (int i = 0; i < positions.Count; i++)
            {
                line.SetPosition(i, positions[i]);
            }
        }

        Material LineMaterial()
        {
            if (lineMaterial != null)
            {
                return lineMaterial;
            }

            if (runtimeMaterial != null)
            {
                return runtimeMaterial;
            }

            Shader shader = Shader.Find("RoomTwin/ArPlaneTransparent");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }

            runtimeMaterial = new Material(shader);
            Color color = new Color(0.7f, 0.9f, 1f, 0.45f);
            runtimeMaterial.color = color;
            if (runtimeMaterial.HasProperty("_BaseColor"))
            {
                runtimeMaterial.SetColor("_BaseColor", color);
            }

            return runtimeMaterial;
        }
    }
}
