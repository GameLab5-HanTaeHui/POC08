// ============================================================
// BossPartManager.cs v3.0
// Boss_Warden 부위 참조 중앙 관리자 — Step 23 Dynamic Part List
//
// [핵심 변경]
//   고정 4팔 구조 폐기.
//   Left/Right/Core를 모두 List 기반으로 관리한다.
//
// [목표]
//   - 팔/파츠가 2개, 4개, 6개 등으로 변해도 Inspector List만 수정
//   - 패턴, 봉인, 피격, 일섬 집행은 모두 이 Manager의 List 기준 사용
//   - 기존 LeftArm/RightArm/CorePart 프로퍼티는 호환용으로 첫 번째 항목만 반환
// ============================================================

using System.Collections.Generic;
using UnityEngine;

namespace SEAL
{
    [DefaultExecutionOrder(-90)]
    public class BossPartManager : MonoBehaviour
    {
        [Header("── Dynamic Part Lists ──────────────────────")]
        [Tooltip("왼쪽 계열 팔/파츠 목록. 개수 제한 없음.")]
        [SerializeField] private List<BossWardenPart> _leftParts = new();

        [Tooltip("오른쪽 계열 팔/파츠 목록. 개수 제한 없음.")]
        [SerializeField] private List<BossWardenPart> _rightParts = new();

        [Tooltip("코어 파츠 목록. 보통 1개지만, 필요하면 여러 개도 가능.")]
        [SerializeField] private List<BossWardenPart> _coreParts = new();

        [Header("── Core Object ──────────────────────")]
        [Tooltip("대표 코어 GameObject. 미연결 시 첫 번째 CorePart.gameObject 사용.")]
        [SerializeField] private GameObject _coreObject;

        [Header("── Auto Collect ──────────────────────")]
        [Tooltip("true면 List에 누락된 BossWardenPart를 보스 루트 하위에서 자동 보강한다.")]
        [SerializeField] private bool _autoCollectChildParts = true;

        [Tooltip("자동 수집 기준 루트. 비워두면 transform.root 사용.")]
        [SerializeField] private Transform _autoCollectRoot;

        [Tooltip("자동 수집 결과를 Inspector List에 반영하지 않고 런타임 캐시에만 사용한다.")]
        [SerializeField] private bool _autoCollectRuntimeOnly = true;

        [SerializeField] private bool _debugLog;

        private readonly List<BossWardenPart> _leftCache = new();
        private readonly List<BossWardenPart> _rightCache = new();
        private readonly List<BossWardenPart> _coreCache = new();
        private readonly List<BossWardenPart> _allCache = new();
        private readonly List<SealableComponent> _sealableCache = new();

        public IReadOnlyList<BossWardenPart> LeftParts => GetLeftArmParts();
        public IReadOnlyList<BossWardenPart> RightParts => GetRightArmParts();
        public IReadOnlyList<BossWardenPart> CoreParts => GetCoreParts();

        // 기존 코드 호환용 이름. 이제 개수 제한 없는 List 기반이다.
        public IReadOnlyList<BossWardenPart> LeftArms => GetLeftArmParts();
        public IReadOnlyList<BossWardenPart> RightArms => GetRightArmParts();

        // 기존 코드 호환용: 첫 번째 유효 항목만 반환한다.
        public BossWardenPart LeftArm => GetFirstValid(GetLeftArmParts());
        public BossWardenPart RightArm => GetFirstValid(GetRightArmParts());
        public BossWardenPart CorePart => GetFirstValid(GetCoreParts());

        public GameObject CoreObject
        {
            get
            {
                if (_coreObject != null) return _coreObject;
                BossWardenPart core = CorePart;
                return core != null ? core.gameObject : null;
            }
        }

        public Transform LeftArmTransform => LeftArm != null ? LeftArm.transform : null;
        public Transform RightArmTransform => RightArm != null ? RightArm.transform : null;
        public Transform CoreTransform => CoreObject != null ? CoreObject.transform : null;

