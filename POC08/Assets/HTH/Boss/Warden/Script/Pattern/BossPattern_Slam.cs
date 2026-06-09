// ============================================================
// BossPattern_Slam.cs v5.1
// Boss_Warden 내려치기 패턴 — Arm FIFO Queue + Per-Arm Target Lock
//
// [수정 내용]
//   - Step27 패턴 인스턴스 Queue 방식 폐기.
//   - Slam 패턴 인스턴스는 1개만 유지한다.
//   - 실행 시점에 봉인되지 않은 팔을 골라 FIFO Queue로 처리한다.
//   - 각 팔 실행 직전 플레이어 위치를 다시 읽고, 그 위치를 예고/실제 공격 위치로 고정한다.
//   - 타격 후/복귀 중 취약 시간을 보장해 플레이어가 팔을 공격할 시간을 만든다.
// ============================================================

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

namespace SEAL
{
    public class BossPattern_Slam : BossPatternBase
    {
        [Header("── Manager 연결 ──────────────────────")]
        [SerializeField] private BossVFXManager _vfxManager;
        [SerializeField] private BossAttackManager _attackManager;
        [SerializeField] private BossPartManager _partManager;
        [SerializeField] private BossWardenDataSO _data;

        [Header("── 팔 후보 ──────────────────────")]
        [Tooltip("true면 직접 후보 배열보다 BossPartManager의 동적 팔 목록을 우선 사용합니다.")]
        [SerializeField] private bool _usePartManagerArmCandidates = true;

        [Tooltip("직접 지정된 Slam 후보 팔. _usePartManagerArmCandidates=false일 때만 우선 사용.")]
        [SerializeField] private BossWardenPart[] _slamArmCandidates;

        [Header("── Arm FIFO Queue ──────────────────────")]
        [Tooltip("true면 이 Slam 패턴 1개가 선택된 팔들을 FIFO Queue로 순서대로 실행합니다.")]
        [SerializeField] private bool _useArmQueue = true;

        [Tooltip("패턴 1회에서 사용하려고 시도할 팔 수. 0이면 사용 가능한 모든 팔을 사용합니다. 봉인된 팔은 자동 제외됩니다.")]
        [Min(0)][SerializeField] private int _maxArmsPerPattern = 0;

        [Tooltip("Queue 구성 시 팔 순서를 랜덤으로 섞습니다.")]
        [SerializeField] private bool _randomizeArmOrder = true;

        [Tooltip("FIFO Queue에서 각 팔 Slam 사이의 간격.")]
        [Min(0f)][SerializeField] private float _queueInterval = 0.08f;

        [Tooltip("true면 FIFO의 각 팔이 시작될 때마다 플레이어 위치를 새로 읽고, 그 위치를 예고/실제 공격 위치로 고정합니다.")]
        [SerializeField] private bool _retargetEachArmOnStart = true;

        [Tooltip("각 팔의 짧은 개별 백스윙 시간.")]
        [Min(0.01f)][SerializeField] private float _perArmWindupDuration = 0.14f;

        [Header("── 판정 ──────────────────────")]
        [SerializeField] private LayerMask _playerLayer;
        [Min(0f)][SerializeField] private float _hitRadius = 2.5f;
        [Min(0f)][SerializeField] private float _warningRadius = 3.0f;

        [Header("── 이동/연출 ──────────────────────")]
        [Min(0.05f)][SerializeField] private float _throwDuration = 0.18f;
        [Min(0.05f)][SerializeField] private float _returnDuration = 0.28f;
        [Min(0f)][SerializeField] private float _backSwingDistance = 0.8f;
        [Tooltip("타격 직후, 복귀 전 바닥에 남아있는 취약 시간.")]
        [Min(0f)][SerializeField] private float _slamVulnWindow = 0.55f;

        [Tooltip("복귀 완료 후 추가로 팔을 공격할 수 있는 여유 시간.")]
        [Min(0f)][SerializeField] private float _postReturnVulnWindow = 0.25f;

        [Min(1f)][SerializeField] private float _slamVulnMultiplier = 2.0f;

        [Header("── 팔 방향 보정 ──────────────────────")]
        [Tooltip("true면 팔의 Vector2.down 방향이 플레이어/목표 방향을 바라보도록 회전합니다.")]
        [SerializeField] private bool _rotateDownAxisToTarget = true;

        private readonly List<BossWardenPart> _availableParts = new();
        private readonly Queue<BossWardenPart> _armQueue = new();

        private BossWardenPart _activePart;
        private Transform _activeArm;
        private Transform _originParent;
        private Vector3 _originLocalPosition;
        private Quaternion _originLocalRotation;
        private Vector3 _originLocalScale;
        private Vector2 _targetPos;
        private bool _isDetached;
        private bool _isPhase2;

