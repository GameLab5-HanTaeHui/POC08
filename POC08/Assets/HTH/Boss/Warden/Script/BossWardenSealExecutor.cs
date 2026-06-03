// ============================================================
// BossWardenSealExecutor.cs  v1.1
// Boss_Warden S키 봉인 집행 / 코어 해제 / 최종 봉인 처리 컴포넌트
//
// [v1.1 버그 수정]
//   🔴 버그1: SubscribeArmEvents() 람다 이벤트 구독 → 해제 불가 + 중복 호출
//       기존: arm.SealGauge.OnSealReady += () => HandleArmSealReady(arm);
//             → 람다는 매번 새 객체 생성 → -= 로 해제 불가
//             → SubscribeArmEvents 2회 이상 호출 시 중복 구독 → 핸들러 2회 이상 실행
//       수정: 멤버 함수 캐싱 방식으로 교체
//             _onArmLSealReady / _onArmRSealReady 필드에 Action 저장
//             → -= 로 정상 해제 가능
//
//   🔴 버그2: Update() 에서 _holdTimer 리셋 조건 역전
//       기존: if (_isExecuting) _holdTimer = 0f;
//             → 집행 중에 타이머를 초기화하는 역전 버그
//       수정: 해당 블록 제거 — _holdTimer 는 DetectSealInput 코루틴에서만 관리
//
// [POC07 참고]
//   TestBossExecution.cs (v1.1) 구조 계승.
//   BossExecutionHandler.cs 의 부위 탐색 / 이동 방식 참고.
//
// [POC07 TestBossExecution 과의 차이]
//   POC07: A키 홀드 / 플레이어가 부위로 자동 이동
//   POC08: S키 홀드 / 플레이어가 직접 걸어가서 범위 내 접근
//          자동 이동 없음 — 플레이어가 직접 위치해야 함
//
//   POC07: 그로기 중 팔 처형만 존재 (이진 상태 ReLock)
//   POC08: 3단계 집행 구조
//          ① 팔 부위 봉인 집행 (봉인도 100% 도달 후)
//          ② 코어 해제 (양팔 봉인 완료 → 그로기 → 코어 활성)
//          ③ 최종 봉인 (코어 봉인도 100% 도달)
//
//   POC07: 처형 완료 후 재발동 방지 (_mustReleaseKey)
//   POC08: 동일 — S키 뗀 후 재누름 확인 후 재감지 허용
//
// [집행 가능 조건]
//   부위 봉인 집행: IsSealReady + 플레이어가 범위 내 + S키 홀드
//   코어 해제:     그로기 상태 + 코어 활성 + 플레이어가 범위 내 + S키 홀드
//   최종 봉인:     딜 페이즈 + 코어 봉인도 100% + 플레이어가 범위 내 + S키 홀드
//
// [슬로우 모션]
//   코어 해제 홀드 중:  Time.timeScale → dilPhaseSlowTimeScale (0.3)
//   최종 봉인 홀드 중:  Time.timeScale → finalSealSlowTimeScale (0.1)
//   완료 or 취소 시:    Time.timeScale → 1.0 복구
//   SetUpdate(true): 슬로우 중에도 DOTween 연출 정상 동작
//
// [namespace]
//   namespace : SEAL
// ============================================================

using System;
using System.Collections;
using UnityEngine;
using DG.Tweening;

