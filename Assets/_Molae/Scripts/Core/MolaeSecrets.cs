using UnityEngine;

namespace Molae.Core
{
    /// <summary>
    /// 라이선스 키처럼 저장소에 올리면 안 되는 값을 읽는다.
    ///
    /// ── 왜 이렇게 하나 ──
    /// 유니티에는 .env 개념이 없다. 씬이나 프리팹의 [SerializeField] 에 키를 넣으면
    /// 그 값은 Game.unity 안에 평문으로 직렬화되어 커밋된다. 실제로 이 프로젝트도
    /// Game.unity 한 곳에 키가 박혀 있었다.
    ///
    /// 그래서 키를 Resources 안의 텍스트 파일 하나로 빼고, 그 파일만 .gitignore 한다.
    /// Resources 에 두는 이유는 빌드에 포함되어야 실기기에서 동작하기 때문이다.
    /// 저장소에는 같은 이름의 .sample 파일만 올라가고, 받는 사람이 복사해서 자기 키를 넣는다.
    ///
    /// ── 한계를 분명히 해둔다 ──
    /// 이 방식은 "공개 저장소에 키가 올라가는 것"만 막는다.
    /// 빌드된 APK 안에는 키가 그대로 들어가고, APK 를 뜯으면 누구나 꺼낼 수 있다.
    /// 클라이언트에 내려가는 키는 원리상 비밀이 될 수 없다.
    /// 배포용 키의 진짜 방어선은 발급처(SeeSo/Eyedid)의 패키지명 제한이다.
    /// </summary>
    public static class MolaeSecrets
    {
        /// <summary>Resources 안의 파일 이름(확장자 제외).</summary>
        private const string LicenseResource = "seeso_license";

        private static string _cached;
        private static bool _loaded;

        /// <summary>SeeSo 라이선스 키. 없으면 빈 문자열.</summary>
        public static string SeeSoLicenseKey
        {
            get
            {
                if (_loaded) return _cached;
                _loaded = true;

                var asset = Resources.Load<TextAsset>(LicenseResource);
                if (asset == null)
                {
                    _cached = string.Empty;
                    Debug.LogWarning(
                        $"[Molae/Secrets] Resources/{LicenseResource}.txt 가 없습니다. " +
                        $"{LicenseResource}.sample.txt 를 복사해 키를 넣으세요.");
                    return _cached;
                }

                // 편집기에서 붙여넣을 때 따라오는 공백·줄바꿈·따옴표를 제거한다.
                _cached = asset.text.Trim().Trim('"', '\'');

                // 주석 줄(#)과 빈 줄은 건너뛰고 첫 유효 줄만 쓴다.
                foreach (var line in _cached.Split('\n'))
                {
                    string t = line.Trim().Trim('"', '\'');
                    if (t.Length == 0 || t.StartsWith("#")) continue;
                    _cached = t;
                    break;
                }

                if (_cached.StartsWith("#") || _cached.Length == 0)
                {
                    _cached = string.Empty;
                    Debug.LogWarning("[Molae/Secrets] 라이선스 파일에 유효한 키가 없습니다.");
                }
                return _cached;
            }
        }

        public static bool HasLicense => !string.IsNullOrEmpty(SeeSoLicenseKey);
    }
}
