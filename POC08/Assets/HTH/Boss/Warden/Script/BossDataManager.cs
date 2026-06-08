// ============================================================
// BossDataManager.cs  v1.0
// Boss 데이터 중앙 관리자 — Step 3
//
// [역할]
//   보스 루트에서 모든 DataSO 접근을 한곳으로 모은다.
//   기존 BossWardenCore._data 직접 연결 구조를 대체하기 위한
//   첫 번째 중앙 관리자이다.
//
// [Step 3 범위]
//   - BossWardenDataSO 단일 참조 보관
//   - BossDataSO / SealDataSO / SealColorDataSO / PatternDataSO 접근 제공
//   - 아직 모든 컴포넌트의 _data 직접 참조를 제거하지는 않는다.
//   - BossWardenCore가 이 Manager를 통해 데이터를 가져오도록 만든다.
//
// [부착 위치]
//   Boss_Warden Root 오브젝트
//
// [namespace] SEAL
// ============================================================

using UnityEngine;

namespace SEAL
{
    /// <summary>
    /// 보스 DataSO 중앙 관리자.
    /// Step 3에서는 BossWardenDataSO를 단일 진입점으로 보관하고,
    /// 이후 단계에서 각 Manager들이 이 컴포넌트를 통해 DataSO를 읽도록 확장한다.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class BossDataManager : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── Warden DataSO ──────────────────────")]

        [Tooltip("Boss_Warden 전용 DataSO. SealData / ColorData / PatternData를 포함한다.")]
        [SerializeField] private BossWardenDataSO _wardenData;

        // ══════════════════════════════════════════════════════
        // Public Accessors
        // ══════════════════════════════════════════════════════

        /// <summary>Warden 전용 DataSO.</summary>
        public BossWardenDataSO WardenData => _wardenData;

        /// <summary>범용 BossDataSO 접근. 범용 봉인 컴포넌트가 읽을 수 있는 데이터.</summary>
        public BossDataSO BossData => _wardenData;

        /// <summary>범용 봉인 수치 DataSO.</summary>
        public SealDataSO SealData => _wardenData != null ? _wardenData.SealData : null;

        /// <summary>봉인 색상 / 연출 수치 DataSO.</summary>
        public SealColorDataSO ColorData => _wardenData != null ? _wardenData.ColorData : null;

        /// <summary>Warden 공격 패턴 전용 DataSO.</summary>
        public BossWardenPatternDataSO PatternData => _wardenData != null ? _wardenData.PatternData : null;

        // ══════════════════════════════════════════════════════
        // Validation
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 필수 DataSO 연결 상태를 검사한다.
        /// </summary>
        public bool IsValid()
        {
            if (_wardenData == null)
            {
                Debug.LogError($"[BossDataManager] {name} — BossWardenDataSO 미연결.");
                return false;
            }

            return _wardenData.IsValid();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // 지금 단계에서는 자동 탐색을 하지 않는다.
            // DataSO는 명시적으로 연결하는 편이 프리팹 구조 추적에 더 안전하다.
        }
#endif
    }
}
