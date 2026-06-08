// ============================================================
// BossPartManager.cs  v1.1
// Boss 부위 참조 중앙 관리자 — Step 4
//
// [역할]
//   Boss_Warden의 LeftArm / RightArm / Core 참조를 한곳으로 모은다.
//   기존 BossWardenCore, 패턴, VFX, 봉인 코드가 각각 팔/코어를
//   직접 들고 있던 구조를 단계적으로 정리하기 위한 중앙 관리자이다.
//
// [Step 4 범위]
//   - LeftArm / RightArm / CorePart / CoreObject 참조 보관
//   - Transform / Collider / Renderer / SealableComponent 접근 제공
//   - BossWardenCore가 이 Manager를 통해 부위 참조를 가져오도록 만든다.
//   - 아직 모든 패턴/VFX/봉인 코드의 직접 참조를 제거하지는 않는다.
//
// [Step 7 추가]
//   - BossHitManager가 Collider 기준으로 부위를 찾을 수 있도록
//     GetPartByCollider(Collider2D) 조회 API 추가.
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
    /// Boss_Warden 부위 참조 중앙 관리자.
    /// Step 4에서는 기존 직접 연결 구조를 유지하면서, 부위 참조의 권장 진입점을 제공한다.
    /// 이후 Step에서 패턴, 피격, VFX, 봉인 코드가 이 Manager를 통해 부위를 조회하도록 확장한다.
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public class BossPartManager : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── Warden Parts ──────────────────────")]

        [Tooltip("왼팔 BossWardenPart.")]
        [SerializeField] private BossWardenPart _leftArm;

        [Tooltip("오른팔 BossWardenPart.")]
        [SerializeField] private BossWardenPart _rightArm;

        [Tooltip("코어 BossWardenPart. 선택 연결이지만 연결 권장.")]
        [SerializeField] private BossWardenPart _corePart;

        [Header("── Core Object ──────────────────────")]

        [Tooltip("코어 GameObject. 미연결 시 CorePart.gameObject를 사용한다.")]
        [SerializeField] private GameObject _coreObject;

        // ══════════════════════════════════════════════════════
        // Public Accessors
        // ══════════════════════════════════════════════════════

        public BossWardenPart LeftArm => _leftArm;
        public BossWardenPart RightArm => _rightArm;
        public BossWardenPart CorePart => _corePart;

        public GameObject CoreObject
        {
            get
            {
                if (_coreObject != null) return _coreObject;
                return _corePart != null ? _corePart.gameObject : null;
            }
        }

        public Transform LeftArmTransform => _leftArm != null ? _leftArm.transform : null;
        public Transform RightArmTransform => _rightArm != null ? _rightArm.transform : null;
        public Transform CoreTransform => CoreObject != null ? CoreObject.transform : null;

        public SealableComponent LeftArmSealable => _leftArm != null ? _leftArm.Sealable : null;
        public SealableComponent RightArmSealable => _rightArm != null ? _rightArm.Sealable : null;
        public SealableComponent CoreSealable => _corePart != null ? _corePart.Sealable : null;

        // ══════════════════════════════════════════════════════
        // Lookup
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// WardenPartType 기준으로 BossWardenPart를 반환한다.
        /// </summary>
        public BossWardenPart GetPart(WardenPartType partType)
        {
            switch (partType)
            {
                case WardenPartType.LeftArm:
                    return _leftArm;
                case WardenPartType.RightArm:
                    return _rightArm;
                case WardenPartType.Core:
                    return _corePart;
                default:
                    return null;
            }
        }



        /// <summary>
        /// Collider2D 기준으로 BossWardenPart를 반환한다.
        /// BossHitManager가 PlayerAttackHitboxManager.OnHit 결과를 라우팅할 때 사용한다.
        /// </summary>
        public BossWardenPart GetPartByCollider(Collider2D hitCollider)
        {
            if (hitCollider == null)
                return null;

            if (_leftArm != null && _leftArm.ContainsCollider(hitCollider))
                return _leftArm;

            if (_rightArm != null && _rightArm.ContainsCollider(hitCollider))
                return _rightArm;

            if (_corePart != null && _corePart.ContainsCollider(hitCollider))
                return _corePart;

            return hitCollider.GetComponentInParent<BossWardenPart>();
        }

        /// <summary>
        /// WardenPartType 기준으로 Transform을 반환한다.
        /// </summary>
        public Transform GetTransform(WardenPartType partType)
        {
            BossWardenPart part = GetPart(partType);
            return part != null ? part.transform : null;
        }

        /// <summary>
        /// WardenPartType 기준으로 SealableComponent를 반환한다.
        /// </summary>
        public SealableComponent GetSealable(WardenPartType partType)
        {
            BossWardenPart part = GetPart(partType);
            return part != null ? part.Sealable : null;
        }

        // ══════════════════════════════════════════════════════
        // Validation
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 필수 부위 참조 연결 상태를 검사한다.
        /// CorePart는 선택 연결로 두지만, CoreObject 또는 CorePart 중 하나는 필요하다.
        /// </summary>
        public bool IsValid()
        {
            bool valid = true;

            if (_leftArm == null)
            {
                Debug.LogError($"[BossPartManager] {name} — LeftArm 미연결.");
                valid = false;
            }

            if (_rightArm == null)
            {
                Debug.LogError($"[BossPartManager] {name} — RightArm 미연결.");
                valid = false;
            }

            if (CoreObject == null)
            {
                Debug.LogError($"[BossPartManager] {name} — CoreObject 또는 CorePart 미연결.");
                valid = false;
            }

            return valid;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (_coreObject == null && _corePart != null)
                _coreObject = _corePart.gameObject;
        }
#endif
    }
}
