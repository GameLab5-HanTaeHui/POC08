// ============================================================
// BossWardenAttackRange.cs  v1.2
// Boss_Warden 공격 예고 범위 표시 전담 컴포넌트
//
// [수정 내용 — v1.2]
//   공격 예고 범위 점멸(Pulse) 메서드 추가
//   패턴 Warning 진입 시 AttackRange 디스크/선이 점멸하는 방식
//
//   추가 메서드:
//     StartChargePulse()      — Charge 예고선 점멸 시작
//     StartSlamPulse(int)     — Slam 디스크 점멸 시작
//     StartSweepPulse()       — Sweep 디스크 점멸 시작
//     StartGuardBreakPulse()  — GuardBreak 디스크 점멸 시작
//     StartRageChargePulse()  — RageCharge 예고선 점멸 시작
//     StopAllPulse()          — 모든 점멸 중단 (Active 진입 / HideAll 시 호출)
//
// [namespace] SEAL
// ============================================================

using UnityEngine;
using DG.Tweening;

namespace SEAL
{
    /// <summary>
    /// Boss_Warden 공격 예고 범위 표시 전담 컴포넌트. (v1.2)
    ///
    /// ────────────────────────────────────────────────────
    /// [점멸 흐름]
    ///   패턴 Warning 진입
    ///   → ShowXxx() 로 위치/크기 설정
    ///   → StartXxxPulse() 로 Alpha 점멸 시작
    ///   패턴 Active 진입
    ///   → StopAllPulse() 로 점멸 중단 + Alpha 고정
    ///   패턴 Recovery / HideAll
    ///   → HideXxx() 로 비활성
    /// ────────────────────────────────────────────────────
    /// </summary>
    public class BossWardenAttackRange : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── Charge 예고선 ──────────────────────")]
        [Tooltip("Charge 돌진 예고 LineRenderer.")]
        [SerializeField] private LineRenderer _chargeLineRenderer;

        [Header("── Slam 예고 디스크 ──────────────────────")]
        [Tooltip("Slam 예고 디스크 0번.")]
        [SerializeField] private SpriteRenderer _slamDisc0;
        [Tooltip("Slam 예고 디스크 1번.")]
        [SerializeField] private SpriteRenderer _slamDisc1;

        [Header("── Sweep 예고 디스크 ──────────────────────")]
        [Tooltip("Sweep 예고 디스크.")]
        [SerializeField] private SpriteRenderer _sweepDisc;

        [Header("── GuardBreak 예고 디스크 ──────────────────────")]
        [Tooltip("GuardBreak 예고 직사각형 디스크.")]
        [SerializeField] private SpriteRenderer _guardBreakDisc;

        [Header("── RageCharge 예고선 ──────────────────────")]
        [Tooltip("RageCharge 예고선 3개.")]
        [SerializeField] private LineRenderer[] _rageChargeLines = new LineRenderer[3];

        [Header("── 봉인 집행 범위 점선 ──────────────────────")]
        [Tooltip("봉인 집행 가능 범위 점선 LineRenderer.")]
        [SerializeField] private LineRenderer _sealRangeCircle;
        [Tooltip("코어 해제 가능 범위 점선 LineRenderer.")]
        [SerializeField] private LineRenderer _coreRangeCircle;

        [Header("── DataSO ──────────────────────")]
        [Tooltip("BossWardenDataSO. Initialize() 에서 주입.")]
        [SerializeField] private BossWardenDataSO _data;

        [Header("── 점멸 설정 ──────────────────────")]
        [Tooltip("Warning 점멸 한 주기 (초). 권장: 0.3~0.5.")]
        [Min(0.05f)]
        [SerializeField] private float _pulsePeriod = 0.35f;

        [Tooltip("점멸 최소 Alpha (0=완전투명). 권장: 0.05~0.1.")]
        [Range(0f, 0.5f)]
        [SerializeField] private float _pulseMinAlpha = 0.05f;

