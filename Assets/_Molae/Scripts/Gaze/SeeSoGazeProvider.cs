// ─────────────────────────────────────────────────────────────────────────────
//  SeeSo(Eyedid) Unity Mobile SDK 연동 프로바이더
//
//  ▶ SDK가 없어도 이 파일은 컴파일된다. 아래 스텁이 대신 쓰인다.
//  ▶ SDK를 넣은 뒤 실제로 켜는 방법:
//      1) manage.seeso.io 에서 Unity Mobile SDK(.unitypackage) 다운로드 후 임포트
//      2) Edit > Project Settings > Player > Android > Other Settings >
//         Scripting Define Symbols 에  MOLAE_SEESO  추가
//      3) 임포트된 SDK의 네임스페이스를 확인해 아래 using 을 맞춘다
//         (2.6.3 기준 전역 또는 SeeSo 네임스페이스. 타입을 못 찾으면 이 줄을 조정)
//
//  ▶ 리플렉션을 쓰지 않는 이유: SeeSo 콜백 등록은 SDK 고유 델리게이트 타입
//    (GazeDelegate.onGaze 등)을 인자로 받는다. 컴파일 타임에 그 타입을 모르면
//    Delegate.CreateDelegate 로 만들 수 없고, IL2CPP(AOT)에서는 동적 델리게이트
//    생성 자체가 신뢰할 수 없다. 그래서 조건부 컴파일이 유일하게 확실한 방법이다.
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;

// SeeSo 공식 문서(docs.eyedid.ai)의 Unity 예제는 using 문을 보여주지 않는다.
// 2.6.3 패키지가 타입을 전역 네임스페이스에 두는지 SeeSo 네임스페이스에 두는지 문서만으로는
// 확정할 수 없으므로, 잘못된 using 한 줄로 컴파일이 깨지지 않도록 별도 심볼로 분리해 둔다.
//
// 임포트 후 "GazeTracker 형식을 찾을 수 없습니다" 오류가 나면
// Scripting Define Symbols 에 MOLAE_SEESO_NAMESPACE 를 추가하면 된다.
#if MOLAE_SEESO && MOLAE_SEESO_NAMESPACE
using SeeSo;
#endif

namespace Molae.Gaze
{
    public class SeeSoGazeProvider : IGazeProvider
    {
        // 워커 스레드에서 기록되고 메인 스레드에서 읽힌다. 반드시 volatile.
        private volatile int _rawTracking = (int)GazeTracking.FaceMissing;
        private volatile int _rawScreen = (int)GazeScreen.Unknown;
        private volatile int _rawMovement = (int)GazeMovement.Unknown;
        private volatile float _rawX;
        private volatile float _rawY;          // SeeSo 원본 좌표(좌상단 원점). Pump에서 뒤집는다.
        private long _rawTimestamp;

        private volatile int _state = (int)GazeProviderState.Idle;
        private string _lastError = string.Empty;

        private readonly ConcurrentQueue<CalibrationEvent> _calibrationEvents =
            new ConcurrentQueue<CalibrationEvent>();

        // 캘리브레이션 점 좌표도 콜백 스레드에서 오므로 raw로 받아 Pump에서 변환한다.
        private readonly ConcurrentQueue<Vector2> _rawCalibrationPoints = new ConcurrentQueue<Vector2>();

        private GazeSample _latest = GazeSample.Invalid;

        private const string CalibrationPrefsKey = "Molae.CalibrationData";

        public GazeProviderState State => (GazeProviderState)_state;
        public string LastError => _lastError;
        public bool SupportsCalibration => true;
        public GazeSample Latest => _latest;

        /// <summary>이 빌드에 SeeSo SDK가 실제로 링크되어 있는지.</summary>
        public static bool IsSdkPresent
        {
            get
            {
#if MOLAE_SEESO
                return true;
#else
                return false;
#endif
            }
        }

        // ───────────────────────────────────────────── 공통(메인 스레드)

