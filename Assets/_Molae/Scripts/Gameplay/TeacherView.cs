using UnityEngine;

namespace Molae.Gameplay
{
    /// <summary>
    /// 선생님 스프라이트 표현. TeacherAI.TurnAmount(0~1)를 받아 판서/정면 두 스프라이트를
    /// 교차 페이드하고, 회전 중에 살짝 몸이 돌아가는 느낌을 스케일/기울기로 더한다.
    ///
    /// 플레이어 캐릭터와 같은 이유로 리깅을 쓰지 않는다 — 스프라이트 2장이면 충분하다.
    ///
    /// 위험 신호의 '형태' 채널을 담당한다. 색각이상 플레이어에게는 이 실루엣 변화와
    /// 분필 소리 정지가 유일하게 신뢰할 수 있는 경고다.
    /// </summary>
    public class TeacherView : MonoBehaviour
    {
        [Header("연결")]
        [SerializeField] private TeacherAI teacher;

        [Header("스프라이트")]
        [Tooltip("판서 중 — 뒷모습")]
        [SerializeField] private SpriteRenderer writingRenderer;
        [Tooltip("정면 응시 — 얼굴이 보인다")]
        [SerializeField] private SpriteRenderer watchingRenderer;

        [Header("연출")]
        [Tooltip("돌아설 때 가로로 살짝 눌렸다 펴지는 정도. 회전을 흉내낸다.")]
        [SerializeField, Range(0f, 0.5f)] private float turnSquash = 0.18f;
        [Tooltip("판서 중 어깨가 흔들리는 진폭(월드 유닛).")]
        [SerializeField] private float writingBobAmplitude = 0.045f;
        [SerializeField] private float writingBobHz = 2.6f;

        private Vector3 _baseScale;
        private Vector3 _basePosition;

        private void Awake()
        {
            _baseScale = transform.localScale;
            _basePosition = transform.localPosition;
            if (teacher == null) teacher = FindFirstObjectByType<TeacherAI>();
        }

        private void LateUpdate()
        {
            if (teacher == null) return;

            float turn = teacher.TurnAmount;

            // 스프라이트 교차 페이드. 중간 지점(0.5)에서 교체되도록 구간을 좁게 잡아
            // '어정쩡하게 둘 다 반투명'한 구간을 최소화한다.
            float watchAlpha = Mathf.Clamp01((turn - 0.45f) / 0.25f);
            SetAlpha(writingRenderer, 1f - watchAlpha);
            SetAlpha(watchingRenderer, watchAlpha);

            // 돌아서는 중에 가로로 눌린다 → 회전처럼 읽힌다
            float squash = 1f - turnSquash * Mathf.Sin(turn * Mathf.PI);
            Vector3 scale = _baseScale;
            scale.x = _baseScale.x * squash;
            transform.localScale = scale;

            // 판서 중에만 어깨가 들썩인다 (안전 상태를 계속 알려주는 모션 채널)
            float bob = 0f;
            if (teacher.State == TeacherState.Writing)
            {
                bob = Mathf.Sin(Time.time * writingBobHz * Mathf.PI * 2f) * writingBobAmplitude;
            }
            transform.localPosition = _basePosition + new Vector3(0f, bob, 0f);
        }

        private static void SetAlpha(SpriteRenderer renderer, float alpha)
        {
            if (renderer == null) return;
            Color c = renderer.color;
            c.a = alpha;
            renderer.color = c;
            // 완전히 투명하면 렌더러를 꺼서 드로우콜을 아낀다
            if (renderer.enabled != alpha > 0.001f) renderer.enabled = alpha > 0.001f;
        }
    }
}