        public SealableComponent LeftArmSealable => LeftArm != null ? LeftArm.Sealable : null;
        public SealableComponent RightArmSealable => RightArm != null ? RightArm.Sealable : null;
        public SealableComponent CoreSealable => CorePart != null ? CorePart.Sealable : null;

        public IReadOnlyList<BossWardenPart> GetLeftArmParts()
        {
            _leftCache.Clear();
            AddPartsToList(_leftCache, _leftParts, WardenPartType.LeftArm);
            AddAutoPartsToList(_leftCache, WardenPartType.LeftArm);
            return _leftCache;
        }

        public IReadOnlyList<BossWardenPart> GetRightArmParts()
        {
            _rightCache.Clear();
            AddPartsToList(_rightCache, _rightParts, WardenPartType.RightArm);
            AddAutoPartsToList(_rightCache, WardenPartType.RightArm);
            return _rightCache;
        }

        public IReadOnlyList<BossWardenPart> GetCoreParts()
        {
            _coreCache.Clear();
            AddPartsToList(_coreCache, _coreParts, WardenPartType.Core);
            AddAutoPartsToList(_coreCache, WardenPartType.Core);
            return _coreCache;
        }

        public IReadOnlyList<BossWardenPart> GetArmParts(WardenPartType side)
        {
            return side switch
            {
                WardenPartType.LeftArm => GetLeftArmParts(),
                WardenPartType.RightArm => GetRightArmParts(),
                _ => GetAllArmParts(),
            };
        }

        public IReadOnlyList<BossWardenPart> GetAllArmParts()
        {
            _allCache.Clear();
            AddListNoDuplicate(_allCache, GetLeftArmParts());
            AddListNoDuplicate(_allCache, GetRightArmParts());
            return _allCache;
        }

        public IReadOnlyList<BossWardenPart> GetAllParts(bool includeCore = true)
        {
            _allCache.Clear();
            AddListNoDuplicate(_allCache, GetLeftArmParts());
            AddListNoDuplicate(_allCache, GetRightArmParts());

            if (includeCore)
                AddListNoDuplicate(_allCache, GetCoreParts());

            return _allCache;
        }

        public IReadOnlyList<SealableComponent> GetAllSealables(bool includeCore = true)
        {
            _sealableCache.Clear();

            foreach (var part in GetAllParts(includeCore))
            {
                if (part == null || part.Sealable == null) continue;
                if (!_sealableCache.Contains(part.Sealable))
                    _sealableCache.Add(part.Sealable);
            }

            return _sealableCache;
        }

        public BossWardenPart GetRandomAvailableArm(bool includeLeft = true, bool includeRight = true)
        {
            _allCache.Clear();

            if (includeLeft) AddAvailablePartsToList(_allCache, GetLeftArmParts());
            if (includeRight) AddAvailablePartsToList(_allCache, GetRightArmParts());

            if (_allCache.Count == 0)
                return null;

            return _allCache[Random.Range(0, _allCache.Count)];
        }

        public BossWardenPart GetPart(WardenPartType partType)
        {
            return partType switch
            {
                WardenPartType.LeftArm => LeftArm,
                WardenPartType.RightArm => RightArm,
                WardenPartType.Core => CorePart,
                _ => null,
            };
        }

        public BossWardenPart GetPartByCollider(Collider2D hitCollider)
        {
            if (hitCollider == null)
                return null;

            foreach (var part in GetAllParts(includeCore: true))
            {
                if (part != null && part.ContainsCollider(hitCollider))
                    return part;
            }

            return null;
        }

        public Transform GetTransform(WardenPartType partType)
        {
            BossWardenPart part = GetPart(partType);
            return part != null ? part.transform : null;
        }

        public SealableComponent GetSealable(WardenPartType partType)
        {
            BossWardenPart part = GetPart(partType);
            return part != null ? part.Sealable : null;
        }

