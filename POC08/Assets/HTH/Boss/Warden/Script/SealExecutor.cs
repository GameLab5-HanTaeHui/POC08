// ============================================================
// SealExecutor.cs  v1.0
// 봉인 집행 관리자 — 적 캐릭터 1개당 1개 보유
//
// [역할]
//   자기 적의 SealableComponent 들에서 발행된
//   집행 승인 요청(OnSealRequested)을 수신하고,
//   플레이어의 S키 홀드 + 범위 접근 조건을 확인하여
//   봉인 집행을 처리한다.
//
// [구조]
//   Start():
//     GetComponentsInChildren<SealableComponent>() 로
//     자기 하위의 모든 SealableComponent 자동 수집 + OnSealRequested 구독
//
//   DetectSealInput() (코루틴):
//     상시 루프. S키 홀드 + 범위 내 대상 탐색.
//     DetermineTarget() → 등급(Grade) 읽어서 슬로우 분기.
//     ExecuteSeal(target) → SealableComponent.ExecuteSeal() 호출.
//
// [SealGrade 별 슬로우 정책]
//   Normal : 슬로우 없음 — 전투 템포 유지
//   Part   : 집행 완료 후 짧은 슬로우 (타격감)
//   Core   : 홀드 중 내내 강한 슬로우
//
// [기존 BossWardenSealExecutor 와의 차이]
//   기존: PartSeal/CoreUnlock/FinalSeal 하드코딩 → 대상마다 Executor 코드 수정 필요
//   신규: SealableComponent.Grade 에서 등급 읽음 → 하드코딩 없음
//         새 부위/코어 추가 시 SealableComponent 만 부착하면 자동 연동
//
// [Boss_Warden 과의 연결]
//   기존 BossWardenSealExecutor 이벤트 (OnPartSealed, OnCoreUnlocked, OnFinalSealCompleted)
//   → SealableComponent.OnSealCompleted 로 통합
//   BossWardenCore 가 SealableComponent.OnSealCompleted 를 직접 구독
//
// [namespace] SEAL
// ============================================================

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SEAL
{
    /// <summary>
    /// 봉인 집행 관리자. 적 캐릭터 1개당 1개 보유. (v1.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [집행 흐름]
    ///   SealableComponent.OnSealRequested 발행
    ///     → _sealReadyList 에 등록
    ///   플레이어 범위 내 접근 + S키 홀드
    ///     → DetermineTarget() 으로 가장 가까운 대상 탐색
    ///     → 등급별 슬로우 적용
    ///   홀드 완료
    ///     → SealableComponent.ExecuteSeal() 호출
    ///     → _sealReadyList 에서 제거
    /// ────────────────────────────────────────────────────
    /// </summary>
    public class SealExecutor : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── DataSO ──────────────────────")]

        /// <summary>
        /// BossWardenDataSO.
        /// 슬로우 배율 / 지속시간 참조.
        /// BossWardenCore.Initialize() 에서 주입 가능.
        /// </summary>
        [Tooltip("BossWardenDataSO. 슬로우 배율 참조.")]
        [SerializeField] private BossWardenDataSO _data;

        // ══════════════════════════════════════════════════════
        // 컴포넌트 참조
        // ══════════════════════════════════════════════════════

        private PlayerInputHandler _input;
        private Transform _playerTransform;
        private Rigidbody2D _playerRigid2D;

        // ══════════════════════════════════════════════════════
        // 내부 상태
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 집행 승인 요청 수신 대기 목록.
        /// SealableComponent.OnSealRequested 발행 시 추가.
        /// 집행 완료 또는 ForceRelease 시 제거.
        /// </summary>
        private readonly List<SealableComponent> _sealReadyList
            = new List<SealableComponent>();

        /// <summary> 현재 집행 실행 중 여부 (중복 방지). </summary>
        private bool _isExecuting;

        /// <summary>
        /// S키 재누름 확인 플래그.
        /// 집행 완료 후 S키를 한 번 뗀 것 확인 후 재집행 허용.
        /// </summary>
        private bool _mustReleaseKey;

        /// <summary> 집행 쿨다운 타이머. </summary>
        private float _cooldownTimer;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Start()
        {
            // 플레이어 탐색
            var players = FindObjectsByType<PlayerMoveController>(FindObjectsSortMode.None);
            if (players.Length > 0)
            {
                _playerTransform = players[0].transform;
                _playerRigid2D = players[0].GetComponent<Rigidbody2D>();
            }
            else
                Debug.LogWarning($"[SealExecutor] {gameObject.name} — PlayerMoveController 탐색 실패");

            _input = PlayerInputHandler.Instance;

            // 자기 하위의 모든 SealableComponent 자동 수집 + 구독
            var sealables = GetComponentsInChildren<SealableComponent>(includeInactive: true);
            foreach (var s in sealables)
                RegisterSealable(s);

            Debug.Log($"[SealExecutor] {gameObject.name} 초기화 완료 | 등록 부위:{sealables.Length}개");

            StartCoroutine(DetectSealInput());
        }

        private void OnDestroy()
        {
            // TimeScale 보호
            RestoreTimeScale();
        }

        private void Update()
        {
            if (_cooldownTimer > 0f)
                _cooldownTimer -= Time.unscaledDeltaTime;

            if (_mustReleaseKey && _input != null && !_input.IsSealHeld)
                _mustReleaseKey = false;
        }

        // ══════════════════════════════════════════════════════
        // SealableComponent 등록
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// SealableComponent 를 이 Executor 에 등록한다.
        /// Start() 에서 자동 수집하지만, 동적 생성 시 수동 호출도 가능.
        /// </summary>
        public void RegisterSealable(SealableComponent sealable)
        {
            if (sealable == null) return;

            sealable.OnSealRequested -= HandleSealRequested;
            sealable.OnSealRequested += HandleSealRequested;

            sealable.OnForceReleased -= () => HandleForceReleased(sealable);

            // ✅ 람다 중복 방지를 위해 Action 캐싱 방식 사용
            // OnForceReleased 는 파라미터 없는 이벤트이므로
            // 각 sealable 별 클로저로 처리
            void onReleased() => HandleForceReleased(sealable);
            sealable.OnForceReleased += onReleased;
        }

        // ══════════════════════════════════════════════════════
        // 이벤트 핸들러
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// SealableComponent.OnSealRequested 수신.
        /// 집행 대기 목록에 추가.
        /// </summary>
        private void HandleSealRequested(SealableComponent sealable)
        {
            if (!_sealReadyList.Contains(sealable))
            {
                _sealReadyList.Add(sealable);
                Debug.Log($"[SealExecutor] {gameObject.name} — {sealable.name} 집행 대기 등록 | 등급:{sealable.Grade}");
            }
        }

        /// <summary>
        /// SealableComponent.OnForceReleased 수신.
        /// 집행 대기 목록에서 제거.
        /// </summary>
        private void HandleForceReleased(SealableComponent sealable)
        {
            _sealReadyList.Remove(sealable);
        }

        // ══════════════════════════════════════════════════════
        // 집행 감지 루프
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// S키 봉인 집행 입력 감지 상시 루프.
        ///
        /// [탐색 우선순위]
        ///   Core > Part > Normal
        ///   같은 등급이면 플레이어와 가장 가까운 대상 선택
        /// </summary>
        private IEnumerator DetectSealInput()
        {
            while (true)
            {
                if (_isExecuting || _cooldownTimer > 0f || _mustReleaseKey)
                {
                    yield return null;
                    continue;
                }

                bool isSHeld = _input != null && _input.IsSealHeld;
                if (!isSHeld)
                {
                    yield return null;
                    continue;
                }

                SealableComponent target = DetermineTarget();
                if (target == null)
                {
                    yield return null;
                    continue;
                }

                yield return StartCoroutine(ExecuteSeal(target));
            }
        }

        // ══════════════════════════════════════════════════════
        // 집행 대상 결정
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 현재 집행 가능한 대상을 결정한다.
        ///
        /// [선택 기준]
        ///   1. _sealReadyList 에서 IsSealed = false 이고 범위 내 있는 대상
        ///   2. 등급 우선순위: Core > Part > Normal
        ///   3. 같은 등급이면 플레이어와 가장 가까운 대상
        /// </summary>
        private SealableComponent DetermineTarget()
        {
            if (_playerTransform == null) return null;

            SealableComponent best = null;
            float bestDist = float.MaxValue;
            SealGrade bestGrade = SealGrade.Normal;

            foreach (var s in _sealReadyList)
            {
                if (s == null || s.IsSealed) continue;

                float dist = Vector2.Distance(
                    _playerTransform.position,
                    s.transform.position);

                if (dist > s.SealRange) continue;

                // 등급 우선순위 비교
                bool higherGrade = s.Grade > bestGrade;
                bool sameGradeCloser = s.Grade == bestGrade && dist < bestDist;

                if (best == null || higherGrade || sameGradeCloser)
                {
                    best = s;
                    bestDist = dist;
                    bestGrade = s.Grade;
                }
            }

            return best;
        }

        // ══════════════════════════════════════════════════════
        // 집행 실행
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 봉인 집행 코루틴.
        ///
        /// [등급별 슬로우 정책]
        ///   Normal : 슬로우 없음
        ///   Part   : 홀드 완료 후 짧은 슬로우 (타격감)
        ///   Core   : 홀드 중 내내 강한 슬로우
        /// </summary>
        private IEnumerator ExecuteSeal(SealableComponent target)
        {
            _isExecuting = true;
            BlockPlayerInput();

            Debug.Log($"[SealExecutor] {target.name} 집행 시작 | 등급:{target.Grade}");

            // Core 등급: 홀드 시작 시 슬로우 적용
            if (target.Grade == SealGrade.Core && _data != null)
            {
                Time.timeScale = _data.dilPhaseSlowTimeScale;
                Debug.Log($"[SealExecutor] Core 슬로우 시작 → {_data.dilPhaseSlowTimeScale}");
            }

            // 홀드 대기
            float elapsed = 0f;
            bool completed = false;

            while (elapsed < target.SealHoldTime)
            {
                if (_input == null || !_input.IsSealHeld)
                {
                    Debug.Log("[SealExecutor] S키 해제 → 집행 취소");
                    break;
                }

                if (Vector2.Distance(_playerTransform.position, target.transform.position)
                    > target.SealRange)
                {
                    Debug.Log("[SealExecutor] 범위 이탈 → 집행 취소");
                    break;
                }

                elapsed += Time.unscaledDeltaTime;

                if (elapsed >= target.SealHoldTime)
                    completed = true;

                yield return null;
            }

            if (completed)
            {
                // 집행 완료
                target.ExecuteSeal();
                _sealReadyList.Remove(target);

                Debug.Log($"[SealExecutor] {target.name} 집행 완료 | 등급:{target.Grade}");

                // Part 등급: 집행 완료 후 짧은 슬로우
                if (target.Grade == SealGrade.Part && _data != null)
                {
                    Time.timeScale = _data.partSealSlowTimeScale;
                    Debug.Log($"[SealExecutor] Part 슬로우 시작 → {_data.partSealSlowTimeScale}");
                    yield return new WaitForSecondsRealtime(_data.partSealSlowDuration);
                }
            }

            RestoreTimeScale();
            UnblockPlayerInput();
            _isExecuting = false;
            _mustReleaseKey = true;
            _cooldownTimer = 0.5f;
        }

        // ══════════════════════════════════════════════════════
        // 플레이어 입력 차단
        // ══════════════════════════════════════════════════════

        private void BlockPlayerInput()
        {
            _input?.BlockAll();
            if (_playerRigid2D != null)
                _playerRigid2D.linearVelocity = Vector2.zero;
        }

        private void UnblockPlayerInput()
        {
            _input?.UnblockAll();
        }

        private void RestoreTimeScale()
        {
            if (!Mathf.Approximately(Time.timeScale, 1.0f))
            {
                Time.timeScale = 1.0f;
                Debug.Log("[SealExecutor] TimeScale 복구 → 1.0");
            }
        }

        // ══════════════════════════════════════════════════════
        // 외부 주입
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// BossWardenCore.Initialize() 에서 DataSO 주입.
        /// </summary>
        public void Initialize(BossWardenDataSO data)
        {
            _data = data;
        }
    }
}