        public void Pump()
        {
            // 캘리브레이션 점: raw(좌상단 원점 px) → Unity 스크린 좌표(좌하단 원점)
            while (_rawCalibrationPoints.TryDequeue(out Vector2 raw))
            {
                _calibrationEvents.Enqueue(new CalibrationEvent
                {
                    Type = CalibrationEventType.NextPoint,
                    Point = new Vector2(raw.x, Screen.height - raw.y),
                });
            }

            var tracking = (GazeTracking)_rawTracking;
            _latest = new GazeSample
            {
                TimestampMs = System.Threading.Interlocked.Read(ref _rawTimestamp),
                // Screen.height 는 메인 스레드에서만 안전하게 읽을 수 있으므로 여기서 y를 뒤집는다.
                Point = new Vector2(_rawX, Screen.height - _rawY),
                Tracking = tracking,
                Screen = (GazeScreen)_rawScreen,
                Movement = (GazeMovement)_rawMovement,
            };
        }

        public void DrainCalibrationEvents(List<CalibrationEvent> into)
        {
            while (_calibrationEvents.TryDequeue(out CalibrationEvent evt)) into.Add(evt);
        }

#if MOLAE_SEESO
        // ═════════════════════════════════════════════ 실제 SDK 경로

        public void Initialize(string licenseKey)
        {
            if (string.IsNullOrEmpty(licenseKey))
            {
                Fail("라이선스 키가 비어 있습니다. manage.seeso.io 에서 발급받아 GazeService 에 입력하세요.");
                return;
            }

            _state = (int)GazeProviderState.Initializing;
            GazeTracker.initGazeTracker(licenseKey, OnInitialized);
        }

        private void OnInitialized(InitializationErrorType error)
        {
            if (error != InitializationErrorType.ERROR_NONE)
            {
                Fail($"SeeSo 초기화 실패: {error} ({(int)error})");
                return;
            }

            GazeTracker.setStatusCallback(OnStarted, OnStopped);
            GazeTracker.setGazeCallback(OnGaze);
            GazeTracker.setCalibrationCallback(OnCalibrationNextPoint, OnCalibrationProgress, OnCalibrationFinished);

            _state = (int)GazeProviderState.Ready;
        }

        public void StartTracking()
        {
            if (_state == (int)GazeProviderState.Ready || _state == (int)GazeProviderState.Tracking)
                GazeTracker.startTracking();
        }

        public void StopTracking()
        {
            if (GazeTracker.isTracking()) GazeTracker.stopTracking();
        }

        public void Shutdown()
        {
            GazeTracker.removeGazeCallback();
            GazeTracker.removeCalibrationCallback();
            GazeTracker.removeStatusCallback();
            GazeTracker.deinitGazeTracker();
            _state = (int)GazeProviderState.Idle;
        }

        /// <summary>응시점 개수. GazeService 가 설정에서 읽어 넣어준다.</summary>
        public int CalibrationPointCount { get; set; } = 5;

        public void StartCalibration()
        {
            // 1점은 오프셋만 보정해 화면 가장자리에서 크게 어긋난다.
            // 5점(중앙 + 네 모서리)이라야 스케일과 기울기까지 잡혀 화면 전역이 맞는다.
            CalibrationModeType mode;
            switch (CalibrationPointCount)
            {
                case 1: mode = CalibrationModeType.ONE_POINT; break;
                case 6: mode = CalibrationModeType.SIX_POINT; break;
                default: mode = CalibrationModeType.FIVE_POINT; break;
            }
            // AccuracyCriteria.HIGH 는 각 점에서 더 많은 샘플을 요구해 느리지만 정확하다.
            GazeTracker.startCalibration(mode, AccuracyCriteria.DEFAULT);
        }

        public void CollectCalibrationSamples()
        {
            GazeTracker.startCollectSamples();
        }

        /// <summary>이전 세션의 캘리브레이션 데이터를 복원한다. 성공하면 true.</summary>
        public bool TryRestoreCalibration()
        {
            string saved = PlayerPrefs.GetString(CalibrationPrefsKey, string.Empty);
            if (string.IsNullOrEmpty(saved)) return false;

            string[] parts = saved.Split(',');
            var data = new double[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!double.TryParse(parts[i], out data[i])) return false;
            }

            return GazeTracker.setCalibrationData(data);
        }

