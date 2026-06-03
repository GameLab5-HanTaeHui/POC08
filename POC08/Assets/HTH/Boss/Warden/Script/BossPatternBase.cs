// ============================================================
// BossPatternBase.cs  v1.2
// Boss_Warden 패턴 추상 베이스 클래스
//
// [v1.2 수정]
//   🔴 디버그 로그 추가: Warning/Active/Recovery 각 단계 진입/종료 + isInterrupted 상태 출력
//       → 어떤 패턴이 어느 단계에서 정지하는지 정확히 추적 가능
//
// [v1.1 수정]
//   🔴 버그2: ExecuteRecovery OnPatternEnd 중복 발행 방지
//   🟡 경고2: SetActive=false 코루틴 중단 주의사항 주석 추가
//
// [레이어 구조 — SEAL 프로젝트 3분리 방식]
//   Player|Enemy         : 보스 본체/부위 피격 감지 (플레이어 공격 → Enemy 레이어 감지)
//   Player|EnemyAttack   : 보스 패턴 공격 발생원 (패턴 OverlapXX 의 소속 레이어)
//   Player|EnemyAttackHitBox : 플레이어 피격 판정 (플레이어 HurtBox 레이어)
//                              ← 패턴 스크립트의 _playerLayer 는 이 레이어 선택
//
// [POC07 참고]
//   TestBossPatternBase.cs (v1.0) 구조를 기반으로
//   탑뷰 시스템에 맞게 재설계.
//
// [POC07과의 차이]
//   POC07: _sealableArm (봉인 투사체 감지) 내장
//          → POC08 에서 제거 (투사체 시스템 없음)
//   POC07: BossPatternSealResult 반환 구조
//          → POC08 에서 단순화 — OnPatternGroggy 이벤트만 사용
//   POC07: WaitScaled() 내부에서 매 프레임 봉인 감지 체크
//          → POC08 에서 제거 (봉인도는 공격 적중으로만 누적)
//   POC08 추가: _linkedArmPart — 연결된 팔 부위 (봉인 완료 시 패턴 비활성)
//   POC08 추가: IsAvailable — 연결 팔이 봉인됐으면 패턴 실행 불가
//   POC08 추가: _isPhase2Only — 2페이즈 전용 패턴 여부
//
// [패턴 3단계 생애주기]
//   Warning  : 예고 구간 — 공격 예고 범위 표시, DOTween 준비 모션
//   Active   : 시전 구간 — 실제 히트박스 판정 (OverlapXX)
//   Recovery : 후딜 구간 — 취약 구간, OnPatternGroggy 발행 가능
//
// [그로기 유도]
//   _triggerGroggyOnRecovery = true 시 Recovery 완료 후 OnPatternGroggy 발행
//   → BossWardenAI.HandlePatternGroggy() → BossWardenCore.EnterGroggy()
//
// [namespace]
//   namespace : SEAL
// ============================================================

using System;
using System.Collections;
using UnityEngine;

namespace SEAL
{
    /// <summary>
    /// Boss_Warden 패턴 추상 베이스 클래스. (v1.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [하위 클래스 구현 필수]
    ///   OnWarning()  : 예고 범위 표시 + 준비 모션
    ///   OnActive()   : 히트박스 판정 (OverlapXX)
    ///   OnRecovery() : 후딜레이 처리
    ///
    /// [외부 사용 예시 — BossWardenAI]
    ///   yield return StartCoroutine(pattern.ExecuteWarning());
    ///   yield return StartCoroutine(pattern.ExecuteActive());
    ///   yield return StartCoroutine(pattern.ExecuteRecovery());
    ///   pattern.Interrupt();
    ///
    /// [패턴 선택 조건 체크]
    ///   if (pattern.CanExecute && pattern.IsAvailable) { ... }
    ///
    /// [MonoBehaviour 코루틴 주의사항]
    ///   이 클래스는 MonoBehaviour 를 상속한다.
    ///   StartCoroutine() 호출 주체가 이 컴포넌트 자신이므로
    ///   이 오브젝트가 SetActive(false) 되면 코루틴이 즉시 중단된다.
    ///   → Patterns 오브젝트는 항상 SetActive(true) 유지 필요.
    ///   → 패턴 비활성은 CanExecute / IsAvailable 플래그로만 처리한다.
    ///   → 오브젝트를 비활성화하지 않는다.
    /// ────────────────────────────────────────────────────
    /// </summary>
    public abstract class BossPatternBase : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        // Inspector — 기본 설정
        // ══════════════════════════════════════════════════════

        [Header("── 패턴 기본 설정 ──────────────────────")]

        /// <summary>
        /// 패턴 쿨타임 (초).
        /// Recovery 완료 후 이 시간 동안 CanExecute = false.
        /// </summary>
        [Tooltip("패턴 쿨타임 (초). Recovery 완료 후 재사용 대기시간.")]
        [Min(0f)]
        [SerializeField] protected float _cooldown = 5.0f;

