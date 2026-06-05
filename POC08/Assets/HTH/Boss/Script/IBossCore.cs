// ============================================================
// IBossCore.cs  v1.0
// 모든 보스가 구현하는 공통 인터페이스
//
// [역할]
//   BossCameraDirector, BossWardenFeedback 등
//   보스 종류에 무관하게 공통 이벤트를 구독할 수 있도록 한다.
//
// [구현 대상]
//   BossWardenCore : IBossCore 구현
//   추후 추가 보스  : IBossCore 구현
//
// [namespace] SEAL
// ============================================================

using System;

namespace SEAL
{
    /// <summary>
    /// 모든 보스 코어가 구현하는 공통 인터페이스.
    ///
    /// [공통 이벤트]
    ///   OnGroggyEnter   : 그로기 진입
    ///   OnGroggyExit    : 그로기 해제 (실패)
    ///   OnDilPhaseEnter : 딜 페이즈 진입
    ///   OnDilPhaseExit  : 딜 페이즈 종료
    ///   OnDead          : 보스 처치
    /// </summary>
    public interface IBossCore
    {
        /// <summary>그로기 진입 시 발행.</summary>
        event Action OnGroggyEnter;

        /// <summary>그로기 해제 (실패) 시 발행.</summary>
        event Action OnGroggyExit;

        /// <summary>딜 페이즈 진입 시 발행.</summary>
        event Action OnDilPhaseEnter;

        /// <summary>딜 페이즈 종료 시 발행.</summary>
        event Action OnDilPhaseExit;

        /// <summary>보스 처치 시 발행.</summary>
        event Action OnDead;

        /// <summary>현재 그로기 여부.</summary>
        bool IsGroggy { get; }

        /// <summary>현재 딜 페이즈 여부.</summary>
        bool IsDilPhase { get; }

        /// <summary>처치 여부.</summary>
        bool IsDead { get; }
    }
}