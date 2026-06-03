// ============================================================
// SealGaugeComponent.cs  v1.0
// 부위 봉인도 관리 공용 컴포넌트
//
// [POC07 참고]
//   TestBossArmPart.cs 의 봉인 상태(ReLock/ForceUnlock) 구조를 참고.
//   SealComponent.cs 의 Dictionary 봉인 구조는 사용하지 않음.
//   → POC08 은 단일 부위 단위 컴포넌트로 단순화.
//
// [POC07과의 차이]
//   POC07: 팔 자체가 봉인 상태(잠김/해제)를 관리 — 이진(true/false) 구조
//   POC08: 봉인도(0~max) 누적 → 100% 시 집행 가능 → 집행 완료 시 봉인 상태
//          → 봉인 저항 배율 적용
//          → 봉인도 단계별 색상 변화 이벤트 발행
//
// [역할]
//   ① 봉인도 내부 수치 관리 (0 ~ _maxGauge)
//   ② 봉인도 100% 도달 시 OnSealReady 이벤트 발행
//   ③ 봉인 집행 완료 시 OnSealed 이벤트 발행
//   ④ 봉인 해제 (ForceRelease) 시 OnReleased 이벤트 발행
//   ⑤ 봉인 저항 배율 적용
//   ⑥ 봉인도 단계(0/25/50/75/100%) 변화 감지 → OnStageChanged 이벤트 발행
//
// [부착 위치]
//   LeftArm, RightArm 오브젝트에 각각 1개씩 부착.
//
// [namespace]
//   namespace : SEAL
// ============================================================

using System;
using UnityEngine;

namespace SEAL
{
    /// <summary>
    /// 부위 봉인도 관리 공용 컴포넌트. (v1.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [봉인도 흐름]
    ///   AddGauge(amount)
    ///     → 저항 배율 적용 후 내부 수치 누적
    ///     → 25/50/75/100% 구간 진입 시 OnStageChanged 발행
    ///     → 100% 도달 시 OnSealReady 발행
    ///
    ///   ExecuteSeal() — 외부 SealExecutor 에서 집행 완료 후 호출
    ///     → IsSealed = true
    ///     → OnSealed 발행
    ///
    ///   ForceRelease() — 딜 페이즈 종료 시 BossWardenCore 에서 호출
    ///     → 봉인도 초기화
    ///     → IsSealed = false
    ///     → OnReleased 발행
    ///
    /// [봉인 저항]
    ///   _sealCount 가 증가할수록 AddGauge 실제 누적량 감소.
    ///   BossWardenDataSO.GetSealResistMultiplier(_sealCount) 참조.
    ///
    /// [외부 사용 예시 — BossWardenArmPart]
    ///   _sealGauge.AddGauge(attackAmount);
    ///   _sealGauge.OnSealReady  += HandleSealReady;
    ///   _sealGauge.OnSealed     += HandleSealed;
    ///   _sealGauge.OnReleased   += HandleReleased;
    /// ────────────────────────────────────────────────────
    /// </summary>
    public class SealGaugeComponent : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── 봉인도 수치 ──────────────────────")]

        /// <summary>
        /// 봉인도 내부 최대 요구치.
        /// BossWardenDataSO.armSealGaugeMax 와 동일 값으로 설정.
        /// Inspector 에서 직접 설정하거나, Initialize() 로 주입.
        /// </summary>
        [Tooltip("봉인도 내부 최대 요구치. BossWardenDataSO.armSealGaugeMax 와 동일하게.")]
        [Min(1f)]
        [SerializeField] private float _maxGauge = 200f;

        [Header("── DataSO 연결 (선택 — Initialize로 주입 가능) ──────────────────────")]

        /// <summary>
        /// BossWardenDataSO 참조.
        /// 봉인 저항 배율 적용에 사용.
        /// Inspector 에서 연결하거나 Initialize() 로 주입.
        /// </summary>
        [Tooltip("BossWardenDataSO. 봉인 저항 배율 참조. 미연결 시 저항 없음.")]
        [SerializeField] private BossWardenDataSO _data;

        // ══════════════════════════════════════════════════════
        // 내부 상태
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 현재 봉인도 내부 수치 (0 ~ _maxGauge).
        /// </summary>
        private float _currentGauge;

        /// <summary>
        /// 봉인 완료(집행) 여부.
        /// true = S키 집행 완료 상태 (부위 봉인 완료).
        /// false = 봉인되지 않은 상태.
        /// </summary>
        private bool _isSealed;

        /// <summary>
        /// 봉인도 100% 도달 여부 (집행 가능 상태).
        /// true = 봉인 가능 / false = 아직 진행 중.
        /// </summary>
        private bool _isSealReady;

        /// <summary>
        /// 이 부위의 봉인 횟수 (0-based).
        /// 봉인 저항 배율 계산에 사용.
        /// ForceRelease 시 카운트 유지 (저항은 딜페이즈 종료 시 초기화).
        /// </summary>
        private int _sealCount;

        /// <summary>
        /// 현재 봉인도 단계 (0~4 → 0%/25%/50%/75%/100%).
        /// 단계 변화 시 OnStageChanged 발행.
        /// </summary>
        private int _currentStage;

