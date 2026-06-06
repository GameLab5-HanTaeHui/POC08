// ============================================================
// SealEffectManager.cs  v1.0
// 봉인 시스템 이펙트 / UI / 범위 표시 총괄 관리자
//
// [역할]
//   봉인 시스템의 씬 전체 / 월드 공간 이펙트를 총괄 관리.
//   SealStateManager 이벤트를 구독하여 상태별 연출 실행.
//
// [SealExecutionEffect 와의 차이]
//   SealExecutionEffect : 부위 오브젝트 기준 로컬 연출
//                         (집행 Arc 게이지, Scale Punch 등)
//   SealEffectManager   : 씬 전체 / 보스 루트 기준 연출
//                         (충격파 트리거, 카메라 쉐이크, 범위 원 표시)
//
// [담당 연출 목록]
//   1. 상태별 본체 색상 연출 요청
//      → BossWardenFeedback 에 상태 전환 알림 (선택적 연동)
//
//   2. 충격파 트리거
//      → DilPhase 종료(실패) / 그로기 실패 시 BossWardenShockwave.Trigger()
//
//   3. 코어 범위 원 표시
//      → SealStateManager.OnGroggyEnter 수신 시 코어 위치에 범위 원 표시
//      → SealStateManager.OnGroggyExit / OnDilPhaseExit 수신 시 숨김
//
//   4. UI 게이지 갱신 요청
//      → SealableComponent.OnGaugeChanged 구독 후 UI 이벤트 발행
//      → 실제 UI 구현 전 이벤트 중계 역할
//
//   5. 페이즈 전환 연출
//      → OnPhaseChanged 수신 시 카메라 쉐이크 + 화면 플래시
//
// [BossWardenShockwave 와의 관계]
//   SealEffectManager 가 Trigger() 를 호출하는 중계자.
//   BossWardenShockwave 는 실제 충격파 물리 + DOTween 연출 담당.
//
// [부착 위치]
//   Boss_Root 오브젝트에 부착. (보스 1개당 1개)
//
// [namespace] SEAL
// ============================================================

using System;
using UnityEngine;
using DG.Tweening;

namespace SEAL
{
    /// <summary>
    /// 봉인 시스템 이펙트 / UI / 범위 표시 총괄 관리자. (v1.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [구독 이벤트]
    ///   SealStateManager.OnGroggyEnter     코어 범위 원 표시
    ///   SealStateManager.OnGroggyExit      코어 범위 원 숨김 + 충격파
    ///   SealStateManager.OnDilPhaseEnter   딜페이즈 연출
    ///   SealStateManager.OnDilPhaseExit    종료 연출 + 충격파
    ///   SealStateManager.OnFinalSealReady  최종봉인 연출
    ///   SealStateManager.OnPhaseChanged    페이즈 전환 연출
    ///   SealStateManager.OnDead            처치 연출
    ///
    /// [외부 이벤트 — UI 연동용]
    ///   OnPartGaugeChanged(SealableComponent, float)
    ///   OnCoreGaugeChanged(SealableComponent, float)
    /// ────────────────────────────────────────────────────
    /// </summary>
    public class SealEffectManager : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── 컴포넌트 연결 ──────────────────────")]

        /// <summary>
        /// 코어 Transform. 범위 원 표시 위치 기준.
        /// 미연결 시 _coreObject 로 대체.
        /// </summary>
        [Tooltip("코어 Transform. 범위 원 표시 기준. 미연결 시 코어 오브젝트 자동 탐색.")]
        [SerializeField] private Transform _coreTransform;

        /// <summary>
        /// 충격파 컴포넌트.
        /// DilPhase 종료 / 그로기 실패 시 Trigger() 호출.
        /// 미연결 시 GetComponent 자동 탐색.
        /// </summary>
        [Tooltip("BossWardenShockwave. 미연결 시 자동 탐색.")]
        [SerializeField] private BossWardenShockwave _shockwave;

