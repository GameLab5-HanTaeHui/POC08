// ============================================================
// PlayerMoveController.cs  v1.1
// 탑뷰 플레이어 이동 핵심 컴포넌트
//
// [v1.1 변경 — 공격 이동 모드 추가]
//
//   [추가 기능: SetAttackMove(bool, Vector2)]
//     공격 시작 → PlayerAttackController 가 SetAttackMove(true, attackDir) 호출
//     공격 종료 → PlayerAttackController 가 SetAttackMove(false, zero) 호출
//
//   [공격 이동 규칙]
//     _isAttackMoving = true 구간:
//       WASD 입력 있음 → WASD 방향으로 AttackMoveSpeed 전진
//       WASD 입력 없음 → 공격 방향(_attackMoveDir)으로 AttackMoveSpeed 전진
//     일반 WASD 이동(ApplyMovement)은 이 구간 동안 velocity 덮어쓰기 금지
//
//   [설계 원칙]
//     SetMoveLocked 호출 없음 — 공격 중에도 WASD 입력은 살아있음
//     ApplyMovement 에서 _isAttackMoving 체크 후 공격 이동 분기
//     대시 중(_isDashing)에는 공격 이동 무시 (대시 최우선)
//     Seal(_isMoveLocked)에는 공격 이동 무시
//
//   [콤보 방향 전환]
//     SetAttackMove(true, newDir) 을 콤보마다 호출하여 전진 방향 갱신
//     WASD 입력이 있으면 newDir 무시하고 WASD 방향 우선
//
// [v1.0 유지 사항]
//   Rigidbody2D.linearVelocity 직접 제어
//   대시: DashDuration 동안 DashSpeed 고정 이동
//   DOTween 스쿼시/대시 펀치 피드백
//   마우스 방향 기반 FacingDirection
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
    /// 탑뷰 플레이어 이동 핵심 컴포넌트. (v1.1)
    ///
    /// ────────────────────────────────────────────────────
    /// [외부 API]
    ///   SetMoveLocked(bool)              → Seal 전용 이동 차단
    ///   SetAttackMove(bool, Vector2)     → 공격 이동 모드 on/off
    ///   FacingDirection                  → 현재 마우스 방향
    ///   IsDashing                        → 대시 중 여부
    ///
    /// [공격 이동 흐름]
    ///   공격 시작 → SetAttackMove(true, attackDir)
    ///   공격 중   → WASD 있으면 WASD방향 / 없으면 attackDir 로 AttackMoveSpeed 전진
    ///   공격 종료 → SetAttackMove(false, zero) → 일반 WASD 이동 복귀
    ///
    /// [Rigidbody2D 설정]
    ///   GravityScale = 0 / Collision Detection = Continuous / Freeze Rotation Z = true
    /// ────────────────────────────────────────────────────
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(SpriteRenderer))]
    public class PlayerMoveController : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── 데이터 SO ──────────────────────")]

        /// <summary>
        /// 이동 수치 ScriptableObject.
        /// MoveSpeed / DashSpeed / DashDuration 등 포함.
        /// </summary>
        [Tooltip("이동 수치 SO. PlayerDataSO 연결 필수.")]
        [SerializeField] private PlayerDataSO _data;

        [Header("── 공격 이동 ──────────────────────")]

        /// <summary>
        /// 공격 중 이동 속도.
        /// PlayerAttackDataSO.AttackMoveSpeed 를 직접 참조하지 않고
        /// PlayerAttackController 가 SetAttackMove 호출 시 전달.
        /// 여기서는 폴백용으로 인스펙터에서 조절 가능.
        /// </summary>
        [Tooltip("공격 중 전진 속도. PlayerAttackDataSO.AttackMoveSpeed 와 동기화 권장.")]
        [SerializeField] private float _attackMoveSpeed = 3f;

        [Header("── 방향 표현 ──────────────────────")]

        /// <summary>
        /// 이동 방향으로 Sprite 를 회전시킬지 여부.
        /// false: 좌우만 flipX 반전 (엔터 더 건전 / 세피리아 스타일).
        /// </summary>
        [Tooltip("이동 방향으로 오브젝트 Z 회전. false=좌우만 flipX 반전.")]
        [SerializeField] private bool _rotateTowardsMoveDirection = false;

        [Tooltip("방향 전환 회전 보간 속도.")]
        [SerializeField] private float _rotationSpeed = 720f;

        [Header("── 비주얼 연결 ──────────────────────")]

        /// <summary>스쿼시/스트레치 대상 Visual Transform. null 이면 자신 사용.</summary>
        [Tooltip("스쿼시/스트레치 대상 Visual 오브젝트. null=자신.")]
        [SerializeField] private Transform _visualTransform;

        // ══════════════════════════════════════════════════════
        // 컴포넌트 참조
        // ══════════════════════════════════════════════════════

        /// <summary>Rigidbody2D. Awake 에서 취득. GravityScale = 0 필수.</summary>
        private Rigidbody2D _rigid2D;

        /// <summary>SpriteRenderer. flipX 좌우 반전에 사용.</summary>
        private SpriteRenderer _spriteRenderer;

        /// <summary>
        /// 무기 스윙 컨트롤러. UpdateFacingDirection 에서 IsSwinging 체크.
        /// 공격 중 flipX/회전 변경 금지를 위해 참조.
        /// </summary>
        private PlayerWeaponSwingController _swingController;

        // ══════════════════════════════════════════════════════
        // 이동 상태
        // ══════════════════════════════════════════════════════

        /// <summary>현재 WASD 입력 벡터. OnMove 콜백으로 수신.</summary>
        private Vector2 _moveInput;

        /// <summary>현재 실제 이동 속도 벡터. 가속/감속 보간용.</summary>
        private Vector2 _currentVelocity;

        /// <summary>마지막 이동 방향. 대시 방향 결정에 사용.</summary>
        private Vector2 _lastMoveDirection = Vector2.right;

        /// <summary>현재 바라보는 방향. 마우스 기준. 무기 공격 방향에 사용.</summary>
        private Vector2 _facingDirection = Vector2.right;

        /// <summary>
        /// Seal 이동 잠금 여부.
        /// true: 모든 이동 차단. SetMoveLocked(true) 로 진입.
        /// </summary>
        private bool _isMoveLocked;

        // ══════════════════════════════════════════════════════
        // 공격 이동 상태 (v1.1 추가)
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 공격 이동 모드 여부.
        /// true: 공격 중 전진 이동 적용.
        /// PlayerAttackController.SetAttackMove(true) 로 진입.
        /// PlayerAttackController.SetAttackMove(false) 로 해제.
        /// </summary>
        private bool _isAttackMoving;

        /// <summary>
        /// 공격 전진 방향.
        /// SetAttackMove(true, dir) 에서 설정.
        /// WASD 입력이 없을 때 이 방향으로 전진.
        /// 콤보 연결 시 SetAttackMove(true, newDir) 로 갱신.
        /// </summary>
        private Vector2 _attackMoveDir;

        /// <summary>
        /// 공격 이동 속도 (런타임).
        /// SetAttackMove(true, dir, speed) 로 설정.
        /// </summary>
        private float _attackMoveSpeedRuntime;

        // ══════════════════════════════════════════════════════
        // 대시 상태
        // ══════════════════════════════════════════════════════

        /// <summary>현재 대시 중 여부.</summary>
        private bool _isDashing;

        /// <summary>남은 대시 충전 횟수.</summary>
        private int _remainingDashCount;

        /// <summary>대시 쿨타임 코루틴 참조.</summary>
        private Coroutine _dashCooldownCoroutine;

        /// <summary>대시 실행 코루틴 참조.</summary>
        private Coroutine _dashCoroutine;

        // ══════════════════════════════════════════════════════
        // DOTween 참조
        // ══════════════════════════════════════════════════════

        /// <summary>이동 스쿼시 Tween 참조. 중복 방지용.</summary>
        private Tweener _squashTween;

        // ══════════════════════════════════════════════════════
        // 이벤트
        // ══════════════════════════════════════════════════════

        /// <summary>대시 시작 시 1회 발행.</summary>
        public event Action OnDashStarted;

        /// <summary>대시 종료 시 1회 발행.</summary>
        public event Action OnDashEnded;

        /// <summary>바라보는 방향이 바뀔 때 발행. 파라미터: 새 방향.</summary>
        public event Action<Vector2> OnFacingChanged;

        // ══════════════════════════════════════════════════════
        // 프로퍼티
        // ══════════════════════════════════════════════════════

        /// <summary>현재 마우스 방향 벡터. 무기/투사체 방향에 사용.</summary>
        public Vector2 FacingDirection => _facingDirection;

        /// <summary>현재 대시 중 여부.</summary>
        public bool IsDashing => _isDashing;

        /// <summary>현재 WASD 이동 중 여부.</summary>
        public bool IsMoving => _moveInput.sqrMagnitude > 0.01f;

        /// <summary>현재 공격 이동 모드 여부.</summary>
        public bool IsAttackMoving => _isAttackMoving;

        /// <summary>남은 대시 충전 횟수.</summary>
        public int RemainingDashCount => _remainingDashCount;

        /// <summary>연결된 데이터 SO.</summary>
        public PlayerDataSO Data => _data;

        /// <summary>현재 Rigidbody2D velocity.</summary>
        public Vector2 CurrentVelocity => _currentVelocity;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            _rigid2D = GetComponent<Rigidbody2D>();
            _spriteRenderer = GetComponent<SpriteRenderer>();
            _swingController = GetComponent<PlayerWeaponSwingController>();

            if (_visualTransform == null)
                _visualTransform = transform;

            if (_data == null)
            {
                Debug.LogError("[PlayerMoveController] PlayerDataSO 미연결.");
                enabled = false;
                return;
            }

            // 탑뷰 Rigidbody2D 설정
            _rigid2D.gravityScale = 0f;
            _rigid2D.freezeRotation = true;

            _remainingDashCount = _data.MaxDashCount;
        }

        private void Start()
        {
            if (PlayerInputHandler.Instance == null)
            {
                Debug.LogError("[PlayerMoveController] PlayerInputHandler 없음.");
                enabled = false;
                return;
            }

            PlayerInputHandler.Instance.OnMove += HandleMoveInput;
            PlayerInputHandler.Instance.OnDash += HandleDashInput;
        }

        private void OnDestroy()
        {
            if (PlayerInputHandler.Instance != null)
            {
                PlayerInputHandler.Instance.OnMove -= HandleMoveInput;
                PlayerInputHandler.Instance.OnDash -= HandleDashInput;
            }

            _squashTween?.Kill();
            DOTween.Kill(_visualTransform);
        }

        private void FixedUpdate()
        {
            if (_isDashing) return;
            ApplyMovement();
        }

        private void Update()
        {
            UpdateFacingDirection();
        }

        // ══════════════════════════════════════════════════════
        // 이동 처리
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// PlayerInputHandler.OnMove 콜백.
        /// _isMoveLocked 시 zero 강제. 공격 이동 중에도 입력은 살아있음.
        /// </summary>
        private void HandleMoveInput(Vector2 input)
        {
            // Seal 잠금 시 강제 zero
            if (_isMoveLocked)
            {
                _moveInput = Vector2.zero;
                return;
            }

            // 대각선 속도 정규화
            if (_data.NormalizeMovement && input.sqrMagnitude > 1f)
                input = input.normalized;

            _moveInput = input;

            // 이동 방향 저장 (대시 방향 결정용)
            if (input.sqrMagnitude > 0.01f)
                _lastMoveDirection = input.normalized;
        }

        /// <summary>
        /// FixedUpdate 에서 호출. Rigidbody2D velocity 적용.
        ///
        /// [v1.1 공격 이동 분기]
        ///   _isAttackMoving = true:
        ///     WASD 입력 있음 → rawInput 방향으로 AttackMoveSpeed
        ///     WASD 입력 없음 → _attackMoveDir 방향으로 AttackMoveSpeed
        ///   _isAttackMoving = false:
        ///     일반 WASD 이동 (MoveSpeed, 가속/감속 보간)
        ///
        /// [우선순위]
        ///   Seal(_isMoveLocked) > 대시(_isDashing) > 공격이동 > 일반이동
        /// </summary>
        private void ApplyMovement()
        {
            // Seal 잠금 시 velocity 건드리지 않음
            if (_isMoveLocked) return;

            // ── 공격 이동 모드 ──────────────────────
            if (_isAttackMoving)
            {
                // 실제 눌린 WASD 읽기 (잠금 무관)
                Vector2 rawInput = PlayerInputHandler.Instance != null
                    ? PlayerInputHandler.Instance.MoveInput
                    : Vector2.zero;

                // WASD 있으면 그 방향 / 없으면 공격 방향
                Vector2 moveDir = rawInput.sqrMagnitude > 0.01f
                    ? rawInput.normalized
                    : _attackMoveDir;

                _currentVelocity = moveDir * _attackMoveSpeedRuntime;
                _rigid2D.linearVelocity = _currentVelocity;
                return;
            }

            // ── 일반 WASD 이동 ──────────────────────
            Vector2 targetVelocity = _moveInput * _data.MoveSpeed;

            if (_data.MoveAcceleration > 0f)
            {
                float rate = _moveInput.sqrMagnitude > 0.01f
                    ? _data.MoveAcceleration
                    : _data.MoveDeceleration;

                _currentVelocity = Vector2.MoveTowards(
                    _currentVelocity,
                    targetVelocity,
                    rate * Time.fixedDeltaTime);
            }
            else
            {
                _currentVelocity = targetVelocity;
            }

            _rigid2D.linearVelocity = _currentVelocity;
        }

        // ══════════════════════════════════════════════════════
        // 방향 처리
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Update 에서 호출. 마우스 방향으로 FacingDirection 갱신.
        /// 스윙 중에는 OnFacingChanged 발행 금지 (DOTween 간섭 방지).
        /// </summary>
        private void UpdateFacingDirection()
        {
            if (PlayerInputHandler.Instance == null) return;

            Vector2 mouseWorld = PlayerInputHandler.Instance.MouseWorldPosition;
            Vector2 playerPos = (Vector2)transform.position;
            Vector2 toMouse = mouseWorld - playerPos;

            if (toMouse.sqrMagnitude < 0.01f) return;

            Vector2 newFacing = toMouse.normalized;
            if (newFacing == _facingDirection) return;

            _facingDirection = newFacing;

            // 스윙 중 → flipX/회전/WeaponPivot 변경 금지
            if (_swingController != null && _swingController.IsSwinging) return;

            OnFacingChanged?.Invoke(_facingDirection);

            if (_rotateTowardsMoveDirection)
            {
                float angle = Mathf.Atan2(_facingDirection.y, _facingDirection.x) * Mathf.Rad2Deg;
                Quaternion targetRot = Quaternion.Euler(0f, 0f, angle - 90f);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, targetRot, _rotationSpeed * Time.deltaTime);
            }
            else
            {
                if (_facingDirection.x != 0f)
                    _spriteRenderer.flipX = _facingDirection.x < 0f;
            }
        }

        // ══════════════════════════════════════════════════════
        // 대시 처리
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// PlayerInputHandler.OnDash 콜백.
        /// 실제 눌린 WASD 기준 대시 방향 결정.
        /// WASD 없으면 FacingDirection(마우스 방향)으로 대시.
        /// </summary>
        private void HandleDashInput()
        {
            if (_isDashing) return;
            if (_remainingDashCount <= 0) return;

            Vector2 wasdInput = PlayerInputHandler.Instance != null
                ? PlayerInputHandler.Instance.MoveInput
                : Vector2.zero;

            Vector2 dashDir = wasdInput.sqrMagnitude > 0.01f
                ? wasdInput.normalized
                : (_facingDirection.sqrMagnitude > 0.01f
                    ? _facingDirection
                    : _lastMoveDirection);

            if (_dashCoroutine != null)
                StopCoroutine(_dashCoroutine);

            _dashCoroutine = StartCoroutine(DashRoutine(dashDir));
        }

        /// <summary>
        /// 대시 코루틴. DashDuration 동안 DashSpeed 고정 이동.
        /// 대시 중 공격 이동 무시 (대시 최우선).
        /// </summary>
        private IEnumerator DashRoutine(Vector2 dashDir)
        {
            _isDashing = true;
            _remainingDashCount--;

            OnDashStarted?.Invoke();
            PlayDashPunch();

            float elapsed = 0f;
            Vector2 dashVelocity = dashDir * _data.DashSpeed;

            while (elapsed < _data.DashDuration)
            {
                _rigid2D.linearVelocity = dashVelocity;
                elapsed += Time.fixedDeltaTime;
                yield return new WaitForFixedUpdate();
            }

            _rigid2D.linearVelocity = Vector2.zero;
            _isDashing = false;

            OnDashEnded?.Invoke();

            if (_dashCooldownCoroutine != null)
                StopCoroutine(_dashCooldownCoroutine);
            _dashCooldownCoroutine = StartCoroutine(DashCooldownRoutine());

            _dashCoroutine = null;
        }

        /// <summary>대시 쿨타임 코루틴. DashCooldown 후 충전 1 회복.</summary>
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

        /// <summary>대시 시작 스케일 펀치 연출.</summary>
        private void PlayDashPunch()
        {
            if (_data.DashPunchScale <= 0f || _visualTransform == null) return;

            DOTween.Kill(_visualTransform, complete: false);
            _visualTransform.localScale = Vector3.one;

            _visualTransform.DOPunchScale(
                punch: Vector3.one * _data.DashPunchScale,
                duration: _data.DashPunchDuration,
                vibrato: 3,
                elasticity: 0.5f)
                .SetUpdate(UpdateType.Normal);
        }

        /// <summary>이동 방향 변경 스쿼시/스트레치 연출.</summary>
        private void PlayMoveSquash()
        {
            if (_data.MoveSquashAmount <= 0f || _visualTransform == null) return;

            float stretch = _data.MoveSquashAmount;
            _squashTween?.Kill(complete: true);

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
                    _squashTween = _visualTransform
                        .DOScale(Vector3.one, 0.1f)
                        .SetEase(Ease.InOutSine);
                });
        }

        // ══════════════════════════════════════════════════════
        // 외부 API
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Seal 이동 잠금 설정.
        /// true: 모든 이동 차단 (Seal 상태 전용).
        /// false: 이동 허용.
        /// </summary>
        public void SetMoveLocked(bool locked)
        {
            _isMoveLocked = locked;

            if (locked)
            {
                _moveInput = Vector2.zero;
                _currentVelocity = Vector2.zero;
                // 공격 이동도 함께 해제
                _isAttackMoving = false;
                _attackMoveDir = Vector2.zero;
            }
        }

        /// <summary>
        /// 공격 이동 모드 설정. (v1.1 신규)
        /// PlayerAttackController 에서 호출.
        ///
        /// [사용 흐름]
        ///   ComboRoutine 시작 → SetAttackMove(true, attackDir, attackMoveSpeed)
        ///   콤보 연결 시     → SetAttackMove(true, newDir, attackMoveSpeed) 갱신
        ///   ComboRoutine 종료 → SetAttackMove(false, zero, 0)
        ///   CancelAttack 시  → SetAttackMove(false, zero, 0)
        ///
        /// [이동 규칙]
        ///   WASD 입력 있음 → WASD 방향으로 speed 전진
        ///   WASD 입력 없음 → attackDir 방향으로 speed 전진
        /// </summary>
        /// <param name="active">true = 공격 이동 on / false = off.</param>
        /// <param name="attackDir">공격 전진 기본 방향 (정규화 Vector2).</param>
        /// <param name="speed">공격 이동 속도.</param>
        public void SetAttackMove(bool active, Vector2 attackDir, float speed = 0f)
        {
            _isAttackMoving = active;
            _attackMoveDir = active ? attackDir.normalized : Vector2.zero;
            _attackMoveSpeedRuntime = active ? (speed > 0f ? speed : _attackMoveSpeed) : 0f;

            // 공격 이동 해제 시 velocity 즉시 zero → 다음 프레임 WASD 이동으로 부드럽게 전환
            if (!active)
            {
                _currentVelocity = Vector2.zero;
                _rigid2D.linearVelocity = Vector2.zero;
            }

            Debug.Log($"[PlayerMoveController] 공격이동: {active} | 방향: {attackDir} | 속도: {speed}");
        }

        /// <summary>대시 강제 중단.</summary>
        public void ForceStopDash()
        {
            if (!_isDashing) return;

            if (_dashCoroutine != null)
            {
                StopCoroutine(_dashCoroutine);
                _dashCoroutine = null;
            }

            _isDashing = false;
            _rigid2D.linearVelocity = Vector2.zero;

            OnDashEnded?.Invoke();
        }

        /// <summary>대시 충전 횟수 최대로 즉시 회복.</summary>
        public void RestoreAllDash()
        {
            _remainingDashCount = _data.MaxDashCount;
        }

        // ══════════════════════════════════════════════════════
        // Gizmos
        // ══════════════════════════════════════════════════════

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // 마우스 방향 (파랑)
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(transform.position, (Vector3)_facingDirection * 1.2f);

            // 대시/이동 방향 (노랑)
            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(transform.position, (Vector3)_lastMoveDirection * 0.8f);

            // 공격 이동 방향 (빨강)
            if (_isAttackMoving)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawRay(transform.position, (Vector3)_attackMoveDir * 1.5f);
            }

            UnityEditor.Handles.color = Color.white;
            UnityEditor.Handles.Label(
                transform.position + Vector3.up * 1.5f,
                $"대시: {_remainingDashCount}/{(_data != null ? _data.MaxDashCount : 0)} | " +
                $"대시중: {_isDashing} | 잠금: {_isMoveLocked} | 공격이동: {_isAttackMoving}");
        }
#endif
    }
}