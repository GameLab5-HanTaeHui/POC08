// ============================================================
// PlayerAttackHitboxManager.cs  v1.0
// 탑뷰 플레이어 무기 히트박스 관리 컴포넌트
//
// [POC07 참고 스크립트]
//   PlayerWeaponHitboxManager.cs (v1.3)
//   → Collider2D 배열 + OverlapCollider 방식 계승
//   → 탑뷰 구조에 맞게 재설계
//
// [POC07 → POC08 변환 내용]
//
//   POC07:
//     콜라이더 배열 [Combo1, Combo2, Combo3, AirAttack]
//     Enemy / EnemyLock / EnemyShield 세 레이어 분기
//     IDamageable.TakeDamage() / LockComponent.TakeDamage() 분리 호출
//     횡스크롤 전용 (히트박스 좌우만 존재)
//
//   POC08:
//     콜라이더 배열 [Combo1, Combo2, Combo3, Charge]
//       → AirAttack 없음 (탑뷰 공중 공격 없음)
//       → Charge 추가 (강공격 전용 히트박스)
//     Enemy 레이어 단일 감지 (봉인도 누적 대상)
//       → Lock/Shield 분기 없음
//       → IDamageable 대신 OnHit 이벤트 발행
//     WeaponPivot 기준 히트박스 월드 위치 자동 계산
//       → 8방향 공격 방향에 따라 히트박스 위치 자동 갱신
//     PlayerWeaponSwingController 와 연동
//       → InsertCallback 타이밍 대신 EnableHitbox/DisableAllHitboxes API 제공
//
// [히트박스 콜라이더 구조]
//   Weapon 오브젝트 하위에 각 히트박스 Collider2D 를 자식으로 배치.
//   WeaponPivot 이 Z회전하면 히트박스도 함께 회전
//   → 8방향 공격 방향에 따라 히트박스 위치가 자동으로 맞춰짐.
//
// [히트 중복 방지]
//   같은 스윙에서 이미 히트한 콜라이더는 _hitTargets HashSet 으로 걸러냄.
//   DisableAllHitboxes() 호출 시 HashSet 초기화.
//
// [봉인도 연동]
//   OnHit(Collider2D hitCol, float sealAmount) 이벤트 발행.
//   PlayerAttackController 에서 구독 → OnHitTarget 으로 전달.
//
// [네임스페이스]
//   namespace : SEAL
// ============================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace SEAL
{
    /// <summary>
    /// 탑뷰 플레이어 무기 히트박스 관리 컴포넌트. (v1.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [히트박스 인덱스]
    ///   HitboxCombo1  = 0 : 횡베기
    ///   HitboxCombo2  = 1 : 내리찍기
    ///   HitboxCombo3  = 2 : 찌르기 피니셔
    ///   HitboxCharge  = 3 : 강공격
    ///
    /// [사용 흐름 — PlayerWeaponSwingController 연동]
    ///   SwingController.PlaySwing(combo, dir, onHit)
    ///     → onHit 콜백(반경 > 0) → EnableHitbox(index, sealAmount)
    ///     → onHit 콜백(반경 = 0) → DisableAllHitboxes()
    ///
    /// [Hierarchy 배치]
    ///   Player
    ///   └─ WeaponPivot
    ///      └─ Weapon
    ///         ├─ [HitboxCollider_Combo1]  BoxCollider2D isTrigger=true
    ///         ├─ [HitboxCollider_Combo2]  BoxCollider2D isTrigger=true
    ///         ├─ [HitboxCollider_Combo3]  BoxCollider2D isTrigger=true
    ///         └─ [HitboxCollider_Charge]  BoxCollider2D isTrigger=true (더 넓음)
    /// ────────────────────────────────────────────────────
    /// </summary>
    public class PlayerAttackHitboxManager : MonoBehaviour
    {
        // ──────────────────────────────────────────
        // 히트박스 인덱스 상수
        // ──────────────────────────────────────────

        /// <summary> Combo1 (횡베기) 히트박스 인덱스. </summary>
        public const int HitboxCombo1 = 0;

        /// <summary> Combo2 (내리찍기) 히트박스 인덱스. </summary>
        public const int HitboxCombo2 = 1;

        /// <summary> Combo3 (찌르기 피니셔) 히트박스 인덱스. </summary>
        public const int HitboxCombo3 = 2;

        /// <summary> 강공격 히트박스 인덱스. </summary>
        public const int HitboxCharge = 3;

        // ──────────────────────────────────────────
        // Inspector — 히트박스 콜라이더 연결
        // ──────────────────────────────────────────

        [Header("── 히트박스 콜라이더 연결 ──────────────────────")]

        /// <summary>
        /// 콤보별 히트박스 콜라이더 배열.
        /// 인덱스: 0=Combo1 / 1=Combo2 / 2=Combo3 / 3=Charge.
        /// Weapon 오브젝트 하위 자식 Collider2D 연결.
        /// WeaponPivot Z회전 시 함께 회전하여 8방향 자동 대응.
        /// </summary>
        [Tooltip("콤보별 히트박스. 0=Combo1 / 1=Combo2 / 2=Combo3 / 3=Charge. Weapon 하위 Collider2D 연결.")]
        [SerializeField] private Collider2D[] _hitboxes;

        // ──────────────────────────────────────────
        // Inspector — 감지 레이어
        // ──────────────────────────────────────────

        [Header("── 감지 레이어 ──────────────────────")]

        /// <summary>
        /// 공격이 적중할 수 있는 레이어마스크.
        /// Enemy 레이어 선택.
        /// 봉인도 누적 대상.
        ///
        /// [POC07 과의 차이]
        ///   POC07: Enemy + EnemyLock + EnemyShield 세 레이어 분기
        ///   POC08: Enemy 단일 레이어 (봉인 시스템은 별도 SealGaugeSystem 처리)
        /// </summary>
        [Tooltip("공격 감지 레이어. Enemy 레이어 선택.")]
        [SerializeField] private LayerMask _enemyLayer;

        // ──────────────────────────────────────────
        // 내부 상태
        // ──────────────────────────────────────────

        /// <summary>
        /// 현재 활성 히트박스 인덱스.
        /// -1 = 비활성 상태.
        /// </summary>
        private int _activeHitboxIndex = -1;

        /// <summary>
        /// 현재 활성 봉인도 누적량.
        /// EnableHitbox 호출 시 설정.
        /// </summary>
        private float _currentSealAmount;

        /// <summary>
        /// 이번 스윙에서 이미 히트한 콜라이더 집합.
        /// 같은 스윙에서 중복 히트 방지.
        /// DisableAllHitboxes() 시 초기화.
        /// POC07 _hitTargets HashSet 과 동일.
        /// </summary>
        private readonly HashSet<Collider2D> _hitTargets = new HashSet<Collider2D>();

        /// <summary>
        /// OverlapCollider GC 방지 버퍼.
        /// POC07 _overlapBuffer List 와 동일.
        /// </summary>
        private readonly List<Collider2D> _overlapBuffer = new List<Collider2D>();

        // ──────────────────────────────────────────
        // 이벤트
        // ──────────────────────────────────────────

        /// <summary>
        /// 적에 히트 발생 시 발행.
        /// 파라미터1: 적중된 Collider2D.
        /// 파라미터2: 봉인도 누적량.
        /// PlayerAttackController 에서 구독 → OnHitTarget 으로 전달.
        ///
        /// [POC07 OnHit(IDamageable, DamageInfo) 와의 차이]
        ///   POC07: IDamageable.TakeDamage() 를 이 컴포넌트가 직접 호출
        ///   POC08: 이벤트 발행만 수행, 피격 처리는 PlayerAttackController 에서 담당
        ///          → 봉인도 시스템과 분리 용이
        /// </summary>
        public event Action<Collider2D, float> OnHit;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            if (_hitboxes == null || _hitboxes.Length == 0)
            {
                Debug.LogWarning("[PlayerAttackHitboxManager] 히트박스 콜라이더가 연결되지 않았습니다.");
                return;
            }

            // 시작 시 모든 히트박스 비활성화
            DisableAllHitboxes();
        }

        private void Update()
        {
            // 활성 히트박스가 있을 때만 매 프레임 감지 수행
            if (_activeHitboxIndex < 0) return;
            if (!IsValidIndex(_activeHitboxIndex)) return;

            CheckHit(_hitboxes[_activeHitboxIndex]);
        }

        // ══════════════════════════════════════════════════════
        // 외부 API — PlayerWeaponSwingController / PlayerAttackController 에서 호출
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 지정 인덱스의 히트박스를 활성화한다.
        /// PlayerWeaponSwingController 의 InsertCallback 타이밍에 호출.
        ///
        /// [흐름]
        ///   1. 이전 히트박스 비활성화
        ///   2. 지정 콜라이더 활성화 (enabled = true)
        ///   3. _activeHitboxIndex 갱신
        ///   4. _currentSealAmount 설정
        ///   5. _hitTargets 초기화 (새 스윙 히트 초기화)
        /// </summary>
        /// <param name="hitboxIndex">활성화할 히트박스 인덱스 (0~3).</param>
        /// <param name="sealAmount">이 히트박스가 적중 시 누적할 봉인도.</param>
        public void EnableHitbox(int hitboxIndex, float sealAmount)
        {
            if (!IsValidIndex(hitboxIndex)) return;

            DisableAllHitboxes();

            _activeHitboxIndex = hitboxIndex;
            _currentSealAmount = sealAmount;
            _hitTargets.Clear(); // 새 히트박스 활성 시 히트 목록 초기화

            _hitboxes[hitboxIndex].enabled = true;

            Debug.Log($"[PlayerAttackHitboxManager] 히트박스 활성: {hitboxIndex} | 봉인도: {sealAmount:F1}");
        }

        /// <summary>
        /// 모든 히트박스를 비활성화한다.
        /// 스윙 종료 / 취소 시 반드시 호출.
        /// _hitTargets 초기화로 다음 스윙 중복 히트 방지.
        /// </summary>
        public void DisableAllHitboxes()
        {
            if (_hitboxes == null) return;

            foreach (var hb in _hitboxes)
                if (hb != null) hb.enabled = false;

            _activeHitboxIndex = -1;
            _hitTargets.Clear();
        }

        // ══════════════════════════════════════════════════════
        // 히트 감지
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 활성 히트박스로 적 감지 + 히트 처리.
        /// Update() 에서 매 프레임 호출.
        ///
        /// [감지 방식 — POC07 계승]
        ///   hitbox.Overlap(filter, buffer) 로 오버랩 감지.
        ///   _hitTargets 에 이미 있는 콜라이더는 중복 처리 건너뜀.
        ///   감지된 콜라이더에 OnHit 이벤트 발행.
        ///
        /// [POC07 과의 차이]
        ///   POC07: Enemy / EnemyLock / EnemyShield 레이어 분기
        ///          → LockComponent.TakeDamage() / IDamageable.TakeDamage() 분리
        ///   POC08: Enemy 레이어 단일 감지
        ///          → OnHit 이벤트 발행만 (피격 처리는 외부)
        /// </summary>
        private void CheckHit(Collider2D hitbox)
        {
            _overlapBuffer.Clear();

            // 감지 필터 설정
            ContactFilter2D filter = new ContactFilter2D();
            filter.SetLayerMask(_enemyLayer);
            filter.useTriggers = true; // isTrigger=true 인 적 콜라이더도 감지

            // OverlapCollider 로 감지 (GC 방지 NonAlloc 방식)
            hitbox.Overlap(filter, _overlapBuffer);

            for (int i = 0; i < _overlapBuffer.Count; i++)
            {
                Collider2D col = _overlapBuffer[i];

                // 이미 이번 스윙에서 히트한 콜라이더는 건너뜀 (중복 방지)
                if (_hitTargets.Contains(col)) continue;

                _hitTargets.Add(col);

                // 히트 이벤트 발행 → PlayerAttackController 에서 처리
                OnHit?.Invoke(col, _currentSealAmount);

                Debug.Log($"[PlayerAttackHitboxManager] 적 감지: {col.name} | " +
                          $"봉인도 +{_currentSealAmount:F1}");
            }
        }

        // ══════════════════════════════════════════════════════
        // 프로퍼티
        // ══════════════════════════════════════════════════════

        /// <summary> 현재 히트박스 활성 여부. </summary>
        public bool IsActive => _activeHitboxIndex >= 0;

        /// <summary> 현재 활성 히트박스 인덱스. -1 = 비활성. </summary>
        public int ActiveHitboxIndex => _activeHitboxIndex;

        // ══════════════════════════════════════════════════════
        // 내부 유틸리티
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 히트박스 인덱스 유효성 확인.
        /// </summary>
        private bool IsValidIndex(int index)
        {
            if (_hitboxes == null) return false;
            if (index < 0 || index >= _hitboxes.Length) return false;
            if (_hitboxes[index] == null) return false;
            return true;
        }

        // ══════════════════════════════════════════════════════
        // Gizmos — 에디터 히트박스 시각화
        // ══════════════════════════════════════════════════════

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (_hitboxes == null) return;

            for (int i = 0; i < _hitboxes.Length; i++)
            {
                if (_hitboxes[i] == null) continue;

                // 활성 히트박스 = 빨강 / 비활성 = 흰색 반투명
                bool isActive = (i == _activeHitboxIndex);
                Gizmos.color = isActive
                    ? new Color(1f, 0.2f, 0.2f, 0.6f)
                    : new Color(1f, 1f, 1f, 0.15f);

                // 콜라이더 Bounds 기준 WireCube 표시
                Gizmos.DrawWireCube(
                    _hitboxes[i].bounds.center,
                    _hitboxes[i].bounds.size);

                // 인덱스 레이블
                string label = i switch
                {
                    HitboxCombo1 => "Combo1 횡베기",
                    HitboxCombo2 => "Combo2 내리찍기",
                    HitboxCombo3 => "Combo3 찌르기",
                    HitboxCharge => "Charge 강타",
                    _ => $"Hitbox[{i}]"
                };

                UnityEditor.Handles.Label(
                    _hitboxes[i].bounds.center + Vector3.up * 0.2f,
                    $"[{label}]" + (isActive ? " ◀ 활성" : ""));
            }
        }
#endif
    }
}