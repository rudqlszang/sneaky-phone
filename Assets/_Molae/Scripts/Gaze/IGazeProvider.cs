using System.Collections.Generic;

namespace Molae.Gaze
{
    /// <summary>
    /// 시선 데이터 공급원 추상화. SeeSo 실기기 구현과 에디터용 마우스 구현이 이걸 구현한다.
    ///
    /// 스레드 안전 규약: SeeSo 콜백은 별도 스레드에서 온다. 구현체는 콜백 안에서
    /// Unity API를 절대 호출하지 말고 volatile 필드/큐에만 기록해야 한다.
    /// 메인 스레드는 매 프레임 Pump()를 호출해 큐를 비우고 Latest를 읽는다.
    /// </summary>
    public interface IGazeProvider
    {
        GazeProviderState State { get; }

        /// <summary>초기화 실패 사유. 성공이면 빈 문자열.</summary>
        string LastError { get; }

        /// <summary>이 프로바이더가 실제 캘리브레이션을 지원하는지.</summary>
        bool SupportsCalibration { get; }

        /// <summary>가장 최근 시선 샘플. Pump() 이후에 읽어야 최신이다.</summary>
        GazeSample Latest { get; }

        /// <summary>초기화 시작. 비동기이므로 State가 Ready가 될 때까지 기다려야 한다.</summary>
        void Initialize(string licenseKey);

        /// <summary>시선 추적 시작.</summary>
        void StartTracking();

        /// <summary>시선 추적 중지. 결과 화면 진입 시 호출해 프레임 드랍을 막는다.</summary>
        void StopTracking();

        /// <summary>완전 해제. OnDestroy에서 호출.</summary>
        void Shutdown();

        /// <summary>메인 스레드에서 매 프레임 호출. 워커 스레드가 쌓아둔 데이터를 반영한다.</summary>
        void Pump();

        /// <summary>1포인트 캘리브레이션 시작.</summary>
        void StartCalibration();

        /// <summary>캘리브레이션 점을 응시하기 시작했음을 알린다(샘플 수집 개시).</summary>
        void CollectCalibrationSamples();

        /// <summary>Pump() 이후 쌓인 캘리브레이션 이벤트를 꺼낸다. 큐는 비워진다.</summary>
        void DrainCalibrationEvents(List<CalibrationEvent> into);
    }
}
