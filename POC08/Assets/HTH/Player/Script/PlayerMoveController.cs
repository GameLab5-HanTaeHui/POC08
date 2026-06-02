// ============================================================
// PlayerTopViewMover.cs  v1.0
// 탑뷰 플레이어 이동 핵심 컴포넌트
//
// [POC07 참고 스크립트]
//   PlayerMover.cs (횡스크롤 이동)
//   → 수직 이동(점프/중력/Gravity) 제거
//   → 1D 수평 이동 → 2D Vector2 8방향 이동으로 전환
//   → Rigidbody2D.linearVelocity 직접 제어 (X/Y 평면)
//   → 대시 방향: 입력 방향 기준 (입력 없으면 마지막 이동 방향)
//   → DOTween 스케일 피드백 (스프라이트 스쿼시 / 대시 펀치)
//
// [이동 처리 방식]
//   Rigidbody2D.linearVelocity 직접 설정.
//   MoveAcceleration > 0: 가속/감속 보간.
//   MoveAcceleration = 0: 즉시 최고속도 (권장: 탑뷰 액션).
//
// [대시 처리]
//   DashDuration 동안 DashSpeed 로 고정 이동.
//   대시 중 입력 이동 차단 (Rigidbody 에 대시 속도만 적용).
//   쿨타임 / 충전 횟수 관리.
//
// [DOTween 피드백 — Sprite Sheet 없이 역동적 표현]
//   대시 시작: PunchScale (DOTween)
//   이동 중  : 방향별 스쿼시 (Squash & Stretch)
//
// [방향 표현]
//   SpriteRenderer.flipX 로 좌우 반전.
//   탑뷰에서는 이동 방향으로 Sprite 회전 (선택 사항, 인스펙터 토글).
//
// [요구 컴포넌트]
//   Rigidbody2D  (GravityScale = 0 필수)
//   SpriteRenderer
//   PlayerInputHandler (씬 내 존재 필요)
//
// [네임스페이스]
//   namespace : SEAL
// ============================================================

using System;
using System.Collections;
using UnityEngine;
using DG.Tweening;

