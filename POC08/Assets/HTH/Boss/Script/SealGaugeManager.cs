// ============================================================
// SealGaugeManager.cs  v1.0
// 봉인도 전체 조율 관리자
//
// [역할]
//   보스 하위의 모든 SealableComponent 를 수집하고
//   봉인도 전체 상태를 조율하는 관리자.
//
//   SealStateManager 에서 상태 전환이 결정되면
//   SealGaugeManager 가 실제 ForceRelease / ActivateGauge 등을 실행.
//
// [담당]
//   ① 하위 모든 SealableComponent 자동 수집
//   ② ForceRelease 전체 일괄 호출 (그로기 실패 / 딜페이즈 종료 시)
//   ③ 코어 SealableComponent ActivateGauge 제어 (딜페이즈 전용)
//   ④ 봉인 카운트 / 저항 배율 관리
//   ⑤ AreAllPartSealed() / GetSealedCount() 상태 조회
//   ⑥ SealExecutionEvent 에 SealReadyNotifier 자동 연동
//
// [SealStateManager 와의 관계]
//   SealStateManager  → 상태 결정 + 이벤트 발행
//   SealGaugeManager  → 실제 봉인도 데이터 조작
//
// [BossWardenCore 와의 관계]
//   기존: BossWardenCore 가 _armL.ForceRelease() / _armR.ForceRelease() 직접 호출
//   변경: SealGaugeManager.ReleaseAllParts() 한 번 호출
//         → 모든 Part 등급 SealableComponent 일괄 해제
//
// [코어 활성 제어]
//   ActivateCore(bool):
//     Core 등급 SealableComponent.ActivateGauge(bool) 호출
//     딜페이즈 진입 시 true / 종료 시 false
//
// [부착 위치]
//   Boss_Root 오브젝트에 부착. (보스 1개당 1개)
//
// [namespace] SEAL
// ============================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace SEAL
{
    /// <summary>
    /// 봉인도 전체 조율 관리자. (v1.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [외부 API]
    ///   Initialize(BossDataSO)       DataSO 주입 + 전체 초기화
    ///   ReleaseAllParts(bool)        모든 Part 봉인 해제
    ///   ReleaseAll(bool)             모든 SealableComponent 봉인 해제
    ///   ActivateCore(bool)           코어 딜페이즈 활성/비활성
    ///   AreAllPartsSealed()          모든 Part 봉인 완료 여부
    ///   GetSealedCount(SealGrade)    등급별 봉인 완료 수
    ///   GetComponent(name)           이름으로 SealableComponent 탐색
    /// ────────────────────────────────────────────────────
    /// </summary>
    public class SealGaugeManager : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── DataSO ──────────────────────")]

        /// <summary>
        /// 범용 보스 DataSO.
        /// 각 SealableComponent 초기화 시 주입.
        /// BossWardenCore.Initialize() 에서 주입 or Inspector 직접 연결.
        /// </summary>
        [Tooltip("BossDataSO. 필수. BossWardenCore 에서 주입 or Inspector 연결.")]
        [SerializeField] private BossDataSO _bossData;

        // ══════════════════════════════════════════════════════
        // 수집된 컴포넌트
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 자기 하위 모든 SealableComponent 목록.
        /// Awake 에서 자동 수집.
        /// </summary>
        private readonly List<SealableComponent> _allSealables = new();

        /// <summary>
        /// Part 등급 SealableComponent 목록.
        /// AreAllPartsSealed() / ReleaseAllParts() 에서 사용.
        /// </summary>
        private readonly List<SealableComponent> _parts = new();

        /// <summary>
        /// Core 등급 SealableComponent 목록.
        /// ActivateCore() 에서 사용.
        /// </summary>
        private readonly List<SealableComponent> _cores = new();

        // ══════════════════════════════════════════════════════
        // 이벤트
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 모든 Part 봉인 완료 시 발행.
        /// SealStateManager 가 구독 → 그로기 진입 조건 체크.
        /// </summary>
        public event Action OnAllPartsSealed;

        /// <summary>
        /// 봉인 해제(ForceRelease) 완료 시 발행.
        /// SealStateManager 가 구독 → 루프 재시작 알림.
        /// </summary>
        public event Action OnAllPartsReleased;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            // 하위 모든 SealableComponent 자동 수집 + 등급별 분류
            var all = GetComponentsInChildren<SealableComponent>(includeInactive: true);
            foreach (var s in all)
            {
                _allSealables.Add(s);

                switch (s.grade)
                {
                    case SealGrade.Part: _parts.Add(s); break;
                    case SealGrade.Core: _cores.Add(s); break;
                }
            }

            Debug.Log($"[SealGaugeManager] {gameObject.name} 수집 완료 | " +
                      $"전체:{_allSealables.Count} Part:{_parts.Count} Core:{_cores.Count}");
        }

        private void Start()
        {
            // 각 Part 봉인 완료 이벤트 구독 → 전체 봉인 체크
            foreach (var s in _parts)
            {
                s.OnSealCompleted -= HandlePartSealCompleted;
                s.OnSealCompleted += HandlePartSealCompleted;
            }
        }

        private void OnDestroy()
        {
            foreach (var s in _parts)
            {
                if (s != null)
                    s.OnSealCompleted -= HandlePartSealCompleted;
            }
        }

        // ══════════════════════════════════════════════════════
        // 초기화
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// DataSO 주입 + 전체 SealableComponent 초기화.
        /// BossWardenCore.Start() 에서 호출.
        ///
        /// [초기화 내용]
        ///   BossDataSO 주입
        ///   Part 등급: partSealGaugeMax 자동 설정
        ///   Core 등급: coreSealGaugeMax 주입
        ///   전체 상태 초기화 (resetSealCount = true)
        /// </summary>
        public void Initialize(BossDataSO data)
        {
            _bossData = data;

            if (_bossData == null || !_bossData.IsValid())
            {
                Debug.LogError($"[SealGaugeManager] BossDataSO 미연결 또는 유효하지 않음.");
                return;
            }

            foreach (var s in _allSealables)
            {
                if (s == null) continue;

                float maxGauge = s.grade == SealGrade.Core
                    ? _bossData.SealData.coreSealGaugeMax
                    : 0f; // Part/Normal 은 BossDataSO 에서 자동 설정

                s.Initialize(_bossData, maxGauge);
            }

            Debug.Log($"[SealGaugeManager] 전체 초기화 완료 | 부위:{_allSealables.Count}개");
        }

        // ══════════════════════════════════════════════════════
        // ForceRelease
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 모든 Part 등급 봉인 강제 해제.
        /// 그로기 실패 / 딜페이즈 종료 시 SealStateManager 에서 호출.
        ///
        /// [저항 횟수 원칙]
        ///   resetSealCount = false (기본): 저항 횟수 유지 → 반복 봉인 난이도 상승
        ///   resetSealCount = true: 씬 완전 초기화 시에만 사용
        /// </summary>
        /// <param name="resetSealCount">저항 횟수 초기화 여부.</param>
        public void ReleaseAllParts(bool resetSealCount = false)
        {
            foreach (var s in _parts)
            {
                if (s != null)
                    s.ForceRelease(resetSealCount);
            }

            OnAllPartsReleased?.Invoke();

            Debug.Log($"[SealGaugeManager] ■ 모든 Part 봉인 해제 | " +
                      $"저항횟수:{(resetSealCount ? "초기화" : "유지")}");
        }

        /// <summary>
        /// 모든 SealableComponent 봉인 강제 해제.
        /// 전투 완전 초기화 시 사용.
        /// </summary>
        /// <param name="resetSealCount">저항 횟수 초기화 여부.</param>
        public void ReleaseAll(bool resetSealCount = false)
        {
            foreach (var s in _allSealables)
            {
                if (s != null)
                    s.ForceRelease(resetSealCount);
            }

            OnAllPartsReleased?.Invoke();

            Debug.Log($"[SealGaugeManager] ■ 전체 봉인 해제");
        }

        // ══════════════════════════════════════════════════════
        // 코어 활성 제어
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 코어 딜페이즈 활성/비활성 전환.
        /// 딜페이즈 진입 시 true / 종료 시 false.
        /// SealStateManager 에서 호출.
        /// </summary>
        public void ActivateCore(bool isActive)
        {
            foreach (var s in _cores)
            {
                if (s != null)
                    s.ActivateGauge(isActive);
            }

            Debug.Log($"[SealGaugeManager] 코어 활성 = {isActive}");
        }

        // ══════════════════════════════════════════════════════
        // 상태 조회
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 모든 Part 등급 봉인 완료 여부.
        /// SealStateManager 에서 그로기 조건 체크 시 사용.
        /// </summary>
        public bool AreAllPartsSealed()
        {
            if (_parts.Count == 0) return false;

            foreach (var s in _parts)
            {
                if (s == null || !s.IsSealed) return false;
            }

            return true;
        }

        /// <summary>
        /// 특정 등급 봉인 완료 수 반환.
        /// </summary>
        /// <param name="grade">조회할 등급.</param>
        /// <returns>해당 등급 중 봉인 완료된 수.</returns>
        public int GetSealedCount(SealGrade grade)
        {
            int count = 0;
            foreach (var s in _allSealables)
            {
                if (s != null && s.grade == grade && s.IsSealed)
                    count++;
            }
            return count;
        }

        /// <summary>
        /// 전체 Part 수 반환.
        /// </summary>
        public int GetPartCount() => _parts.Count;

        /// <summary>
        /// 이름으로 SealableComponent 탐색.
        /// 특정 부위만 개별 제어 시 사용.
        /// </summary>
        /// <param name="goName">탐색할 오브젝트 이름.</param>
        /// <returns>찾은 SealableComponent. 없으면 null.</returns>
        public SealableComponent GetSealable(string goName)
        {
            foreach (var s in _allSealables)
            {
                if (s != null && s.gameObject.name == goName)
                    return s;
            }
            return null;
        }

        /// <summary>
        /// 전체 SealableComponent 목록 반환 (읽기 전용).
        /// </summary>
        public IReadOnlyList<SealableComponent> GetAllSealables()
            => _allSealables.AsReadOnly();

        // ══════════════════════════════════════════════════════
        // 이벤트 핸들러
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Part SealableComponent.OnSealCompleted 수신.
        /// 전체 Part 봉인 완료 여부 체크 → OnAllPartsSealed 발행.
        /// </summary>
        private void HandlePartSealCompleted()
        {
            if (!AreAllPartsSealed()) return;

            OnAllPartsSealed?.Invoke();

            Debug.Log($"[SealGaugeManager] ✅ 모든 Part 봉인 완료 ({_parts.Count}개) " +
                      $"→ OnAllPartsSealed 발행");
        }

        // ══════════════════════════════════════════════════════
        // 디버그 — ContextMenu
        // ══════════════════════════════════════════════════════

#if UNITY_EDITOR
        [ContextMenu("디버그 — 모든 Part 봉인도 즉시 채우기")]
        private void Debug_FillAllParts()
        {
            if (!Application.isPlaying) return;
            foreach (var s in _parts)
            {
                if (s != null)
                    s.AddGauge(99999f);
            }
            Debug.Log("[SealGaugeManager] 디버그: 모든 Part 봉인도 즉시 채움");
        }

        [ContextMenu("디버그 — 모든 Part 강제 해제")]
        private void Debug_ReleaseAllParts()
        {
            if (!Application.isPlaying) return;
            ReleaseAllParts(resetSealCount: false);
        }

        [ContextMenu("디버그 — 코어 봉인도 즉시 채우기")]
        private void Debug_FillCore()
        {
            if (!Application.isPlaying) return;
            foreach (var s in _cores)
            {
                if (s != null)
                    s.AddGauge(99999f);
            }
            Debug.Log("[SealGaugeManager] 디버그: 코어 봉인도 즉시 채움");
        }
#endif
    }
}