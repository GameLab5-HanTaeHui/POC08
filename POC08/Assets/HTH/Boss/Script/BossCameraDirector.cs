// ============================================================
// BossCameraDirector.cs  v1.0
// 보스 전투 카메라 연출 컴포넌트 — 모든 보스 공용
//
// [역할]
//   IBossCore 인터페이스 이벤트 수신 → Camera.main orthographicSize DOTween 제어
//   특정 보스에 종속되지 않음 — IBossCore 구현체라면 어떤 보스든 연결 가능
//
// [연결 방법]
//   씬의 독립 오브젝트(CameraRoot 또는 GameManager) 에 부착
//   _bossCore : IBossCore 구현 MonoBehaviour Inspector 연결
//               (BossWardenCore, 추후 보스 코어 등)
//
// [줌 흐름]
//   그로기 진입   → 약한 줌인  (_groggyZoomRatio  기본 0.85×)
//   그로기 해제   → 원래 크기 복귀
//   딜 페이즈 진입 → 중간 줌인 (_dilPhaseZoomRatio 기본 0.75×)
//   딜 페이즈 종료 → 원래 크기 복귀
//   보스 처치     → 강한 줌인  (_deadZoomRatio     기본 0.5×)
//                   → 유지(_deadZoomHoldDuration) → 원래 크기 복귀
//
// [간략 구현 원칙]
//   Cinemachine 미사용 — Camera.main.DOOrthoSize 직접 제어
//   SetUpdate(true)    — TimeScale 슬로우 중에도 카메라 정상 동작
//   추후 Cinemachine 교체 시 ZoomTo() 메서드만 수정하면 됨
//
// [namespace] SEAL
// ============================================================

using UnityEngine;
using DG.Tweening;

namespace SEAL
{
    /// <summary>
    /// 보스 전투 카메라 연출 컴포넌트. 모든 보스 공용. (v1.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [사용법]
    ///   씬의 CameraRoot 에 부착 후
    ///   _bossCore 에 BossWardenCore (또는 다른 보스 코어) 연결
    ///   → IBossCore 이벤트 자동 구독
    /// ────────────────────────────────────────────────────
    /// </summary>
    public class BossCameraDirector : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── 보스 코어 연결 ──────────────────────")]

        /// <summary>
        /// IBossCore 구현체. MonoBehaviour + IBossCore 를 동시에 만족해야 함.
        /// BossWardenCore 또는 추후 추가되는 다른 보스 코어 연결.
        ///
        /// [주의]
        ///   UnityEngine.Object 를 직접 받아서 IBossCore 로 캐스팅.
        ///   Inspector 에서는 MonoBehaviour 로 연결하고
        ///   Start() 에서 IBossCore 로 캐스팅하여 이벤트 구독.
        /// </summary>
        [Tooltip("IBossCore 구현 MonoBehaviour. BossWardenCore 등 연결.")]
        [SerializeField] private MonoBehaviour _bossCoreObject;

        [Header("── 줌 배율 ──────────────────────")]

        /// <summary>기본 orthographicSize. 0이면 씬 시작 시 자동 캐싱.</summary>
        [Tooltip("기본 카메라 orthographicSize. 0이면 자동 캐싱.")]
        [Min(0f)]
        [SerializeField] private float _defaultSize = 0f;

        /// <summary>그로기 줌인 배율. 1=줌 없음 / 0.85=15% 줌인.</summary>
        [Tooltip("그로기 줌인 배율. 권장: 0.85")]
        [Range(0.5f, 1f)]
        [SerializeField] private float _groggyZoomRatio = 0.85f;

        /// <summary>딜 페이즈 줌인 배율.</summary>
        [Tooltip("딜 페이즈 줌인 배율. 권장: 0.75")]
        [Range(0.4f, 1f)]
        [SerializeField] private float _dilPhaseZoomRatio = 0.75f;

        /// <summary>보스 처치 최종 줌인 배율.</summary>
        [Tooltip("보스 처치 최종 줌인 배율. 권장: 0.5")]
        [Range(0.2f, 1f)]
        [SerializeField] private float _deadZoomRatio = 0.5f;

        [Header("── 줌 속도 ──────────────────────")]

        [Tooltip("줌인 전환 시간 (초). 권장: 0.5")]
        [Min(0.05f)]
        [SerializeField] private float _zoomInDuration = 0.5f;

        [Tooltip("줌아웃 전환 시간 (초). 권장: 0.4")]
        [Min(0.05f)]
        [SerializeField] private float _zoomOutDuration = 0.4f;

        [Tooltip("보스 처치 후 줌 유지 시간 (초). 권장: 2.0")]
        [Min(0f)]
        [SerializeField] private float _deadZoomHoldDuration = 2.0f;

