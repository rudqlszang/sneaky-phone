using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Molae.Core;
using Molae.Gameplay;

namespace Molae.UI
{
    /// <summary>
    /// 게임플레이 HUD.
    ///
    /// 하이퍼캐주얼 원칙에 따라 상시 표시 요소를 최소로 유지한다: 점수 / 남은 시간 / 인지도.
    ///
    /// 선생님 상태는 여기서 표시하지 않는다. 플레이어의 시선은 화면 하단 폰에 고정돼 있고
    /// 주변시는 응시점 2° 밖에서 작은 아이콘·텍스트를 거의 읽지 못하기 때문에,
    /// 상단 HUD로 위험을 알리는 설계는 원리적으로 실패한다.
    /// 경고는 화면 전체 연출(비네트 명도 변화) + 선생님 실루엣 + 분필 소리 정지가 담당한다.
    /// </summary>
    public class HudView : MonoBehaviour
    {
        [Header("연결")]
        [SerializeField] private GameDirector director;

        [Header("점수")]
        [SerializeField] private TMP_Text scoreLabel;
        [SerializeField] private TMP_Text comboLabel;
        [Tooltip("콤보가 0일 때 콤보 라벨을 숨긴다.")]
        [SerializeField] private bool hideComboAtZero = true;

        [Header("타이머")]
        [SerializeField] private TMP_Text timerLabel;
        [SerializeField] private Image timerFill;

        // 선생님 상태 표시용 HUD 아이콘은 두지 않는다.
        //
        // 처음에는 색각이상 대응(색 단독 전달 금지)으로 상단에 상태 아이콘을 뒀는데,
        // 캐릭터 실루엣을 그대로 쓰다 보니 화면에 선생님이 둘 있는 것처럼 보였다.
        // 상태 구분은 이미 색이 아닌 3채널로 확보돼 있어서 이 아이콘이 없어도 접근성이 깨지지 않는다:
        //   1) 선생님 실루엣 폭 변화 (판서 44 / 예고 38 / 정면 48 px)
        //   2) 화면 전체 비네트 명도 변화 (안전 앰버 ↔ 위험 남색, 대비 8.27:1)
        //   3) 분필 소리 정지 + 0.3~0.5초 무음
        // 주변시는 어차피 작은 아이콘을 못 읽으므로 HUD 아이콘은 실효도 낮았다.

        [Header("인지도 게이지")]
        [Tooltip("적발까지 얼마나 남았는지. 인지도 방식일 때만 의미가 있다.")]
        [SerializeField] private Image suspicionFill;
        [SerializeField] private CanvasGroup suspicionGroup;

        private void Reset() => director = FindFirstObjectByType<GameDirector>();

        private void Awake()
        {
            if (director == null) director = FindFirstObjectByType<GameDirector>();
        }

        private void LateUpdate()
        {
            if (director == null || director.Config == null) return;

            UpdateScore();
            UpdateTimer();
            UpdateSuspicion();
        }

        private void UpdateScore()
        {
            var score = director.Score;
            if (score == null) return;

            // 점수 텍스트 자체는 ScorePopAnimator가 갱신한다. 여기서는 없을 때만 채운다.
            if (scoreLabel != null && scoreLabel.GetComponent<Feedback.ScorePopAnimator>() == null)
            {
                scoreLabel.text = score.Score.ToString("N0");
            }

            if (comboLabel == null) return;

            int step = score.ComboStep;
            if (hideComboAtZero && step <= 0)
            {
                if (comboLabel.gameObject.activeSelf) comboLabel.gameObject.SetActive(false);
                return;
            }

            if (!comboLabel.gameObject.activeSelf) comboLabel.gameObject.SetActive(true);
            comboLabel.text = $"x{score.Multiplier:0.##}";
        }

        private void UpdateTimer()
        {
            float remaining = director.Remaining;

            if (timerLabel != null)
            {
                // 남은 시간은 올림해서 보여줘야 0을 보고도 아직 살아있는 어색함이 없다.
                timerLabel.text = Mathf.CeilToInt(remaining).ToString();
            }

            if (timerFill != null)
            {
                timerFill.fillAmount = 1f - director.NormalizedProgress;
            }
        }

        private void UpdateSuspicion()
        {
            SuspicionMeter meter = director.Suspicion;
            if (meter == null) return;

            float value = meter.Suspicion;

            if (suspicionFill != null) suspicionFill.fillAmount = value;

            if (suspicionGroup != null)
            {
                // 인지도가 0일 때는 완전히 숨겨 HUD를 깔끔하게 유지한다.
                float target = value > 0.01f ? 1f : 0f;
                suspicionGroup.alpha = Mathf.MoveTowards(suspicionGroup.alpha, target, Time.deltaTime * 6f);
            }
        }
    }
}