        [Tooltip("점멸 최대 Alpha. 권장: 0.5~0.7.")]
        [Range(0.3f, 1f)]
        [SerializeField] private float _pulseMaxAlpha = 0.55f;

        // ══════════════════════════════════════════════════════
        // DOTween 핸들
        // ══════════════════════════════════════════════════════

        private Tween _slamDisc0Tween;
        private Tween _slamDisc1Tween;
        private Tween _sweepDiscTween;
        private Tween _guardBreakDiscTween;
        private Tween _chargeLineTween;
        private Tween[] _rageLineTweens = new Tween[3];

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void OnDestroy()
        {
            StopAllPulse();
        }

        // ══════════════════════════════════════════════════════
        // 초기화
        // ══════════════════════════════════════════════════════

        /// <summary>BossWardenCore.Initialize() 에서 DataSO 주입.</summary>
        public void Initialize(BossWardenDataSO data)
        {
            _data = data;
        }

        // ══════════════════════════════════════════════════════
        // Charge 예고선
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Charge 돌진 예고선 표시.
        /// ShowChargeLine() 호출 후 StartChargePulse() 로 점멸 시작.
        /// </summary>
        public void ShowChargeLine(
            Vector2 startPos,
            Vector2 direction,
            float length,
            float width = 0f)
        {
            if (_chargeLineRenderer == null) return;

            _chargeLineRenderer.gameObject.SetActive(true);

            Vector3 start = new Vector3(startPos.x, startPos.y, 0f);
            Vector3 end = start + new Vector3(direction.x, direction.y, 0f) * length;

            _chargeLineRenderer.positionCount = 2;
            _chargeLineRenderer.SetPosition(0, start);
            _chargeLineRenderer.SetPosition(1, end);

            if (width > 0f)
            {
                _chargeLineRenderer.startWidth = width;
                _chargeLineRenderer.endWidth = width;
            }

            Color c = _data != null ? _data.colorWarningRange : new Color(1f, 0f, 0f, 0.4f);
            c.a = _pulseMaxAlpha;
            _chargeLineRenderer.startColor = c;
            _chargeLineRenderer.endColor = c;
        }

        /// <summary>
        /// Charge 예고선 점멸 시작.
        /// Warning 진입 직후 ShowChargeLine() 다음에 호출.
        /// </summary>
        public void StartChargePulse()
        {
            if (_chargeLineRenderer == null) return;

            _chargeLineTween?.Kill();

            Color baseColor = _chargeLineRenderer.startColor;
            Color minColor = baseColor;
            minColor.a = _pulseMinAlpha;
            Color maxColor = baseColor;
            maxColor.a = _pulseMaxAlpha;

            _chargeLineRenderer.startColor = maxColor;
            _chargeLineRenderer.endColor = maxColor;

            _chargeLineTween = DOTween.To(
                    () => _chargeLineRenderer.startColor,
                    c => { _chargeLineRenderer.startColor = c; _chargeLineRenderer.endColor = c; },
                    minColor,
                    _pulsePeriod * 0.5f)
                .SetLoops(-1, LoopType.Yoyo)
                .SetEase(Ease.InOutSine)
                .SetUpdate(true);
        }

        /// <summary>Charge 예고선 숨김.</summary>
        public void HideChargeLine()
        {
            _chargeLineTween?.Kill();
            if (_chargeLineRenderer != null)
                _chargeLineRenderer.gameObject.SetActive(false);
        }

        // ══════════════════════════════════════════════════════
        // Slam 예고 디스크
        // ══════════════════════════════════════════════════════

        /// <summary>Slam 예고 디스크 표시.</summary>
        public void ShowSlamDisc(Vector2 worldPos, float radius, int discIndex = 0)
        {
            SpriteRenderer disc = discIndex == 0 ? _slamDisc0 : _slamDisc1;
            if (disc == null) return;

            disc.transform.position = new Vector3(worldPos.x, worldPos.y, 0f);
            float diameter = radius * 2f;
            disc.transform.localScale = new Vector3(diameter, diameter, 1f);

            Color c = _data != null ? _data.colorWarningRange : new Color(1f, 0f, 0f, 0.4f);
            c.a = _pulseMaxAlpha;
            disc.color = c;
            disc.gameObject.SetActive(true);
        }

