// ============================================================
// SealableComponent.cs  v1.0
// 봉인 가능 부위/코어 통합 컴포넌트
//
// [기존 파일 통합]
//   SealGaugeComponent     → 팔 부위 봉인도 관리
//   BossWardenCoreSealGauge → 코어 봉인도 관리
//   두 파일의 역할이 동일 → 하나로 통합
//
// [역할]
//   ① 봉인도 수치 관리 (0 ~ _maxGauge)
//   ② 봉인 저항 배율 적용 (_sealCount 기반)
//   ③ 봉인도 단계 변화 감지 (0/25/50/75/100%)
//   ④ 봉인 가능 상태 도달 시 → SealExecutor 에게 집행 승인 요청 발행
//   ⑤ SealExecutor 가 집행 완료 시 → ExecuteSeal() 호출 처리
//   ⑥ BossWardenCore 가 강제 해제 시 → ForceRelease() 처리
//
// [SealGaugeComponent / BossWardenCoreSealGauge 와의 차이]
//   _isDilPhaseOnly = true  : 딜 페이즈에서만 봉인도 누적 (코어용)
//   _phaseTarget > 0        : 페이즈 목표치 이벤트 발행 (코어용)
//   grade = SealGrade.Part  : 부위 봉인 등급 (Inspector 설정)
//   grade = SealGrade.Core  : 코어 봉인 등급 (Inspector 설정)
//
// [SealExecutor 와의 연결]
//   OnSealRequested 발행 → SealExecutor 가 구독
//   → SealExecutor._sealReadyList 에 등록
//   → 플레이어 접근 + S키 홀드 → ExecuteSeal() 호출
//
// [부착 위치]
//   LeftArm, RightArm : grade = Part
//   Core              : grade = Core, isDilPhaseOnly = true
//   일반 몬스터       : grade = Normal
//
// [namespace] SEAL
// ============================================================

using System;
using UnityEngine;
using DG.Tweening;

namespace SEAL
{
    /// <summary>
    /// 봉인 가능 부위/코어 통합 컴포넌트. (v1.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [봉인도 흐름]
    ///   AddGauge(amount)
    ///     → 저항 배율 적용 후 내부 수치 누적
    ///     → 단계 변화 시 OnStageChanged 발행
    ///     → 100% 도달 시 OnSealRequested 발행 → SealExecutor 에 전달
    ///
    ///   ExecuteSeal() — SealExecutor 에서 집행 완료 후 호출
    ///     → IsSealed = true
    ///     → OnSealCompleted 발행
    ///
    ///   ForceRelease() — BossWardenCore 에서 딜 페이즈 종료 시 호출
    ///     → 봉인도 초기화
    ///     → IsSealed = false
    ///     → OnForceReleased 발행
    ///
    ///   ActivateGauge(bool) — 딜 페이즈 전용 (isDilPhaseOnly = true 일 때)
    ///     → BossWardenCore.OnDilPhaseEnter/Exit 에서 호출
    ///     → false 시 AddGauge 무시
    /// ────────────────────────────────────────────────────
    /// </summary>
    public class SealableComponent : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── 봉인 등급 ──────────────────────")]

        /// <summary>
        /// 이 부위의 봉인 집행 등급.
        /// SealExecutor 에서 등급에 따라 슬로우/연출을 분기한다.
        ///
        /// [설정 기준]
        ///   LeftArm / RightArm : SealGrade.Part
        ///   Core               : SealGrade.Core
        ///   일반 몬스터         : SealGrade.Normal
        /// </summary>
        [Tooltip("봉인 집행 등급. Part=부위 / Core=코어 / Normal=일반")]
        [SerializeField] public SealGrade grade = SealGrade.Part;

        [Header("── 봉인도 수치 ──────────────────────")]

        /// <summary>
        /// 봉인도 내부 최대 요구치.
        /// 이 값에 도달하면 봉인 가능 상태(OnSealRequested 발행).
        /// </summary>
        [Tooltip("봉인도 최대 요구치. 팔: 200 / 코어: 500")]
        [Min(1f)]
        [SerializeField] private float _maxGauge = 200f;

        /// <summary>
        /// 집행 가능 범위 반경 (units).
        /// SealExecutor 가 플레이어 거리 체크 시 참조.
        /// </summary>
        [Tooltip("봉인 집행 접근 범위. 권장: 1.5")]
        [Min(0.1f)]
        [SerializeField] private float _sealRange = 1.5f;

