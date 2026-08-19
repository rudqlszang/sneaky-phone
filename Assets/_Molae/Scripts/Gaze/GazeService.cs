using System.Collections.Generic;
using UnityEngine;
using Molae.Core;

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace Molae.Gaze
{
    /// <summary>
    /// 시선 시스템의 유일한 진입점. 프로바이더 선택, 카메라 권한, 좌표 변환,
    /// 다수결 스무딩, "폰을 보고 있는가" 판정까지 전부 여기서 담당한다.
    ///
    /// 다른 시스템은 SeeSo를 몰라도 되고 IsLookingAtPhone / IsFaceMissing만 보면 된다.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class GazeService : MonoBehaviour
    {
        public enum ProviderMode
        {
            [Tooltip("실기기에서는 SeeSo, 에디터에서는 마우스 Mock")]
            Auto,
            ForceSeeSo,
            ForceMock,
        }

        [Header("설정")]
        [SerializeField] private MolaeConfig config;

        [Header("프로바이더")]
        [SerializeField] private ProviderMode providerMode = ProviderMode.Auto;
        [Tooltip("manage.seeso.io 에서 발급받은 라이선스 키. 절대 공개 저장소에 커밋하지 말 것.")]
        [Tooltip("비워두세요. 키는 Resources/seeso_license.txt 에서 읽습니다.")]
        [SerializeField] private string seeSoLicenseKey = "";

        /// <summary>
        /// 실제로 쓸 라이선스 키.
        ///
        /// Resources/seeso_license.txt 를 우선한다. 이 파일은 .gitignore 되어 있어
        /// 저장소에 올라가지 않는다. 인스펙터 필드는 비상용 폴백이다
        /// (씨에 직접 넣으면 Game.unity 에 평문으로 박혀 커밋된다).
        /// </summary>
        private string LicenseKey
        {
            get
            {
                string fromFile = Molae.Core.MolaeSecrets.SeeSoLicenseKey;
                return !string.IsNullOrEmpty(fromFile) ? fromFile : seeSoLicenseKey;
            }
        }

        [Header("응시 판정 영역")]
        [Tooltip("플레이어의 '몰래폰' 화면 RectTransform. 이 영역을 보면 응시로 판정한다.")]
        [SerializeField] private RectTransform phoneScreenRect;
        [Tooltip("판정에 쓰는 카메라. Screen Space - Overlay 캔버스면 비워둔다.")]
        [SerializeField] private Camera uiCamera;
        [Tooltip("켜면 판정 영역을 씬 뷰에 그린다.")]
        [SerializeField] private bool drawDebugGizmo = true;

        private IGazeProvider _provider;
        private readonly List<CalibrationEvent> _calibrationBuffer = new List<CalibrationEvent>(8);
        private readonly Queue<bool> _voteWindow = new Queue<bool>();
        private int _voteTrueCount;

        private float _faceMissingTimer;
        private bool _permissionRequested;

        /// <summary>
        /// "추적이 켜져 있어야 한다"는 의도. 실제 시작 여부와 별개로 유지된다.
        ///
        /// 이게 필요한 이유: Android 카메라 권한 팝업은 사용자가 '허용'을 누른 뒤에야
        /// Initialize()가 호출된다. 그 사이 GameDirector가 StartTracking()을 부르면
        /// 프로바이더가 아직 Idle이라 그냥 무시되고, 이후 초기화가 끝나도 다시 부르는 곳이 없어서
        /// 시선 데이터가 영원히 안 들어온다. 의도를 기억해 두고 Ready가 되는 순간 자동으로 켠다.
        /// </summary>
        private bool _wantTracking;

        /// <summary>초기화 실패 후 재시도 간격(초). 권한을 늦게 허용한 경우를 자가 복구한다.</summary>
        private const float RetryInterval = 2f;
        private float _retryTimer;

        // ───────────────────────────────────────────── 외부 공개 상태

        /// <summary>다수결 스무딩까지 거친 최종 응시 판정.</summary>
        public bool IsLookingAtPhone { get; private set; }

        /// <summary>얼굴이 설정된 시간 이상 검출되지 않은 상태. 게임오버가 아니라 일시정지 대상.</summary>
        public bool IsFaceMissing { get; private set; }

        /// <summary>시선이 화면 밖으로 나갔는지.</summary>
        public bool IsOutOfScreen { get; private set; }

        public GazeSample LatestSample { get; private set; }
        public GazeProviderState State => _provider?.State ?? GazeProviderState.Idle;
        public string LastError => _provider?.LastError ?? string.Empty;
        public bool UsingMock => _provider is MockGazeProvider;

        /// <summary>캘리브레이션 이벤트. CalibrationFlow가 구독한다.</summary>
        public event System.Action<CalibrationEvent> CalibrationEventReceived;

        // ───────────────────────────────────────────── 수명주기

        private void Awake()
        {
            if (config == null)
            {
                Debug.LogError("[Molae/Gaze] MolaeConfig가 연결되지 않았습니다.", this);
                enabled = false;
                return;
            }

            _provider = CreateProvider();
        }

        private void Start()
        {
            RequestPermissionThenInitialize();
        }

        private void OnDestroy()
        {
            _provider?.Shutdown();
        }

        private void Update()
        {
            if (_provider == null) return;

            EnsureTracking();
            _provider.Pump();

            _calibrationBuffer.Clear();
            _provider.DrainCalibrationEvents(_calibrationBuffer);
            for (int i = 0; i < _calibrationBuffer.Count; i++)
            {
                CalibrationEventReceived?.Invoke(_calibrationBuffer[i]);
            }

            GazeSample sample = _provider.Latest;
            LatestSample = sample;

            UpdateFaceMissing(sample);
            IsOutOfScreen = sample.Screen == GazeScreen.Outside;

            bool rawHit = EvaluateRawHit(sample);
            IsLookingAtPhone = PushVote(rawHit);
        }

        // ───────────────────────────────────────────── 판정

        /// <summary>스무딩 이전의 원시 응시 판정.</summary>
        private bool EvaluateRawHit(GazeSample sample)
        {
            if (!sample.HasUsablePoint) return false;
            if (sample.Tracking == GazeTracking.LowConfidence && !config.AcceptLowConfidence) return false;
            if (sample.Screen != GazeScreen.Inside) return false;
            if (config.RequireFixation && sample.Movement != GazeMovement.Fixation) return false;
            if (phoneScreenRect == null) return false;

            return ContainsWithPadding(phoneScreenRect, sample.Point, config.GazePadding);
        }

        /// <summary>
        /// RectTransform 영역에 패딩을 더해 점 포함 여부를 본다.
        /// 시선 정확도 오차를 감안해 시각 크기보다 넉넉한 히트박스를 쓴다.
        /// </summary>
        private bool ContainsWithPadding(RectTransform rect, Vector2 screenPoint, float paddingReferencePx)
        {
            Camera cam = uiCamera;
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);

            // 월드 코너 → 스크린 좌표
            for (int i = 0; i < 4; i++)
            {
                corners[i] = cam == null
                    ? (Vector3)RectTransformUtility.WorldToScreenPoint(null, corners[i])
                    : (Vector3)RectTransformUtility.WorldToScreenPoint(cam, corners[i]);
            }

            float minX = Mathf.Min(corners[0].x, corners[1].x, corners[2].x, corners[3].x);
            float maxX = Mathf.Max(corners[0].x, corners[1].x, corners[2].x, corners[3].x);
            float minY = Mathf.Min(corners[0].y, corners[1].y, corners[2].y, corners[3].y);
            float maxY = Mathf.Max(corners[0].y, corners[1].y, corners[2].y, corners[3].y);

            // 패딩은 1080 기준값이므로 실제 화면 폭에 맞춰 스케일한다.
            float pad = paddingReferencePx * (Screen.width / 1080f);

            return screenPoint.x >= minX - pad && screenPoint.x <= maxX + pad
                && screenPoint.y >= minY - pad && screenPoint.y <= maxY + pad;
        }

        /// <summary>최근 N개 샘플의 다수결. 1~2프레임짜리 오탐을 걸러낸다.</summary>
        private bool PushVote(bool value)
        {
            int window = Mathf.Max(1, config.GazeVoteWindow);

            _voteWindow.Enqueue(value);
            if (value) _voteTrueCount++;

            while (_voteWindow.Count > window)
            {
                if (_voteWindow.Dequeue()) _voteTrueCount--;
            }

            return _voteTrueCount * 2 > _voteWindow.Count;
        }

        private void UpdateFaceMissing(GazeSample sample)
        {
            if (sample.IsFaceMissing)
            {
                _faceMissingTimer += Time.unscaledDeltaTime;
                if (_faceMissingTimer >= config.FaceMissingPauseDelay) IsFaceMissing = true;
            }
            else
            {
                _faceMissingTimer = 0f;
                IsFaceMissing = false;
            }
        }

        // ───────────────────────────────────────────── 프로바이더 / 권한

        /// <summary>
        /// 프로바이더 선택.
        ///
        /// 규칙: 실기기 빌드에서는 Mock 을 절대 쓰지 않는다.
        /// 폰에는 마우스가 없으므로 Mock 으로 떨어지면 "시선이 한 점에 고정된 채"
        /// 게임이 정상인 척 돌아간다. 이건 눈에 안 보이는 실패라 그냥 죽는 것보다 나쁘다.
        /// SDK 나 권한이 없으면 Failed 상태로 두고 UI 가 안내하게 한다.
        /// </summary>
        private IGazeProvider CreateProvider()
        {
            switch (providerMode)
            {
                case ProviderMode.ForceMock:
#if UNITY_EDITOR
                    return new MockGazeProvider();
#else
                    Debug.LogError("[Molae/Gaze] ForceMock 은 에디터 전용입니다. 실기기에서는 SeeSo 를 사용합니다.");
                    return new SeeSoGazeProvider();
#endif

                case ProviderMode.ForceSeeSo:
                    return new SeeSoGazeProvider();

                default: // Auto
#if UNITY_EDITOR
                    // 에디터에는 전면 카메라 경로가 없다. 로직 검증용으로만 Mock 을 쓴다.
                    return new MockGazeProvider();
#else
                    return new SeeSoGazeProvider();
#endif
            }
        }

        private void RequestPermissionThenInitialize()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_provider is SeeSoGazeProvider)
            {
                if (Permission.HasUserAuthorizedPermission(Permission.Camera))
                {
                    _provider.Initialize(LicenseKey);
                    return;
                }

                if (_permissionRequested) return;
                _permissionRequested = true;

                var callbacks = new PermissionCallbacks();
                callbacks.PermissionGranted += _ => _provider.Initialize(LicenseKey);
                callbacks.PermissionDenied += _ =>
                    Debug.LogWarning("[Molae/Gaze] 카메라 권한이 거부되었습니다. 시선 추적을 사용할 수 없습니다.");
                callbacks.PermissionDeniedAndDontAskAgain += _ =>
                    Debug.LogWarning("[Molae/Gaze] 카메라 권한이 영구 거부되었습니다. 설정 앱에서 허용해야 합니다.");

                Permission.RequestUserPermission(Permission.Camera, callbacks);
                return;
            }
