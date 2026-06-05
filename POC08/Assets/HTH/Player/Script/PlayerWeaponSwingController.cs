// ============================================================
// PlayerWeaponSwingController.cs  v2.0
// 탑뷰 무기 스윙 DOTween 연출 전담 컴포넌트
//
// [v2.0 변경 — 강공격 관련 전부 제거]
//   제거:
//     PlayChargeSwing() / ChargeSwingRoutine() / BuildChargeSequence()
//     ComboIndex.Charge 열거값
//     _data.ChargeXxx 참조 전부
//     ChargeHitStopDuration 참조
//
//   유지:
//     StartChargePulse() / StopChargePulse()
//     → 추후 다른 메커니즘에서 활용 가능성 있으므로 유지
//
// [v1.3 — SetUpdate(true) + HitStopRoutine 단순화]
// [v1.2 — onHit 콜백 제거, HitboxManager 직접 제어]
// [v1.1 — ArcPath 호 궤적 + 8방향 적용]
//
// [스윙 흐름]
//   1. WeaponPivot 을 공격 방향 각도로 즉시 회전
//   2. Weapon 을 원점으로 스냅
//   3. BuildComboSequence:
//      백스윙 → 히트박스 활성 → 타격(호 궤적) → 히트박스 비활성 → 복귀
//   4. 복귀 직전 OnReturnStart 발행
//   5. IsSwinging = false
//
// [namespace]
//   namespace : SEAL
// ============================================================

using System;
using System.Collections;
using UnityEngine;
using DG.Tweening;

namespace SEAL
{
    /// <summary>
    /// 탑뷰 무기 스윙 DOTween 연출 전담 컴포넌트. (v2.0)
    /// 강공격 제거. PlaySwing(콤보) 만 사용.
    /// </summary>
    public class PlayerWeaponSwingController : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        // 콤보 인덱스
        // ══════════════════════════════════════════════════════

        /// <summary>콤보 단계 식별 열거형. Charge 제거됨.</summary>
        public enum ComboIndex
        {
            /// <summary>1콤보 — 횡베기.</summary>
            Combo1 = 0,
            /// <summary>2콤보 — 내리찍기.</summary>
            Combo2 = 1,
            /// <summary>3콤보 — 찌르기 피니셔.</summary>
            Combo3 = 2,
        }

        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── 오브젝트 연결 ──────────────────────")]

        /// <summary>무기 피벗 Transform. 공격 방향으로 Z회전. WeaponPivot 연결.</summary>
        [Tooltip("무기 피벗 Transform. 공격 방향으로 Z회전. WeaponPivot 연결.")]
        [SerializeField] private Transform _weaponPivot;

        /// <summary>무기 오브젝트 Transform. DOLocalMove/DOLocalRotate 대상. Weapon 연결.</summary>
        [Tooltip("무기 오브젝트 Transform. DOLocalMove/DOLocalRotate 대상. Weapon 연결.")]
        [SerializeField] private Transform _weapon;

        /// <summary>Visual Transform. Lunge 연출 대상. 미연결 시 자신 사용.</summary>
        [Tooltip("Visual Transform. Lunge 연출 대상. 미연결 시 자신 사용.")]
        [SerializeField] private Transform _visualTransform;

        [Header("── 데이터 SO ──────────────────────")]

        /// <summary>공격 수치 SO. PlayerAttackDataSO 연결 필수.</summary>
        [Tooltip("공격 수치 SO. PlayerAttackDataSO 연결 필수.")]
        [SerializeField] private PlayerAttackDataSO _data;

        [Header("── 히트박스 매니저 ──────────────────────")]

        /// <summary>히트박스 활성/비활성 담당. null 이면 Awake 에서 자동 탐색.</summary>
        [Tooltip("히트박스 관리 컴포넌트. null=Awake 자동 탐색.")]
        [SerializeField] private PlayerAttackHitboxManager _hitboxManager;

        // ══════════════════════════════════════════════════════
        // 내부 상태
        // ══════════════════════════════════════════════════════

        /// <summary>현재 스윙 진행 중 여부.</summary>
        private bool _isSwinging;

        /// <summary>현재 실행 중인 DOTween Sequence.</summary>
        private Sequence _swingSequence;

        /// <summary>스윙 코루틴 참조.</summary>
        private Coroutine _swingCoroutine;

        /// <summary>강공격 홀드 맥동 Tween 참조. (강공격 제거 후에도 API 유지)</summary>
        private Tween _chargePulseTween;

