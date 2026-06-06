// ============================================================
// SealExecutionRunner.cs  v1.0
// S키 홀드 집행 실행 관리자
//
// [역할]
//   S키 홀드 입력 감지 + 집행 실행 전담.
//   SealExecutionEvent 에서 최적 대상을 받아
//   SealableComponent.ExecuteSeal() 호출까지 처리.
//
// [구버전 SealExecutor 와의 역할 분리]
//   SealExecutionEvent  → 목록 관리 + 우선순위 결정
//   SealExecutionRunner → S키 입력 + 홀드 타이머 + 집행 실행 + 슬로우
//
// [집행 흐름]
//   DetectSealInput() 상시 루프
//     → S키 홀드 여부 체크 (PlayerInputHandler.IsSealHeld)
//     → SealExecutionEvent.GetBestTarget(playerPos) 조회
//     → 대상 있음 → 홀드 타이머 누적
//     → 홀드 완료 → ExecuteSeal(target) 코루틴
//
// [등급별 슬로우 정책]
//   Normal : 슬로우 없음
//   Part   : 집행 완료 후 짧은 슬로우 (partSealSlowTimeScale, partSealSlowDuration)
//   Core   : 홀드 시작 시 슬로우 (finalSealSlowTimeScale)
//            최종 봉인(코어 봉인도 100%) 시 더 강한 슬로우 적용
//
// [재집행 방지]
//   _mustReleaseKey: 집행 완료 후 S키를 한 번 뗀 것 확인 후 재집행 허용
//   _cooldownTimer:  집행 완료 후 0.5초 쿨다운
//
// [플레이어 입력 차단]
//   집행 중 PlayerInputHandler.BlockAll()
//   집행 완료/취소 후 PlayerInputHandler.UnblockAll()
//
// [SealExecutionEffect 연동]
//   GetComponent<SealExecutionEffect> 로 집행 연출 컴포넌트 탐색.
//   OnExecutionStart() / OnExecutionProgress() / OnExecutionComplete() / OnExecutionCancel() 호출.
//
// [부착 위치]
//   Boss_Root 오브젝트에 부착. (보스 1개당 1개)
//
// [namespace] SEAL
// ============================================================

using System.Collections;
using UnityEngine;

namespace SEAL
{
    /// <summary>
    /// S키 홀드 집행 실행 관리자. (v1.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [집행 흐름]
    ///   DetectSealInput() 루프
    ///     S키 홀드 + GetBestTarget() → 홀드 타이머 누적
    ///     홀드 완료 → ExecuteSeal(target)
    ///       → Grade 별 슬로우 적용
    ///       → SealableComponent.ExecuteSeal()
    ///       → 슬로우 복구 + 입력 차단 해제
    ///
    /// [외부 API]
    ///   Initialize(BossDataSO)   DataSO 주입
    ///   ForceStop()              집행 강제 중단 (보스 처치 등)
    /// ────────────────────────────────────────────────────
    /// </summary>
    public class SealExecutionRunner : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── DataSO ──────────────────────")]

        /// <summary>
        /// 범용 보스 DataSO.
        /// 슬로우 배율 / 홀드 시간 참조.
        /// BossWardenCore.Initialize() 에서 주입 or Inspector 직접 연결.
        /// </summary>
        [Tooltip("BossDataSO. 슬로우 배율 참조. 필수.")]
        [SerializeField] private BossDataSO _bossData;

        [Header("── 집행 이벤트 관리자 ──────────────────────")]

        /// <summary>
        /// SealExecutionEvent 참조.
        /// 미연결 시 GetComponent 자동 탐색.
        /// </summary>
        [Tooltip("SealExecutionEvent. 미연결 시 자동 탐색.")]
        [SerializeField] private SealExecutionEvent _executionEvent;

        // ══════════════════════════════════════════════════════
        // 컴포넌트 참조
        // ══════════════════════════════════════════════════════

        /// <summary>PlayerInputHandler 싱글턴.</summary>
        private PlayerInputHandler _input;

        /// <summary>플레이어 Transform. 거리 체크 + 입력 차단에 사용.</summary>
        private Transform _playerTransform;

        /// <summary>플레이어 Rigidbody2D. 집행 중 velocity 정지에 사용.</summary>
        private Rigidbody2D _playerRigid2D;

        // ══════════════════════════════════════════════════════
        // 내부 상태
        // ══════════════════════════════════════════════════════

        /// <summary>현재 집행 실행 중 여부 (중복 방지).</summary>
        private bool _isExecuting;

        /// <summary>
        /// S키 재누름 확인 플래그.
        /// 집행 완료 후 S키를 한 번 뗀 것 확인 후 재집행 허용.
        /// </summary>
        private bool _mustReleaseKey;

        /// <summary>집행 쿨다운 타이머 (UnscaledTime 기준).</summary>
        private float _cooldownTimer;

