using UnityEngine;

namespace Molae.Gaze
{
    /// <summary>
    /// SeeSo TrackingState 미러. SDK 미설치 상태에서도 컴파일되도록 자체 정의한다.
    /// SeeSo 원본: SUCCESS / LOW_CONFIDENCE / UNSUPPORTED / FACE_MISSING
    /// </summary>
    public enum GazeTracking
    {
        Success = 0,
        LowConfidence = 1,
        Unsupported = 2,
        FaceMissing = 3,
    }

    /// <summary>SeeSo ScreenState 미러. INSIDE_OF_SCREEN / OUTSIDE_OF_SCREEN / UNKNOWN</summary>
    public enum GazeScreen
    {
        Inside = 0,
        Outside = 1,
        Unknown = 2,
    }

    /// <summary>SeeSo EyeMovementState 미러. FIXATION / SACCADE / UNKNOWN</summary>
    public enum GazeMovement
    {
        Fixation = 0,
        Saccade = 1,
        Unknown = 2,
    }

    /// <summary>시선 프로바이더의 수명주기 상태.</summary>
    public enum GazeProviderState
    {
        Idle,
        Initializing,
        Ready,
        Tracking,
        Failed,
    }

    /// <summary>
    /// 한 프레임분 시선 샘플. 좌표는 이미 Unity 스크린 좌표계(좌하단 원점)로 변환된 값이다.
    /// SeeSo 원본은 좌상단 원점이므로 프로바이더가 y를 뒤집어서 넣는다.
    /// </summary>
    public struct GazeSample
    {
        public long TimestampMs;
        public Vector2 Point;
        public GazeTracking Tracking;
        public GazeScreen Screen;
        public GazeMovement Movement;

        /// <summary>좌표를 신뢰할 수 있는 상태인지. UNSUPPORTED/FACE_MISSING이면 x,y가 무효다.</summary>
        public bool HasUsablePoint => Tracking == GazeTracking.Success || Tracking == GazeTracking.LowConfidence;

        /// <summary>얼굴이 아예 안 잡히는 상태. 게임오버가 아니라 일시정지 대상.</summary>
        public bool IsFaceMissing => Tracking == GazeTracking.FaceMissing;

        public static GazeSample Invalid => new GazeSample
        {
            TimestampMs = 0,
            Point = Vector2.zero,
            Tracking = GazeTracking.FaceMissing,
            Screen = GazeScreen.Unknown,
            Movement = GazeMovement.Unknown,
        };
    }

    /// <summary>캘리브레이션 진행 이벤트 종류.</summary>
    public enum CalibrationEventType
    {
        NextPoint,
        Progress,
        Finished,
        Failed,
    }

    /// <summary>스레드 경계를 넘어 큐잉되는 캘리브레이션 이벤트.</summary>
    public struct CalibrationEvent
    {
        public CalibrationEventType Type;
        /// <summary>NextPoint일 때 Unity 스크린 좌표(좌하단 원점).</summary>
        public Vector2 Point;
        /// <summary>Progress일 때 0~1.</summary>
        public float Progress;
    }
}