        [Header("── 코어 범위 원 ──────────────────────")]

        /// <summary>
        /// 코어 범위 원 LineRenderer.
        /// 그로기 진입 시 활성. 자식 오브젝트 연결.
        /// 미연결 시 Awake 에서 자동 생성.
        /// </summary>
        [Tooltip("코어 범위 원 LineRenderer. 미연결 시 자동 생성.")]
        [SerializeField] private LineRenderer _coreRangeCircle;

        /// <summary>
        /// 코어 범위 원 선 두께.
        /// </summary>
        [Tooltip("코어 범위 원 선 두께. 권장: 0.06.")]
        [Min(0.01f)]
        [SerializeField] private float _coreRangeLineWidth = 0.06f;

        /// <summary>
        /// 코어 범위 원 분할 수.
        /// </summary>
        [Tooltip("코어 범위 원 분할 수. 권장: 32.")]
        [Range(8, 64)]
        [SerializeField] private int _coreRangeSegments = 32;

        [Header("── 카메라 연결 (선택) ──────────────────────")]

        /// <summary>
        /// 카메라 Transform. 페이즈 전환 쉐이크에 사용.
        /// 미연결 시 Camera.main 자동 탐색.
        /// </summary>
        [Tooltip("카메라 Transform. 페이즈 전환 쉐이크용. 미연결 시 Camera.main 사용.")]
        [SerializeField] private Transform _cameraTransform;

        // ══════════════════════════════════════════════════════
        // 컴포넌트 참조
        // ══════════════════════════════════════════════════════

        /// <summary>상태 총괄. 이벤트 구독 대상.</summary>
        private SealStateManager _stateManager;

        /// <summary>봉인도 전체 조율. SealableComponent 목록 조회.</summary>
        private SealGaugeManager _gaugeManager;

        /// <summary>BossDataSO. 색상 참조.</summary>
        private BossDataSO _bossData;

        // ══════════════════════════════════════════════════════
        // UI 이벤트 — 외부 UI 컴포넌트 연동용
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Part 등급 봉인도 변화 시 발행.
        /// 파라미터: (SealableComponent 참조, UI 퍼센트 0~100).
        /// UI 게이지 컴포넌트가 구독하여 갱신.
        /// </summary>
        public event Action<SealableComponent, float> OnPartGaugeChanged;

        /// <summary>
        /// Core 등급 봉인도 변화 시 발행.
        /// 파라미터: (SealableComponent 참조, UI 퍼센트 0~100).
        /// </summary>
        public event Action<SealableComponent, float> OnCoreGaugeChanged;

        /// <summary>
        /// 페이즈 전환 시 발행. UI 페이즈 표시 갱신용.
        /// 파라미터: 새 페이즈 번호.
        /// </summary>
        public event Action<int> OnPhaseUIChanged;

        // ══════════════════════════════════════════════════════
        // 내부 상태
        // ══════════════════════════════════════════════════════

        /// <summary>코어 범위 원 맥동 Tween.</summary>
        private Tweener _coreRangePulseTween;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            _stateManager = GetComponent<SealStateManager>();
            _gaugeManager = GetComponent<SealGaugeManager>();

            if (_shockwave == null)
                _shockwave = GetComponent<BossWardenShockwave>();

            if (_cameraTransform == null && Camera.main != null)
                _cameraTransform = Camera.main.transform;

            // 코어 범위 원 자동 생성
            if (_coreRangeCircle == null)
                _coreRangeCircle = CreateCircle("CoreRangeCircle");