        public override bool IsAvailable
        {
            get
            {
                ResolveReferences();
                return BuildAvailablePartSequence(previewOnly: true).Count > 0;
            }
        }

        private void Awake()
        {
            ResolveReferences();
            _triggerGroggyOnRecovery = false;
        }

        private void OnDestroy()
        {
            ReattachImmediate();
        }

        public override void Initialize(BossWardenDataSO data)
        {
            base.Initialize(data);
            ResolveReferences();

            _data = data != null ? data : _data;
            if (_data != null)
            {
                _hitRadius = _data.slamHitRadius;
                _warningRadius = _data.slamWarningRadius;
            }
        }

        public override void UnlockPhase2()
        {
            base.UnlockPhase2();
            _isPhase2 = true;
        }

        protected override IEnumerator OnWarning()
        {
            ResolveReferences();

            BuildArmQueue();
            if (_armQueue.Count == 0) yield break;

            // 전체 패턴 진입 경고 시간. 실제 공격 예고 범위는 각 팔 실행 직전에 새로 표시한다.
            _vfxManager?.HideSlamDisc(0);
            yield return StartCoroutine(WaitForPattern(_warningDuration));
        }

        protected override IEnumerator OnActive()
        {
            if (_isInterrupted) yield break;
            if (_armQueue.Count == 0) yield break;

            _vfxManager?.StopAllPulse();

            while (_armQueue.Count > 0)
            {
                if (_isInterrupted) yield break;

                BossWardenPart part = _armQueue.Dequeue();
                if (part == null || part.IsSealed) continue;

                yield return StartCoroutine(ExecuteSingleSlam(part));

                if (_isInterrupted) yield break;
                if (_queueInterval > 0f && _armQueue.Count > 0)
                    yield return StartCoroutine(WaitForPattern(_queueInterval));
            }
        }

        protected override IEnumerator OnRecovery()
        {
            ReattachTween(_returnDuration);
            yield return StartCoroutine(WaitForPattern(_recoveryDuration));
        }

        public override void Interrupt()
        {
            _vfxManager?.StopAllPulse();
            _vfxManager?.HideSlamDisc(0);
            _activePart?.SetSlamVuln(false, 1f);
            ReattachImmediate();
            base.Interrupt();
        }

        private IEnumerator ExecuteSingleSlam(BossWardenPart part)
        {
            if (part == null || part.IsSealed) yield break;

            _activePart = part;
            _activeArm = part.transform;
            if (_activeArm == null) yield break;

            CacheOrigin();

            if (_retargetEachArmOnStart || _targetPos == Vector2.zero)
                _targetPos = GetPlayerPosition();

            // 각 팔마다 새로 잠근 공격 예상 범위와 실제 공격 위치는 동일해야 한다.
            _vfxManager?.ShowSlamDisc(_targetPos, _warningRadius, 0);
            _vfxManager?.StartSlamPulse(0);

            Vector2 dirFromTarget = ((Vector2)_activeArm.position - _targetPos).normalized;
            if (dirFromTarget.sqrMagnitude < 0.01f)
                dirFromTarget = -GetFacingDirection();

            Vector3 windupWorld = _activeArm.position + (Vector3)(dirFromTarget * _backSwingDistance);
            _activeArm.DOKill();
            _activeArm.DOMove(windupWorld, _perArmWindupDuration).SetEase(Ease.OutBack).SetUpdate(true);
            RotateArmDownTo(_activeArm, (_targetPos - (Vector2)_activeArm.position).normalized, _perArmWindupDuration, Ease.OutBack);

            yield return StartCoroutine(WaitForPattern(_perArmWindupDuration));
            if (_isInterrupted) yield break;

            _vfxManager?.StopAllPulse();

            _originParent = _activeArm.parent;
            _activeArm.SetParent(null, worldPositionStays: true);
            _isDetached = true;

            float duration = _isPhase2 ? _throwDuration * 0.8f : _throwDuration;

            _activeArm.DOKill();

            Vector2 flyDir = (_targetPos - (Vector2)_activeArm.position).normalized;
            RotateArmDownTo(_activeArm, flyDir, duration * 0.5f, Ease.OutQuart);

            _activeArm.DOMove(new Vector3(_targetPos.x, _targetPos.y, _activeArm.position.z), duration)
                .SetEase(Ease.InCubic)
                .SetUpdate(true);

            yield return StartCoroutine(WaitForPattern(duration));
            if (_isInterrupted) yield break;

            Collider2D hit = Physics2D.OverlapCircle(_targetPos, _hitRadius, _playerLayer);
            if (hit != null)
                Debug.Log($"[BossPattern_Slam] 플레이어 피격 | target:{_targetPos} radius:{_hitRadius}");

            _vfxManager?.FlashAndHideSlamDisc(0);

            _activePart?.SetSlamVuln(true, _slamVulnMultiplier);

            // 타격 후 바로 복귀하지 않고 짧게 남겨 플레이어가 팔을 칠 수 있게 한다.
            yield return StartCoroutine(WaitForPattern(_slamVulnWindow));
            if (_isInterrupted) yield break;

            // 복귀 중에도 취약 상태 유지.
            ReattachTween(_returnDuration);
            yield return StartCoroutine(WaitForPattern(_returnDuration));
            if (_isInterrupted) yield break;

            if (_postReturnVulnWindow > 0f)
                yield return StartCoroutine(WaitForPattern(_postReturnVulnWindow));

            _activePart?.SetSlamVuln(false, 1f);
        }

