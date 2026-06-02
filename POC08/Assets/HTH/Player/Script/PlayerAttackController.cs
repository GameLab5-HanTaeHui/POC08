// ============================================================
// PlayerAttackController.cs  v1.0
// 플레이어 A키 공격 컨트롤러
//
// [역할]
//   A 탭      : 기본 공격 (최대 3콤보, 봉인도 누적)
//   A 홀드 릴리즈 : 강공격 (높은 봉인도 누적, 더 큰 히트박스)
//   공격 방향 : PlayerMoveController.FacingDirection 기준 (마우스 없음)
//   적중 시   : 히트스톱 + 봉인도 누적 이벤트 발행
//
// [DOTween 연출]
//   기본 공격:
//     1. 무기 백스윙 (공격 방향 반대로 당김)
//     2. 무기 전방 스윙 (공격 방향으로 뻗음) + 플레이어 소량 전진
//     3. 히트박스 활성 + 스케일 펀치
//     4. 무기 복귀
//   강공격:
//     홀드 중 : 무기 맥동(Pulse) 연출
//     릴리즈  : 더 큰 스윙 + 더 큰 히트박스 + 더 긴 히트스톱
//
// [히트스톱 방식]
//   Time.timeScale 을 낮춰 전체 일시 정지 효과.
//   WaitForSecondsRealtime → timeScale 영향 없이 실시간 대기.
//
// [봉인도 연동]
//   적중 시 OnHitTarget(Vector2 hitPos, float sealAmount) 이벤트 발행.
//   추후 SealGaugeSystem 이 구독하여 봉인도 처리.
//
// [요구 컴포넌트]
//   PlayerMoveController  (FacingDirection 참조)
//   PlayerInputHandler    (OnAttack / OnAttackReleased 구독)
//   PlayerAttackDataSO    (_data 연결 필수)
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
    /// 플레이어 A키 공격 컨트롤러. (v1.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [공격 흐름]
    ///   A 탭 → 기본 공격
    ///     BackswingDuration 동안 무기 당김
    ///     → SwingDuration 동안 전방 스윙 + 플레이어 전진
    ///     → 히트박스 활성 (OverlapCircle)
    ///     → 적중 시 히트스톱 + OnHitTarget 발행
    ///     → RecoverDuration 동안 복귀
    ///
    ///   A 홀드 → 강공격
    ///     홀드 중 무기 Pulse 연출
    ///     → ChargeMinHoldTime 이상 홀드 후 릴리즈
    ///     → 더 큰 SwingDistance + 히트박스
    ///     → OnHitTarget(ChargeSealGaugeAmount) 발행
    ///
    /// [외부 이벤트 구독 예시]
    ///   _attackCtrl.OnHitTarget += (pos, sealAmt) =>
    ///       sealGauge.AddGauge(targetPart, sealAmt);
    /// ────────────────────────────────────────────────────
    /// </summary>
    [RequireComponent(typeof(PlayerMoveController))]
    public class PlayerAttackController : MonoBehaviour
    {
        // ──────────────────────────────────────────
        // Inspector — 데이터 SO
        // ──────────────────────────────────────────

        [Header("── 데이터 SO ──────────────────────")]

        /// <summary>
        /// 공격 수치 ScriptableObject.
        /// 봉인도 누적량 / 히트박스 / DOTween 수치 포함.
        /// 미연결 시 컴포넌트 비활성화.
        /// </summary>
        [Tooltip("공격 수치 SO. PlayerAttackDataSO 연결 필수.")]
        [SerializeField] private PlayerAttackDataSO _data;

        // ──────────────────────────────────────────
        // Inspector — 오브젝트 연결
        // ──────────────────────────────────────────

        [Header("── 오브젝트 연결 ──────────────────────")]

        /// <summary>
        /// 무기 오브젝트 Transform.
        /// DOTween 으로 이동/스케일 연출 대상.
        /// Player 하위 WeaponPivot 또는 Weapon 오브젝트 연결.
        /// </summary>
        [Tooltip("무기 오브젝트 Transform. DOTween 연출 대상.")]
        [SerializeField] private Transform _weaponTransform;

        /// <summary>
        /// 플레이어 Visual Transform.
        /// 공격 시 소량 전진(Lunge) 연출 대상.
        /// null 이면 자신 transform 사용.
        /// </summary>
        [Tooltip("공격 전진 연출 대상 Visual Transform. null=자신 transform.")]
        [SerializeField] private Transform _visualTransform;

        // ──────────────────────────────────────────
        // 컴포넌트 참조
        // ──────────────────────────────────────────

        /// <summary>
        /// PlayerMoveController 참조.
        /// FacingDirection (공격 방향) 읽기용.
        /// </summary>
        private PlayerMoveController _moveController;

        /// <summary>
        /// Rigidbody2D 참조.
        /// 공격 전진(Lunge) 시 velocity 일시 적용.
        /// </summary>
        private Rigidbody2D _rigid2D;

        // ──────────────────────────────────────────
        // 공격 상태
        // ──────────────────────────────────────────

        /// <summary>
        /// 현재 공격 처리 중 여부.
        /// true: 새 공격 입력 중 콤보 윈도우 이후에만 수신.
        /// </summary>
        private bool _isAttacking;

        /// <summary>
        /// 현재 콤보 단계 (0부터 시작).
        /// 0 = 1콤보, 1 = 2콤보, 2 = 3콤보.
        /// MaxComboCount 도달 시 0으로 초기화.
        /// </summary>
        private int _currentCombo;

        /// <summary>
        /// 콤보 윈도우 열림 여부.
        /// true: 다음 공격 입력을 받을 수 있는 구간.
        /// </summary>
        private bool _comboWindowOpen;

        /// <summary>
        /// 콤보 윈도우 내에 입력이 들어왔는지 여부.
        /// true: 다음 공격 예약됨.
        /// </summary>
        private bool _comboInputQueued;

        /// <summary>
        /// 공격 코루틴 참조.
        /// 중단 시 StopCoroutine 에 사용.
        /// </summary>
        private Coroutine _attackCoroutine;

        /// <summary>
        /// 콤보 리셋 타이머 코루틴 참조.
        /// 입력 없을 때 일정 시간 후 콤보 초기화.
        /// </summary>
        private Coroutine _comboResetCoroutine;

        // ──────────────────────────────────────────
        // 강공격 상태
        // ──────────────────────────────────────────

        /// <summary>
        /// A 키 누른 시각 (Time.time 기준).
        /// 홀드 시간 계산에 사용.
        /// </summary>
        private float _attackPressTime;

        /// <summary>
        /// 현재 강공격 홀드 중 여부.
        /// true: ChargeMinHoldTime 체크 대기 중.
        /// </summary>
        private bool _isChargeHolding;

        /// <summary>
        /// 강공격 홀드 연출 Tween 참조.
        /// 홀드 해제 시 Kill.
        /// </summary>
        private Tween _chargePulseTween;

        // ──────────────────────────────────────────
        // 히트스톱
        // ──────────────────────────────────────────

        /// <summary>
        /// 히트스톱 코루틴 참조.
        /// 중복 실행 방지.
        /// </summary>
        private Coroutine _hitStopCoroutine;

        // ──────────────────────────────────────────
        // 무기 Transform 원점 캐싱
        // ──────────────────────────────────────────

        /// <summary>
        /// 무기 오브젝트의 로컬 원점 위치.
        /// DOTween 복귀 시 이 위치로 돌아옴.
        /// Awake 에서 캐싱.
        /// </summary>
        private Vector3 _weaponOriginLocalPos;

        /// <summary>
        /// 무기 오브젝트의 원점 스케일.
        /// DOTween Scale 복귀용.
        /// </summary>
        private Vector3 _weaponOriginScale;

        // ──────────────────────────────────────────
        // 이벤트
        // ──────────────────────────────────────────

        /// <summary>
        /// 공격이 적에 적중했을 때 발행.
        /// 파라미터1: 적중 위치 (Vector2).
        /// 파라미터2: 봉인도 누적량 (float).
        /// 추후 SealGaugeSystem 이 구독하여 봉인도 처리.
        /// README: 공격 적중 시 봉인도 누적.
        /// </summary>
        public event Action<Vector2, float> OnHitTarget;

        /// <summary>
        /// 공격 시작 시 1회 발행.
        /// UI, 오디오 등에서 구독.
        /// </summary>
        public event Action OnAttackStarted;

        /// <summary>
        /// 강공격 시작 시 1회 발행.
        /// </summary>
        public event Action OnChargeAttackStarted;

        // ──────────────────────────────────────────
        // 프로퍼티
        // ──────────────────────────────────────────

        /// <summary> 현재 공격 중 여부. </summary>
        public bool IsAttacking => _isAttacking;

        /// <summary> 현재 콤보 단계 (0 = 1콤보). </summary>
        public int CurrentCombo => _currentCombo;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            // ── 컴포넌트 취득 ──────────────────────
            _moveController = GetComponent<PlayerMoveController>();
            _rigid2D = GetComponent<Rigidbody2D>();

            // Visual Transform 미설정 시 자신 transform 사용
            if (_visualTransform == null)
                _visualTransform = transform;

            // ── 데이터 유효성 확인 ──────────────────────
            if (_data == null)
            {
                Debug.LogError("[PlayerAttackController] PlayerAttackDataSO 가 연결되지 않았습니다.");
                enabled = false;
                return;
            }

            // ── 무기 원점 캐싱 ──────────────────────
            if (_weaponTransform != null)
            {
                _weaponOriginLocalPos = _weaponTransform.localPosition;
                _weaponOriginScale = _weaponTransform.localScale;
            }
        }

        private void Start()
        {
            // ── 입력 이벤트 구독 ──────────────────────
            if (PlayerInputHandler.Instance == null)
            {
                Debug.LogError("[PlayerAttackController] PlayerInputHandler 가 씬에 없습니다.");
                enabled = false;
                return;
            }

            PlayerInputHandler.Instance.OnAttack += HandleAttackPress;
            PlayerInputHandler.Instance.OnAttackReleased += HandleAttackRelease;
        }

        private void OnDestroy()
        {
            // ── 이벤트 구독 해제 ──────────────────────
            if (PlayerInputHandler.Instance != null)
            {
                PlayerInputHandler.Instance.OnAttack -= HandleAttackPress;
                PlayerInputHandler.Instance.OnAttackReleased -= HandleAttackRelease;
            }

            // ── DOTween 정리 ──────────────────────
            _chargePulseTween?.Kill();
            if (_weaponTransform != null) DOTween.Kill(_weaponTransform);
            if (_visualTransform != null) DOTween.Kill(_visualTransform);

            // ── TimeScale 안전 복구 ──────────────────────
            if (_hitStopCoroutine != null)
                Time.timeScale = 1f;
        }

        // ══════════════════════════════════════════════════════
        // 입력 처리
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// A 키 누름 콜백. PlayerInputHandler.OnAttack 수신.
        ///
        /// [처리 흐름]
        ///   홀드 타이머 시작 (_attackPressTime 기록)
        ///   공격 중이 아님 → 기본 공격 즉시 실행
        ///   공격 중 + 콤보 윈도우 열림 → 콤보 예약
        /// </summary>
        private void HandleAttackPress()
        {
            // 홀드 시각 기록 (강공격 판정용)
            _attackPressTime = Time.time;
            _isChargeHolding = true;

            // 현재 공격 중이 아니면 즉시 기본 공격 시작
            if (!_isAttacking)
            {
                ExecuteBasicAttack();
                return;
            }

            // 공격 중 + 콤보 윈도우 열려 있으면 다음 콤보 예약
            if (_comboWindowOpen)
                _comboInputQueued = true;
        }

        /// <summary>
        /// A 키 뗌 콜백. PlayerInputHandler.OnAttackReleased 수신.
        ///
        /// [강공격 판정]
        ///   홀드 시간 >= ChargeMinHoldTime → 강공격 실행
        ///   홀드 시간 <  ChargeMinHoldTime → 이미 기본 공격이 진행 중이므로 무시
        /// </summary>
        private void HandleAttackRelease()
        {
            if (!_isChargeHolding) return;

            float holdTime = Time.time - _attackPressTime;
            _isChargeHolding = false;

            // 강공격 홀드 연출 종료
            StopChargePulse();

            // 강공격 조건: 최소 홀드 시간 충족 + 현재 공격 중이 아님
            if (holdTime >= _data.ChargeMinHoldTime && !_isAttacking)
            {
                ExecuteChargeAttack();
            }
        }

        // ══════════════════════════════════════════════════════
        // 기본 공격
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 기본 공격 실행.
        /// 콤보 단계에 따라 봉인도 배율 적용.
        /// DOTween 백스윙 → 스윙 → 히트박스 → 복귀 시퀀스.
        /// </summary>
        private void ExecuteBasicAttack()
        {
            if (_attackCoroutine != null)
                StopCoroutine(_attackCoroutine);

            _attackCoroutine = StartCoroutine(BasicAttackRoutine());
        }

        /// <summary>
        /// 기본 공격 코루틴.
        ///
        /// [흐름]
        ///   1. 이동 잠금 (공격 중 이동 차단)
        ///   2. 백스윙 DOTween (BackswingDuration)
        ///   3. 스윙 DOTween + 플레이어 전진 (SwingDuration)
        ///   4. 히트박스 활성 → 적중 판정 → 히트스톱 + 이벤트 발행
        ///   5. 복귀 DOTween (RecoverDuration)
        ///   6. 콤보 윈도우 열기 → 다음 입력 대기
        ///   7. 콤보 예약 있으면 다음 콤보 / 없으면 콤보 초기화
        /// </summary>
        private IEnumerator BasicAttackRoutine()
        {
            _isAttacking = true;
            _comboWindowOpen = false;
            _comboInputQueued = false;

            OnAttackStarted?.Invoke();

            // 공격 방향 결정 (마우스 없음, 이동 방향 기준)
            Vector2 attackDir = GetAttackDirection();

            // 이동 잠금 — 공격 중 슬라이딩 방지
            _moveController.SetMoveLocked(true);

            // ── 1. 백스윙 ──────────────────────
            PlayWeaponBackswing(attackDir);
            yield return new WaitForSeconds(_data.BackswingDuration);

            // ── 2. 스윙 + 플레이어 전진 ──────────────────────
            float sealMultiplier = GetComboSealMultiplier();
            float sealAmount = _data.BasicSealGaugeAmount * sealMultiplier;

            PlayWeaponSwing(attackDir, isCharge: false);
            PlayPlayerLunge(attackDir);

            yield return new WaitForSeconds(_data.SwingDuration * 0.5f);

            // ── 3. 히트박스 활성 (스윙 중간 지점) ──────────────────────
            CheckHit(attackDir, _data.HitboxRadius, sealAmount, isCharge: false);

            yield return new WaitForSeconds(_data.SwingDuration * 0.5f);

            // ── 4. 복귀 시작 + 콤보 윈도우 열기 ──────────────────────
            PlayWeaponRecover();
            _comboWindowOpen = true;

            // 콤보 윈도우 대기 (BasicAttackDuration 의 남은 시간)
            float windowTime = _data.BasicAttackDuration
                               * (1f - _data.ComboWindowStartRatio)
                               - _data.RecoverDuration;
            if (windowTime > 0f)
                yield return new WaitForSeconds(windowTime);

            // ── 5. 콤보 처리 ──────────────────────
            _comboWindowOpen = false;
            _isAttacking = false;

            if (_comboInputQueued && _currentCombo < _data.MaxComboCount - 1)
            {
                // 다음 콤보 진행
                _comboInputQueued = false;
                _currentCombo++;
                ExecuteBasicAttack();
            }
            else
            {
                // 콤보 초기화
                StartComboResetTimer();
            }

            // 이동 잠금 해제
            _moveController.SetMoveLocked(false);
            _attackCoroutine = null;
        }

        // ══════════════════════════════════════════════════════
        // 강공격
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 강공격 실행.
        /// 기본 공격보다 큰 스윙 거리 + 히트박스 + 히트스톱.
        /// </summary>
        private void ExecuteChargeAttack()
        {
            if (_attackCoroutine != null)
                StopCoroutine(_attackCoroutine);

            _attackCoroutine = StartCoroutine(ChargeAttackRoutine());
        }

        /// <summary>
        /// 강공격 코루틴.
        /// 기본 공격 코루틴과 동일한 흐름이나
        /// SwingDistance, HitboxRadius, HitStopDuration 이 더 큼.
        /// </summary>
        private IEnumerator ChargeAttackRoutine()
        {
            _isAttacking = true;
            OnChargeAttackStarted?.Invoke();

            Vector2 attackDir = GetAttackDirection();

            // 이동 잠금
            _moveController.SetMoveLocked(true);

            // ── 백스윙 ──────────────────────
            PlayWeaponBackswing(attackDir);
            yield return new WaitForSeconds(_data.BackswingDuration);

            // ── 강공격 스윙 ──────────────────────
            PlayWeaponSwing(attackDir, isCharge: true);
            PlayPlayerLunge(attackDir);

            yield return new WaitForSeconds(_data.SwingDuration * 0.5f);

            // ── 히트박스 (강공격 — 더 큰 반경) ──────────────────────
            float chargeRadius = _data.HitboxRadius * _data.ChargeHitboxScale;
            CheckHit(attackDir, chargeRadius, _data.ChargeSealGaugeAmount, isCharge: true);

            yield return new WaitForSeconds(_data.SwingDuration * 0.5f);

            // ── 복귀 ──────────────────────
            PlayWeaponRecover();
            yield return new WaitForSeconds(_data.RecoverDuration);

            // ── 정리 ──────────────────────
            _currentCombo = 0;
            _isAttacking = false;
            _moveController.SetMoveLocked(false);
            _attackCoroutine = null;
        }

        // ══════════════════════════════════════════════════════
        // 히트박스 판정
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// OverlapCircle 로 공격 방향 앞의 적을 감지하고
        /// 적중 시 히트스톱 + OnHitTarget 이벤트를 발행한다.
        ///
        /// [판정 방식]
        ///   플레이어 위치 + 공격방향 * HitboxOffset 을 중심으로
        ///   radius 반경 OverlapCircleNonAlloc 사용.
        ///
        /// [적중 처리]
        ///   HitLayer 에 속하는 콜라이더 감지 시
        ///   → 히트스톱 코루틴 시작
        ///   → OnHitTarget(적중 위치, 봉인도 누적량) 발행
        ///   README: 히트스톱 발생 / 봉인 파편 발생 / 타격음 발생
        /// </summary>
        /// <param name="attackDir">공격 방향 벡터 (정규화).</param>
        /// <param name="radius">히트박스 반경.</param>
        /// <param name="sealAmount">적중 시 봉인도 누적량.</param>
        /// <param name="isCharge">강공격 여부 (히트스톱 시간 분기).</param>
        private void CheckHit(Vector2 attackDir, float radius, float sealAmount, bool isCharge)
        {
            // 히트박스 중심 위치 계산
            Vector2 hitCenter = (Vector2)transform.position + attackDir * _data.HitboxOffset;

            // OverlapCircle 로 감지 (NonAlloc — GC 방지)
            Collider2D[] results = new Collider2D[8];
            int count = Physics2D.OverlapCircleNonAlloc(
                hitCenter,
                radius,
                results,
                _data.HitLayer);

            if (count == 0)
            {
                // 공격 실패 — README: 히트스톱 없음, 봉인 파편 없음
                Debug.Log("[PlayerAttackController] 공격 미스 — 히트스톱 없음");
                return;
            }

            // 첫 번째 감지된 적에만 판정 (중복 방지)
            // 추후 범위 공격(관통 열쇠 등) 어빌리티로 복수 적중 구현 가능
            Collider2D hit = results[0];
            Vector2 hitPos = hit.ClosestPoint(hitCenter);

            // 히트스톱
            float stopDuration = isCharge
                ? _data.ChargeHitStopDuration
                : _data.HitStopDuration;

            if (stopDuration > 0f)
            {
                if (_hitStopCoroutine != null) StopCoroutine(_hitStopCoroutine);
                _hitStopCoroutine = StartCoroutine(HitStopRoutine(stopDuration));
            }

            // 봉인도 누적 이벤트 발행
            OnHitTarget?.Invoke(hitPos, sealAmount);

            Debug.Log($"[PlayerAttackController] 적중: {hit.name} | 봉인도 +{sealAmount:F1} | 강공격: {isCharge}");
        }

        // ══════════════════════════════════════════════════════
        // DOTween 무기 연출
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 백스윙 연출.
        /// 무기를 공격 방향 반대로 BackswingDistance 만큼 당김.
        /// DOTween MoveLocalPosition.
        /// </summary>
        /// <param name="attackDir">공격 방향.</param>
        private void PlayWeaponBackswing(Vector2 attackDir)
        {
            if (_weaponTransform == null) return;

            // 기존 tween 종료 후 원점 복귀
            DOTween.Kill(_weaponTransform, complete: false);
            _weaponTransform.localPosition = _weaponOriginLocalPos;
            _weaponTransform.localScale = _weaponOriginScale;

            // 공격 방향 반대로 백스윙
            Vector3 backPos = _weaponOriginLocalPos
                              + (Vector3)(-attackDir * _data.BackswingDistance);

            _weaponTransform
                .DOLocalMove(backPos, _data.BackswingDuration)
                .SetEase(Ease.OutQuad);
        }

        /// <summary>
        /// 스윙 연출.
        /// 무기를 공격 방향으로 SwingDistance 만큼 뻗음 + 스케일 펀치.
        /// 강공격이면 SwingDistance × ChargeSwingDistanceMultiplier 적용.
        /// </summary>
        /// <param name="attackDir">공격 방향.</param>
        /// <param name="isCharge">강공격 여부.</param>
        private void PlayWeaponSwing(Vector2 attackDir, bool isCharge)
        {
            if (_weaponTransform == null) return;

            float swingDist = _data.SwingDistance
                              * (isCharge ? _data.ChargeSwingDistanceMultiplier : 1f);

            Vector3 swingPos = _weaponOriginLocalPos
                               + (Vector3)(attackDir * swingDist);

            // 스윙 이동
            DOTween.Kill(_weaponTransform, complete: false);

            Sequence swingSeq = DOTween.Sequence();
            swingSeq.Append(
                _weaponTransform
                    .DOLocalMove(swingPos, _data.SwingDuration)
                    .SetEase(Ease.OutCubic));

            // 스케일 펀치 — 타격감 연출
            if (_data.SwingPunchScale > 0f)
            {
                swingSeq.Join(
                    _weaponTransform
                        .DOPunchScale(
                            Vector3.one * _data.SwingPunchScale,
                            _data.SwingDuration,
                            vibrato: 2,
                            elasticity: 0.3f));
            }
        }

        /// <summary>
        /// 복귀 연출.
        /// 무기를 원점 위치 / 스케일로 복귀.
        /// </summary>
        private void PlayWeaponRecover()
        {
            if (_weaponTransform == null) return;

            DOTween.Kill(_weaponTransform, complete: false);

            Sequence recoverSeq = DOTween.Sequence();
            recoverSeq.Append(
                _weaponTransform
                    .DOLocalMove(_weaponOriginLocalPos, _data.RecoverDuration)
                    .SetEase(Ease.InOutSine));
            recoverSeq.Join(
                _weaponTransform
                    .DOScale(_weaponOriginScale, _data.RecoverDuration)
                    .SetEase(Ease.InOutSine));
        }

        /// <summary>
        /// 플레이어 공격 전진(Lunge) 연출.
        /// 공격 방향으로 소량 이동 후 즉시 복귀.
        /// DOTween DOLocalMove 사용. Rigidbody2D 와 충돌하지 않도록
        /// visualTransform 의 localPosition 을 건드림.
        ///
        /// [Rigidbody2D 와 분리한 이유]
        ///   Rigidbody2D.velocity 를 직접 바꾸면 이동 시스템과 충돌.
        ///   Visual 오브젝트만 DOTween 으로 움직여 시각적 전진감 표현.
        /// </summary>
        /// <param name="attackDir">공격 방향.</param>
        private void PlayPlayerLunge(Vector2 attackDir)
        {
            if (_data.AttackLungeDistance <= 0f) return;

            DOTween.Kill(_visualTransform, complete: false);

            Vector3 originPos = _visualTransform.localPosition;
            Vector3 lungePos = originPos + (Vector3)(attackDir * _data.AttackLungeDistance);

            Sequence lungeSeq = DOTween.Sequence();
            lungeSeq.Append(
                _visualTransform
                    .DOLocalMove(lungePos, _data.AttackLungeDuration)
                    .SetEase(Ease.OutQuad));
            lungeSeq.Append(
                _visualTransform
                    .DOLocalMove(originPos, _data.AttackLungeDuration * 1.5f)
                    .SetEase(Ease.InOutSine));
        }

        /// <summary>
        /// 강공격 홀드 중 무기 맥동(Pulse) 연출.
        /// 무기 오브젝트가 커졌다 작아지는 Yoyo 루프.
        /// 릴리즈 시 StopChargePulse() 로 종료.
        /// </summary>
        private void PlayChargePulse()
        {
            if (_weaponTransform == null || _data.ChargePulseScale <= 0f) return;

            StopChargePulse();

            _chargePulseTween = _weaponTransform
                .DOScale(
                    _weaponOriginScale * (1f + _data.ChargePulseScale),
                    _data.ChargePulsePeriod)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo);
        }

        /// <summary>
        /// 강공격 홀드 맥동 연출 종료.
        /// 무기 스케일 원점 복귀.
        /// </summary>
        private void StopChargePulse()
        {
            if (_chargePulseTween == null) return;

            _chargePulseTween.Kill();
            _chargePulseTween = null;

            if (_weaponTransform != null)
                _weaponTransform.localScale = _weaponOriginScale;
        }

        // ══════════════════════════════════════════════════════
        // 히트스톱
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 히트스톱 코루틴.
        /// Time.timeScale 을 낮춰 전체 일시 정지 효과.
        /// WaitForSecondsRealtime → timeScale 영향 없이 실시간 대기.
        ///
        /// README: 공격 성공 — 히트 스톱 발생.
        /// </summary>
        /// <param name="duration">히트스톱 지속 시간 (실시간 초).</param>
        private IEnumerator HitStopRoutine(float duration)
        {
            Time.timeScale = _data.HitStopTimeScale;
            yield return new WaitForSecondsRealtime(duration);

            // timeScale 이 자신이 설정한 값 그대로일 때만 복구
            // (다른 시스템이 변경했을 경우 덮어쓰지 않음)
            if (Mathf.Approximately(Time.timeScale, _data.HitStopTimeScale))
                Time.timeScale = 1f;

            _hitStopCoroutine = null;
        }

        // ══════════════════════════════════════════════════════
        // 콤보 관리
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 콤보 리셋 타이머 시작.
        /// 일정 시간 입력 없으면 콤보 0으로 초기화.
        /// </summary>
        private void StartComboResetTimer()
        {
            if (_comboResetCoroutine != null)
                StopCoroutine(_comboResetCoroutine);

            _comboResetCoroutine = StartCoroutine(ComboResetRoutine());
        }

        /// <summary>
        /// 콤보 리셋 코루틴.
        /// BasicAttackDuration 동안 입력 없으면 _currentCombo 를 0으로 초기화.
        /// </summary>
        private IEnumerator ComboResetRoutine()
        {
            yield return new WaitForSeconds(_data.BasicAttackDuration);
            _currentCombo = 0;
            _comboResetCoroutine = null;
        }

        // ══════════════════════════════════════════════════════
        // 유틸리티
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 현재 공격 방향을 반환한다.
        /// PlayerMoveController.FacingDirection 사용.
        /// README: 공격 방향 = 현재 이동 방향 / 마지막 이동 방향 기준 (마우스 없음).
        /// </summary>
        /// <returns>정규화된 공격 방향 Vector2.</returns>
        private Vector2 GetAttackDirection()
        {
            if (_moveController == null) return Vector2.right;
            return _moveController.FacingDirection.normalized;
        }

        /// <summary>
        /// 현재 콤보 단계에 맞는 봉인도 배율을 반환한다.
        /// ComboSealMultipliers 배열 범위 초과 시 마지막 값 사용.
        /// </summary>
        /// <returns>봉인도 배율 (float).</returns>
        private float GetComboSealMultiplier()
        {
            if (_data.ComboSealMultipliers == null
                || _data.ComboSealMultipliers.Length == 0)
                return 1f;

            int idx = Mathf.Min(_currentCombo, _data.ComboSealMultipliers.Length - 1);
            return _data.ComboSealMultipliers[idx];
        }

        // ══════════════════════════════════════════════════════
        // Gizmos — 에디터 히트박스 시각화
        // ══════════════════════════════════════════════════════

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (_data == null) return;

            Vector2 attackDir = Application.isPlaying
                ? GetAttackDirection()
                : Vector2.right;

            Vector2 hitCenter = (Vector2)transform.position + attackDir * _data.HitboxOffset;

            // 기본 공격 히트박스 (흰색)
            Gizmos.color = new Color(1f, 1f, 1f, 0.4f);
            Gizmos.DrawWireSphere(hitCenter, _data.HitboxRadius);

            // 강공격 히트박스 (노란색)
            Gizmos.color = new Color(1f, 0.8f, 0f, 0.3f);
            Gizmos.DrawWireSphere(hitCenter, _data.HitboxRadius * _data.ChargeHitboxScale);

            // 공격 방향 표시 (청록색)
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(transform.position, (Vector3)attackDir * (_data.HitboxOffset + _data.HitboxRadius));

            // 콤보 / 공격 상태 표시
            UnityEditor.Handles.Label(
                transform.position + Vector3.up * 2f,
                $"콤보: {_currentCombo + 1} / {(_data != null ? _data.MaxComboCount : 0)} | " +
                $"공격중: {_isAttacking} | 윈도우: {_comboWindowOpen}");
        }
#endif
    }
}