namespace SEAL
{
    /// <summary>
    /// 탑뷰 플레이어 이동 핵심 컴포넌트. (v1.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [외부 API 사용 예시]
    ///
    ///   // 이동 차단 (봉인 집행 상태 진입 시)
    ///   _mover.SetMoveLocked(true);
    ///
    ///   // 현재 바라보는 방향 읽기
    ///   Vector2 dir = _mover.FacingDirection;
    ///
    ///   // 대시 중 여부 확인
    ///   bool isDashing = _mover.IsDashing;
    ///
    /// [Rigidbody2D 설정]
    ///   GravityScale = 0 (탑뷰 — 중력 없음)
    ///   Collision Detection = Continuous
    ///   Freeze Rotation Z = true
    /// ────────────────────────────────────────────────────
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(SpriteRenderer))]
    public class PlayerMoveController : MonoBehaviour
    {
        // ──────────────────────────────────────────
        // Inspector — 데이터 연결
        // ──────────────────────────────────────────

        [Header("── 데이터 SO ──────────────────────")]

        /// <summary>
        /// 이동 수치 ScriptableObject.
        /// MoveSpeed / DashSpeed / DashDuration 등 포함.
        /// 미연결 시 컴포넌트 비활성화.
        /// </summary>
        [Tooltip("이동 수치 SO. PlayerTopViewDataSO 연결 필수.")]
        [SerializeField] private PlayerDataSO _data;

        // ──────────────────────────────────────────
        // Inspector — 방향 표현
        // ──────────────────────────────────────────

        [Header("── 방향 표현 ──────────────────────")]

        /// <summary>
        /// 이동 방향으로 Sprite 를 회전시킬지 여부.
        /// true: 이동 방향으로 오브젝트 회전 (8방향 회전).
        /// false: 좌우(X)만 flipX 로 반전 (기본 탑뷰 스타일).
        /// </summary>
        [Tooltip("이동 방향으로 오브젝트 Z 회전. false=좌우만 flipX 반전.")]
        [SerializeField] private bool _rotateTowardsMoveDirection = false;

        /// <summary>
        /// 방향 전환 시 Sprite 회전 보간 속도.
        /// _rotateTowardsMoveDirection = true 일 때만 적용.
        /// </summary>
        [Tooltip("방향 전환 회전 보간 속도. 높을수록 즉시 회전.")]
        [SerializeField] private float _rotationSpeed = 720f;

        // ──────────────────────────────────────────
        // Inspector — 비주얼 연결
        // ──────────────────────────────────────────

        [Header("── 비주얼 연결 ──────────────────────")]

        /// <summary>
        /// 스쿼시/스트레치 대상 Transform.
        /// 플레이어 Visual 자식 오브젝트 연결.
        /// null 이면 자신 transform 사용.
        /// </summary>
        [Tooltip("스쿼시/스트레치 대상 Visual 오브젝트. null=자신 transform.")]
        [SerializeField] private Transform _visualTransform;

        // ──────────────────────────────────────────
        // 컴포넌트 참조 (런타임)
        // ──────────────────────────────────────────

        /// <summary>
        /// Rigidbody2D. Awake 에서 자동 취득.
        /// GravityScale = 0 설정 필수.
        /// </summary>
        private Rigidbody2D _rigid2D;

        /// <summary>
        /// SpriteRenderer. Awake 에서 자동 취득.
        /// flipX 좌우 반전에 사용.
        /// </summary>
        private SpriteRenderer _spriteRenderer;

        // ──────────────────────────────────────────
        // 이동 상태
        // ──────────────────────────────────────────

        /// <summary>
        /// 현재 이동 입력 벡터. PlayerInputHandler.OnMove 콜백으로 수신.
        /// FixedUpdate 에서 물리 이동에 사용.
        /// </summary>
        private Vector2 _moveInput;

        /// <summary>
        /// 현재 실제 이동 속도 벡터.
        /// 가속/감속이 있을 경우 목표 속도로 보간됨.
        /// </summary>
        private Vector2 _currentVelocity;

        /// <summary>
        /// 마지막으로 이동한 방향. 대시 방향 결정에 사용.
        /// 입력이 없어도 유지됨.
        /// </summary>
        private Vector2 _lastMoveDirection = Vector2.right;

        /// <summary>
        /// 현재 바라보는 방향 벡터.
        /// 무기 공격 방향, 투사체 방향 결정에 사용.
        /// 기본값: 오른쪽.
        /// </summary>
        private Vector2 _facingDirection = Vector2.right;

        /// <summary>
        /// 이동 잠금 여부.
        /// true: 입력 이동 차단 (대시 중, 봉인 집행 중 등).
        /// </summary>
        private bool _isMoveLocked;

        // ──────────────────────────────────────────
        // 대시 상태
        // ──────────────────────────────────────────

        /// <summary>
        /// 현재 대시 중 여부.
        /// true: DashDuration 동안 대시 속도로 이동.
        /// </summary>
        private bool _isDashing;

        /// <summary>
        /// 현재 남은 대시 충전 횟수.
        /// 대시 시 1 감소, 쿨타임 후 1 회복.
        /// </summary>
        private int _remainingDashCount;

        /// <summary>
        /// 대시 쿨타임 진행 코루틴 참조.
        /// 중복 실행 방지에 사용.
        /// </summary>
        private Coroutine _dashCooldownCoroutine;

        /// <summary>
        /// 대시 실행 코루틴 참조.
        /// 대시 강제 중단 시 StopCoroutine 에 사용.
        /// </summary>
        private Coroutine _dashCoroutine;

        // ──────────────────────────────────────────
        // DOTween 참조 (중복 방지)
        // ──────────────────────────────────────────

        /// <summary>
        /// 이동 방향 스쿼시 Tween 참조.
        /// 새 이동 입력 시 이전 tween Kill 후 재실행.
        /// </summary>
        private Tweener _squashTween;

        // ──────────────────────────────────────────
        // 이벤트
        // ──────────────────────────────────────────

        /// <summary>
        /// 대시 시작 시 1회 발행.
        /// UI 대시 아이콘 업데이트, 오디오 등에서 구독.
        /// </summary>
        public event Action OnDashStarted;

        /// <summary>
        /// 대시 종료 시 1회 발행.
        /// </summary>
        public event Action OnDashEnded;

        /// <summary>
        /// 바라보는 방향이 바뀔 때 1회 발행.
        /// 파라미터: 새 방향 벡터.
        /// 무기 피벗 방향 전환 등에서 구독.
        /// </summary>
        public event Action<Vector2> OnFacingChanged;

        // ──────────────────────────────────────────
        // 프로퍼티
        // ──────────────────────────────────────────

        /// <summary>
        /// 현재 바라보는 방향 벡터.
        /// 무기 공격 방향, 투사체 발사 방향 등에 사용.
        /// </summary>
        public Vector2 FacingDirection => _facingDirection;

        /// <summary> 현재 대시 중 여부. </summary>
        public bool IsDashing => _isDashing;

        /// <summary> 현재 이동 중 여부. </summary>
        public bool IsMoving => _moveInput.sqrMagnitude > 0.01f;

        /// <summary>
        /// 현재 남은 대시 충전 횟수.
        /// UI 대시 아이콘 표시에 사용.
        /// </summary>
        public int RemainingDashCount => _remainingDashCount;

        /// <summary> 연결된 데이터 SO. 외부 수치 읽기용. </summary>
        public PlayerDataSO Data => _data;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            // ── 컴포넌트 취득 ──────────────────────
            _rigid2D = GetComponent<Rigidbody2D>();
            _spriteRenderer = GetComponent<SpriteRenderer>();

            // Visual Transform 미설정 시 자신 transform 사용
            if (_visualTransform == null)
                _visualTransform = transform;

            // ── 데이터 유효성 확인 ──────────────────────
            if (_data == null)
            {
                Debug.LogError("[PlayerTopViewMover] PlayerTopViewDataSO 가 연결되지 않았습니다.");
                enabled = false;
                return;
            }

            // ── Rigidbody2D 탑뷰 설정 ──────────────────────
            // 탑뷰는 중력이 없어야 함
            _rigid2D.gravityScale = 0f;
            _rigid2D.freezeRotation = true; // 물리 충돌로 인한 Z축 회전 방지

            // ── 대시 충전 초기화 ──────────────────────
            _remainingDashCount = _data.MaxDashCount;
        }

        private void Start()
        {
            // ── InputHandler 이벤트 구독 ──────────────────────
            // Start 에서 구독 (Awake 실행 순서 보장)
            if (PlayerInputHandler.Instance == null)
            {
                Debug.LogError("[PlayerTopViewMover] PlayerInputHandler 가 씬에 없습니다.");
                enabled = false;
                return;
            }

            PlayerInputHandler.Instance.OnMove += HandleMoveInput;
            PlayerInputHandler.Instance.OnDash += HandleDashInput;
        }

        private void OnDestroy()
        {
            // ── 이벤트 구독 해제 (메모리 누수 방지) ──────────────────────
            if (PlayerInputHandler.Instance != null)
            {
                PlayerInputHandler.Instance.OnMove -= HandleMoveInput;
                PlayerInputHandler.Instance.OnDash -= HandleDashInput;
            }

            // DOTween Kill (오브젝트 파괴 시 정리)
            _squashTween?.Kill();
            DOTween.Kill(_visualTransform);
        }

        private void FixedUpdate()
        {
            // 대시 중에는 이동 물리 적용 안 함 (대시 코루틴이 직접 처리)
            if (_isDashing) return;

            ApplyMovement();
        }

        private void Update()
        {
            // 방향 / 회전 업데이트 (렌더링과 동기화 위해 Update 에서 처리)
            UpdateFacingDirection();
        }

        // ══════════════════════════════════════════════════════
        // 이동 처리
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// PlayerInputHandler.OnMove 콜백.
        /// 이동 입력 벡터를 수신하여 _moveInput 에 저장.
        ///
        /// [정규화 처리]
        ///   _data.NormalizeMovement = true 시 대각선 속도 보정.
        ///   예: (1, 1) → (0.707, 0.707) — 대각선도 동일 속도.
        /// </summary>
        /// <param name="input">PlayerInputHandler 에서 전달된 입력 벡터.</param>
        private void HandleMoveInput(Vector2 input)
        {
            // 이동 잠금 시 강제 zero
            if (_isMoveLocked)
            {
                _moveInput = Vector2.zero;
                return;
            }

            // 대각선 속도 정규화 (8방향 동일 속도 보장)
            if (_data.NormalizeMovement && input.sqrMagnitude > 1f)
                input = input.normalized;

            _moveInput = input;

            // 이동 방향 저장 (대시 방향 결정용)
            if (input.sqrMagnitude > 0.01f)
                _lastMoveDirection = input.normalized;
        }

        /// <summary>
        /// FixedUpdate 에서 호출. 실제 Rigidbody2D 속도를 적용한다.
        ///
        /// [가속도 처리]
        ///   _data.MoveAcceleration > 0: Vector2.MoveTowards 로 보간.
        ///   _data.MoveAcceleration = 0: 즉시 최고속도 (탑뷰 액션 권장).
        ///
        /// [감속 처리]
        ///   입력 없을 때 MoveDeceleration 으로 0 으로 수렴.
        /// </summary>
        private void ApplyMovement()
        {
            Vector2 targetVelocity = _moveInput * _data.MoveSpeed;

            if (_data.MoveAcceleration > 0f)
            {
                // 가속/감속 보간
                float rate = (_moveInput.sqrMagnitude > 0.01f)
                    ? _data.MoveAcceleration
                    : _data.MoveDeceleration;

                _currentVelocity = Vector2.MoveTowards(
                    _currentVelocity,
                    targetVelocity,
                    rate * Time.fixedDeltaTime);
            }
            else
            {
                // 즉시 적용 (가속 없음)
                _currentVelocity = targetVelocity;
            }

            _rigid2D.linearVelocity = _currentVelocity;
        }

        // ══════════════════════════════════════════════════════
        // 방향 처리
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Update 에서 호출. 이동 입력 방향에 따라 Sprite 방향을 갱신한다.
        ///
        /// [_rotateTowardsMoveDirection = false (기본)]
        ///   X 방향에 따라 SpriteRenderer.flipX 반전.
        ///   탑뷰에서 좌/우 방향으로 스프라이트 플립.
        ///
        /// [_rotateTowardsMoveDirection = true]
        ///   이동 방향 각도로 transform.rotation 보간.
        ///   8방향 회전 표현.
        /// </summary>
        private void UpdateFacingDirection()
        {
            if (_moveInput.sqrMagnitude < 0.01f) return;

            Vector2 newFacing = _moveInput.normalized;

            // 방향이 바뀌었을 때만 처리
            if (newFacing == _facingDirection) return;

            _facingDirection = newFacing;
            OnFacingChanged?.Invoke(_facingDirection);

            if (_rotateTowardsMoveDirection)
            {
                // 이동 방향 각도 계산 (오른쪽=0°, 반시계 증가)
                float angle = Mathf.Atan2(_facingDirection.y, _facingDirection.x) * Mathf.Rad2Deg;
                Quaternion targetRot = Quaternion.Euler(0f, 0f, angle - 90f); // -90°: 스프라이트가 위를 향할 경우 보정

                // 보간 회전 (Slerp)
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    targetRot,
                    _rotationSpeed * Time.deltaTime);
            }
            else
            {
                // 좌우만 flipX 반전
                // X 방향이 음수(왼쪽)이면 flipX = true
                if (_facingDirection.x != 0f)
                    _spriteRenderer.flipX = _facingDirection.x < 0f;
            }

            // 이동 방향 스쿼시 피드백
            PlayMoveSquash();
        }

        // ══════════════════════════════════════════════════════
        // 대시 처리
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// PlayerInputHandler.OnDash 콜백.
        /// 대시 가능 조건 확인 후 대시 코루틴 시작.
        ///
        /// [대시 가능 조건]
        ///   _remainingDashCount > 0 (충전 횟수 남음)
        ///   !_isDashing (중복 대시 방지)
        /// </summary>
        private void HandleDashInput()
        {
            if (_isDashing) return;
            if (_remainingDashCount <= 0) return;

            // 대시 방향 결정: 현재 입력 → 없으면 마지막 이동 방향
            Vector2 dashDir = _moveInput.sqrMagnitude > 0.01f
                ? _moveInput.normalized
                : _lastMoveDirection;

            // 대시 실행
            if (_dashCoroutine != null)
                StopCoroutine(_dashCoroutine);

            _dashCoroutine = StartCoroutine(DashRoutine(dashDir));
        }

        /// <summary>
        /// 대시 코루틴.
        /// DashDuration 동안 DashSpeed 로 고정 이동 후 종료.
        ///
        /// [대시 흐름]
        ///   1. 상태 진입 → _isDashing = true
        ///   2. 이동 잠금 (일반 이동 차단)
        ///   3. DashDuration 동안 linearVelocity 고정
        ///   4. 종료 → 이동 잠금 해제, 쿨타임 시작
        /// </summary>
        /// <param name="dashDir">대시 방향 (정규화된 Vector2).</param>
        private IEnumerator DashRoutine(Vector2 dashDir)
        {
            // ── 대시 시작 ──────────────────────
            _isDashing = true;
            _remainingDashCount--;

            // 이동 잠금 (대시 중 일반 이동 입력 무시)
            bool wasMoveLocked = _isMoveLocked;
            _isMoveLocked = true;

            OnDashStarted?.Invoke();

            // DOTween 대시 시작 스케일 펀치
            PlayDashPunch();

            // ── 대시 이동 (DashDuration 동안) ──────────────────────
            float elapsed = 0f;
            Vector2 dashVelocity = dashDir * _data.DashSpeed;

            while (elapsed < _data.DashDuration)
            {
                _rigid2D.linearVelocity = dashVelocity;
                elapsed += Time.fixedDeltaTime;
                yield return new WaitForFixedUpdate();
            }

            // ── 대시 종료 ──────────────────────
            _rigid2D.linearVelocity = Vector2.zero; // 대시 후 즉시 멈춤
            _isDashing = false;
            _isMoveLocked = wasMoveLocked; // 이전 잠금 상태 복원

            OnDashEnded?.Invoke();

            // ── 쿨타임 시작 ──────────────────────
            if (_dashCooldownCoroutine != null)
                StopCoroutine(_dashCooldownCoroutine);

            _dashCooldownCoroutine = StartCoroutine(DashCooldownRoutine());

            _dashCoroutine = null;
        }

        /// <summary>
        /// 대시 쿨타임 코루틴.
        /// DashCooldown 시간 후 충전 횟수 1 회복.
        /// MaxDashCount 미만일 때만 회복.
        /// </summary>
        private IEnumerator DashCooldownRoutine()
        {
            yield return new WaitForSeconds(_data.DashCooldown);

            if (_remainingDashCount < _data.MaxDashCount)
                _remainingDashCount++;

            _dashCooldownCoroutine = null;
        }

        // ══════════════════════════════════════════════════════
        // DOTween 피드백
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 대시 시작 시 스케일 펀치 연출 (DOTween).
        /// _data.DashPunchScale > 0 일 때 실행.
        ///
        /// [연출]
        ///   대시 방향으로 약간 늘어났다 복귀하는 효과.
        ///   스프라이트 시트 없이 역동적인 대시감 표현.
        /// </summary>
        private void PlayDashPunch()
        {
            if (_data.DashPunchScale <= 0f || _visualTransform == null) return;

            // 기존 Tween Kill 후 재실행
            DOTween.Kill(_visualTransform, complete: false);
            _visualTransform.localScale = Vector3.one;

            _visualTransform.DOPunchScale(
                punch: Vector3.one * _data.DashPunchScale,
                duration: _data.DashPunchDuration,
                vibrato: 3,
                elasticity: 0.5f)
                .SetUpdate(UpdateType.Normal);
        }

        /// <summary>
        /// 이동 방향 변경 시 스쿼시/스트레치 연출 (DOTween).
        /// _data.MoveSquashAmount > 0 일 때 실행.
        ///
        /// [연출]
        ///   이동 방향으로 약간 늘어나고, 수직 방향으로 눌리는 효과.
        ///   Squash and Stretch 원리. 탑뷰 이동에 생동감 부여.
        /// </summary>
        private void PlayMoveSquash()
        {
            if (_data.MoveSquashAmount <= 0f || _visualTransform == null) return;

            float stretch = _data.MoveSquashAmount;

            // 이전 스쿼시 Tween 종료
            _squashTween?.Kill(complete: true);

            // 이동 방향 축: X 이동 → X 늘어남, Y이동 → Y 늘어남
            Vector3 targetScale = Vector3.one;
            if (Mathf.Abs(_moveInput.x) > Mathf.Abs(_moveInput.y))
                targetScale = new Vector3(1f + stretch, 1f - stretch * 0.5f, 1f);
            else if (Mathf.Abs(_moveInput.y) > Mathf.Abs(_moveInput.x))
                targetScale = new Vector3(1f - stretch * 0.5f, 1f + stretch, 1f);

            _squashTween = _visualTransform
                .DOScale(targetScale, 0.08f)
                .SetEase(Ease.OutQuad)
                .OnComplete(() =>
                {
                    // 원래 크기로 복귀
                    _squashTween = _visualTransform
                        .DOScale(Vector3.one, 0.1f)
                        .SetEase(Ease.InOutSine);
                });
        }

        // ══════════════════════════════════════════════════════
        // 외부 API
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 이동 잠금 상태를 설정한다.
        /// true: 이동 차단 (Rigidbody velocity = 0).
        /// false: 이동 허용.
        ///
        /// [사용 예시]
        ///   봉인 집행 상태 Enter → SetMoveLocked(true)
        ///   봉인 집행 상태 Exit  → SetMoveLocked(false)
        /// </summary>
        /// <param name="locked">true = 이동 차단.</param>
        public void SetMoveLocked(bool locked)
        {
            _isMoveLocked = locked;

            // 즉시 정지 (잠금 진입 시 velocity 초기화)
            if (locked)
            {
                _moveInput = Vector2.zero;
                _rigid2D.linearVelocity = Vector2.zero;
                _currentVelocity = Vector2.zero;
            }
        }

        /// <summary>
        /// 대시를 강제로 중단한다.
        /// 피격 경직, 컷씬 진입 등에서 호출.
        /// </summary>
        public void ForceStopDash()
        {
            if (!_isDashing) return;

            if (_dashCoroutine != null)
            {
                StopCoroutine(_dashCoroutine);
                _dashCoroutine = null;
            }

            _isDashing = false;
            _isMoveLocked = false;
            _rigid2D.linearVelocity = Vector2.zero;

            OnDashEnded?.Invoke();
        }

        /// <summary>
        /// 대시 충전 횟수를 최대로 즉시 회복한다.
        /// 어빌리티 열쇠(반전 열쇠: 봉인 집행 성공 시 대시 회복) 등에서 호출.
        /// </summary>
        public void RestoreAllDash()
        {
            _remainingDashCount = _data.MaxDashCount;
        }

        // ══════════════════════════════════════════════════════
        // Gizmos — 에디터 디버그 표시
        // ══════════════════════════════════════════════════════

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // 바라보는 방향 표시 (씬 뷰)
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(transform.position, (Vector3)_facingDirection * 1.2f);

            // 대시 방향 표시
            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(transform.position, (Vector3)_lastMoveDirection * 0.8f);

            // 대시 충전 수 표시
            UnityEditor.Handles.color = Color.white;
            UnityEditor.Handles.Label(
                transform.position + Vector3.up * 1.5f,
                $"대시: {_remainingDashCount}/{(_data != null ? _data.MaxDashCount : 0)} | " +
                $"대시중: {_isDashing} | 잠금: {_isMoveLocked}");
        }
#endif
    }
}