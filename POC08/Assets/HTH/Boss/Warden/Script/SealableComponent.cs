// ============================================================
// SealableComponent.cs  v2.0
// 봉인 가능 부위/코어 통합 컴포넌트 — 범용 리팩토링
//
// [v2.0 — BossDataSO 참조 + 색상 점진 보간 + Loop 파티클]
//
//   [변경 1] BossWardenDataSO → BossDataSO 참조로 교체
//     기존: _data (BossWardenDataSO) 직접 참조
//     변경: _bossData (BossDataSO) 참조
//           봉인 저항 → _bossData.SealData.GetSealResistMultiplier()
//           색상      → _bossData.ColorData.GetPartColor()
//     효과: 어떤 보스에게도 이 컴포넌트 그대로 재사용 가능
//
//   [변경 2] 색상 점진 보간 방식으로 전환
//     기존: OnStageChanged 이벤트 → BossWardenFeedback 이 단계별 색상 적용
//           25/50/75/100% 구간에서 갑자기 색상 바뀜 (띡띡)
//     변경: AddGauge() 호출마다 SealColorDataSO.GetPartColor(UIPercent) 로
//           colorBase → colorFull 선형 보간 색상 계산
//           SpriteRenderer.DOColor(targetColor, colorLerpDuration) 로
//           봉인도 수치가 오를수록 서서히 물들어 가는 방식
//
//   [변경 3] 피격 점멸 색상 교체
//     기존: DOColor(Color.white) → 흰색 점멸 → 봉인도 색상 정보 유실
//     변경: DOColor(colorHitFlash) → 봉인도 보라 계열 밝은 색 점멸
//           점멸 종료 후 현재 봉인도 비율 색상으로 자동 복귀
//           패턴 사용 후 색상 초기화 문제 해결
//
//   [변경 4] 봉인 완료 Loop 파티클 추가
//     기존: 봉인 완료 시 색상 고정만
//     변경: ExecuteSeal() 완료 시
//           SealColorDataSO.sealedParticlePrefab Instantiate
//           자기 Transform 자식으로 부착 (부위 이동 시 자동 따라감)
//           ForceRelease() 시 파티클 비활성/파괴
//
//   [변경 5] _maxGauge Inspector 제거 → BossDataSO 에서 주입
//     기존: Inspector 에서 _maxGauge 직접 설정
//     변경: grade = Part  → _bossData.SealData.partSealGaugeMax 자동 적용
//           grade = Core  → Initialize(maxGauge) 로 외부 주입
//           grade = Normal → Inspector _maxGauge 유지 (일반 몬스터용)
//
//   [v2.1 유지 사항]
//     ForceRelease 저항 횟수 유지 원칙
//     ActivateGauge (코어 전용 딜 페이즈 활성)
//
//   [Step 31]
//     팔/코어 자식으로 배치된 봉인 완료 ParticleSystem 연결 지원
//     OnStageChanged 이벤트 유지 (외부 UI 연동용)
//     CheckPhaseTarget (코어 페이즈 목표치)
//
// [부착 위치]
//   Boss_LeftArm  : grade = Part
//   Boss_RightArm : grade = Part
//   Boss_Core     : grade = Core, isDilPhaseOnly = true
//   일반 몬스터   : grade = Normal
//
// [namespace] SEAL
// ============================================================

using System;
using UnityEngine;
using DG.Tweening;

namespace SEAL
{
    /// <summary>
    /// 봉인 가능 부위/코어 통합 컴포넌트. (v2.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [봉인도 색상 변화 흐름]
    ///   AddGauge() 호출
    ///     → UIPercent 계산
    ///     → ColorData.GetPartColor(UIPercent) 보간 색상 계산
    ///     → DOColor(targetColor, colorLerpDuration) 서서히 변화
    ///     → 봉인도가 오를수록 colorBase → colorFull 로 물들어 감
    ///
    /// [피격 점멸 — 색상 초기화 방지]
    ///   colorHitFlash 로 점멸 (흰색 금지)
    ///   점멸 종료 후 현재 봉인도 비율 색상으로 자동 복귀
    ///
    /// [봉인 완료]
    ///   colorSealed 고정 + Loop 파티클 활성
    ///   ForceRelease 시 colorBase 복귀 + 파티클 파괴
    ///
    /// [외부 API]
    ///   AddGauge(float)         봉인도 누적
    ///   ExecuteSeal()           집행 완료 처리
    ///   ForceRelease(bool)      강제 해제
    ///   ActivateGauge(bool)     딜 페이즈 활성 (코어 전용)
    ///   Initialize(float)       최대치 외부 주입 (코어 전용)
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
        /// BossSealManager에서 집행 분기에 사용.
        ///
        ///   Part   : 팔 등 부위 봉인
        ///   Core   : 코어 봉인
        ///   Normal : 일반 몬스터
        /// </summary>
        [Tooltip("봉인 집행 등급. Part=부위 / Core=코어 / Normal=일반.")]
        [SerializeField] public SealGrade grade = SealGrade.Part;