        /// <summary>
        /// Warning 구간 지속 시간 (초).
        /// OnWarning() 내부에서 WaitForPattern(_warningDuration) 으로 사용.
        /// </summary>
        [Tooltip("Warning 구간 지속 시간 (초). 권장: 0.6~1.2.")]
        [Min(0f)]
        [SerializeField] protected float _warningDuration = 1.0f;

        /// <summary>
        /// Recovery 구간 지속 시간 (초).
        /// OnRecovery() 내부에서 WaitForPattern(_recoveryDuration) 으로 사용.
        /// </summary>
        [Tooltip("Recovery 구간 지속 시간 (초). 권장: 0.5~1.2.")]
        [Min(0f)]
        [SerializeField] protected float _recoveryDuration = 0.8f;

        /// <summary>
        /// Recovery 완료 후 그로기를 유도할지 여부.
        /// true → Recovery 완료 시 OnPatternGroggy 발행.
        /// false → 그로기 유도 없음 (단순 패턴).
        /// </summary>
        [Tooltip("Recovery 완료 시 그로기 유도 여부.")]
        [SerializeField] protected bool _triggerGroggyOnRecovery = false;

        [Header("── 부위 연결 ──────────────────────")]

        /// <summary>
        /// 이 패턴과 연결된 팔 부위 참조.
        /// 연결된 팔이 봉인(IsSealed == true) 되면 IsAvailable = false.
        /// null 이면 독립 패턴 (부위 연결 없음 — RageCharge 등).
        /// </summary>
        [Tooltip("연결된 팔 부위. 봉인 완료 시 이 패턴 비활성. null=독립 패턴.")]
        [SerializeField] protected BossWardenArmPart _linkedArmPart;

        [Header("── 페이즈 설정 ──────────────────────")]

        /// <summary>
        /// 2페이즈 전용 패턴 여부.
        /// true → 1페이즈에서는 CanExecute 강제 false.
        /// BossWardenAI 에서 2페이즈 진입 시 _isPhase2Unlocked = true 로 활성화.
        /// </summary>
        [Tooltip("2페이즈 전용 패턴 여부. true = 1페이즈에서 실행 불가.")]
        [SerializeField] protected bool _isPhase2Only = false;

        // ══════════════════════════════════════════════════════
        // 내부 상태
        // ══════════════════════════════════════════════════════

        /// <summary> 쿨타임 잔여 시간. </summary>
        private float _cooldownTimer;

        /// <summary> 현재 실행 중 여부. </summary>
        protected bool _isExecuting;

        /// <summary>
        /// 강제 중단 플래그.
        /// Interrupt() 호출 시 true.
        /// WaitForPattern() 내부에서 매 프레임 체크 → 코루틴 자연 종료.
        /// </summary>
        protected bool _isInterrupted;

        /// <summary>
        /// 2페이즈 활성화 여부.
        /// BossWardenAI 에서 OnPhaseChanged(2) 수신 시 UnlockPhase2() 호출.
        /// </summary>
        private bool _isPhase2Unlocked;

        // ══════════════════════════════════════════════════════
        // 이벤트
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 패턴 시작 시 발행.
        /// BossWardenAI 에서 상태 전환에 사용.
        /// </summary>
        public event Action<BossPatternBase> OnPatternStart;

        /// <summary>
        /// 패턴 종료 시 발행 (정상 종료 + Interrupt).
        /// BossWardenAI 에서 _currentPattern 정리에 사용.
        /// </summary>
        public event Action<BossPatternBase> OnPatternEnd;

        /// <summary>
        /// 그로기 유도 조건 충족 시 발행.
        /// BossWardenAI 가 구독 → BossWardenCore.EnterGroggy() 호출.
        ///
        /// [발행 시점]
        ///   _triggerGroggyOnRecovery = true → Recovery 완료 직후
        ///   하위 클래스에서 특정 조건에 TriggerGroggy() 직접 호출 가능.
        /// </summary>
        public event Action OnPatternGroggy;

        // ══════════════════════════════════════════════════════
        // 프로퍼티
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 실행 가능 여부.
        /// 쿨타임 완료 + 현재 실행 중 아님 + 2페이즈 조건 충족.
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
        /// 연결된 팔 부위가 봉인되지 않아 실행 가능한지 여부.
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

        /// <summary>
        /// 현재 Warning Duration (외부 참조용).
        /// </summary>
        public float WarningDuration => _warningDuration;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Update()
        {
            // 쿨타임 감소
            if (_cooldownTimer > 0f)
                _cooldownTimer -= Time.deltaTime;
        }

        // ══════════════════════════════════════════════════════
        // 외부 실행 API — BossWardenAI 에서 호출
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Warning 구간 실행 코루틴.
        /// BossWardenAI.ExecutePattern() 에서 yield return.
        /// 내부에서 OnWarning() 호출.
        /// </summary>
        public IEnumerator ExecuteWarning()
        {
            _isExecuting = true;
            _isInterrupted = false;

            Debug.Log($"[{GetType().Name}] Warning 진입 | isInterrupted:{_isInterrupted}");
            OnPatternStart?.Invoke(this);
            yield return StartCoroutine(OnWarning());
            Debug.Log($"[{GetType().Name}] Warning OnWarning() 반환 | isInterrupted:{_isInterrupted}");
        }

