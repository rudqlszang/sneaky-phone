using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Molae.Core;

namespace Molae.UI
{
    /// <summary>
    /// 1포인트 캘리브레이션 화면.
    ///
    /// UX 설계: 앱 실행부터 실제 플레이까지의 예산은 약 10초인데 캘리브레이션이 이걸
    /// 통째로 잡아먹는다. 그래서 "설정 절차"가 아니라 "1교시 시작 전 눈 풀기"라는
    /// 게임 내 연출로 포장해 온보딩과 캘리브레이션을 하나로 합친다.
    ///
    /// 개인정보 고지도 여기서 한다. 카메라 권한 화면은 최대 이탈 지점이므로
    /// "모든 얼굴 인식은 기기 안에서만 처리되며 어떤 영상도 저장·전송되지 않습니다"를
    /// 권한 요청 직전에 한국어로 명시하는 것이 법적 요건이자 전환율 장치다.
    /// </summary>
    public class CalibrationView : MonoBehaviour
    {
        [Header("연결")]
        [SerializeField] private GameDirector director;

        [Header("루트")]
        [SerializeField] private GameObject root;
        [SerializeField] private Canvas canvas;

        [Header("응시점")]
        [Tooltip("플레이어가 응시할 점. 스크린 좌표로 옮겨진다.")]
        [SerializeField] private RectTransform gazeDot;
        [Tooltip("점 주위를 채우는 진행 링. Image Type = Filled, Radial 360 권장.")]
        [SerializeField] private Image progressRing;
        [Tooltip("점이 살짝 맥동해 시선을 붙잡는다.")]
        [SerializeField] private float pulseAmplitude = 0.12f;
        [SerializeField] private float pulseHz = 1.6f;

        [Header("문구")]
        [SerializeField] private TMP_Text titleLabel;
        [SerializeField] private TMP_Text bodyLabel;
        [SerializeField] private TMP_Text privacyLabel;

        [SerializeField] private string titleText = "1교시 시작 전, 눈 풀기";
        [SerializeField, TextArea] private string bodyText = "칠판의 점을 2초만 바라보세요";
        [SerializeField, TextArea]
        private string privacyText =
            "얼굴 인식은 모두 이 기기 안에서만 처리됩니다.\n어떤 영상도 저장되거나 전송되지 않습니다.";

        private Vector3 _dotBaseScale = Vector3.one;
        private bool _dotVisible;

        private void Reset() => director = FindFirstObjectByType<GameDirector>();

        private void Awake()
        {
            if (director == null) director = FindFirstObjectByType<GameDirector>();
            if (gazeDot != null) _dotBaseScale = gazeDot.localScale;
            if (root != null) root.SetActive(false);

            if (titleLabel != null) titleLabel.text = titleText;
            if (bodyLabel != null) bodyLabel.text = bodyText;
            if (privacyLabel != null) privacyLabel.text = privacyText;
        }

        private void OnEnable()
        {
            if (director == null) return;
            director.PhaseChanged += HandlePhaseChanged;
            director.CalibrationPointShown += HandlePointShown;
            director.CalibrationProgress += HandleProgress;
        }

        private void OnDisable()
        {
            if (director == null) return;
            director.PhaseChanged -= HandlePhaseChanged;
            director.CalibrationPointShown -= HandlePointShown;
            director.CalibrationProgress -= HandleProgress;
        }

        private void Update()
        {
            if (!_dotVisible || gazeDot == null) return;

            float pulse = 1f + pulseAmplitude * Mathf.Sin(Time.unscaledTime * pulseHz * Mathf.PI * 2f);
            gazeDot.localScale = _dotBaseScale * pulse;
        }

        private void HandlePhaseChanged(GamePhase phase)
        {
            bool show = phase == GamePhase.Calibrating || phase == GamePhase.Preparing;
            if (root != null) root.SetActive(show);

            if (!show)
            {
                _dotVisible = false;
                return;
            }

            if (progressRing != null) progressRing.fillAmount = 0f;

            // Preparing 단계에서는 점 없이 안내만 보여준다.
            if (phase == GamePhase.Preparing)
            {
                _dotVisible = false;
                if (gazeDot != null) gazeDot.gameObject.SetActive(false);
                if (bodyLabel != null) bodyLabel.text = "카메라를 준비하고 있어요...";
            }
            else
            {
                if (bodyLabel != null) bodyLabel.text = bodyText;
            }
        }

        private void HandlePointShown(Vector2 screenPoint)
        {
            if (gazeDot == null) return;

            gazeDot.gameObject.SetActive(true);
            _dotVisible = true;

            // 스크린 좌표 → 캔버스 로컬 좌표
            RectTransform parent = gazeDot.parent as RectTransform;
            if (parent == null) return;

            Camera cam = (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                ? null
                : canvas != null ? canvas.worldCamera : null;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenPoint, cam, out Vector2 local))
            {
                gazeDot.anchoredPosition = local;
            }
        }

        private void HandleProgress(float progress)
        {
            if (progressRing != null) progressRing.fillAmount = Mathf.Clamp01(progress);
        }
    }
}