        [Header("── DataSO (필수) ──────────────────────")]

        /// <summary>
        /// 범용 보스 DataSO.
        /// SealData (봉인도 수치 / 저항 배율) 와
        /// ColorData (색상 보간 / DOTween 수치) 를 통합 참조.
        ///
        /// [v2.0 변경]
        ///   기존 BossWardenDataSO → BossDataSO 로 교체.
        ///   어떤 보스든 이 컴포넌트 재사용 가능.
        ///
        /// BossDataManager/BossSealManager 초기화에서 주입 or Inspector 직접 연결.
        /// </summary>
        [Tooltip("BossDataSO. 봉인도 수치 + 색상 참조. 필수.")]
        [SerializeField] private BossDataSO _bossData;

        [Header("── 집행 수치 (Inspector 설정) ──────────────────────")]

        /// <summary>
        /// 봉인 집행 가능 범위 반경 (units).
        /// BossSealManager가 플레이어 접근/일섬 대상 선택 시 참조.
        /// 즉시 집행/일섬 접근 범위로 사용.
        ///
        /// [권장값] 1.5
        /// </summary>
        [Tooltip("봉인 집행 접근 범위. 권장: 1.5.")]
        [Min(0.1f)]
        [SerializeField] private float _sealRange = 1.5f;

        [Header("── Normal 등급 전용 수치 ──────────────────────")]

        /// <summary>
        /// Normal 등급 전용 최대 봉인도.
        /// Part / Core 등급은 BossDataSO 에서 자동 적용.
        /// 일반 몬스터처럼 BossDataSO 없이 단독 사용 시에만 이 값 적용.
        /// </summary>
        [Tooltip("Normal 등급 전용 최대 봉인도. Part/Core 는 BossDataSO 에서 자동 적용.")]
        [Min(1f)]
        [SerializeField] private float _normalMaxGauge = 100f;

        [Header("── 코어 전용 옵션 ──────────────────────")]

        /// <summary>
        /// 딜 페이즈에서만 봉인도 누적 허용.
        /// true : ActivateGauge(true) 호출 후에만 AddGauge 유효.
        /// false: 항상 누적 허용 (팔, 일반 몬스터).
        /// </summary>
        [Tooltip("딜 페이즈 전용 여부. Core = true / 팔 = false.")]
        [SerializeField] private bool _isDilPhaseOnly = false;

        /// <summary>
        /// 페이즈 목표치 (0이면 미사용).
        /// 이 수치 도달 시 OnPhaseTargetReached 발행.
        /// BossSealManager에서 페이즈 전환 판단에 사용.
        /// </summary>
        [Tooltip("페이즈 목표치. 0이면 미사용. 코어용.")]
        [Min(0f)]
        [SerializeField] private float _phaseTarget = 0f;


        [Header("── 봉인 가능 로컬 파티클 ──────────────────────")]

        [Tooltip("봉인도 100% 도달 시 재생할 Ready 표시 ParticleSystem. 기존 LineRenderer 범위 표시 대체용입니다.")]
        [SerializeField] private ParticleSystem[] _sealReadyLocalParticles;

        [Tooltip("true면 자식 ParticleSystem 중 이름에 Ready/SealReady/Available 이 들어간 파티클을 자동 수집합니다.")]
        [SerializeField] private bool _autoCollectLocalReadyParticles = true;

        [Tooltip("시작 시 로컬 Ready 파티클을 Stop+Clear 합니다.")]
        [SerializeField] private bool _clearReadyParticlesOnAwake = true;

        [Header("── 봉인 완료 로컬 파티클 ──────────────────────")]

