using UnityEngine;

namespace Molae.Feedback
{
    /// <summary>
    /// Android 네이티브 진동. Handheld.Vibrate()는 인자가 없고 길이를 OS가 정하므로
    /// (iOS는 약 500ms 고정) 캐주얼 게임의 짧은 "툭" 피드백에는 쓸 수 없다.
    /// 그래서 AndroidJavaObject로 Vibrator를 직접 호출한다.
    ///
    /// API 레벨 분기가 필수다:
    ///   31+     : getSystemService("vibrator_manager") → getDefaultVibrator()
    ///   26~30   : getSystemService("vibrator") + VibrationEffect.createOneShot(long, int)
    ///   25 이하 : vibrator.vibrate(long)
    ///
    /// 주의: duration은 반드시 (long)으로 명시 캐스팅해야 한다. int로 넘기면
    /// Java의 long 파라미터와 시그니처가 어긋나 호출이 조용히 실패한다.
    ///
    /// 또한 코드에 Handheld.Vibrate() 호출이 없으면 Unity가 VIBRATE 권한을 자동 추가하지
    /// 않는다. AndroidManifest.xml 에 android.permission.VIBRATE 를 직접 넣어야 한다.
    /// </summary>
    public static class HapticService
    {
        public const long LightMs = 20;
        public const long MediumMs = 40;
        public const long HeavyMs = 180;

        public const int LightAmplitude = 80;    // 1~255
        public const int MediumAmplitude = 140;
        public const int HeavyAmplitude = 255;

        private static bool _initialized;
        private static bool _supported;

#if UNITY_ANDROID && !UNITY_EDITOR
        private static AndroidJavaObject _vibrator;
        private static AndroidJavaClass _vibrationEffectClass;
        private static int _apiLevel;
#endif

        /// <summary>사용자가 진동을 껐을 때 false로 설정한다.</summary>
        public static bool Enabled { get; set; } = true;

        private static void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                {
                    _apiLevel = version.GetStatic<int>("SDK_INT");
                }

                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    if (_apiLevel >= 31)
                    {
                        using (var manager = activity.Call<AndroidJavaObject>("getSystemService", "vibrator_manager"))
                        {
                            _vibrator = manager.Call<AndroidJavaObject>("getDefaultVibrator");
                        }
                    }
                    else
                    {
                        _vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                    }
                }

                if (_apiLevel >= 26)
                {
                    _vibrationEffectClass = new AndroidJavaClass("android.os.VibrationEffect");
                }

                _supported = _vibrator != null && _vibrator.Call<bool>("hasVibrator");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Molae/Haptic] 진동 초기화 실패: {e.Message}");
                _supported = false;
            }
#else
            _supported = false;
#endif
        }

        public static void Light() => Vibrate(LightMs, LightAmplitude);
        public static void Medium() => Vibrate(MediumMs, MediumAmplitude);
        public static void Heavy() => Vibrate(HeavyMs, HeavyAmplitude);

        /// <summary>지정한 길이(ms)와 진폭(1~255, 0이면 기기 기본)으로 진동한다.</summary>
        public static void Vibrate(long milliseconds, int amplitude = 0)
        {
            if (!Enabled) return;

            EnsureInitialized();
            if (!_supported) return;

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                if (_apiLevel >= 26 && _vibrationEffectClass != null)
                {
                    int amp = amplitude <= 0 ? -1 : Mathf.Clamp(amplitude, 1, 255); // -1 = DEFAULT_AMPLITUDE
                    using (var effect = _vibrationEffectClass.CallStatic<AndroidJavaObject>(
                               "createOneShot", (long)milliseconds, amp))
                    {
                        _vibrator.Call("vibrate", effect);
                    }
                }
                else
                {
                    _vibrator.Call("vibrate", (long)milliseconds);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Molae/Haptic] 진동 호출 실패: {e.Message}");
            }
#endif
        }

        public static void Cancel()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            EnsureInitialized();
            if (_supported) _vibrator?.Call("cancel");
#endif
        }
    }
}
