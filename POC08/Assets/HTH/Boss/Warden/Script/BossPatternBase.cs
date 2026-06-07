// ============================================================
// BossPatternBase.cs
// Boss_Warden 패턴 추상 기반 클래스
//
// [수정 내용]
//   GetArmGaugeColor(Transform) 헬퍼 추가
//   → 패턴 Recovery / Interrupt 에서 팔 색상 복귀 시 봉인도 기반 색상 반환
//   → 모든 하위 패턴에서 상속 사용
//
// [namespace] SEAL
// ============================================================

using System;
using System.Collections;
using UnityEngine;

namespace SEAL
{
    /// <summary>
    /// Boss_Warden 패턴 추상 기반 클래스.
    ///
    /// ────────────────────────────────────────────────────
    /// [패턴 생애주기]
    ///   ExecuteWarning() → ExecuteActive() → ExecuteRecovery()
    ///   각 구간은 BossWardenAI.ExecutePattern() 코루틴에서 순서대로 호출.
    ///
    /// [중단 처리]
    ///   Interrupt() → _isInterrupted = true
    ///   → WaitForPattern() 에서 매 프레임 체크 → 코루틴 자연 종료
    ///
    /// [패턴 비활성 원칙]
    ///   Patterns 오브젝트는 항상 SetActive(true) 유지.
    ///   패턴 비활성은 CanExecute / IsAvailable 플래그로만 처리.
    ///
    /// [GetArmGaugeColor() 헬퍼]
    ///   Recovery / Interrupt 에서 팔 색상 복귀 시
    ///   _armOriginColor 대신 이 헬퍼를 사용.
    ///   봉인도 비율 색상 / 봉인 완료 시 colorSealed 자동 반환.
    /// ────────────────────────────────────────────────────
    /// </summary>
    public abstract class BossPatternBase : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        // Inspector — 기본 설정
        // ══════════════════════════════════════════════════════

        [Header("── 패턴 기본 설정 ──────────────────────")]

        /// <summary>패턴 쿨타임 (초). Recovery 완료 후 재사용 대기시간.</summary>
        [Tooltip("패턴 쿨타임 (초). Recovery 완료 후 재사용 대기시간.")]
        [Min(0f)]
        [SerializeField] protected float _cooldown = 5.0f;

        /// <summary>Warning 구간 지속 시간 (초).</summary>
        [Tooltip("Warning 구간 지속 시간 (초). 권장: 0.6~1.2.")]
        [Min(0f)]
        [SerializeField] protected float _warningDuration = 1.0f;

        /// <summary>Recovery 구간 지속 시간 (초).</summary>
        [Tooltip("Recovery 구간 지속 시간 (초). 권장: 0.5~1.2.")]
        [Min(0f)]
        [SerializeField] protected float _recoveryDuration = 0.8f;

        /// <summary>
        /// Recovery 완료 후 그로기를 유도할지 여부.
        /// true → Recovery 완료 시 OnPatternGroggy 발행.
        /// </summary>
        [Tooltip("Recovery 완료 시 그로기 유도 여부.")]
        [SerializeField] protected bool _triggerGroggyOnRecovery = false;

        [Header("── 부위 연결 ──────────────────────")]

        /// <summary>
        /// 이 패턴과 연결된 팔 부위.
        /// IsSealed == true 이면 IsAvailable = false.
        /// null 이면 독립 패턴.
        /// </summary>
        [Tooltip("연결된 팔 부위. 봉인 완료 시 이 패턴 비활성. null=독립 패턴.")]
        [SerializeField] protected BossWardenArmPart _linkedArmPart;

        [Header("── 페이즈 설정 ──────────────────────")]

        /// <summary>2페이즈 전용 패턴 여부. true = 1페이즈에서 실행 불가.</summary>
        [Tooltip("2페이즈 전용 패턴 여부. true = 1페이즈에서 실행 불가.")]
        [SerializeField] protected bool _isPhase2Only = false;

        // ══════════════════════════════════════════════════════
        // 내부 상태
        // ══════════════════════════════════════════════════════

        /// <summary>쿨타임 잔여 시간.</summary>
        private float _cooldownTimer;

