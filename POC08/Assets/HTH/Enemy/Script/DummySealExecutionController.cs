// ============================================================
// DummySealExecutionController.cs
// DummyEnemy 자동 일섬 봉인 집행 테스트 컨트롤러
//
// [역할]
//   - PlayerInputHandler.OnSeal 입력 1회를 받는다.
//   - 첫 Ready DummyEnemy 만 플레이어가 직접 지정한다.
//   - 이후 연결 고리는 시스템이 자동 계산한다.
//   - 플레이어가 대상에게 이동 → 도착 후 봉인 집행 → 다음 Ready 대상 자동 탐색.
//   - 중간에 대상의 Ready 상태가 풀리면 현재 위치/마지막 집행 위치 기준으로 즉시 재계산한다.
//   - 연쇄 중 플레이어 입력/이동/공격은 잠그고, 종료 시 반드시 복구한다.
//
// [부착 위치]
//   Player 오브젝트에 부착 권장.
//
// [namespace] SEAL
// ============================================================

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SEAL
{
    [RequireComponent(typeof(Rigidbody2D))]
    public class DummySealExecutionController : MonoBehaviour
    {
        [Header("── 연결 ─────────────────────")]
        [SerializeField] private PlayerController _playerController;
        [SerializeField] private PlayerInputHandler _input;
        [SerializeField] private Rigidbody2D _rigid2D;

        [Header("── 연결 고리 표시 Optional ─────────────────────")]
        [Tooltip("자동 일섬 연결 고리를 표시할 LineRenderer. 미연결 시 기능만 동작한다.")]
        [SerializeField] private LineRenderer _chainPreviewLine;

        [Tooltip("일섬 중 연결 고리 미리보기를 계속 갱신한다.")]
        [SerializeField] private bool _showChainPreview = true;

        [Header("── Fallback 수치 ─────────────────────")]
        [Tooltip("대상 DataSO가 없을 때 사용할 집행 시작 거리.")]
        [Min(0.1f)]
        [SerializeField] private float _fallbackExecuteRange = 4.5f;

        [Tooltip("대상 DataSO가 없을 때 사용할 일섬 이동 속도.")]
        [Min(0.1f)]
        [SerializeField] private float _fallbackIssenMoveSpeed = 28.0f;

        [Tooltip("대상 DataSO가 없을 때 사용할 도착 판정 거리.")]
        [Min(0.01f)]
        [SerializeField] private float _fallbackArriveDistance = 0.08f;

        [Tooltip("대상 DataSO가 없을 때 사용할 대상 중심 정지 오프셋.")]
        [Min(0f)]
        [SerializeField] private float _fallbackStopOffset = 0.15f;

        [Tooltip("대상 DataSO가 없을 때 사용할 대상별 최대 이동 시간.")]
        [Min(0.05f)]
        [SerializeField] private float _fallbackMaxTravelTime = 0.8f;

        [Tooltip("대상 DataSO가 없을 때 사용할 연쇄 반경.")]
        [Min(0.1f)]
        [SerializeField] private float _fallbackChainRadius = 8.5f;

        [Tooltip("대상 DataSO가 없을 때 사용할 연쇄 간격.")]
        [Min(0f)]
        [SerializeField] private float _fallbackChainInterval = 0.04f;

        [Tooltip("대상 DataSO가 없을 때 사용할 최대 연쇄 수. 0 이하면 무제한.")]
        [SerializeField] private int _fallbackMaxChainCount = 12;

        [Tooltip("대상 DataSO가 없을 때 사용할 연결 고리 재계산 주기.")]
        [Min(0.01f)]
        [SerializeField] private float _fallbackRecalculateInterval = 0.03f;

        [Header("── 옵션 ─────────────────────")]
        [SerializeField] private bool _lockPlayerDuringExecution = true;
        [SerializeField] private bool _blockInputDuringExecution = true;
        [SerializeField] private bool _snapToTargetOnTimeout = false;

        [Tooltip("일섬/봉인 집행 중 모든 DummyEnemy AttackHitBox를 비활성화한다. HurtBox는 유지된다.")]
        [SerializeField] private bool _disableEnemyAttackHitBoxesDuringExecution = true;

        private bool _isExecuting;
        private bool _lockedByThisController;
        private bool _inputBlockedByThisController;
        private bool _enemyAttackHitBoxesSuppressedByThisController;
        private Coroutine _executionRoutine;

        private readonly List<DummyEnemy> _chainPreview = new List<DummyEnemy>();
        private float _nextPreviewRefreshTime;

        /// <summary>
        /// UI/VFX가 자동 연결 고리 목록을 표시하고 싶을 때 구독할 수 있다.
        /// </summary>
        public event Action<IReadOnlyList<DummyEnemy>> OnChainPreviewChanged;

        private void Awake()
        {
            if (_playerController == null)
                _playerController = GetComponent<PlayerController>();

            if (_rigid2D == null)
                _rigid2D = GetComponent<Rigidbody2D>();
        }

        private void Start()
        {
            if (_input == null)
                _input = PlayerInputHandler.Instance;

            if (_input == null)
            {
                Debug.LogError("[DummySealExecutionController] PlayerInputHandler.Instance 없음.");
                enabled = false;
                return;
            }

            _input.OnSeal -= HandleSealPressed;
            _input.OnSeal += HandleSealPressed;
        }

        private void OnDisable()
        {
            CleanupExecutionLock();
        }

        private void OnDestroy()
        {
            if (_input != null)
                _input.OnSeal -= HandleSealPressed;

            CleanupExecutionLock();
        }

        private void HandleSealPressed()
        {
            if (_isExecuting) return;

            Vector2 playerPos = GetPlayerPosition();

            // 첫 대상만 플레이어가 직접 지정한다.
            // 이후 대상은 자동 체인이 계속 계산한다.
            DummyEnemy firstTarget = DummyEnemyRegistry.FindNearestReadyInOwnExecuteRange(
                playerPos,
                _fallbackExecuteRange);

            if (firstTarget == null)
                return;

            _executionRoutine = StartCoroutine(AutoIssenChainRoutine(firstTarget));
        }

        private IEnumerator AutoIssenChainRoutine(DummyEnemy firstTarget)
        {
            _isExecuting = true;
            LockPlayerForExecution();

            HashSet<DummyEnemy> executed = new HashSet<DummyEnemy>();
            DummyEnemy current = firstTarget;
            DummyEnemy lastSealed = null;
            int count = 0;

            while (true)
            {
                if (current == null || !current.IsReadyToExecute)
                {
                    current = FindReplacementTarget(lastSealed, executed, current);
                    if (current == null)
                        break;
                }

                RefreshChainPreview(current, executed, force: true);

                bool arrived = false;
                DummyEnemy arrivedTarget = null;

                yield return MovePlayerToTargetAuto(
                    current,
                    lastSealed,
                    executed,
                    (target, result) =>
                    {
                        arrivedTarget = target;
                        arrived = result;
                    });

                if (!arrived || arrivedTarget == null)
                    break;

                if (!arrivedTarget.IsReadyToExecute)
                {
                    current = FindReplacementTarget(lastSealed, executed, arrivedTarget);
                    continue;
                }

                arrivedTarget.ExecuteSeal();
                executed.Add(arrivedTarget);
                lastSealed = arrivedTarget;
                count++;

                int maxCount = GetMaxChainCount(arrivedTarget);
                if (maxCount > 0 && count >= maxCount)
                    break;

                float interval = GetChainInterval(arrivedTarget);
                if (interval > 0f)
                    yield return new WaitForSeconds(interval);

                current = DummyEnemyRegistry.FindNearestReady(
                    arrivedTarget.ExecutionPosition,
                    GetChainRadius(arrivedTarget),
                    executed);
            }

            ClearChainPreview();
            FinishExecution();
        }

        /// <summary>
        /// 대상에게 이동한다.
        /// 이동 도중 대상 Ready 상태가 풀리면 즉시 다음 대상 재탐색으로 갈아탄다.
        /// </summary>
        private IEnumerator MovePlayerToTargetAuto(
            DummyEnemy startTarget,
            DummyEnemy lastSealed,
            HashSet<DummyEnemy> executed,
            Action<DummyEnemy, bool> onComplete)
        {
            DummyEnemy target = startTarget;
            float elapsed = 0f;

            while (true)
            {
                if (target == null || !target.IsReadyToExecute)
                {
                    target = FindReplacementTarget(lastSealed, executed, target);
                    elapsed = 0f;

                    if (target == null)
                    {
                        onComplete?.Invoke(null, false);
                        yield break;
                    }
                }

                RefreshChainPreview(target, executed, force: false);

                float speed = GetIssenMoveSpeed(target);
                float arriveDistance = GetArriveDistance(target);
                float maxTravelTime = GetMaxTravelTime(target);

                Vector2 currentPos = GetPlayerPosition();
                Vector2 targetPos = GetExecutionMovePosition(target, currentPos);
                Vector2 toTarget = targetPos - currentPos;

                if (toTarget.magnitude <= arriveDistance)
                {
                    SetPlayerPosition(targetPos);
                    onComplete?.Invoke(target, true);
                    yield break;
                }

                Vector2 next = Vector2.MoveTowards(currentPos, targetPos, speed * Time.fixedDeltaTime);
                SetPlayerPosition(next);

                elapsed += Time.fixedDeltaTime;
                if (elapsed >= maxTravelTime)
                {
                    if (_snapToTargetOnTimeout)
                    {
                        SetPlayerPosition(targetPos);
                        onComplete?.Invoke(target, true);
                    }
                    else
                    {
                        // 도착 실패 시 같은 연결 범위에서 다른 Ready 대상이 있는지 한 번 더 재계산한다.
                        DummyEnemy replacement = FindReplacementTarget(lastSealed, executed, target);
                        if (replacement != null && replacement != target)
                        {
                            target = replacement;
                            elapsed = 0f;
                            continue;
                        }

                        onComplete?.Invoke(target, false);
                    }
                    yield break;
                }

                yield return new WaitForFixedUpdate();
            }
        }

        private DummyEnemy FindReplacementTarget(
            DummyEnemy lastSealed,
            HashSet<DummyEnemy> executed,
            DummyEnemy invalidTarget)
        {
            HashSet<DummyEnemy> ignore = new HashSet<DummyEnemy>(executed);
            if (invalidTarget != null)
                ignore.Add(invalidTarget);

            if (lastSealed != null)
            {
                DummyEnemy fromLast = DummyEnemyRegistry.FindNearestReady(
                    lastSealed.ExecutionPosition,
                    GetChainRadius(lastSealed),
                    ignore);

                if (fromLast != null)
                    return fromLast;
            }

            // 첫 대상이 이동 중 풀린 경우: 플레이어 현재 위치 기준으로 다시 첫 대상 조건 탐색.
            return DummyEnemyRegistry.FindNearestReadyInOwnExecuteRange(
                GetPlayerPosition(),
                _fallbackExecuteRange,
                ignore);
        }

        private void RefreshChainPreview(DummyEnemy start, HashSet<DummyEnemy> executed, bool force)
        {
            if (!_showChainPreview)
                return;

            float interval = GetRecalculateInterval(start);
            if (!force && Time.time < _nextPreviewRefreshTime)
                return;

            _nextPreviewRefreshTime = Time.time + interval;
            _chainPreview.Clear();

            if (start != null && start.IsReadyToExecute)
            {
                int previewMax = GetPreviewMaxChainCount(start);
                float radius = GetChainRadius(start);
                _chainPreview.AddRange(DummyEnemyRegistry.BuildReadyChainPreview(start, radius, previewMax, executed));
            }

            UpdatePreviewLine();
            OnChainPreviewChanged?.Invoke(_chainPreview);
        }

        private void ClearChainPreview()
        {
            _chainPreview.Clear();
            UpdatePreviewLine();
            OnChainPreviewChanged?.Invoke(_chainPreview);
        }

        private void UpdatePreviewLine()
        {
            if (_chainPreviewLine == null)
                return;

            if (_chainPreview.Count <= 0)
            {
                _chainPreviewLine.positionCount = 0;
                return;
            }

            _chainPreviewLine.positionCount = _chainPreview.Count + 1;
            _chainPreviewLine.SetPosition(0, transform.position);

            for (int i = 0; i < _chainPreview.Count; i++)
                _chainPreviewLine.SetPosition(i + 1, _chainPreview[i].ExecutionPosition);
        }

        private Vector2 GetExecutionMovePosition(DummyEnemy target, Vector2 currentPlayerPos)
        {
            Vector2 targetPos = target.ExecutionPosition;
            float offset = GetStopOffset(target);

            if (offset <= 0f)
                return targetPos;

            Vector2 fromTargetToPlayer = currentPlayerPos - targetPos;
            if (fromTargetToPlayer.sqrMagnitude <= 0.0001f)
                fromTargetToPlayer = Vector2.left;

            return targetPos + fromTargetToPlayer.normalized * offset;
        }

        private Vector2 GetPlayerPosition()
        {
            if (_rigid2D != null)
                return _rigid2D.position;

            return transform.position;
        }

        private void SetPlayerPosition(Vector2 position)
        {
            if (_rigid2D != null)
            {
                _rigid2D.linearVelocity = Vector2.zero;
                _rigid2D.MovePosition(position);
            }
            else
            {
                transform.position = position;
            }
        }

        private void LockPlayerForExecution()
        {
            if (_blockInputDuringExecution && _input != null)
            {
                _input.BlockAll();
                _inputBlockedByThisController = true;
            }

            if (_lockPlayerDuringExecution && _playerController != null)
            {
                _playerController.EnterSeal();
                _lockedByThisController = true;
            }

            if (_rigid2D != null)
                _rigid2D.linearVelocity = Vector2.zero;

            SuppressEnemyAttackHitBoxes(true);
        }

        private void FinishExecution()
        {
            CleanupExecutionLock();
            Debug.Log("[DummySealExecutionController] 자동 일섬 봉인 집행 종료");
        }

        private void CleanupExecutionLock()
        {
            if (_executionRoutine != null)
            {
                StopCoroutine(_executionRoutine);
                _executionRoutine = null;
            }

            ClearChainPreview();

            if (_rigid2D != null)
                _rigid2D.linearVelocity = Vector2.zero;

            SuppressEnemyAttackHitBoxes(false);

            if (_lockedByThisController && _playerController != null)
                _playerController.ExitSeal();

            if (_inputBlockedByThisController && _input != null)
                _input.UnblockAll();

            _lockedByThisController = false;
            _inputBlockedByThisController = false;
            _isExecuting = false;
        }


        /// <summary>
        /// 일섬/봉인 집행 중 적 공격 판정만 끄고, 플레이어 공격 피격용 HurtBox는 유지한다.
        /// PlayerHit 구현은 추후 DummyEnemy AttackHitBox 쪽에서 추가하면 된다.
        /// </summary>
        private void SuppressEnemyAttackHitBoxes(bool suppress)
        {
            if (!_disableEnemyAttackHitBoxesDuringExecution)
                return;

            if (suppress)
            {
                if (_enemyAttackHitBoxesSuppressedByThisController)
                    return;

                _enemyAttackHitBoxesSuppressedByThisController = true;
            }
            else
            {
                if (!_enemyAttackHitBoxesSuppressedByThisController)
                    return;

                _enemyAttackHitBoxesSuppressedByThisController = false;
            }

            List<DummyEnemy> enemies = DummyEnemyRegistry.GetAllEnemiesSnapshot();
            for (int i = 0; i < enemies.Count; i++)
            {
                if (enemies[i] == null) continue;
                enemies[i].SetAttackHitBoxSuppressed(suppress);
            }
        }

        private float GetIssenMoveSpeed(DummyEnemy enemy)
            => enemy != null && enemy.Data != null ? enemy.Data.IssenMoveSpeed : _fallbackIssenMoveSpeed;

        private float GetArriveDistance(DummyEnemy enemy)
            => enemy != null && enemy.Data != null ? enemy.Data.IssenArriveDistance : _fallbackArriveDistance;

        private float GetStopOffset(DummyEnemy enemy)
            => enemy != null && enemy.Data != null ? enemy.Data.IssenStopOffset : _fallbackStopOffset;

        private float GetMaxTravelTime(DummyEnemy enemy)
            => enemy != null && enemy.Data != null ? enemy.Data.MaxTravelTimePerTarget : _fallbackMaxTravelTime;

        private float GetChainRadius(DummyEnemy enemy)
            => enemy != null && enemy.Data != null ? enemy.Data.ChainRadius : _fallbackChainRadius;

        private float GetChainInterval(DummyEnemy enemy)
            => enemy != null && enemy.Data != null ? enemy.Data.ChainInterval : _fallbackChainInterval;

        private int GetMaxChainCount(DummyEnemy enemy)
            => enemy != null && enemy.Data != null ? enemy.Data.MaxChainCount : _fallbackMaxChainCount;

        private float GetRecalculateInterval(DummyEnemy enemy)
            => enemy != null && enemy.Data != null ? enemy.Data.ChainRecalculateInterval : _fallbackRecalculateInterval;

        private int GetPreviewMaxChainCount(DummyEnemy enemy)
        {
            if (enemy != null && enemy.Data != null)
            {
                if (enemy.Data.PreviewMaxChainCount > 0)
                    return enemy.Data.PreviewMaxChainCount;

                return enemy.Data.MaxChainCount;
            }

            return _fallbackMaxChainCount;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.8f, 0.25f, 1f, 0.18f);
            Gizmos.DrawWireSphere(transform.position, _fallbackExecuteRange);
        }
#endif
    }
}