        /// <summary>
        /// S키 홀드 시간 (초).
        /// SealExecutor 가 집행 홀드 시간 체크 시 참조.
        /// </summary>
        [Tooltip("봉인 집행 홀드 시간. 권장: Part=1.5 / Core=3.0")]
        [Min(0.1f)]
        [SerializeField] private float _sealHoldTime = 1.5f;

        [Header("── 코어 전용 옵션 ──────────────────────")]

        /// <summary>
        /// 딜 페이즈에서만 봉인도 누적 허용.
        /// true : BossWardenCore.ActivateGauge() 호출이 있어야 누적
        /// false: 항상 누적 허용 (팔, 일반 몬스터)
        /// </summary>
        [Tooltip("딜 페이즈 전용 여부. Core 오브젝트에만 true.")]
        [SerializeField] private bool _isDilPhaseOnly = false;

        /// <summary>
        /// 페이즈 목표치 (0이면 미사용).
        /// 이 수치 도달 시 OnPhaseTargetReached 발행.
        /// 코어: phase1CoreSealTarget / phase2CoreSealTarget
        /// </summary>
        [Tooltip("페이즈 목표치. 0이면 미사용. 코어용.")]
        [Min(0f)]
        [SerializeField] private float _phaseTarget = 0f;

        [Header("── DataSO ──────────────────────")]

        /// <summary>
        /// BossWardenDataSO 참조.
        /// 봉인 저항 배율 적용에 사용.
        /// BossWardenCore.Initialize() 에서 주입 가능.
        /// </summary>
        [Tooltip("BossWardenDataSO. 봉인 저항 배율 참조.")]
        [SerializeField] private BossWardenDataSO _data;

        [Header("── SpriteRenderer (피격 점멸) ──────────────────────")]

        /// <summary>
        /// 피격 점멸 연출용 SpriteRenderer.
        /// 미연결 시 GetComponent 자동 탐색.
        /// </summary>
        [Tooltip("피격 점멸용 SpriteRenderer. 미연결 시 자동 탐색.")]
        [SerializeField] private SpriteRenderer _spriteRenderer;

        // ══════════════════════════════════════════════════════
        // 프로퍼티 (SealExecutor 참조용)
        // ══════════════════════════════════════════════════════

        /// <summary> 봉인 집행 등급. </summary>
        public SealGrade Grade => grade;

        /// <summary> 봉인 가능 상태 (봉인도 100% 도달). </summary>
        public bool IsSealReady { get; private set; }

        /// <summary> 봉인 완료 상태 (S키 집행 완료). </summary>
        public bool IsSealed { get; private set; }

        /// <summary> 집행 가능 범위 반경. SealExecutor 참조. </summary>
        public float SealRange => _sealRange;

        /// <summary> S키 홀드 시간. SealExecutor 참조. </summary>
        public float SealHoldTime => _sealHoldTime;

        /// <summary> 봉인도 UI 퍼센트 (0~100). </summary>
        public float UIPercent => _maxGauge > 0f
            ? (_currentGauge / _maxGauge) * 100f
            : 0f;

        // ══════════════════════════════════════════════════════
        // 내부 상태
        // ══════════════════════════════════════════════════════

        /// <summary> 현재 봉인도 수치 (0 ~ _maxGauge). </summary>
        private float _currentGauge;

        /// <summary>
        /// 봉인 횟수 (0-based).
        /// 횟수가 증가할수록 봉인 저항 배율 감소.
        /// ForceRelease 시에도 유지 (저항 누적 설계).
        /// </summary>
        private int _sealCount;

        /// <summary> 현재 봉인도 단계 (0~4). </summary>
        private int _currentStage;

        /// <summary>
        /// 딜 페이즈 활성 여부.
        /// _isDilPhaseOnly = true 일 때 false 면 AddGauge 무시.
        /// </summary>
        private bool _isGaugeActive;

        /// <summary> 페이즈 목표 도달 이벤트 발행 완료 여부. </summary>
        private bool _phaseTargetReached;

        /// <summary> 기본 색상 (피격 점멸 복귀 기준). </summary>
        private Color _baseColor;

        /// <summary> 피격 점멸 Tween 핸들. </summary>
        private Tweener _flashTween;

