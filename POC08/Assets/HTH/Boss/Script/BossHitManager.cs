// ============================================================
// BossHitManager.cs  v1.0
// Boss_Warden 피격 라우팅 중앙 관리자 — Step 7
//
// [역할]
//   기존 BossWardenPart 각각이 PlayerAttackHitboxManager.OnHit 을 직접 구독하던 구조를
//   BossHitManager 하나가 구독하고, 맞은 Collider 를 BossPartManager 로 조회하여
//   해당 BossWardenPart 에 피격 처리를 위임한다.
//
// [Step 7 범위]
//   - 피격 이벤트 구독 지점을 BossHitManager 로 중앙화
//   - BossWardenPart 의 기존 피격 처리 로직은 최대한 유지
//   - BossWardenPart 는 중앙 매니저가 있으면 직접 OnHit 구독을 하지 않음
//   - 봉인도 계산 / 가드 무효 / 취약 배율은 아직 BossWardenPart 내부에 유지
//   - 이후 Step에서 BossSealManager 로 봉인도 규칙을 추가 이관할 준비
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
    /// Boss_Warden 피격 라우팅 중앙 관리자.
    /// PlayerAttackHitboxManager 의 OnHit 을 한 곳에서 수신하고,
    /// Collider 기준으로 어느 부위가 맞았는지 찾아 BossWardenPart 에 위임한다.
    /// </summary>
    [DefaultExecutionOrder(-4)]
    public class BossHitManager : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── 연결 컴포넌트 ──────────────────────")]

        [Tooltip("BossPartManager. 미연결 시 같은 오브젝트에서 자동 탐색.")]
        [SerializeField] private BossPartManager _partManager;

        [Tooltip("BossEventHub. 미연결 시 같은 오브젝트에서 자동 탐색.")]
        [SerializeField] private BossEventHub _eventHub;

        [Header("── Player HitBox Manager ──────────────────────")]

        [Tooltip("PlayerAttackHitboxManager. 미연결 시 씬에서 자동 탐색.")]
        [SerializeField] private PlayerAttackHitboxManager _hitboxManager;

        [Tooltip("Start에서 PlayerAttackHitboxManager를 자동 탐색하고 OnHit을 구독한다.")]
        [SerializeField] private bool _autoSubscribePlayerHitbox = true;

        [Header("── Debug ──────────────────────")]

        [Tooltip("피격 라우팅 로그 출력 여부.")]
        [SerializeField] private bool _debugLog;

        // ══════════════════════════════════════════════════════
        // Runtime
        // ══════════════════════════════════════════════════════

        private bool _subscribed;

        // ══════════════════════════════════════════════════════
        // Unity Lifecycle
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            if (_partManager == null) _partManager = GetComponent<BossPartManager>();
            if (_eventHub == null) _eventHub = GetComponent<BossEventHub>();
        }

        private void Start()
        {
            if (_autoSubscribePlayerHitbox)
                SubscribePlayerHitbox();
        }

        private void OnDestroy()
        {
            UnsubscribePlayerHitbox();
        }

        // ══════════════════════════════════════════════════════
        // Subscribe
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// PlayerAttackHitboxManager.OnHit 구독.
        /// Step 7 이후 권장 구독 지점은 BossHitManager 하나이다.
        /// </summary>
        public void SubscribePlayerHitbox()
        {
            if (_subscribed) return;

            if (_hitboxManager == null)
            {
                var managers = FindObjectsByType<PlayerAttackHitboxManager>(FindObjectsSortMode.None);
                if (managers.Length > 0)
                    _hitboxManager = managers[0];
            }

            if (_hitboxManager == null)
            {
                Debug.LogWarning($"[BossHitManager] {name} — PlayerAttackHitboxManager 없음. 피격 중앙 라우팅 비활성.");
                return;
            }

            _hitboxManager.OnHit += HandlePlayerHit;
            _subscribed = true;

            if (_debugLog)
                Debug.Log($"[BossHitManager] {name} — PlayerAttackHitboxManager.OnHit 구독 완료");
        }

        /// <summary>
        /// PlayerAttackHitboxManager.OnHit 구독 해제.
        /// </summary>
        public void UnsubscribePlayerHitbox()
        {
            if (!_subscribed) return;

            if (_hitboxManager != null)
                _hitboxManager.OnHit -= HandlePlayerHit;

            _subscribed = false;
        }

        // ══════════════════════════════════════════════════════
        // Hit Routing
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// PlayerAttackHitboxManager.OnHit 수신.
        /// Collider 기준으로 BossWardenPart 를 찾아 피격 처리를 위임한다.
        /// </summary>
        private void HandlePlayerHit(Collider2D hitCol, float sealAmount)
        {
            if (hitCol == null) return;

            BossWardenPart targetPart = FindTargetPart(hitCol);
            if (targetPart == null)
            {
                if (_debugLog)
                    Debug.Log($"[BossHitManager] 매칭되는 BossWardenPart 없음: {hitCol.name}");
                return;
            }

            Vector3 hitPoint = hitCol.bounds.center;
            BossPartHitResult result = targetPart.TryReceiveHit(sealAmount, hitPoint);

            RaiseHitEvent(targetPart, result, hitPoint);

            if (_debugLog)
                Debug.Log($"[BossHitManager] {targetPart.PartType} hit result: {result}, amount: {sealAmount}");
        }

        /// <summary>
        /// Collider 로부터 대상 BossWardenPart 조회.
        /// 우선 BossPartManager 를 사용하고, 실패 시 Collider 부모에서 fallback 탐색한다.
        /// </summary>
        private BossWardenPart FindTargetPart(Collider2D hitCol)
        {
            if (_partManager != null)
            {
                BossWardenPart part = _partManager.GetPartByCollider(hitCol);
                if (part != null)
                    return part;
            }

            return hitCol.GetComponentInParent<BossWardenPart>();
        }

        /// <summary>
        /// 피격 처리 결과를 BossEventHub에 발행한다.
        /// 실제 VFX/Sound 구독 이동은 이후 Step에서 진행한다.
        /// </summary>
        private void RaiseHitEvent(BossWardenPart part, BossPartHitResult result, Vector3 hitPoint)
        {
            if (_eventHub == null || part == null) return;

            switch (result)
            {
                case BossPartHitResult.Applied:
                    if (part.IsWeakVulnerable)
                        _eventHub.RaiseWeakPointHit(part.PartType, hitPoint);
                    else
                        _eventHub.RaisePartHit(part.PartType, hitPoint);
                    break;

                case BossPartHitResult.BlockedByGuard:
                    _eventHub.RaiseHitBlocked(part.PartType, hitPoint);
                    break;
            }
        }
    }
}