        /// <summary>현재 실행 중 여부.</summary>
        protected bool _isExecuting;

        /// <summary>
        /// 강제 중단 플래그.
        /// Interrupt() 호출 시 true.
        /// WaitForPattern() 내부에서 매 프레임 체크 → 코루틴 자연 종료.
        /// </summary>
        protected bool _isInterrupted;

        /// <summary>2페이즈 활성화 여부. UnlockPhase2() 호출 시 true.</summary>
        private bool _isPhase2Unlocked;

        // ══════════════════════════════════════════════════════
        // 이벤트
        // ══════════════════════════════════════════════════════

        /// <summary>패턴 시작 시 발행. BossWardenFeedback 색상 연출용.</summary>
        public event Action<BossPatternBase> OnPatternStart;

        /// <summary>패턴 정상 완료 또는 Interrupt 시 발행. BossWardenAI Idle 복귀용.</summary>
        public event Action<BossPatternBase> OnPatternEnd;

        /// <summary>
        /// Recovery 완료 후 그로기 유도 시 발행.
        /// _triggerGroggyOnRecovery = true 인 패턴에서만 발행.
        /// </summary>
        public event Action OnPatternGroggy;

        // ══════════════════════════════════════════════════════
        // 프로퍼티
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 실행 가능 여부.
        /// 쿨타임 중 or 이미 실행 중 or 2페이즈 전용(미활성) 이면 false.
        /// </summary>
        public bool CanExecute
        {
            get
            {
                if (_isExecuting) return false;
                if (_cooldownTimer > 0f) return false;
                if (_isPhase2Only && !_isPhase2Unlocked) return false;
                return true;
            }
        }

        /// <summary>
        /// 사용 가능 여부.
        /// _linkedArmPart == null → 독립 패턴 → 항상 true.
        /// _linkedArmPart.IsSealed == true → 팔 봉인됨 → false.
        /// </summary>
        public bool IsAvailable
        {
            get
            {
                if (_linkedArmPart == null) return true;
                return !_linkedArmPart.IsSealed;
            }
        }

        /// <summary>현재 Warning Duration (외부 참조용).</summary>
        public float WarningDuration => _warningDuration;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Update()
        {
            if (_cooldownTimer > 0f)
                _cooldownTimer -= Time.deltaTime;
        }

        // ══════════════════════════════════════════════════════
        // 외부 실행 API — BossWardenAI 에서 호출
        // ══════════════════════════════════════════════════════

        /// <summary>Warning 구간 실행 코루틴.</summary>
        public IEnumerator ExecuteWarning()
        {
            _isExecuting = true;
            _isInterrupted = false;

            Debug.Log($"[{GetType().Name}] Warning 진입 | isInterrupted:{_isInterrupted}");
            OnPatternStart?.Invoke(this);
            yield return StartCoroutine(OnWarning());
            Debug.Log($"[{GetType().Name}] Warning OnWarning() 반환 | isInterrupted:{_isInterrupted}");
        }

        /// <summary>Active 구간 실행 코루틴.</summary>
        public IEnumerator ExecuteActive()
        {
            if (_isInterrupted)
            {
                Debug.Log($"[{GetType().Name}] Active 진입 전 isInterrupted=true → 스킵");
                yield break;
            }

            Debug.Log($"[{GetType().Name}] Active 진입");
            yield return StartCoroutine(OnActive());
            Debug.Log($"[{GetType().Name}] Active OnActive() 반환 | isInterrupted:{_isInterrupted}");
        }

        /// <summary>
        /// Recovery 구간 실행 코루틴.
        /// 정상 완료 시 그로기 유도 여부에 따라 OnPatternGroggy 발행.
        /// </summary>
        public IEnumerator ExecuteRecovery()
        {
            if (_isInterrupted)
            {
                Debug.Log($"[{GetType().Name}] Recovery 진입 전 isInterrupted=true → 스킵");
                yield break;
            }

            Debug.Log($"[{GetType().Name}] Recovery 진입");
            yield return StartCoroutine(OnRecovery());
            Debug.Log($"[{GetType().Name}] Recovery OnRecovery() 반환 | isInterrupted:{_isInterrupted}");

            if (_isInterrupted) yield break;

            _cooldownTimer = _cooldown;
            _isExecuting = false;

            OnPatternEnd?.Invoke(this);
            Debug.Log($"[{GetType().Name}] Recovery 정상 완료 | triggerGroggy:{_triggerGroggyOnRecovery}");

            if (_triggerGroggyOnRecovery)
                TriggerGroggy();
        }

