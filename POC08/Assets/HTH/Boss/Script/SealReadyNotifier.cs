// ============================================================
// SealReadyNotifier.cs  v1.0
// 봉인 집행 가능 신호 전달 + 범위 표시 요청 컴포넌트
//
// [역할]
//   SealableComponent 와 SealExecutor 사이의 중계자.
//   단일 책임: "이 부위가 집행 가능 상태가 됐다" 는 신호만 전달.
//
//   SealableComponent 가 봉인도 100% 도달 시
//   OnSealRequested 를 직접 SealExecutor 에 발행하지 않고
//   이 컴포넌트를 통해 범위 표시도 함께 처리.
//
// [SealableComponent 와의 역할 분리]
//   SealableComponent  : 봉인도 수치 + 색상 변화만 담당
//   SealReadyNotifier  : 집행 가능 신호 전달 + 범위 표시 요청만 담당
//   → 단일 책임 원칙 유지
//
// [범위 표시 방식]
//   기존: SealExecutor 가 직접 BossWardenAttackRange 를 참조
//   변경: SealReadyNotifier 가 자기 SpriteRenderer 로
//         집행 가능 원형 범위를 직접 표시 (LineRenderer 또는 SpriteRenderer)
//
//   [두 가지 표시 방법]
//   A. SealExecutor 에 있는 BossWardenAttackRange 를 통해 요청
//      → 기존 구조 유지 (ShowSealRange / ShowCoreRange 호출)
//   B. 자기 자식 LineRenderer 로 직접 범위 원 그리기
//      → 부위별 독립 표시 가능 (부위가 이동해도 자동 추적)
//
//   이 컴포넌트는 B 방식 채택:
//   자기 자식 LineRenderer 로 직접 그림 → 부위 이동 시 자동 추적
//   SealExecutor 와 BossWardenAttackRange 에 대한 의존성 없음
//
// [이벤트 흐름]
//   SealableComponent.OnSealRequested
//     → SealReadyNotifier.HandleSealRequested()
//       → LineRenderer 범위 원 활성 + 맥동 시작
//       → OnReadyToExecute 발행 → SealExecutor 가 구독
//
//   SealableComponent.OnSealCompleted
//     → SealReadyNotifier.HandleSealCompleted()
//       → LineRenderer 범위 원 비활성
//
//   SealableComponent.OnForceReleased
//     → SealReadyNotifier.HandleForceReleased()
//       → LineRenderer 범위 원 비활성
//
// [부착 위치]
//   SealableComponent 와 같은 오브젝트에 부착.
//   Boss_LeftArm / Boss_RightArm / Boss_Core
//
// [namespace] SEAL
// ============================================================

using System;
using UnityEngine;
using DG.Tweening;

namespace SEAL
{
    /// <summary>
    /// 봉인 집행 가능 신호 전달 + 범위 표시 컴포넌트. (v1.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [동작 흐름]
    ///   Awake: SealableComponent + BossDataSO 자동 탐색
    ///   OnSealRequested 수신
    ///     → 범위 원 LineRenderer 활성
    ///     → 범위 원 맥동 (Scale DOTween Yoyo Loop)
    ///     → OnReadyToExecute 발행 → SealExecutor 구독
    ///   OnSealCompleted / OnForceReleased 수신
    ///     → 범위 원 비활성
    ///     → 맥동 종료
    ///
    /// [범위 원 구성]
    ///   자기 자식 오브젝트에 LineRenderer 부착.
    ///   SealableComponent.SealRange 반경으로 원 그리기.
    ///   SealColorDataSO.colorSealRange / colorCoreRange 색상 적용.
    ///   부위가 이동해도 자식으로 붙어 있어 자동 추적.
    /// ────────────────────────────────────────────────────
    /// </summary>
    [RequireComponent(typeof(SealableComponent))]
    public class SealReadyNotifier : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── 범위 원 LineRenderer ──────────────────────")]

        /// <summary>
        /// 봉인 집행 가능 범위 원 LineRenderer.
        /// 자기 자식 오브젝트에 부착된 LineRenderer 연결.
        /// 미연결 시 Awake 에서 자동 생성.
        ///
        /// [씬 구성 예시]
        ///   Boss_LeftArm
        ///     ├─ SealableComponent
        ///     ├─ SealReadyNotifier
        ///     └─ SealRangeCircle [LineRenderer]  ← 여기 연결
        /// </summary>
        [Tooltip("집행 가능 범위 원 LineRenderer. 미연결 시 자동 생성.")]
        [SerializeField] private LineRenderer _rangeCircle;

        [Header("── 원 그리기 수치 ──────────────────────")]

        /// <summary>
        /// 범위 원 선 두께.
        ///
        /// [권장값] 0.05
        /// </summary>
        [Tooltip("범위 원 선 두께. 권장: 0.05.")]
        [Min(0.01f)]
        [SerializeField] private float _lineWidth = 0.05f;