        // ══════════════════════════════════════════════════════
        // 이벤트
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 봉인도 100% 도달 시 1회 발행.
        /// BossWardenSealExecutor 가 구독 → 집행 가능 상태 전환 + 범위 피드백 표시.
        /// </summary>
        public event Action OnSealReady;

        /// <summary>
        /// 봉인 집행 완료 시 발행.
        /// BossWardenCore 가 구독 → 그로기 조건 체크.
        /// BossWardenFeedback 이 구독 → 부위 봉인 완료 색상 고정.
        /// </summary>
        public event Action OnSealed;

        /// <summary>
        /// 봉인 강제 해제 시 발행.
        /// 딜 페이즈 종료 시 BossWardenCore 에서 ForceRelease() 호출.
        /// BossWardenFeedback 이 구독 → 부위 색상 초기화.
        /// </summary>
        public event Action OnReleased;

        /// <summary>
        /// 봉인도 단계 변화 시 발행.
        /// 파라미터: 새 단계 (0=0%, 1=25%, 2=50%, 3=75%, 4=100%).
        /// BossWardenFeedback 이 구독 → 부위 색상 단계별 전환.
        /// </summary>
        public event Action<int> OnStageChanged;

        /// <summary>
        /// 봉인도 수치 변화 시 매 AddGauge 호출마다 발행.
        /// 파라미터: UI 퍼센트 (0~100).
        /// UI 게이지 업데이트에 사용.
        /// </summary>
        public event Action<float> OnGaugeChanged;

        // ══════════════════════════════════════════════════════
        // 프로퍼티
        // ══════════════════════════════════════════════════════

        /// <summary> 현재 봉인도 내부 수치. </summary>
        public float CurrentGauge => _currentGauge;

        /// <summary> 봉인도 최대치. </summary>
        public float MaxGauge => _maxGauge;

        /// <summary>
        /// 봉인도 UI 퍼센트 (0~100).
        /// _data 연결 시 DataSO 변환 공식 사용, 미연결 시 직접 계산.
        /// </summary>
        public float UIPercent => (_maxGauge > 0f)
            ? Mathf.Clamp01(_currentGauge / _maxGauge) * 100f
            : 0f;

        /// <summary> 봉인 집행 완료 여부. </summary>
        public bool IsSealed => _isSealed;

        /// <summary> 봉인 가능 상태 (봉인도 100%) 여부. </summary>
        public bool IsSealReady => _isSealReady;

        /// <summary> 현재 봉인 횟수 (봉인 저항 계산용). </summary>
        public int SealCount => _sealCount;

        // ══════════════════════════════════════════════════════
        // 초기화
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 외부에서 DataSO 와 최대치를 주입하여 초기화.
        /// BossWardenArmPart.Initialize() 에서 호출.
        ///
        /// [호출 타이밍]
        ///   BossWardenCore.Start() → BossWardenArmPart.Initialize() → 이 함수
        /// </summary>
        /// <param name="data">BossWardenDataSO 참조.</param>
        /// <param name="maxGauge">봉인도 내부 최대치.</param>
        public void Initialize(BossWardenDataSO data, float maxGauge)
        {
            _data = data;
            _maxGauge = maxGauge;
            ResetGauge(resetSealCount: true);

            Debug.Log($"[SealGaugeComponent] {gameObject.name} 초기화 — MaxGauge: {_maxGauge}");
        }

        // ══════════════════════════════════════════════════════
        // 외부 API
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 봉인도를 누적한다.
        /// 봉인 완료(IsSealed) 상태이면 무시.
        /// 봉인 저항 배율을 적용한 후 실제 누적.
        ///
        /// [호출처]
        ///   BossWardenArmPart.OnTriggerEnter2D() — 플레이어 공격 피격 시
        ///   Recovery 구간에서 recoveryVulnMultiplier 추가 적용 가능.
        /// </summary>
        /// <param name="rawAmount">원시 봉인도 누적량 (저항 적용 전).</param>
        public void AddGauge(float rawAmount)
        {
            // 이미 봉인 완료면 무시
            if (_isSealed) return;
            if (rawAmount <= 0f) return;

            // 봉인 저항 배율 적용
            float multiplier = (_data != null)
                ? _data.GetSealResistMultiplier(_sealCount)
                : 1.0f;

            float actualAmount = rawAmount * multiplier;
            _currentGauge = Mathf.Min(_currentGauge + actualAmount, _maxGauge);

            // 게이지 변화 이벤트
            OnGaugeChanged?.Invoke(UIPercent);

            // 단계 변화 체크
            CheckStageChange();

            // 100% 도달 체크
            if (!_isSealReady && _currentGauge >= _maxGauge)
            {
                _isSealReady = true;
                OnSealReady?.Invoke();
                Debug.Log($"[SealGaugeComponent] {gameObject.name} 봉인 가능 상태 (100%)");
            }
        }

