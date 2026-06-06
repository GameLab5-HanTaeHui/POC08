// ============================================================
// BossDataSO.cs  v1.0
// 범용 보스 베이스 ScriptableObject
//
// [역할]
//   모든 보스가 공통으로 사용하는 봉인 시스템 DataSO 묶음.
//   범용 봉인 컴포넌트(SealableComponent, SealExecutionRunner 등)는
//   이 BossDataSO 만 참조하여 어떤 보스에게도 동작.
//
// [포함 SO]
//   SealDataSO      — 봉인도 수치 / 집행 수치 / 슬로우 / 너프
//   SealColorDataSO — 봉인 색상 / DOTween 연출 수치
//
// [확장 방법]
//   보스별 전용 수치는 이 클래스를 상속하여 추가.
//
//   예시:
//     BossWardenDataSO : BossDataSO
//       WardenMovementDataSO  movementData
//       WardenPatternDataSO   patternData
//       WardenPhaseDataSO     phaseData
//
//     BossDragonDataSO : BossDataSO
//       DragonMovementDataSO  movementData
//       DragonPatternDataSO   patternData
//
// [연결 원칙]
//   Inspector 연결은 보스 루트 컴포넌트 1곳에서만.
//   모든 하위 범용 컴포넌트는 Initialize() 로 주입받음.
//
// [범용 컴포넌트 참조 방식]
//   SealableComponent._bossData.SealData.partSealGaugeMax
//   SealableComponent._bossData.ColorData.colorBase
//
// [namespace]
//   namespace : SEAL
// ============================================================

using UnityEngine;

namespace SEAL
{
    /// <summary>
    /// 범용 보스 베이스 ScriptableObject. (v1.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [설계 원칙]
    ///   범용 봉인 컴포넌트는 BossDataSO 만 참조한다.
    ///   보스별 전용 수치는 BossXxxDataSO : BossDataSO 에서 관리.
    ///   어떤 보스든 SealData + ColorData 만 채우면 봉인 시스템 구동 가능.
    ///
    /// [Inspector 연결 체크리스트]
    ///   □ sealData     → SealDataSO 에셋 연결 필수
    ///   □ colorData    → SealColorDataSO 에셋 연결 필수
    /// ────────────────────────────────────────────────────
    /// </summary>
    [CreateAssetMenu(
        menuName = "SEAL/Boss/BossDataSO",
        fileName = "BossDataSO")]
    public class BossDataSO : ScriptableObject
    {
        // ══════════════════════════════════════════════════════
        // 범용 봉인 시스템 SO
        // ══════════════════════════════════════════════════════

        [Header("── 범용 봉인 시스템 (필수) ──────────────────────")]

        /// <summary>
        /// 봉인도 수치 SO.
        /// 부위 봉인도 / 코어 봉인도 / 집행 수치 / 슬로우 / 너프 포함.
        /// 범용 봉인 컴포넌트 전체가 이 SO 를 참조.
        /// 필수 연결.
        /// </summary>
        [Tooltip("봉인도 수치 SO. 필수 연결. SealDataSO 에셋 연결.")]
        [SerializeField] private SealDataSO _sealData;

        /// <summary>
        /// 봉인 색상 / DOTween 연출 수치 SO.
        /// 봉인도 색상 보간 / 맥동 / 피격 점멸 / 파티클 포함.
        /// 범용 봉인 컴포넌트 전체가 이 SO 를 참조.
        /// 필수 연결.
        /// </summary>
        [Tooltip("봉인 색상 / 연출 수치 SO. 필수 연결. SealColorDataSO 에셋 연결.")]
        [SerializeField] private SealColorDataSO _colorData;

        // ══════════════════════════════════════════════════════
        // 프로퍼티 — 외부 참조용
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 봉인도 수치 SO 참조.
        /// SealableComponent, SealExecutionRunner 등 범용 컴포넌트에서 사용.
        ///
        /// [사용 예시]
        ///   _bossData.SealData.partSealGaugeMax
        ///   _bossData.SealData.GetSealResistMultiplier(_sealCount)
        /// </summary>
        public SealDataSO SealData => _sealData;

        /// <summary>
        /// 봉인 색상 / 연출 수치 SO 참조.
        /// SealableComponent, SealExecutionEffect 등에서 사용.
        ///
        /// [사용 예시]
        ///   _bossData.ColorData.GetPartColor(gaugePercent)
        ///   _bossData.ColorData.colorSealed
        ///   _bossData.ColorData.sealReadyPulseDuration
        /// </summary>
        public SealColorDataSO ColorData => _colorData;

        // ══════════════════════════════════════════════════════
        // 유효성 검사
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 필수 SO 연결 여부를 검사한다.
        /// 보스 루트 컴포넌트의 Awake/Start 에서 호출 권장.
        ///
        /// [사용 예시]
        ///   if (!_bossData.IsValid())
        ///   {
        ///       Debug.LogError("[BossCore] BossDataSO 필수 SO 미연결.");
        ///       enabled = false;
        ///   }
        /// </summary>
        /// <returns>모든 필수 SO 연결 시 true.</returns>
        public bool IsValid()
        {
            if (_sealData == null)
            {
                Debug.LogError($"[BossDataSO] {name} — SealDataSO 미연결.");
                return false;
            }

            if (_colorData == null)
            {
                Debug.LogError($"[BossDataSO] {name} — SealColorDataSO 미연결.");
                return false;
            }

            return true;
        }
    }
}