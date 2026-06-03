// ============================================================
// BossPattern_Charge.cs  v1.2
// Boss_Warden 돌진 패턴
//
// [v1.2 변경 — transform.position → _rigid2D.position 수정]
//   🔴 버그 수정: OnActive() 거리 계산 + CheckChargeHit() 위치 기준 오류
//
//   [원인]
//     Patterns 오브젝트는 Boss_Warden 의 자식.
//     BossPattern_Charge 는 Patterns 에 부착되어 있음.
//     따라서 transform 은 Patterns 오브젝트 자신을 가리킴.
//     Boss_Warden(부모)이 이동하면 Patterns(자식)도 함께 이동.
//     → _chargeStartPos = transform.position 저장 시점과
//       루프 내 transform.position 이 항상 같은 위치
//       → distanceTraveled = 0 → chargeDistance 도달 불가 → while(true) 무한루프
//
//   [수정]
//     _chargeStartPos   : transform.position → _rigid2D.position
//     distanceTraveled  : transform.position → _rigid2D.position
//     CheckChargeHit()  : (Vector2)transform.position → _rigid2D.position
//     _rigid2D 는 GetComponentInParent 로 Boss_Warden 본체의 Rigidbody2D 참조
//     → Boss_Warden 의 실제 월드 위치를 정확히 반환
//
// [v1.1 변경 — 버그 수정 + 레이어 구조 적용]
//   🔴 버그 수정: Awake() 에서 _triggerGroggyOnRecovery = true 강제 덮어쓰기 제거
//       기존: Awake() 에서 코드로 true 강제 설정
//             → Inspector 직렬화값(false) 을 덮어써서
//               Charge 패턴이 Recovery 완료마다 그로기를 무한 발행하는 버그
//       수정: Awake() 의 강제 설정 코드 제거
//             → Inspector 에서 _triggerGroggyOnRecovery 값을 그대로 사용
//             → Prefab Inspector 에서 원하는 값으로 설정 가능
//
//   레이어 명시: _playerLayer Tooltip → PlayerAttackHitBox 레이어 명시
//       기존: "Player 레이어 마스크"
//       변경: "PlayerAttackHitBox 레이어 마스크"
//             → EnemyAttack(패턴 OverlapXX) 이 PlayerAttackHitBox 를 감지
//             → 플레이어 HurtBox 의 레이어 = PlayerAttackHitBox 필수
//
// [1페이즈 흐름]
//   Warning (0.8초)
//     → 플레이어 방향 계산 후 고정
//     → LineRenderer 돌진 예고선 표시
//     → 본체 주황 Pulse DOColor
//
//   Active
//     → 예고선 제거
//     → Rigidbody2D.linearVelocity = direction × chargeSpeed
//     → 거리 도달 or 벽 충돌 시 종료
//     → OverlapBox 히트박스 체크 (매 frame)
//     → 플레이어 피격 시 Debug.Log (추후 PlayerHitReceiver 연동)
//
//   Recovery (0.8초)
//     → linearVelocity = 0
//     → DOShakePosition 충격 연출
//     → 취약 구간 표시 (붉은 페이드)
//     → _triggerGroggyOnRecovery = true 이면 OnPatternGroggy 발행
//
// [2페이즈 강화]
//   Active: 속도 phase2ChargeSpeed
//   Recovery: 즉시 스킵 → Slam 패턴 연계 (AI 처리)
//
// [레이어 구조]
//   _playerLayer = PlayerAttackHitBox 레이어
//   → 플레이어 HurtBox 오브젝트의 Layer = PlayerAttackHitBox 설정 필수
//   → Warden 패턴(EnemyAttack)이 PlayerAttackHitBox 레이어를 감지
//
// [namespace]
//   namespace : SEAL
// ============================================================

using System.Collections;
using UnityEngine;
using DG.Tweening;

namespace SEAL
{
    /// <summary>
    /// Boss_Warden 돌진 패턴. (v1.1)
    ///
    /// ────────────────────────────────────────────────────
    /// [연결 부위] 오른팔 (RightArm)
    /// [그로기 유발] Inspector 의 _triggerGroggyOnRecovery 값에 따름
    ///              (Prefab 에서 직접 설정. 코드 강제 없음)
    /// [2페이즈] 돌진 속도 증가 + Recovery 스킵
    /// ────────────────────────────────────────────────────
    /// </summary>
    public class BossPattern_Charge : BossPatternBase
    {
        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── 컴포넌트 연결 ──────────────────────")]

