// ============================================================
// BossWardenArmPart.cs  v1.1
// Boss_Warden 팔 부위 컴포넌트
//
// [v1.1 수정]
//   ① Initialize() 이벤트 중복 구독 방지
//       기존: += 만 사용 → Initialize 2회 호출 시 중복 구독 버그
//       수정: 구독 전 -= 먼저 실행하여 중복 방지
//
//   ② OnTriggerEnter2D → PlayerAttackHitboxManager.OnHit 구독 방식으로 전환
//       기존: 팔 Collider2D 의 OnTriggerEnter2D 에서 플레이어 공격 감지
//             → POC08 PlayerAttackHitboxManager 가 OverlapCollider 로
//                Enemy 레이어를 감지하고 OnHit 이벤트를 발행하는 구조와 충돌
//       수정: Start() 에서 PlayerAttackHitboxManager 를 찾아 OnHit 구독
//             OnHit(Collider2D hitCol, float sealAmount) 수신
//             → hitCol 이 이 부위의 Collider2D 와 일치하면 AddGauge 처리
//
//   [PlayerAttackHitboxManager.OnHit 연동 흐름]
//     PlayerAttackHitboxManager.Update()
//       → CheckHit() → OverlapCollider → Enemy 레이어 감지
//       → OnHit?.Invoke(col, sealAmount)
//     BossWardenArmPart.HandlePlayerHit(col, sealAmount)
//       → col 이 _ownCollider 와 일치하면 봉인도 누적 + 점멸
//
// [POC07 참고]
//   TestBossArmPart.cs 의 봉인 상태 관리 구조 참고.
//   → ReLock / ForceUnlock 이진 구조 → SealGaugeComponent 로 교체.
//
// [역할]
//   ① 팔 부위 식별 (LeftArm / RightArm)
//   ② PlayerAttackHitboxManager.OnHit 구독 → 봉인도 누적
//   ③ IsSealed 프로퍼티 — 연결된 패턴 IsAvailable 체크에 사용
//   ④ 봉인 완료 / 해제 이벤트 BossWardenCore 에 전달
//   ⑤ 히트 스탑 점멸 (흰색 점멸 → 원래 색상 복귀)
//
// [부착 위치]
//   LeftArm 오브젝트 / RightArm 오브젝트.
//   SealGaugeComponent 와 함께 부착 필수.
//
// [namespace]
//   namespace : SEAL
// ============================================================

using System;
using UnityEngine;
using DG.Tweening;

namespace SEAL
{
    /// <summary>
    /// Boss_Warden 팔 부위 컴포넌트. (v1.1)
    ///
    /// ────────────────────────────────────────────────────
    /// [봉인 흐름]
    ///   PlayerAttackHitboxManager.OnHit(col, sealAmount)
    ///     → HandlePlayerHit() : col 이 _ownCollider 와 일치하면
    ///     → SealGaugeComponent.AddGauge(sealAmount * recoveryMult)
    ///     → 히트 스탑 점멸
    ///     → SealGaugeComponent.OnSealReady → BossWardenSealExecutor 처리
    ///     → SealGaugeComponent.OnSealed → BossWardenCore.CheckGroggyCondition()
    ///
    /// [연결 패턴 비활성]
    ///   IsSealed = true 시 BossPatternBase.IsAvailable = false
    ///   → BossWardenAI 패턴 선택에서 해당 패턴 스킵
    /// ────────────────────────────────────────────────────
    /// </summary>
    [RequireComponent(typeof(SealGaugeComponent))]
    public class BossWardenArmPart : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── 부위 식별 ──────────────────────")]

        /// <summary>
        /// 팔 타입 (LeftArm / RightArm).
        /// BossWardenCore 에서 양팔 봉인 완료 여부 체크에 사용.
        /// </summary>
        [Tooltip("팔 타입. LeftArm 또는 RightArm 설정.")]
        [SerializeField] private WardenPartType _partType = WardenPartType.LeftArm;