        /// <summary>현재 홀드 누적 시간 (UnscaledTime 기준).</summary>
        private float _holdTimer;

        /// <summary>집행 강제 중단 플래그. ForceStop() 에서 설정.</summary>
        private bool _forceStop;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Start()
        {
            // SealExecutionEvent 자동 탐색
            if (_executionEvent == null)
                _executionEvent = GetComponent<SealExecutionEvent>();

            if (_executionEvent == null)
            {
                Debug.LogError($"[SealExecutionRunner] {gameObject.name} — SealExecutionEvent 미연결.");
                enabled = false;
                return;
            }

            // 플레이어 탐색
            var players = FindObjectsByType<PlayerMoveController>(FindObjectsSortMode.None);
            if (players.Length > 0)
            {
                _playerTransform = players[0].transform;
                _playerRigid2D = players[0].GetComponent<Rigidbody2D>();
            }
            else
                Debug.LogWarning($"[SealExecutionRunner] PlayerMoveController 탐색 실패.");

            _input = PlayerInputHandler.Instance;

            StartCoroutine(DetectSealInput());
        }

        private void OnDestroy()
        {
            RestoreTimeScale();
        }

        private void Update()
        {
            // 쿨다운 타이머 (UnscaledTime — 슬로우 중에도 감소)
            if (_cooldownTimer > 0f)
                _cooldownTimer -= Time.unscaledDeltaTime;

            // S키 뗌 확인 → 재집행 허용
            if (_mustReleaseKey && _input != null && !_input.IsSealHeld)
                _mustReleaseKey = false;
        }

        // ══════════════════════════════════════════════════════
        // 입력 감지 루프
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// S키 봉인 집행 입력 감지 상시 루프.
        ///
        /// [루프 조건 — 스킵]
        ///   _isExecuting   현재 집행 중
        ///   _cooldownTimer 쿨다운 중
        ///   _mustReleaseKey 재누름 대기 중
        ///   _forceStop     강제 중단 플래그
        ///
        /// [홀드 타이머]
        ///   S키 홀드 + 대상 있음 → 타이머 누적 (UnscaledDeltaTime)
        ///   S키 해제 or 대상 없음 → 타이머 리셋
        ///   타이머 >= target.SealHoldTime → ExecuteSeal 코루틴 시작
        /// </summary>
        private IEnumerator DetectSealInput()
        {
            while (true)
            {
                // 스킵 조건
                if (_isExecuting || _cooldownTimer > 0f ||
                    _mustReleaseKey || _forceStop)
                {
                    _holdTimer = 0f;
                    yield return null;
                    continue;
                }

                // S키 홀드 체크
                if (_input == null || !_input.IsSealHeld)
                {
                    _holdTimer = 0f;
                    yield return null;
                    continue;
                }

                // 최적 집행 대상 조회
                Vector2 playerPos = _playerTransform != null
                    ? (Vector2)_playerTransform.position
                    : Vector2.zero;

                SealableComponent target = _executionEvent.GetBestTarget(playerPos);

                if (target == null)
                {
                    _holdTimer = 0f;
                    yield return null;
                    continue;
                }

                // 홀드 타이머 누적 (UnscaledDeltaTime — 슬로우 중에도 일정 속도)
                _holdTimer += Time.unscaledDeltaTime;

                // 진행도 연출 갱신 (SealExecutionEffect)
                float progress = _holdTimer / target.SealHoldTime;
                GetEffectComponent(target)?.OnExecutionProgress(Mathf.Clamp01(progress));

                // 홀드 완료 → 집행 실행
                if (_holdTimer >= target.SealHoldTime)
                {
                    _holdTimer = 0f;
                    yield return StartCoroutine(ExecuteSeal(target));
                }

                yield return null;
            }
        }