        [Tooltip("봉인 완료 시 재생할 로컬 ParticleSystem. 각 팔 자식 파티클을 여기에 연결하세요.")]
        [SerializeField] private ParticleSystem[] _sealedLocalParticles;

        [Tooltip("true면 자식 ParticleSystem을 자동 수집합니다. 팔마다 하나씩 붙어있는 봉인 파티클 연결용.")]
        [SerializeField] private bool _autoCollectLocalSealedParticles = true;

        [Tooltip("시작 시 로컬 봉인 파티클을 Stop+Clear 합니다.")]
        [SerializeField] private bool _clearLocalParticlesOnAwake = true;

        [Header("── SpriteRenderer ──────────────────────")]

        /// <summary>
        /// 색상 변화 / 피격 점멸 대상 SpriteRenderer.
        /// 미연결 시 GetComponent 자동 탐색.
        /// </summary>
        [Tooltip("색상 변화 + 피격 점멸 SpriteRenderer. 미연결 시 자동 탐색.")]
        [SerializeField] private SpriteRenderer _spriteRenderer;

        // ══════════════════════════════════════════════════════
        // 내부 상태
        // ══════════════════════════════════════════════════════

        /// <summary>봉인도 내부 최대치. Awake 에서 grade 기준으로 자동 설정.</summary>
        private float _maxGauge;

        /// <summary>현재 봉인도 수치 (0 ~ _maxGauge).</summary>
        private float _currentGauge;

        /// <summary>
        /// 봉인 횟수 (0-based).
        /// 횟수 증가 → 저항 배율 감소 → 봉인 어려워짐.
        /// ForceRelease 시에도 유지 (저항 누적 원칙).
        /// </summary>
        private int _sealCount;

        /// <summary>현재 봉인도 단계 (0~4). 외부 이벤트 발행용.</summary>
        private int _currentStage;

        /// <summary>딜 페이즈 활성 여부. _isDilPhaseOnly=true 일 때만 유효.</summary>
        private bool _isGaugeActive;

        /// <summary>페이즈 목표치 도달 여부. 한 번만 발행.</summary>
        private bool _phaseTargetReached;

        /// <summary>봉인 완료 Loop 파티클 인스턴스.</summary>
        private GameObject _sealedParticleInstance;

        /// <summary>현재 색상 Tween 참조. 중복 방지.</summary>
        private Tweener _colorTween;

        /// <summary>피격 점멸 Tween 참조. 중복 방지.</summary>
        private Tweener _flashTween;

        /// <summary>맥동 Tween 참조. 봉인 가능 상태 맥동용.</summary>
        private Tweener _pulseTween;

        // ══════════════════════════════════════════════════════
        // 이벤트
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 봉인도 100% 도달 시 1회 발행.
        /// BossSealManager가 구독 → 집행 대기 목록 등록 + 범위 표시.
        /// 파라미터: 자기 자신(SealableComponent).
        /// </summary>
        public event Action<SealableComponent> OnSealRequested;

        /// <summary>
        /// 봉인 집행 완료 시 발행.
        /// BossSealManager가 구독 → 보스 봉인 상태 갱신.
        /// </summary>
        public event Action OnSealCompleted;

        /// <summary>
        /// 강제 해제 시 발행.
        /// BossSealManager가 구독 → 집행 목록에서 제거.
        /// </summary>
        public event Action OnForceReleased;

        /// <summary>
        /// 봉인도 단계 변화 시 발행 (0~4).
        /// 외부 UI / 연출 컴포넌트 연동용.
        /// </summary>
        public event Action<int> OnStageChanged;

        /// <summary>
        /// 봉인도 수치 변화 시 발행.
        /// 파라미터: UI 퍼센트 (0~100).
        /// </summary>
        public event Action<float> OnGaugeChanged;

        /// <summary>
        /// 페이즈 목표치 도달 시 1회 발행 (코어 전용).
        /// BossSealManager에서 페이즈 전환 판단에 사용.
        /// </summary>
        public event Action OnPhaseTargetReached;

        // ══════════════════════════════════════════════════════
        // 프로퍼티
        // ══════════════════════════════════════════════════════

        /// <summary>봉인 집행 등급.</summary>
        public SealGrade Grade => grade;

        /// <summary>봉인 가능 상태 (봉인도 100% 도달).</summary>
        public bool IsSealReady { get; private set; }

        /// <summary>봉인 완료 상태 (집행 완료).</summary>
        public bool IsSealed { get; private set; }

