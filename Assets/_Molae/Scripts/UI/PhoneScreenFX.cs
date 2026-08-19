using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Molae.Core;
using Molae.Gaze;

namespace Molae.UI
{
    /// <summary>
    /// 폰 화면이 "살아 있는" 느낌을 만드는 연출. 메신저에 타자를 치는 것처럼 보이게 한다.
    ///
    /// 목적은 예쁘게 하는 게 아니라 <b>시선을 폰에 붙잡아 두는 것</b>이다.
    /// 그래서 모션 예산 3원칙을 지킨다:
    ///   1) 무한 루프 모션의 진폭은 화면 8px 이하
    ///   2) 화면 24px 이상 이동은 2.6초에 1회 이하
    ///   3) 폰 화면 안 어떤 연출도 400ms를 넘지 않는다
    ///
    /// 시간축은 _t 하나만 쓴다. 코루틴 여러 개를 돌리면 깜빡임 위상이 어긋나 산만해진다.
    /// </summary>
    public class PhoneScreenFX : MonoBehaviour
    {
        [Header("연결")]
        [SerializeField] private GameDirector director;
        [SerializeField] private GazeService gaze;

        [Header("타이핑 인디케이터 (점 3개)")]
        [SerializeField] private RectTransform[] dots = new RectTransform[3];
        [SerializeField] private Color dotIdle = new Color(0.776f, 0.624f, 0.647f);   // #C69FA5
        [SerializeField] private Color dotActive = new Color(0.286f, 0.302f, 0.494f); // #494D7E
        [Tooltip("점이 튀는 높이(화면 px). 8px 이하로 유지.")]
        [SerializeField] private float dotJump = 8f;

        [Header("커서")]
        [SerializeField] private Image cursor;
        [Tooltip("반전 간격(초). 0.5 = 1Hz, 광과민 안전 범위.")]
        [SerializeField] private float cursorBlinkSec = 0.5f;

        [Header("말풍선")]
        [SerializeField] private RectTransform bubbleRoot;
        [SerializeField] private GameObject bubblePrefab;
        [SerializeField] private int maxBubbles = 4;
        [Tooltip("말풍선이 위로 밀려나는 간격(화면 px).")]
        [SerializeField] private float bubbleStackStep = 56f;

        [Header("사이클 타이밍 (초)")]
        [Tooltip("인디케이터 표시 → 정지 → 말풍선 등장 순서. 정지 구간이 있어야 다음 등장이 눈에 띈다.")]
        [SerializeField] private float indicatorSec = 0.90f;
        [SerializeField] private float pauseSec = 0.30f;
        [SerializeField] private float popSec = 0.20f;
        [SerializeField] private float cycleSec = 2.60f;
        [Tooltip("위험 상태일 때는 주기를 줄여 시선을 더 강하게 붙잡는다.")]
        [SerializeField] private float dangerCycleSec = 1.40f;

        [Header("메시지 풀")]
        [SerializeField, TextArea]
        private string[] messages = {
            "ㅋㅋㅋㅋㅋ",
            "야 지금 수업중",
            "선생님 뒤돌았어",
            "빨리 답장해",
            "아 걸릴뻔",
            "ㅇㅇ 나 지금 폰함",
            "조용히 해봐",
            "급식 뭐야 오늘",
        };

        private float _t;
        private float _cycleT;
        private int _phase;                 // 0 인디케이터 / 1 정지 / 2 팝 / 3 대기
        private readonly List<RectTransform> _bubbles = new List<RectTransform>();
        private int _msgIndex;

        private void Awake()
        {
            if (director == null) director = FindFirstObjectByType<GameDirector>();
            if (gaze == null) gaze = FindFirstObjectByType<GazeService>();
        }

