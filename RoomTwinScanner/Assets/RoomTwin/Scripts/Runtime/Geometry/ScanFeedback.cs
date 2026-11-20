using System;

namespace RoomTwin.Geometry
{
    public enum ScanPhase
    {
        Tracking,
        NeedFloor,
        FloorUncertain,
        NeedWall,
        NeedAdjacentWall,
        ReadyToEstimate,
        RoomBuilt
    }

    public struct ScanSnapshot
    {
        public bool IsTracking;
        public bool FloorLocked;
        public bool FloorUncertain;
        public int VerticalWallCount;
        public bool HasIntersectablePair;
        public int CornerCount;
        public bool RoomBuilt;
    }

    /// <summary>
    /// Maps measured AR counts to scan copy. It does not advance with elapsed time.
    /// </summary>
    public static class ScanFeedback
    {
        public static ScanPhase Resolve(in ScanSnapshot snapshot)
        {
            if (!snapshot.IsTracking)
            {
                return ScanPhase.Tracking;
            }

            if (snapshot.RoomBuilt)
            {
                return ScanPhase.RoomBuilt;
            }

            if (!snapshot.FloorLocked)
            {
                return snapshot.FloorUncertain ? ScanPhase.FloorUncertain : ScanPhase.NeedFloor;
            }

            if (snapshot.VerticalWallCount <= 0)
            {
                return ScanPhase.NeedWall;
            }

            if (snapshot.VerticalWallCount == 1 || !snapshot.HasIntersectablePair)
            {
                return ScanPhase.NeedAdjacentWall;
            }

            return ScanPhase.ReadyToEstimate;
        }

        public static string ProgressLabel(int cornerCount)
        {
            int count = cornerCount < 0 ? 0 : cornerCount > 4 ? 4 : cornerCount;
            return count + "/4";
        }

        public static string Headline(ScanPhase phase)
        {
            return phase switch
            {
                ScanPhase.Tracking => "추적 준비 중",
                ScanPhase.NeedFloor => "바닥을 비춰 주세요",
                ScanPhase.FloorUncertain => "바닥 높이가 불확실합니다",
                ScanPhase.NeedWall => "벽을 비춰 주세요",
                ScanPhase.NeedAdjacentWall => "인접한 벽을 더 스캔하세요",
                ScanPhase.ReadyToEstimate => "방향이 다른 벽이 있습니다",
                ScanPhase.RoomBuilt => "방 윤곽을 만들었습니다",
                _ => "스캔 중"
            };
        }

        public static string Guidance(ScanPhase phase, int cornerCount, string failure)
        {
            if (!string.IsNullOrEmpty(failure))
            {
                return failure;
            }

            if (phase == ScanPhase.ReadyToEstimate && cornerCount >= 4)
            {
                return "방 윤곽 미리보기입니다. 맞으면 확정하세요. 추정 길이는 실측이 아닙니다.";
            }

            if (phase == ScanPhase.ReadyToEstimate && cornerCount > 0)
            {
                return "모서리 " + ProgressLabel(cornerCount) + "을 추정했습니다. 다음 벽을 선택하세요.";
            }

            return phase switch
            {
                ScanPhase.Tracking => "카메라가 공간을 인식할 때까지 천천히 비춰 주세요.",
                ScanPhase.NeedFloor => "방 바닥이 보이도록 카메라를 내려 주세요.",
                ScanPhase.FloorUncertain => "바닥과 다른 수평면이 함께 보입니다. 방 바닥을 더 넓게 비춰 주세요.",
                ScanPhase.NeedWall => "벽면이 보이도록 카메라를 옮겨 주세요.",
                ScanPhase.NeedAdjacentWall => "만나는 다른 벽을 더 스캔하세요.",
                ScanPhase.ReadyToEstimate => "벽을 선택하면 교차를 계산합니다. 경계 밖이면 모서리는 생기지 않습니다.",
                ScanPhase.RoomBuilt => "방을 걸어 보며 윤곽이 바닥에 고정되는지 확인하세요.",
                _ => "벽과 바닥을 스캔하세요."
            };
        }

        public static string ToKorean(string reason)
        {
            if (string.IsNullOrEmpty(reason))
            {
                return string.Empty;
            }

            if (reason.IndexOf("not enough", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "벽 정보가 부족합니다. 다른 방향의 벽을 더 비춰 주세요.";
            }

            if (reason.Contains("ambiguous"))
            {
                return "벽 후보가 불확실합니다. 그 벽을 더 오래 비춰 주세요.";
            }

            if (reason.Contains("inconsistent"))
            {
                return "벽 후보가 서로 맞지 않습니다. 모서리 주변을 더 비춰 주세요.";
            }

            if (reason.Contains("not a stable"))
            {
                return "안정적인 벽 후보가 아닙니다. 벽을 더 비춰 주세요.";
            }

            if (reason.Contains("parallel"))
            {
                return "두 벽이 거의 평행해서 모서리를 계산하지 않았습니다.";
            }

            if (reason.Contains("too far") || reason.Contains("outside"))
            {
                return "검출된 벽 범위 안에서 모서리가 만나지 않습니다. 모서리 주변을 더 비춰 주세요.";
            }

            if (reason.Contains("adjacent wall"))
            {
                return "인접한 벽을 더 스캔하세요.";
            }

            if (reason.Contains("uncertain") || reason.Contains("too small") || reason.Contains("not large enough"))
            {
                return "바닥 높이가 확실하지 않습니다. 방 바닥을 더 비춰 주세요.";
            }

            if (reason.Contains("No HorizontalUp") || reason.Contains("Scan the floor"))
            {
                return "바닥을 더 스캔하세요.";
            }

            if (reason.Contains("vertical wall"))
            {
                return "벽을 선택하세요. 바닥을 누르면 모서리가 되지 않습니다.";
            }

            if (reason.Contains("No plane hit"))
            {
                return "인식된 평면이 없습니다. 벽과 바닥을 더 비춰 주세요.";
            }

            return "모서리를 확정하지 못했습니다. 벽이 만나는 곳을 더 비춰 주세요.";
        }
    }
}