        // ══════════════════════════════════════════════════════
        // 이벤트
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 봉인 가능 상태 도달 시 발행.
        /// SealExecutor 가 구독 → _sealReadyList 에 등록.
        /// 파라미터: 이 SealableComponent 자신.
        /// </summary>
        public event Action<SealableComponent> OnSealRequested;

        /// <summary>
        /// 봉인 집행 완료 시 발행.
        /// BossWardenCore 가 구독 → 그로기 조건 체크.
        /// BossWardenFeedback 이 구독 → 봉인 완료 색상 고정.
        /// </summary>
        public event Action OnSealCompleted;

        /// <summary>
        /// 강제 해제 시 발행.
        /// BossWardenFeedback 이 구독 → 색상 초기화.
        /// </summary>
        public event Action OnForceReleased;

        /// <summary>
        /// 봉인도 수치 변화 시 발행.
        /// 파라미터: UI 퍼센트 (0~100).
        /// </summary>
        public event Action<float> OnGaugeChanged;

        /// <summary>
        /// 봉인도 단계 변화 시 발행.
        /// 파라미터: 새 단계 (0=0%, 1=25%, 2=50%, 3=75%, 4=100%).
        /// BossWardenFeedback 이 구독 → 색상 단계별 전환.
        /// </summary>
        public event Action<int> OnStageChanged;

        /// <summary>
        /// 페이즈 목표치 도달 시 발행 (코어 전용).
        /// _phaseTarget > 0 이고 해당 수치 도달 시 1회 발행.
        /// BossWardenCore 가 구독 → 페이즈 전환 처리.
        /// </summary>
        public event Action OnPhaseTargetReached;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            if (_spriteRenderer == null)
                _spriteRenderer = GetComponent<SpriteRenderer>();

            if (_spriteRenderer != null)
                _baseColor = _spriteRenderer.color;

