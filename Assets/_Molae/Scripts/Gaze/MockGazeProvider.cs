using System.Collections.Generic;
using UnityEngine;

namespace Molae.Gaze
{
    /// <summary>
    /// 에디터/데스크톱용 가짜 시선 프로바이더. 마우스 커서를 시선으로 취급한다.
    /// SeeSo 없이도 게임 루프 전체를 돌려볼 수 있게 해주는 개발 필수 장치.
    ///
    /// 조작:
    ///   마우스 이동          → 시선 좌표
    ///   마우스 화면 밖       → OUTSIDE_OF_SCREEN
    ///   F 키 누르고 있기     → FACE_MISSING (일시정지 UI 테스트)
    ///   L 키 누르고 있기     → LOW_CONFIDENCE
    /// </summary>
    public class MockGazeProvider : IGazeProvider
    {
        private GazeProviderState _state = GazeProviderState.Idle;
        private GazeSample _latest;
        private readonly Queue<CalibrationEvent> _calibrationEvents = new Queue<CalibrationEvent>();

        private float _calibrationTimer;
        private bool _calibrating;
        private bool _collecting;
        private readonly float _fakeCalibrationDuration;

        private Vector2 _lastPoint;
        private float _lastMoveTime;

        public MockGazeProvider(float fakeCalibrationDuration = 1.2f)
        {
            _fakeCalibrationDuration = fakeCalibrationDuration;
            _latest = GazeSample.Invalid;
        }

        public GazeProviderState State => _state;
        public string LastError => string.Empty;
        public bool SupportsCalibration => true;
        public GazeSample Latest => _latest;

        public void Initialize(string licenseKey)
        {
            _state = GazeProviderState.Ready;
        }

        public void StartTracking()
        {
            if (_state == GazeProviderState.Ready || _state == GazeProviderState.Tracking)
                _state = GazeProviderState.Tracking;
        }

        public void StopTracking()
        {
            if (_state == GazeProviderState.Tracking) _state = GazeProviderState.Ready;
        }

        public void Shutdown()
        {
            _state = GazeProviderState.Idle;
            _calibrationEvents.Clear();
        }

        public void Pump()
        {
            PumpCalibration();

            if (_state != GazeProviderState.Tracking)
            {
                _latest = GazeSample.Invalid;
                return;
            }

            Vector2 mouse = GetPointerPosition();
            bool inside = mouse.x >= 0f && mouse.x <= Screen.width && mouse.y >= 0f && mouse.y <= Screen.height;

            GazeTracking tracking = GazeTracking.Success;
            if (IsKeyHeld(KeyCode.F)) tracking = GazeTracking.FaceMissing;
            else if (IsKeyHeld(KeyCode.L)) tracking = GazeTracking.LowConfidence;

            // 마우스가 멈춰 있으면 FIXATION, 움직이는 중이면 SACCADE로 흉내낸다.
            GazeMovement movement = GazeMovement.Fixation;
            if ((mouse - _lastPoint).sqrMagnitude > 4f)
            {
                _lastMoveTime = Time.unscaledTime;
                _lastPoint = mouse;
            }
            if (Time.unscaledTime - _lastMoveTime < 0.08f) movement = GazeMovement.Saccade;

            _latest = new GazeSample
            {
                TimestampMs = (long)(Time.unscaledTimeAsDouble * 1000d),
                Point = mouse,
                Tracking = tracking,
                Screen = inside ? GazeScreen.Inside : GazeScreen.Outside,
                Movement = movement,
            };
        }

        public void StartCalibration()
        {
            _calibrating = true;
            _collecting = false;
            _calibrationTimer = 0f;
            _calibrationEvents.Enqueue(new CalibrationEvent
            {
                Type = CalibrationEventType.NextPoint,
                Point = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f),
            });
        }

        public void CollectCalibrationSamples()
        {
            if (_calibrating) _collecting = true;
        }

        public void DrainCalibrationEvents(List<CalibrationEvent> into)
        {
            while (_calibrationEvents.Count > 0) into.Add(_calibrationEvents.Dequeue());
        }

        private void PumpCalibration()
        {
            if (!_calibrating || !_collecting) return;

            _calibrationTimer += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(_calibrationTimer / _fakeCalibrationDuration);

            _calibrationEvents.Enqueue(new CalibrationEvent
            {
                Type = CalibrationEventType.Progress,
                Progress = progress,
            });

            if (progress >= 1f)
            {
                _calibrating = false;
                _collecting = false;
                _calibrationEvents.Enqueue(new CalibrationEvent { Type = CalibrationEventType.Finished });
            }
        }

        // 구 Input Manager / 신 Input System 어느 쪽이 켜져 있어도 동작하도록 방어한다.
        private static Vector2 GetPointerPosition()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.mousePosition;
#else
            return new Vector2(Screen.width * 0.5f, Screen.height * 0.25f);
#endif
        }

        private static bool IsKeyHeld(KeyCode key)
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKey(key);
#else
            return false;
#endif
        }
    }
}