        [Header("── 컴포넌트 연결 ──────────────────────")]

        /// <summary>
        /// 부위 SpriteRenderer.
        /// 히트 스탑 점멸 + 초기 색상 캐싱에 사용.
        /// 미연결 시 GetComponent 자동 탐색.
        /// </summary>
        [Tooltip("SpriteRenderer. 미연결 시 자동 탐색.")]
        [SerializeField] private SpriteRenderer _spriteRenderer;

        [Header("── DataSO ──────────────────────")]

        /// <summary>
        /// BossWardenDataSO.
        /// 히트 점멸 시간 / 색상 참조.
        /// BossWardenCore.Initialize() 에서 주입.
        /// </summary>
        [Tooltip("BossWardenDataSO. BossWardenCore.Initialize 에서 주입.")]
        [SerializeField] private BossWardenDataSO _data;

        // ══════════════════════════════════════════════════════
        // 컴포넌트 참조
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 봉인도 관리 컴포넌트.
        /// Awake 에서 GetComponent 로 자동 취득.
        /// </summary>
        private SealGaugeComponent _sealGauge;

        /// <summary>
        /// 이 부위 오브젝트의 Collider2D.
        /// PlayerAttackHitboxManager.OnHit 에서 전달된 col 과 대조하여
        /// 이 부위에 적중한 공격인지 판별.
        /// Awake 에서 GetComponent 로 자동 취득.
        /// </summary>
        private Collider2D _ownCollider;

        /// <summary>
        /// 씬 내 PlayerAttackHitboxManager 참조.
        /// Start() 에서 FindObjectsByType 으로 탐색.
        /// OnHit 이벤트 구독 대상.
        /// </summary>
        private PlayerAttackHitboxManager _hitboxManager;

        // ══════════════════════════════════════════════════════
        // 내부 상태
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Recovery 취약 구간 활성 여부.
        /// true → AddGauge 시 recoveryVulnMultiplier 적용.
        /// BossWardenAI 가 Recovery 상태 진입/종료 시 SetRecoveryVuln() 호출.
        /// </summary>
        private bool _isRecoveryVuln;

        /// <summary>
        /// 패턴 공략 타임 (팔 분리 구간) 활성 여부.
        /// BossPattern_Slam / Sweep 에서 SetSlamVuln(true, mult) 호출 시 활성.
        /// </summary>
        private bool _isSlamVuln;

        /// <summary>
        /// 공략 타임 봉인도 배율.
        /// Slam 공략 타임 = 2.0 / Sweep 날리기 타임 = 1.5 / 기본 = 1.0.
        /// </summary>
        private float _slamVulnMultiplier = 1.0f;

        /// <summary>
        /// 기본 색상 캐시 (Awake 에서 저장).
        /// 히트 점멸 후 복귀 색상.
        /// BossWardenFeedback 이 색상을 바꿀 때 UpdateBaseColor() 로 동기화 필요.
        /// </summary>
        private Color _baseColor;

        /// <summary> 현재 히트 점멸 Tween 핸들. </summary>
        private Tweener _flashTween;

        // ══════════════════════════════════════════════════════
        // 이벤트
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 봉인 집행 완료 시 발행 (SealGaugeComponent.OnSealed 를 래핑).
        /// 파라미터: 봉인된 팔 타입.
        /// BossWardenCore 가 구독 → 그로기 조건 체크.
        /// </summary>
        public event Action<WardenPartType> OnPartSealed;

        /// <summary>
        /// 봉인 강제 해제 시 발행.
        /// BossWardenCore 가 구독 → 상태 동기화.
        /// </summary>
        public event Action<WardenPartType> OnPartReleased;

        // ══════════════════════════════════════════════════════
        // 프로퍼티
        // ══════════════════════════════════════════════════════

        /// <summary> 팔 타입. </summary>
        public WardenPartType PartType => _partType;

