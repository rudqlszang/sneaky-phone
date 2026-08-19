using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Molae.Core;

namespace Molae.UI
{
    /// <summary>
    /// GameFlow 의 이벤트를 실제 UI 오브젝트에 연결한다.
    ///
    /// GameFlow 는 진행 로직만 알고 UI 를 모른다. 이 바인더가 사이에 끼어서
    /// "인터미션을 띄워라" 같은 요청을 화면 조작으로 번역한다.
    /// 덕분에 UI 구조를 바꿔도 진행 로직은 건드릴 필요가 없다.
    /// </summary>
    public class FlowUIBinder : MonoBehaviour
    {
        [Header("연결")]
        [SerializeField] private GameFlow flow;
        [SerializeField] private RoundManager rounds;
        [SerializeField] private EndingDirector ending;
        [SerializeField] private ExitConfirmDialog exitDialog;
        [SerializeField] private CaughtDialog caughtDialog;
        [SerializeField] private TitleScreen titleScreen;

        [Header("인터미션")]
        [SerializeField] private GameObject intermissionContent;
        [SerializeField] private TMP_Text intermissionTitle;
        [SerializeField] private TMP_Text intermissionScore;
        [SerializeField] private Image bannerBg;
        [SerializeField] private TMP_Text bannerText;

        [Header("카운트다운")]
        [SerializeField] private GameObject countdownContent;
        [SerializeField] private TMP_Text countdownLabel;

        [Header("HUD")]
        [Tooltip("라운드 표시. '1교시' 등.")]
        [SerializeField] private TMP_Text roundLabel;
        [Tooltip("남은 라이프 표시.")]
        [SerializeField] private TMP_Text livesLabel;

        [Header("색")]
        [SerializeField] private Color amber = new Color(0.941f, 0.655f, 0.408f);
        [SerializeField] private Color night = new Color(0.106f, 0.114f, 0.200f);
        [SerializeField] private Color ink = new Color(0.153f, 0.153f, 0.267f);
        [SerializeField] private Color chalk = new Color(0.914f, 0.929f, 0.925f);

        private Coroutine _intermissionAnim;

        private void Awake()
        {
            if (flow == null) flow = FindFirstObjectByType<GameFlow>();
            if (rounds == null) rounds = FindFirstObjectByType<RoundManager>();
            if (ending == null) ending = FindFirstObjectByType<EndingDirector>();
            if (exitDialog == null) exitDialog = FindFirstObjectByType<ExitConfirmDialog>();
        }

        private bool _waitingRetry;
        private bool _waitingTitle = true;

        private void OnEnable()
        {
            if (flow == null) return;
            flow.IntermissionRequested += OnIntermission;
            flow.CountdownTick += OnCountdown;
            flow.EndingRequested += OnEnding;
            flow.CaughtStinger += OnCaught;
            flow.SessionFailed += OnSessionFailed;

            // GameFlow 는 이 델리게이트가 false 를 줄 때까지 진행을 멈춘다.
            flow.WaitingForRetryDecision = () => _waitingRetry;
            flow.WaitingForTitle = () => _waitingTitle;

            if (caughtDialog != null)
            {
                caughtDialog.RetryPressed += OnRetryChosen;
                caughtDialog.QuitPressed += OnQuitChosen;
                caughtDialog.HomePressed += OnHomeChosen;
            }
            if (ending != null) ending.HomePressed += OnHomeChosen;
            if (titleScreen != null) titleScreen.StartPressed += OnTitleStart;
        }

        private void OnDisable()
        {
            if (flow != null)
            {
                flow.IntermissionRequested -= OnIntermission;
                flow.CountdownTick -= OnCountdown;
                flow.EndingRequested -= OnEnding;
                flow.CaughtStinger -= OnCaught;
                flow.SessionFailed -= OnSessionFailed;
                flow.WaitingForRetryDecision = null;
                flow.WaitingForTitle = null;
            }
            if (caughtDialog != null)
            {
                caughtDialog.RetryPressed -= OnRetryChosen;
                caughtDialog.QuitPressed -= OnQuitChosen;
                caughtDialog.HomePressed -= OnHomeChosen;
            }
            if (ending != null) ending.HomePressed -= OnHomeChosen;
            if (titleScreen != null) titleScreen.StartPressed -= OnTitleStart;
        }

        private void Start()
        {
            flow?.StartSession();   // 타이틀 대기 상태로 들어간다
        }

        private void OnTitleStart() => _waitingTitle = false;

        private void OnRetryChosen() => _waitingRetry = false;

        /// <summary>
        /// '메인화면' — 진행 중이던 판을 버리고 타이틀로 돌아간다.
        /// 씬은 다시 로드하지 않는다. 캘리브레이션을 유지하기 위해서다.
        /// </summary>
        private void OnHomeChosen()
        {
            flow?.RequestReturnToTitle();
            _waitingRetry = false;      // 대기 루프를 풀어 세션을 끝낸다
            StartCoroutine(BackToTitle());
        }