        // ── SDK 콜백 (워커 스레드! Unity API 호출 금지) ──────────────────

        private void OnStarted() => _state = (int)GazeProviderState.Tracking;

        private void OnStopped(StatusErrorType error)
        {
            _state = (int)GazeProviderState.Ready;
            if (error != StatusErrorType.ERROR_NONE) _lastError = $"추적 중지: {error}";
        }

        private void OnGaze(GazeInfo info)
        {
            _rawX = info.x;
            _rawY = info.y;
            System.Threading.Interlocked.Exchange(ref _rawTimestamp, info.timestamp);
            _rawTracking = MapTracking(info.trackingState);
            _rawScreen = MapScreen(info.screenState);
            _rawMovement = MapMovement(info.eyeMovementState);
        }

        private void OnCalibrationNextPoint(float x, float y)
        {
            // Screen.height 를 못 쓰므로 raw 그대로 큐잉하고 Pump에서 변환한다.
            _rawCalibrationPoints.Enqueue(new Vector2(x, y));
        }

        private void OnCalibrationProgress(float progress)
        {
            _calibrationEvents.Enqueue(new CalibrationEvent
            {
                Type = CalibrationEventType.Progress,
                Progress = progress,
            });
        }

        private void OnCalibrationFinished(double[] calibrationData)
        {
            if (calibrationData != null && calibrationData.Length > 0)
            {
                _pendingCalibrationData = calibrationData;
            }
            _calibrationEvents.Enqueue(new CalibrationEvent { Type = CalibrationEventType.Finished });
        }

        private double[] _pendingCalibrationData;

        /// <summary>메인 스레드에서 호출. 캘리브레이션 결과를 PlayerPrefs에 저장한다.</summary>
        public void PersistCalibration()
        {
            if (_pendingCalibrationData == null) return;
            PlayerPrefs.SetString(CalibrationPrefsKey, string.Join(",", _pendingCalibrationData));
            PlayerPrefs.Save();
            _pendingCalibrationData = null;
        }

        // ── enum 매핑 ────────────────────────────────────────────────────

        private static int MapTracking(TrackingState s)
        {
            switch (s)
            {
                case TrackingState.SUCCESS: return (int)GazeTracking.Success;
                case TrackingState.LOW_CONFIDENCE: return (int)GazeTracking.LowConfidence;
                case TrackingState.UNSUPPORTED: return (int)GazeTracking.Unsupported;
                default: return (int)GazeTracking.FaceMissing;
            }
        }

        private static int MapScreen(ScreenState s)
        {
            switch (s)
            {
                case ScreenState.INSIDE_OF_SCREEN: return (int)GazeScreen.Inside;
                case ScreenState.OUTSIDE_OF_SCREEN: return (int)GazeScreen.Outside;
                default: return (int)GazeScreen.Unknown;
            }
        }

        private static int MapMovement(EyeMovementState s)
        {
            switch (s)
            {
                case EyeMovementState.FIXATION: return (int)GazeMovement.Fixation;
                case EyeMovementState.SACCADE: return (int)GazeMovement.Saccade;
                default: return (int)GazeMovement.Unknown;
            }
        }

#else
        // ═════════════════════════════════════════════ SDK 미설치 스텁

        private const string NotInstalled =
            "SeeSo SDK가 설치되지 않았습니다. .unitypackage 임포트 후 Scripting Define Symbols에 MOLAE_SEESO 를 추가하세요.";

        public void Initialize(string licenseKey) => Fail(NotInstalled);
        public void StartTracking() { }
        public void StopTracking() { }
        public void Shutdown() => _state = (int)GazeProviderState.Idle;
        public void StartCalibration() => _calibrationEvents.Enqueue(new CalibrationEvent { Type = CalibrationEventType.Failed });
        public void CollectCalibrationSamples() { }
        public bool TryRestoreCalibration() => false;
        public void PersistCalibration() { }

#endif

        private void Fail(string message)
        {
            _lastError = message;
            _state = (int)GazeProviderState.Failed;
            Debug.LogWarning($"[Molae/Gaze] {message}");
        }
    }
}
