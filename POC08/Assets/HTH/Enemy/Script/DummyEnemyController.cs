// ============================================================
// DummyEnemyController.cs  v1.0
// 봉인 시스템 테스트용 더미 적 컨트롤러
//
// [역할]
//   SealableComponent 를 직접 제어하여 봉인 시스템 테스트.
//   Inspector 의 _gaugePercent (0~100) 슬라이더로 봉인도 수동 설정.
//   봉인 완료 시 주변 DummyEnemy 에게 연쇄 봉인 집행 신호 전달.
//
// [부착 구성]
//   DummyEnemy (루트)
//     [DummyEnemyController]     ← 이 컴포넌트
//     [SealableComponent]        ← 봉인도 관리 (grade=Normal, sealRange/sealHoldTime 설정)
//     [SealReadyNotifier]        ← 집행 가능 범위 원 표시 (자동)
//     [SealExecutionEffect]      ← Arc 게이지 연출 (자동)
//     [SpriteRenderer]           ← 시각 표시
//     [CircleCollider2D]         ← HurtBox
//
// [SealableComponent 단독 동작 설정]
//   grade = Normal
//   _sealRange : Inspector 직접 설정 (플레이어 집행 가능 거리)
//   _sealHoldTime : Inspector 직접 설정 (홀드 유지 시간)
//   _maxGauge: DummyEnemyController._maxGauge 와 동일하게 설정
//   BossDataSO 불필요 → SealableComponent.Initialize 호출 안 해도 됨
//   SealableComponent Awake 에서 _isGaugeActive = true 자동 설정됨 (isDilPhaseOnly=false)
//
// [Inspector 슬라이더]
//   _gaugePercent [Range(0, 100)]
//   → OnValidate() + Update() 에서 실시간 반영
//   → 내부적으로 ForceRelease 후 AddGauge 조합으로 봉인도 설정
//
// [연쇄 봉인]
//   봉인 완료 시 _chainRadius 범위 내 다른 DummyEnemyController 탐색
//   → 미봉인 Dummy 에게 봉인도 100% 강제 설정
//   → 자동으로 OnSealRequested 발행 → SealExecutionRunner/SealExecutor 감지
//
// [독립 동작]
//   BossWardenCore / SealStateManager 없이 단독 동작 가능.
//   SealableComponent + SealReadyNotifier + SealExecutionEvent + SealExecutionRunner 만 있으면 됨.
//
// [씬 구성]
//   SystemRoot
//     [SealExecutionEvent]   ← 집행 목록 관리 (보스 없이 단독)
//     [SealExecutionRunner]  ← S키 홀드 집행
//   DummyEnemy_1 / 2 / 3 ... ← DummyEnemyController 부착
//
// [namespace] SEAL
// ============================================================

using System.Collections;
using UnityEngine;
using DG.Tweening;

namespace SEAL
{
    /// <summary>
    /// 봉인 시스템 테스트용 더미 적 컨트롤러. (v1.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [사용법]
    ///   1. DummyEnemy 오브젝트에 이 컴포넌트 부착
    ///   2. SealableComponent 도 함께 부착 (grade=Normal)
    ///   3. Inspector 에서 _gaugePercent 슬라이더 조절
    ///   4. 100% 도달 시 SealReadyNotifier 범위 원 자동 표시
    ///   5. 플레이어 S키 홀드 → 봉인 집행 → 연쇄 봉인 발동
    /// ────────────────────────────────────────────────────
    /// </summary>
    [RequireComponent(typeof(SealableComponent))]
    [RequireComponent(typeof(SpriteRenderer))]
    public class DummyEnemyController : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        // Inspector — 봉인도 슬라이더
        // ══════════════════════════════════════════════════════

        [Header("── 봉인도 설정 ──────────────────────")]

        /// <summary>
        /// 봉인도 퍼센트 (0 ~ 100).
        /// Inspector 슬라이더로 실시간 조절 가능.
        /// 100 으로 설정 시 SealableComponent.OnSealRequested 자동 발행.
        /// </summary>
        [Tooltip("봉인도 0~100%. Inspector 슬라이더로 실시간 조절.")]
        [Range(0f, 100f)]
        [SerializeField] private float _gaugePercent = 0f;

        /// <summary>
        /// 봉인도 최대값.
        /// SealableComponent._maxGauge 와 반드시 동일하게 설정.
        /// 권장: 100
        /// </summary>
        [Tooltip("봉인도 최대치. SealableComponent._maxGauge 와 동일하게 설정. 권장: 100.")]
        [Min(1f)]
        [SerializeField] private float _maxGauge = 100f;

        [Header("── 연쇄 봉인 설정 ──────────────────────")]