        private IEnumerator BackToTitle()
        {
            yield return null;          // GameFlow 가 yield break 할 한 프레임을 준다

            Time.timeScale = 1f;
            AudioListener.pause = false;

            if (countdownContent != null) countdownContent.SetActive(false);
            if (intermissionContent != null) intermissionContent.SetActive(false);
            caughtDialog?.Hide();

            _waitingTitle = true;
            titleScreen?.Show();        // 기록판도 여기서 갱신된다
            flow?.StartSession();
        }

        private void OnQuitChosen()
        {
            caughtDialog?.Hide();
            exitDialog?.Open();
            // 종료를 취소하면 다시 선택창으로 돌아온다
            StartCoroutine(ReopenIfCancelled());
        }

        private IEnumerator ReopenIfCancelled()
        {
            yield return null;
            while (exitDialog != null && exitDialog.IsOpen) yield return null;
            if (_waitingRetry && caughtDialog != null && rounds != null)
                caughtDialog.Show(rounds.RoundNumber, rounds.Lives);
        }

        private void Update()
        {
            if (roundLabel != null && rounds != null)
                roundLabel.text = $"{rounds.RoundNumber}교시";
            // 라이프 표시는 없앴다. 한 번 걸리면 즉시 패배라 남은 기회라는 개념이 없다.
            if (livesLabel != null && livesLabel.gameObject.activeSelf)
                livesLabel.gameObject.SetActive(false);
        }

        private int _lastFailScore;

        /// <summary>적발로 판이 끝났다. 점수를 받아두고 적발창에 넘긴다.</summary>
        private void OnSessionFailed(int reachedRound, int finalScore) => _lastFailScore = finalScore;

        // ───────────────────────────────────────────── 이벤트

        private void OnCountdown(int roundNumber, float t)
        {
            if (countdownContent == null) return;
            bool show = t < 1f;
            if (countdownContent.activeSelf != show) countdownContent.SetActive(show);
            if (countdownLabel != null)
                countdownLabel.text = t < 0.5f ? "차렷" : "시작";
        }

        private void OnIntermission(int endedRound, RoundConfig next, int roundScore)
        {
            if (intermissionContent == null) return;
            if (_intermissionAnim != null) StopCoroutine(_intermissionAnim);
            _intermissionAnim = StartCoroutine(IntermissionRoutine(endedRound, next, roundScore));
        }

        private IEnumerator IntermissionRoutine(int endedRound, RoundConfig next, int roundScore)
        {
            intermissionContent.SetActive(true);
            if (intermissionTitle != null) intermissionTitle.text = $"{endedRound}교시 종료";
            if (intermissionScore != null) intermissionScore.text = "0";

            // 3교시는 밤이라 배너 색을 반전한다
            bool isNight = next != null && next.roundIndex >= 3;
            if (bannerBg != null) bannerBg.color = isNight ? night : amber;
            if (bannerText != null)
            {
                bannerText.color = isNight ? chalk : ink;
                bannerText.text = next != null ? next.bannerText : "";
            }

            // 0.375~1.2s 도장, 1.2~2.2s 점수 카운트업
            yield return new WaitForSecondsRealtime(0.375f);

            float t = 0f;
            const float countDur = 1.0f;
            while (t < countDur)
            {
                t += Time.unscaledDeltaTime;
                float e = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / countDur), 2f);
                if (intermissionScore != null)
                    intermissionScore.text = Mathf.FloorToInt(roundScore * e).ToString("N0");
                yield return null;
            }
            if (intermissionScore != null) intermissionScore.text = roundScore.ToString("N0");

            yield return new WaitForSecondsRealtime(1.2f);
            intermissionContent.SetActive(false);
            _intermissionAnim = null;
        }

        private void OnCaught(int roundNumber, int _)
        {
            if (countdownContent != null) countdownContent.SetActive(false);
            if (intermissionContent != null) intermissionContent.SetActive(false);

            _waitingRetry = true;
            caughtDialog?.Show(roundNumber, _lastFailScore);
        }

        private void OnEnding(int totalScore)
        {
            if (ending == null) return;

            int retries = rounds != null ? rounds.RetryCount : 0;
            int cleared = rounds != null ? rounds.TotalRounds : 3;
            int max = rounds != null ? rounds.TheoreticalMaxScore : 0;

            // 3라운드를 다 깼어도 점수가 이론상 최대의 90% 에 못 미치면 나쁜 엔딩이다.
            // "완주했다"가 아니라 "얼마나 잘했나"로 갈린다.
            bool good = rounds != null && rounds.QualifiesForGoodEnding;

            // 최고 기록 3개에 제출한다. 순위에 들면 1~3, 아니면 0.
            Molae.Core.ScoreBoard.Submit(totalScore, cleared, true);

            ending.Play(totalScore, max, good, retries, cleared);
        }

        /// <summary>HUD 의 '그만' 버튼이 호출한다.</summary>
        public void OnQuitButtonPressed() => exitDialog?.Open();
    }
}
