using TMPro;
using UnityEngine;
using Molae.Core;
using Molae.Gaze;

namespace Molae.UI
{
    /// <summary>
    /// 얼굴 인식 실패 시 뜨는 일시정지 안내.
    ///
    /// 요구사항대로 게임오버가 아니라 일시정지다. 얼굴이 다시 잡히면 자동으로 재개된다.
    /// 시선 추적은 저조도·역광·거리 문제로 흔하게 끊기므로, 이 상황을 실패로 처리하면
    /// 플레이어는 자기 잘못이 아닌 일로 벌을 받는다고 느낀다.
    ///
    /// 안내 문구는 원인별로 바꿔서 무엇을 고쳐야 하는지 알려준다.
    /// </summary>
    public class FaceMissingOverlay : MonoBehaviour
    {
        [Header("연결")]
        [SerializeField] private GameDirector director;
        [SerializeField] private GazeService gaze;

        [Header("루트")]
        [SerializeField] private GameObject root;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private float fadeDuration = 0.2f;

        [Header("문구")]
        [SerializeField] private TMP_Text titleLabel;
        [SerializeField] private TMP_Text bodyLabel;

        [SerializeField] private string titleText = "잠깐 멈췄어요";
        [SerializeField, TextArea] private string faceMissingText = "얼굴이 화면에 보이지 않아요.\n기기를 얼굴 정면으로 들어주세요.";
        [SerializeField, TextArea] private string outOfScreenText = "시선이 화면 밖으로 나갔어요.";
        [SerializeField, TextArea] private string darkText = "주변이 어두우면 인식이 어려워요.\n조금 더 밝은 곳에서 해보세요.";

        [Tooltip("얼굴 미검출이 이 시간(초) 이상 이어지면 조명 안내로 문구를 바꾼다.")]
        [SerializeField] private float longMissingThreshold = 4f;

        private float _missingElapsed;
        private bool _visible;

        private void Reset()
        {
            director = FindFirstObjectByType<GameDirector>();
            gaze = FindFirstObjectByType<GazeService>();
        }

        private void Awake()
        {
            if (director == null) director = FindFirstObjectByType<GameDirector>();
            if (gaze == null) gaze = FindFirstObjectByType<GazeService>();

            if (titleLabel != null) titleLabel.text = titleText;
            if (root != null) root.SetActive(false);
            if (canvasGroup != null) canvasGroup.alpha = 0f;
        }

        private void OnEnable()
        {
            if (director != null) director.PhaseChanged += HandlePhaseChanged;
        }

        private void OnDisable()
        {
            if (director != null) director.PhaseChanged -= HandlePhaseChanged;
        }

        private void HandlePhaseChanged(GamePhase phase)
        {
            bool show = phase == GamePhase.Paused;
            if (show == _visible) return;

            _visible = show;
            if (show)
            {
                _missingElapsed = 0f;
                if (root != null) root.SetActive(true);
            }
        }

        private void Update()
        {
            if (canvasGroup != null)
            {
                float target = _visible ? 1f : 0f;
                float step = fadeDuration <= 0f ? 1f : Time.unscaledDeltaTime / fadeDuration;
                canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, target, step);

                if (!_visible && canvasGroup.alpha <= 0f && root != null && root.activeSelf)
                {
                    root.SetActive(false);
                }
            }

            if (!_visible) return;

            _missingElapsed += Time.unscaledDeltaTime;
            if (bodyLabel != null) bodyLabel.text = ResolveMessage();
        }

        private string ResolveMessage()
        {
            if (_missingElapsed >= longMissingThreshold) return darkText;
            if (gaze != null && gaze.IsOutOfScreen && !gaze.IsFaceMissing) return outOfScreenText;
            return faceMissingText;
        }
    }
}
