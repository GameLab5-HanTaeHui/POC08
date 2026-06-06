// ============================================================
// SealExecutionEffect.cs  v1.0
// 부위 로컬 봉인 집행 연출 컴포넌트
//
// [역할]
//   봉인 집행 과정의 부위 로컬 DOTween 연출 전담.
//   SealableComponent 이벤트를 구독하여 각 단계별 연출 실행.
//
// [SealExecutionFX 와의 차이]
//   SealExecutionEffect : 해당 부위 오브젝트 기준 로컬 연출
//                         (크기 진동, 색상 강조, 홀드 진행 원형 등)
//   SealExecutionFX     : 씬 전체 / 월드 공간 이펙트
//                         (카메라 쉐이크, 전체 화면 플래시 등)
//
// [연출 단계]
//   1. 집행 가능 상태 (IsSealReady)
//      → SealReadyNotifier 가 범위 원 + 맥동 처리
//      → 이 컴포넌트는 부위 자체 Scale 진동만 담당
//
//   2. 홀드 진행 중
//      → 홀드 진행 원형 게이지 (LineRenderer 호 그리기)
//      → 부위 Scale 서서히 커짐 (홀드 진행도에 비례)
//
//   3. 집행 완료 (OnSealCompleted)
//      → Scale Punch DOTween
//      → 완료 플래시 DOColor
//      → 원형 게이지 소멸
//
//   4. 집행 취소 (홀드 중단)
//      → Scale 원점 복귀
//      → 원형 게이지 빠른 소멸
//
//   5. 강제 해제 (OnForceReleased)
//      → Scale Shake DOTween
//      → 원형 게이지 소멸
//
// [홀드 진행 원형 게이지]
//   기존 SealableComponent 의 TMP 텍스트 방식 대신
//   LineRenderer 호(Arc) 그리기 방식 사용.
//   0% = 호 없음 / 100% = 완전한 원
//   색상: colorSealRange → colorSealReadyPulse (진행도에 따라 보간)
//
// [부착 위치]
//   SealableComponent 와 같은 오브젝트에 부착.
//   Boss_LeftArm / Boss_RightArm / Boss_Core
//   보스 부위에 부착하는 이유:
//     연출 기준점이 부위 Transform 이므로 보스에 붙여야 함
//     플레이어에 붙이면 부위 위치 추적 코드가 필요 → 결합도 증가
//
// [namespace] SEAL
// ============================================================

using System.Collections;
using UnityEngine;
using DG.Tweening;

namespace SEAL
{
    /// <summary>
    /// 부위 로컬 봉인 집행 연출 컴포넌트. (v1.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [연출 흐름]
    ///   집행 시작 → ShowArcGauge(0f)
    ///   홀드 중   → UpdateArcGauge(progress 0~1)
    ///   집행 완료 → PlayCompleteEffect() → HideArcGauge()
    ///   홀드 취소 → PlayCancelEffect() → HideArcGauge()
    ///   강제 해제 → PlayForceReleaseEffect() → HideArcGauge()
    ///
    /// [외부 API — SealExecutor 에서 호출]
    ///   OnExecutionStart()        집행 시작
    ///   OnExecutionProgress(0~1)  홀드 진행도 갱신
    ///   OnExecutionComplete()     집행 완료
    ///   OnExecutionCancel()       홀드 취소
    ///
    /// [SealableComponent 이벤트 자동 구독]
    ///   OnSealCompleted  → OnExecutionComplete() 자동 호출
    ///   OnForceReleased  → PlayForceReleaseEffect() 자동 호출
    /// ────────────────────────────────────────────────────
    /// </summary>
    [RequireComponent(typeof(SealableComponent))]
    public class SealExecutionEffect : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── 홀드 진행 원형 게이지 LineRenderer ──────────────────────")]

