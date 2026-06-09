// ============================================================
// BossPattern_Swing.cs v2.1
// Boss_Warden 휘두르기 패턴 — Arm FIFO Queue + Per-Arm Target Lock
//
// [수정 내용]
//   - Step27 패턴 인스턴스 Queue 방식 폐기.
//   - Swing 패턴 인스턴스는 1개만 유지한다.
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
    public class BossPattern_Swing : BossPatternBase
    {
        public enum SwingSide
        {
            Any,
            Left,
            Right,
            All,
        }

        [Header("── Manager 연결 ──────────────────────")]
        [SerializeField] private BossVFXManager _vfxManager;
        [SerializeField] private BossAttackManager _attackManager;
        [SerializeField] private BossPartManager _partManager;
        [SerializeField] private BossWardenDataSO _data;

        [Header("── 팔 그룹 ──────────────────────")]
        [Tooltip("true면 Inspector 팔 배열보다 BossPartManager의 동적 팔 목록을 우선 사용합니다.")]
        [SerializeField] private bool _usePartManagerArmGroups = true;

        [Tooltip("왼팔 그룹. _usePartManagerArmGroups=false일 때 직접 지정용.")]
        [SerializeField] private Transform[] _leftArmTransforms;

        [Tooltip("오른팔 그룹. _usePartManagerArmGroups=false일 때 직접 지정용.")]
        [SerializeField] private Transform[] _rightArmTransforms;

        [Tooltip("이 패턴이 사용할 팔 그룹.")]
        [SerializeField] private SwingSide _swingSide = SwingSide.Any;

        [Header("── Arm FIFO Queue ──────────────────────")]
        [Tooltip("true면 이 Swing 패턴 1개가 선택된 팔들을 FIFO Queue로 순서대로 실행합니다.")]
        [SerializeField] private bool _useArmQueue = true;

        [Tooltip("패턴 1회에서 사용하려고 시도할 팔 수. 0이면 선택 그룹의 모든 사용 가능 팔을 사용합니다. 봉인된 팔은 자동 제외됩니다.")]
        [Min(0)][SerializeField] private int _maxArmsPerPattern = 0;

        [Tooltip("Queue 구성 시 팔 순서를 랜덤으로 섞습니다.")]
        [SerializeField] private bool _randomizeArmOrder = false;

        [Tooltip("FIFO Queue에서 각 팔 Swing 사이의 간격.")]
        [Min(0f)][SerializeField] private float _queueInterval = 0.08f;

        [Tooltip("각 팔의 짧은 개별 준비 회전 시간.")]
        [Min(0.01f)][SerializeField] private float _perArmWindupDuration = 0.12f;

        [Tooltip("true면 FIFO의 각 팔이 시작될 때마다 플레이어 위치를 새로 읽고, 그 위치를 예고/실제 공격 위치로 고정합니다.")]
        [SerializeField] private bool _retargetEachArmOnStart = true;

        [Header("── 판정 ──────────────────────")]
        [SerializeField] private LayerMask _playerLayer;
        [Min(0f)][SerializeField] private float _hitRadius = 5.0f;
        [Min(0f)][SerializeField] private float _warningRadius = 5.5f;

        [Header("── 연출 수치 ──────────────────────")]
        [Min(0f)][SerializeField] private float _windupAngle = 35f;
        [Min(0f)][SerializeField] private float _swingAngle = 135f;
        [Min(0.05f)][SerializeField] private float _swingDuration = 0.18f;
        [Min(0.05f)][SerializeField] private float _returnDuration = 0.35f;
        [Min(0f)][SerializeField] private float _hitDelay = 0.08f;
        [SerializeField] private bool _randomizeSideWhenAny = true;

        [Header("── 공격 후 공략 시간 ──────────────────────")]
        [Tooltip("타격 후 복귀 전 팔이 취약하게 남아있는 시간.")]
        [Min(0f)][SerializeField] private float _swingVulnWindow = 0.45f;

        [Tooltip("복귀 완료 후 추가로 팔을 공격할 수 있는 여유 시간.")]
        [Min(0f)][SerializeField] private float _postReturnVulnWindow = 0.2f;

        [Min(1f)][SerializeField] private float _swingVulnMultiplier = 1.5f;

        [Header("── 팔 방향 보정 ──────────────────────")]
        [Tooltip("true면 각 팔의 Vector2.down 방향이 플레이어를 바라본 상태에서 Swing 됩니다.")]
        [SerializeField] private bool _rotateDownAxisToPlayer = true;

        private readonly List<Transform> _activeArms = new();
        private readonly List<Vector3> _originLocalPositions = new();
        private readonly List<Quaternion> _originLocalRotations = new();
        private readonly List<Vector3> _originLocalScales = new();

        private Transform _moveRoot;
        private Vector2 _swingCenter;
        private Vector2 _targetPos;
        private int _swingDirection = 1;
        private bool _isPhase2;

        public override bool IsAvailable
        {
            get
            {
                BuildActiveArmsPreview();
                return _activeArms.Count > 0;
            }
        }

        private void Awake()
        {
            ResolveReferences();
            _triggerGroggyOnRecovery = false;
        }

        public override void Initialize(BossWardenDataSO data)
        {
            base.Initialize(data);
            ResolveReferences();

            _data = data != null ? data : _data;

            if (_data != null)
            {
                // Swing 전용 DataSO 정리 전까지 기존 Sweep 계열 수치를 임시 호환 사용.
                _hitRadius = _data.sweepHitRadius;
                _warningRadius = _data.sweepWarningRadius;
                _swingDuration = Mathf.Max(0.05f, 180f / Mathf.Max(1f, _data.GetSweepRotateSpeed(_isPhase2 ? 2 : 1)));
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
            BuildActiveArms();
            if (_activeArms.Count == 0) yield break;

            CacheOrigins();

            // 전체 패턴 진입 경고 시간. 실제 Swing 예고 범위는 각 팔 실행 직전에 새로 표시한다.
            _targetPos = GetPlayerPosition();
            _swingCenter = _targetPos;
            _vfxManager?.HideSwingDisc();

            yield return StartCoroutine(WaitForPattern(_warningDuration));
        }

        protected override IEnumerator OnActive()
        {
            if (_isInterrupted) yield break;
            if (_activeArms.Count == 0) yield break;

            _vfxManager?.StopAllPulse();

            if (!_useArmQueue)
            {
                yield return StartCoroutine(ExecuteSimultaneousSwing());
                yield break;
            }

            for (int i = 0; i < _activeArms.Count; i++)
            {
                if (_isInterrupted) yield break;

                Transform arm = _activeArms[i];
                if (!IsArmAvailable(arm)) continue;

                yield return StartCoroutine(ExecuteSingleSwing(arm, i));

                if (_isInterrupted) yield break;
                if (_queueInterval > 0f && i < _activeArms.Count - 1)
                    yield return StartCoroutine(WaitForPattern(_queueInterval));
            }
        }

        protected override IEnumerator OnRecovery()
        {
            RestoreArmsTween(_returnDuration);
            yield return StartCoroutine(WaitForPattern(_recoveryDuration));
        }

        public override void Interrupt()
        {
            _vfxManager?.StopAllPulse();
            _vfxManager?.HideSwingDisc();
            SetActiveArmsVuln(false);
            RestoreArmsImmediate();
            base.Interrupt();
        }

        private IEnumerator ExecuteSingleSwing(Transform arm, int index)
        {
            if (arm == null) yield break;

            if (_retargetEachArmOnStart)
            {
                _targetPos = GetPlayerPosition();
                _swingCenter = _targetPos;
            }

            // 각 팔마다 새로 잠근 예고 범위/타겟 기준으로만 공격한다.
            _vfxManager?.ShowSwingDisc(_swingCenter, _warningRadius);
            _vfxManager?.StartSwingPulse();

            Vector2 facing = GetLockedFacingDirection(arm);
            _swingDirection = DetermineSwingDirection(facing);

            Vector2 toTarget = (_targetPos - (Vector2)arm.position).normalized;
            if (toTarget.sqrMagnitude < 0.0001f)
                toTarget = facing;

            float baseAngle = AngleForDownToDirection(toTarget);
            float windupZ = baseAngle - (_windupAngle * _swingDirection);
            float targetZ = baseAngle + (_swingAngle * _swingDirection);
            float duration = _isPhase2 ? _swingDuration * 0.8f : _swingDuration;

            arm.DOKill();
            RotateArmWorldZ(arm, windupZ, _perArmWindupDuration, Ease.OutBack);
            yield return StartCoroutine(WaitForPattern(_perArmWindupDuration));
            if (_isInterrupted) yield break;

            _vfxManager?.StopAllPulse();

            arm.DOKill();
            RotateArmWorldZ(arm, targetZ, duration, Ease.OutCubic);

            yield return StartCoroutine(WaitForPattern(_hitDelay));
            if (_isInterrupted) yield break;

            Collider2D hit = Physics2D.OverlapCircle(_swingCenter, _hitRadius, _playerLayer);
            if (hit != null)
                Debug.Log($"[BossPattern_Swing] 플레이어 피격 | lockedCenter:{_swingCenter} radius:{_hitRadius}");

            yield return StartCoroutine(WaitForPattern(Mathf.Max(0f, duration - _hitDelay)));
            if (_isInterrupted) yield break;

            var part = GetPartFromArm(arm);
            part?.SetSlamVuln(true, _swingVulnMultiplier);

            _vfxManager?.FlashAndHideSwingDisc();

            if (_swingVulnWindow > 0f)
                yield return StartCoroutine(WaitForPattern(_swingVulnWindow));
            if (_isInterrupted) yield break;

            RestoreArmTween(index, _returnDuration);
            yield return StartCoroutine(WaitForPattern(_returnDuration));
            if (_isInterrupted) yield break;

            if (_postReturnVulnWindow > 0f)
                yield return StartCoroutine(WaitForPattern(_postReturnVulnWindow));

            part?.SetSlamVuln(false, 1f);
        }

        private IEnumerator ExecuteSimultaneousSwing()
        {
            if (_retargetEachArmOnStart)
            {
                _targetPos = GetPlayerPosition();
                _swingCenter = _targetPos;
                _vfxManager?.ShowSwingDisc(_swingCenter, _warningRadius);
                _vfxManager?.StartSwingPulse();
            }

            Vector2 facing = GetFacingDirection();
            _swingDirection = DetermineSwingDirection(facing);
            float duration = _isPhase2 ? _swingDuration * 0.8f : _swingDuration;

            for (int i = 0; i < _activeArms.Count; i++)
            {
                Transform arm = _activeArms[i];
                if (!IsArmAvailable(arm)) continue;

                Vector2 toTarget = (_targetPos - (Vector2)arm.position).normalized;
                if (toTarget.sqrMagnitude < 0.0001f)
                    toTarget = facing;

                float baseAngle = AngleForDownToDirection(toTarget);
                float targetZ = baseAngle + (_swingAngle * _swingDirection);

                arm.DOKill();
                RotateArmWorldZ(arm, targetZ, duration, Ease.OutCubic);
            }

            yield return StartCoroutine(WaitForPattern(_hitDelay));
            if (_isInterrupted) yield break;

            Collider2D hit = Physics2D.OverlapCircle(_swingCenter, _hitRadius, _playerLayer);
            if (hit != null)
                Debug.Log($"[BossPattern_Swing] 플레이어 피격 | lockedCenter:{_swingCenter} radius:{_hitRadius}");

            yield return StartCoroutine(WaitForPattern(Mathf.Max(0f, duration - _hitDelay)));
            if (_isInterrupted) yield break;

            SetActiveArmsVuln(true);
            _vfxManager?.FlashAndHideSwingDisc();
            if (_swingVulnWindow > 0f)
                yield return StartCoroutine(WaitForPattern(_swingVulnWindow));
            RestoreArmsTween(_returnDuration);
            yield return StartCoroutine(WaitForPattern(_returnDuration));
            if (_postReturnVulnWindow > 0f)
                yield return StartCoroutine(WaitForPattern(_postReturnVulnWindow));
            SetActiveArmsVuln(false);
        }

        private void ResolveReferences()
        {
            Transform root = transform.root;
            if (_vfxManager == null) _vfxManager = root.GetComponentInChildren<BossVFXManager>(true);
            if (_attackManager == null) _attackManager = root.GetComponentInChildren<BossAttackManager>(true);
            if (_partManager == null) _partManager = root.GetComponentInChildren<BossPartManager>(true);

            if (_moveRoot == null)
            {
                var rb = root.GetComponentInChildren<Rigidbody2D>(true);
                _moveRoot = rb != null ? rb.transform : root;
            }

            if (_usePartManagerArmGroups && _partManager != null)
            {
                _leftArmTransforms = BuildTransformArray(_partManager.GetLeftArmParts());
                _rightArmTransforms = BuildTransformArray(_partManager.GetRightArmParts());
            }
            else
            {
                if ((_leftArmTransforms == null || _leftArmTransforms.Length == 0) && _partManager != null)
                    _leftArmTransforms = BuildTransformArray(_partManager.GetLeftArmParts());

                if ((_rightArmTransforms == null || _rightArmTransforms.Length == 0) && _partManager != null)
                    _rightArmTransforms = BuildTransformArray(_partManager.GetRightArmParts());
            }
        }

        private Transform[] BuildTransformArray(IReadOnlyList<BossWardenPart> parts)
        {
            if (parts == null) return null;

            var list = new List<Transform>();
            foreach (var part in parts)
            {
                if (part == null) continue;
                list.Add(part.transform);
            }

            return list.ToArray();
        }

        private void BuildActiveArmsPreview()
        {
            ResolveReferences();
            BuildActiveArms();
        }

        private void BuildActiveArms()
        {
            _activeArms.Clear();
            ResolveReferences();

            SwingSide side = _swingSide;

            if (side == SwingSide.Any && _randomizeSideWhenAny)
            {
                int leftCount = CountAvailableArms(_leftArmTransforms);
                int rightCount = CountAvailableArms(_rightArmTransforms);

                if (leftCount > 0 && rightCount > 0)
                    side = Random.value < 0.5f ? SwingSide.Left : SwingSide.Right;
                else if (leftCount > 0)
                    side = SwingSide.Left;
                else if (rightCount > 0)
                    side = SwingSide.Right;
                else
                    side = SwingSide.All;
            }

            if (side == SwingSide.Left || side == SwingSide.All || side == SwingSide.Any)
                AddAvailableArms(_leftArmTransforms);

            if (side == SwingSide.Right || side == SwingSide.All || side == SwingSide.Any)
                AddAvailableArms(_rightArmTransforms);

            if (_randomizeArmOrder)
                Shuffle(_activeArms);

            int useCount = _maxArmsPerPattern <= 0
                ? _activeArms.Count
                : Mathf.Min(_maxArmsPerPattern, _activeArms.Count);

            if (_activeArms.Count > useCount)
                _activeArms.RemoveRange(useCount, _activeArms.Count - useCount);
        }

        private int CountAvailableArms(Transform[] arms)
        {
            if (arms == null) return 0;

            int count = 0;
            foreach (var arm in arms)
            {
                if (IsArmAvailable(arm))
                    count++;
            }
            return count;
        }

        private void AddAvailableArms(Transform[] arms)
        {
            if (arms == null) return;

            foreach (var arm in arms)
            {
                if (!IsArmAvailable(arm)) continue;
                if (!_activeArms.Contains(arm))
                    _activeArms.Add(arm);
            }
        }

        private bool IsArmAvailable(Transform arm)
        {
            if (arm == null) return false;
            var part = arm.GetComponent<BossWardenPart>();
            if (part != null && part.IsSealed) return false;
            return true;
        }

        private void Shuffle(List<Transform> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                int j = Random.Range(i, list.Count);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        private void CacheOrigins()
        {
            _originLocalPositions.Clear();
            _originLocalRotations.Clear();
            _originLocalScales.Clear();

            foreach (var arm in _activeArms)
            {
                if (arm == null)
                {
                    _originLocalPositions.Add(Vector3.zero);
                    _originLocalRotations.Add(Quaternion.identity);
                    _originLocalScales.Add(Vector3.one);
                    continue;
                }

                _originLocalPositions.Add(arm.localPosition);
                _originLocalRotations.Add(arm.localRotation);
                _originLocalScales.Add(arm.localScale);
            }
        }

        private void RestoreArmsTween(float duration)
        {
            for (int i = 0; i < _activeArms.Count; i++)
                RestoreArmTween(i, duration);
        }

        private void RestoreArmTween(int index, float duration)
        {
            if (index < 0 || index >= _activeArms.Count) return;
            var arm = _activeArms[index];
            if (arm == null) return;

            arm.DOKill();
            if (index < _originLocalPositions.Count)
                arm.DOLocalMove(_originLocalPositions[index], duration).SetEase(Ease.OutBack).SetUpdate(true);
            if (index < _originLocalRotations.Count)
                arm.DOLocalRotateQuaternion(_originLocalRotations[index], duration).SetEase(Ease.OutBack).SetUpdate(true);
            if (index < _originLocalScales.Count)
                arm.localScale = _originLocalScales[index];
        }

        private void RestoreArmsImmediate()
        {
            for (int i = 0; i < _activeArms.Count; i++)
            {
                var arm = _activeArms[i];
                if (arm == null) continue;

                arm.DOKill();
                if (i < _originLocalPositions.Count) arm.localPosition = _originLocalPositions[i];
                if (i < _originLocalRotations.Count) arm.localRotation = _originLocalRotations[i];
                if (i < _originLocalScales.Count) arm.localScale = _originLocalScales[i];
            }
        }

        private float AngleForDownToDirection(Vector2 direction)
        {
            if (direction.sqrMagnitude < 0.0001f)
                direction = Vector2.right;

            return Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg + 90f;
        }

        private void RotateArmWorldZ(Transform arm, float worldZ, float duration, Ease ease)
        {
            if (arm == null) return;

            if (!_rotateDownAxisToPlayer)
            {
                arm.DOLocalRotate(new Vector3(0f, 0f, worldZ), Mathf.Max(0.01f, duration))
                    .SetEase(ease)
                    .SetUpdate(true);
                return;
            }

            arm.DORotate(new Vector3(0f, 0f, worldZ), Mathf.Max(0.01f, duration))
                .SetEase(ease)
                .SetUpdate(true);
        }

        private BossWardenPart GetPartFromArm(Transform arm)
        {
            return arm != null ? arm.GetComponent<BossWardenPart>() : null;
        }

        private void SetActiveArmsVuln(bool isVuln)
        {
            foreach (var arm in _activeArms)
            {
                var part = GetPartFromArm(arm);
                part?.SetSlamVuln(isVuln, isVuln ? _swingVulnMultiplier : 1f);
            }
        }

        private Vector2 GetLockedFacingDirection(Transform arm)
        {
            if (arm == null) return GetFacingDirection();

            Vector2 dir = (_targetPos - (Vector2)arm.position).normalized;
            if (dir.sqrMagnitude < 0.0001f)
                dir = GetFacingDirection();
            return dir;
        }

        private Vector2 GetPlayerPosition()
        {
            if (_attackManager != null && _attackManager.PlayerTransform != null)
                return _attackManager.PlayerTransform.position;

            return GetBossPosition() + GetFacingDirection();
        }

        private Vector2 GetBossPosition()
        {
            return _moveRoot != null ? (Vector2)_moveRoot.position : (Vector2)transform.position;
        }

        private Vector2 GetFacingDirection()
        {
            if (_attackManager != null && _attackManager.FacingDir.sqrMagnitude > 0.01f)
                return _attackManager.FacingDir;

            if (_attackManager != null && _attackManager.PlayerTransform != null)
                return ((Vector2)_attackManager.PlayerTransform.position - GetBossPosition()).normalized;

            return Vector2.right;
        }

        private int DetermineSwingDirection(Vector2 facing)
        {
            if (Mathf.Abs(facing.x) >= Mathf.Abs(facing.y))
                return facing.x >= 0f ? 1 : -1;

            return facing.y >= 0f ? 1 : -1;
        }
    }
}