        /// <summary>
        /// 연쇄 봉인 반경 (units).
        /// 봉인 완료 시 이 범위 내 다른 DummyEnemy 에게 봉인도 100% 주입.
        /// 0 이면 연쇄 봉인 없음.
        /// </summary>
        [Tooltip("연쇄 봉인 반경. 봉인 완료 시 이 범위 내 Dummy 에게 봉인도 100% 주입. 0=없음.")]
        [Min(0f)]
        [SerializeField] private float _chainRadius = 5f;

        /// <summary>
        /// 연쇄 봉인 대상 레이어 마스크.
        /// DummyEnemy 오브젝트가 있는 레이어 선택.
        /// </summary>
        [Tooltip("연쇄 봉인 대상 레이어. DummyEnemy 레이어 선택.")]
        [SerializeField] private LayerMask _dummyLayer;

        /// <summary>
        /// 연쇄 봉인 각 Dummy 주입 딜레이 (초).
        /// 0 이면 즉시 전체 주입.
        /// </summary>
        [Tooltip("연쇄 봉인 주입 간격 (초). 0=즉시.")]
        [Min(0f)]
        [SerializeField] private float _chainDelay = 0.15f;

        [Header("── 시각 피드백 색상 ──────────────────────")]

        /// <summary>봉인도 0% 기본 색상.</summary>
        [Tooltip("봉인도 0% 기본 색상.")]
        [SerializeField] private Color _colorIdle = new Color(0.55f, 0.55f, 0.55f);

        /// <summary>봉인도 100% 집행 대기 색상.</summary>
        [Tooltip("봉인도 100% 집행 대기 색상.")]
        [SerializeField] private Color _colorReady = new Color(0.3f, 0.5f, 1f);

        /// <summary>봉인 완료 색상.</summary>
        [Tooltip("봉인 완료 색상.")]
        [SerializeField] private Color _colorSealed = new Color(0.1f, 0.1f, 0.45f);

        /// <summary>연쇄 봉인 수신 플래시 색상 (노란색).</summary>
        [Tooltip("연쇄 봉인 수신 플래시 색상.")]
        [SerializeField] private Color _colorChainFlash = new Color(1f, 0.85f, 0.1f);

        // ══════════════════════════════════════════════════════
        // 컴포넌트 참조
        // ══════════════════════════════════════════════════════

        /// <summary>봉인도 관리 컴포넌트.</summary>
        private SealableComponent _sealable;

        /// <summary>색상 표시 SpriteRenderer.</summary>
        private SpriteRenderer _renderer;

        // ══════════════════════════════════════════════════════
        // 내부 상태
        // ══════════════════════════════════════════════════════

        /// <summary>마지막으로 적용한 봉인도 퍼센트 (중복 방지).</summary>
        private float _lastAppliedPercent = -1f;

        /// <summary>색상 Tween 핸들.</summary>
        private Tweener _colorTween;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            _sealable = GetComponent<SealableComponent>();
            _renderer = GetComponent<SpriteRenderer>();