        /// <summary>
        /// Slam 예고 디스크 점멸 시작.
        /// Warning 진입 직후 ShowSlamDisc() 다음에 호출.
        /// </summary>
        public void StartSlamPulse(int discIndex = 0)
        {
            SpriteRenderer disc = discIndex == 0 ? _slamDisc0 : _slamDisc1;
            ref Tween tween = ref (discIndex == 0 ? ref _slamDisc0Tween : ref _slamDisc1Tween);

            if (disc == null) return;
            tween?.Kill();

            Color maxColor = disc.color;
            maxColor.a = _pulseMaxAlpha;
            Color minColor = disc.color;
            minColor.a = _pulseMinAlpha;

            disc.color = maxColor;
            tween = disc.DOColor(minColor, _pulsePeriod * 0.5f)
                .SetLoops(-1, LoopType.Yoyo)
                .SetEase(Ease.InOutSine)
                .SetUpdate(true);
        }

        /// <summary>Slam Active 히트 순간 디스크 플래시 후 제거.</summary>
        public void FlashAndHideSlamDisc(int discIndex = 0)
        {
            SpriteRenderer disc = discIndex == 0 ? _slamDisc0 : _slamDisc1;
            ref Tween tween = ref (discIndex == 0 ? ref _slamDisc0Tween : ref _slamDisc1Tween);

            if (disc == null || !disc.gameObject.activeSelf) return;

            tween?.Kill();
            disc.color = Color.white;
            tween = disc.DOColor(new Color(1f, 1f, 1f, 0f), 0.1f)
                .SetEase(Ease.OutCubic)
                .SetUpdate(true)
                .OnComplete(() => disc.gameObject.SetActive(false));
        }

        /// <summary>Slam 디스크 즉시 숨김.</summary>
        public void HideSlamDisc(int discIndex = 0)
        {
            SpriteRenderer disc = discIndex == 0 ? _slamDisc0 : _slamDisc1;
            ref Tween tween = ref (discIndex == 0 ? ref _slamDisc0Tween : ref _slamDisc1Tween);
            tween?.Kill();
            if (disc != null) disc.gameObject.SetActive(false);
        }

        // ══════════════════════════════════════════════════════
        // Sweep 예고 디스크
        // ══════════════════════════════════════════════════════

        /// <summary>Sweep 예고 디스크 표시.</summary>
        public void ShowSweepDisc(Vector2 worldPos, float radius)
        {
            if (_sweepDisc == null) return;

            _sweepDisc.transform.position = new Vector3(worldPos.x, worldPos.y, 0f);
            float diameter = radius * 2f;
            _sweepDisc.transform.localScale = new Vector3(diameter, diameter, 1f);

            Color c = _data != null ? _data.colorWarningRange : new Color(1f, 0f, 0f, 0.4f);
            c.a = _pulseMaxAlpha;
            _sweepDisc.color = c;
            _sweepDisc.gameObject.SetActive(true);
        }

        /// <summary>Sweep 디스크 위치 갱신 (보스 중심 추적).</summary>
        public void UpdateSweepDiscPosition(Vector2 worldPos)
        {
            if (_sweepDisc == null || !_sweepDisc.gameObject.activeSelf) return;
            _sweepDisc.transform.position = new Vector3(worldPos.x, worldPos.y, 0f);
        }

        /// <summary>
        /// Sweep 예고 디스크 점멸 시작.
        /// Warning 진입 직후 ShowSweepDisc() 다음에 호출.
        /// </summary>
        public void StartSweepPulse()
        {
            if (_sweepDisc == null) return;
            _sweepDiscTween?.Kill();

            Color maxColor = _sweepDisc.color;
            maxColor.a = _pulseMaxAlpha;
            Color minColor = _sweepDisc.color;
            minColor.a = _pulseMinAlpha;

            _sweepDisc.color = maxColor;
            _sweepDiscTween = _sweepDisc.DOColor(minColor, _pulsePeriod * 0.5f)
                .SetLoops(-1, LoopType.Yoyo)
                .SetEase(Ease.InOutSine)
                .SetUpdate(true);
        }