        /// <summary>Weapon 원점 로컬 위치. Awake 캐싱.</summary>
        private Vector3 _weaponOriginLocalPos;

        /// <summary>Weapon 원점 스케일. Awake 캐싱.</summary>
        private Vector3 _weaponOriginScale;

        /// <summary>히트스톱 코루틴 참조.</summary>
        private Coroutine _hitStopCoroutine;

        // ══════════════════════════════════════════════════════
        // 이벤트
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 복귀 구간 시작 직전 발행.
        /// PlayerAttackController 에서 구독 → _canChangeDir = true 설정.
        /// </summary>
        public event Action OnReturnStart;

        // ══════════════════════════════════════════════════════
        // 프로퍼티
        // ══════════════════════════════════════════════════════

        /// <summary>현재 스윙 중 여부.</summary>
        public bool IsSwinging => _isSwinging;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            if (_visualTransform == null)
                _visualTransform = transform;

            if (_weapon != null)
            {
                _weaponOriginLocalPos = _weapon.localPosition;
                _weaponOriginScale = _weapon.localScale;
            }

            if (_hitboxManager == null)
                _hitboxManager = GetComponent<PlayerAttackHitboxManager>();

            if (_hitboxManager == null)
                Debug.LogWarning("[PlayerWeaponSwingController] PlayerAttackHitboxManager 미연결.");
            if (_data == null)
                Debug.LogError("[PlayerWeaponSwingController] PlayerAttackDataSO 미연결.");
            if (_weaponPivot == null)
                Debug.LogError("[PlayerWeaponSwingController] WeaponPivot 미연결.");
            if (_weapon == null)
                Debug.LogError("[PlayerWeaponSwingController] Weapon 미연결.");
        }

        private void OnDestroy()
        {
            _swingSequence?.Kill();
            _chargePulseTween?.Kill();
            if (_hitStopCoroutine != null) Time.timeScale = 1f;
        }

        // ══════════════════════════════════════════════════════
        // 외부 API
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 콤보 스윙 실행.
        /// PlayerAttackController.ComboRoutine 에서 호출.
        /// </summary>
        /// <param name="combo">콤보 단계 (Combo1/2/3).</param>
        /// <param name="attackDir">공격 방향 (정규화 Vector2).</param>
        /// <param name="sealAmount">이 콤보의 봉인도 누적량.</param>
        public void PlaySwing(ComboIndex combo, Vector2 attackDir, float sealAmount)
        {
            if (_data == null || _weapon == null || _weaponPivot == null) return;

            CancelSwing();
            _swingCoroutine = StartCoroutine(SwingRoutine(combo, attackDir, sealAmount));
        }

        /// <summary>
        /// 강공격 홀드 맥동 시작. (강공격 제거 후에도 API 유지)
        /// 향후 다른 메커니즘 활용 가능.
        /// </summary>
        public void StartChargePulse()
        {
            // 강공격 제거로 현재 사용 안 함 — API 유지
        }

        /// <summary>강공격 홀드 맥동 종료.</summary>
        public void StopChargePulse()
        {
            _chargePulseTween?.Kill();
            _chargePulseTween = null;

            if (_weapon != null)
                _weapon.localScale = _weaponOriginScale;
        }

        /// <summary>
        /// 스윙 즉시 중단 + 무기 원점 복귀.
        /// CancelAttack / 봉인 집행 / 피격 시 호출.
        /// </summary>
        public void CancelSwing()
        {
            _swingSequence?.Kill();

            if (_swingCoroutine != null)
            {
                StopCoroutine(_swingCoroutine);
                _swingCoroutine = null;
            }

            _isSwinging = false;
            SnapWeaponToOrigin();
        }

        /// <summary>
        /// 이동 방향으로 WeaponPivot 즉시 회전.
        /// IsSwinging = false 일 때 ObjectDirectionController 가 호출.
        /// </summary>
        public void UpdatePivotToFacing(Vector2 facingDir)
        {
            if (_weaponPivot == null) return;
            if (facingDir.sqrMagnitude < 0.001f) return;

            float angle = Mathf.Atan2(facingDir.y, facingDir.x) * Mathf.Rad2Deg;
            _weaponPivot.rotation = Quaternion.Euler(0f, 0f, angle);
        }

        // ══════════════════════════════════════════════════════
        // 스윙 코루틴
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 기본 공격 스윙 코루틴.
        /// Combo3 피니셔 히트스톱 포함.
        /// </summary>
        private IEnumerator SwingRoutine(ComboIndex combo, Vector2 attackDir, float sealAmount)
        {
            _isSwinging = true;

            _hitboxManager?.DisableAllHitboxes();
            RotatePivotToAttackDir(attackDir);
            SnapWeaponToOrigin();

            bool done = false;
            _swingSequence = BuildComboSequence(combo, sealAmount);
            _swingSequence.OnComplete(() => done = true);
            _swingSequence.Play();

            yield return new WaitUntil(() => done);

            _hitboxManager?.DisableAllHitboxes();

            // Combo3 피니셔 히트스톱
            if (combo == ComboIndex.Combo3 && _data.Combo3HitStopDuration > 0f)
            {
                if (_hitStopCoroutine != null) StopCoroutine(_hitStopCoroutine);
                _hitStopCoroutine = StartCoroutine(
                    HitStopRoutine(_data.Combo3HitStopDuration));
                yield return _hitStopCoroutine;
            }

            _isSwinging = false;
            _swingCoroutine = null;
        }

        // ══════════════════════════════════════════════════════
        // Sequence 빌더
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 콤보별 DOTween Sequence 생성.
        /// Combo1(횡베기) / Combo2(내리찍기) / Combo3(찌르기 피니셔).
        /// </summary>
        private Sequence BuildComboSequence(ComboIndex combo, float sealAmount)
        {
            Sequence seq = DOTween.Sequence();
            seq.SetUpdate(true);

            float bD = _data.BackswingDuration;
            float aD = _data.AttackDuration;
            float rD = _data.ReturnDuration;

            Vector3 origin = _weaponOriginLocalPos;

            switch (combo)
            {
                case ComboIndex.Combo1:
                    {
                        Vector3 backPos = ToV3(_data.Combo1BackPos);
                        Vector3 atkPos = ToV3(_data.Combo1AttackPos);
                        float rotBack = _data.Combo1RotBack;
                        float rotAtk = _data.Combo1RotAtk;
                        float delta = CalcSwingDelta(rotBack, rotAtk, _data.Combo1Clockwise);

                        // ① 백스윙
                        seq.Append(_weapon.DOLocalMove(backPos, bD).SetEase(Ease.OutQuart).SetUpdate(true));
                        seq.Join(RotateWeapon(rotBack, bD, Ease.OutQuart));

                        // ② 히트박스 활성
                        seq.AppendCallback(() => _hitboxManager?.EnableHitbox(0, sealAmount));

                        // ③ 타격 (호 궤적)
                        seq.Append(ArcPath(backPos, atkPos, _data.Combo1ArcHeight, aD, Ease.InOutCubic));
                        seq.Join(RotateWeaponDelta(delta, aD, Ease.InOutCubic));

                        // ④ 히트박스 비활성
                        seq.AppendCallback(() => _hitboxManager?.DisableAllHitboxes());

                        // ⑤ 복귀 시작 알림 + 복귀
                        seq.AppendCallback(() => OnReturnStart?.Invoke());
                        seq.Append(_weapon.DOLocalMove(origin, rD).SetEase(Ease.OutQuart).SetUpdate(true));
                        seq.Join(RotateWeapon(0f, rD, Ease.OutQuart));
                        break;
                    }

                case ComboIndex.Combo2:
                    {
                        Vector3 backPos = ToV3(_data.Combo2BackPos);
                        Vector3 atkPos = ToV3(_data.Combo2AttackPos);
                        float rotBack = _data.Combo2RotBack;
                        float rotAtk = _data.Combo2RotAtk;
                        float delta = CalcSwingDelta(rotBack, rotAtk, _data.Combo2Clockwise);

                        seq.Append(_weapon.DOLocalMove(backPos, bD).SetEase(Ease.OutQuart).SetUpdate(true));
                        seq.Join(RotateWeapon(rotBack, bD, Ease.OutQuart));

                        seq.AppendCallback(() => _hitboxManager?.EnableHitbox(1, sealAmount));

                        seq.Append(ArcPath(backPos, atkPos, _data.Combo2ArcHeight, aD, Ease.InCubic));
                        seq.Join(RotateWeaponDelta(delta, aD, Ease.InCubic));

                        seq.AppendCallback(() => _hitboxManager?.DisableAllHitboxes());

                        seq.AppendCallback(() => OnReturnStart?.Invoke());
                        seq.Append(_weapon.DOLocalMove(origin, rD).SetEase(Ease.OutBounce).SetUpdate(true));
                        seq.Join(RotateWeapon(0f, rD, Ease.OutQuart));
                        break;
                    }

                case ComboIndex.Combo3:
                    {
                        Vector3 backPos = ToV3(_data.Combo3BackPos);
                        Vector3 atkPos = ToV3(_data.Combo3AttackPos);

                        // 찌르기 — 직선 이동
                        seq.Append(_weapon.DOLocalMove(backPos, bD).SetEase(Ease.OutQuart).SetUpdate(true));

                        seq.AppendCallback(() => _hitboxManager?.EnableHitbox(2, sealAmount));

                        seq.Append(_weapon.DOLocalMove(atkPos, aD).SetEase(Ease.OutExpo).SetUpdate(true));

                        seq.AppendCallback(() => _hitboxManager?.DisableAllHitboxes());

                        seq.AppendCallback(() => OnReturnStart?.Invoke());
                        seq.Append(_weapon.DOLocalMove(origin, rD).SetEase(Ease.OutBack).SetUpdate(true));
                        break;
                    }
            }

            return seq;
        }

        // ══════════════════════════════════════════════════════
        // 히트스톱
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 히트스톱 코루틴.
        /// Time.timeScale 낮춤 → WaitForSecondsRealtime 대기 → 복귀.
        /// </summary>
        private IEnumerator HitStopRoutine(float duration)
        {
            Time.timeScale = _data.HitStopTimeScale;
            yield return new WaitForSecondsRealtime(duration);
            Time.timeScale = 1f;
            _hitStopCoroutine = null;
        }

        // ══════════════════════════════════════════════════════
        // 보조 메서드
        // ══════════════════════════════════════════════════════

        /// <summary>WeaponPivot 을 공격 방향 각도로 즉시 회전.</summary>
        private void RotatePivotToAttackDir(Vector2 attackDir)
        {
            if (_weaponPivot == null || attackDir.sqrMagnitude < 0.001f) return;

            float angle = Mathf.Atan2(attackDir.y, attackDir.x) * Mathf.Rad2Deg;
            _weaponPivot.rotation = Quaternion.Euler(0f, 0f, angle);
        }

        /// <summary>Weapon 을 원점으로 즉시 스냅.</summary>
        private void SnapWeaponToOrigin()
        {
            if (_weapon == null) return;
            _weapon.localPosition = _weaponOriginLocalPos;
            _weapon.localRotation = Quaternion.identity;
            _weapon.localScale = _weaponOriginScale;
        }

        /// <summary>Weapon Z 절대 회전 Tween.</summary>
        private Tweener RotateWeapon(float zAngle, float duration, Ease ease)
        {
            return _weapon
                .DOLocalRotate(new Vector3(0f, 0f, zAngle), duration, RotateMode.Fast)
                .SetEase(ease)
                .SetUpdate(true);
        }

        /// <summary>Weapon Z 델타 회전 Tween (LocalAxisAdd).</summary>
        private Tweener RotateWeaponDelta(float delta, float duration, Ease ease)
        {
            return _weapon
                .DOLocalRotate(new Vector3(0f, 0f, delta), duration, RotateMode.LocalAxisAdd)
                .SetEase(ease)
                .SetUpdate(true);
        }

        /// <summary>
        /// 호(arc) 궤적 경로 Tween.
        /// backPos → controlPoint → atkPos 3점 CatmullRom 경로.
        /// </summary>
        private Tweener ArcPath(Vector3 from, Vector3 to, float arcHeight, float duration, Ease ease)
        {
            Vector3 mid = (from + to) * 0.5f;
            Vector3 dir = (to - from).normalized;
            Vector3 perp = Vector3.Cross(dir, Vector3.forward).normalized;
            Vector3 ctrl = mid + perp * arcHeight;

            Vector3[] path = { from, ctrl, to };

            return _weapon
                .DOLocalPath(path, duration, PathType.CatmullRom, PathMode.Ignore)
                .SetEase(ease)
                .SetUpdate(true);
        }

        /// <summary>
        /// 회전 방향에 따른 스윙 델타 계산.
        /// Clockwise = true  → delta 를 음수로 (시계)
        /// Clockwise = false → delta 를 양수로 (반시계)
        /// </summary>
        private static float CalcSwingDelta(float rotBack, float rotAtk, bool clockwise)
        {
            float delta = rotAtk - rotBack;

            if (clockwise && delta > 0f) delta -= 360f;
            if (!clockwise && delta < 0f) delta += 360f;

            return delta;
        }

        /// <summary>Vector2 → Vector3 변환 (Z=0).</summary>
        private static Vector3 ToV3(Vector2 v) => new Vector3(v.x, v.y, 0f);
    }
}