        private void Update()
        {
            _t += Time.deltaTime;

            bool looking = gaze != null && gaze.IsLookingAtPhone;
            bool danger = director != null && director.Teacher != null && director.Teacher.IsDangerous;

            UpdateDots(looking);
            UpdateCursor(looking);

            // 폰을 보고 있을 때만 대화가 진행된다. 안 보면 멈춘다 —
            // "내가 보고 있어서 진행된다"는 인과가 느껴져야 시선을 붙잡는다.
            if (!looking) return;

            float cycle = danger ? dangerCycleSec : cycleSec;
            _cycleT += Time.deltaTime;

            if (_phase == 0 && _cycleT >= indicatorSec) { _phase = 1; _cycleT = 0f; }
            else if (_phase == 1 && _cycleT >= pauseSec) { SpawnBubble(); _phase = 2; _cycleT = 0f; }
            else if (_phase == 2 && _cycleT >= popSec) { _phase = 3; _cycleT = 0f; }
            else if (_phase == 3 && _cycleT >= Mathf.Max(0.1f, cycle - indicatorSec - pauseSec - popSec))
            { _phase = 0; _cycleT = 0f; }
        }

        private void UpdateDots(bool active)
        {
            if (dots == null) return;
            bool show = active && _phase == 0;

            // 순차형: 1.5초를 6등분해 0.25초마다 점 하나만 튄다.
            int slot = (int)(Mathf.Repeat(_t, 1.5f) / 0.25f);
            for (int i = 0; i < dots.Length; i++)
            {
                if (dots[i] == null) continue;
                dots[i].gameObject.SetActive(show);
                if (!show) continue;

                bool up = (slot % 2 == 0) && (slot / 2 == i);
                var p = dots[i].anchoredPosition;
                p.y = up ? dotJump : 0f;          // Lerp 없이 두 값만 — 픽셀아트에 맞다
                dots[i].anchoredPosition = p;

                var img = dots[i].GetComponent<Image>();
                if (img != null) img.color = up ? dotActive : dotIdle;
            }
        }

        private void UpdateCursor(bool active)
        {
            if (cursor == null) return;
            cursor.enabled = active && Mathf.Repeat(_t, cursorBlinkSec * 2f) < cursorBlinkSec;
        }

        private void SpawnBubble()
        {
            if (bubbleRoot == null || bubblePrefab == null) return;

            // 기존 말풍선을 위로 민다
            for (int i = 0; i < _bubbles.Count; i++)
            {
                if (_bubbles[i] == null) continue;
                var p = _bubbles[i].anchoredPosition;
                p.y += bubbleStackStep;
                _bubbles[i].anchoredPosition = p;
            }

            // 넘치면 가장 오래된 것 제거
            while (_bubbles.Count >= maxBubbles)
            {
                if (_bubbles[0] != null) Destroy(_bubbles[0].gameObject);
                _bubbles.RemoveAt(0);
            }

            var go = Instantiate(bubblePrefab, bubbleRoot);
            var rt = go.GetComponent<RectTransform>();
            rt.anchoredPosition = Vector2.zero;
            _bubbles.Add(rt);

            var label = go.GetComponentInChildren<TMP_Text>();
            if (label != null && messages.Length > 0)
            {
                label.text = messages[_msgIndex % messages.Length];
                _msgIndex++;
            }

            StartCoroutine(PopIn(rt, go.GetComponent<CanvasGroup>()));
        }

        /// <summary>말풍선 등장 — 200ms 감속. 스케일은 3단 정수 스텝으로 픽셀 무결성을 지킨다.</summary>
        private System.Collections.IEnumerator PopIn(RectTransform rt, CanvasGroup cg)
        {
            float t = 0f;
            Vector2 target = rt.anchoredPosition;
            while (t < popSec)
            {
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / popSec);
                float e = 1f - Mathf.Pow(1f - u, 3f);

                var p = target; p.y = target.y + Mathf.Lerp(24f, 0f, e);
                rt.anchoredPosition = p;

                // 연속 보간 대신 3단 양자화
                float s = u < 0.33f ? 0.6f : (u < 0.66f ? 1.1f : 1.0f);
                rt.localScale = new Vector3(1f, s, 1f);
                if (cg != null) cg.alpha = u < 0.33f ? 0f : (u < 0.66f ? 0.5f : 1f);
                yield return null;
            }
            rt.anchoredPosition = target;
            rt.localScale = Vector3.one;
            if (cg != null) cg.alpha = 1f;
        }

        /// <summary>적발 시 폰 화면을 즉시 죽인다.</summary>
        public void OnCaught()
        {
            _t = 0f; _cycleT = 0f; _phase = 0;
            if (cursor != null) cursor.enabled = false;
            if (dots != null) foreach (var d in dots) if (d != null) d.gameObject.SetActive(false);
        }
    }
}
