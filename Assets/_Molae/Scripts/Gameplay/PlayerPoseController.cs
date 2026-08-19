using UnityEngine;

namespace Molae.Gameplay
{
    /// <summary>
    /// 플레이어 캐릭터의 2상태 스프라이트 전환 컨트롤러.
    ///
    ///   Phone   = 고개 숙이고 책상 아래로 폰을 든 자세
    ///   Upright = 고개 들고 칠판 보는 정자세
    ///
    /// 리깅/애니메이터 없이 두 SpriteRenderer의 알파를 교차 페이드하고,
    /// 거기에 아주 작은 위치/회전 오프셋을 더해 고개 움직임을 흉내낸다.
    /// 실제 시선 데이터에 따라 실시간으로 두 상태만 오간다.
    /// </summary>
    public class PlayerPoseController : MonoBehaviour
    {
        public enum Pose { Upright, Phone }

        [Header("스프라이트")]
        [Tooltip("정자세 — 고개 들고 칠판 보는 모습")]
        [SerializeField] private SpriteRenderer uprightRenderer;
        [Tooltip("폰 보는 중 — 고개 숙인 모습")]
        [SerializeField] private SpriteRenderer phoneRenderer;

        [Header("전환")]
        [Tooltip("두 자세 사이 트윈 시간(초). 요구사항 0.1~0.2초.")]
        [SerializeField, Range(0.05f, 0.4f)] private float tweenDuration = 0.15f;

        [Tooltip("폰 자세일 때 캐릭터가 내려가는 정도(월드 유닛).")]
        [SerializeField] private float headDropOffset = 0.08f;

        [Tooltip("폰 자세일 때 살짝 기울어지는 각도.")]
        [SerializeField] private float headTiltDegrees = 4f;

        [Header("폰 화면 발광")]
        [Tooltip("폰을 볼 때 켜지는 화면 빛. 없으면 비워둔다.")]
        [SerializeField] private SpriteRenderer phoneGlow;
        [SerializeField] private float glowMaxAlpha = 0.85f;

        private Pose _target = Pose.Upright;
        private float _blend;          // 0 = Upright, 1 = Phone
        private Vector3 _basePosition;
        private Quaternion _baseRotation;

        /// <summary>현재 목표 자세.</summary>
        public Pose Target => _target;

        /// <summary>0~1 보간값. 연출 스크립트가 참고할 수 있다.</summary>
        public float Blend => _blend;

        private void Awake()
        {
            _basePosition = transform.localPosition;
            _baseRotation = transform.localRotation;
            ApplyBlend(0f);
        }

        /// <summary>시선 상태에 따라 자세를 지정한다. 매 프레임 호출해도 안전하다.</summary>
        public void SetPose(Pose pose) => _target = pose;

        /// <summary>편의 오버로드 — 폰을 보고 있으면 true.</summary>
        public void SetLookingAtPhone(bool lookingAtPhone) =>
            _target = lookingAtPhone ? Pose.Phone : Pose.Upright;

        /// <summary>즉시 해당 자세로 스냅한다(세션 시작/리셋용).</summary>
        public void SnapTo(Pose pose)
        {
            _target = pose;
            ApplyBlend(pose == Pose.Phone ? 1f : 0f);
        }

        private void Update()
        {
            float goal = _target == Pose.Phone ? 1f : 0f;
            if (Mathf.Approximately(_blend, goal)) return;

            float step = tweenDuration <= 0f ? 1f : Time.deltaTime / tweenDuration;
            _blend = Mathf.MoveTowards(_blend, goal, step);
            ApplyBlend(_blend);
        }

        private void ApplyBlend(float t)
        {
            _blend = t;

            // SmoothStep으로 시작/끝을 부드럽게 — 선형은 기계적으로 보인다.
            float eased = t * t * (3f - 2f * t);

            if (uprightRenderer != null) SetAlpha(uprightRenderer, 1f - eased);
            if (phoneRenderer != null) SetAlpha(phoneRenderer, eased);
            if (phoneGlow != null) SetAlpha(phoneGlow, eased * glowMaxAlpha);

            transform.localPosition = _basePosition + Vector3.down * (headDropOffset * eased);
            transform.localRotation = _baseRotation * Quaternion.Euler(0f, 0f, -headTiltDegrees * eased);
        }

        private static void SetAlpha(SpriteRenderer renderer, float alpha)
        {
            Color c = renderer.color;
            c.a = alpha;
            renderer.color = c;
        }
    }
}