        /// <summary>Sweep 디스크 숨김.</summary>
        public void HideSweepDisc()
        {
            _sweepDiscTween?.Kill();
            if (_sweepDisc != null) _sweepDisc.gameObject.SetActive(false);
        }

        // ══════════════════════════════════════════════════════
        // GuardBreak 예고 디스크
        // ══════════════════════════════════════════════════════

        /// <summary>GuardBreak 예고 디스크 표시.</summary>
        public void ShowGuardBreakDisc(Vector2 worldPos, Vector2 size, float angle)
        {
            if (_guardBreakDisc == null) return;

            _guardBreakDisc.transform.position = new Vector3(worldPos.x, worldPos.y, 0f);
            _guardBreakDisc.transform.localScale = new Vector3(size.x, size.y, 1f);
            _guardBreakDisc.transform.eulerAngles = new Vector3(0f, 0f, angle);

            Color c = _data != null ? _data.colorWarningRange : new Color(1f, 0f, 0f, 0.4f);
            c.a = _pulseMaxAlpha;
            _guardBreakDisc.color = c;
            _guardBreakDisc.gameObject.SetActive(true);
        }

        /// <summary>
        /// GuardBreak 예고 디스크 점멸 시작.
        /// Warning 진입 직후 ShowGuardBreakDisc() 다음에 호출.
        /// </summary>
        public void StartGuardBreakPulse()
        {
            if (_guardBreakDisc == null) return;
            _guardBreakDiscTween?.Kill();

            Color maxColor = _guardBreakDisc.color;
            maxColor.a = _pulseMaxAlpha;
            Color minColor = _guardBreakDisc.color;
            minColor.a = _pulseMinAlpha;

            _guardBreakDisc.color = maxColor;
            _guardBreakDiscTween = _guardBreakDisc.DOColor(minColor, _pulsePeriod * 0.5f)
                .SetLoops(-1, LoopType.Yoyo)
                .SetEase(Ease.InOutSine)
                .SetUpdate(true);
        }

        /// <summary>GuardBreak 디스크 숨김.</summary>
        public void HideGuardBreakDisc()
        {
            _guardBreakDiscTween?.Kill();
            if (_guardBreakDisc != null) _guardBreakDisc.gameObject.SetActive(false);
        }

        // ══════════════════════════════════════════════════════
        // RageCharge 예고선
        // ══════════════════════════════════════════════════════

        /// <summary>RageCharge 예고선 표시 (인덱스 0~2).</summary>
        public void ShowRageChargeLine(int index, Vector2 startPos, Vector2 direction, float length)
        {
            if (index < 0 || index >= _rageChargeLines.Length) return;
            var line = _rageChargeLines[index];
            if (line == null) return;

            line.gameObject.SetActive(true);

            Vector3 start = new Vector3(startPos.x, startPos.y, 0f);
            Vector3 end = start + new Vector3(direction.x, direction.y, 0f) * length;

            line.positionCount = 2;
            line.SetPosition(0, start);
            line.SetPosition(1, end);

            Color c = _data != null ? _data.colorWarningRange : new Color(1f, 0f, 0f, 0.4f);
            c.a = _pulseMaxAlpha;
            line.startColor = c;
            line.endColor = c;
        }