        /// <summary>
        /// 범위 원 분할 수. 클수록 더 부드러운 원.
        ///
        /// [권장값] 32
        /// </summary>
        [Tooltip("범위 원 분할 수. 클수록 부드러운 원. 권장: 32.")]
        [Range(8, 64)]
        [SerializeField] private int _segments = 32;

        [Header("── 맥동 수치 ──────────────────────")]

        /// <summary>
        /// 집행 가능 상태 맥동 배율.
        /// 범위 원 Scale 이 1.0 ↔ (1.0 + pulseAmount) 반복.
        ///
        /// [권장값] 0.1
        /// </summary>
        [Tooltip("맥동 Scale 배율. 1.0 ↔ (1.0 + 이 값) 반복. 권장: 0.1.")]
        [Range(0f, 0.5f)]
        [SerializeField] private float _pulseAmount = 0.1f;

        // ══════════════════════════════════════════════════════
        // 컴포넌트 참조
        // ══════════════════════════════════════════════════════

        /// <summary>같은 오브젝트의 SealableComponent.</summary>
        private SealableComponent _sealable;

        /// <summary>BossDataSO. SealableComponent 에서 가져옴.</summary>
        private BossDataSO _bossData;

        // ══════════════════════════════════════════════════════
        // 내부 상태
        // ══════════════════════════════════════════════════════

        /// <summary>범위 원 맥동 Tween 참조.</summary>
        private Tweener _pulseTween;

        /// <summary>범위 원 현재 활성 여부.</summary>
        private bool _isRangeVisible;

        // ══════════════════════════════════════════════════════
        // 이벤트
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 집행 가능 상태 진입 시 발행.
        /// SealExecutor 가 구독 → 집행 대기 목록 등록.
        /// 파라미터: 자기 SealableComponent.
        /// </summary>
        public event Action<SealableComponent> OnReadyToExecute;

        /// <summary>
        /// 집행 가능 상태 해제 시 발행 (완료 or 강제 해제).
        /// SealExecutor 가 구독 → 집행 대기 목록에서 제거.
        /// 파라미터: 자기 SealableComponent.
        /// </summary>
        public event Action<SealableComponent> OnReadyCancelled;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            // SealableComponent 자동 탐색
            _sealable = GetComponent<SealableComponent>();
            if (_sealable == null)
            {
                Debug.LogError($"[SealReadyNotifier] {gameObject.name} — SealableComponent 미부착.");
                enabled = false;
                return;
            }

            // LineRenderer 자동 생성 (미연결 시)
            if (_rangeCircle == null)
                _rangeCircle = CreateRangeCircle();

