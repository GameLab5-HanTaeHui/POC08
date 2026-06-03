// ============================================================
// PlayerAttackController.cs  v2.1
// 플레이어 A키 공격 컨트롤러
//
// [v2.1 변경 — 히트박스 판정 경로 통합]
//   기존 두 경로 (OverlapCircle / Collider2D) 가 독립적으로 존재하던 구조를
//   PlayerAttackHitboxManager 단일 경로로 통합.
//
//   [제거]
//     _activeHitboxRadius  : OverlapCircle 반경 (사용 안 함)
//     _hitProcessed        : 중복 판정 방지 플래그 (HitboxManager 내부로 이전)
//     OnSwingHitCallback() : onHit 콜백 핸들러
//     ProcessHitCheck()    : OverlapCircle 판정 함수
//     Update() 판정 루프  : _activeHitboxRadius > 0 체크
//
//   [추가]
//     _hitboxManager 참조 (Awake 자동 탐색)
//     Start() 에서 _hitboxManager.OnHit 구독
//     HandleHitboxHit(Collider2D, float) : OnHit 수신 → OnHitTarget 발행
//
//   [PlaySwing / PlayChargeSwing 시그니처 변경]
//     기존: (combo, dir, Action<float> onHit)
//     변경: (combo, dir, float sealAmount)
//     → SwingController 내부에서 HitboxManager 직접 제어
//
// [v2.0 변경 — 무기 연출 분리]
//   PlayerWeaponSwingController 로 무기 연출 완전 위임.
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
    /// 플레이어 A키 공격 컨트롤러. (v2.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [외부 이벤트 구독 예시]
    ///   _atk.OnHitTarget += (hitPos, sealAmt) =>
    ///       sealGauge.AddGauge(target, sealAmt);
    ///
    /// [요구 컴포넌트]
    ///   PlayerMoveController        (FacingDirection 참조)
    ///   PlayerWeaponSwingController (무기 연출 위임)
    ///   PlayerInputHandler          (OnAttack / OnAttackReleased 구독)
    ///   PlayerAttackDataSO          (_data 연결 필수)
    /// ────────────────────────────────────────────────────
    /// </summary>
    [RequireComponent(typeof(PlayerMoveController))]
    [RequireComponent(typeof(PlayerWeaponSwingController))]
    public class PlayerAttackController : MonoBehaviour
    {
        // ──────────────────────────────────────────
        // Inspector — 데이터 SO
        // ──────────────────────────────────────────

        [Header("── 데이터 SO ──────────────────────")]

        /// <summary>
        /// 공격 수치 ScriptableObject.
        /// 콤보별 위치/회전/히트박스/봉인도 수치 포함.
        /// </summary>
        [Tooltip("공격 수치 SO. PlayerAttackDataSO 연결 필수.")]
        [SerializeField] private PlayerAttackDataSO _data;

        /// <summary>
        /// 플레이어 Visual Transform.
        /// 공격 전진(Lunge) 연출 대상.
        /// null 이면 자신 transform 사용.
        /// </summary>
        [Tooltip("공격 전진 Visual Transform. null=자신.")]
        [SerializeField] private Transform _visualTransform;

        // ──────────────────────────────────────────
        // 컴포넌트 참조
        // ──────────────────────────────────────────

        /// <summary>
        /// 이동 컨트롤러. 공격 방향(FacingDirection) 읽기 + 이동 잠금.
        /// </summary>
        private PlayerMoveController _moveController;

        /// <summary>
        /// 무기 스윙 연출 컨트롤러. 모든 DOTween 연출 위임.
        /// </summary>
        private PlayerWeaponSwingController _swingController;

        /// <summary>
        /// Rigidbody2D. 공격 전진 시 velocity 초기화에 사용.
        /// </summary>
        private Rigidbody2D _rigid2D;

        // ──────────────────────────────────────────
        // 콤보 상태
        // ──────────────────────────────────────────

        /// <summary>
        /// 현재 공격 처리 중 여부.
        /// true 동안 콤보 윈도우에서만 입력 수신.
        /// </summary>
        private bool _isAttacking;

        /// <summary>
        /// 현재 콤보 단계 (0=1콤보, 1=2콤보, 2=3콤보).
        /// MaxComboCount 도달 후 다음 공격 시 0으로 초기화.
        /// </summary>
        private int _currentCombo;

        /// <summary>
        /// 콤보 윈도우 열림 여부.
        /// true: 다음 공격 입력을 받을 수 있는 구간.
        /// </summary>
        private bool _comboWindowOpen;

        /// <summary>
        /// 콤보 윈도우 내 입력 예약 여부.
        /// </summary>
        private bool _comboInputQueued;

        /// <summary>
        /// 공격 코루틴 참조.
        /// </summary>
        private Coroutine _attackCoroutine;

        /// <summary>
        /// 콤보 리셋 타이머 코루틴 참조.
        /// 일정 시간 입력 없을 때 콤보 초기화.
        /// </summary>
        private Coroutine _comboResetCoroutine;

        // ──────────────────────────────────────────
        // 강공격 상태
        // ──────────────────────────────────────────

        /// <summary>
        /// A키 누른 시각 (Time.time). 홀드 시간 계산에 사용.
        /// </summary>
        private float _attackPressTime;

        /// <summary>
        /// 강공격 홀드 중 여부.
        /// performed = true / canceled = false.
        /// </summary>
        private bool _isChargeHolding;

        // ──────────────────────────────────────────
        // 히트박스 참조
        // ──────────────────────────────────────────

        /// <summary>
        /// 히트박스 관리 컴포넌트.
        /// OnHit 이벤트를 구독하여 적중 시 OnHitTarget 발행.
        /// Awake 에서 GetComponent 로 자동 탐색.
        /// </summary>
        private PlayerAttackHitboxManager _hitboxManager;

        /// <summary>
        /// 현재 공격 방향. Lunge 연출 + Gizmos 표시에 사용.
        /// </summary>
        private Vector2 _currentAttackDir;

        // ──────────────────────────────────────────
        // 이벤트
        // ──────────────────────────────────────────

        /// <summary>
        /// 공격이 적에 적중 시 발행.
        /// 파라미터1: 적중 위치 / 파라미터2: 봉인도 누적량.
        /// SealGaugeSystem 에서 구독하여 봉인도 처리.
        /// </summary>
        public event Action<Vector2, float> OnHitTarget;

        /// <summary> 기본 공격 시작 시 발행. </summary>
        public event Action OnAttackStarted;

        /// <summary> 강공격 시작 시 발행. </summary>
        public event Action OnChargeAttackStarted;

        // ──────────────────────────────────────────
        // 프로퍼티
        // ──────────────────────────────────────────

        /// <summary> 현재 공격 중 여부. </summary>
        public bool IsAttacking => _isAttacking;

        /// <summary> 현재 콤보 단계 (0부터). </summary>
        public int CurrentCombo => _currentCombo;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            _moveController = GetComponent<PlayerMoveController>();
            _swingController = GetComponent<PlayerWeaponSwingController>();
            _rigid2D = GetComponent<Rigidbody2D>();
            _hitboxManager = GetComponent<PlayerAttackHitboxManager>();

            if (_visualTransform == null)
                _visualTransform = transform;

            if (_hitboxManager == null)
                Debug.LogWarning("[PlayerAttackController] PlayerAttackHitboxManager 미연결.");

            if (_data == null)
            {
                Debug.LogError("[PlayerAttackController] PlayerAttackDataSO 미연결.");
                enabled = false;
            }
        }

        private void Start()
        {
            if (PlayerInputHandler.Instance == null)
            {
                Debug.LogError("[PlayerAttackController] PlayerInputHandler 없음.");
                enabled = false;
                return;
            }

            PlayerInputHandler.Instance.OnAttack -= HandleAttackPress;
            PlayerInputHandler.Instance.OnAttack += HandleAttackPress;
            PlayerInputHandler.Instance.OnAttackReleased -= HandleAttackRelease;
            PlayerInputHandler.Instance.OnAttackReleased += HandleAttackRelease;

            // HitboxManager.OnHit 구독 → 적중 시 OnHitTarget 발행
            if (_hitboxManager != null)
            {
                _hitboxManager.OnHit -= HandleHitboxHit;
                _hitboxManager.OnHit += HandleHitboxHit;
            }
        }

        // Update() 제거 — ProcessHitCheck OverlapCircle 경로 삭제
        // 히트박스 판정은 PlayerAttackHitboxManager.Update() 가 전담

        private void OnDestroy()
        {
            if (PlayerInputHandler.Instance != null)
            {
                PlayerInputHandler.Instance.OnAttack -= HandleAttackPress;
                PlayerInputHandler.Instance.OnAttackReleased -= HandleAttackRelease;
            }

            if (_hitboxManager != null)
                _hitboxManager.OnHit -= HandleHitboxHit;
        }

        // ══════════════════════════════════════════════════════
        // 입력 처리
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// A키 누름. OnAttack 콜백.
        /// 홀드 타이머 시작 + 공격 실행 또는 콤보 예약.
        /// </summary>
        private void HandleAttackPress()
        {
            _attackPressTime = Time.time;
            _isChargeHolding = true;

            // 강공격 홀드 맥동 연출 시작
            _swingController.StartChargePulse();

            if (!_isAttacking)
            {
                ExecuteComboAttack();
                return;
            }

            if (_comboWindowOpen)
                _comboInputQueued = true;
        }

        /// <summary>
        /// A키 뗌. OnAttackReleased 콜백.
        /// 홀드 시간 확인 후 강공격 판정.
        /// </summary>
        private void HandleAttackRelease()
        {
            if (!_isChargeHolding) return;

            float holdTime = Time.time - _attackPressTime;
            _isChargeHolding = false;

            _swingController.StopChargePulse();

            // 강공격: 최소 홀드 시간 충족 + 현재 공격 중 아님
            if (holdTime >= _data.ChargeMinHoldTime && !_isAttacking)
                ExecuteChargeAttack();
        }

        // ══════════════════════════════════════════════════════
        // 공격 실행
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 현재 콤보 단계에 맞는 기본 공격 실행.
        /// </summary>
        private void ExecuteComboAttack()
        {
            if (_attackCoroutine != null) StopCoroutine(_attackCoroutine);
            _attackCoroutine = StartCoroutine(ComboAttackRoutine());
        }

        /// <summary>
        /// 강공격 실행.
        /// </summary>
        private void ExecuteChargeAttack()
        {
            if (_attackCoroutine != null) StopCoroutine(_attackCoroutine);
            _attackCoroutine = StartCoroutine(ChargeAttackRoutine());
        }

        // ══════════════════════════════════════════════════════
        // 공격 코루틴
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 기본 공격 코루틴.
        ///
        /// [흐름]
        ///   1. 이동 잠금
        ///   2. SwingController.PlaySwing 호출 (무기 DOTween 연출 위임)
        ///      onHit 콜백 → _activeHitboxRadius 설정 → Update에서 판정
        ///   3. Lunge 전진 연출
        ///   4. SwingController 완료 대기 (IsSwinging 폴링)
        ///   5. 콤보 윈도우 → 다음 입력 대기
        ///   6. 콤보 예약 있으면 다음 콤보 / 없으면 리셋
        /// </summary>
        private IEnumerator ComboAttackRoutine()
        {
            _isAttacking = true;
            _comboWindowOpen = false;
            _comboInputQueued = false;

            OnAttackStarted?.Invoke();

            _currentAttackDir = GetAttackDirection();

            _moveController.SetMoveLocked(true);

            // 콤보 인덱스 변환 + 봉인도 직접 전달
            var comboIndex = (PlayerWeaponSwingController.ComboIndex)
                Mathf.Clamp(_currentCombo, 0, 2);
            float sealAmount = GetComboSealAmount();

            // SwingController 가 내부에서 HitboxManager 직접 제어
            _swingController.PlaySwing(comboIndex, _currentAttackDir, sealAmount);
            PlayLunge(_currentAttackDir);

            float maxWait = (_data.BackswingDuration + _data.AttackDuration + _data.ReturnDuration) * 2f;
            float elapsed = 0f;
            while (_swingController.IsSwinging && elapsed < maxWait)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            // ── 콤보 윈도우 ──────────────────────
            _comboWindowOpen = true;
            float windowDuration = _data.BackswingDuration
                                 + _data.AttackDuration * (1f - _data.ComboWindowStartRatio);
            float windowElapsed = 0f;

            while (windowElapsed < windowDuration)
            {
                windowElapsed += Time.deltaTime;
                yield return null;
            }

            _comboWindowOpen = false;
            _isAttacking = false;

            if (_comboInputQueued && _currentCombo < _data.MaxComboCount - 1)
            {
                _comboInputQueued = false;
                _currentCombo++;
                ExecuteComboAttack();
            }
            else
            {
                _moveController.SetMoveLocked(false);
                StartComboResetTimer();
            }

            _attackCoroutine = null;
        }

        /// <summary>
        /// 강공격 코루틴.
        /// v2.1: sealAmount 직접 전달 → SwingController 내부에서 HitboxManager 제어.
        /// </summary>
        private IEnumerator ChargeAttackRoutine()
        {
            _isAttacking = true;
            OnChargeAttackStarted?.Invoke();

            _currentAttackDir = GetAttackDirection();

            _moveController.SetMoveLocked(true);

            _swingController.PlayChargeSwing(_currentAttackDir, _data.ChargeSealAmount);
            PlayLunge(_currentAttackDir);

            float maxWait = (_data.BackswingDuration + _data.AttackDuration + _data.ReturnDuration) * 3f;
            float elapsed = 0f;
            while (_swingController.IsSwinging && elapsed < maxWait)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            _currentCombo = 0;
            _isAttacking = false;
            _moveController.SetMoveLocked(false);
            _attackCoroutine = null;
        }

        // ══════════════════════════════════════════════════════
        // 히트박스 적중 처리
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// PlayerAttackHitboxManager.OnHit 수신 핸들러.
        /// 히트박스가 적 콜라이더에 Overlap 되면 이 함수가 호출됨.
        ///
        /// [v2.1 — OnSwingHitCallback + ProcessHitCheck 대체]
        ///   기존: SwingController onHit 콜백 → _activeHitboxRadius 설정
        ///         → Update() 매 프레임 OverlapCircle 판정
        ///   변경: HitboxManager.OnHit 이벤트 → 이 함수에서 OnHitTarget 발행
        ///         → 중복 판정 방지는 HitboxManager 내부 _hitTargets HashSet 이 담당
        ///         → ProcessHitCheck() 완전 제거
        /// </summary>
        private void HandleHitboxHit(Collider2D hitCol, float sealAmount)
        {
            Vector2 hitPos = hitCol.bounds.center;
            OnHitTarget?.Invoke(hitPos, sealAmount);

            Debug.Log($"[PlayerAttackController] 적중: {hitCol.name} | " +
                      $"봉인도 +{sealAmount:F1} | 콤보: {_currentCombo + 1}");
        }

        // ══════════════════════════════════════════════════════
        // 전진 연출 (Lunge)
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 공격 시 플레이어 Visual 소량 전진 연출.
        /// Rigidbody 가 아닌 Visual Transform 만 DOTween 이동.
        /// 물리 이동계와 분리되어 충돌에 영향을 주지 않음.
        /// </summary>
        private void PlayLunge(Vector2 attackDir)
        {
            if (_data.LungeDistance <= 0f) return;

            DOTween.Kill(_visualTransform, complete: false);

            Vector3 originPos = _visualTransform.localPosition;
            Vector3 lungePos = originPos + (Vector3)(attackDir * _data.LungeDistance);

            DOTween.Sequence()
                .Append(_visualTransform
                    .DOLocalMove(lungePos, _data.LungeDuration)
                    .SetEase(Ease.OutQuad))
                .Append(_visualTransform
                    .DOLocalMove(originPos, _data.LungeDuration * 1.5f)
                    .SetEase(Ease.InOutSine));
        }

        // ══════════════════════════════════════════════════════
        // 콤보 관리
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 콤보 리셋 타이머 시작.
        /// 일정 시간 내 입력 없으면 콤보 0으로 초기화.
        /// </summary>
        private void StartComboResetTimer()
        {
            if (_comboResetCoroutine != null) StopCoroutine(_comboResetCoroutine);
            _comboResetCoroutine = StartCoroutine(ComboResetRoutine());
        }

        private IEnumerator ComboResetRoutine()
        {
            yield return new UnityEngine.WaitForSeconds(
                _data.BackswingDuration + _data.AttackDuration + _data.ReturnDuration);
            _currentCombo = 0;
            _comboResetCoroutine = null;
        }

        // ══════════════════════════════════════════════════════
        // 유틸리티
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 현재 공격 방향 반환.
        /// PlayerMoveController.FacingDirection 기준 (마우스 없음).
        /// </summary>
        private Vector2 GetAttackDirection()
            => _moveController != null
                ? _moveController.FacingDirection.normalized
                : Vector2.right;

        /// <summary>
        /// 현재 콤보 단계에 맞는 봉인도 누적량 반환.
        /// </summary>
        private float GetComboSealAmount()
        {
            return _currentCombo switch
            {
                0 => _data.Combo1SealAmount,
                1 => _data.Combo2SealAmount,
                _ => _data.Combo3SealAmount,
            };
        }

        // ══════════════════════════════════════════════════════
        // Gizmos
        // ══════════════════════════════════════════════════════

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (_data == null) return;

            Vector2 dir = Application.isPlaying ? GetAttackDirection() : Vector2.right;
            Vector2 center = (Vector2)transform.position + dir * _data.HitboxOffset;

            // 기본 히트박스
            UnityEngine.Gizmos.color = new UnityEngine.Color(1f, 1f, 1f, 0.3f);
            UnityEngine.Gizmos.DrawWireSphere(center, _data.HitboxRadius);

            // 강공격 히트박스
            UnityEngine.Gizmos.color = new UnityEngine.Color(1f, 0.8f, 0f, 0.2f);
            UnityEngine.Gizmos.DrawWireSphere(center, _data.HitboxRadius * _data.ChargeHitboxScale);

            UnityEngine.Gizmos.color = UnityEngine.Color.cyan;
            UnityEngine.Gizmos.DrawRay(transform.position, (UnityEngine.Vector3)dir * 1.5f);

            UnityEditor.Handles.Label(
                transform.position + UnityEngine.Vector3.up * 2.2f,
                $"콤보: {_currentCombo + 1} | 공격중: {_isAttacking} | 윈도우: {_comboWindowOpen}");
        }
#endif
    }
}