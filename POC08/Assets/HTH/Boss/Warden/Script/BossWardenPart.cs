// ============================================================
// BossWardenPart.cs v2.0
// Boss_Warden 부위 피격 처리 컴포넌트 — Step 20
//
// [Step 20]
//   4팔 구조 대응.
//   GuardBreak 제거 흐름에 맞춰 BossPattern_GuardBreak / BossWardenAI 의존성 제거.
//   피격 라우팅은 BossHitManager만 담당하고, 이 컴포넌트는 TryReceiveHit만 제공한다.
//
// [Step 21]
//   보스 일섬 집행용 ExecutionPoint를 추가한다.
//   미연결 시 transform.position을 사용한다.
// ============================================================

using UnityEngine;

namespace SEAL
{
    public enum WardenPartType
    {
        LeftArm,
        RightArm,
        Core,
    }

    public enum BossPartHitResult
    {
        None,
        Miss,
        Invalid,
        AlreadySealed,
        BlockedByGuard,
        Applied,
    }

    [RequireComponent(typeof(SealableComponent))]
    public class BossWardenPart : MonoBehaviour
    {
        [Header("── 부위 설정 ──────────────────────")]
        [SerializeField] private WardenPartType _partType;

        [Header("── 컴포넌트 연결 ──────────────────────")]
        [SerializeField] private Collider2D _ownCollider;
        [SerializeField] private BossWardenDataSO _data;

        [Header("── 봉인 집행 위치 ──────────────────────")]
        [Tooltip("보스 일섬 집행 시 플레이어가 이동할 위치. 비워두면 이 파츠 Transform을 사용합니다.")]
        [SerializeField] private Transform _executionPoint;

        private SealableComponent _sealable;
        private bool _isRecoveryVuln;
        private bool _isSlamVuln;
        private float _slamVulnMultiplier = 1f;

        public bool IsSealed => _sealable != null && _sealable.IsSealed;
        public WardenPartType PartType => _partType;
        public SealableComponent Sealable => _sealable;
        public Collider2D OwnCollider => _ownCollider;
        public Transform ExecutionPoint => _executionPoint != null ? _executionPoint : transform;
        public Vector2 ExecutionPosition => ExecutionPoint != null ? (Vector2)ExecutionPoint.position : (Vector2)transform.position;
        public bool IsWeakVulnerable => _isRecoveryVuln || _isSlamVuln;

        private void Awake()
        {
            _sealable = GetComponent<SealableComponent>();

            if (_sealable == null)
                Debug.LogError($"[BossWardenPart] {gameObject.name} — SealableComponent 미부착.");

            if (_ownCollider == null)
                Debug.LogWarning($"[BossWardenPart] {gameObject.name} — OwnCollider 미연결.");

            if (_executionPoint == null)
                _executionPoint = transform;
        }

        private void Start()
        {
            if (_sealable != null)
            {
                _sealable.OnSealCompleted += HandleSealCompleted;
                _sealable.OnForceReleased += HandleForceReleased;
            }
        }

        private void OnDestroy()
        {
            if (_sealable != null)
            {
                _sealable.OnSealCompleted -= HandleSealCompleted;
                _sealable.OnForceReleased -= HandleForceReleased;
            }
        }

        public void Initialize(BossWardenDataSO data)
        {
            _data = data;
            _sealable?.Initialize((BossDataSO)data);
        }

        public bool ContainsCollider(Collider2D hitCol)
        {
            return hitCol != null && hitCol == _ownCollider;
        }

        public BossPartHitResult TryReceiveHit(float sealAmount, Vector3 hitPoint)
        {
            if (_sealable == null) return BossPartHitResult.Invalid;
            if (IsSealed) return BossPartHitResult.AlreadySealed;

            float finalAmount = sealAmount;

            if (_partType != WardenPartType.Core)
            {
                if (_isRecoveryVuln && _data != null)
                    finalAmount *= _data.recoveryVulnMultiplier;

                if (_isSlamVuln)
                    finalAmount *= _slamVulnMultiplier;
            }

            _sealable.AddGauge(finalAmount);
            _sealable.PlayHitFlash();
            return BossPartHitResult.Applied;
        }

        private void HandleSealCompleted()
        {
            if (_ownCollider != null)
                _ownCollider.enabled = false;
        }

        private void HandleForceReleased()
        {
            if (_ownCollider != null)
                _ownCollider.enabled = true;
        }

        public void SetRecoveryVuln(bool isVuln)
        {
            if (_partType == WardenPartType.Core) return;
            _isRecoveryVuln = isVuln;
        }

        public void SetSlamVuln(bool isActive, float multiplier = 1f)
        {
            if (_partType == WardenPartType.Core) return;
            _isSlamVuln = isActive;
            _slamVulnMultiplier = multiplier;
        }

        public void SetColliderEnabled(bool enabled)
        {
            if (_ownCollider != null)
                _ownCollider.enabled = enabled;
        }

        public void ForceRelease(bool resetSealCount = false)
        {
            _sealable?.ForceRelease(resetSealCount);
        }
    }
}
