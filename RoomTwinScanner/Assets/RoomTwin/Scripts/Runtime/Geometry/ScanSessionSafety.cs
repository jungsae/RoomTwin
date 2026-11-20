using System.Collections.Generic;
using UnityEngine;

namespace RoomTwin.Geometry
{
    /// <summary>
    /// Keeps planes from the scan that was reset out of the next session.
    /// Waiting ends when tracking is ready and the old ids are gone, or when the
    /// timeout elapses so a missed state event cannot block the session forever.
    /// Retired ids stay blocked either way.
    /// </summary>
    public sealed class RescanGate
    {
        public const float DefaultTimeoutSeconds = 12f;

        readonly HashSet<string> retiredIds = new HashSet<string>();

        public bool Waiting { get; private set; }
        public bool PlanesCleared { get; private set; }
        public bool TimedOut { get; private set; }
        public int Generation { get; private set; }

        float startedAt = float.NegativeInfinity;

        public void Begin(IReadOnlyList<string> currentIds, float now)
        {
            Generation++;
            Waiting = true;
            TimedOut = false;
            retiredIds.Clear();
            if (currentIds != null)
            {
                for (int i = 0; i < currentIds.Count; i++)
                {
                    if (!string.IsNullOrEmpty(currentIds[i]))
                    {
                        retiredIds.Add(currentIds[i]);
                    }
                }
            }

            PlanesCleared = retiredIds.Count == 0;
            startedAt = now;
        }

        public bool IsRetired(string id)
        {
            return !string.IsNullOrEmpty(id) && retiredIds.Contains(id);
        }

        public void Observe(IReadOnlyList<string> visibleIds, bool sessionTracking, float now)
        {
            if (!Waiting)
            {
                return;
            }

            bool retiredVisible = false;
            if (visibleIds != null)
            {
                for (int i = 0; i < visibleIds.Count; i++)
                {
                    if (IsRetired(visibleIds[i]))
                    {
                        retiredVisible = true;
                        break;
                    }
                }
            }

            if (!retiredVisible)
            {
                PlanesCleared = true;
            }

            if (now - startedAt >= DefaultTimeoutSeconds)
            {
                TimedOut = true;
            }

            if ((sessionTracking && PlanesCleared) || TimedOut)
            {
                Waiting = false;
            }
        }
    }

    /// <summary>
    /// Moves a selected plane id onto the plane that subsumed it.
    /// A lost id is removed and reported so the caller can withhold the outline.
    /// </summary>
    public static class WallSelectionTransfer
    {
        public static void Resolve(
            List<string> intendedIds,
            IReadOnlyList<string> liveMemberIds,
            IReadOnlyDictionary<string, string> subsumedBy,
            out bool transferred,
            out bool lost)
        {
            transferred = false;
            lost = false;
            if (intendedIds == null)
            {
                return;
            }

            for (int i = intendedIds.Count - 1; i >= 0; i--)
            {
                string id = intendedIds[i];
                if (Contains(liveMemberIds, id))
                {
                    continue;
                }

                if (TryFollow(id, liveMemberIds, subsumedBy, out string parent))
                {
                    intendedIds[i] = parent;
                    transferred = true;
                    continue;
                }

                intendedIds.RemoveAt(i);
                lost = true;
            }
        }

        static bool TryFollow(
            string id,
            IReadOnlyList<string> liveMemberIds,
            IReadOnlyDictionary<string, string> subsumedBy,
            out string resolved)
        {
            resolved = null;
            if (subsumedBy == null)
            {
                return false;
            }

            string current = id;
            for (int hop = 0; hop < 4; hop++)
            {
                if (!subsumedBy.TryGetValue(current, out string parent) || string.IsNullOrEmpty(parent))
                {
                    return false;
                }

                if (Contains(liveMemberIds, parent))
                {
                    resolved = parent;
                    return true;
                }

                current = parent;
            }

            return false;
        }

        static bool Contains(IReadOnlyList<string> ids, string id)
        {
            if (ids == null || string.IsNullOrEmpty(id))
            {
                return false;
            }

            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i] == id)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public static class PreviewConfirmation
    {
        public const float CornerChangeMeters = 0.25f;

        public static bool CanConfirm(bool hasPreview, bool trackingReady, bool waitingForRescan, bool meshBuilt)
        {
            return hasPreview && trackingReady && !waitingForRescan && !meshBuilt;
        }

        public static bool ShouldClear(bool waitingForRescan, bool trackingReady, bool floorValid, bool selectionKept, bool outlineValid)
        {
            return waitingForRescan || !trackingReady || !floorValid || !selectionKept || !outlineValid;
        }

        public static bool CornersMoved(Vector3[] previous, Vector3[] latest, float toleranceMeters)
        {
            if (previous == null || latest == null || previous.Length != latest.Length || previous.Length == 0)
            {
                return true;
            }

            float limit = toleranceMeters * toleranceMeters;
            var used = new bool[latest.Length];
            for (int i = 0; i < previous.Length; i++)
            {
                int match = -1;
                float best = float.PositiveInfinity;
                for (int j = 0; j < latest.Length; j++)
                {
                    if (used[j])
                    {
                        continue;
                    }

                    float distance = (previous[i] - latest[j]).sqrMagnitude;
                    if (distance < best)
                    {
                        best = distance;
                        match = j;
                    }
                }

                if (match < 0 || best > limit)
                {
                    return true;
                }

                used[match] = true;
            }

            return false;
        }
    }
}