            // 딜 페이즈 전용이 아니면 즉시 활성
            _isGaugeActive = !_isDilPhaseOnly;
        }

        private void OnDestroy()
        {
            _flashTween?.Kill();
        }

        // ══════════════════════════════════════════════════════
        // 초기화 (BossWardenCore 에서 호출)
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// BossWardenCore.Initialize() 에서 DataSO 주입.
        /// </summary>
        public void Initialize(BossWardenDataSO data)
        {
            _data = data;

            // DataSO 에서 등급에 따라 기본 수치 자동 설정
            if (_data != null)
            {
                switch (grade)
                {
                    case SealGrade.Part:
                        _maxGauge = _data.armSealGaugeMax;
                        _sealRange = _data.sealExecutionRange;
                        _sealHoldTime = _data.sealExecutionHoldTime;
                        break;

                    case SealGrade.Core:
                        _maxGauge = _data.coreSealGaugeMax;
                        _sealRange = _data.coreUnlockRange;
                        _sealHoldTime = _data.coreUnlockHoldTime;
                        break;
                }
            }

            Debug.Log($"[SealableComponent] {gameObject.name} 초기화 완료 | 등급:{grade} 최대:{_maxGauge}");
        }

        // ══════════════════════════════════════════════════════
        // 봉인도 누적 (외부 — BossWardenArmPart, PlayerAttack 등)
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 봉인도를 누적한다.
        ///
        /// [차단 조건]
        ///   IsSealed = true        : 이미 봉인 완료
        ///   _isDilPhaseOnly = true
        ///   + _isGaugeActive = false: 딜 페이즈 외 누적 차단
        ///
        /// [저항 배율]
        ///   _sealCount 기반 BossWardenDataSO.GetSealResistMultiplier() 적용.
        ///   Normal 등급은 저항 없음 (일반 몬스터).
        /// </summary>
        public void AddGauge(float rawAmount)
        {
            if (IsSealed) return;
            if (rawAmount <= 0f) return;
            if (_isDilPhaseOnly && !_isGaugeActive) return;

            // 봉인 저항 배율 적용 (Normal 등급은 저항 없음)
            float multiplier = 1f;
            if (grade != SealGrade.Normal && _data != null)
                multiplier = _data.GetSealResistMultiplier(_sealCount);

            float actualAmount = rawAmount * multiplier;
            _currentGauge = Mathf.Min(_currentGauge + actualAmount, _maxGauge);

            OnGaugeChanged?.Invoke(UIPercent);
            CheckStageChange();
            CheckPhaseTarget();

            // 100% 도달 → 집행 승인 요청 발행
            if (!IsSealReady && _currentGauge >= _maxGauge)
            {
                IsSealReady = true;
                OnSealRequested?.Invoke(this);
                Debug.Log($"[SealableComponent] {gameObject.name} 집행 승인 요청 발행 | 등급:{grade}");
            }
        }

        // ══════════════════════════════════════════════════════
        // 집행 완료 (SealExecutor 에서 호출)
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 봉인 집행 완료 처리.
        /// SealExecutor 가 홀드 완료 시 호출.
        /// </summary>
        public void ExecuteSeal()
        {
            if (!IsSealReady || IsSealed) return;

            IsSealed = true;
            _sealCount++;

            OnSealCompleted?.Invoke();
            PlayHitFlash();
            Debug.Log($"[SealableComponent] {gameObject.name} 봉인 완료 | 저항횟수:{_sealCount}");
        }

        // ══════════════════════════════════════════════════════
        // 강제 해제 (BossWardenCore 에서 호출)
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 봉인을 강제 해제하고 봉인도를 초기화한다.
        /// 딜 페이즈 종료 시 BossWardenCore 에서 호출.
        ///
        /// [저항 횟수 설계]
        ///   resetSealCount = false (기본): 저항 횟수 유지 → 반복 봉인 시 난이도 상승
        ///   resetSealCount = true: 씬 리셋 등 완전 초기화 시만 사용
        /// </summary>
        public void ForceRelease(bool resetSealCount = false)
        {
            _currentGauge = 0f;
            IsSealReady = false;
            IsSealed = false;
            _currentStage = 0;
            _phaseTargetReached = false;

            if (resetSealCount) _sealCount = 0;

            OnGaugeChanged?.Invoke(0f);
            OnForceReleased?.Invoke();
            Debug.Log($"[SealableComponent] {gameObject.name} 강제 해제 | 저항횟수:{(resetSealCount ? "초기화" : "유지")}");
        }

        // ══════════════════════════════════════════════════════
        // 딜 페이즈 활성 제어 (BossWardenCore 에서 호출)
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 딜 페이즈 활성/비활성 전환.
        /// _isDilPhaseOnly = true 인 컴포넌트에만 의미 있음 (코어용).
        /// BossWardenCore.OnDilPhaseEnter/Exit 에서 호출.
        /// </summary>
        public void ActivateGauge(bool isActive)
        {
            _isGaugeActive = isActive;
            Debug.Log($"[SealableComponent] {gameObject.name} 게이지 활성 = {isActive}");
        }

        /// <summary>
        /// 기본 색상 업데이트.
        /// BossWardenFeedback 이 상태 색상 변경 시 호출.
        /// </summary>
        public void UpdateBaseColor(Color newColor)
        {
            _baseColor = newColor;
        }

        // ══════════════════════════════════════════════════════
        // 내부 유틸
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 봉인도 단계 변화 체크 (0/25/50/75/100%).
        /// 단계 전환 시 OnStageChanged 발행.
        /// </summary>
        private void CheckStageChange()
        {
            int newStage = Mathf.FloorToInt(UIPercent / 25f);
            newStage = Mathf.Clamp(newStage, 0, 4);

            if (newStage != _currentStage)
            {
                _currentStage = newStage;
                OnStageChanged?.Invoke(_currentStage);
            }
        }

        /// <summary>
        /// 페이즈 목표치 도달 체크 (코어 전용).
        /// </summary>
        private void CheckPhaseTarget()
        {
            if (_phaseTarget <= 0f) return;
            if (_phaseTargetReached) return;

            if (_currentGauge >= _phaseTarget)
            {
                _phaseTargetReached = true;
                OnPhaseTargetReached?.Invoke();
                Debug.Log($"[SealableComponent] {gameObject.name} 페이즈 목표 도달 ({_phaseTarget})");
            }
        }

        /// <summary>
        /// 피격 점멸 연출.
        /// SetUpdate(true) — 슬로우 중에도 정상 복귀.
        /// </summary>
        private void PlayHitFlash()
        {
            if (_spriteRenderer == null || _data == null) return;

            _flashTween?.Kill();
            _spriteRenderer.color = Color.white;
            _flashTween = _spriteRenderer
                .DOColor(_baseColor, _data.hitFlashDuration)
                .SetEase(Ease.OutCubic)
                .SetUpdate(true);
        }
    }
}