#endif
            _provider.Initialize(LicenseKey);
        }

        // ───────────────────────────────────────────── 외부 제어

        /// <summary>
        /// 추적을 켠다. 프로바이더가 아직 준비되지 않았어도 의도만 기억해 두고
        /// Ready가 되는 순간 EnsureTracking()이 자동으로 시작시킨다.
        /// </summary>
        public void StartTracking()
        {
            _wantTracking = true;
            _provider?.StartTracking();
        }

        public void StopTracking()
        {
            _wantTracking = false;
            _provider?.StopTracking();
        }

        /// <summary>
        /// 매 프레임 호출. "켜져 있어야 하는데 안 켜진" 상태를 자가 복구한다.
        ///  - Ready인데 Tracking이 아니면 → 시작
        ///  - Failed면 일정 간격으로 초기화 재시도 (권한을 늦게 허용한 경우)
        /// </summary>
        private void EnsureTracking()
        {
            if (!_wantTracking) return;

            GazeProviderState s = _provider.State;

            if (s == GazeProviderState.Ready)
            {
                _provider.StartTracking();
                return;
            }

            if (s == GazeProviderState.Idle || s == GazeProviderState.Failed)
            {
                _retryTimer -= Time.unscaledDeltaTime;
                if (_retryTimer > 0f) return;
                _retryTimer = RetryInterval;

#if UNITY_ANDROID && !UNITY_EDITOR
                // 권한이 이제 막 허용됐을 수 있다. 있으면 초기화를 다시 시도한다.
                if (_provider is SeeSoGazeProvider && !Permission.HasUserAuthorizedPermission(Permission.Camera)) return;
#endif
                _provider.Initialize(LicenseKey);
            }
        }
        public void StartCalibration()
        {
            // 응시점 개수를 설정에서 프로바이더로 전달한다.
            // 1점은 오프셋만 보정해 화면 가장자리에서 어긋나므로 기본은 5점이다.
            if (_provider is SeeSoGazeProvider seeSo && config != null)
                seeSo.CalibrationPointCount = (int)config.CalibrationPointCount;

            _provider?.StartCalibration();
        }

        /// <summary>남은 캘리브레이션 점 개수(진행 표시용).</summary>
        public int CalibrationPointCount => config != null ? (int)config.CalibrationPointCount : 5;
        public void CollectCalibrationSamples() => _provider?.CollectCalibrationSamples();

        /// <summary>저장된 캘리브레이션을 복원해 캘리브레이션 단계를 건너뛸 수 있는지 시도한다.</summary>
        public bool TryRestoreCalibration()
        {
            if (!config.CacheCalibration) return false;
            return _provider is SeeSoGazeProvider seeSo && seeSo.TryRestoreCalibration();
        }

        /// <summary>캘리브레이션 완료 후 결과를 저장한다.</summary>
        public void PersistCalibration()
        {
            if (!config.CacheCalibration) return;
            (_provider as SeeSoGazeProvider)?.PersistCalibration();
        }

        /// <summary>판정 영역을 런타임에 교체한다(씬 로드 후 연결용).</summary>
        public void SetPhoneRect(RectTransform rect) => phoneScreenRect = rect;

        private void OnDrawGizmosSelected()
        {
            if (!drawDebugGizmo || phoneScreenRect == null) return;
            var corners = new Vector3[4];
            phoneScreenRect.GetWorldCorners(corners);
            Gizmos.color = new Color(0.94f, 0.66f, 0.41f, 0.9f); // amber
            for (int i = 0; i < 4; i++) Gizmos.DrawLine(corners[i], corners[(i + 1) % 4]);
        }
    }
}