            if (_coreRangeCircle != null)
                _coreRangeCircle.gameObject.SetActive(false);
        }

        private void Start()
        {
            // SealStateManager 이벤트 구독
            if (_stateManager != null)
            {
                _stateManager.OnGroggyEnter -= HandleGroggyEnter;
                _stateManager.OnGroggyEnter += HandleGroggyEnter;
                _stateManager.OnGroggyExit -= HandleGroggyExit;
                _stateManager.OnGroggyExit += HandleGroggyExit;
                _stateManager.OnDilPhaseEnter -= HandleDilPhaseEnter;
                _stateManager.OnDilPhaseEnter += HandleDilPhaseEnter;
                _stateManager.OnDilPhaseExit -= HandleDilPhaseExit;
                _stateManager.OnDilPhaseExit += HandleDilPhaseExit;
                _stateManager.OnFinalSealReady -= HandleFinalSealReady;
                _stateManager.OnFinalSealReady += HandleFinalSealReady;
                _stateManager.OnPhaseChanged -= HandlePhaseChanged;
                _stateManager.OnPhaseChanged += HandlePhaseChanged;
                _stateManager.OnDead -= HandleDead;
                _stateManager.OnDead += HandleDead;
            }

            // SealGaugeManager 의 SealableComponent 게이지 이벤트 구독
            SubscribeGaugeEvents();
        }

        private void OnDestroy()
        {
            _coreRangePulseTween?.Kill();

            if (_stateManager != null)
            {
                _stateManager.OnGroggyEnter -= HandleGroggyEnter;
                _stateManager.OnGroggyExit -= HandleGroggyExit;
                _stateManager.OnDilPhaseEnter -= HandleDilPhaseEnter;
                _stateManager.OnDilPhaseExit -= HandleDilPhaseExit;
                _stateManager.OnFinalSealReady -= HandleFinalSealReady;
                _stateManager.OnPhaseChanged -= HandlePhaseChanged;
                _stateManager.OnDead -= HandleDead;
            }
        }

        // ══════════════════════════════════════════════════════
        // 게이지 이벤트 구독
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// SealGaugeManager 하위 모든 SealableComponent 의
        /// OnGaugeChanged 를 구독하여 UI 이벤트 중계.
        /// </summary>
        private void SubscribeGaugeEvents()
        {
            if (_gaugeManager == null) return;

            foreach (var s in _gaugeManager.GetAllSealables())
            {
                if (s == null) continue;

                var captured = s;

                if (s.grade == SealGrade.Core)
                {
                    s.OnGaugeChanged -= (p) => OnCoreGaugeChanged?.Invoke(captured, p);
                    s.OnGaugeChanged += (p) => OnCoreGaugeChanged?.Invoke(captured, p);
                }
                else if (s.grade == SealGrade.Part)
                {
                    s.OnGaugeChanged -= (p) => OnPartGaugeChanged?.Invoke(captured, p);
                    s.OnGaugeChanged += (p) => OnPartGaugeChanged?.Invoke(captured, p);
                }
            }
        }

        // ══════════════════════════════════════════════════════
        // 상태별 이벤트 핸들러
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Groggy 진입 연출.
        /// 코어 범위 원 표시 + 맥동 시작.
        /// </summary>
        private void HandleGroggyEnter()
        {
            if (_coreTransform == null) return;

            float radius = _bossData?.SealData?.sealExecutionRange ?? 1.5f;
            Color color = _bossData?.ColorData?.colorCoreRange ?? Color.yellow;

            ShowCoreRangeCircle(_coreTransform.position, radius, color);

            Debug.Log("[SealEffectManager] ▶ Groggy 진입 — 코어 범위 원 표시");
        }

        /// <summary>
        /// Groggy 실패 종료 연출.
        /// 코어 범위 원 숨김 + 충격파 트리거.
        /// </summary>
        private void HandleGroggyExit()
        {
            HideCoreRangeCircle();
            TriggerShockwave();

            Debug.Log("[SealEffectManager] ■ Groggy 실패 종료 — 충격파 발동");
        }

        /// <summary>
        /// DilPhase 진입 연출.
        /// 코어 범위 원 색상 변경 (딜페이즈 색상으로).
        /// </summary>
        private void HandleDilPhaseEnter()
        {
            if (_coreRangeCircle == null || !_coreRangeCircle.gameObject.activeSelf) return;

            // 범위 원 색상 → 딜페이즈 색상
            Color dilColor = _bossData?.ColorData?.colorCoreDilPhase ?? Color.white;
            _coreRangeCircle.startColor = dilColor;
            _coreRangeCircle.endColor = dilColor;

            Debug.Log("[SealEffectManager] ▶ DilPhase 진입 — 코어 범위 원 색상 전환");
        }

        /// <summary>
        /// DilPhase 종료 연출.
        /// 코어 범위 원 숨김 + 충격파 트리거 (실패 루프 경우).
        /// </summary>
        private void HandleDilPhaseExit()
        {
            HideCoreRangeCircle();
            TriggerShockwave();

            Debug.Log("[SealEffectManager] ■ DilPhase 종료 — 코어 범위 원 숨김 + 충격파");
        }

        /// <summary>
        /// FinalSeal 진입 연출.
        /// 코어 범위 원 최종봉인 색상으로 전환 + 강한 맥동.
        /// </summary>
        private void HandleFinalSealReady()
        {
            if (_coreTransform == null) return;

            float radius = _bossData?.SealData?.sealExecutionRange ?? 1.5f;
            Color finalColor = _bossData?.ColorData?.colorCoreFinalSeal ?? Color.red;

            // 범위 원 최종봉인 색상으로 재표시
            ShowCoreRangeCircle(_coreTransform.position, radius, finalColor);

            // 강한 맥동 (빠른 주기)
            if (_coreRangeCircle != null)
            {
                _coreRangePulseTween?.Kill();
                float pulseDur = (_bossData?.ColorData?.sealReadyPulseDuration ?? 0.4f) * 0.5f;
                _coreRangePulseTween = _coreRangeCircle.transform
                    .DOScale(1.15f, pulseDur)
                    .SetEase(Ease.InOutSine)
                    .SetLoops(-1, LoopType.Yoyo)
                    .SetUpdate(true);
            }

            Debug.Log("[SealEffectManager] ▶ FinalSeal — 최종봉인 범위 연출");
        }

        /// <summary>
        /// 페이즈 전환 연출.
        /// 카메라 쉐이크 + UI 페이즈 이벤트 발행.
        /// </summary>
        private void HandlePhaseChanged(int newPhase)
        {
            // 카메라 쉐이크
            if (_cameraTransform != null)
            {
                _cameraTransform
                    .DOShakePosition(0.4f, strength: 0.3f, vibrato: 10, randomness: 45f)
                    .SetUpdate(true);
            }

            // 코어 범위 원 숨김 (페이즈 전환 중 잠시 숨김)
            HideCoreRangeCircle();

            // UI 이벤트 발행
            OnPhaseUIChanged?.Invoke(newPhase);

            Debug.Log($"[SealEffectManager] ▶ 페이즈 전환 → {newPhase}페이즈 | 카메라 쉐이크");
        }

        /// <summary>
        /// Dead 연출.
        /// 코어 범위 원 즉시 숨김 + 맥동 종료.
        /// </summary>
        private void HandleDead()
        {
            _coreRangePulseTween?.Kill();
            HideCoreRangeCircle();

            Debug.Log("[SealEffectManager] ✅ Dead — 모든 범위 원 정리");
        }

        // ══════════════════════════════════════════════════════
        // 충격파
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 충격파 트리거.
        /// BossWardenShockwave.Trigger(bossPos) 호출.
        /// 미연결 시 경고 로그만 출력.
        /// </summary>
        private void TriggerShockwave()
        {
            if (_shockwave != null)
            {
                _shockwave.Trigger(transform.position);
                Debug.Log("[SealEffectManager] 충격파 트리거");
            }
            else
            {
                Debug.LogWarning("[SealEffectManager] BossWardenShockwave 미연결 — 충격파 스킵");
            }
        }

        // ══════════════════════════════════════════════════════
        // 코어 범위 원
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 코어 범위 원 활성 + 그리기.
        /// 그로기 진입 / FinalSeal 진입 시 호출.
        /// </summary>
        private void ShowCoreRangeCircle(Vector3 centerPos, float radius, Color color)
        {
            if (_coreRangeCircle == null) return;

            // 위치 동기화 — 코어 Transform 에 붙어 있지 않을 경우 수동 설정
            if (_coreRangeCircle.transform.parent == null)
                _coreRangeCircle.transform.position = centerPos;

            DrawCircle(_coreRangeCircle, radius, color);
            _coreRangeCircle.gameObject.SetActive(true);

            // 맥동 시작
            _coreRangePulseTween?.Kill();
            float pulseDur = _bossData?.ColorData?.sealReadyPulseDuration ?? 0.4f;
            _coreRangePulseTween = _coreRangeCircle.transform
                .DOScale(1.08f, pulseDur * 0.5f)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo)
                .SetUpdate(true);
        }

        /// <summary>
        /// 코어 범위 원 비활성 + 맥동 종료.
        /// </summary>
        private void HideCoreRangeCircle()
        {
            _coreRangePulseTween?.Kill();
            _coreRangePulseTween = null;

            if (_coreRangeCircle != null)
            {
                _coreRangeCircle.transform.localScale = Vector3.one;
                _coreRangeCircle.gameObject.SetActive(false);
            }
        }

        // ══════════════════════════════════════════════════════
        // 내부 유틸
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// LineRenderer 로 원을 그린다.
        /// </summary>
        private void DrawCircle(LineRenderer lr, float radius, Color color)
        {
            lr.positionCount = _coreRangeSegments + 1;
            lr.startWidth = _coreRangeLineWidth;
            lr.endWidth = _coreRangeLineWidth;
            lr.startColor = color;
            lr.endColor = color;
            lr.loop = true;
            lr.useWorldSpace = false;

            for (int i = 0; i <= _coreRangeSegments; i++)
            {
                float angle = i * 2f * Mathf.PI / _coreRangeSegments;
                lr.SetPosition(i, new Vector3(
                    Mathf.Cos(angle) * radius,
                    Mathf.Sin(angle) * radius,
                    0f));
            }
        }

        /// <summary>
        /// 범위 원 LineRenderer 자동 생성.
        /// </summary>
        private LineRenderer CreateCircle(string name)
        {
            GameObject go = new GameObject(name);

            // 코어 Transform 이 있으면 자식으로, 없으면 보스 루트 자식
            go.transform.SetParent(
                _coreTransform != null ? _coreTransform : transform,
                worldPositionStays: false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localScale = Vector3.one;

            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.startWidth = _coreRangeLineWidth;
            lr.endWidth = _coreRangeLineWidth;
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.sortingLayerName = "Default";
            lr.sortingOrder = 1;

            Debug.Log($"[SealEffectManager] {name} 자동 생성");
            return lr;
        }

        // ══════════════════════════════════════════════════════
        // 외부 API
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// BossDataSO 주입. BossWardenCore.Initialize() 에서 호출.
        /// </summary>
        public void Initialize(BossDataSO data)
        {
            _bossData = data;
        }

        /// <summary>
        /// 코어 Transform 외부 주입.
        /// BossWardenCore.Initialize() 에서 코어 오브젝트 연결 시 호출.
        /// </summary>
        public void SetCoreTransform(Transform coreTransform)
        {
            _coreTransform = coreTransform;

            // 이미 생성된 범위 원이 있으면 부모 재설정
            if (_coreRangeCircle != null && coreTransform != null)
                _coreRangeCircle.transform.SetParent(coreTransform, worldPositionStays: false);
        }

        /// <summary>
        /// 모든 범위 표시 즉시 숨김. 보스 처치 / 씬 전환 시 사용.
        /// </summary>
        public void HideAll()
        {
            HideCoreRangeCircle();
            Debug.Log("[SealEffectManager] HideAll — 모든 범위 원 숨김");
        }
    }
}