        /// <summary>집행 가능 범위 반경. SealExecutor 참조용.</summary>
        public float SealRange => _sealRange;

        /// <summary>봉인도 UI 퍼센트 (0~100).</summary>
        public float UIPercent => _maxGauge > 0f
            ? Mathf.Clamp01(_currentGauge / _maxGauge) * 100f
            : 0f;

        /// <summary>현재 봉인도 원본 수치.</summary>
        public float CurrentGauge => _currentGauge;

        /// <summary>현재 봉인도 최대치.</summary>
        public float MaxGauge => _maxGauge;

        /// <summary>딜 페이즈 전용 게이지 활성 여부.</summary>
        public bool IsGaugeActive => _isGaugeActive;

        /// <summary>딜 페이즈에서만 봉인도 누적되는 코어형 게이지인지 여부.</summary>
        public bool IsDilPhaseOnly => _isDilPhaseOnly;

        /// <summary>현재 봉인 횟수 (저항 배율 참조용).</summary>
        public int SealCount => _sealCount;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            // SpriteRenderer 자동 탐색
            if (_spriteRenderer == null)
                _spriteRenderer = GetComponent<SpriteRenderer>();

            AutoCollectLocalReadyParticles();
            AutoCollectLocalSealedParticles();

            if (_clearReadyParticlesOnAwake)
                StopLocalReadyParticles(clear: true);

            if (_clearLocalParticlesOnAwake)
                StopLocalSealedParticles(clear: true);

