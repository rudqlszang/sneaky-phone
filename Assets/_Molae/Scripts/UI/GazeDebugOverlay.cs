using System.Text;
using UnityEngine;
using Molae.Core;
using Molae.Gaze;

namespace Molae.UI
{
    /// <summary>
    /// 시선 추적이 실제로 살아 있는지 화면에서 바로 확인하기 위한 디버그 오버레이.
    ///
    /// 이게 없으면 "게임이 안 되는 건지, 시선이 안 잡히는 건지, 판정 영역이 틀린 건지"를
    /// 구분할 방법이 없다. 실기기에 adb 를 못 붙이는 상황에서는 화면 표시가 유일한 진단 수단이다.
    ///
    /// 화면 우상단을 3초 안에 4번 탭하면 켜지고 꺼진다(출시 빌드에서도 숨겨둘 수 있게).
    /// </summary>
    public class GazeDebugOverlay : MonoBehaviour
    {
        [SerializeField] private GameDirector director;
        [SerializeField] private GazeService gaze;

        [Tooltip("시작하자마자 켜둘지. 켜두면 화면을 가리므로 기본은 끈다. " +
                 "우상단을 3초 안에 4번 탭하면 켜진다.")]
        [SerializeField] private bool visibleOnStart = true;

        [Tooltip("켜져 있어도 상세 패널 없이 시선 십자선만 그린다. 플레이를 방해하지 않는다.")]
        [SerializeField] private bool crosshairOnly = true;

        [Tooltip("시선 좌표에 십자선을 그린다. 판정 영역이 맞는지 눈으로 확인할 때 필수.")]
        [SerializeField] private bool drawCrosshair = true;

        private bool _visible;
        private GUIStyle _style;
        private GUIStyle _boxStyle;
        private Texture2D _dot;
        private Texture2D _panel;

        private int _tapCount;
        private float _firstTapTime;