        // ══════════════════════════════════════════════════════
        // 집행 실행
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 봉인 집행 코루틴.
        ///
        /// [등급별 슬로우 정책]
        ///   Normal : 슬로우 없음
        ///   Part   : 집행 완료 후 짧은 슬로우 (타격감)
        ///            partSealSlowTimeScale / partSealSlowDuration
        ///   Core   : 홀드 시작 시 슬로우 적용
        ///            finalSealSlowTimeScale (최종 봉인 강한 슬로우)
        ///
        /// [집행 중 처리]
        ///   플레이어 입력 차단 (BlockAll)
        ///   홀드 중 S키 해제 or 범위 이탈 → 취소
        ///   완료 → SealableComponent.ExecuteSeal()
        /// </summary>
        private IEnumerator ExecuteSeal(SealableComponent target)
        {
            _isExecuting = true;
            BlockPlayerInput();

            var effect = GetEffectComponent(target);

            // 집행 시작 연출
            effect?.OnExecutionStart();

            // Core 등급: 홀드 시작 시 슬로우
            if (target.Grade == SealGrade.Core && _bossData?.SealData != null)
            {
                Time.timeScale = _bossData.SealData.finalSealSlowTimeScale;
                Debug.Log($"[SealExecutionRunner] Core 슬로우 시작 → " +
                          $"{_bossData.SealData.finalSealSlowTimeScale}");
            }

            Debug.Log($"[SealExecutionRunner] ▶ {target.name} 집행 시작 | 등급:{target.Grade}");

            // 홀드 실행 루프
            float elapsed = 0f;
            float holdTime = target.SealHoldTime;
            bool completed = false;

            while (elapsed < holdTime)
            {
                // 강제 중단
                if (_forceStop) goto cleanup;

                // S키 해제
                if (_input == null || !_input.IsSealHeld)
                {
                    Debug.Log("[SealExecutionRunner] S키 해제 → 집행 취소");
                    goto cleanup;
                }

                // 범위 이탈
                if (_playerTransform != null)
                {
                    float dist = Vector2.Distance(
                        _playerTransform.position,
                        target.transform.position);

                    if (dist > target.SealRange)
                    {
                        Debug.Log("[SealExecutionRunner] 범위 이탈 → 집행 취소");
                        goto cleanup;
                    }
                }

                elapsed += Time.unscaledDeltaTime;

                // 진행도 연출 갱신
                effect?.OnExecutionProgress(elapsed / holdTime);

                yield return null;
            }

            // ── 집행 완료 ──────────────────────
            completed = true;

            // 슬로우 복구
            RestoreTimeScale();

            // SealableComponent 집행 완료 처리
            target.ExecuteSeal();

            // 완료 연출
            effect?.OnExecutionComplete();

            // Part 등급: 집행 완료 후 짧은 슬로우 (타격감)
            if (target.Grade == SealGrade.Part && _bossData?.SealData != null)
            {
                float slowScale = _bossData.SealData.partSealSlowTimeScale;
                float slowDuration = _bossData.SealData.partSealSlowDuration;

                Time.timeScale = slowScale;

                yield return new WaitForSecondsRealtime(slowDuration);

                RestoreTimeScale();
            }

            Debug.Log($"[SealExecutionRunner] ✅ {target.name} 집행 완료 | 등급:{target.Grade}");
            goto finish;

        cleanup:
            // ── 집행 취소 ──────────────────────
            RestoreTimeScale();
            effect?.OnExecutionCancel();

            Debug.Log($"[SealExecutionRunner] ■ {target.name} 집행 취소");

        finish:
            UnblockPlayerInput();
            _isExecuting = false;
            _mustReleaseKey = true;
            _cooldownTimer = 0.5f;
        }

        // ══════════════════════════════════════════════════════
        // 플레이어 입력 차단 / 해제
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 집행 중 플레이어 이동 / 대시 / 공격 차단.
        /// velocity 즉시 정지.
        /// </summary>
        private void BlockPlayerInput()
        {
            _input?.BlockAll();

            if (_playerRigid2D != null)
                _playerRigid2D.linearVelocity = Vector2.zero;
        }

        /// <summary>
        /// 집행 완료 / 취소 후 입력 차단 해제.
        /// </summary>
        private void UnblockPlayerInput()
        {
            _input?.UnblockAll();
        }

        // ══════════════════════════════════════════════════════
        // 슬로우 처리
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Time.timeScale 을 1.0 으로 복구.
        /// 집행 완료 / 취소 / OnDestroy 에서 호출.
        /// </summary>
        private void RestoreTimeScale()
        {
            if (!Mathf.Approximately(Time.timeScale, 1.0f))
            {
                Time.timeScale = 1.0f;
                Debug.Log("[SealExecutionRunner] TimeScale 복구 → 1.0");
            }
        }

        // ══════════════════════════════════════════════════════
        // 내부 유틸
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 집행 대상의 SealExecutionEffect 컴포넌트 탐색.
        /// 없으면 null 반환 (연출 생략).
        /// </summary>
        private SealExecutionEffect GetEffectComponent(SealableComponent target)
        {
            if (target == null) return null;
            return target.GetComponent<SealExecutionEffect>();
        }

        // ══════════════════════════════════════════════════════
        // 외부 API
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// BossDataSO 외부 주입.
        /// BossWardenCore.Initialize() 에서 호출.
        /// </summary>
        public void Initialize(BossDataSO data)
        {
            _bossData = data;
        }

        /// <summary>
        /// 집행 강제 중단.
        /// 보스 처치 / 씬 전환 시 호출.
        /// </summary>
        public void ForceStop()
        {
            _forceStop = true;
            RestoreTimeScale();
            UnblockPlayerInput();

            Debug.Log("[SealExecutionRunner] 집행 강제 중단");
        }

        /// <summary>
        /// 강제 중단 해제. 전투 재시작 시 호출.
        /// </summary>
        public void Resume()
        {
            _forceStop = false;
        }
    }
}