        /// <summary>
        /// 봉인 완료 여부.
        /// BossPatternBase.IsAvailable 체크에 사용.
        /// SealGaugeComponent.IsSealed 를 래핑.
        /// </summary>
        public bool IsSealed => _sealGauge != null && _sealGauge.IsSealed;

        /// <summary>
        /// 봉인 가능 상태 여부 (봉인도 100%).
        /// </summary>
        public bool IsSealReady => _sealGauge != null && _sealGauge.IsSealReady;

        /// <summary>
        /// SealGaugeComponent 직접 참조.
        /// BossWardenSealExecutor 에서 집행 완료 처리 시 사용.
        /// </summary>
        public SealGaugeComponent SealGauge => _sealGauge;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            // 컴포넌트 자동 취득
            _sealGauge = GetComponent<SealGaugeComponent>();
            _ownCollider = GetComponent<Collider2D>();

            if (_spriteRenderer == null)
                _spriteRenderer = GetComponent<SpriteRenderer>();

            // 기본 색상 캐싱
            if (_spriteRenderer != null)
                _baseColor = _spriteRenderer.color;
        }

        private void Start()
        {
            // PlayerAttackHitboxManager 탐색 및 OnHit 구독
            // FindObjectsByType: 씬에 단 1개 존재하는 컴포넌트 탐색 (성능 무관, 1회만 호출)
            var managers = FindObjectsByType<PlayerAttackHitboxManager>(FindObjectsSortMode.None);
            if (managers.Length > 0)
            {
                _hitboxManager = managers[0];
                _hitboxManager.OnHit += HandlePlayerHit;
                Debug.Log($"[BossWardenArmPart] {_partType} — PlayerAttackHitboxManager.OnHit 구독 완료");
            }
            else
            {
                Debug.LogWarning($"[BossWardenArmPart] {_partType} — PlayerAttackHitboxManager 를 씬에서 찾을 수 없습니다. 봉인도 누적 불가.");
            }
        }

        private void OnDestroy()
        {
            // SealGauge 이벤트 구독 해제
            if (_sealGauge != null)
            {
                _sealGauge.OnSealed -= HandleSealed;
                _sealGauge.OnReleased -= HandleReleased;
            }

            // PlayerAttackHitboxManager OnHit 구독 해제
            if (_hitboxManager != null)
                _hitboxManager.OnHit -= HandlePlayerHit;

            // DOTween 정리
            _flashTween?.Kill();
        }

        // ══════════════════════════════════════════════════════
        // 초기화
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 초기화. BossWardenCore.Start() 에서 호출.
        /// DataSO 주입 + SealGaugeComponent 초기화 + 이벤트 연결.
        ///
        /// [이벤트 중복 구독 방지]
        ///   Initialize() 가 여러 번 호출될 수 있으므로
        ///   구독 전 -= 먼저 실행하여 중복 방지.
        ///   (같은 delegate 를 -= 하면 미구독 상태여도 오류 없음)
        ///
        /// [호출 순서]
        ///   BossWardenCore.Start()
        ///     → _armL.Initialize(data)
        ///     → _armR.Initialize(data)
        /// </summary>
        /// <param name="data">BossWardenDataSO 참조.</param>
        public void Initialize(BossWardenDataSO data)
        {
            _data = data;

            // SealGaugeComponent 초기화
            if (_sealGauge != null)
                _sealGauge.Initialize(data, data.armSealGaugeMax);

            // 이벤트 중복 구독 방지: 먼저 해제 후 구독
            if (_sealGauge != null)
            {
                _sealGauge.OnSealed -= HandleSealed;
                _sealGauge.OnReleased -= HandleReleased;
                _sealGauge.OnSealed += HandleSealed;
                _sealGauge.OnReleased += HandleReleased;
            }

            Debug.Log($"[BossWardenArmPart] {_partType} 초기화 완료");
        }

        // ══════════════════════════════════════════════════════
        // 피격 처리 — PlayerAttackHitboxManager.OnHit 수신
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// PlayerAttackHitboxManager.OnHit 이벤트 수신 핸들러.
        /// 씬 내 모든 BossWardenArmPart 가 동일 이벤트를 받으므로
        /// hitCol 이 이 부위의 _ownCollider 와 일치하는지 먼저 확인.
        ///
        /// [흐름]
        ///   PlayerAttackHitboxManager.CheckHit()
        ///     → OverlapCollider 로 Enemy 레이어 감지
        ///     → OnHit?.Invoke(col, sealAmount)
        ///   HandlePlayerHit(col, sealAmount)
        ///     → col == _ownCollider 확인
        ///     → AddGauge + 점멸
        ///
        /// [POC07과의 차이]
        ///   POC07: OnTriggerEnter2D 에서 SealProjectile 직접 감지
        ///   POC08: PlayerAttackHitboxManager 이벤트 구독 방식
        ///          → 히트박스 판정 로직이 HitboxManager 에 집중되어
        ///             중복 히트 방지 / 봉인도 누적량 관리가 일원화됨
        /// </summary>
        /// <param name="hitCol">적중된 Collider2D.</param>
        /// <param name="sealAmount">봉인도 누적량 (PlayerAttackHitboxManager 에서 결정).</param>
        private void HandlePlayerHit(Collider2D hitCol, float sealAmount)
        {
            // 이 부위의 Collider 와 일치하는지 확인
            if (hitCol != _ownCollider) return;

            // 봉인 완료 상태면 무시
            if (IsSealed) return;

            float rawAmount = sealAmount;

            // Recovery 취약 구간 배율 적용
            if (_isRecoveryVuln && _data != null)
                rawAmount *= _data.recoveryVulnMultiplier;

            // 패턴 공략 타임 배율 적용 (Slam/Sweep 팔 분리 구간)
            // RecoveryVuln 과 중복 적용되지 않도록 else if 처리
            // → 공략 타임은 패턴 Active 구간이지 Recovery 구간이 아니므로 별개
            if (_isSlamVuln)
                rawAmount *= _slamVulnMultiplier;

            // 봉인도 누적
            if (_sealGauge != null)
                _sealGauge.AddGauge(rawAmount);

            // 히트 스탑 점멸
            PlayHitFlash();
        }

        // ══════════════════════════════════════════════════════
        // 이벤트 핸들러
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// SealGaugeComponent.OnSealed 수신.
        /// OnPartSealed 이벤트를 BossWardenCore 에 전달.
        /// </summary>
        private void HandleSealed()
        {
            OnPartSealed?.Invoke(_partType);
            Debug.Log($"[BossWardenArmPart] {_partType} 봉인 완료 이벤트 발행");
        }

        /// <summary>
        /// SealGaugeComponent.OnReleased 수신.
        /// OnPartReleased 이벤트를 BossWardenCore 에 전달.
        /// </summary>
        private void HandleReleased()
        {
            OnPartReleased?.Invoke(_partType);
            Debug.Log($"[BossWardenArmPart] {_partType} 봉인 해제 이벤트 발행");
        }

        // ══════════════════════════════════════════════════════
        // 히트 스탑 점멸
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 히트 스탑 점멸 연출.
        /// SpriteRenderer 를 흰색으로 순간 전환 후 기본 색상으로 복귀.
        ///
        /// [구현 원칙]
        ///   Sprite / Particle 없음 → SpriteRenderer 점멸로 타격감 표현.
        ///   SetUpdate(true) 로 TimeScale 영향 없이 실시간 복귀.
        ///   (히트스탑으로 Time.timeScale 이 낮아져 있어도 정상 복귀)
        /// </summary>
        private void PlayHitFlash()
        {
            if (_spriteRenderer == null) return;
            if (_data == null) return;

            // 진행 중인 점멸 중단
            _flashTween?.Kill();

            // 흰색 순간 전환 → 기본 색상 DOTween 복귀
            _spriteRenderer.color = Color.white;
            _flashTween = _spriteRenderer
                .DOColor(_baseColor, _data.hitFlashDuration)
                .SetEase(Ease.OutCubic)
                .SetUpdate(true);
        }

        // ══════════════════════════════════════════════════════
        // 외부 API
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Recovery 취약 구간 활성화.
        /// BossWardenAI 가 Recovery 상태 진입/종료 시 호출.
        /// </summary>
        public void SetRecoveryVuln(bool isVuln)
        {
            _isRecoveryVuln = isVuln;
        }

        /// <summary>
        /// 패턴 공략 타임 전용 봉인도 배율 설정.
        /// BossPattern_Slam / BossPattern_Sweep 에서 팔 분리 구간 동안 호출.
        ///
        /// [RecoveryVuln 과의 차이]
        ///   RecoveryVuln : DataSO.recoveryVulnMultiplier 고정 배율 (보통 1.5)
        ///   SlamVuln     : 패턴별 자유 배율 (Slam = 2.0, Sweep = 1.5)
        ///                  팔이 분리된 공략 타임에만 활성
        /// </summary>
        public void SetSlamVuln(bool isActive, float multiplier)
        {
            _isSlamVuln = isActive;
            _slamVulnMultiplier = multiplier;
        }

        /// <summary>
        /// 기본 색상 업데이트.
        /// BossWardenFeedback 이 봉인도 단계 색상을 변경할 때 호출.
        /// 히트 점멸 후 복귀 색상 동기화.
        /// </summary>
        public void UpdateBaseColor(Color newColor)
        {
            _baseColor = newColor;
        }

        /// <summary>
        /// 봉인 강제 해제.
        /// BossWardenCore.ExitDilPhase() 에서 딜 페이즈 종료 시 호출.
        /// </summary>
        /// <param name="resetSealCount">저항 횟수 초기화 여부. 기본 false (유지 권장).</param>
        public void ForceRelease(bool resetSealCount = false)
        {
            if (_sealGauge != null)
                _sealGauge.ForceRelease(resetSealCount);
        }

        // ══════════════════════════════════════════════════════
        // Gizmos — 에디터 시각화
        // ══════════════════════════════════════════════════════

        private void OnDrawGizmosSelected()
        {
            bool sealed_ = _sealGauge != null && _sealGauge.IsSealed;
            bool ready = _sealGauge != null && _sealGauge.IsSealReady;

            if (sealed_)
                Gizmos.color = new Color(0f, 0.27f, 0.8f, 0.5f);
            else if (ready)
                Gizmos.color = new Color(0f, 0.53f, 1f, 0.5f);
            else
                Gizmos.color = new Color(0.8f, 0.4f, 0.1f, 0.3f);

            Gizmos.DrawWireSphere(transform.position, 0.6f);

#if UNITY_EDITOR
            string state = sealed_ ? "봉인완료" : (ready ? "집행가능" : "진행중");
            UnityEditor.Handles.Label(
                transform.position + Vector3.up * 0.8f,
                $"{_partType} [{state}]");
#endif
        }
    }

    // ══════════════════════════════════════════════════════
    // 팔 타입 열거형
    // ══════════════════════════════════════════════════════

    /// <summary>
    /// Warden 부위 타입.
    /// BossWardenCore 에서 L/R 구분 + 그로기 조건 체크에 사용.
    /// </summary>
    public enum WardenPartType
    {
        /// <summary> 왼팔. Slam / Sweep 연결. </summary>
        LeftArm,

        /// <summary> 오른팔. Charge / GuardBreak 연결. </summary>
        RightArm,

        /// <summary> 코어. 양팔 봉인 후 활성. </summary>
        Core,
    }
}