        public bool IsValid()
        {
            int leftCount = GetLeftArmParts().Count;
            int rightCount = GetRightArmParts().Count;
            int armCount = GetAllArmParts().Count;
            int coreCount = GetCoreParts().Count;
            GameObject coreObj = CoreObject;

            bool valid = true;

            if (armCount <= 0)
            {
                Debug.LogError($"[BossPartManager] {name} — Arm Part가 없습니다. Left/Right List에 최소 1개 이상 연결하세요.");
                valid = false;
            }

            if (coreCount <= 0 || coreObj == null)
            {
                Debug.LogError($"[BossPartManager] {name} — CorePart/CoreObject 미연결/미수집.");
                valid = false;
            }

            if (_debugLog)
                Debug.Log($"[BossPartManager] Dynamic 수집 결과 | Left:{leftCount} Right:{rightCount} Arms:{armCount} Core:{coreCount}");

            return valid;
        }

        [ContextMenu("Rebuild Auto Collected Lists")]
        public void RebuildAutoCollectedLists()
        {
            if (_autoCollectRuntimeOnly) return;

            _leftParts ??= new List<BossWardenPart>();
            _rightParts ??= new List<BossWardenPart>();
            _coreParts ??= new List<BossWardenPart>();

            Transform root = GetCollectRoot();
            if (root == null) return;

            foreach (var part in root.GetComponentsInChildren<BossWardenPart>(true))
            {
                if (part == null) continue;

                switch (part.PartType)
                {
                    case WardenPartType.LeftArm:
                        AddIfMissing(_leftParts, part);
                        break;
                    case WardenPartType.RightArm:
                        AddIfMissing(_rightParts, part);
                        break;
                    case WardenPartType.Core:
                        AddIfMissing(_coreParts, part);
                        break;
                }
            }
        }

        private void AddPartsToList(List<BossWardenPart> target, List<BossWardenPart> source, WardenPartType filter)
        {
            if (target == null || source == null) return;

            for (int i = source.Count - 1; i >= 0; i--)
            {
                BossWardenPart part = source[i];
                if (part == null) continue;
                if (part.PartType != filter) continue;
                AddIfMissing(target, part);
            }
        }

        private void AddAutoPartsToList(List<BossWardenPart> list, WardenPartType filter)
        {
            if (!_autoCollectChildParts || list == null) return;

            Transform root = GetCollectRoot();
            if (root == null) return;

            BossWardenPart[] parts = root.GetComponentsInChildren<BossWardenPart>(true);
            foreach (var part in parts)
            {
                if (part == null) continue;
                if (part.PartType != filter) continue;
                AddIfMissing(list, part);
            }
        }

        private Transform GetCollectRoot()
        {
            if (_autoCollectRoot != null) return _autoCollectRoot;
            return transform.root != null ? transform.root : transform;
        }

        private void AddAvailablePartsToList(List<BossWardenPart> list, IReadOnlyList<BossWardenPart> parts)
        {
            if (list == null || parts == null) return;

            foreach (var part in parts)
            {
                if (part == null) continue;
                if (part.IsSealed) continue;
                AddIfMissing(list, part);
            }
        }

        private static void AddListNoDuplicate(List<BossWardenPart> target, IReadOnlyList<BossWardenPart> source)
        {
            if (target == null || source == null) return;

            for (int i = 0; i < source.Count; i++)
                AddIfMissing(target, source[i]);
        }

        private static void AddIfMissing(List<BossWardenPart> list, BossWardenPart part)
        {
            if (list == null || part == null) return;
            if (!list.Contains(part))
                list.Add(part);
        }

        private static BossWardenPart GetFirstValid(IReadOnlyList<BossWardenPart> parts)
        {
            if (parts == null) return null;
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i] != null)
                    return parts[i];
            }
            return null;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            _leftParts ??= new List<BossWardenPart>();
            _rightParts ??= new List<BossWardenPart>();
            _coreParts ??= new List<BossWardenPart>();

            if (_coreObject == null && _coreParts.Count > 0 && _coreParts[0] != null)
                _coreObject = _coreParts[0].gameObject;
        }
#endif
    }
}
