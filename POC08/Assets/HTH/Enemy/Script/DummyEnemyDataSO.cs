// ============================================================
// DummyEnemyDataSO.cs
// DummyEnemy 추적 / 봉인 / 자동 일섬 연쇄 수치 DataSO
//
// [중요]
//   DummyEnemy 봉인 집행은 홀드가 아니다.
//   봉인도 100% + 플레이어가 ExecuteRange 안에 있음 + 봉인 입력 1회
//   → 자동 일섬 체인 시작
//   → 플레이어가 대상에게 이동
//   → 도착 후 해당 적 봉인 집행
//   → 방금 봉인한 적 위치 기준으로 다음 Ready 적 자동 탐색
//   → 중간에 대상이 부활/해제되어 Ready 상태가 아니게 되면 즉시 재탐색
//
// [namespace] SEAL
// ============================================================

using UnityEngine;

namespace SEAL
{
    [CreateAssetMenu(
        menuName = "SEAL/Dummy/DummyEnemyDataSO",
        fileName = "DummyEnemyDataSO")]
    public class DummyEnemyDataSO : ScriptableObject
    {
        [Header("── 추적 ─────────────────────")]
        [Min(0f)] public float MoveSpeed = 2.8f;
        [Min(0f)] public float StopDistance = 0.45f;

        [Header("── 개체별 랜덤 편차 ─────────────────────")]
        [Tooltip("켜두면 DummyEnemy가 생성될 때 이동 속도와 최대 봉인도에 개체별 편차를 적용한다.")]
        public bool UseRuntimeVariance = true;

        [Tooltip("MoveSpeed에 적용할 ±퍼센트 편차. 20이면 MoveSpeed의 80%~120% 사이에서 결정된다.")]
        [Range(0f, 100f)] public float MoveSpeedVariancePercent = 20f;

        [Tooltip("MaxSealGauge에 적용할 ±고정값 편차. 20이면 MaxSealGauge 기준 -20~+20 사이에서 결정된다.")]
        [Min(0f)] public float MaxSealGaugeVarianceAmount = 20f;

        [Header("── 봉인도 ─────────────────────")]
        [Min(1f)] public float MaxSealGauge = 100f;

        [Tooltip("봉인 가능 상태가 된 뒤, 이 시간 안에 집행하지 않으면 다시 살아난다.")]
        [Min(0.1f)] public float ReadyReviveTime = 4.0f;

        [Header("── Enemy AttackHitBox ─────────────────────")]
        [Tooltip("AttackHitBox가 PlayerHealth에 줄 피해량. 추후 정식 PlayerHit 시스템으로 교체 가능.")]
        [Min(0f)] public float AttackDamage = 10f;

        [Tooltip("같은 AttackHitBox가 플레이어에게 다시 피해를 줄 수 있는 최소 간격.")]
        [Min(0f)] public float AttackHitCooldown = 0.6f;

        [Tooltip("Ready 상태에서는 공격 판정을 끈다. DummyEnemy가 봉인 집행 가능 상태일 때 플레이어를 때리지 않게 하기 위한 옵션.")]
        public bool DisableAttackHitBoxWhenReady = true;

        [Tooltip("Sealed 상태에서는 공격 판정을 끈다. 일반적으로 true 유지.")]
        public bool DisableAttackHitBoxWhenSealed = true;

        [Header("── 봉인 집행 시작 ─────────────────────")]
        [Tooltip("플레이어가 이 거리 이내에 있어야 봉인 입력 1회로 자동 일섬을 시작할 수 있다.")]
        [Min(0.1f)] public float ExecuteRange = 4.5f;

        [Header("── 일섬 이동 ─────────────────────")]
        [Tooltip("집행 대상에게 이동하는 속도. 값이 높을수록 순간이동처럼 보인다.")]
        [Min(0.1f)] public float IssenMoveSpeed = 28.0f;

        [Tooltip("이 거리 이내에 도착하면 대상에게 도착한 것으로 보고 봉인 집행한다.")]
        [Min(0.01f)] public float IssenArriveDistance = 0.08f;

        [Tooltip("대상 중심에서 살짝 떨어져 멈추고 싶을 때 사용. 0이면 대상 위치까지 이동한다.")]
        [Min(0f)] public float IssenStopOffset = 0.15f;

        [Tooltip("한 대상에게 이동하는 최대 시간. 벽/예외로 도착 못 하면 다음 대상 재탐색 또는 종료한다.")]
        [Min(0.05f)] public float MaxTravelTimePerTarget = 0.8f;

        [Tooltip("각 대상 봉인 후 다음 대상 탐색/이동 전 대기 시간.")]
        [Min(0f)] public float ChainInterval = 0.04f;

        [Header("── 자동 일섬 연쇄 ─────────────────────")]
        [Tooltip("현재 봉인 집행한 적 위치 기준으로 이 반경 안의 다음 Ready 적을 찾는다.")]
        [Min(0.1f)] public float ChainRadius = 8.5f;

        [Tooltip("첫 대상 포함 최대 집행 수. 0 이하이면 무제한.")]
        public int MaxChainCount = 12;

        [Tooltip("일섬 중 대상이 Ready 상태를 잃었을 때 자동으로 다음 대상을 재탐색한다.")]
        public bool AutoRetargetWhenTargetLost = true;

        [Tooltip("연쇄 연결 고리/다음 대상 후보를 재계산하는 주기.")]
        [Min(0.01f)] public float ChainRecalculateInterval = 0.03f;

        [Tooltip("LineRenderer 미리보기에서 최대 몇 개까지 연결 고리를 그릴지. 0 이하면 MaxChainCount를 사용한다.")]
        public int PreviewMaxChainCount = 12;

        [Header("── 처리 방식 ─────────────────────")]
        [Tooltip("봉인 완료 후 GameObject를 비활성화한다. false면 Sealed 상태로 남긴다.")]
        public bool DeactivateOnSealed = false;

        [Tooltip("봉인 완료 후 비활성화까지 지연 시간.")]
        [Min(0f)] public float DeactivateDelay = 0.15f;

        [Header("── 색상 피드백 ─────────────────────")]
        public Color AliveColor = Color.white;
        public Color HitFlashColor = new Color(1f, 0.7f, 0.9f, 1f);
        public Color ReadyColor = new Color(0.8f, 0.25f, 1f, 1f);
        public Color SealedColor = new Color(0.2f, 0.7f, 1f, 1f);

        public float GetRuntimeMoveSpeed()
        {
            if (!UseRuntimeVariance)
                return MoveSpeed;

            float variance = Mathf.Clamp(MoveSpeedVariancePercent, 0f, 100f) / 100f;
            float multiplier = UnityEngine.Random.Range(1f - variance, 1f + variance);
            return Mathf.Max(0f, MoveSpeed * multiplier);
        }

        public float GetRuntimeMaxSealGauge()
        {
            if (!UseRuntimeVariance)
                return MaxSealGauge;

            float offset = UnityEngine.Random.Range(-MaxSealGaugeVarianceAmount, MaxSealGaugeVarianceAmount);
            return Mathf.Max(1f, MaxSealGauge + offset);
        }

    }
}