        /// <summary>
        /// 예고 범위 표시 컴포넌트.
        /// 미연결 시 GetComponentInParent 로 자동 탐색.
        /// </summary>
        [Tooltip("BossWardenAttackRange. 미연결 시 자동 탐색.")]
        [SerializeField] private BossWardenAttackRange _attackRange;

        /// <summary>
        /// 보스 Rigidbody2D.
        /// 미연결 시 GetComponentInParent 로 자동 탐색.
        /// </summary>
        [Tooltip("Rigidbody2D. 미연결 시 자동 탐색.")]
        [SerializeField] private Rigidbody2D _rigid2D;

        /// <summary>
        /// BossWardenAI 참조.
        /// 플레이어 방향/위치 참조.
        /// </summary>
        [Tooltip("BossWardenAI. 미연결 시 자동 탐색.")]
        [SerializeField] private BossWardenAI _ai;

        [Header("── DataSO ──────────────────────")]

        [Tooltip("BossWardenDataSO.")]
        [SerializeField] private BossWardenDataSO _data;

        [Header("── 히트박스 ──────────────────────")]

        /// <summary>
        /// 플레이어 피격 감지 레이어 마스크.
        /// PlayerAttackHitBox 레이어 선택.
        ///
        /// [레이어 구조]
        ///   Warden 패턴(EnemyAttack) 이 PlayerAttackHitBox 레이어를 감지.
        ///   플레이어 HurtBox 오브젝트의 Layer = PlayerAttackHitBox 설정 필수.
        /// </summary>
        [Tooltip("플레이어 피격 감지 레이어. PlayerAttackHitBox 레이어 선택. 플레이어 HurtBox Layer와 일치해야 함.")]
        [SerializeField] private LayerMask _playerLayer;

        // ══════════════════════════════════════════════════════
        // 내부 상태
        // ══════════════════════════════════════════════════════

        /// <summary> Warning 시 계산한 고정 돌진 방향. </summary>
        private Vector2 _chargeDirection;

        /// <summary> 돌진 시작 위치 (거리 계산용). </summary>
        private Vector2 _chargeStartPos;

        /// <summary> 현재 2페이즈 여부. </summary>
        private bool _isPhase2;

        /// <summary> 이번 Active 에서 이미 플레이어를 피격했는지. </summary>
        private bool _hasHitPlayer;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            if (_attackRange == null)
                _attackRange = GetComponentInParent<BossWardenAttackRange>();
            if (_rigid2D == null)
                _rigid2D = GetComponentInParent<Rigidbody2D>();
            if (_ai == null)
                _ai = GetComponentInParent<BossWardenAI>();

            // ✅ v1.1 수정: _triggerGroggyOnRecovery 강제 설정 코드 제거
            // 기존 코드: _triggerGroggyOnRecovery = true;
            // 이유: Awake() 에서 코드로 강제 설정하면 Inspector 직렬화값을 덮어씀
            //       → Prefab 에서 false 로 설정해도 항상 true 가 되어 버그 발생
            //       → Inspector 값을 그대로 사용하도록 수정 (BossPatternBase 기본값: true)
        }

        // ══════════════════════════════════════════════════════
        // BossPatternBase 오버라이드
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 2페이즈 활성화 시 속도 증가 + Recovery 스킵 플래그 설정.
        /// BossWardenAI.HandlePhaseChanged(2) 에서 호출.
        /// </summary>
        public new void UnlockPhase2()
        {
            base.UnlockPhase2();
            _isPhase2 = true;
        }

        // ══════════════════════════════════════════════════════
        // Warning — 예고선 + 방향 고정
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnWarning()
        {
            if (_data == null || _ai == null) yield break;

            // 플레이어 방향 계산 후 고정 (Warning 중 변경 없음)
            _chargeDirection = _ai.FacingDir;

            // LineRenderer 돌진 예고선 표시
            _attackRange?.ShowChargeLine(
                transform.position,
                _chargeDirection,
                _data.chargeWarningSize.y);

            // Warning 대기 (중단 체크 포함)
            yield return StartCoroutine(WaitForPattern(_warningDuration));
        }

