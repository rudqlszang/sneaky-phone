using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Molae.Core;

namespace Molae.UI
{
    /// <summary>
    /// 선생님에게 적발됐을 때 뜨는 창. "다시하기 / 종료하기" 를 고르게 한다.
    ///
    /// 구조 주의: 이 컴포넌트는 항상 활성인 루트에 붙이고, 실제로 켜고 끄는 것은
    /// content 자식이다. 컴포넌트가 자기 GameObject 를 끄면 Awake/OnEnable 이
    /// 다시 돌면서 스스로를 꺼버리는 사고가 난다.
    ///
    /// 사망~재개는 2초 이내를 목표로 한다. 실패 연출이 길면 이탈한다.
    /// </summary>
    public class CaughtDialog : MonoBehaviour
    {
        [Header("루트 (컴포넌트는 항상 활성, content 만 토글)")]
        [SerializeField] private GameObject content;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private RectTransform panel;

        [Header("내용")]
        [SerializeField] private TMP_Text titleLabel;
        [SerializeField] private TMP_Text bodyLabel;
        [SerializeField] private TMP_Text livesLabel;
        [SerializeField] private Button retryButton;
        [SerializeField] private Button quitButton;
        [Tooltip("메인화면으로 돌아가는 버튼.")]
        [SerializeField] private Button homeButton;

        [Header("문구")]
        [SerializeField] private string titleText = "선생님한테 걸렸습니다!";
        [Tooltip("{0}=도달 교시, {1}=점수")]
        [SerializeField, TextArea] private string bodyFormat = "{0}교시에서 걸렸습니다   {1}점";

        [Header("연출")]
        [SerializeField] private float openSec = 0.25f;
        [SerializeField] private float slideFrom = -120f;

        private bool _open;
        private Coroutine _anim;

        /// <summary>'다시하기'를 눌렀을 때. GameFlow 가 구독한다.</summary>
        public event System.Action RetryPressed;

        /// <summary>'종료하기'를 눌렀을 때.</summary>
        public event System.Action QuitPressed;

        /// <summary>'메인화면'을 눌렀을 때.</summary>
        public event System.Action HomePressed;

        public bool IsOpen => _open;

        private void Awake()
        {
            if (titleLabel != null) titleLabel.text = titleText;
            if (content != null) content.SetActive(false);
            if (retryButton != null) retryButton.onClick.AddListener(OnRetry);
            if (quitButton != null) quitButton.onClick.AddListener(OnQuit);
            if (homeButton != null) homeButton.onClick.AddListener(OnHome);
        }

        /// <summary>적발 결과를 띄운다. 한 번 걸리면 그 판은 끝이므로 라이프 표시는 없다.</summary>
        public void Show(int roundNumber, int finalScore)
        {
            if (_open) return;
            _open = true;

            if (content != null) content.SetActive(true);
            if (titleLabel != null) titleLabel.text = titleText;
            if (bodyLabel != null) bodyLabel.text = string.Format(bodyFormat, roundNumber, finalScore.ToString("N0"));

            // 기록 순위에 들었으면 알려준다. 다시 도전할 이유가 된다.
            if (livesLabel != null)
            {
                int rank = Molae.Core.ScoreBoard.Submit(finalScore, roundNumber, false);
                livesLabel.text = rank == 1 ? "<color=#FCD68D>신기록!</color>"
                                            : $"최고 기록  {Molae.Core.ScoreBoard.Best:N0}";
            }

            if (retryButton != null) retryButton.interactable = true;
            if (quitButton != null) quitButton.interactable = true;
            if (homeButton != null) homeButton.interactable = true;

            if (_anim != null) StopCoroutine(_anim);
            _anim = StartCoroutine(Animate(true));
        }

        public void Hide()
        {
            if (!_open) return;
            _open = false;
            if (_anim != null) StopCoroutine(_anim);
            _anim = StartCoroutine(HideRoutine());
        }

        private IEnumerator HideRoutine()
        {
            yield return Animate(false);
            if (content != null) content.SetActive(false);
            _anim = null;
        }

        private IEnumerator Animate(bool opening)
        {
            float t = 0f;
            while (t < openSec)
            {
                t += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(t / openSec);
                float e = opening ? 1f - Mathf.Pow(1f - u, 3f) : u * u;

                if (panel != null)
                {
                    var p = panel.anchoredPosition;
                    p.y = opening ? Mathf.Lerp(slideFrom, 0f, e) : Mathf.Lerp(0f, slideFrom, e);
                    panel.anchoredPosition = new Vector2(p.x, Mathf.Round(p.y / 4f) * 4f);
                }
                if (canvasGroup != null) canvasGroup.alpha = opening ? u : 1f - u;
                yield return null;
            }
            if (canvasGroup != null) canvasGroup.alpha = opening ? 1f : 0f;
            _anim = null;
        }

        private void OnRetry()
        {
            if (retryButton != null) retryButton.interactable = false;
            if (quitButton != null) quitButton.interactable = false;
            Hide();
            RetryPressed?.Invoke();
        }

        private void OnQuit()
        {
            SetButtons(false);
            QuitPressed?.Invoke();
        }

        private void OnHome()
        {
            SetButtons(false);
            Hide();
            HomePressed?.Invoke();
        }

        private void SetButtons(bool on)
        {
            if (retryButton != null) retryButton.interactable = on;
            if (quitButton != null) quitButton.interactable = on;
            if (homeButton != null) homeButton.interactable = on;
        }
    }
}