        // ══════════════════════════════════════════════════════
        // 내부 상태
        // ══════════════════════════════════════════════════════

        private Camera _cam;
        private Tweener _zoomTween;

        /// <summary>
        /// IBossCore 캐스팅 결과.
        /// Start() 에서 _bossCoreObject → IBossCore 캐스팅.
        /// </summary>
        private IBossCore _bossCore;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            _cam = Camera.main;

            if (_cam != null && _defaultSize <= 0f)
                _defaultSize = _cam.orthographicSize;
        }

        private void Start()
        {
            if (_bossCoreObject == null)
            {
                Debug.LogWarning("[BossCameraDirector] _bossCoreObject 미연결.");
                return;
            }

            // MonoBehaviour → IBossCore 캐스팅
            _bossCore = _bossCoreObject as IBossCore;
            if (_bossCore == null)
            {
                Debug.LogError(
                    $"[BossCameraDirector] {_bossCoreObject.name} 이(가) IBossCore 를 구현하지 않습니다.");
                return;
            }

            SubscribeEvents();
        }

        private void OnDestroy()
        {
            _zoomTween?.Kill();
            UnsubscribeEvents();
        }

        // ══════════════════════════════════════════════════════
        // 이벤트 구독 / 해제
        // ══════════════════════════════════════════════════════

        private void SubscribeEvents()
        {
            if (_bossCore == null) return;

            _bossCore.OnDilPhaseEnter += HandleDilPhaseEnter;
            _bossCore.OnDilPhaseExit += HandleDilPhaseExit;
            _bossCore.OnDead += HandleDead;
        }

        private void UnsubscribeEvents()
        {
            if (_bossCore == null) return;

            _bossCore.OnDilPhaseEnter -= HandleDilPhaseEnter;
            _bossCore.OnDilPhaseExit -= HandleDilPhaseExit;
            _bossCore.OnDead -= HandleDead;
        }

        // ══════════════════════════════════════════════════════
        // 이벤트 핸들러
        // ══════════════════════════════════════════════════════

        private void HandleGroggyEnter()
        {
            ZoomTo(_defaultSize * _groggyZoomRatio, _zoomInDuration, Ease.OutCubic);
        }

        private void HandleGroggyExit()
        {
            ZoomTo(_defaultSize, _zoomOutDuration, Ease.OutCubic);
        }

        private void HandleDilPhaseEnter()
        {
            ZoomTo(_defaultSize * _dilPhaseZoomRatio, _zoomInDuration, Ease.OutCubic);
        }

        private void HandleDilPhaseExit()
        {
            ZoomTo(_defaultSize, _zoomOutDuration, Ease.OutCubic);
        }

        private void HandleDead()
        {
            // 강한 줌인 → 유지 → 원래 크기 복귀
            _zoomTween?.Kill();
            _zoomTween = _cam
                .DOOrthoSize(_defaultSize * _deadZoomRatio, _zoomInDuration)
                .SetEase(Ease.OutCubic)
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    _zoomTween = _cam
                        .DOOrthoSize(_defaultSize, _zoomOutDuration)
                        .SetDelay(_deadZoomHoldDuration)
                        .SetEase(Ease.InOutCubic)
                        .SetUpdate(true);
                });
        }

        // ══════════════════════════════════════════════════════
        // 외부 API
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 수동 줌인. 외부에서 직접 호출 가능.
        /// ratio: 기본 Size 대비 배율 (0.5 = 50% 줌인).
        /// </summary>
        public void ZoomIn(float ratio, float duration = -1f)
        {
            float dur = duration > 0f ? duration : _zoomInDuration;
            ZoomTo(_defaultSize * ratio, dur, Ease.OutCubic);
        }

        /// <summary>수동 줌아웃. 원래 크기로 복귀.</summary>
        public void ZoomOut(float duration = -1f)
        {
            float dur = duration > 0f ? duration : _zoomOutDuration;
            ZoomTo(_defaultSize, dur, Ease.OutCubic);
        }

        /// <summary>
        /// 런타임 중 다른 보스로 교체.
        /// 기존 구독 해제 → 새 보스 구독.
        /// </summary>
        public void SetBossCore(IBossCore newCore)
        {
            UnsubscribeEvents();
            _bossCore = newCore;
            SubscribeEvents();
        }

        // ══════════════════════════════════════════════════════
        // 내부 줌 처리
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 카메라 orthographicSize DOTween 전환.
        /// 추후 Cinemachine 방식으로 교체 시 이 메서드만 수정.
        /// </summary>
        private void ZoomTo(float targetSize, float duration, Ease ease)
        {
            if (_cam == null) return;

            _zoomTween?.Kill();
            _zoomTween = _cam
                .DOOrthoSize(targetSize, duration)
                .SetEase(ease)
                .SetUpdate(true);
        }
    }
}