            // grade 기준 최대 봉인도 자동 설정
            ApplyMaxGaugeFromData();
        }

        private void OnDestroy()
        {
            _colorTween?.Kill();
            _flashTween?.Kill();
            _pulseTween?.Kill();

            // 파티클 정리
            StopLocalReadyParticles(clear: true);
            StopLocalSealedParticles(clear: true);

            if (_sealedParticleInstance != null)
                Destroy(_sealedParticleInstance);
        }

        // ══════════════════════════════════════════════════════
        // 초기화
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 외부에서 BossDataSO 와 최대치를 주입하여 초기화.
        /// BossDataManager/BossSealManager 초기화 과정에서 호출.
        ///
        /// [코어 전용]
        ///   grade = Core 일 때 coreSealGaugeMax 를 maxGauge 로 전달.
        ///   grade = Part 일 때는 BossDataSO 에서 자동 적용되므로
        ///   maxGauge = 0 전달 시 BossDataSO.SealData.partSealGaugeMax 사용.
        /// </summary>
        /// <param name="data">BossDataSO 참조.</param>
        /// <param name="maxGauge">봉인도 최대치. 0이면 grade 기준 자동 적용.</param>
        public void Initialize(BossDataSO data, float maxGauge = 0f)
        {
            _bossData = data;

            if (maxGauge > 0f)
                _maxGauge = maxGauge;
            else
                ApplyMaxGaugeFromData();

            ResetState(resetSealCount: true);

            Debug.Log($"[SealableComponent] {gameObject.name} 초기화 | " +
                      $"등급:{grade} | MaxGauge:{_maxGauge}");
        }

        /// <summary>
        /// grade 기준으로 _maxGauge 를 BossDataSO 에서 자동 설정.
        /// </summary>
        private void ApplyMaxGaugeFromData()
        {
            if (_bossData == null || _bossData.SealData == null)
            {
                // BossDataSO 미연결 — Normal 기본값 사용
                if (grade == SealGrade.Normal)
                    _maxGauge = _normalMaxGauge;
                return;
            }

            _maxGauge = grade switch
            {
                SealGrade.Part => _bossData.SealData.partSealGaugeMax,
                SealGrade.Core => _bossData.SealData.coreSealGaugeMax,
                SealGrade.Normal => _normalMaxGauge,
                _ => _normalMaxGauge,
            };
        }

        // ══════════════════════════════════════════════════════
        // 봉인도 누적
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 봉인도를 누적한다.
        ///
        /// [차단 조건]
        ///   IsSealed = true               이미 봉인 완료
        ///   _isDilPhaseOnly && !_isGaugeActive  딜 페이즈 외 차단
        ///
        /// [저항 배율]
        ///   Normal 등급은 저항 없음.
        ///   Part / Core 는 BossDataSO.SealData.GetSealResistMultiplier() 적용.
        ///
        /// [색상 변화 — v2.0]
        ///   AddGauge 호출마다 UIPercent 비율로 colorBase → colorFull 보간.
        ///   DOColor 로 부드럽게 물들어 가는 방식.
        /// </summary>
        /// <param name="rawAmount">원시 봉인도 누적량 (저항 적용 전).</param>
        public void AddGauge(float rawAmount)
        {
            if (IsSealed) return;
            if (rawAmount <= 0f) return;
            if (_isDilPhaseOnly && !_isGaugeActive) return;

            // 봉인 저항 배율 적용
            float multiplier = 1f;
            if (grade != SealGrade.Normal && _bossData?.SealData != null)
                multiplier = _bossData.SealData.GetSealResistMultiplier(_sealCount);

            float actualAmount = rawAmount * multiplier;
            _currentGauge = Mathf.Min(_currentGauge + actualAmount, _maxGauge);

            // 이벤트 발행
            OnGaugeChanged?.Invoke(UIPercent);
            CheckStageChange();
            CheckPhaseTarget();

            // 색상 점진 보간 (v2.0)
            UpdateGaugeColor();

            // 100% 도달 → 집행 승인 요청
            if (!IsSealReady && _currentGauge >= _maxGauge)
            {
                IsSealReady = true;
                StartSealReadyPulse();
                PlayLocalReadyParticles();
                OnSealRequested?.Invoke(this);

                Debug.Log($"[SealableComponent] ▶ {gameObject.name} 봉인 집행 가능 | " +
                          $"등급:{grade} | 저항횟수:{_sealCount}");
            }
        }

        // ══════════════════════════════════════════════════════
        // 집행 완료
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 봉인 집행 완료 처리.
        /// BossSealManager가 즉시 집행/일섬 도착 후 호출.
        ///
        /// [처리 내용]
        ///   IsSealed = true
        ///   _sealCount++ (저항 횟수 누적)
        ///   맥동 종료 → colorSealed 고정
        ///   Loop 파티클 활성
        ///   OnSealCompleted 발행
        /// </summary>
        public void ExecuteSeal()
        {
            if (!IsSealReady || IsSealed) return;

            IsSealed = true;
            _sealCount++;

            // Ready 표시 종료 → colorSealed 고정
            StopPulse();
            StopLocalReadyParticles(clear: true);
            ApplyColor(_bossData?.ColorData?.colorSealed ?? Color.blue,
                       _bossData?.ColorData?.sealTransitionDuration ?? 0.3f);

            // Loop 파티클 활성
            SpawnSealedParticle();
            PlayLocalSealedParticles();

            OnSealCompleted?.Invoke();

            Debug.Log($"[SealableComponent] ✅ {gameObject.name} 봉인 완료 | " +
                      $"등급:{grade} | 저항횟수:{_sealCount}");
        }

        // ══════════════════════════════════════════════════════
        // 강제 해제
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 봉인을 강제 해제하고 봉인도를 초기화한다.
        /// 딜 페이즈 종료 / 그로기 실패 시 SealGaugeManager 에서 호출.
        ///
        /// [저항 횟수 원칙]
        ///   resetSealCount = false (기본): 저항 횟수 유지
        ///     → 반복 봉인 시 난이도 상승 (루프 재시작 시 사용)
        ///   resetSealCount = true: 씬 완전 초기화 시에만 사용
        ///
        /// [색상 처리]
        ///   colorBase 로 DOTween 보간 복귀
        ///   Loop 파티클 파괴
        ///   맥동 종료
        /// </summary>
        /// <param name="resetSealCount">저항 횟수 초기화 여부. 기본 false.</param>
        public void ForceRelease(bool resetSealCount = false)
        {
            ResetState(resetSealCount);

            // 색상 복귀
            StopPulse();
            ApplyColor(_bossData?.ColorData?.colorBase ?? Color.white,
                       _bossData?.ColorData?.sealTransitionDuration ?? 0.3f);

            // Ready/Loop 파티클 정리
            StopLocalReadyParticles(clear: true);
            DestroySealedParticle();
            StopLocalSealedParticles(clear: true);

            OnForceReleased?.Invoke();

            Debug.Log($"[SealableComponent] ■ {gameObject.name} 강제 해제 | " +
                      $"저항횟수:{(resetSealCount ? "초기화" : "유지")}");
        }

        // ══════════════════════════════════════════════════════
        // 딜 페이즈 활성 제어
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 딜 페이즈 활성/비활성 전환.
        /// _isDilPhaseOnly = true 인 코어 컴포넌트에만 의미 있음.
        /// BossSealManager의 DilPhase Enter/Exit에서 호출.
        /// </summary>
        public void ActivateGauge(bool isActive)
        {
            _isGaugeActive = isActive;
            Debug.Log($"[SealableComponent] {gameObject.name} 게이지 활성 = {isActive}");
        }

        /// <summary>코어 페이즈 목표치를 런타임에서 설정한다. 0 이하이면 목표치 이벤트를 사용하지 않는다.</summary>
        public void SetPhaseTarget(float target, bool resetReached = true)
        {
            _phaseTarget = Mathf.Max(0f, target);
            if (resetReached)
                _phaseTargetReached = false;
        }

        /// <summary>현재 게이지 수치를 초기화한다. 코어 DilPhase 재진입/실패 복구용.</summary>
        public void ResetGaugeOnly(bool clearReady = true)
        {
            _currentGauge = 0f;
            _currentStage = 0;
            _phaseTargetReached = false;

            if (clearReady)
                IsSealReady = false;

            StopPulse();
            StopLocalReadyParticles(clear: true);
            OnGaugeChanged?.Invoke(0f);
            UpdateGaugeColor();
        }

        // ══════════════════════════════════════════════════════
        // 피격 점멸 (외부 호출)
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 피격 점멸 연출.
        /// BossWardenPart 등 피격 처리 컴포넌트에서 호출.
        ///
        /// [v2.0 변경]
        ///   흰색(white) → colorHitFlash 로 점멸 (봉인도 색상 초기화 방지)
        ///   점멸 종료 후 현재 봉인도 비율 색상으로 자동 복귀
        ///   SetUpdate(true) — 슬로우 중에도 정상 동작
        /// </summary>
        public void PlayHitFlash()
        {
            if (_spriteRenderer == null) return;

            Color flashColor = _bossData?.ColorData?.colorHitFlash ?? Color.white;
            float flashDuration = _bossData?.ColorData?.hitFlashDuration ?? 0.07f;

            _flashTween?.Kill();
            _colorTween?.Kill();

            _spriteRenderer.color = flashColor;

            // 점멸 종료 후 현재 봉인도 비율 색상으로 자동 복귀
            Color returnColor = GetCurrentGaugeColor();
            _flashTween = _spriteRenderer
                .DOColor(returnColor, flashDuration)
                .SetEase(Ease.OutCubic)
                .SetUpdate(true);
        }

        // ══════════════════════════════════════════════════════
        // 내부 — 색상 관리
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 봉인도 수치에 따른 색상 DOTween 보간 적용.
        /// AddGauge() 호출마다 실행.
        /// colorBase → colorFull 서서히 물들어 가는 방식.
        /// </summary>
        private void UpdateGaugeColor()
        {
            if (_spriteRenderer == null) return;
            if (IsSealed) return; // 봉인 완료 후 색상 변경 금지

            Color targetColor = GetCurrentGaugeColor();
            float duration = _bossData?.ColorData?.colorLerpDuration ?? 0.15f;

            _colorTween?.Kill();
            _colorTween = _spriteRenderer
                .DOColor(targetColor, duration)
                .SetEase(Ease.OutCubic)
                .SetUpdate(true);
        }

        /// <summary>
        /// 현재 봉인도 비율에 해당하는 색상 반환.
        /// colorBase → colorFull 선형 보간.
        /// BossDataSO 미연결 시 흰색 반환.
        /// </summary>
        public Color GetCurrentGaugeColor()
        {
            if (_bossData?.ColorData == null) return Color.white;
            return _bossData.ColorData.GetPartColor(UIPercent);
        }

        /// <summary>
        /// 지정 색상으로 DOTween 보간 적용.
        /// ExecuteSeal / ForceRelease 에서 사용.
        /// </summary>
        private void ApplyColor(Color target, float duration)
        {
            if (_spriteRenderer == null) return;

            _colorTween?.Kill();
            _colorTween = _spriteRenderer
                .DOColor(target, duration)
                .SetEase(Ease.OutCubic)
                .SetUpdate(true);
        }

        /// <summary>
        /// 봉인 가능 상태 (100%) 맥동 시작.
        /// colorFull ↔ colorSealReadyPulse DOTween Yoyo Loop.
        /// </summary>
        private void StartSealReadyPulse()
        {
            if (_spriteRenderer == null || _bossData?.ColorData == null) return;

            _colorTween?.Kill();
            _pulseTween?.Kill();

            Color pulseColor = _bossData.ColorData.colorSealReadyPulse;
            Color fullColor = _bossData.ColorData.colorFull;
            float period = _bossData.ColorData.sealReadyPulseDuration;

            _spriteRenderer.color = fullColor;
            _pulseTween = _spriteRenderer
                .DOColor(pulseColor, period * 0.5f)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo)
                .SetUpdate(true);
        }

        /// <summary>맥동 Tween 종료.</summary>
        private void StopPulse()
        {
            _pulseTween?.Kill();
            _pulseTween = null;
        }

        // ══════════════════════════════════════════════════════
        // 내부 — Loop 파티클
        // ══════════════════════════════════════════════════════

        /// <summary>자식으로 배치된 봉인 가능 Ready ParticleSystem 자동 수집.</summary>
        private void AutoCollectLocalReadyParticles()
        {
            if (!_autoCollectLocalReadyParticles) return;
            if (_sealReadyLocalParticles != null && _sealReadyLocalParticles.Length > 0) return;

            var all = GetComponentsInChildren<ParticleSystem>(true);
            var list = new System.Collections.Generic.List<ParticleSystem>();
            foreach (var ps in all)
            {
                if (ps == null) continue;
                if (IsReadyParticleName(ps.name))
                    list.Add(ps);
            }

            _sealReadyLocalParticles = list.ToArray();
        }

        /// <summary>자식으로 배치된 봉인 완료 ParticleSystem 자동 수집.</summary>
        private void AutoCollectLocalSealedParticles()
        {
            if (!_autoCollectLocalSealedParticles) return;
            if (_sealedLocalParticles != null && _sealedLocalParticles.Length > 0) return;

            var all = GetComponentsInChildren<ParticleSystem>(true);
            var list = new System.Collections.Generic.List<ParticleSystem>();
            foreach (var ps in all)
            {
                if (ps == null) continue;
                if (IsReadyParticleName(ps.name)) continue;

                if (IsSealedParticleName(ps.name))
                    list.Add(ps);
            }

            // 이름 규칙이 없는 기존 프리팹 호환: Ready 파티클이 아닌 모든 파티클을 완료 파티클로 취급.
            if (list.Count == 0)
            {
                foreach (var ps in all)
                {
                    if (ps == null) continue;
                    if (IsReadyParticleName(ps.name)) continue;
                    list.Add(ps);
                }
            }

            _sealedLocalParticles = list.ToArray();
        }

        private static bool IsReadyParticleName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string lower = name.ToLowerInvariant();
            return lower.Contains("ready") || lower.Contains("available") || lower.Contains("canseal");
        }

        private static bool IsSealedParticleName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string lower = name.ToLowerInvariant();
            return lower.Contains("sealed") || lower.Contains("complete") || lower.Contains("success") || lower.Contains("execution");
        }

        /// <summary>프리팹에 미리 배치된 로컬 Ready 파티클 재생.</summary>
        private void PlayLocalReadyParticles()
        {
            if (_sealReadyLocalParticles == null) return;

            foreach (var ps in _sealReadyLocalParticles)
            {
                if (ps == null) continue;
                ps.gameObject.SetActive(true);
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.Play(true);
            }
        }

        /// <summary>로컬 Ready 파티클 정지/초기화.</summary>
        private void StopLocalReadyParticles(bool clear)
        {
            if (_sealReadyLocalParticles == null) return;

            var stopBehavior = clear
                ? ParticleSystemStopBehavior.StopEmittingAndClear
                : ParticleSystemStopBehavior.StopEmitting;

            foreach (var ps in _sealReadyLocalParticles)
            {
                if (ps == null) continue;
                ps.Stop(true, stopBehavior);
            }
        }

        /// <summary>프리팹에 미리 배치된 로컬 봉인 파티클 재생.</summary>
        private void PlayLocalSealedParticles()
        {
            if (_sealedLocalParticles == null) return;

            foreach (var ps in _sealedLocalParticles)
            {
                if (ps == null) continue;
                ps.gameObject.SetActive(true);
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.Play(true);
            }
        }

        /// <summary>로컬 봉인 파티클 정지/초기화.</summary>
        private void StopLocalSealedParticles(bool clear)
        {
            if (_sealedLocalParticles == null) return;

            var stopBehavior = clear
                ? ParticleSystemStopBehavior.StopEmittingAndClear
                : ParticleSystemStopBehavior.StopEmitting;

            foreach (var ps in _sealedLocalParticles)
            {
                if (ps == null) continue;
                ps.Stop(true, stopBehavior);
            }
        }

        /// <summary>
        /// 봉인 완료 Loop 파티클 생성 + 자식으로 부착.
        /// 부위가 이동해도 파티클이 자동으로 따라감.
        /// SealColorDataSO.sealedParticlePrefab 미연결 시 스킵.
        /// </summary>
        private void SpawnSealedParticle()
        {
            if (_bossData?.ColorData?.sealedParticlePrefab == null) return;
            if (_sealedParticleInstance != null) return; // 중복 방지

            _sealedParticleInstance = Instantiate(
                _bossData.ColorData.sealedParticlePrefab,
                transform.position,
                Quaternion.identity,
                transform); // 자기 자식으로 부착

            Debug.Log($"[SealableComponent] {gameObject.name} Loop 파티클 활성");
        }

        /// <summary>
        /// 봉인 완료 Loop 파티클 파괴.
        /// ForceRelease 시 호출.
        /// </summary>
        private void DestroySealedParticle()
        {
            if (_sealedParticleInstance == null) return;

            Destroy(_sealedParticleInstance);
            _sealedParticleInstance = null;

            Debug.Log($"[SealableComponent] {gameObject.name} Loop 파티클 제거");
        }

        // ══════════════════════════════════════════════════════
        // 내부 — 단계 / 목표치 체크
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 봉인도 단계 변화 체크 (0/25/50/75/100%).
        /// 단계 전환 시 OnStageChanged 발행.
        /// </summary>
        private void CheckStageChange()
        {
            int newStage = Mathf.Clamp(Mathf.FloorToInt(UIPercent / 25f), 0, 4);

            if (newStage != _currentStage)
            {
                _currentStage = newStage;
                OnStageChanged?.Invoke(_currentStage);

                Debug.Log($"[SealableComponent] {gameObject.name} 단계 변화 → " +
                          $"Stage {_currentStage} ({UIPercent:F0}%)");
            }
        }

        /// <summary>
        /// 페이즈 목표치 도달 체크 (코어 전용).
        /// 1회만 발행.
        /// </summary>
        private void CheckPhaseTarget()
        {
            if (_phaseTarget <= 0f) return;
            if (_phaseTargetReached) return;
            if (_currentGauge < _phaseTarget) return;

            _phaseTargetReached = true;
            OnPhaseTargetReached?.Invoke();

            Debug.Log($"[SealableComponent] {gameObject.name} 페이즈 목표 도달 ({_phaseTarget})");
        }

        // ══════════════════════════════════════════════════════
        // 내부 — 상태 초기화
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 내부 상태 초기화.
        /// ForceRelease / Initialize 에서 호출.
        /// </summary>
        private void ResetState(bool resetSealCount)
        {
            _currentGauge = 0f;
            IsSealReady = false;
            IsSealed = false;
            _currentStage = 0;
            _phaseTargetReached = false;
            _isGaugeActive = false;

            if (resetSealCount) _sealCount = 0;

            StopPulse();
            StopLocalReadyParticles(clear: true);

            OnGaugeChanged?.Invoke(0f);
        }

        // ══════════════════════════════════════════════════════
        // Gizmos — 에디터 시각화
        // ══════════════════════════════════════════════════════

        private void OnDrawGizmosSelected()
        {
            // 집행 범위 표시
            Color gizmoColor = IsSealed
                ? new Color(0f, 0.27f, 0.8f, 0.5f)
                : IsSealReady
                    ? new Color(0.6f, 0.8f, 1.0f, 0.5f)
                    : new Color(1f, 0.4f, 0f, 0.3f);

            Gizmos.color = gizmoColor;
            Gizmos.DrawWireSphere(transform.position, _sealRange);

#if UNITY_EDITOR
            UnityEditor.Handles.Label(
                transform.position + Vector3.up * 0.8f,
                $"{gameObject.name}\n" +
                $"등급:{grade} | {UIPercent:F0}% | " +
                $"봉인:{IsSealed} | 가능:{IsSealReady}");
#endif
        }
    }
}