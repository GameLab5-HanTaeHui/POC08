// ============================================================
// SealExecutionEvent.cs  v1.0
// 봉인 집행 이벤트 통합 관리자
//
// [역할]
//   SealReadyNotifier 들에서 발행된 OnReadyToExecute 이벤트를 수신하고
//   집행 가능 대상 목록을 통합 관리.
//   SealExecutionRunner 에 "현재 집행 가능 목록" 을 제공.
//
// [SealExecutor(구버전) 와의 역할 분리]
//   구버전 SealExecutor 는 목록 관리 + S키 입력 + 집행 실행을 전부 담당.
//   → 단일 책임 원칙 위반
//
//   신버전 분리:
//     SealExecutionEvent   → 집행 가능 목록 관리 + 우선순위 결정
//     SealExecutionRunner  → S키 홀드 입력 감지 + 집행 실행
//
// [등록 방식]
//   SealReadyNotifier 자동 수집:
//     Start() 에서 GetComponentsInChildren<SealReadyNotifier>() 로
//     자기 하위의 모든 SealReadyNotifier 자동 등록.
//   수동 등록:
//     동적 생성 부위 → RegisterNotifier(notifier) 수동 호출.
//
// [우선순위]
//   Core > Part > Normal
//   같은 등급이면 플레이어와 가장 가까운 대상 선택.
//   SealExecutionRunner 가 GetBestTarget(playerPos) 로 조회.
//
// [OnForceReleased 중복 구독 방지]
//   SealableComponent.OnForceReleased 를 SealReadyNotifier 를 통해 수신.
//   기존 SealExecutor 의 Dictionary 캐싱 방식 계승.
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
    /// 봉인 집행 이벤트 통합 관리자. (v1.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [흐름]
    ///   SealReadyNotifier.OnReadyToExecute 수신
    ///     → _readyList 에 SealableComponent 등록
    ///     → OnTargetAdded 발행 → SealExecutionRunner 알림
    ///
    ///   SealReadyNotifier.OnReadyCancelled 수신
    ///     → _readyList 에서 제거
    ///     → OnTargetRemoved 발행 → SealExecutionRunner 알림
    ///
    ///   SealExecutionRunner.GetBestTarget(playerPos)
    ///     → 우선순위 + 거리 기준 최적 대상 반환
    ///
    /// [외부 API]
    ///   GetBestTarget(Vector2 playerPos)  최적 집행 대상 조회
    ///   RegisterNotifier(notifier)        수동 등록
    ///   UnregisterNotifier(notifier)      수동 해제
    ///   GetReadyCount()                   현재 집행 가능 대상 수
    /// ────────────────────────────────────────────────────
    /// </summary>
    public class SealExecutionEvent : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        // 이벤트
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 집행 가능 대상이 추가될 때 발행.
        /// SealExecutionRunner 가 구독.
        /// 파라미터: 추가된 SealableComponent.
        /// </summary>
        public event Action<SealableComponent> OnTargetAdded;

        /// <summary>
        /// 집행 가능 대상이 제거될 때 발행.
        /// SealExecutionRunner 가 구독.
        /// 파라미터: 제거된 SealableComponent.
        /// </summary>
        public event Action<SealableComponent> OnTargetRemoved;

        // ══════════════════════════════════════════════════════
        // 내부 상태
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 현재 집행 가능 대상 목록.
        /// SealReadyNotifier.OnReadyToExecute 수신 시 추가.
        /// OnReadyCancelled 수신 시 제거.
        /// </summary>
        private readonly List<SealableComponent> _readyList = new();

        /// <summary>
        /// OnReadyCancelled 핸들러 캐시.
        /// SealReadyNotifier 별로 Action 저장.
        /// 재등록 시 중복 구독 방지 + 정확한 -= 해제 보장.
        /// </summary>
        private readonly Dictionary<SealReadyNotifier, Action<SealableComponent>>
            _cancelledHandlers = new();

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Start()
        {
            // 자기 하위 모든 SealReadyNotifier 자동 수집
            var notifiers = GetComponentsInChildren<SealReadyNotifier>(includeInactive: true);
            foreach (var n in notifiers)
                RegisterNotifier(n);

            Debug.Log($"[SealExecutionEvent] {gameObject.name} 초기화 | " +
                      $"등록 노티파이어:{notifiers.Length}개");
        }

        private void OnDestroy()
        {
            // 모든 구독 해제
            var notifiers = new List<SealReadyNotifier>(_cancelledHandlers.Keys);
            foreach (var n in notifiers)
                UnregisterNotifier(n);
        }

        // ══════════════════════════════════════════════════════
        // 등록 / 해제
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// SealReadyNotifier 를 등록한다.
        /// Start() 에서 자동 수집하지만 동적 생성 시 수동 호출도 가능.
        ///
        /// [중복 구독 방지]
        ///   _cancelledHandlers 딕셔너리에 핸들러 캐싱.
        ///   재등록 전 기존 핸들러 -= 후 새 핸들러 += 보장.
        /// </summary>
        public void RegisterNotifier(SealReadyNotifier notifier)
        {
            if (notifier == null) return;

            // OnReadyToExecute: 멤버 함수 → -= / += 정상 동작
            notifier.OnReadyToExecute -= HandleReadyToExecute;
            notifier.OnReadyToExecute += HandleReadyToExecute;

            // OnReadyCancelled: 딕셔너리 캐싱으로 중복 방지
            if (_cancelledHandlers.TryGetValue(notifier, out var existing))
            {
                notifier.OnReadyCancelled -= existing;
                _cancelledHandlers.Remove(notifier);
            }

            Action<SealableComponent> handler = (s) => HandleReadyCancelled(s);
            _cancelledHandlers[notifier] = handler;
            notifier.OnReadyCancelled += handler;

            Debug.Log($"[SealExecutionEvent] 노티파이어 등록: {notifier.gameObject.name}");
        }

        /// <summary>
        /// SealReadyNotifier 구독 해제.
        /// OnDestroy 또는 적 오브젝트 파괴 시 호출.
        /// </summary>
        public void UnregisterNotifier(SealReadyNotifier notifier)
        {
            if (notifier == null) return;

            notifier.OnReadyToExecute -= HandleReadyToExecute;

            if (_cancelledHandlers.TryGetValue(notifier, out var handler))
            {
                notifier.OnReadyCancelled -= handler;
                _cancelledHandlers.Remove(notifier);
            }
        }

        // ══════════════════════════════════════════════════════
        // 이벤트 핸들러
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// SealReadyNotifier.OnReadyToExecute 수신.
        /// 집행 가능 목록에 추가 + OnTargetAdded 발행.
        /// </summary>
        private void HandleReadyToExecute(SealableComponent sealable)
        {
            if (sealable == null) return;
            if (_readyList.Contains(sealable)) return;

            _readyList.Add(sealable);
            OnTargetAdded?.Invoke(sealable);

            Debug.Log($"[SealExecutionEvent] ▶ 집행 대상 추가: {sealable.name} | " +
                      $"등급:{sealable.Grade} | 총:{_readyList.Count}개");
        }

        /// <summary>
        /// SealReadyNotifier.OnReadyCancelled 수신.
        /// 집행 가능 목록에서 제거 + OnTargetRemoved 발행.
        /// </summary>
        private void HandleReadyCancelled(SealableComponent sealable)
        {
            if (sealable == null) return;
            if (!_readyList.Contains(sealable)) return;

            _readyList.Remove(sealable);
            OnTargetRemoved?.Invoke(sealable);

            Debug.Log($"[SealExecutionEvent] ■ 집행 대상 제거: {sealable?.name} | " +
                      $"총:{_readyList.Count}개");
        }

        // ══════════════════════════════════════════════════════
        // 외부 API — SealExecutionRunner 에서 호출
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 현재 집행 가능 최적 대상을 반환한다.
        /// SealExecutionRunner 가 매 프레임 또는 S키 입력 시 호출.
        ///
        /// [선택 기준]
        ///   1. IsSealed = false (아직 봉인 안 됨)
        ///   2. 플레이어가 SealRange 이내
        ///   3. 등급 우선순위: Core > Part > Normal
        ///   4. 같은 등급이면 플레이어와 가장 가까운 대상
        /// </summary>
        /// <param name="playerPos">플레이어 현재 위치 (거리 계산용).</param>
        /// <returns>최적 집행 대상. 없으면 null.</returns>
        public SealableComponent GetBestTarget(Vector2 playerPos)
        {
            SealableComponent best = null;
            float bestDist = float.MaxValue;
            SealGrade bestGrade = SealGrade.Normal;

            foreach (var s in _readyList)
            {
                if (s == null || s.IsSealed) continue;

                float dist = Vector2.Distance(playerPos, s.transform.position);
                if (dist > s.SealRange) continue;

                // 등급 우선순위 비교 (Core > Part > Normal)
                bool higherGrade = (int)s.Grade > (int)bestGrade;
                bool sameGradeClose = s.Grade == bestGrade && dist < bestDist;

                if (best == null || higherGrade || sameGradeClose)
                {
                    best = s;
                    bestDist = dist;
                    bestGrade = s.Grade;
                }
            }

            return best;
        }

        /// <summary>
        /// 현재 집행 가능 대상 수 반환.
        /// UI 표시 또는 상태 확인용.
        /// </summary>
        public int GetReadyCount() => _readyList.Count;

        /// <summary>
        /// 집행 가능 목록 전체 반환 (읽기 전용).
        /// SealExecutionRunner 등 외부에서 목록 조회 시 사용.
        /// </summary>
        public IReadOnlyList<SealableComponent> GetReadyList()
            => _readyList.AsReadOnly();

        /// <summary>
        /// 집행 가능 목록 전체 강제 초기화.
        /// 전투 리셋 시 SealGaugeManager 에서 호출.
        /// </summary>
        public void ClearAll()
        {
            _readyList.Clear();
            Debug.Log($"[SealExecutionEvent] 집행 목록 전체 초기화");
        }

        // ══════════════════════════════════════════════════════
        // Gizmos
        // ══════════════════════════════════════════════════════

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            foreach (var s in _readyList)
            {
                if (s == null) continue;

                Gizmos.color = s.Grade == SealGrade.Core
                    ? new Color(1f, 0.93f, 0f, 0.4f)
                    : new Color(0f, 0.53f, 1f, 0.4f);

                Gizmos.DrawWireSphere(s.transform.position, s.SealRange);
            }
        }
#endif
    }
}