namespace SEAL
{
    /// <summary>
    /// Boss_Warden S키 봉인 집행 / 코어 해제 / 최종 봉인 처리 컴포넌트. (v1.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [집행 흐름 1 — 부위 봉인 집행]
    ///   SealGaugeComponent.OnSealReady 발행
    ///     → _sealReadyParts 에 등록 + BossWardenAttackRange.ShowSealRange()
    ///   플레이어가 부위 범위 내 진입 + S키 홀드 시작
    ///     → BlockPlayerInput()
    ///     → 홀드 진행 게이지 표시
    ///   홀드 완료 (sealExecutionHoldTime)
    ///     → SealGaugeComponent.ExecuteSeal()
    ///     → OnPartSealed 발행 → BossWardenCore.CheckGroggyCondition()
    ///     → UnblockPlayerInput()
    ///
    /// [집행 흐름 2 — 코어 해제]
    ///   BossWardenCore.OnGroggyEnter 수신
    ///     → _isCoreUnlockActive = true
    ///     → ShowCoreRange()
    ///   플레이어가 코어 범위 내 + S키 홀드
    ///     → Time.timeScale 슬로우 시작
    ///   홀드 완료 (coreUnlockHoldTime)
    ///     → Time.timeScale 복구
    ///     → OnCoreUnlocked 발행 → BossWardenCore.EnterDilPhase()
    ///
    /// [집행 흐름 3 — 최종 봉인]
    ///   BossWardenCore.OnFinalSealReady 수신
    ///     → _isFinalSealActive = true
    ///   플레이어가 코어 범위 내 + S키 홀드
    ///     → 강한 슬로우 (finalSealSlowTimeScale)
    ///   홀드 완료 (finalSealHoldTime)
    ///     → Time.timeScale 복구
    ///     → OnFinalSealCompleted 발행 → BossWardenCore.Die()
    /// ────────────────────────────────────────────────────
    /// </summary>
    public class BossWardenSealExecutor : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── DataSO (필수) ──────────────────────")]

        [Tooltip("BossWardenDataSO. 필수 연결.")]
        [SerializeField] private BossWardenDataSO _data;

        [Header("── 부위 연결 (필수) ──────────────────────")]

        [Tooltip("왼팔 BossWardenArmPart. 필수 연결.")]
        [SerializeField] private BossWardenArmPart _armL;

        [Tooltip("오른팔 BossWardenArmPart. 필수 연결.")]
        [SerializeField] private BossWardenArmPart _armR;

        [Header("── 코어 연결 (필수) ──────────────────────")]

        [Tooltip("코어 GameObject. 기본 SetActive=false. 필수 연결.")]
        [SerializeField] private GameObject _coreObject;

        [Tooltip("코어 BossWardenCoreSealGauge. 필수 연결.")]
        [SerializeField] private BossWardenCoreSealGauge _coreSealGauge;

        [Header("── 예고 범위 표시 ──────────────────────")]

        [Tooltip("BossWardenAttackRange. 미연결 시 GetComponent 탐색.")]
        [SerializeField] private BossWardenAttackRange _attackRange;

        // ══════════════════════════════════════════════════════
        // 컴포넌트 참조
        // ══════════════════════════════════════════════════════

        private BossWardenCore _core;
        private PlayerInputHandler _input;
        private Transform _playerTransform;
        private Rigidbody2D _playerRigid2D;

        // ══════════════════════════════════════════════════════
        // 내부 상태
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 봉인 집행 가능 상태인 부위 목록.
        /// OnSealReady 수신 시 추가, ExecuteSeal / ForceRelease 시 제거.
        /// </summary>
        private System.Collections.Generic.List<BossWardenArmPart> _sealReadyParts
            = new System.Collections.Generic.List<BossWardenArmPart>();

        /// <summary> 코어 해제 감지 활성 여부. </summary>
        private bool _isCoreUnlockActive;

        /// <summary> 최종 봉인 감지 활성 여부. </summary>
        private bool _isFinalSealActive;

        /// <summary> 현재 집행 실행 중 여부 (중복 방지). </summary>
        private bool _isExecuting;

        /// <summary>
        /// S키 재누름 확인 플래그.
        /// 집행 완료 후 S키를 한 번 뗀 것을 확인해야 재집행 허용.
        /// </summary>
        private bool _mustReleaseKey;

        /// <summary> S키 홀드 누적 시간. </summary>
        private float _holdTimer;

        /// <summary> 집행 쿨다운 타이머. </summary>
        private float _cooldownTimer;

        private Coroutine _detectCoroutine;

        // ══════════════════════════════════════════════════════
        // 람다 대체 이벤트 핸들러 캐시
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 왼팔 SealReady 이벤트 핸들러 캐시.
        ///
        /// [버그1 수정]
        ///   람다 (() => HandleArmSealReady(_armL)) 는 매번 새 객체를 생성하여
        ///   -= 로 해제가 불가능하고 중복 구독 시 핸들러가 여러 번 호출됨.
        ///   Action 필드에 저장하면 동일 참조로 += / -= 가 정상 동작.
        /// </summary>
        private Action _onArmLSealReady;
        private Action _onArmLReleased;
        private Action _onArmRSealReady;
        private Action _onArmRReleased;

