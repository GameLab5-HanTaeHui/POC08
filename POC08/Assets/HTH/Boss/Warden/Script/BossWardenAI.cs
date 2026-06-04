// ============================================================
// BossWardenAI.cs  v1.2
// Boss_Warden 탑뷰 AI
//
// [v1.2 수정]
//   🔴 ExecutePattern() 상태 이중 체크 + 상세 디버그 로그 추가
//       기존: _isStopped 만 체크 → Active 내부 무한루프 시 탈출 불가
//       수정: POC07 TestBossAI 처럼 _currentState == Warning/Active 이중 체크
//             → 상태 불일치 감지 시 즉시 패턴 중단
//       추가: 패턴명/단계 진입출 로그 → "어떤 패턴이 어디서 멈추는지" 추적 가능
//
// [v1.1 수정]
//   🔴 버그5: TrySelectPattern() 에서 매 프레임 List 생성으로 GC 압박
//       수정: _availablePatterns 멤버 변수로 캐싱 → Clear() 후 재사용
//
// [레이어 구조 — SEAL 프로젝트 3분리]
//   Player|Enemy            : 보스 본체/부위 레이어 (플레이어 공격 감지 대상)
//   Player|EnemyAttack      : 보스 패턴 OverlapXX 발생원 레이어
//   Player|EnemyAttackHitBox: 플레이어 HurtBox 레이어 (패턴 _playerLayer 에 선택)
//
// [POC07 참고]
//   TestBossAI.cs (v1.0) 구조를 기반으로 탑뷰 시스템에 맞게 재설계.
//   TestBossAI 의 시행착오를 그대로 계승하여 안정성 우선.
//
// [POC07 TestBossAI 와의 차이]
//   POC07: 횡스크롤 — X축 이동만 / SpriteRenderer.flipX 로 방향 표현
//   POC08: 탑뷰 — X/Y 평면 8방향 이동 / transform.up 방향 회전 없음
//          → Rigidbody2D.linearVelocity 로 8방향 이동
//          → 방향은 플레이어 기준 Vector2 로 계산
//
//   POC07: SealProjectile 감지 / SealComponent 보스 전체 봉인 체크
//   POC08: 투사체 시스템 없음 → 제거
//          BossWardenArmPart.IsSealed 로 팔 봉인 여부만 체크
//
//   POC07: 딜타임(DilTime) 이벤트 구독
//   POC08: DilPhase(딜 페이즈) 이벤트 구독
//          + Phase2 전환 이벤트 추가
//          + 최종 봉인/처치 이벤트 추가
//
// [상태 다이어그램]
//   Idle ──(패턴 선택)──────→ Warning → Active → Recovery → Idle
//   Idle ──(패턴 범위 외)───→ Chase
//   Chase ──(패턴 범위 내)──→ Idle
//   Recovery ──(OnPatternGroggy)──→ Core 가 EnterGroggy() 판단
//   ※ Groggy / DilPhase 상태는 BossWardenCore 가 관리
//     AI 는 _isStopped 플래그로 이동/패턴 정지만 처리
//
// [패턴 선택 구조]
//   Idle 상태 → TrySelectPattern()
//     → CanExecute && IsAvailable 인 패턴 수집
//     → 랜덤 선택 → ExecutePattern() 코루틴
//
// [Recovery 취약 구간]
//   Recovery 진입 시 → 양팔 SetRecoveryVuln(true)
//   Recovery 종료 시 → 양팔 SetRecoveryVuln(false)
//   → 봉인도 recoveryVulnMultiplier 배율 자동 적용
//
// [2페이즈 전환]
//   BossWardenCore.OnPhaseChanged(2) 수신
//   → 모든 패턴 UnlockPhase2() 호출
//   → _moveSpeed = _data.phase2MoveSpeed 로 갱신
//   → Recovery 취약 구간 배율도 자동 갱신 (DataSO 참조)
//
// [namespace]
//   namespace : SEAL
// ============================================================

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SEAL
{
    /// <summary>
    /// Boss_Warden 탑뷰 AI. (v1.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [이 스크립트가 하는 것]
    ///   - 상태 관리 (Idle / Chase / Warning / Active / Recovery)
    ///   - 탑뷰 8방향 플레이어 추적 이동 (Rigidbody2D.linearVelocity)
    ///   - 패턴 선택 및 실행 코루틴 관리
    ///   - Groggy / DilPhase 중 이동·패턴 정지 (_isStopped)
    ///   - Recovery 취약 구간 팔 부위 전달
    ///   - 2페이즈 패턴 강화 적용
    ///
    /// [이 스크립트가 하지 않는 것]
    ///   - Groggy / DilPhase 상태 관리 → BossWardenCore
    ///   - 봉인 집행 처리 → BossWardenSealExecutor
    ///   - 코어 해제 처리 → BossWardenSealExecutor
    ///   - 색상 피드백 → BossWardenFeedback
    /// ────────────────────────────────────────────────────
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(SpriteRenderer))]
    public class BossWardenAI : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        // 상태 열거형
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Warden AI 상태.
        ///
        /// Groggy / DilPhase 는 BossWardenCore 가 관리.
        /// AI 는 _isStopped 플래그만으로 이 구간을 처리.
        /// </summary>
        public enum WardenAIState
        {
            /// <summary>
            /// 패턴 대기.
            /// 플레이어 방향 유지 + 패턴 선택 시도.
            /// 플레이어가 _patternRange 밖이면 Chase 전환.
            /// </summary>
            Idle,

            /// <summary>
            /// 플레이어 추적 이동.
            /// 탑뷰 8방향 이동 (Rigidbody2D.linearVelocity).
            /// _patternRange 이내 진입 시 Idle 전환.
            /// </summary>
            Chase,

            /// <summary>
            /// 패턴 예고 중.
            /// BossPatternBase.ExecuteWarning() 실행 중.
            /// 공격 예고 범위 표시 구간.
            /// </summary>
            Warning,

            /// <summary>
            /// 패턴 시전 중.
            /// BossPatternBase.ExecuteActive() 실행 중.
            /// 실제 히트박스 판정 구간.
            /// </summary>
            Active,

            /// <summary>
            /// 패턴 후딜레이.
            /// BossPatternBase.ExecuteRecovery() 실행 중.
            /// 취약 구간 — 봉인도 recoveryVulnMultiplier 배율 적용.
            /// 완료 후 OnPatternGroggy 발행 시 BossWardenCore.EnterGroggy() 호출.
            /// </summary>
            Recovery,
        }

        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── DataSO 연결 (필수) ──────────────────────")]

        /// <summary>
        /// Warden 수치 ScriptableObject.
        /// 이동 속도 / 패턴 범위 / 방향 쿨타임 참조.
        /// BossWardenCore.Start() 에서 Initialize() 로 주입.
        /// </summary>
        [Tooltip("BossWardenDataSO. 필수 연결.")]
        [SerializeField] private BossWardenDataSO _data;

        [Header("── 패턴 목록 (필수) ──────────────────────")]

        /// <summary>
        /// 패턴 목록.
        /// Inspector 에서 Patterns 하위의 BossPattern_XX 컴포넌트 연결.
        /// BossPatternBase.CanExecute + IsAvailable 로 실행 가능 패턴 필터.
        /// </summary>
        [Tooltip("패턴 목록. Inspector 에서 BossPattern_XX 연결.")]
        [SerializeField] private List<BossPatternBase> _patterns = new();

        [Header("── 팔 부위 연결 (필수) ──────────────────────")]

        /// <summary>
        /// 왼팔 부위 참조.
        /// Recovery 취약 구간 SetRecoveryVuln() 전달 대상.
        /// </summary>
        [Tooltip("왼팔 BossWardenArmPart. 필수 연결.")]
        [SerializeField] private BossWardenArmPart _armL;

        /// <summary>
        /// 오른팔 부위 참조.
        /// Recovery 취약 구간 SetRecoveryVuln() 전달 대상.
        /// </summary>
        [Tooltip("오른팔 BossWardenArmPart. 필수 연결.")]
        [SerializeField] private BossWardenArmPart _armR;

        // ══════════════════════════════════════════════════════
        // 컴포넌트 참조
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Rigidbody2D.
        /// Chase 이동 시 linearVelocity 직접 제어.
        /// Awake 에서 GetComponent.
        /// </summary>
        private Rigidbody2D _rigid2D;

        /// <summary>
        /// SpriteRenderer.
        /// BossWardenFeedback 이 색상을 제어하므로 AI 에서는 직접 조작하지 않음.
        /// Awake 에서 GetComponent (참조 유지용).
        /// </summary>
        private SpriteRenderer _spriteRenderer;

        /// <summary>
        /// BossWardenCore 참조.
        /// 이벤트 구독 + EnterGroggy() 호출.
        /// Awake 에서 GetComponent.
        /// </summary>
        private BossWardenCore _core;

        /// <summary>
        /// 플레이어 Transform.
        /// Chase 이동 방향 계산 / 패턴 범위 거리 계산에 사용.
        /// Start() 에서 FindObjectsByType 으로 탐색.
        /// </summary>
        private Transform _playerTransform;

        // ══════════════════════════════════════════════════════
        // 내부 상태
        // ══════════════════════════════════════════════════════

        /// <summary> 현재 AI 상태. </summary>
        private WardenAIState _currentState = WardenAIState.Idle;

        /// <summary>
        /// 현재 실행 중인 패턴.
        /// null = 패턴 없음.
        /// ExecutePattern() 코루틴 시작 시 설정, CleanupPattern() 에서 null.
        /// </summary>
        private BossPatternBase _currentPattern;

        /// <summary>
        /// 현재 패턴 실행 코루틴 핸들.
        /// 그로기 진입 시 StopCoroutine() 에 사용.
        /// </summary>
        private Coroutine _patternCoroutine;

        /// <summary>
        /// 이동/패턴 선택 정지 플래그.
        ///
        /// true  : Groggy / DilPhase / Dead 중 — 이동 및 패턴 선택 완전 정지.
        /// false : 정상 동작.
        ///
        /// [주의]
        ///   _isStopped = true 시 FixedUpdate 에서 linearVelocity = 0 강제 적용.
        ///   Groggy 종료 / DilPhase 종료 시 false 로 복구.
        /// </summary>
        private bool _isStopped;

        /// <summary>
        /// 현재 이동 속도 (units/s).
        /// 1페이즈: _data.moveSpeed / 2페이즈: _data.phase2MoveSpeed.
        /// 페이즈 전환 시 OnPhaseChanged 수신 후 갱신.
        /// </summary>
        private float _currentMoveSpeed;

        /// <summary>
        /// 방향 전환 쿨타임 잔여 시간 (초).
        /// 탑뷰에서 너무 잦은 방향 재계산을 방지.
        /// </summary>
        private float _flipCooldownTimer;

        /// <summary>
        /// 현재 플레이어 방향 벡터 (정규화).
        /// UpdateFacingTowardPlayer() 에서 갱신.
        /// Chase 이동 방향 및 패턴 Warning 방향에 사용.
        /// </summary>
        private Vector2 _facingDir = Vector2.right;

        /// <summary>
        /// 패턴 선택 시 재사용하는 리스트 캐시.
        ///
        /// [v1.1 버그5 수정]
        ///   TrySelectPattern() 이 Idle 상태에서 매 프레임 호출됨.
        ///   기존: var available = new List&#60;BossPatternBase&#62;() — 매 프레임 GC 할당
        ///   수정: 멤버 변수로 캐싱 → Clear() 후 재사용 → GC 할당 제거
        /// </summary>
        private readonly List<BossPatternBase> _availablePatterns = new List<BossPatternBase>();

        // ══════════════════════════════════════════════════════
        // 이벤트
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 상태 전환 시 발행.
        /// BossWardenFeedback 이 구독하여 상태별 DOTween 색상 연출 시작.
        /// 파라미터: (새 상태, 현재 실행 중인 패턴 — null 가능)
        /// </summary>
        public event Action<WardenAIState, BossPatternBase> OnStateChanged;

        /// <summary>
        /// 플레이어 방향 벡터 변화 시 발행.
        /// 탑뷰에서 SpriteRenderer flipX 처리에 사용.
        /// 파라미터: 플레이어 방향 Vector2 (정규화).
        /// </summary>
        public event Action<Vector2> OnFacingChanged;

        // ══════════════════════════════════════════════════════
        // 프로퍼티
        // ══════════════════════════════════════════════════════

        /// <summary> 현재 AI 상태. </summary>
        public WardenAIState CurrentState => _currentState;

        /// <summary> 이동/패턴 정지 여부. </summary>
        public bool IsStopped => _isStopped;

        /// <summary>
        /// 현재 플레이어 방향 벡터 (정규화).
        /// 패턴이 Warning 방향 결정 시 참조.
        /// </summary>
        public Vector2 FacingDir => _facingDir;

        /// <summary>
        /// 플레이어 Transform 참조.
        /// 패턴 스크립트에서 플레이어 위치 참조 시 사용.
        /// </summary>
        public Transform PlayerTransform => _playerTransform;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            _rigid2D = GetComponent<Rigidbody2D>();
            _spriteRenderer = GetComponent<SpriteRenderer>();
            _core = GetComponent<BossWardenCore>();

            // 초기 이동 속도 설정
            _currentMoveSpeed = _data != null ? _data.moveSpeed : 3.5f;
        }

        private void Start()
        {
            // 플레이어 탐색 — FindObjectsByType: Awake/Start 1회만 호출 (성능 안전)
            var playerComps = FindObjectsByType<PlayerMoveController>(FindObjectsSortMode.None);
            if (playerComps.Length > 0)
                _playerTransform = playerComps[0].transform;
            else
                Debug.LogWarning("[BossWardenAI] PlayerMoveController 를 씬에서 찾을 수 없습니다.");

            // BossWardenCore 이벤트 구독
            SubscribeCoreEvents();

            // 패턴 이벤트 구독
            SubscribePatternEvents();
        }

        private void OnDestroy()
        {
            // BossWardenCore 이벤트 구독 해제
            UnsubscribeCoreEvents();

            // 패턴 이벤트 구독 해제
            UnsubscribePatternEvents();
        }

        private void Update()
        {
            if (_isStopped) return;

            UpdateTimers();
            UpdateStateLogic();
        }

        private void FixedUpdate()
        {
            // _isStopped 중에는 반드시 속도 0 강제 적용
            if (_isStopped)
            {
                _rigid2D.linearVelocity = Vector2.zero;
                return;
            }

            UpdateMovement();
        }

        // ══════════════════════════════════════════════════════
        // 초기화
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// BossWardenCore.Start() 에서 DataSO 주입 후 초기화 완료.
        /// </summary>
        public void Initialize(BossWardenDataSO data)
        {
            _data = data;
            _currentMoveSpeed = data.moveSpeed;
            Debug.Log("[BossWardenAI] 초기화 완료");
        }

        // ══════════════════════════════════════════════════════
        // 이벤트 구독 / 해제 — 명시적 분리로 중복 구독 방지
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// BossWardenCore 이벤트 구독.
        /// 구독 전 반드시 -= 먼저 실행하여 중복 구독 방지.
        /// </summary>
        private void SubscribeCoreEvents()
        {
            if (_core == null) return;

            // 중복 방지: 먼저 해제 후 구독
            _core.OnGroggyEnter -= HandleGroggyEnter;
            _core.OnGroggyExit -= HandleGroggyExit;
            _core.OnDilPhaseEnter -= HandleDilPhaseEnter;
            _core.OnDilPhaseExit -= HandleDilPhaseExit;
            _core.OnPhaseChanged -= HandlePhaseChanged;
            _core.OnDead -= HandleDead;

            _core.OnGroggyEnter += HandleGroggyEnter;
            _core.OnGroggyExit += HandleGroggyExit;
            _core.OnDilPhaseEnter += HandleDilPhaseEnter;
            _core.OnDilPhaseExit += HandleDilPhaseExit;
            _core.OnPhaseChanged += HandlePhaseChanged;
            _core.OnDead += HandleDead;
        }

        private void UnsubscribeCoreEvents()
        {
            if (_core == null) return;

            _core.OnGroggyEnter -= HandleGroggyEnter;
            _core.OnGroggyExit -= HandleGroggyExit;
            _core.OnDilPhaseEnter -= HandleDilPhaseEnter;
            _core.OnDilPhaseExit -= HandleDilPhaseExit;
            _core.OnPhaseChanged -= HandlePhaseChanged;
            _core.OnDead -= HandleDead;
        }

        /// <summary>
        /// 패턴 이벤트 구독.
        /// 구독 전 반드시 -= 먼저 실행하여 중복 구독 방지.
        /// </summary>
        private void SubscribePatternEvents()
        {
            foreach (var pattern in _patterns)
            {
                if (pattern == null) continue;

                pattern.OnPatternGroggy -= HandlePatternGroggy;
                pattern.OnPatternGroggy += HandlePatternGroggy;
            }
        }

        private void UnsubscribePatternEvents()
        {
            foreach (var pattern in _patterns)
            {
                if (pattern == null) continue;
                pattern.OnPatternGroggy -= HandlePatternGroggy;
            }
        }

        // ══════════════════════════════════════════════════════
        // 타이머
        // ══════════════════════════════════════════════════════

        private void UpdateTimers()
        {
            if (_flipCooldownTimer > 0f)
                _flipCooldownTimer -= Time.deltaTime;
        }

        // ══════════════════════════════════════════════════════
        // 상태 로직 (Update)
        // ══════════════════════════════════════════════════════

        private void UpdateStateLogic()
        {
            switch (_currentState)
            {
                case WardenAIState.Idle:
                    // 플레이어 방향 유지
                    UpdateFacingTowardPlayer();

                    // 패턴 범위 밖 → Chase
                    if (!IsPlayerInRange(_data.patternRange))
                    {
                        ChangeState(WardenAIState.Chase);
                        return;
                    }

                    // 패턴 선택 시도
                    TrySelectPattern();
                    break;

                case WardenAIState.Chase:
                    // 플레이어 방향 유지
                    UpdateFacingTowardPlayer();

                    // 패턴 범위 진입 → Idle
                    if (IsPlayerInRange(_data.patternRange))
                        ChangeState(WardenAIState.Idle);
                    break;

                // Warning / Active / Recovery 는 ExecutePattern 코루틴이 처리
                case WardenAIState.Warning:
                case WardenAIState.Active:
                case WardenAIState.Recovery:
                    break;
            }
        }

        // ══════════════════════════════════════════════════════
        // 이동 (FixedUpdate)
        // ══════════════════════════════════════════════════════

        private void UpdateMovement()
        {
            // Chase 상태에서만 이동
            if (_currentState != WardenAIState.Chase)
            {
                _rigid2D.linearVelocity = Vector2.zero;
                return;
            }

            if (_playerTransform == null) return;

            // 플레이어 방향으로 이동 (탑뷰 8방향)
            Vector2 dir = GetDirToPlayer();
            _rigid2D.linearVelocity = dir * _currentMoveSpeed;
        }

        // ══════════════════════════════════════════════════════
        // 방향 계산
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 플레이어 방향을 향하도록 _facingDir 갱신.
        /// 쿨타임(_flipCooldownTimer) 이 남아있으면 갱신 스킵.
        ///
        /// [탑뷰 방향 처리]
        ///   횡스크롤과 달리 단순 flipX 가 아님.
        ///   _facingDir 을 OnFacingChanged 로 발행 → BossWardenFeedback 에서 처리.
        /// </summary>
        private void UpdateFacingTowardPlayer()
        {
            if (_playerTransform == null) return;
            if (_flipCooldownTimer > 0f) return;

            Vector2 newDir = GetDirToPlayer();

            // 방향이 실질적으로 변화했을 때만 이벤트 발행
            if (Vector2.Dot(newDir, _facingDir) < 0.98f)
            {
                _facingDir = newDir;
                _flipCooldownTimer = _data != null ? _data.flipCooldown : 0.5f;
                OnFacingChanged?.Invoke(_facingDir);
            }
        }

        /// <summary>
        /// 플레이어 방향으로 즉시 전환 (쿨타임 무시).
        /// 그로기 종료 / DilPhase 종료 시 즉각 플레이어 방향으로 복구.
        /// </summary>
        private void TurnTowardPlayerImmediate()
        {
            if (_playerTransform == null) return;

            _facingDir = GetDirToPlayer();
            _flipCooldownTimer = 0f;
            OnFacingChanged?.Invoke(_facingDir);
        }

        /// <summary>
        /// 플레이어 방향 벡터 반환 (정규화).
        /// 플레이어와 거리가 0 이면 현재 _facingDir 유지.
        /// </summary>
        private Vector2 GetDirToPlayer()
        {
            if (_playerTransform == null) return _facingDir;

            Vector2 diff = (Vector2)(_playerTransform.position - transform.position);
            return diff.sqrMagnitude > 0.001f ? diff.normalized : _facingDir;
        }

        // ══════════════════════════════════════════════════════
        // 거리 / 범위 판정
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 플레이어가 지정 범위 이내인지 체크.
        /// </summary>
        private bool IsPlayerInRange(float range)
        {
            if (_playerTransform == null) return false;
            return Vector2.Distance(transform.position, _playerTransform.position) <= range;
        }

        // ══════════════════════════════════════════════════════
        // 상태 전환
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 상태를 전환하고 OnStateChanged 이벤트를 발행한다.
        /// 동일 상태로 전환하면 이벤트 미발행 (불필요한 피드백 방지).
        /// </summary>
        private void ChangeState(WardenAIState newState)
        {
            if (_currentState == newState) return;

            _currentState = newState;
            OnStateChanged?.Invoke(_currentState, _currentPattern);

            Debug.Log($"[BossWardenAI] 상태 전환 → {_currentState}");
        }

        // ══════════════════════════════════════════════════════
        // 패턴 선택
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 실행 가능한 패턴을 선택하여 ExecutePattern() 코루틴을 시작한다.
        ///
        /// [선택 조건]
        ///   BossPatternBase.CanExecute  = true (쿨타임 완료 + 실행 중 아님 + 페이즈 조건)
        ///   BossPatternBase.IsAvailable = true (연결 팔이 봉인되지 않음)
        ///
        /// [선택 방식]
        ///   조건 통과 패턴 목록에서 랜덤 선택.
        ///   추후 가중치 / 우선순위 확장 가능.
        ///
        /// [POC07 TrySelectPattern 과의 차이]
        ///   POC07: SealComponent 전체 봉인 체크 + 개별 팔 타입 캐스팅 체크
        ///   POC08: BossPatternBase.IsAvailable 로 통일 (캐스팅 불필요)
        /// </summary>
        private void TrySelectPattern()
        {
            // 이미 패턴 실행 중이면 무시
            if (_currentPattern != null) return;
            if (_patterns == null || _patterns.Count == 0) return;

            // ✅ v1.1 버그5 수정: 캐시된 리스트 재사용 (GC 할당 제거)
            _availablePatterns.Clear();
            foreach (var p in _patterns)
            {
                if (p == null) continue;
                if (!p.CanExecute) continue;
                if (!p.IsAvailable) continue;
                _availablePatterns.Add(p);
            }

            if (_availablePatterns.Count == 0) return;

            // 랜덤 선택
            int idx = UnityEngine.Random.Range(0, _availablePatterns.Count);
            var selected = _availablePatterns[idx];

            _currentPattern = selected;
            _patternCoroutine = StartCoroutine(ExecutePattern(selected));
        }

        // ══════════════════════════════════════════════════════
        // 패턴 실행 코루틴
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 패턴 실행 코루틴.
        /// Warning → Active → Recovery 순서로 실행.
        ///
        /// [_isStopped 체크]
        ///   각 단계 전/후 _isStopped 체크.
        ///   Groggy / DilPhase 진입 시 즉시 중단하여 패턴 정리.
        ///
        /// [POC07 ExecutePattern 과의 차이]
        ///   POC07: _currentState == Warning/Active 체크로 단계 진행 여부 판단
        ///   POC08: _isStopped 체크만으로 단순화 + Recovery 취약 구간 팔 전달 추가
        ///
        /// [코루틴 호출 주의]
        ///   Interrupt() 를 먼저 호출한 뒤 StopCoroutine().
        ///   StopCoroutine 만 호출하면 패턴 내부 정리가 실행되지 않음.
        /// </summary>
        /// <summary>
        /// 패턴 실행 코루틴.
        /// Warning → Active → Recovery 순서로 실행.
        ///
        /// [v1.2 수정 — POC07 TestBossAI 상태 이중 체크 적용]
        ///   기존: _isStopped 만 체크 → Active 내부 무한루프 시 탈출 불가
        ///   수정: POC07 처럼 _currentState == Warning/Active 이중 체크 추가
        ///         + 패턴명/단계 상세 디버그 로그 추가
        ///
        /// [Active 무한루프 원인]
        ///   BossPattern_Charge.OnActive() 내부 while(true) 에서
        ///   _isInterrupted 체크로만 탈출 → Interrupt 호출 없이 Active 가 종료되지 않으면
        ///   ExecuteActive() 가 영원히 반환되지 않음.
        ///   → _isStopped 가 true 여도 이미 Active 코루틴 내부에 있으면 탈출 불가.
        ///   → 상태 이중 체크: _currentState != Active 면 Active 단계 스킵.
        ///
        /// [코루틴 호출 주의]
        ///   Interrupt() 를 먼저 호출한 뒤 StopCoroutine().
        ///   StopCoroutine 만 호출하면 패턴 내부 정리가 실행되지 않음.
        /// </summary>
        private IEnumerator ExecutePattern(BossPatternBase pattern)
        {
            string patternName = pattern.GetType().Name;

            // ── Warning ──────────────────────────────
            Debug.Log($"[BossWardenAI] ▶ [{patternName}] Warning 시작");
            ChangeState(WardenAIState.Warning);
            yield return StartCoroutine(pattern.ExecuteWarning());
            Debug.Log($"[BossWardenAI] ■ [{patternName}] Warning 종료 | isStopped:{_isStopped}");

            // Groggy / DilPhase 진입 감지
            if (_isStopped)
            {
                Debug.Log($"[BossWardenAI] ⚠ [{patternName}] Warning 후 isStopped → 패턴 중단");
                CleanupPattern();
                yield break;
            }

            // ── Active ──────────────────────────────
            // [POC07 이중 체크] Warning 단계가 정상 완료됐는지 상태로 확인
            if (_currentState != WardenAIState.Warning)
            {
                Debug.Log($"[BossWardenAI] ⚠ [{patternName}] Warning 후 상태 불일치({_currentState}) → 패턴 중단");
                CleanupPattern();
                yield break;
            }

            Debug.Log($"[BossWardenAI] ▶ [{patternName}] Active 시작");
            ChangeState(WardenAIState.Active);
            yield return StartCoroutine(pattern.ExecuteActive());
            Debug.Log($"[BossWardenAI] ■ [{patternName}] Active 종료 | isStopped:{_isStopped}");

            if (_isStopped)
            {
                Debug.Log($"[BossWardenAI] ⚠ [{patternName}] Active 후 isStopped → 패턴 중단");
                CleanupPattern();
                yield break;
            }

            // ── Recovery ────────────────────────────
            // [POC07 이중 체크] Active 단계가 정상 완료됐는지 상태로 확인
            if (_currentState != WardenAIState.Active)
            {
                Debug.Log($"[BossWardenAI] ⚠ [{patternName}] Active 후 상태 불일치({_currentState}) → 패턴 중단");
                CleanupPattern();
                yield break;
            }

            Debug.Log($"[BossWardenAI] ▶ [{patternName}] Recovery 시작");
            ChangeState(WardenAIState.Recovery);

            // 취약 구간 시작 — 양팔 봉인도 배율 활성
            SetArmsRecoveryVuln(true);

            yield return StartCoroutine(pattern.ExecuteRecovery());

            // 취약 구간 종료 — 반드시 해제 (정상/중단 무관)
            SetArmsRecoveryVuln(false);
            Debug.Log($"[BossWardenAI] ■ [{patternName}] Recovery 종료 | isStopped:{_isStopped}");

            if (_isStopped)
            {
                Debug.Log($"[BossWardenAI] ⚠ [{patternName}] Recovery 후 isStopped → 패턴 중단");
                CleanupPattern();
                yield break;
            }

            // ── 정상 종료 → Idle 복귀 ──────────────
            Debug.Log($"[BossWardenAI] ✅ [{patternName}] 패턴 정상 완료 → Idle 복귀");
            CleanupPattern();
            ChangeState(WardenAIState.Idle);
        }

        /// <summary>
        /// 패턴 실행 종료 후 상태 정리.
        /// _currentPattern / _patternCoroutine 초기화.
        /// </summary>
        private void CleanupPattern()
        {
            _currentPattern = null;
            _patternCoroutine = null;
        }

        // ══════════════════════════════════════════════════════
        // Recovery 취약 구간 팔 전달
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 양팔에 Recovery 취약 구간 활성/비활성 전달.
        /// 봉인도 누적 시 recoveryVulnMultiplier 배율 적용 여부를 제어.
        ///
        /// [null 안전]
        ///   팔이 null 이거나 봉인 완료(IsSealed) 상태이면 무시.
        ///   봉인 완료된 팔은 추가 봉인도 누적이 없으므로 배율 적용 불필요.
        /// </summary>
        private void SetArmsRecoveryVuln(bool isVuln)
        {
            if (_armL != null && !_armL.IsSealed)
                _armL.SetRecoveryVuln(isVuln);

            if (_armR != null && !_armR.IsSealed)
                _armR.SetRecoveryVuln(isVuln);
        }

        // ══════════════════════════════════════════════════════
        // BossWardenCore 이벤트 핸들러
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 그로기 진입 수신.
        /// 이동 정지 + 현재 패턴 강제 중단.
        ///
        /// [Interrupt → StopCoroutine 순서]
        ///   Interrupt() 먼저 → 패턴 내부 OnPatternEnd 발행 + 상태 정리 완료
        ///   그 후 StopCoroutine() → 코루틴 즉시 중단
        /// </summary>
        public void HandleGroggyEnter()
        {
            _isStopped = true;

            // 취약 구간이 활성화 중이었으면 해제
            SetArmsRecoveryVuln(false);

            // 현재 패턴 강제 중단
            InterruptCurrentPattern();

            Debug.Log("[BossWardenAI] 그로기 진입 → 이동/패턴 정지");
        }

        /// <summary>
        /// 그로기 종료 수신.
        /// 이동/패턴 재개 + 플레이어 방향 즉시 전환 후 Idle 복귀.
        /// </summary>
        public void HandleGroggyExit()
        {
            _isStopped = false;
            TurnTowardPlayerImmediate();
            ChangeState(WardenAIState.Idle);

            Debug.Log("[BossWardenAI] 그로기 종료 → Idle 복귀");
        }

        /// <summary>
        /// 딜 페이즈 진입 수신.
        /// 이동/패턴 정지 (그로기와 동일).
        /// </summary>
        public void HandleDilPhaseEnter()
        {
            _isStopped = true;
            SetArmsRecoveryVuln(false);
            InterruptCurrentPattern();

            Debug.Log("[BossWardenAI] 딜 페이즈 진입 → 이동/패턴 정지");
        }

        /// <summary>
        /// 딜 페이즈 종료 수신.
        /// 이동/패턴 재개 + Idle 복귀.
        /// </summary>
        public void HandleDilPhaseExit()
        {
            _isStopped = false;
            TurnTowardPlayerImmediate();
            ChangeState(WardenAIState.Idle);

            Debug.Log("[BossWardenAI] 딜 페이즈 종료 → Idle 복귀");
        }

        /// <summary>
        /// 페이즈 전환 수신.
        /// 2페이즈 진입 시 패턴 강화 + 이동 속도 갱신.
        /// </summary>
        public void HandlePhaseChanged(int newPhase)
        {
            if (newPhase != 2) return;

            // 2페이즈 이동 속도 갱신
            if (_data != null)
                _currentMoveSpeed = _data.phase2MoveSpeed;

            // 모든 패턴 2페이즈 활성화
            foreach (var p in _patterns)
            {
                if (p == null) continue;
                p.UnlockPhase2();
            }

            Debug.Log("[BossWardenAI] 2페이즈 전환 — 이동 속도 / 패턴 강화 적용");
        }

        /// <summary>
        /// 보스 처치 수신.
        /// AI 완전 정지 + 컴포넌트 비활성.
        /// </summary>
        public void HandleDead()
        {
            _isStopped = true;
            SetArmsRecoveryVuln(false);
            InterruptCurrentPattern();

            enabled = false;
            Debug.Log("[BossWardenAI] 보스 처치 → AI 정지");
        }

        // ══════════════════════════════════════════════════════
        // 패턴 이벤트 핸들러
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// BossPatternBase.OnPatternGroggy 수신.
        /// BossWardenCore.EnterGroggy() 호출로 그로기 상태 전환 요청.
        ///
        /// [흐름]
        ///   패턴 Recovery 완료
        ///   → OnPatternGroggy 발행
        ///   → HandlePatternGroggy() 수신
        ///   → BossWardenCore.EnterGroggy()
        ///   → BossWardenCore.OnGroggyEnter 발행
        ///   → HandleGroggyEnter() 수신 → _isStopped = true
        /// </summary>
        private void HandlePatternGroggy()
        {
            if (_core != null)
                _core.EnterGroggy();
        }

        // ══════════════════════════════════════════════════════
        // 패턴 강제 중단 공용
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 현재 실행 중인 패턴을 안전하게 강제 중단한다.
        ///
        /// [순서 원칙]
        ///   1. Interrupt() → 패턴 내부 OnPatternEnd 발행 + 상태 초기화
        ///   2. StopCoroutine() → 코루틴 즉시 중단
        ///   3. CleanupPattern() → 참조 정리
        ///
        /// [null 안전]
        ///   _currentPattern 또는 _patternCoroutine 이 null 이면 해당 단계 스킵.
        /// </summary>
        private void InterruptCurrentPattern()
        {
            if (_currentPattern != null)
            {
                _currentPattern.Interrupt();
                _currentPattern = null;
            }

            if (_patternCoroutine != null)
            {
                StopCoroutine(_patternCoroutine);
                _patternCoroutine = null;
            }
        }

        // ══════════════════════════════════════════════════════
        // Gizmos — 에디터 시각화
        // ══════════════════════════════════════════════════════

        private void OnDrawGizmosSelected()
        {
            if (_data == null) return;

            // 패턴 발동 범위 시각화
            Gizmos.color = new Color(1f, 0.8f, 0f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, _data.patternRange);

            // 현재 방향 시각화
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(transform.position, (Vector3)_facingDir * 1.5f);

#if UNITY_EDITOR
            UnityEditor.Handles.Label(
                transform.position + Vector3.up * 1.5f,
                $"AI: {_currentState} | Stopped: {_isStopped}");
#endif
        }
    }
}