        // ══════════════════════════════════════════════════════
        // Active — 돌진 + 히트박스
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnActive()
        {
            if (_isInterrupted) yield break;
            if (_data == null || _rigid2D == null) yield break;

            // 예고선 제거
            _attackRange?.HideChargeLine();

            // 돌진 시작
            // ✅ v1.2 수정: transform.position → _rigid2D.position
            //   이유: Patterns 오브젝트(자식)는 Boss_Warden(부모)이 이동할 때 함께 이동.
            //         transform.position 은 Patterns 의 로컬 기준 월드좌표이므로
            //         부모가 이동해도 자식과의 상대 거리가 항상 0 → 거리 계산 불가.
            //         _rigid2D.position 은 실제 Rigidbody2D(Boss_Warden 본체)의 월드좌표.
            _chargeStartPos = _rigid2D.position;
            _hasHitPlayer = false;

            float speed = _isPhase2 ? _data.phase2ChargeSpeed : _data.chargeSpeed;
            _rigid2D.linearVelocity = _chargeDirection * speed;

            // 거리 / 피격 체크 루프
            while (true)
            {
                if (_isInterrupted)
                {
                    _rigid2D.linearVelocity = Vector2.zero;
                    yield break;
                }

                // ✅ v1.2 수정: transform.position → _rigid2D.position (동일 이유)
                float distanceTraveled = Vector2.Distance(_chargeStartPos, _rigid2D.position);

                // 히트박스 체크 (이번 Active 에서 1회만)
                if (!_hasHitPlayer)
                    CheckChargeHit();

                // 돌진 거리 도달
                if (distanceTraveled >= _data.chargeDistance)
                {
                    _rigid2D.linearVelocity = Vector2.zero;
                    yield break;
                }

                yield return null;
            }
        }

        /// <summary>
        /// OverlapBox 로 플레이어 피격 체크.
        /// 실제 히트박스 = chargeHitboxSize (chargeWarningSize 보다 작음).
        ///
        /// [감지 대상]
        ///   _playerLayer = PlayerAttackHitBox 레이어.
        ///   플레이어 HurtBox 오브젝트가 이 레이어여야 감지됨.
        ///
        /// [피격 처리]
        ///   현재: Debug.Log 출력 (추후 PlayerHitReceiver 연동 예정).
        ///   STEP 09 완료 후 PlayerHealth.TakeDamage() 연동.
        /// </summary>
        private void CheckChargeHit()
        {
            if (_data == null) return;

            float angle = Mathf.Atan2(_chargeDirection.y, _chargeDirection.x) * Mathf.Rad2Deg;

            // 히트박스 중심 = 보스 본체 위치 + 방향 × (히트박스 길이/2)
            // ✅ v1.2 수정: (Vector2)transform.position → _rigid2D.position
            //   이유: transform 은 Patterns 자식 오브젝트 기준 → 부모 이동 시에도 함께 이동
            //         _rigid2D.position 은 Rigidbody2D(Boss_Warden 본체) 월드좌표
            Vector2 boxCenter = _rigid2D.position
                + _chargeDirection * (_data.chargeHitboxSize.y * 0.5f);

            Collider2D hit = Physics2D.OverlapBox(
                boxCenter,
                _data.chargeHitboxSize,
                angle,
                _playerLayer);

            if (hit != null)
            {
                _hasHitPlayer = true;
                // 플레이어 피격 처리 (추후 PlayerHitReceiver 연동)
                Debug.Log("[BossPattern_Charge] 플레이어 피격 — PlayerAttackHitBox 감지");
            }
        }

        // ══════════════════════════════════════════════════════
        // Recovery — 충격 연출
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnRecovery()
        {
            if (_isInterrupted) yield break;

            // 2페이즈: Recovery 스킵 (즉시 종료 → Slam 연계는 AI 가 처리)
            if (_isPhase2) yield break;

            // 충격 연출 — DOShakePosition
            transform.DOShakePosition(
                duration: 0.3f,
                strength: 0.3f,
                vibrato: 10,
                randomness: 90f)
                .SetUpdate(true);

            yield return StartCoroutine(WaitForPattern(_recoveryDuration));
        }

        // ══════════════════════════════════════════════════════
        // Interrupt 오버라이드 — 돌진 강제 정지
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 돌진 중 강제 중단.
        /// Rigidbody2D 속도 즉시 0 + 예고선 제거.
        /// </summary>
        public override void Interrupt()
        {
            if (_rigid2D != null)
                _rigid2D.linearVelocity = Vector2.zero;

            _attackRange?.HideChargeLine();
            base.Interrupt();
        }
    }
}