        /// <summary>
        /// 강제 중단.
        /// BossWardenAI 에서 DilPhase 진입 시 호출.
        /// </summary>
        public virtual void Interrupt()
        {
            if (_isInterrupted) return;

            _isInterrupted = true;
            _isExecuting = false;
            _cooldownTimer = _cooldown;

            OnPatternEnd?.Invoke(this);
            Debug.Log($"[BossPatternBase] {GetType().Name} 강제 중단");
        }

        /// <summary>
        /// 2페이즈 패턴 활성화.
        /// BossWardenAI.OnPhaseChanged(2) 수신 시 호출.
        /// </summary>
        public void UnlockPhase2()
        {
            _isPhase2Unlocked = true;
            Debug.Log($"[BossPatternBase] {GetType().Name} 2페이즈 활성화");
        }

        // ══════════════════════════════════════════════════════
        // 하위 클래스 구현 필수
        // ══════════════════════════════════════════════════════

        /// <summary>Warning 구간 구현. 예고 범위 표시 + DOTween 준비 모션.</summary>
        protected abstract IEnumerator OnWarning();

        /// <summary>Active 구간 구현. 실제 히트박스 판정.</summary>
        protected abstract IEnumerator OnActive();

        /// <summary>Recovery 구간 구현. WaitForPattern(_recoveryDuration) 으로 대기.</summary>
        protected abstract IEnumerator OnRecovery();

        // ══════════════════════════════════════════════════════
        // 보조 메서드 — 하위 클래스에서 사용
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 중단 체크 포함 대기.
        /// 하위 클래스에서 yield return WaitForPattern(시간) 으로 사용.
        /// </summary>
        protected IEnumerator WaitForPattern(float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (_isInterrupted) yield break;
                elapsed += Time.deltaTime;
                yield return null;
            }
        }

        /// <summary>그로기 유도 이벤트 발행.</summary>
        protected void TriggerGroggy()
        {
            OnPatternGroggy?.Invoke();
        }

        // ══════════════════════════════════════════════════════
        // 팔 색상 복귀 헬퍼 — 봉인도 기반
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 팔 Transform 의 현재 봉인도에 해당하는 색상 반환.
        ///
        /// ────────────────────────────────────────────────────
        /// [사용 목적]
        ///   패턴 Recovery / Interrupt 에서 팔 색상 복귀 시
        ///   _armOriginColor(기본색) 대신 이 값 사용.
        ///
        ///   봉인도 0%      → colorBase  (시작 색상)
        ///   봉인도 1~99%   → 비율 보간색 (봉인도 색상)
        ///   봉인 집행 완료  → colorSealed (파랑 고정)
        ///
        /// [fallback]
        ///   armTransform null or SealableComponent 없음 → Color.white
        ///
        /// [사용 예시]
        ///   // Recovery 색상 복귀
        ///   _armRColorTween = _armRRenderer
        ///       .DOColor(GetArmGaugeColor(_armRTransform), duration)
        ///       .SetUpdate(true);
        ///
        ///   // Interrupt 즉시 복귀
        ///   _armRRenderer
        ///       .DOColor(GetArmGaugeColor(_armRTransform), 0.1f)
        ///       .SetUpdate(true);
        /// ────────────────────────────────────────────────────
        /// </summary>
        /// <param name="armTransform">팔 Transform. SealableComponent 탐색 기준.</param>
        /// <returns>현재 봉인도에 해당하는 색상.</returns>
        protected Color GetArmGaugeColor(Transform armTransform)
        {
            if (armTransform == null) return Color.white;

            var sealable = armTransform.GetComponent<SealableComponent>();
            if (sealable == null) return Color.white;

            // SealableComponent.GetCurrentGaugeColor() 가
            // IsSealed 여부 + colorSealed / 비율 보간 모두 처리
            return sealable.GetCurrentGaugeColor();
        }
    }
}