            if (_renderer != null)
                _renderer.color = _colorIdle;
        }

        private void Start()
        {
            // 이벤트 구독
            _sealable.OnSealCompleted += HandleSealCompleted;
            _sealable.OnGaugeChanged += HandleGaugeChanged;

            // 초기 봉인도 적용
            ApplyGaugePercent(_gaugePercent);

            Debug.Log($"[DummyEnemyController] {gameObject.name} 초기화 | MaxGauge:{_maxGauge}");
        }

        private void OnDestroy()
        {
            _colorTween?.Kill();

            if (_sealable != null)
            {
                _sealable.OnSealCompleted -= HandleSealCompleted;
                _sealable.OnGaugeChanged -= HandleGaugeChanged;
            }
        }

        /// <summary>
        /// Inspector 슬라이더 변경 시 자동 호출 (플레이 모드 전용).
        /// </summary>
        private void OnValidate()
        {
            if (!Application.isPlaying) return;
            if (_sealable == null) return;
            if (Mathf.Abs(_gaugePercent - _lastAppliedPercent) > 0.01f)
                ApplyGaugePercent(_gaugePercent);
        }

        // ══════════════════════════════════════════════════════
        // 봉인도 적용
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 봉인도 퍼센트를 SealableComponent 에 적용한다.
        ///
        /// [동작 방식]
        ///   현재 봉인도보다 낮은 값 → ForceRelease 후 AddGauge
        ///   현재 봉인도보다 높은 값 → AddGauge 로 차이만큼 추가
        ///   봉인 완료 상태 → 무시
        /// </summary>
        private void ApplyGaugePercent(float percent)
        {
            if (_sealable == null) return;
            if (_sealable.IsSealed) return;

            _lastAppliedPercent = percent;

            float targetGauge = _maxGauge * (percent / 100f);
            float currentGauge = _maxGauge * (_sealable.UIPercent / 100f);
            float delta = targetGauge - currentGauge;

            if (delta < -0.01f)
            {
                // 낮아진 경우 → ForceRelease 후 재설정
                _sealable.ForceRelease(resetSealCount: false);
                currentGauge = 0f;
                delta = targetGauge;
            }

            if (delta > 0.01f)
                _sealable.AddGauge(delta);
        }

        // ══════════════════════════════════════════════════════
        // 이벤트 핸들러
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// SealableComponent.OnGaugeChanged 수신.
        /// 봉인도 비율에 따라 색상 보간.
        /// </summary>
        private void HandleGaugeChanged(float uiPercent)
        {
            if (_renderer == null) return;

            float t = uiPercent / 100f;
            Color target = Color.Lerp(_colorIdle, _colorReady, t);

            _colorTween?.Kill();
            _colorTween = _renderer.DOColor(target, 0.1f).SetUpdate(true);
        }

        /// <summary>
        /// SealableComponent.OnSealCompleted 수신.
        /// 봉인 완료 색상 적용 + 연쇄 봉인 발동.
        /// </summary>
        private void HandleSealCompleted()
        {
            _colorTween?.Kill();
            _renderer?.DOColor(_colorSealed, 0.3f).SetUpdate(true);

            _gaugePercent = 100f;
            _lastAppliedPercent = 100f;

            if (_chainRadius > 0f)
                StartCoroutine(TriggerChainSeal());

            Debug.Log($"[DummyEnemyController] ✅ {gameObject.name} 봉인 완료 → 연쇄 봉인 시작");
        }

        // ══════════════════════════════════════════════════════
        // 연쇄 봉인
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 연쇄 봉인 코루틴.
        /// _chainRadius 범위 내 DummyEnemy 를 거리 순으로 탐색
        /// → 미봉인 + 집행 대기 아닌 대상에게 봉인도 100% 주입.
        /// </summary>
        private IEnumerator TriggerChainSeal()
        {
            Collider2D[] hits = Physics2D.OverlapCircleAll(
                transform.position, _chainRadius, _dummyLayer);

            // 거리 기준 오름차순 정렬
            System.Array.Sort(hits, (a, b) =>
            {
                float da = Vector2.Distance(transform.position, a.transform.position);
                float db = Vector2.Distance(transform.position, b.transform.position);
                return da.CompareTo(db);
            });

            int count = 0;
            foreach (var hit in hits)
            {
                if (hit.gameObject == gameObject) continue;

                var other = hit.GetComponent<DummyEnemyController>();
                if (other == null) continue;
                if (other._sealable == null) continue;
                if (other._sealable.IsSealed) continue;
                if (other._sealable.IsSealReady) continue; // 이미 집행 대기 중이면 스킵

                // 연쇄 플래시 + 봉인도 100% 주입
                other.PlayChainFlash();
                other.SetGaugePercent(100f);
                count++;

                Debug.Log($"[DummyEnemyController] ⚡ 연쇄: {other.gameObject.name}");

                if (_chainDelay > 0f)
                    yield return new WaitForSecondsRealtime(_chainDelay);
            }

            Debug.Log($"[DummyEnemyController] 연쇄 봉인 완료: {count}개");
        }

        // ══════════════════════════════════════════════════════
        // 외부 API
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 봉인도 퍼센트 외부 설정.
        /// 연쇄 봉인 수신 시 다른 DummyEnemyController 에서 호출.
        /// </summary>
        public void SetGaugePercent(float percent)
        {
            _gaugePercent = Mathf.Clamp(percent, 0f, 100f);
            ApplyGaugePercent(_gaugePercent);
        }

        /// <summary>
        /// 연쇄 봉인 수신 플래시 연출.
        /// 노란 플래시 후 현재 봉인도 색상으로 복귀.
        /// </summary>
        public void PlayChainFlash()
        {
            if (_renderer == null) return;
            _colorTween?.Kill();
            _renderer.color = _colorChainFlash;
            float t = _gaugePercent / 100f;
            _colorTween = _renderer
                .DOColor(Color.Lerp(_colorIdle, _colorReady, t), 0.25f)
                .SetUpdate(true);
        }

        // ══════════════════════════════════════════════════════
        // Gizmos
        // ══════════════════════════════════════════════════════

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // 연쇄 봉인 범위
            Gizmos.color = new Color(1f, 0.85f, 0.1f, 0.25f);
            Gizmos.DrawWireSphere(transform.position, _chainRadius);

            UnityEditor.Handles.color = Color.white;
            UnityEditor.Handles.Label(
                transform.position + Vector3.up * 1.3f,
                $"{gameObject.name}\n봉인도: {_gaugePercent:F0}%");
        }
#endif
    }
}