        /// <summary>
        /// 봉인 집행을 완료 처리한다.
        /// BossWardenSealExecutor 에서 S키 홀드 완료 시 호출.
        ///
        /// [조건]
        ///   IsSealReady == true 일 때만 유효.
        ///   이미 IsSealed 이면 무시.
        /// </summary>
        public void ExecuteSeal()
        {
            if (!_isSealReady || _isSealed) return;

            _isSealed = true;
            _sealCount++;

            OnSealed?.Invoke();
            Debug.Log($"[SealGaugeComponent] {gameObject.name} 봉인 완료 (SealCount: {_sealCount})");
        }

        /// <summary>
        /// 봉인을 강제 해제하고 봉인도를 초기화한다.
        /// 딜 페이즈 종료 시 BossWardenCore.ExitDilPhase() 에서 호출.
        /// OnReleased 이벤트를 발행한다.
        ///
        /// [저항 카운트 설계 원칙 — README #28]
        ///   봉인 저항은 "같은 부위를 반복 봉인할수록" 누적되어야 한다.
        ///   딜 페이즈 종료마다 저항을 초기화하면 항상 1회 봉인 = 저항 없음이 되어
        ///   저항 시스템이 무의미해진다.
        ///   따라서 기본값은 resetSealCount = false (저항 횟수 유지).
        ///
        ///   resetSealCount = false (기본): 저항 횟수 유지
        ///     → 딜 페이즈 종료 후 루프 재시작 시 사용 (권장)
        ///     → 같은 부위를 반복 봉인할수록 봉인 난이도 상승
        ///   resetSealCount = true: 저항 횟수 초기화
        ///     → 전투 완전 초기화(씬 리셋 등) 시에만 사용
        /// </summary>
        /// <param name="resetSealCount">봉인 저항 횟수 초기화 여부. 기본 false (유지 권장).</param>
        public void ForceRelease(bool resetSealCount = false)
        {
            ResetGauge(resetSealCount);
            OnReleased?.Invoke();
            Debug.Log($"[SealGaugeComponent] {gameObject.name} 강제 해제 — 봉인도 초기화 (저항횟수: {(resetSealCount ? "초기화" : "유지")})");
        }

        /// <summary>
        /// 봉인도 수치 + 상태를 조용히 초기화한다.
        /// OnReleased 이벤트를 발행하지 않는다.
        ///
        /// [ForceRelease 와의 차이]
        ///   ForceRelease : 딜 페이즈 종료 / 루프 재시작 → OnReleased 발행 (피드백 필요)
        ///   ResetGaugeOnly : 전투 초기화 / 내부 리셋 → 이벤트 없이 조용히 초기화
        ///
        /// [사용 케이스]
        ///   씬 로드 직후 강제 초기화, 테스트용 리셋 등.
        ///   일반 전투 루프에서는 ForceRelease 사용 권장.
        /// </summary>
        public void ResetGaugeOnly()
        {
            _currentGauge = 0f;
            _isSealReady = false;
            _isSealed = false;
            _currentStage = 0;

            OnGaugeChanged?.Invoke(0f);
            OnStageChanged?.Invoke(0);
        }

        // ══════════════════════════════════════════════════════
        // 내부 처리
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 봉인도 및 상태를 초기화한다.
        /// </summary>
        /// <param name="resetSealCount">봉인 저항 횟수도 초기화할지 여부.</param>
        private void ResetGauge(bool resetSealCount)
        {
            _currentGauge = 0f;
            _isSealed = false;
            _isSealReady = false;
            _currentStage = 0;

            if (resetSealCount)
                _sealCount = 0;
        }

        /// <summary>
        /// 봉인도 단계 변화를 감지하고 이벤트를 발행한다.
        ///
        /// [단계 정의]
        ///   Stage 0 : 0%
        ///   Stage 1 : 25% 이상
        ///   Stage 2 : 50% 이상
        ///   Stage 3 : 75% 이상
        ///   Stage 4 : 100%
        /// </summary>
        private void CheckStageChange()
        {
            float percent = UIPercent;
            int newStage = 0;

            if (percent >= 100f) newStage = 4;
            else if (percent >= 75f) newStage = 3;
            else if (percent >= 50f) newStage = 2;
            else if (percent >= 25f) newStage = 1;
            else newStage = 0;

            if (newStage != _currentStage)
            {
                _currentStage = newStage;
                OnStageChanged?.Invoke(_currentStage);
                Debug.Log($"[SealGaugeComponent] {gameObject.name} 봉인도 단계 → Stage {_currentStage} ({percent:F0}%)");
            }
        }

        // ══════════════════════════════════════════════════════
        // Gizmos — 에디터 시각화
        // ══════════════════════════════════════════════════════

        private void OnDrawGizmosSelected()
        {
            // 봉인도 상태를 색상으로 시각화
            if (_isSealed)
                Gizmos.color = new Color(0f, 0.27f, 0.8f, 0.5f);   // 봉인 완료 — 파랑
            else if (_isSealReady)
                Gizmos.color = new Color(0f, 0.53f, 1f, 0.5f);     // 집행 가능 — 밝은 파랑
            else
                Gizmos.color = new Color(1f, 0.4f, 0f, 0.3f);      // 진행 중 — 주황

            Gizmos.DrawWireSphere(transform.position, 0.5f);
        }
    }
}