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
//   [v1.1 유지 사항]
//     홀드 진행 UI (ShowHoldProgress / UpdateHoldProgress / Complete / Cancel)
//     ForceRelease 저항 횟수 유지 원칙
//     ActivateGauge (코어 전용 딜 페이즈 활성)
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
using TMPro;
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
    ///   ShowHoldProgress()      홀드 UI 표시
    ///   UpdateHoldProgress(float) 홀드 진행도 갱신
    ///   CompleteHoldProgress()  홀드 완료 UI
    ///   CancelHoldProgress()    홀드 취소 UI
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
        /// SealExecutor 에서 슬로우 / 연출 분기에 사용.
        ///
        ///   Part   : 팔 등 부위 봉인 (집행 완료 후 짧은 슬로우)
        ///   Core   : 코어 봉인 (홀드 중 내내 강한 슬로우)
        ///   Normal : 일반 몬스터 (슬로우 없음)
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
        /// BossWardenCore.Initialize() 에서 주입 or Inspector 직접 연결.
        /// </summary>
        [Tooltip("BossDataSO. 봉인도 수치 + 색상 참조. 필수.")]
        [SerializeField] private BossDataSO _bossData;

        [Header("── 집행 수치 (Inspector 설정) ──────────────────────")]

        /// <summary>
        /// 봉인 집행 가능 범위 반경 (units).
        /// SealExecutor 가 플레이어 거리 체크 시 참조.
        /// BossDataSO.SealData.sealExecutionRange 와 동일하게 설정 권장.
        ///
        /// [권장값] 1.5
        /// </summary>
        [Tooltip("봉인 집행 접근 범위. 권장: 1.5.")]
        [Min(0.1f)]
        [SerializeField] private float _sealRange = 1.5f;

        /// <summary>
        /// S키 홀드 시간 (초).
        /// SealExecutor 가 집행 홀드 시간 체크 시 참조.
        ///
        /// [권장값]
        ///   Part = 1.5 / Core = 2.0 (최종 봉인)
        /// </summary>
        [Tooltip("S키 홀드 시간. Part=1.5 / Core(최종봉인)=2.0.")]
        [Min(0.1f)]
        [SerializeField] private float _sealHoldTime = 1.5f;

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
        /// BossWardenCore 에서 페이즈 전환 판단에 사용.
        /// </summary>
        [Tooltip("페이즈 목표치. 0이면 미사용. 코어용.")]
        [Min(0f)]
        [SerializeField] private float _phaseTarget = 0f;

        [Header("── SpriteRenderer ──────────────────────")]

        /// <summary>
        /// 색상 변화 / 피격 점멸 대상 SpriteRenderer.
        /// 미연결 시 GetComponent 자동 탐색.
        /// </summary>
        [Tooltip("색상 변화 + 피격 점멸 SpriteRenderer. 미연결 시 자동 탐색.")]
        [SerializeField] private SpriteRenderer _spriteRenderer;

        [Header("── 홀드 진행 UI ──────────────────────")]

        /// <summary>
        /// 봉인 집행 홀드 진행도 TextMeshPro.
        /// 자기 자식 오브젝트로 연결. 미연결 시 텍스트 생략.
        ///
        /// [씬 구성]
        ///   Boss_LeftArm
        ///     └─ SealProgressText [TextMeshPro]
        ///          Font Size = 3 / Alignment = Center / SetActive = false
        /// </summary>
        [Tooltip("홀드 진행도 TMP. 자기 자식으로 연결. 미연결 시 생략.")]
        [SerializeField] private TextMeshPro _holdProgressText;

        /// <summary>
        /// 진행 텍스트 로컬 위치 오프셋.
        /// 부위 위에 표시되도록 기본값 (0, 1.5, 0) 권장.
        /// </summary>
        [Tooltip("진행 텍스트 로컬 오프셋. 기본: (0, 1.5, 0).")]
        [SerializeField] private Vector3 _progressTextLocalOffset = new Vector3(0f, 1.5f, 0f);

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
        /// SealExecutor 가 구독 → 집행 대기 목록 등록 + 범위 표시.
        /// 파라미터: 자기 자신(SealableComponent).
        /// </summary>
        public event Action<SealableComponent> OnSealRequested;

        /// <summary>
        /// 봉인 집행 완료 시 발행.
        /// SealManager 가 구독 → 그로기 조건 체크.
        /// </summary>
        public event Action OnSealCompleted;

        /// <summary>
        /// 강제 해제 시 발행.
        /// SealExecutor 가 구독 → 집행 목록에서 제거.
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
        /// BossWardenCore 에서 페이즈 전환 판단에 사용.
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

        /// <summary>S키 홀드 시간. SealExecutor 참조용.</summary>
        public float SealHoldTime => _sealHoldTime;

        /// <summary>봉인도 UI 퍼센트 (0~100).</summary>
        public float UIPercent => _maxGauge > 0f
            ? Mathf.Clamp01(_currentGauge / _maxGauge) * 100f
            : 0f;

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

            // grade 기준 최대 봉인도 자동 설정
            ApplyMaxGaugeFromData();
        }

        private void OnDestroy()
        {
            _colorTween?.Kill();
            _flashTween?.Kill();
            _pulseTween?.Kill();

            // 파티클 정리
            if (_sealedParticleInstance != null)
                Destroy(_sealedParticleInstance);
        }

        // ══════════════════════════════════════════════════════
        // 초기화
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 외부에서 BossDataSO 와 최대치를 주입하여 초기화.
        /// BossWardenCore.Start() → 각 부위 Initialize() 에서 호출.
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
        /// SealExecutor 가 S키 홀드 완료 후 호출.
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

            // 맥동 종료 → colorSealed 고정
            StopPulse();
            ApplyColor(_bossData?.ColorData?.colorSealed ?? Color.blue,
                       _bossData?.ColorData?.sealTransitionDuration ?? 0.3f);

            // Loop 파티클 활성
            SpawnSealedParticle();

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

            // Loop 파티클 파괴
            DestroySealedParticle();

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
        /// BossWardenCore.OnDilPhaseEnter/Exit 에서 호출.
        /// </summary>
        public void ActivateGauge(bool isActive)
        {
            _isGaugeActive = isActive;
            Debug.Log($"[SealableComponent] {gameObject.name} 게이지 활성 = {isActive}");
        }

        // ══════════════════════════════════════════════════════
        // 홀드 진행 UI
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 집행 시작 시 홀드 진행 텍스트 표시.
        /// SealExecutor 에서 집행 루프 진입 직후 호출.
        /// </summary>
        public void ShowHoldProgress()
        {
            if (_holdProgressText == null) return;

            _holdProgressText.DOKill();
            _holdProgressText.transform.localPosition = _progressTextLocalOffset;
            _holdProgressText.text = "0%";
            _holdProgressText.color = new Color(1f, 1f, 1f, 0f);
            _holdProgressText.gameObject.SetActive(true);

            _holdProgressText
                .DOFade(1f, 0.15f)
                .SetUpdate(true);
        }

        /// <summary>
        /// 홀드 진행도 갱신.
        /// SealExecutor 홀드 루프에서 매 프레임 호출.
        /// </summary>
        /// <param name="progress">진행도 0.0 ~ 1.0.</param>
        public void UpdateHoldProgress(float progress)
        {
            if (_holdProgressText == null || !_holdProgressText.gameObject.activeSelf) return;

            int percent = Mathf.Clamp(Mathf.RoundToInt(progress * 100f), 0, 100);
            _holdProgressText.text = $"{percent}%";
        }

        /// <summary>
        /// 집행 완료 UI 처리.
        /// "100%" 표시 → 보라색 강조 → DOFade 소멸.
        /// </summary>
        public void CompleteHoldProgress()
        {
            if (_holdProgressText == null) return;

            _holdProgressText.DOKill();
            _holdProgressText.text = "100%";
            _holdProgressText.color = new Color(0.78f, 0.49f, 1f, 1f); // 보라색 강조

            _holdProgressText
                .DOFade(0f, 0.3f)
                .SetDelay(0.1f)
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    if (_holdProgressText != null)
                        _holdProgressText.gameObject.SetActive(false);
                });
        }

        /// <summary>
        /// 집행 취소 UI 처리.
        /// 키 해제 / 범위 이탈 시 SealExecutor 에서 호출.
        /// </summary>
        public void CancelHoldProgress()
        {
            if (_holdProgressText == null || !_holdProgressText.gameObject.activeSelf) return;

            _holdProgressText.DOKill();
            _holdProgressText
                .DOFade(0f, 0.1f)
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    if (_holdProgressText != null)
                        _holdProgressText.gameObject.SetActive(false);
                });
        }

        // ══════════════════════════════════════════════════════
        // 피격 점멸 (외부 호출)
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 피격 점멸 연출.
        /// BossWardenArmPart 등 피격 처리 컴포넌트에서 호출.
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