        /// <summary>
        /// RageCharge 예고선 3개 전부 점멸 시작.
        /// Warning 진입 직후 ShowRageChargeLine() 3번 호출 후 사용.
        /// </summary>
        public void StartRageChargePulse()
        {
            for (int i = 0; i < _rageChargeLines.Length; i++)
            {
                var line = _rageChargeLines[i];
                if (line == null) continue;

                _rageLineTweens[i]?.Kill();

                Color baseColor = line.startColor;
                Color minColor = baseColor; minColor.a = _pulseMinAlpha;
                Color maxColor = baseColor; maxColor.a = _pulseMaxAlpha;

                line.startColor = maxColor;
                line.endColor = maxColor;

                int idx = i;
                _rageLineTweens[idx] = DOTween.To(
                        () => _rageChargeLines[idx].startColor,
                        c => { _rageChargeLines[idx].startColor = c; _rageChargeLines[idx].endColor = c; },
                        minColor,
                        _pulsePeriod * 0.5f)
                    .SetLoops(-1, LoopType.Yoyo)
                    .SetEase(Ease.InOutSine)
                    .SetUpdate(true);
            }
        }

        /// <summary>RageCharge 예고선 전부 숨김.</summary>
        public void HideAllRageChargeLines()
        {
            for (int i = 0; i < _rageChargeLines.Length; i++)
            {
                _rageLineTweens[i]?.Kill();
                if (_rageChargeLines[i] != null)
                    _rageChargeLines[i].gameObject.SetActive(false);
            }
        }

        // ══════════════════════════════════════════════════════
        // 점멸 전체 중단
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 모든 점멸 DOTween 중단.
        /// Active 진입 시 또는 HideAll() 전에 호출.
        /// 각 렌더러의 Alpha 는 현재 상태 유지.
        /// </summary>
        public void StopAllPulse()
        {
            _chargeLineTween?.Kill();
            _slamDisc0Tween?.Kill();
            _slamDisc1Tween?.Kill();
            _sweepDiscTween?.Kill();
            _guardBreakDiscTween?.Kill();
            for (int i = 0; i < _rageLineTweens.Length; i++)
                _rageLineTweens[i]?.Kill();
        }

        // ══════════════════════════════════════════════════════
        // 봉인 집행 범위 점선 (기존 유지)
        // ══════════════════════════════════════════════════════

        /// <summary>봉인 집행 범위 점선 표시.</summary>
        public void ShowSealRange(Vector2 center, float radius)
        {
            DrawDashedCircle(_sealRangeCircle, center, radius,
                _data != null ? _data.ColorData?.colorSealRange ?? Color.cyan : Color.cyan);
        }

        /// <summary>코어 해제 범위 점선 표시.</summary>
        public void ShowCoreRange(Vector2 center, float radius)
        {
            DrawDashedCircle(_coreRangeCircle, center, radius,
                _data != null ? _data.ColorData?.colorCoreRange ?? Color.yellow : Color.yellow);
        }

        /// <summary>봉인/코어 범위 점선 모두 숨김.</summary>
        public void HideSealRange()
        {
            if (_sealRangeCircle != null) _sealRangeCircle.gameObject.SetActive(false);
        }

        public void HideCoreRange()
        {
            if (_coreRangeCircle != null) _coreRangeCircle.gameObject.SetActive(false);
        }

        // ══════════════════════════════════════════════════════
        // 전체 숨김
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 모든 예고 범위 숨김.
        /// DilPhase 진입 / 보스 처치 시 호출.
        /// </summary>
        public void HideAll()
        {
            StopAllPulse();
            HideChargeLine();
            HideSlamDisc(0);
            HideSlamDisc(1);
            HideSweepDisc();
            HideGuardBreakDisc();
            HideAllRageChargeLines();
        }

        // ══════════════════════════════════════════════════════
        // 내부 유틸
        // ══════════════════════════════════════════════════════

        /// <summary>점선 원 LineRenderer 그리기.</summary>
        private void DrawDashedCircle(LineRenderer lr, Vector2 center, float radius, Color color)
        {
            if (lr == null) return;

            const int segments = 60;
            lr.positionCount = segments + 1;
            lr.startColor = color;
            lr.endColor = color;
            lr.gameObject.SetActive(true);

            for (int i = 0; i <= segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                float x = center.x + Mathf.Cos(angle) * radius;
                float y = center.y + Mathf.Sin(angle) * radius;
                lr.SetPosition(i, new Vector3(x, y, 0f));
            }
        }
    }
}