        /// <summary>
        /// Active 구간 실행 코루틴.
        /// </summary>
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
        ///
        /// [OnPatternEnd / 쿨타임 / _isExecuting 중복 처리 방지]
        ///   Interrupt() 호출 시:
        ///     → _isExecuting = false, _cooldownTimer = _cooldown, OnPatternEnd 발행 완료
        ///   이후 OnRecovery() 내부 WaitForPattern 이 중단되어 코루틴이 빠져나오면
        ///   아래 코드가 실행되는데, _isInterrupted 체크로 중복 처리를 방지.
        ///
        /// [MonoBehaviour 비활성 주의]
        ///   이 패턴 오브젝트가 SetActive(false) 되면 코루틴이 즉시 중단됨.
        ///   → 코루틴 이후 정리 코드가 실행되지 않아 _isExecuting 이 true 로 남을 수 있음.
        ///   → Interrupt() 를 먼저 호출한 뒤 SetActive(false) 하는 것을 권장.
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

            // _isInterrupted = true 면 Interrupt() 에서 이미 처리됨 → 중복 방지
            if (_isInterrupted) yield break;

            // 정상 종료 처리
            _cooldownTimer = _cooldown;
            _isExecuting = false;

            OnPatternEnd?.Invoke(this);
            Debug.Log($"[{GetType().Name}] Recovery 정상 완료 | triggerGroggy:{_triggerGroggyOnRecovery}");

            // 그로기 유도 (정상 종료 + 설정 true 일 때만)
            if (_triggerGroggyOnRecovery)
                TriggerGroggy();
        }

        /// <summary>
        /// 강제 중단.
        /// BossWardenAI 에서 그로기/딜페이즈 진입 시 호출.
        /// _isInterrupted = true → WaitForPattern() 다음 프레임에 자연 종료.
        ///
        /// [이중 실행 가드]
        ///   이미 중단 중이면 무시.
        ///
        /// [호출 순서 원칙]
        ///   반드시 Interrupt() 를 먼저 호출한 뒤 SetActive(false) 할 것.
        ///   SetActive(false) 를 먼저 하면 MonoBehaviour 의 코루틴이 즉시 중단되어
        ///   이 함수 내부의 정리 코드(OnPatternEnd 발행 등)가 실행되지 않음.
        ///   → BossWardenAI 의 _currentPattern 이 null 로 정리되지 않아 참조 잔존 버그 발생 가능.
        ///
        /// [WaitForPattern 중단 타이밍]
        ///   _isInterrupted = true 설정 후 WaitForPattern 이 다음 yield return null 에서
        ///   체크하여 종료 → 1프레임 지연 발생.
        ///   즉각 중단이 필요하면 StopCoroutine 을 BossWardenAI 에서 직접 호출.
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
        /// _isPhase2Only = true 인 패턴의 CanExecute 를 허용.
        /// </summary>
        public void UnlockPhase2()
        {
            _isPhase2Unlocked = true;
            Debug.Log($"[BossPatternBase] {GetType().Name} 2페이즈 활성화");
        }

        // ══════════════════════════════════════════════════════
        // 하위 클래스 구현 필수
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Warning 구간 구현.
        /// 예고 범위 표시 + DOTween 준비 모션.
        /// WaitForPattern(_warningDuration) 으로 대기 권장.
        /// </summary>
        protected abstract IEnumerator OnWarning();

        /// <summary>
        /// Active 구간 구현.
        /// 실제 히트박스 판정 (OverlapXX).
        /// </summary>
        protected abstract IEnumerator OnActive();

        /// <summary>
        /// Recovery 구간 구현.
        /// WaitForPattern(_recoveryDuration) 으로 대기 권장.
        /// </summary>
        protected abstract IEnumerator OnRecovery();

        // ══════════════════════════════════════════════════════
        // 보조 메서드 — 하위 클래스에서 사용
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 중단 체크 포함 대기.
        /// 하위 클래스에서 yield return WaitForPattern(시간) 으로 사용.
        ///
        /// [POC07 WaitScaled 와의 차이]
        ///   POC07: 봉인 투사체 감지 로직 포함
        ///   POC08: 중단 체크만 수행 (투사체 시스템 없음)
        /// </summary>
        /// <param name="duration">대기 시간 (초).</param>
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

        /// <summary>
        /// 그로기 유도 이벤트를 수동으로 발행.
        /// 하위 클래스에서 특정 조건(돌진 벽 충돌 등)에 직접 호출.
        /// </summary>
        protected void TriggerGroggy()
        {
            OnPatternGroggy?.Invoke();
        }
    }
}