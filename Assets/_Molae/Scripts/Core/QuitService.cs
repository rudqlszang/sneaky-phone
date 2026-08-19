using UnityEngine;

namespace Molae.Core
{
    /// <summary>
    /// 안드로이드에서 "진짜로" 앱을 끝낸다.
    ///
    /// Application.Quit() 만으로는 부족하다. 이 호출은 액티비티를 finish 할 뿐이고
    /// 안드로이드는 프로세스를 캐시로 살려둔다. 최근 앱 목록에도 태스크가 남는다.
    /// 그래서 아이콘을 다시 누르면 죽지 않은 프로세스가 그대로 복귀하고,
    /// 플레이어 눈에는 "종료했는데 이어서 시작"으로 보인다.
    ///
    /// 해결: finishAndRemoveTask() 로 태스크까지 지운 뒤 종료한다.
    /// (API 21+ / 우리 minSdk 25 이므로 분기 불필요)
    /// </summary>
    public static class QuitService
    {
        public static void QuitApp()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    // 최근 앱 목록에서도 제거한다. 이게 없으면 태스크가 남아 복귀한다.
                    activity.Call("finishAndRemoveTask");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Molae/Quit] finishAndRemoveTask 실패: {e.Message}");
            }
#endif
            Application.Quit();

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }
    }
}
