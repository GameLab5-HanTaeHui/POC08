// ============================================================
// IBossCore.cs  v2.0
// 모든 보스가 구현하는 공통 인터페이스
//
// [v2.0 변경 — Groggy 제거]
//   제거:
//     OnGroggyEnter 이벤트
//     OnGroggyExit 이벤트
//     IsGroggy 프로퍼티
//
//   유지:
//     OnDilPhaseEnter / OnDilPhaseExit
//     OnDead
//     IsDilPhase / IsDead
//
// [namespace] SEAL
// ============================================================

using System;

namespace SEAL
{
    /// <summary>
    /// 모든 보스 코어가 구현하는 공통 인터페이스. (v2.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [공통 이벤트 v2.0]
    ///   OnDilPhaseEnter : DilPhase 진입 (Part 전체 봉인 완료)
    ///   OnDilPhaseExit  : DilPhase 종료 (성공 or 실패)
    ///   OnDead          : 보스 처치
    /// ────────────────────────────────────────────────────
    /// </summary>
    public interface IBossCore
    {
        /// <summary>DilPhase 진입 시 발행. Part 전체 봉인 완료 → 코어 활성.</summary>
        event Action OnDilPhaseEnter;

        /// <summary>DilPhase 종료 시 발행 (성공 or 실패).</summary>
        event Action OnDilPhaseExit;

        /// <summary>보스 처치 시 발행.</summary>
        event Action OnDead;

        /// <summary>현재 DilPhase 여부.</summary>
        bool IsDilPhase { get; }

        /// <summary>처치 여부.</summary>
        bool IsDead { get; }
    }
}