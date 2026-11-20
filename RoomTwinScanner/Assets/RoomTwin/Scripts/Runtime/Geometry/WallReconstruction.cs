using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoomTwin.Geometry
{
    public enum WallTrackingQuality
    {
        None = 0,
        Limited = 1,
        Tracking = 2
    }

    /// <summary>
    /// One vertical plane observation. Boundary polygons are not required;
    /// half extents are the detected span in the wall plane.
    /// </summary>
    public struct WallObservation
    {
        public DetectedWall Wall;
        public float AreaSquareMeters;
        public float ObservedSeconds;
        public bool Subsumed;
        public WallTrackingQuality Tracking;
    }

    public struct FloorObservation
    {
        public string Id;
        public float Height;
        public float AreaSquareMeters;
        public float ObservedSeconds;
    }

    /// <summary>
    /// Inspector-tunable reconstruction thresholds. Zero angle means "use defaults".
    /// Values are assumptions from the 2026-11-20 field test, not surveyed tolerances.
    /// </summary>
    [Serializable]
    public struct WallClusterSettings
    {
        public float MaxNormalAngleDegrees;
        public float MaxPlaneOffsetMeters;
        public float MaxBoundaryGapMeters;
        public float MinimumConfidence;
        public float MinimumObservationSeconds;
        public float RectangleAngleToleranceDegrees;
        public float OppositeLengthTolerance;
        public float MinimumEdgeMeters;
        public float MinimumAreaSquareMeters;
        public float InconsistentScoreRatio;

        public static WallClusterSettings CreateDefault()
        {
            return new WallClusterSettings
            {
                MaxNormalAngleDegrees = 12f,
                MaxPlaneOffsetMeters = 0.18f,
                MaxBoundaryGapMeters = 1.2f,
                MinimumConfidence = 0.45f,
                MinimumObservationSeconds = 0.4f,
                RectangleAngleToleranceDegrees = 12f,
                OppositeLengthTolerance = 0.35f,
                MinimumEdgeMeters = 0.45f,
                MinimumAreaSquareMeters = 0.8f,
                InconsistentScoreRatio = 0.85f
            };
        }

        public static WallClusterSettings Resolve(WallClusterSettings value)
        {
            if (value.MaxNormalAngleDegrees <= 0f)
            {
                return CreateDefault();
            }

            return value;
        }
    }

    public sealed class WallCluster
    {
        public string Id;
        public readonly List<string> MemberIds = new List<string>(4);
        public DetectedWall Representative;
        public float Confidence;
        public float AreaSquareMeters;
        public float ObservedSeconds;
        public float VerticalMin;
        public float VerticalMax;
        public float HorizontalMin;
        public float HorizontalMax;
        public bool LowConfidence;
        public WallTrackingQuality Tracking;
        public string DiscardReason;

        public void Reset()
        {
            Id = string.Empty;
            MemberIds.Clear();
            Representative = default;
            Confidence = 0f;
            AreaSquareMeters = 0f;
            ObservedSeconds = 0f;
            VerticalMin = 0f;
            VerticalMax = 0f;
            HorizontalMin = 0f;
            HorizontalMax = 0f;
            LowConfidence = false;
            Tracking = WallTrackingQuality.None;
            DiscardReason = string.Empty;
        }
    }

    public struct RoomReconstruction
    {
        public bool Accepted;
        public string Rejection;
        public Vector3[] Corners;
        public string[] WallIds;
        public float[] EdgeLengths;
        public float Confidence;
    }

    /// <summary>
    /// Merges vertical plane fragments that lie on one physical wall.
    /// Parallel surfaces farther than the offset threshold stay separate.
    /// </summary>
    public sealed class WallClusterBuilder
    {
        readonly List<WallCluster> pool = new List<WallCluster>(16);
        int poolIndex;

        public List<WallCluster> Clusters { get; } = new List<WallCluster>(8);

        public List<WallCluster> Discarded { get; } = new List<WallCluster>(8);

        public void Rebuild(IReadOnlyList<WallObservation> samples, WallClusterSettings settings, float floorY, bool hasFloor)
        {
            settings = WallClusterSettings.Resolve(settings);
            poolIndex = 0;
            Clusters.Clear();
            Discarded.Clear();
            if (samples == null)
            {
                return;
            }

            for (int i = 0; i < samples.Count; i++)
            {
                WallObservation sample = samples[i];
                if (string.IsNullOrEmpty(sample.Wall.Id))
                {
                    continue;
                }

                if (sample.Subsumed)
                {
                    Discard(sample, "subsumed by another plane");
                    continue;
                }

                if (sample.Tracking == WallTrackingQuality.None)
                {
                    Discard(sample, "tracking lost");
                    continue;
                }

                if (sample.Wall.Normal.sqrMagnitude < 0.0001f)
                {
                    Discard(sample, "normal is degenerate");
                    continue;
                }

                WallCluster match = null;
                for (int c = 0; c < Clusters.Count; c++)
                {
                    if (SameWall(Clusters[c], sample.Wall, settings))
                    {
                        match = Clusters[c];
                        break;
                    }
                }

                if (match == null)
                {
                    match = Rent();
                    Seed(match, sample);
                    Clusters.Add(match);
                }
                else
                {
                    Absorb(match, sample);
                }
            }

            for (int i = 0; i < Clusters.Count; i++)
            {
                AssignConfidence(Clusters[i], settings, floorY, hasFloor);
            }
        }

        public WallCluster FindByMember(string planeId)
        {
            if (string.IsNullOrEmpty(planeId))
            {
                return null;
            }

            for (int i = 0; i < Clusters.Count; i++)
            {
                WallCluster cluster = Clusters[i];
                for (int m = 0; m < cluster.MemberIds.Count; m++)
                {
                    if (cluster.MemberIds[m] == planeId)
                    {
                        return cluster;
                    }
                }
            }

            return null;
        }

        void Discard(WallObservation sample, string reason)
        {
            WallCluster cluster = Rent();
            Seed(cluster, sample);
            cluster.DiscardReason = reason;
            cluster.Confidence = 0f;
            cluster.LowConfidence = true;
            Discarded.Add(cluster);
        }

        static void Seed(WallCluster cluster, WallObservation sample)
        {
            cluster.Reset();
            cluster.MemberIds.Add(sample.Wall.Id);
            cluster.Id = "wall-" + sample.Wall.Id;
            cluster.AreaSquareMeters = Mathf.Max(0f, sample.AreaSquareMeters);
            cluster.ObservedSeconds = Mathf.Max(0f, sample.ObservedSeconds);
            cluster.Tracking = sample.Tracking;
            Span(sample.Wall, sample.Wall.Normal, out float horizontalMin, out float horizontalMax, out float verticalMin, out float verticalMax);
            cluster.HorizontalMin = horizontalMin;
            cluster.HorizontalMax = horizontalMax;
            cluster.VerticalMin = verticalMin;
            cluster.VerticalMax = verticalMax;
            cluster.Representative = BuildRepresentative(cluster, AlignedNormal(sample.Wall.Normal, sample.Wall.Normal), sample.Wall.Point);
        }

        static void Absorb(WallCluster cluster, WallObservation sample)
        {
            Vector3 normal = AlignedNormal(sample.Wall.Normal, cluster.Representative.Normal);
            float area = Mathf.Max(sample.AreaSquareMeters, 0.01f);
            float total = cluster.AreaSquareMeters + area;
            Vector3 point = cluster.Representative.Point;
            if (total > 0.0001f)
            {
                point = (cluster.Representative.Point * cluster.AreaSquareMeters + sample.Wall.Point * area) / total;
                normal = (cluster.Representative.Normal * cluster.AreaSquareMeters + normal * area).normalized;
            }

            Span(sample.Wall, cluster.Representative.Normal, out float horizontalMin, out float horizontalMax, out float verticalMin, out float verticalMax);
            cluster.HorizontalMin = Mathf.Min(cluster.HorizontalMin, horizontalMin);
            cluster.HorizontalMax = Mathf.Max(cluster.HorizontalMax, horizontalMax);
            cluster.VerticalMin = Mathf.Min(cluster.VerticalMin, verticalMin);
            cluster.VerticalMax = Mathf.Max(cluster.VerticalMax, verticalMax);
            cluster.AreaSquareMeters = total;
            if (sample.ObservedSeconds > cluster.ObservedSeconds)
            {
                cluster.ObservedSeconds = sample.ObservedSeconds;
            }

            if (sample.Tracking == WallTrackingQuality.Tracking)
            {
                cluster.Tracking = WallTrackingQuality.Tracking;
            }

            cluster.MemberIds.Add(sample.Wall.Id);
            cluster.Id = StableId(cluster.MemberIds);
            cluster.Representative = BuildRepresentative(cluster, normal, point);
        }

        static void AssignConfidence(WallCluster cluster, WallClusterSettings settings, float floorY, bool hasFloor)
        {
            float vertical = Mathf.Max(0f, cluster.VerticalMax - cluster.VerticalMin);
            float areaScore = Saturate(cluster.AreaSquareMeters / 1.5f);
            float verticalScore = Saturate(vertical / 1.2f);
            float timeScore = Saturate(cluster.ObservedSeconds / 2f);
            float value = 0.15f + 0.25f * areaScore + 0.15f * verticalScore + 0.45f * timeScore;
            if (cluster.ObservedSeconds < settings.MinimumObservationSeconds)
            {
                value *= 0.5f;
            }

            if (cluster.Tracking == WallTrackingQuality.Limited)
            {
                value *= 0.6f;
            }

            if (hasFloor)
            {
                bool reachesFloor = cluster.VerticalMin <= floorY + 0.45f;
                if (!reachesFloor && cluster.AreaSquareMeters < 0.8f)
                {
                    value *= 0.75f;
                }
            }

            cluster.Confidence = Mathf.Clamp01(value);
            cluster.LowConfidence = cluster.Confidence < settings.MinimumConfidence;
        }

        static bool SameWall(WallCluster cluster, DetectedWall sample, WallClusterSettings settings)
        {
            Vector3 normal = AlignedNormal(sample.Normal, cluster.Representative.Normal);
            float dot = Mathf.Clamp(Vector3.Dot(normal, cluster.Representative.Normal.normalized), -1f, 1f);
            float angle = Mathf.Acos(dot) * Mathf.Rad2Deg;
            if (angle > settings.MaxNormalAngleDegrees)
            {
                return false;
            }

            float offset = Mathf.Abs(Vector3.Dot(cluster.Representative.Normal.normalized, sample.Point - cluster.Representative.Point));
            if (offset > settings.MaxPlaneOffsetMeters)
            {
                return false;
            }

            Span(sample, cluster.Representative.Normal, out float horizontalMin, out float horizontalMax, out _, out _);
            float gap = 0f;
            if (horizontalMax < cluster.HorizontalMin)
            {
                gap = cluster.HorizontalMin - horizontalMax;
            }
            else if (horizontalMin > cluster.HorizontalMax)
            {
                gap = horizontalMin - cluster.HorizontalMax;
            }

            return gap <= settings.MaxBoundaryGapMeters;
        }

        static Vector3 AlignedNormal(Vector3 normal, Vector3 reference)
        {
            Vector3 aligned = normal.normalized;
            if (Vector3.Dot(aligned, reference.normalized) < 0f)
            {
                aligned = -aligned;
            }

            return aligned;
        }

        static void Span(DetectedWall wall, Vector3 referenceNormal, out float horizontalMin, out float horizontalMax, out float verticalMin, out float verticalMax)
        {
            Vector3 normal = AlignedNormal(wall.Normal.sqrMagnitude > 0.0001f ? wall.Normal : Vector3.right, referenceNormal);
            Vector3 axis = Vector3.Cross(Vector3.up, normal);
            if (axis.sqrMagnitude < 0.0001f)
            {
                axis = Vector3.right;
            }
            else
            {
                axis.Normalize();
            }

            Vector3 center = wall.Center;
            Vector3 along = axis * wall.HalfExtents.x;
            Vector3 vertical = wall.AxisV * wall.HalfExtents.y;
            horizontalMin = float.PositiveInfinity;
            horizontalMax = float.NegativeInfinity;
            verticalMin = float.PositiveInfinity;
            verticalMax = float.NegativeInfinity;
            Accumulate(center + along + vertical, axis, ref horizontalMin, ref horizontalMax, ref verticalMin, ref verticalMax);
            Accumulate(center + along - vertical, axis, ref horizontalMin, ref horizontalMax, ref verticalMin, ref verticalMax);
            Accumulate(center - along + vertical, axis, ref horizontalMin, ref horizontalMax, ref verticalMin, ref verticalMax);
            Accumulate(center - along - vertical, axis, ref horizontalMin, ref horizontalMax, ref verticalMin, ref verticalMax);
        }

        static void Accumulate(
            Vector3 point,
            Vector3 axis,
            ref float horizontalMin,
            ref float horizontalMax,
            ref float verticalMin,
            ref float verticalMax)
        {
            float along = Vector3.Dot(point, axis);
            horizontalMin = Mathf.Min(horizontalMin, along);
            horizontalMax = Mathf.Max(horizontalMax, along);
            verticalMin = Mathf.Min(verticalMin, point.y);
            verticalMax = Mathf.Max(verticalMax, point.y);
        }

        static DetectedWall BuildRepresentative(WallCluster cluster, Vector3 normal, Vector3 point)
        {
            Vector3 aligned = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.right;
            Vector3 axis = Vector3.Cross(Vector3.up, aligned);
            if (axis.sqrMagnitude < 0.0001f)
            {
                axis = Vector3.right;
            }
            else
            {
                axis.Normalize();
            }

            float mid = (cluster.HorizontalMin + cluster.HorizontalMax) * 0.5f;
            float half = Mathf.Max(0.05f, (cluster.HorizontalMax - cluster.HorizontalMin) * 0.5f);
            float midY = (cluster.VerticalMin + cluster.VerticalMax) * 0.5f;
            float halfY = Mathf.Max(0.05f, (cluster.VerticalMax - cluster.VerticalMin) * 0.5f);
            float pointAlong = Vector3.Dot(point, axis);
            Vector3 center = point + axis * (mid - pointAlong);
            center.y = midY;
            Vector3 offset = center - point;
            offset -= aligned * Vector3.Dot(offset, aligned);
            center = point + offset;
            Vector3 axisV = Vector3.Cross(aligned, axis);
            if (axisV.sqrMagnitude < 0.0001f)
            {
                axisV = Vector3.up;
            }
            else
            {
                axisV.Normalize();
            }

            return new DetectedWall
            {
                Id = cluster.Id,
                Point = point,
                Normal = aligned,
                Center = center,
                AxisU = axis,
                AxisV = axisV,
                HalfExtents = new Vector2(half, halfY)
            };
        }

        static string StableId(List<string> memberIds)
        {
            string best = memberIds[0];
            for (int i = 1; i < memberIds.Count; i++)
            {
                if (string.CompareOrdinal(memberIds[i], best) < 0)
                {
                    best = memberIds[i];
                }
            }

            return "wall-" + best;
        }

        static float Saturate(float value)
        {
            if (value <= 0f)
            {
                return 0f;
            }

            return value >= 1f ? 1f : value;
        }

        WallCluster Rent()
        {
            if (poolIndex >= pool.Count)
            {
                pool.Add(new WallCluster());
            }

            WallCluster cluster = pool[poolIndex];
            poolIndex++;
            cluster.Reset();
            return cluster;
        }
    }

    /// <summary>
    /// Chooses a floor height from horizontal surfaces, camera height, and wall bottoms.
    /// The lowest or largest surface is not selected by itself.
    /// </summary>
    public sealed class FloorHeightEstimator
    {
        public const float AmbiguousScoreRatio = 1.6f;
        public const float WallSupportMeters = 0.4f;

        readonly List<FloorObservation> sorted = new List<FloorObservation>(8);
        readonly List<float> areas = new List<float>(8);
        readonly List<float> heights = new List<float>(8);
        readonly List<float> times = new List<float>(8);
        readonly List<float> scores = new List<float>(8);

        public bool TryChoose(
            IReadOnlyList<FloorObservation> samples,
            float cameraY,
            float toleranceMeters,
            IReadOnlyList<float> wallBottomYs,
            out float floorY,
            out string reason)
        {
            floorY = 0f;
            reason = null;
            sorted.Clear();
            areas.Clear();
            heights.Clear();
            times.Clear();
            scores.Clear();
            if (samples == null || samples.Count == 0)
            {
                reason = "Scan the floor. No HorizontalUp plane is available, so the floor height is not locked.";
                return false;
            }

            for (int i = 0; i < samples.Count; i++)
            {
                sorted.Add(samples[i]);
            }

            sorted.Sort(CompareHeight);
            samples = sorted;

            float clusterArea = 0f;
            float clusterWeightedHeight = 0f;
            float clusterWeightedTime = 0f;
            float clusterMean = 0f;
            bool open = false;
            for (int i = 0; i < samples.Count; i++)
            {
                FloorObservation sample = samples[i];
                if (!float.IsFinite(sample.Height) || !float.IsFinite(sample.AreaSquareMeters) || sample.AreaSquareMeters <= 0f)
                {
                    continue;
                }

                if (!open)
                {
                    clusterMean = sample.Height;
                    open = true;
                }
                else if (Mathf.Abs(sample.Height - clusterMean) > toleranceMeters)
                {
                    PushCluster(clusterArea, clusterWeightedHeight, clusterWeightedTime);
                    clusterArea = 0f;
                    clusterWeightedHeight = 0f;
                    clusterWeightedTime = 0f;
                    clusterMean = sample.Height;
                }

                float seconds = sample.ObservedSeconds > 0f ? sample.ObservedSeconds : 0f;
                clusterArea += sample.AreaSquareMeters;
                clusterWeightedHeight += sample.Height * sample.AreaSquareMeters;
                clusterWeightedTime += seconds * sample.AreaSquareMeters;
                clusterMean = clusterWeightedHeight / clusterArea;
            }

            if (open)
            {
                PushCluster(clusterArea, clusterWeightedHeight, clusterWeightedTime);
            }

            if (areas.Count == 0)
            {
                reason = "Scan the floor. The horizontal surfaces found are not large enough to lock a floor height.";
                return false;
            }

            for (int i = 0; i < areas.Count; i++)
            {
                float height = heights[i];
                float timeScore = Saturate(times[i] / 2f);
                float score = areas[i] * (0.5f + timeScore) * CameraFactor(cameraY, height) * WallFactor(height, wallBottomYs);
                scores.Add(score);
            }

            int best = 0;
            int second = -1;
            for (int i = 1; i < scores.Count; i++)
            {
                if (scores[i] > scores[best])
                {
                    second = best;
                    best = i;
                }
                else if (second < 0 || scores[i] > scores[second])
                {
                    second = i;
                }
            }

            if (areas[best] < 0.25f && WallFactor(heights[best], wallBottomYs) < 1.2f)
            {
                reason = "Scan the floor. The horizontal surfaces found are not large enough to lock a floor height.";
                return false;
            }

            if (second >= 0 && scores[best] < scores[second] * AmbiguousScoreRatio)
            {
                reason =
                    "Floor height is uncertain. More than one large horizontal surface is visible. " +
                    "Scan the room floor until one surface is clearly larger.";
                return false;
            }

            floorY = heights[best];
            reason = wallBottomYs != null && wallBottomYs.Count > 0
                ? "height cluster agreed with observed wall bottoms"
                : "height cluster was the stable dominant surface below the camera";
            return true;
        }

        static int CompareHeight(FloorObservation a, FloorObservation b)
        {
            return a.Height.CompareTo(b.Height);
        }

        void PushCluster(float area, float weightedHeight, float weightedTime)
        {
            areas.Add(area);
            heights.Add(weightedHeight / area);
            times.Add(area > 0f ? weightedTime / area : 0f);
        }

        static float CameraFactor(float cameraY, float height)
        {
            if (!float.IsFinite(cameraY))
            {
                return 1f;
            }

            float drop = cameraY - height;
            if (drop < 0.3f)
            {
                return 0.2f;
            }

            return 1f;
        }

        static float WallFactor(float height, IReadOnlyList<float> wallBottomYs)
        {
            if (wallBottomYs == null || wallBottomYs.Count == 0)
            {
                return 1f;
            }

            int supporting = 0;
            int below = 0;
            for (int i = 0; i < wallBottomYs.Count; i++)
            {
                float bottom = wallBottomYs[i];
                if (!float.IsFinite(bottom))
                {
                    continue;
                }

                float delta = bottom - height;
                if (Mathf.Abs(delta) <= WallSupportMeters)
                {
                    supporting++;
                }
                else if (delta < -0.45f)
                {
                    below++;
                }
            }

            if (supporting > 0 && below == 0)
            {
                return 1.5f;
            }

            if (supporting > 0)
            {
                return 1.1f;
            }

            if (below > 0)
            {
                return 0.35f;
            }

            return 0.85f;
        }

        static float Saturate(float value)
        {
            if (value <= 0f)
            {
                return 0f;
            }

            return value >= 1f ? 1f : value;
        }
    }

    /// <summary>
    /// Builds one closed quad from wall clusters. It does not invent a missing wall
    /// and does not square a room whose observations are not orthogonal.
    /// </summary>
    public sealed class RoomReconstructionSolver
    {
        readonly List<WallCluster> usable = new List<WallCluster>(8);
        readonly List<float> directions = new List<float>(8);
        readonly WallCluster[] quad = new WallCluster[4];
        readonly WallCluster[] ordered = new WallCluster[4];
        readonly Vector3[] corners = new Vector3[4];
        readonly string[] ids = new string[4];
        readonly float[] edges = new float[4];
        readonly List<string> bestKey = new List<string>(4);
        readonly List<string> candidateKey = new List<string>(4);
        readonly Vector2[] scratchXz = new Vector2[4];
        readonly RoomGeometryData scratchRoom = new RoomGeometryData { wallHeightMeters = RoomGeometryData.DefaultWallHeightMeters };

        public bool TryBuild(
            IReadOnlyList<WallCluster> clusters,
            float floorY,
            IReadOnlyList<string> requiredClusterIds,
            WallClusterSettings settings,
            out RoomReconstruction result)
        {
            settings = WallClusterSettings.Resolve(settings);
            result = new RoomReconstruction { Rejection = "Not enough wall information to close a room." };
            usable.Clear();
            directions.Clear();
            if (!float.IsFinite(floorY) || clusters == null)
            {
                result.Rejection = "Scan the floor before closing the room.";
                return false;
            }

            if (!CollectUsable(clusters, requiredClusterIds, settings, ref result))
            {
                return false;
            }

            if (directions.Count < 2)
            {
                result.Rejection = "Not enough wall information. Scan a wall that faces another direction.";
                return false;
            }

            if (usable.Count < 4)
            {
                result.Rejection = "Not enough wall information. An opposite wall was not observed.";
                return false;
            }

            LimitUsable(requiredClusterIds);
            bool found = false;
            float bestScore = float.NegativeInfinity;
            float secondScore = float.NegativeInfinity;
            bool secondDifferent = false;
            bool sawAmbiguous = false;
            string failure = result.Rejection;
            Vector3[] bestCorners = null;
            string[] bestIds = null;
            float[] bestEdges = null;
            float bestConfidence = 0f;

            int count = usable.Count;
            for (int i = 0; i < count; i++)
            {
                for (int j = i + 1; j < count; j++)
                {
                    for (int k = j + 1; k < count; k++)
                    {
                        for (int l = k + 1; l < count; l++)
                        {
                            if (!Evaluate(usable[i], usable[j], usable[k], usable[l], floorY, requiredClusterIds, settings, out float score, out string rejection))
                            {
                                if (!string.IsNullOrEmpty(rejection))
                                {
                                    failure = rejection;
                                    if (rejection.IndexOf("ambiguous", StringComparison.Ordinal) >= 0)
                                    {
                                        sawAmbiguous = true;
                                    }
                                }

                                continue;
                            }

                            CopyKey(ids, candidateKey);
                            if (!found || score > bestScore)
                            {
                                if (found && !SameKey(candidateKey, bestKey))
                                {
                                    secondScore = bestScore;
                                    secondDifferent = true;
                                }

                                bestScore = score;
                                found = true;
                                bestConfidence = score;
                                CopyKey(ids, bestKey);
                                bestCorners = CopyCorners();
                                bestIds = CopyIds();
                                bestEdges = CopyEdges();
                            }
                            else if (score > secondScore && !SameKey(candidateKey, bestKey))
                            {
                                secondScore = score;
                                secondDifferent = true;
                            }
                        }
                    }
                }
            }

            if (!found)
            {
                result.Rejection = sawAmbiguous
                    ? "The selected wall observation is ambiguous. Scan that wall longer."
                    : failure;
                return false;
            }

            if (secondDifferent && secondScore >= bestScore * settings.InconsistentScoreRatio)
            {
                result.Rejection = "Wall candidates are inconsistent. Scan the corners until one closed room remains.";
                return false;
            }

            result.Accepted = true;
            result.Rejection = string.Empty;
            result.Corners = bestCorners;
            result.WallIds = bestIds;
            result.EdgeLengths = bestEdges;
            result.Confidence = bestConfidence;
            return true;
        }

        bool CollectUsable(
            IReadOnlyList<WallCluster> clusters,
            IReadOnlyList<string> requiredClusterIds,
            WallClusterSettings settings,
            ref RoomReconstruction result)
        {
            for (int r = 0; requiredClusterIds != null && r < requiredClusterIds.Count; r++)
            {
                WallCluster required = Find(clusters, requiredClusterIds[r]);
                if (required == null || !string.IsNullOrEmpty(required.DiscardReason))
                {
                    result.Rejection = "The selected wall is not a stable wall candidate.";
                    return false;
                }

                if (required.LowConfidence || required.Confidence < settings.MinimumConfidence)
                {
                    result.Rejection = "The selected wall observation is ambiguous. Scan that wall longer.";
                    return false;
                }
            }

            for (int i = 0; i < clusters.Count; i++)
            {
                WallCluster cluster = clusters[i];
                if (!string.IsNullOrEmpty(cluster.DiscardReason) || cluster.LowConfidence)
                {
                    continue;
                }

                if (cluster.Confidence < settings.MinimumConfidence)
                {
                    continue;
                }

                usable.Add(cluster);
                float direction = FoldedDirection(cluster.Representative.Normal);
                bool grouped = false;
                for (int g = 0; g < directions.Count; g++)
                {
                    if (Mathf.Abs(directions[g] - direction) <= settings.MaxNormalAngleDegrees)
                    {
                        grouped = true;
                        break;
                    }
                }

                if (!grouped)
                {
                    directions.Add(direction);
                }
            }

            return true;
        }

        void LimitUsable(IReadOnlyList<string> requiredClusterIds)
        {
            const int max = 8;
            if (usable.Count <= max)
            {
                return;
            }

            usable.Sort(ConfidenceComparer.Instance);
            for (int i = max; i < usable.Count; i++)
            {
                if (!IsRequired(usable[i], requiredClusterIds))
                {
                    continue;
                }

                for (int j = max - 1; j >= 0; j--)
                {
                    if (IsRequired(usable[j], requiredClusterIds))
                    {
                        continue;
                    }

                    WallCluster swap = usable[j];
                    usable[j] = usable[i];
                    usable[i] = swap;
                    break;
                }
            }

            if (usable.Count > max)
            {
                usable.RemoveRange(max, usable.Count - max);
            }
        }

        sealed class ConfidenceComparer : IComparer<WallCluster>
        {
            public static readonly ConfidenceComparer Instance = new ConfidenceComparer();

            public int Compare(WallCluster a, WallCluster b)
            {
                return b.Confidence.CompareTo(a.Confidence);
            }
        }

        bool Evaluate(
            WallCluster a,
            WallCluster b,
            WallCluster c,
            WallCluster d,
            float floorY,
            IReadOnlyList<string> requiredClusterIds,
            WallClusterSettings settings,
            out float score,
            out string rejection)
        {
            score = 0f;
            rejection = string.Empty;
            quad[0] = a;
            quad[1] = b;
            quad[2] = c;
            quad[3] = d;
            OrderAroundCentroid();
            if (requiredClusterIds != null)
            {
                for (int r = 0; r < requiredClusterIds.Count; r++)
                {
                    if (!ContainsId(requiredClusterIds[r]))
                    {
                        rejection = "The selected walls do not close one four-sided room.";
                        return false;
                    }
                }
            }

            for (int i = 0; i < 4; i++)
            {
                if (ordered[i].LowConfidence)
                {
                    rejection = "A wall candidate is ambiguous, so the room was not created.";
                    return false;
                }

                WallCluster next = ordered[(i + 1) % 4];
                if (!WallCornerSolver.TryPair(ordered[i].Representative, next.Representative, floorY, out WallCornerEstimate estimate))
                {
                    rejection = estimate.Rejection;
                    return false;
                }

                corners[i] = estimate.Corner;
            }

            for (int i = 0; i < 4; i++)
            {
                edges[i] = Vector2.Distance(new Vector2(corners[i].x, corners[i].z), new Vector2(corners[(i + 1) % 4].x, corners[(i + 1) % 4].z));
                if (edges[i] < settings.MinimumEdgeMeters)
                {
                    rejection = "A generated edge is shorter than the room minimum.";
                    return false;
                }
            }

            for (int i = 0; i < 4; i++)
            {
                scratchXz[i] = new Vector2(corners[i].x, corners[i].z);
            }

            scratchRoom.floorCornersXz = scratchXz;
            scratchRoom.wallHeightMeters = RoomGeometryData.DefaultWallHeightMeters;
            if (!RoomGeometryValidator.TryValidate(scratchRoom, out Vector2[] validated, out string error))
            {
                rejection = error;
                return false;
            }

            if (Mathf.Abs(RoomGeometryValidator.SignedArea(validated)) < settings.MinimumAreaSquareMeters)
            {
                rejection = "The closed outline area is below the room minimum.";
                return false;
            }

            if (!ObservedEdgesMatch(settings, out rejection))
            {
                return false;
            }

            if (IsRectangle(settings) && !OppositeLengthsMatch(settings))
            {
                rejection = "Opposite walls are not consistent with a rectangle supported by the observations.";
                return false;
            }

            float confidence = 0f;
            for (int i = 0; i < 4; i++)
            {
                confidence += ordered[i].Confidence;
                ids[i] = ordered[i].Id;
            }

            ApplyValidatedOrder(validated);
            for (int i = 0; i < 4; i++)
            {
                edges[i] = Vector2.Distance(
                    new Vector2(corners[i].x, corners[i].z),
                    new Vector2(corners[(i + 1) % 4].x, corners[(i + 1) % 4].z));
            }

            score = confidence + Mathf.Abs(RoomGeometryValidator.SignedArea(validated)) * 0.05f;
            return true;
        }

        void OrderAroundCentroid()
        {
            float cx = 0f;
            float cz = 0f;
            for (int i = 0; i < 4; i++)
            {
                ordered[i] = quad[i];
                cx += quad[i].Representative.Center.x;
                cz += quad[i].Representative.Center.z;
            }

            cx *= 0.25f;
            cz *= 0.25f;
            for (int i = 1; i < 4; i++)
            {
                WallCluster key = ordered[i];
                float keyAngle = Mathf.Atan2(key.Representative.Center.z - cz, key.Representative.Center.x - cx);
                int j = i - 1;
                while (j >= 0 && Mathf.Atan2(ordered[j].Representative.Center.z - cz, ordered[j].Representative.Center.x - cx) > keyAngle)
                {
                    ordered[j + 1] = ordered[j];
                    j--;
                }

                ordered[j + 1] = key;
            }
        }

        bool ContainsId(string id)
        {
            for (int i = 0; i < 4; i++)
            {
                if (ordered[i].Id == id)
                {
                    return true;
                }
            }

            return false;
        }

        void ApplyValidatedOrder(Vector2[] validated)
        {
            var source = new Vector2[4];
            var sourceCorners = new Vector3[4];
            var sourceIds = new string[4];
            for (int i = 0; i < 4; i++)
            {
                source[i] = new Vector2(corners[i].x, corners[i].z);
                sourceCorners[i] = corners[i];
                sourceIds[i] = ids[i];
            }

            var used = new bool[4];
            for (int i = 0; i < 4; i++)
            {
                int match = 0;
                float best = float.PositiveInfinity;
                for (int s = 0; s < 4; s++)
                {
                    if (used[s])
                    {
                        continue;
                    }

                    float distance = Vector2.Distance(validated[i], source[s]);
                    if (distance < best)
                    {
                        best = distance;
                        match = s;
                    }
                }

                used[match] = true;
                corners[i] = sourceCorners[match];
                ids[i] = sourceIds[match];
            }
        }

        bool ObservedEdgesMatch(WallClusterSettings settings, out string rejection)
        {
            for (int i = 0; i < 4; i++)
            {
                WallCluster wall = ordered[i];
                Vector3 previous = corners[(i + 3) % 4];
                Vector3 current = corners[i];
                Vector3 midpoint = (previous + current) * 0.5f;
                Vector3 normal = wall.Representative.Normal.normalized;
                float planeDistance = Mathf.Abs(Vector3.Dot(normal, midpoint - wall.Representative.Point));
                if (planeDistance > settings.MaxPlaneOffsetMeters)
                {
                    rejection = "The generated boundary is too far from an observed wall.";
                    return false;
                }
            }

            rejection = string.Empty;
            return true;
        }

        bool IsRectangle(WallClusterSettings settings)
        {
            float oppositeA = Mathf.Abs(FoldedDirection(ordered[0].Representative.Normal) - FoldedDirection(ordered[2].Representative.Normal));
            float oppositeB = Mathf.Abs(FoldedDirection(ordered[1].Representative.Normal) - FoldedDirection(ordered[3].Representative.Normal));
            float between = Mathf.Abs(FoldedDirection(ordered[0].Representative.Normal) - FoldedDirection(ordered[1].Representative.Normal));
            return oppositeA <= settings.MaxNormalAngleDegrees
                && oppositeB <= settings.MaxNormalAngleDegrees
                && Mathf.Abs(between - 90f) <= settings.RectangleAngleToleranceDegrees;
        }

        bool OppositeLengthsMatch(WallClusterSettings settings)
        {
            float longerA = Mathf.Max(edges[0], edges[2]);
            float longerB = Mathf.Max(edges[1], edges[3]);
            if (longerA <= 0.0001f || longerB <= 0.0001f)
            {
                return false;
            }

            return Mathf.Abs(edges[0] - edges[2]) / longerA <= settings.OppositeLengthTolerance
                && Mathf.Abs(edges[1] - edges[3]) / longerB <= settings.OppositeLengthTolerance;
        }

        Vector3[] CopyCorners()
        {
            return new[] { corners[0], corners[1], corners[2], corners[3] };
        }

        string[] CopyIds()
        {
            return new[] { ids[0], ids[1], ids[2], ids[3] };
        }

        float[] CopyEdges()
        {
            return new[] { edges[0], edges[1], edges[2], edges[3] };
        }

        static void CopyKey(string[] source, List<string> target)
        {
            target.Clear();
            for (int i = 0; i < source.Length; i++)
            {
                target.Add(source[i]);
            }

            target.Sort(StringComparer.Ordinal);
        }

        static bool SameKey(List<string> a, List<string> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }

            for (int i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }

        static WallCluster Find(IReadOnlyList<WallCluster> clusters, string id)
        {
            for (int i = 0; i < clusters.Count; i++)
            {
                if (clusters[i].Id == id)
                {
                    return clusters[i];
                }
            }

            return null;
        }

        static bool IsRequired(WallCluster cluster, IReadOnlyList<string> requiredClusterIds)
        {
            if (requiredClusterIds == null)
            {
                return false;
            }

            for (int i = 0; i < requiredClusterIds.Count; i++)
            {
                if (requiredClusterIds[i] == cluster.Id)
                {
                    return true;
                }
            }

            return false;
        }

        public static float FoldedDirection(Vector3 normal)
        {
            Vector3 horizontal = new Vector3(normal.x, 0f, normal.z);
            if (horizontal.sqrMagnitude < 0.000001f)
            {
                return 0f;
            }

            horizontal.Normalize();
            if (horizontal.x < 0f || (Mathf.Abs(horizontal.x) <= 0.0001f && horizontal.z < 0f))
            {
                horizontal = -horizontal;
            }

            return Mathf.Atan2(horizontal.z, horizontal.x) * Mathf.Rad2Deg;
        }
    }
}