        private void Awake()
        {
            if (director == null) director = FindFirstObjectByType<GameDirector>();
            if (gaze == null) gaze = FindFirstObjectByType<GazeService>();
            _visible = visibleOnStart;

            _dot = new Texture2D(1, 1);
            _dot.SetPixel(0, 0, Color.white);
            _dot.Apply();

            _panel = new Texture2D(1, 1);
            _panel.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.72f));
            _panel.Apply();
        }

        private void Update()
        {
            // 우상단 4연타로 토글
            if (Input.touchCount > 0)
            {
                Touch t = Input.GetTouch(0);
                if (t.phase == TouchPhase.Began
                    && t.position.x > Screen.width * 0.7f
                    && t.position.y > Screen.height * 0.85f)
                {
                    RegisterTap();
                }
            }
#if UNITY_EDITOR
            if (Input.GetKeyDown(KeyCode.BackQuote)) _visible = !_visible;
#endif
        }

        private void RegisterTap()
        {
            if (Time.unscaledTime - _firstTapTime > 3f) { _tapCount = 0; _firstTapTime = Time.unscaledTime; }
            _tapCount++;
            if (_tapCount >= 4) { _visible = !_visible; _tapCount = 0; }
        }

        private void OnGUI()
        {
            if (!_visible) return;

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = Mathf.RoundToInt(Screen.height * 0.018f),
                    richText = true,
                    alignment = TextAnchor.UpperLeft,
                    wordWrap = false,
                };
                _boxStyle = new GUIStyle(GUI.skin.box);
                _boxStyle.normal.background = _panel;
            }

            DrawCrosshair();

            // 십자가만 그린다. 상태 텍스트는 플레이 화면을 가리므로 띄우지 않는다.
            if (crosshairOnly) return;

            string text = BuildText();
            float w = Screen.width * 0.62f;
            float h = Screen.height * 0.34f;
            GUI.Box(new Rect(12, 12, w, h), GUIContent.none, _boxStyle);
            GUI.Label(new Rect(24, 22, w - 24, h - 20), text, _style);
        }

        private void DrawCrosshair()
        {
            if (!drawCrosshair || gaze == null) return;

            GazeSample s = gaze.LatestSample;
            if (!s.HasUsablePoint) return;

            // GUI 좌표계는 좌상단 원점이라 y를 뒤집는다
            float x = s.Point.x;
            float y = Screen.height - s.Point.y;
            float len = Screen.height * 0.03f;
            float th = Mathf.Max(2f, Screen.height * 0.004f);

            Color c = gaze.IsLookingAtPhone ? new Color(0.35f, 1f, 0.45f) : new Color(1f, 0.85f, 0.3f);
            Color old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(new Rect(x - len, y - th * 0.5f, len * 2f, th), _dot);
            GUI.DrawTexture(new Rect(x - th * 0.5f, y - len, th, len * 2f), _dot);
            GUI.color = old;
        }

        private string BuildText()
        {
            var sb = new StringBuilder();

            if (gaze == null) { sb.AppendLine("<color=#ff6666>GazeService 없음</color>"); return sb.ToString(); }

            // ── 시선 시스템 ──
            string prov = gaze.UsingMock ? "<color=#ffcc44>Mock(마우스)</color>" : "<color=#88ff88>SeeSo</color>";
            sb.AppendLine($"프로바이더 : {prov}");

            string st = gaze.State.ToString();
            string stColor = gaze.State == GazeProviderState.Tracking ? "#88ff88"
                           : gaze.State == GazeProviderState.Failed ? "#ff6666" : "#ffcc44";
            sb.AppendLine($"상태       : <color={stColor}>{st}</color>");

            if (!string.IsNullOrEmpty(gaze.LastError))
                sb.AppendLine($"<color=#ff6666>오류: {gaze.LastError}</color>");

            GazeSample s = gaze.LatestSample;
            string trackColor = s.Tracking == GazeTracking.Success ? "#88ff88"
                              : s.Tracking == GazeTracking.LowConfidence ? "#ffcc44" : "#ff6666";
            sb.AppendLine($"추적       : <color={trackColor}>{s.Tracking}</color>   화면: {s.Screen}");
            sb.AppendLine($"좌표       : ({s.Point.x:0}, {s.Point.y:0})   눈: {s.Movement}");

            string look = gaze.IsLookingAtPhone
                ? "<color=#88ff88>폰 보는 중 ●</color>"
                : "<color=#888888>폰 안 봄 ○</color>";
            sb.AppendLine($"판정       : {look}");

            if (gaze.IsFaceMissing) sb.AppendLine("<color=#ff6666>얼굴 미검출 → 일시정지</color>");

            sb.AppendLine();

            // ── 게임 상태 ──
            if (director == null) { sb.AppendLine("<color=#ff6666>GameDirector 없음</color>"); return sb.ToString(); }

            sb.AppendLine($"페이즈     : {director.Phase}   {director.Elapsed:0.0}s / {(director.Config != null ? director.Config.SessionDuration : 0):0}s");

            if (director.Teacher != null)
            {
                string ts = director.Teacher.State.ToString();
                string tc = director.Teacher.IsDangerous ? "#ff6666"
                          : director.Teacher.IsTelegraphing ? "#ffcc44" : "#88ff88";
                sb.AppendLine($"선생님     : <color={tc}>{ts}</color>   회전 {director.Teacher.TurnAmount:0.00}");
            }

            if (director.Suspicion != null)
            {
                float sus = director.Suspicion.Suspicion;
                int bars = Mathf.RoundToInt(sus * 20f);
                sb.AppendLine($"인지도     : {sus:0.00} [{new string('|', bars)}{new string('.', 20 - bars)}]");
                if (director.Suspicion.InGracePeriod)
                    sb.AppendLine($"<color=#ffcc44>유예 {director.Suspicion.GraceRemaining:0.00}s</color>");
            }

            if (director.Score != null)
                sb.AppendLine($"점수       : {director.Score.Score}   콤보 x{director.Score.Multiplier:0.##}   응시 {director.Score.SafeWatchSeconds:0.0}s");

            return sb.ToString();
        }

        private void OnDestroy()
        {
            if (_dot != null) Destroy(_dot);
            if (_panel != null) Destroy(_panel);
        }
    }
}