            // 초기 비활성
            if (_rangeCircle != null)
                _rangeCircle.gameObject.SetActive(false);
        }

        private void Start()
        {
            // SealableComponent 이벤트 구독
            _sealable.OnSealRequested -= HandleSealRequested;
            _sealable.OnSealRequested += HandleSealRequested;
            _sealable.OnSealCompleted -= HandleSealCompleted;
            _sealable.OnSealCompleted += HandleSealCompleted;
            _sealable.OnForceReleased -= HandleForceReleased;
            _sealable.OnForceReleased += HandleForceReleased;
        }

        private void OnDestroy()
        {
            _pulseTween?.Kill();

            if (_sealable != null)
            {
                _sealable.OnSealRequested -= HandleSealRequested;
                _sealable.OnSealCompleted -= HandleSealCompleted;
                _sealable.OnForceReleased -= HandleForceReleased;
            }
        }

        // ══════════════════════════════════════════════════════
        // 이벤트 핸들러
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// SealableComponent.OnSealRequested 수신.
        /// 봉인도 100% 도달 → 범위 원 활성 + 맥동 시작 + OnReadyToExecute 발행.
        /// </summary>
        private void HandleSealRequested(SealableComponent sealable)
        {
            ShowRange();
            StartPulse();
            OnReadyToExecute?.Invoke(sealable);

            Debug.Log($"[SealReadyNotifier] ▶ {gameObject.name} 집행 가능 알림 발행 | " +
                      $"등급:{_sealable.Grade}");
        }

        /// <summary>
        /// SealableComponent.OnSealCompleted 수신.
        /// 봉인 집행 완료 → 범위 원 비활성 + OnReadyCancelled 발행.
        /// </summary>
        private void HandleSealCompleted()
        {
            HideRange();
            OnReadyCancelled?.Invoke(_sealable);

            Debug.Log($"[SealReadyNotifier] ✅ {gameObject.name} 집행 완료 → 범위 원 숨김");
        }

        /// <summary>
        /// SealableComponent.OnForceReleased 수신.
        /// 강제 해제 → 범위 원 비활성 + OnReadyCancelled 발행.
        /// </summary>
        private void HandleForceReleased()
        {
            HideRange();
            OnReadyCancelled?.Invoke(_sealable);

            Debug.Log($"[SealReadyNotifier] ■ {gameObject.name} 강제 해제 → 범위 원 숨김");
        }

        // ══════════════════════════════════════════════════════
        // 범위 원 표시 / 숨김
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 범위 원 활성 + 그리기.
        /// SealableComponent.SealRange 반경 사용.
        /// 등급에 따라 colorSealRange / colorCoreRange 적용.
        /// </summary>
        private void ShowRange()
        {
            if (_rangeCircle == null) return;

            // 반경
            float radius = _sealable.SealRange;

            // 색상 결정 (등급 기준)
            Color rangeColor = GetRangeColor();

            // 원 그리기
            DrawCircle(_rangeCircle, radius, rangeColor);

            _rangeCircle.gameObject.SetActive(true);
            _isRangeVisible = true;
        }

        /// <summary>
        /// 범위 원 비활성 + 맥동 종료.
        /// </summary>
        private void HideRange()
        {
            StopPulse();

            if (_rangeCircle == null) return;
            _rangeCircle.gameObject.SetActive(false);
            _isRangeVisible = false;
        }

        // ══════════════════════════════════════════════════════
        // 맥동
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 범위 원 맥동 시작.
        /// Transform Scale 을 1.0 ↔ (1.0 + _pulseAmount) DOTween Yoyo Loop.
        /// SealColorDataSO.sealReadyPulseDuration 사용.
        /// </summary>
        private void StartPulse()
        {
            if (_rangeCircle == null || _pulseAmount <= 0f) return;

            StopPulse();

            float duration = _bossData?.ColorData?.sealReadyPulseDuration ?? 0.4f;
            float targetScale = 1.0f + _pulseAmount;

            _pulseTween = _rangeCircle.transform
                .DOScale(targetScale, duration * 0.5f)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo)
                .SetUpdate(true);
        }

        /// <summary>맥동 Tween 종료 + Scale 원점 복귀.</summary>
        private void StopPulse()
        {
            _pulseTween?.Kill();
            _pulseTween = null;

            if (_rangeCircle != null)
                _rangeCircle.transform.localScale = Vector3.one;
        }

        // ══════════════════════════════════════════════════════
        // 내부 유틸
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 등급에 따른 범위 원 색상 반환.
        ///   Core   → colorCoreRange
        ///   Part   → colorSealRange
        ///   Normal → colorSealRange (기본)
        /// </summary>
        private Color GetRangeColor()
        {
            if (_bossData?.ColorData == null)
                return _sealable.Grade == SealGrade.Core ? Color.yellow : Color.cyan;

            return _sealable.Grade == SealGrade.Core
                ? _bossData.ColorData.colorCoreRange
                : _bossData.ColorData.colorSealRange;
        }

        /// <summary>
        /// LineRenderer 로 원을 그린다.
        /// _segments 개수만큼 점을 계산하여 닫힌 원 형태로 그림.
        /// </summary>
        /// <param name="lr">대상 LineRenderer.</param>
        /// <param name="radius">반경 (units).</param>
        /// <param name="color">선 색상.</param>
        private void DrawCircle(LineRenderer lr, float radius, Color color)
        {
            lr.positionCount = _segments + 1;
            lr.startWidth = _lineWidth;
            lr.endWidth = _lineWidth;
            lr.startColor = color;
            lr.endColor = color;
            lr.loop = true;
            lr.useWorldSpace = false; // 로컬 좌표 → 부위 이동 시 자동 추적

            for (int i = 0; i <= _segments; i++)
            {
                float angle = i * 2f * Mathf.PI / _segments;
                float x = Mathf.Cos(angle) * radius;
                float y = Mathf.Sin(angle) * radius;
                lr.SetPosition(i, new Vector3(x, y, 0f));
            }
        }

        /// <summary>
        /// 범위 원 LineRenderer 자동 생성.
        /// Inspector 에 연결되지 않은 경우 자식 오브젝트로 생성.
        /// </summary>
        private LineRenderer CreateRangeCircle()
        {
            GameObject go = new GameObject("SealRangeCircle");
            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.startWidth = _lineWidth;
            lr.endWidth = _lineWidth;
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.sortingLayerName = "Default";
            lr.sortingOrder = 1;

            Debug.Log($"[SealReadyNotifier] {gameObject.name} — SealRangeCircle 자동 생성");
            return lr;
        }

        // ══════════════════════════════════════════════════════
        // 외부 API — BossDataSO 주입
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// BossDataSO 외부 주입.
        /// BossWardenCore.Initialize() 에서 호출.
        /// </summary>
        public void Initialize(BossDataSO data)
        {
            _bossData = data;
        }

        // ══════════════════════════════════════════════════════
        // Gizmos
        // ══════════════════════════════════════════════════════

        private void OnDrawGizmosSelected()
        {
            if (!_isRangeVisible) return;

            // 범위 원 씬 뷰 표시
            Gizmos.color = _sealable != null && _sealable.Grade == SealGrade.Core
                ? new Color(1f, 0.93f, 0f, 0.3f)
                : new Color(0f, 0.53f, 1f, 0.3f);

            float radius = _sealable?.SealRange ?? 1.5f;
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}