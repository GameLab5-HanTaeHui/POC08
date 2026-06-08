// ============================================================
// BossEventHub.cs  v1.0
// Boss 이벤트 중앙 허브 — Step 5
//
// [역할]
//   Boss_Warden 내부 관리자들이 직접 서로를 강하게 참조하지 않도록
//   상태 / 공격 / 봉인 / 피격 / 연출 요청 이벤트를 한곳에 모은다.
//
// [Step 5 범위]
//   - 이벤트 허브 컴포넌트 추가
//   - BossWardenCore가 SealStateManager 상태 이벤트를 수신한 뒤
//     기존 IBossCore event와 함께 BossEventHub에도 발행한다.
//   - 아직 AI / Attack / Seal / Hit / VFX / Sound 전체를 EventHub 기반으로
//     갈아엎지는 않는다. 이후 Step에서 구독자를 단계적으로 이동한다.
//
// [부착 위치]
//   Boss_Warden Root 오브젝트
//
// [namespace] SEAL
// ============================================================

using System;
using UnityEngine;

namespace SEAL
{
    /// <summary>
    /// 보스 내부 이벤트 중앙 허브.
    /// Step 5에서는 상태 이벤트를 중심으로 먼저 배치하고,
    /// 이후 Step에서 공격/봉인/피격/VFX/Sound 흐름을 이 허브로 이동한다.
    /// </summary>
    [DefaultExecutionOrder(-80)]
    public class BossEventHub : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        // Runtime State
        // ══════════════════════════════════════════════════════

        public enum BossRuntimeState
        {
            None,
            Idle,
            Chase,
            Attack,
            Recovery,
            DilPhase,
            FinalSeal,
            Dead,
        }

        // ══════════════════════════════════════════════════════
        // 상태 이벤트
        // ══════════════════════════════════════════════════════

        public event Action<BossRuntimeState> OnBossStateChanged;
        public event Action OnDilPhaseEnter;
        public event Action OnDilPhaseExit;
        public event Action OnFinalSealReady;
        public event Action<int> OnPhaseChanged;
        public event Action OnDead;

        // ══════════════════════════════════════════════════════
        // 공격 이벤트 — 이후 BossAttackManager 단계에서 본격 사용
        // ══════════════════════════════════════════════════════

        public event Action<BossPatternBase> OnAttackStarted;
        public event Action<BossPatternBase> OnAttackWarning;
        public event Action<BossPatternBase> OnAttackActive;
        public event Action<BossPatternBase> OnAttackRecovery;
        public event Action<BossPatternBase> OnAttackEnded;
        public event Action<BossPatternBase> OnAttackInterrupted;

        // ══════════════════════════════════════════════════════
        // 피격 이벤트 — 이후 BossHitManager 단계에서 본격 사용
        // ══════════════════════════════════════════════════════

        public event Action<WardenPartType, Vector3> OnPartHit;
        public event Action<WardenPartType, Vector3> OnWeakPointHit;
        public event Action<WardenPartType, Vector3> OnHitBlocked;

        // ══════════════════════════════════════════════════════
        // 봉인 이벤트 — 이후 BossSealManager 단계에서 본격 사용
        // ══════════════════════════════════════════════════════

        public event Action<WardenPartType, float, float> OnSealGaugeChanged;
        public event Action<WardenPartType> OnSealReady;
        public event Action<WardenPartType> OnSealExecuted;
        public event Action<WardenPartType> OnSealReleased;
        public event Action OnAllPartsSealed;
        public event Action<float, float> OnCoreSealGaugeChanged;
        public event Action OnCoreSealCompleted;

        // ══════════════════════════════════════════════════════
        // VFX / Sound / Camera 요청 이벤트 — 이후 단계에서 구독 이동
        // ══════════════════════════════════════════════════════

        public event Action<Vector3> OnRequestShockwave;
        public event Action<float, float> OnRequestCameraShake;
        public event Action<float, float> OnRequestCameraZoom;
        public event Action<string> OnRequestSound;

        // ══════════════════════════════════════════════════════
        // Raise API — 상태
        // ══════════════════════════════════════════════════════

        public void RaiseBossStateChanged(BossRuntimeState state)
            => OnBossStateChanged?.Invoke(state);

        public void RaiseDilPhaseEnter()
            => OnDilPhaseEnter?.Invoke();

        public void RaiseDilPhaseExit()
            => OnDilPhaseExit?.Invoke();

        public void RaiseFinalSealReady()
            => OnFinalSealReady?.Invoke();

        public void RaisePhaseChanged(int newPhase)
            => OnPhaseChanged?.Invoke(newPhase);

        public void RaiseDead()
            => OnDead?.Invoke();

        // ══════════════════════════════════════════════════════
        // Raise API — 공격
        // ══════════════════════════════════════════════════════

        public void RaiseAttackStarted(BossPatternBase pattern)
            => OnAttackStarted?.Invoke(pattern);

        public void RaiseAttackWarning(BossPatternBase pattern)
            => OnAttackWarning?.Invoke(pattern);

        public void RaiseAttackActive(BossPatternBase pattern)
            => OnAttackActive?.Invoke(pattern);

        public void RaiseAttackRecovery(BossPatternBase pattern)
            => OnAttackRecovery?.Invoke(pattern);

        public void RaiseAttackEnded(BossPatternBase pattern)
            => OnAttackEnded?.Invoke(pattern);

        public void RaiseAttackInterrupted(BossPatternBase pattern)
            => OnAttackInterrupted?.Invoke(pattern);

        // ══════════════════════════════════════════════════════
        // Raise API — 피격
        // ══════════════════════════════════════════════════════

        public void RaisePartHit(WardenPartType partType, Vector3 hitPoint)
            => OnPartHit?.Invoke(partType, hitPoint);

        public void RaiseWeakPointHit(WardenPartType partType, Vector3 hitPoint)
            => OnWeakPointHit?.Invoke(partType, hitPoint);

        public void RaiseHitBlocked(WardenPartType partType, Vector3 hitPoint)
            => OnHitBlocked?.Invoke(partType, hitPoint);

        // ══════════════════════════════════════════════════════
        // Raise API — 봉인
        // ══════════════════════════════════════════════════════

        public void RaiseSealGaugeChanged(WardenPartType partType, float current, float max)
            => OnSealGaugeChanged?.Invoke(partType, current, max);

        public void RaiseSealReady(WardenPartType partType)
            => OnSealReady?.Invoke(partType);

        public void RaiseSealExecuted(WardenPartType partType)
            => OnSealExecuted?.Invoke(partType);

        public void RaiseSealReleased(WardenPartType partType)
            => OnSealReleased?.Invoke(partType);

        public void RaiseAllPartsSealed()
            => OnAllPartsSealed?.Invoke();

        public void RaiseCoreSealGaugeChanged(float current, float max)
            => OnCoreSealGaugeChanged?.Invoke(current, max);

        public void RaiseCoreSealCompleted()
            => OnCoreSealCompleted?.Invoke();

        // ══════════════════════════════════════════════════════
        // Raise API — 연출 요청
        // ══════════════════════════════════════════════════════

        public void RaiseRequestShockwave(Vector3 origin)
            => OnRequestShockwave?.Invoke(origin);

        public void RaiseRequestCameraShake(float strength, float duration)
            => OnRequestCameraShake?.Invoke(strength, duration);

        public void RaiseRequestCameraZoom(float targetSize, float duration)
            => OnRequestCameraZoom?.Invoke(targetSize, duration);

        public void RaiseRequestSound(string soundKey)
            => OnRequestSound?.Invoke(soundKey);
    }
}