        /// <summary>
        /// 홀드 진행 원형 게이지 LineRenderer.
        /// 자기 자식 오브젝트에 부착. 미연결 시 Awake 에서 자동 생성.
        ///
        /// [씬 구성]
        ///   Boss_LeftArm
        ///     ├─ SealExecutionEffect (이 컴포넌트)
        ///     └─ ExecutionArcGauge [LineRenderer]  ← 여기 연결
        /// </summary>
        [Tooltip("홀드 진행 호(Arc) LineRenderer. 미연결 시 자동 생성.")]
        [SerializeField] private LineRenderer _arcGauge;

        [Header("── 호 게이지 수치 ──────────────────────")]

        /// <summary>
        /// 호 게이지 반경 (units).
        /// SealableComponent.SealRange 보다 약간 크게 설정 권장.
        ///
        /// [권장값] 1.7
        /// </summary>
        [Tooltip("호 게이지 반경. SealRange 보다 약간 크게. 권장: 1.7.")]
        [Min(0.1f)]
        [SerializeField] private float _arcRadius = 1.7f;

        /// <summary>
        /// 호 게이지 선 두께.
        ///
        /// [권장값] 0.08
        /// </summary>
        [Tooltip("호 게이지 선 두께. 권장: 0.08.")]
        [Min(0.01f)]
        [SerializeField] private float _arcWidth = 0.08f;

        /// <summary>
        /// 호 분할 수. 클수록 부드러운 호.
        ///
        /// [권장값] 48
        /// </summary>
        [Tooltip("호 분할 수. 클수록 부드러운 호. 권장: 48.")]
        [Range(8, 64)]
        [SerializeField] private int _arcSegments = 48;

        [Header("── Scale 연출 수치 ──────────────────────")]

        /// <summary>
        /// 집행 완료 시 Scale Punch 강도.
        ///
        /// [권장값] 0.3
        /// </summary>
        [Tooltip("집행 완료 Scale Punch 강도. 권장: 0.3.")]
        [Range(0f, 1f)]
        [SerializeField] private float _completePunchStrength = 0.3f;

        /// <summary>
        /// 강제 해제 시 Scale Shake 강도.
        ///
        /// [권장값] 0.15
        /// </summary>
        [Tooltip("강제 해제 Scale Shake 강도. 권장: 0.15.")]
        [Range(0f, 1f)]
        [SerializeField] private float _forceReleaseShakeStrength = 0.15f;

        /// <summary>
        /// 홀드 중 최대 Scale 배율.
        /// 홀드 진행도에 비례하여 1.0 → 이 값으로 서서히 커짐.
        ///
        /// [권장값] 1.08
        /// </summary>
        [Tooltip("홀드 중 최대 Scale 배율. 1.0 → 이 값으로 서서히 커짐. 권장: 1.08.")]
        [Range(1f, 1.5f)]
        [SerializeField] private float _holdMaxScale = 1.08f;

        // ══════════════════════════════════════════════════════
        // 컴포넌트 참조
        // ══════════════════════════════════════════════════════

        /// <summary>같은 오브젝트의 SealableComponent.</summary>
        private SealableComponent _sealable;

        /// <summary>BossDataSO 참조. SealableComponent 에서 가져옴.</summary>
        private BossDataSO _bossData;

        /// <summary>Scale 연출 대상 Visual Transform. 미설정 시 자기 Transform.</summary>
        private Transform _visualTransform;

        // ══════════════════════════════════════════════════════
        // 내부 상태
        // ══════════════════════════════════════════════════════

        /// <summary>현재 호 게이지 진행도 (0~1).</summary>
        private float _currentProgress;

        /// <summary>홀드 중 Scale Tween 참조.</summary>
        private Tweener _scaleTween;

        /// <summary>완료/취소 연출 코루틴 참조.</summary>
        private Coroutine _effectCoroutine;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            _sealable = GetComponent<SealableComponent>();
            if (_sealable == null)
            {
                Debug.LogError($"[SealExecutionEffect] {gameObject.name} — SealableComponent 미부착.");
                enabled = false;
                return;
            }

