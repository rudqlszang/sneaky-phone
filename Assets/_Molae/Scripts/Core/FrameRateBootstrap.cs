using UnityEngine;

namespace Molae.Core
{
    /// <summary>
    /// 씬 로드 전에 실행되는 부팅 설정.
    ///
    /// Android/iOS에서 Application.targetFrameRate 의 기본값 -1 은 배터리 절약을 위해
    /// "30fps 고정"으로 동작한다. 60fps를 목표로 한다면 반드시 명시적으로 설정해야 한다.
    /// QualitySettings.vSyncCount 는 모바일에서 아예 무시되므로 vSync로는 제어할 수 없다.
    ///
    /// 또한 targetFrameRate 는 디스플레이 주사율의 약수로 내림된다.
    /// (60Hz 기기에서 45를 요청하면 설정되지 않고, 25를 요청하면 실제로는 20fps가 된다)
    /// </summary>
    public static class FrameRateBootstrap
    {
        public const int DefaultTargetFrameRate = 60;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            Apply(DefaultTargetFrameRate);
        }

        /// <summary>MolaeConfig 값으로 다시 적용하고 싶을 때 호출한다.</summary>
        public static void Apply(int targetFrameRate)
        {
            // 에디터/데스크톱에서의 일관성을 위해 꺼둔다. 모바일에서는 무시된다.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = targetFrameRate;

            // 50초 세션 도중 화면이 꺼지면 시선 추적이 끊긴다.
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
        }
    }
}