        // ══════════════════════════════════════════════════════
        // 이벤트
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 부위 봉인 집행 완료 시 발행.
        /// 파라미터: 봉인된 팔 타입.
        /// BossWardenCore 가 구독 → CheckGroggyCondition().
        /// </summary>
        public event Action<WardenPartType> OnPartSealed;

        /// <summary>
        /// 코어 해제 완료 시 발행.
        /// BossWardenCore 가 구독 → EnterDilPhase().
        /// </summary>
        public event Action OnCoreUnlocked;

        /// <summary>
        /// 최종 봉인 완료 시 발행.
        /// BossWardenCore 가 구독 → Die().
        /// </summary>
        public event Action OnFinalSealCompleted;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            _core = GetComponent<BossWardenCore>();

            if (_attackRange == null)
                _attackRange = GetComponent<BossWardenAttackRange>();

            // ✅ v1.1 버그1 수정: 람다 대신 Action 필드에 저장하여 정상 해제 보장
            // 람다는 매번 새 객체 생성 → -= 해제 불가 → 중복 구독 버그
            // Action 필드에 저장 → 동일 참조로 +=/- = 정상 동작
            _onArmLSealReady = () => HandleArmSealReady(_armL);
            _onArmLReleased = () => HandleArmReleased(_armL);
            _onArmRSealReady = () => HandleArmSealReady(_armR);
            _onArmRReleased = () => HandleArmReleased(_armR);
        }

        private void Start()
        {
            // 플레이어 탐색 (1회, 성능 안전)
            var players = FindObjectsByType<PlayerMoveController>(FindObjectsSortMode.None);
            if (players.Length > 0)
            {
                _playerTransform = players[0].transform;
                _playerRigid2D = players[0].GetComponent<Rigidbody2D>();
            }
            else
                Debug.LogWarning("[BossWardenSealExecutor] PlayerMoveController 를 찾을 수 없습니다.");

            _input = PlayerInputHandler.Instance;
            if (_input == null)
                Debug.LogWarning("[BossWardenSealExecutor] PlayerInputHandler.Instance 가 null 입니다.");

            // Core 이벤트 구독
            SubscribeCoreEvents();

            // 팔 부위 SealGauge 이벤트 구독
            SubscribeArmEvents(_armL);
            SubscribeArmEvents(_armR);

            // 감지 루프 시작
            _detectCoroutine = StartCoroutine(DetectSealInput());
        }

        private void OnDestroy()
        {
            UnsubscribeCoreEvents();
            UnsubscribeArmEvents(_armL);
            UnsubscribeArmEvents(_armR);

            // TimeScale 보호 — 오브젝트 파괴 시 복구
            Time.timeScale = 1.0f;
        }

        private void Update()
        {
            // 쿨다운 감소
            if (_cooldownTimer > 0f)
                _cooldownTimer -= Time.unscaledDeltaTime; // 슬로우 중에도 쿨다운 진행

            // S키 재누름 확인
            if (_mustReleaseKey && _input != null && !_input.IsSealHeld)
                _mustReleaseKey = false;

            // ✅ v1.1 버그 수정: _holdTimer 는 DetectSealInput 코루틴에서 전담 관리
            // 기존: if (_isExecuting) _holdTimer = 0f; ← 집행 중에 리셋하는 역전 버그
            // 수정: 이 블록 제거 — DetectSealInput 루프 내에서만 초기화
        }

        // ══════════════════════════════════════════════════════
        // 이벤트 구독 / 해제
        // ══════════════════════════════════════════════════════

        private void SubscribeCoreEvents()
        {
            if (_core == null) return;

            _core.OnGroggyEnter -= HandleGroggyEnter;
            _core.OnGroggyExit -= HandleGroggyExit;
            _core.OnFinalSealReady -= HandleFinalSealReady;
            _core.OnDilPhaseExit -= HandleDilPhaseExit;

            _core.OnGroggyEnter += HandleGroggyEnter;
            _core.OnGroggyExit += HandleGroggyExit;
            _core.OnFinalSealReady += HandleFinalSealReady;
            _core.OnDilPhaseExit += HandleDilPhaseExit;
        }

        private void UnsubscribeCoreEvents()
        {
            if (_core == null) return;

            _core.OnGroggyEnter -= HandleGroggyEnter;
            _core.OnGroggyExit -= HandleGroggyExit;
            _core.OnFinalSealReady -= HandleFinalSealReady;
            _core.OnDilPhaseExit -= HandleDilPhaseExit;
        }

        private void SubscribeArmEvents(BossWardenArmPart arm)
        {
            if (arm == null || arm.SealGauge == null) return;

            // ✅ v1.1 버그1 수정: 람다 → 캐싱된 Action 참조 사용
            // 왼팔 / 오른팔 각각 다른 Action 참조로 정상 등록/해제
            bool isLeft = arm == _armL;
            Action onReady = isLeft ? _onArmLSealReady : _onArmRSealReady;
            Action onReleased = isLeft ? _onArmLReleased : _onArmRReleased;

            arm.SealGauge.OnSealReady -= onReady;
            arm.SealGauge.OnSealReady += onReady;
            arm.SealGauge.OnReleased -= onReleased;
            arm.SealGauge.OnReleased += onReleased;
        }

        private void UnsubscribeArmEvents(BossWardenArmPart arm)
        {
            if (arm == null || arm.SealGauge == null) return;

            // ✅ v1.1 버그1 수정: 캐싱된 Action 참조로 정상 해제
            bool isLeft = arm == _armL;
            Action onReady = isLeft ? _onArmLSealReady : _onArmRSealReady;
            Action onReleased = isLeft ? _onArmLReleased : _onArmRReleased;

            arm.SealGauge.OnSealReady -= onReady;
            arm.SealGauge.OnReleased -= onReleased;
        }

        // ══════════════════════════════════════════════════════
        // Core 이벤트 핸들러
        // ══════════════════════════════════════════════════════

        private void HandleGroggyEnter()
        {
            _isCoreUnlockActive = true;
            _holdTimer = 0f;

            // 코어 범위 점선 표시
            if (_coreObject != null)
                _attackRange?.ShowCoreRange(
                    _coreObject.transform.position,
                    _data != null ? _data.coreUnlockRange : 1.5f);

            Debug.Log("[BossWardenSealExecutor] 코어 해제 감지 활성");
        }

        private void HandleGroggyExit()
        {
            _isCoreUnlockActive = false;
            _holdTimer = 0f;
            _attackRange?.HideCoreRange();

            // 슬로우 해제 보호
            RestoreTimeScale();
            Debug.Log("[BossWardenSealExecutor] 코어 해제 감지 비활성");
        }

        private void HandleFinalSealReady()
        {
            _isFinalSealActive = true;
            _holdTimer = 0f;
            Debug.Log("[BossWardenSealExecutor] 최종 봉인 감지 활성");
        }

        private void HandleDilPhaseExit()
        {
            _isFinalSealActive = false;
            _isCoreUnlockActive = false;
            RestoreTimeScale();
        }

        // ══════════════════════════════════════════════════════
        // 팔 봉인도 이벤트 핸들러
        // ══════════════════════════════════════════════════════

        private void HandleArmSealReady(BossWardenArmPart arm)
        {
            if (!_sealReadyParts.Contains(arm))
                _sealReadyParts.Add(arm);

            // 봉인 집행 범위 표시
            _attackRange?.ShowSealRange(
                arm.transform.position,
                _data != null ? _data.sealExecutionRange : 1.5f);

            Debug.Log($"[BossWardenSealExecutor] {arm.PartType} 봉인 집행 가능");
        }

        private void HandleArmReleased(BossWardenArmPart arm)
        {
            _sealReadyParts.Remove(arm);
            _attackRange?.HideSealRange();
        }

        // ══════════════════════════════════════════════════════
        // 집행 감지 루프 (메인 코루틴)
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// S키 봉인 집행 입력 감지 상시 루프.
        ///
        /// [우선순위]
        ///   최종 봉인 > 코어 해제 > 부위 봉인 집행
        ///
        /// [POC07 DetectExecutionInput 와의 차이]
        ///   POC07: 그로기 중에만 활성 (isGroggyActive 플래그)
        ///   POC08: 상시 활성 — 조건(isCoreUnlockActive, isFinalSealActive 등)으로 분기
        /// </summary>
        private IEnumerator DetectSealInput()
        {
            while (true)
            {
                // 실행 중이면 대기
                if (_isExecuting || _cooldownTimer > 0f || _mustReleaseKey)
                {
                    _holdTimer = 0f;
                    yield return null;
                    continue;
                }

                // S키 홀드 체크
                bool isSHeld = _input != null && _input.IsSealHeld;
                if (!isSHeld)
                {
                    _holdTimer = 0f;
                    yield return null;
                    continue;
                }

                // 우선순위 집행 대상 결정
                ExecutionTarget target = DetermineTarget();
                if (target == null)
                {
                    _holdTimer = 0f;
                    yield return null;
                    continue;
                }

                // 홀드 타이머 누적 (UnscaledDeltaTime — 슬로우 중에도 일정 속도)
                _holdTimer += Time.unscaledDeltaTime;

                if (_holdTimer >= target.RequiredHoldTime)
                {
                    // 집행 실행
                    _holdTimer = 0f;
                    yield return StartCoroutine(ExecuteSeal(target));
                }

                yield return null;
            }
        }

        // ══════════════════════════════════════════════════════
        // 집행 대상 결정
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 현재 집행 가능한 대상을 결정한다.
        /// 플레이어가 범위 내에 있어야 한다.
        ///
        /// [우선순위]
        ///   1. 최종 봉인 (_isFinalSealActive + 코어 범위 내)
        ///   2. 코어 해제 (_isCoreUnlockActive + 코어 범위 내)
        ///   3. 부위 봉인 집행 (_sealReadyParts 중 가장 가까운 부위)
        /// </summary>
        private ExecutionTarget DetermineTarget()
        {
            if (_playerTransform == null || _data == null) return null;

            // 1. 최종 봉인
            if (_isFinalSealActive && _coreObject != null)
            {
                float dist = Vector2.Distance(
                    _playerTransform.position,
                    _coreObject.transform.position);

                if (dist <= _data.coreUnlockRange)
                    return new ExecutionTarget(
                        ExecutionType.FinalSeal,
                        _coreObject.transform,
                        _data.finalSealHoldTime);
            }

            // 2. 코어 해제
            if (_isCoreUnlockActive && _coreObject != null)
            {
                float dist = Vector2.Distance(
                    _playerTransform.position,
                    _coreObject.transform.position);

                if (dist <= _data.coreUnlockRange)
                    return new ExecutionTarget(
                        ExecutionType.CoreUnlock,
                        _coreObject.transform,
                        _data.coreUnlockHoldTime);
            }

            // 3. 부위 봉인 집행
            BossWardenArmPart nearest = null;
            float nearestDist = float.MaxValue;

            foreach (var arm in _sealReadyParts)
            {
                if (arm == null || arm.IsSealed) continue;

                float dist = Vector2.Distance(
                    _playerTransform.position,
                    arm.transform.position);

                if (dist <= _data.sealExecutionRange && dist < nearestDist)
                {
                    nearestDist = dist;
                    nearest = arm;
                }
            }

            if (nearest != null)
                return new ExecutionTarget(
                    ExecutionType.PartSeal,
                    nearest.transform,
                    _data.sealExecutionHoldTime,
                    nearest);

            return null;
        }

        // ══════════════════════════════════════════════════════
        // 집행 실행 코루틴
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 집행 실행 코루틴.
        /// 슬로우 → 입력 차단 → 홀드 대기 → 완료 이벤트 → 복구.
        ///
        /// [POC07 ExecuteRoutine 과의 차이]
        ///   POC07: 플레이어 자동 이동 포함
        ///   POC08: 자동 이동 없음. 플레이어가 이미 범위 내에 있음.
        ///          슬로우 모션 추가.
        /// </summary>
        private IEnumerator ExecuteSeal(ExecutionTarget target)
        {
            _isExecuting = true;
            BlockPlayerInput();

            // 슬로우 적용
            float slowScale = target.Type switch
            {
                ExecutionType.FinalSeal => _data.finalSealSlowTimeScale,
                ExecutionType.CoreUnlock => _data.dilPhaseSlowTimeScale,
                _ => _data.dilPhaseSlowTimeScale,
            };

            Time.timeScale = slowScale;

            Debug.Log($"[BossWardenSealExecutor] {target.Type} 집행 시작 (SlowScale: {slowScale})");

            float elapsed = 0f;
            float holdRequired = target.RequiredHoldTime;

            while (elapsed < holdRequired)
            {
                // S키 해제 → 중단
                if (_input == null || !_input.IsSealHeld)
                {
                    Debug.Log("[BossWardenSealExecutor] S키 해제 → 집행 취소");
                    goto cleanup;
                }

                // 범위 이탈 → 중단
                if (!IsInRange(target))
                {
                    Debug.Log("[BossWardenSealExecutor] 범위 이탈 → 집행 취소");
                    goto cleanup;
                }

                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            // 집행 완료 처리
            CompleteExecution(target);
            goto finish;

        cleanup:
            RestoreTimeScale();

        finish:
            UnblockPlayerInput();
            _isExecuting = false;
            _mustReleaseKey = true;
            _cooldownTimer = 0.5f;
        }

        // ══════════════════════════════════════════════════════
        // 집행 완료 처리
        // ══════════════════════════════════════════════════════

        private void CompleteExecution(ExecutionTarget target)
        {
            RestoreTimeScale();

            switch (target.Type)
            {
                case ExecutionType.PartSeal:
                    if (target.ArmPart != null)
                    {
                        target.ArmPart.SealGauge?.ExecuteSeal();
                        _sealReadyParts.Remove(target.ArmPart);
                        _attackRange?.HideSealRange();
                        OnPartSealed?.Invoke(target.ArmPart.PartType);
                        Debug.Log($"[BossWardenSealExecutor] {target.ArmPart.PartType} 봉인 집행 완료");
                    }
                    break;

                case ExecutionType.CoreUnlock:
                    _isCoreUnlockActive = false;
                    _attackRange?.HideCoreRange();
                    OnCoreUnlocked?.Invoke();
                    Debug.Log("[BossWardenSealExecutor] 코어 해제 완료");
                    break;

                case ExecutionType.FinalSeal:
                    _isFinalSealActive = false;
                    _attackRange?.HideCoreRange();
                    OnFinalSealCompleted?.Invoke();
                    Debug.Log("[BossWardenSealExecutor] 최종 봉인 완료");
                    break;
            }
        }

        // ══════════════════════════════════════════════════════
        // 플레이어 입력 차단 / 해제
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 집행 중 플레이어 이동/대시/공격 차단.
        /// PlayerInputHandler.BlockAll() 사용.
        /// </summary>
        private void BlockPlayerInput()
        {
            _input?.BlockAll();

            if (_playerRigid2D != null)
                _playerRigid2D.linearVelocity = Vector2.zero;
        }

        /// <summary>
        /// 집행 완료/취소 후 입력 차단 해제.
        /// </summary>
        private void UnblockPlayerInput()
        {
            _input?.UnblockAll();
        }

        // ══════════════════════════════════════════════════════
        // 유틸
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 플레이어가 집행 대상 범위 내에 있는지 체크.
        /// </summary>
        private bool IsInRange(ExecutionTarget target)
        {
            if (_playerTransform == null || target.Transform == null) return false;

            float dist = Vector2.Distance(
                _playerTransform.position,
                target.Transform.position);

            float range = target.Type == ExecutionType.PartSeal
                ? _data.sealExecutionRange
                : _data.coreUnlockRange;

            return dist <= range;
        }

        /// <summary>
        /// Time.timeScale 을 1.0 으로 복구한다.
        /// 슬로우 모션 종료 공통 처리.
        /// </summary>
        private void RestoreTimeScale()
        {
            if (!Mathf.Approximately(Time.timeScale, 1.0f))
            {
                Time.timeScale = 1.0f;
                Debug.Log("[BossWardenSealExecutor] TimeScale 복구 → 1.0");
            }
        }

        // ══════════════════════════════════════════════════════
        // 내부 데이터 클래스
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 집행 대상 종류.
        /// </summary>
        private enum ExecutionType { PartSeal, CoreUnlock, FinalSeal }

        /// <summary>
        /// 집행 대상 정보.
        /// DetermineTarget() 에서 생성, ExecuteSeal() 에 전달.
        /// </summary>
        private class ExecutionTarget
        {
            public ExecutionType Type { get; }
            public Transform Transform { get; }
            public float RequiredHoldTime { get; }
            public BossWardenArmPart ArmPart { get; }

            public ExecutionTarget(
                ExecutionType type,
                Transform transform,
                float holdTime,
                BossWardenArmPart armPart = null)
            {
                Type = type;
                Transform = transform;
                RequiredHoldTime = holdTime;
                ArmPart = armPart;
            }
        }
    }
}