            _visualTransform = transform;

            // 호 게이지 자동 생성
            if (_arcGauge == null)
                _arcGauge = CreateArcGauge();

            // 초기 비활성
            if (_arcGauge != null)
                _arcGauge.gameObject.SetActive(false);
        }

        private void Start()
        {
            // SealableComponent 이벤트 구독
            _sealable.OnSealCompleted -= HandleSealCompleted;
            _sealable.OnSealCompleted += HandleSealCompleted;
            _sealable.OnForceReleased -= HandleForceReleased;
            _sealable.OnForceReleased += HandleForceReleased;
        }

        private void OnDestroy()
        {
            _scaleTween?.Kill();

            if (_effectCoroutine != null)
                StopCoroutine(_effectCoroutine);

            if (_sealable != null)
            {
                _sealable.OnSealCompleted -= HandleSealCompleted;
                _sealable.OnForceReleased -= HandleForceReleased;
            }
        }

        // ══════════════════════════════════════════════════════
        // 외부 API — SealExecutor 에서 호출
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 집행 시작 연출.
        /// SealExecutor 에서 홀드 루프 진입 직후 호출.
        /// 호 게이지 표시 시작 + 부위 Scale 준비.
        /// </summary>
        public void OnExecutionStart()
        {
            // 이전 연출 정리
            if (_effectCoroutine != null)
            {
                StopCoroutine(_effectCoroutine);
                _effectCoroutine = null;
            }

            _scaleTween?.Kill();
            _visualTransform.localScale = Vector3.one;

            // 호 게이지 초기화
            _currentProgress = 0f;
            ShowArcGauge(0f);

            Debug.Log($"[SealExecutionEffect] {gameObject.name} 집행 시작 연출");
        }

        /// <summary>
        /// 홀드 진행도 갱신.
        /// SealExecutor 에서 매 프레임 호출.
        ///
        /// [연출]
        ///   호 게이지 0% → 100% 진행
        ///   부위 Scale 1.0 → _holdMaxScale 서서히 커짐
        /// </summary>
        /// <param name="progress">진행도 0.0 ~ 1.0.</param>
        public void OnExecutionProgress(float progress)
        {
            _currentProgress = Mathf.Clamp01(progress);

            // 호 게이지 갱신
            UpdateArcGauge(_currentProgress);

            // Scale 홀드 커짐 연출
            float targetScale = Mathf.Lerp(1.0f, _holdMaxScale, _currentProgress);
            _visualTransform.localScale = Vector3.one * targetScale;
        }

        /// <summary>
        /// 집행 완료 연출.
        /// SealExecutor 에서 홀드 완료 후 호출.
        ///
        /// [연출]
        ///   Scale Punch DOTween
        ///   호 게이지 빠른 소멸
        ///   Scale 원점 복귀
        /// </summary>
        public void OnExecutionComplete()
        {
            _scaleTween?.Kill();

            if (_effectCoroutine != null)
                StopCoroutine(_effectCoroutine);

            _effectCoroutine = StartCoroutine(CompleteEffectRoutine());
        }

        /// <summary>
        /// 집행 취소 연출 (홀드 중단 — S키 해제 or 범위 이탈).
        /// SealExecutor 에서 취소 시 호출.
        ///
        /// [연출]
        ///   Scale 빠른 원점 복귀
        ///   호 게이지 빠른 소멸
        /// </summary>
        public void OnExecutionCancel()
        {
            _scaleTween?.Kill();

            if (_effectCoroutine != null)
                StopCoroutine(_effectCoroutine);

            _effectCoroutine = StartCoroutine(CancelEffectRoutine());
        }

        // ══════════════════════════════════════════════════════
        // 이벤트 핸들러 (SealableComponent 자동 구독)
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// SealableComponent.OnSealCompleted 수신.
        /// ExecuteSeal() 완료 시 자동 호출.
        /// </summary>
        private void HandleSealCompleted()
        {
            // SealExecutor 가 OnExecutionComplete() 를 이미 호출했을 수도 있음
            // 중복 방지: 이미 연출 중이면 스킵
            if (_effectCoroutine != null) return;

            _effectCoroutine = StartCoroutine(CompleteEffectRoutine());
        }

        /// <summary>
        /// SealableComponent.OnForceReleased 수신.
        /// 강제 해제 시 자동 호출.
        ///
        /// [연출]
        ///   Scale Shake DOTween
        ///   호 게이지 소멸
        /// </summary>
        private void HandleForceReleased()
        {
            _scaleTween?.Kill();

            if (_effectCoroutine != null)
                StopCoroutine(_effectCoroutine);

            _effectCoroutine = StartCoroutine(ForceReleaseEffectRoutine());
        }

        // ══════════════════════════════════════════════════════
        // 연출 코루틴
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 집행 완료 연출 코루틴.
        /// Scale Punch → 호 게이지 페이드 소멸 → Scale 원점 복귀.
        /// </summary>
        private IEnumerator CompleteEffectRoutine()
        {
            // 호 게이지 즉시 완성 (100% 표시)
            UpdateArcGauge(1f);

            // Scale Punch
            bool punchDone = false;
            _visualTransform
                .DOPunchScale(
                    punch: Vector3.one * _completePunchStrength,
                    duration: 0.35f,
                    vibrato: 5,
                    elasticity: 0.5f)
                .SetEase(Ease.OutQuart)
                .SetUpdate(true)
                .OnComplete(() => punchDone = true);

            // Punch 완료 대기
            yield return new WaitUntil(() => punchDone);

            // Scale 원점 복귀
            _visualTransform.localScale = Vector3.one;

            // 호 게이지 페이드 소멸
            HideArcGauge();

            _effectCoroutine = null;

            Debug.Log($"[SealExecutionEffect] ✅ {gameObject.name} 집행 완료 연출 종료");
        }

        /// <summary>
        /// 집행 취소 연출 코루틴.
        /// Scale 빠른 원점 복귀 + 호 게이지 빠른 소멸.
        /// </summary>
        private IEnumerator CancelEffectRoutine()
        {
            // Scale 빠른 원점 복귀
            bool scaleDone = false;
            _visualTransform
                .DOScale(Vector3.one, 0.15f)
                .SetEase(Ease.OutQuart)
                .SetUpdate(true)
                .OnComplete(() => scaleDone = true);

            yield return new WaitUntil(() => scaleDone);

            // 호 게이지 빠른 소멸
            HideArcGauge();

            _effectCoroutine = null;

            Debug.Log($"[SealExecutionEffect] ■ {gameObject.name} 집행 취소 연출 종료");
        }

        /// <summary>
        /// 강제 해제 연출 코루틴.
        /// Scale Shake + 호 게이지 소멸.
        /// </summary>
        private IEnumerator ForceReleaseEffectRoutine()
        {
            // 호 게이지 즉시 숨김
            HideArcGauge();

            // Scale Shake
            bool shakeDone = false;
            _visualTransform
                .DOShakeScale(
                    duration: 0.4f,
                    strength: Vector3.one * _forceReleaseShakeStrength,
                    vibrato: 8,
                    randomness: 30f)
                .SetUpdate(true)
                .OnComplete(() => shakeDone = true);

            yield return new WaitUntil(() => shakeDone);

            // Scale 원점 복귀
            _visualTransform.localScale = Vector3.one;

            _effectCoroutine = null;

            Debug.Log($"[SealExecutionEffect] ■ {gameObject.name} 강제 해제 연출 종료");
        }

        // ══════════════════════════════════════════════════════
        // 호(Arc) 게이지 그리기
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 호 게이지 활성 + 초기 그리기.
        /// 12시 방향(-90도)에서 시계 방향으로 진행.
        /// </summary>
        private void ShowArcGauge(float progress)
        {
            if (_arcGauge == null) return;

            _arcGauge.gameObject.SetActive(true);
            DrawArc(progress);
        }

        /// <summary>
        /// 홀드 진행도에 따라 호 게이지 갱신.
        /// progress = 0 → 호 없음 / progress = 1 → 완전한 원.
        /// </summary>
        private void UpdateArcGauge(float progress)
        {
            if (_arcGauge == null || !_arcGauge.gameObject.activeSelf) return;
            DrawArc(progress);
        }

        /// <summary>
        /// 호 게이지 비활성.
        /// </summary>
        private void HideArcGauge()
        {
            if (_arcGauge == null) return;
            _arcGauge.gameObject.SetActive(false);
        }

        /// <summary>
        /// LineRenderer 로 호(Arc) 를 그린다.
        ///
        /// [그리기 방식]
        ///   12시 방향(-90도)에서 시계 방향으로 진행.
        ///   progress = 0.5 → 반원 (180도).
        ///   progress = 1.0 → 완전한 원 (360도).
        ///
        /// [색상]
        ///   시작 색상: colorSealRange (집행 가능 범위 색상)
        ///   끝 색상:   colorSealReadyPulse (집행 가능 맥동 색상)
        ///   두 색상 보간으로 진행도에 따라 색상 변화.
        /// </summary>
        private void DrawArc(float progress)
        {
            if (_arcGauge == null) return;

            // 최소 진행도 보장 (아주 작은 호라도 표시)
            float clampedProgress = Mathf.Clamp(progress, 0.01f, 1f);
            float totalAngle = 360f * clampedProgress;
            int pointCount = Mathf.Max(2, Mathf.RoundToInt(_arcSegments * clampedProgress));

            _arcGauge.positionCount = pointCount;
            _arcGauge.startWidth = _arcWidth;
            _arcGauge.endWidth = _arcWidth;
            _arcGauge.useWorldSpace = false;
            _arcGauge.loop = false;

            // 색상 설정
            Color startColor = _bossData?.ColorData?.colorSealRange ?? Color.cyan;
            Color endColor = _bossData?.ColorData?.colorSealReadyPulse ?? Color.white;
            _arcGauge.startColor = startColor;
            _arcGauge.endColor = endColor;

            // 12시 방향(-90도)에서 시계 방향으로 점 배치
            for (int i = 0; i < pointCount; i++)
            {
                float t = (float)i / (pointCount - 1);
                float angle = Mathf.Deg2Rad * (-90f + totalAngle * t);
                float x = Mathf.Cos(angle) * _arcRadius;
                float y = Mathf.Sin(angle) * _arcRadius;
                _arcGauge.SetPosition(i, new Vector3(x, y, 0f));
            }
        }

        // ══════════════════════════════════════════════════════
        // 내부 유틸
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 호 게이지 LineRenderer 자동 생성.
        /// Inspector 에 미연결 시 자식 오브젝트로 생성.
        /// </summary>
        private LineRenderer CreateArcGauge()
        {
            GameObject go = new GameObject("ExecutionArcGauge");
            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.startWidth = _arcWidth;
            lr.endWidth = _arcWidth;
            lr.useWorldSpace = false;
            lr.loop = false;
            lr.sortingLayerName = "Default";
            lr.sortingOrder = 2;

            Debug.Log($"[SealExecutionEffect] {gameObject.name} — ExecutionArcGauge 자동 생성");
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

        /// <summary>
        /// Visual Transform 설정.
        /// Scale 연출 대상이 자기 Transform 이 아닌 경우 설정.
        /// 예: Boss_LeftArm 에 Visual 자식 오브젝트가 별도로 있는 경우.
        /// </summary>
        public void SetVisualTransform(Transform visual)
        {
            if (visual != null)
                _visualTransform = visual;
        }
    }
}