        private void ResolveReferences()
        {
            Transform root = transform.root;
            if (_vfxManager == null) _vfxManager = root.GetComponentInChildren<BossVFXManager>(true);
            if (_attackManager == null) _attackManager = root.GetComponentInChildren<BossAttackManager>(true);
            if (_partManager == null) _partManager = root.GetComponentInChildren<BossPartManager>(true);
        }

        private void BuildArmQueue()
        {
            _armQueue.Clear();
            _availableParts.Clear();

            if (_usePartManagerArmCandidates && _partManager != null)
            {
                foreach (var part in _partManager.GetAllArmParts())
                    AddAvailablePart(_availableParts, part);
            }
            else if (_slamArmCandidates != null)
            {
                foreach (var part in _slamArmCandidates)
                    AddAvailablePart(_availableParts, part);
            }

            if (_availableParts.Count == 0 && _partManager != null)
            {
                foreach (var part in _partManager.GetAllArmParts())
                    AddAvailablePart(_availableParts, part);
            }

            if (_randomizeArmOrder)
                Shuffle(_availableParts);

            int useCount = _maxArmsPerPattern <= 0
                ? _availableParts.Count
                : Mathf.Min(_maxArmsPerPattern, _availableParts.Count);

            for (int i = 0; i < useCount; i++)
                _armQueue.Enqueue(_availableParts[i]);
        }

        private List<BossWardenPart> BuildAvailablePartSequence(bool previewOnly)
        {
            BuildArmQueue();
            return new List<BossWardenPart>(_armQueue);
        }

        private void AddAvailablePart(List<BossWardenPart> list, BossWardenPart part)
        {
            if (part == null) return;
            if (part.IsSealed) return;
            if (list.Contains(part)) return;
            list.Add(part);
        }

        private void Shuffle(List<BossWardenPart> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                int j = Random.Range(i, list.Count);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        private void CacheOrigin()
        {
            if (_activeArm == null) return;

            _originParent = _activeArm.parent;
            _originLocalPosition = _activeArm.localPosition;
            _originLocalRotation = _activeArm.localRotation;
            _originLocalScale = _activeArm.localScale;
        }

        private Vector2 GetPlayerPosition()
        {
            if (_attackManager != null && _attackManager.PlayerTransform != null)
                return _attackManager.PlayerTransform.position;

            return transform.position;
        }

        private Vector2 GetFacingDirection()
        {
            if (_attackManager != null && _attackManager.FacingDir.sqrMagnitude > 0.01f)
                return _attackManager.FacingDir;

            return Vector2.right;
        }

        private void RotateArmDownTo(Transform arm, Vector2 direction, float duration, Ease ease)
        {
            if (!_rotateDownAxisToTarget) return;
            if (arm == null) return;
            if (direction.sqrMagnitude < 0.0001f) return;

            float z = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg + 90f;
            arm.DORotate(new Vector3(0f, 0f, z), Mathf.Max(0.01f, duration))
                .SetEase(ease)
                .SetUpdate(true);
        }

        private void ReattachTween(float duration)
        {
            if (_activeArm == null) return;

            if (_isDetached && _originParent != null)
                _activeArm.SetParent(_originParent, worldPositionStays: true);

            _activeArm.DOKill();
            _activeArm.DOLocalMove(_originLocalPosition, duration).SetEase(Ease.OutBack).SetUpdate(true);
            _activeArm.DOLocalRotateQuaternion(_originLocalRotation, duration).SetEase(Ease.OutBack).SetUpdate(true);
            _activeArm.localScale = _originLocalScale;

            _isDetached = false;
        }

        private void ReattachImmediate()
        {
            if (_activeArm == null) return;

            _activeArm.DOKill();

            if (_isDetached && _originParent != null)
                _activeArm.SetParent(_originParent, worldPositionStays: true);

            _activeArm.localPosition = _originLocalPosition;
            _activeArm.localRotation = _originLocalRotation;
            _activeArm.localScale = _originLocalScale;
            _activePart?.SetSlamVuln(false, 1f);

            _isDetached = false;
        }
    }
}
