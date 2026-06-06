// ============================================================
// BossWardenAttackRange.cs  v1.1
// Boss_Warden 공격 예고 범위 표시 전담 컴포넌트
//
// [v1.1 수정 — Charge 예고선 너비 + Slam/Sweep 디스크 크기 기준 통일]
//
//   [변경 1] ShowChargeLine width 파라미터 추가
//     기존: ShowChargeLine(startPos, direction, length)
//           → LineRenderer 선 두께 고정 → 히트박스 폭 인식 불가
//
//     변경: ShowChargeLine(startPos, direction, length, width = 0f)
//           → width > 0 이면 startWidth/endWidth 에 적용
//           → BossPattern_Charge.OnWarning 에서
//              ShowChargeLine(bossPos, dir, warningSize.y, warningSize.x) 호출
//           → 예고선 폭이 실제 히트박스 너비에 대응
//
//   [변경 2] ShowSlamDisc — slamWarningRadius 대신 slamHitRadius 기준 사용
//     기존: 패턴에서 slamWarningRadius 를 넘겨서 실제 히트보다 크게 표시
//     변경: 패턴에서 slamHitRadius 를 넘겨서 정확한 공격 범위 표시
//           (예고 범위 오해 방지 — 디스크 크기 = 실제 피격 크기)
//           ※ DataSO 값 조정은 Inspector 에서 직접 수행
//
//   [변경 3] ShowSweepDisc — sweepHitRadius 기준 동일 원칙 적용
//
// [v1.0 유지]
//   Slam FlashAndHideSlamDisc
//   SweepDisc UpdateSweepDiscPosition
//   GuardBreak ShowGuardBreakDisc (제거 예정이나 코드 유지)
//   RageCharge ShowRageChargeLine 3개
//   SealRange / CoreRange DrawDashedCircle
//   HideAll
//
// [namespace] SEAL
// ============================================================

using UnityEngine;
using DG.Tweening;

namespace SEAL
{
    /// <summary>
    /// Boss_Warden 공격 예고 범위 표시 전담 컴포넌트. (v1.1)
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

        [Tooltip("Slam 예고 디스크 0번 (1페이즈 단일 / 2페이즈 첫 번째).")]
        [SerializeField] private SpriteRenderer _slamDisc0;

        [Tooltip("Slam 예고 디스크 1번 (2페이즈 두 번째).")]
        [SerializeField] private SpriteRenderer _slamDisc1;

        [Header("── Sweep 예고 디스크 ──────────────────────")]

        [Tooltip("Sweep 예고 디스크.")]
        [SerializeField] private SpriteRenderer _sweepDisc;

        [Header("── GuardBreak 예고 디스크 ──────────────────────")]

        [Tooltip("GuardBreak 예고 직사각형 디스크.")]
        [SerializeField] private SpriteRenderer _guardBreakDisc;

        [Header("── RageCharge 예고선 ──────────────────────")]

        [Tooltip("RageCharge 예고선 3개. 순서대로 연결.")]
        [SerializeField] private LineRenderer[] _rageChargeLines = new LineRenderer[3];

        [Header("── 봉인 집행 범위 점선 ──────────────────────")]

        [Tooltip("봉인 집행 가능 범위 점선 LineRenderer.")]
        [SerializeField] private LineRenderer _sealRangeCircle;

        [Tooltip("코어 해제 가능 범위 점선 LineRenderer.")]
        [SerializeField] private LineRenderer _coreRangeCircle;

        [Header("── DataSO ──────────────────────")]

        [Tooltip("BossWardenDataSO.")]
        [SerializeField] private BossWardenDataSO _data;

        // ══════════════════════════════════════════════════════
        // 내부 Tween
        // ══════════════════════════════════════════════════════

        private Tween _slamDisc0Tween;
        private Tween _slamDisc1Tween;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            HideAll();
        }

        private void OnDestroy()
        {
            _slamDisc0Tween?.Kill();
            _slamDisc1Tween?.Kill();
        }

        // ══════════════════════════════════════════════════════
        // 초기화
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// BossWardenDataSO 주입.
        /// BossWardenCore.InjectData() 에서 호출.
        /// </summary>
        public void Initialize(BossWardenDataSO data)
        {
            _data = data;
        }

        // ══════════════════════════════════════════════════════
        // Charge 예고선 — v1.1 수정
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Charge 돌진 예고선을 표시한다.
        ///
        /// [v1.1 변경]
        ///   width 파라미터 추가.
        ///   width > 0 이면 LineRenderer.startWidth/endWidth 에 적용.
        ///   → 예고선 폭이 실제 히트박스 너비에 대응.
        ///   BossPattern_Charge 에서 chargeWarningSize.x 를 width 로 전달.
        ///
        /// [호출 예시]
        ///   _attackRange.ShowChargeLine(
        ///       bossPos,
        ///       direction,
        ///       _data.chargeWarningSize.y,   // 길이
        ///       _data.chargeWarningSize.x);  // 너비 (v1.1 추가)
        /// </summary>
        /// <param name="startPos">보스 월드 위치 (예고선 시작점).</param>
        /// <param name="direction">돌진 방향 (정규화).</param>
        /// <param name="length">예고선 길이 (chargeWarningSize.y).</param>
        /// <param name="width">
        /// 예고선 너비 (chargeWarningSize.x).
        /// 0 이하이면 LineRenderer Inspector 설정값 유지.
        /// </param>
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

            // v1.1: 너비 적용
            if (width > 0f)
            {
                _chargeLineRenderer.startWidth = width;
                _chargeLineRenderer.endWidth = width;
            }

            // 색상
            Color c = _data != null
                ? _data.colorWarningRange
                : new Color(1f, 0f, 0f, 0.4f);
            _chargeLineRenderer.startColor = c;
            _chargeLineRenderer.endColor = c;
        }

        /// <summary>Charge 예고선을 숨긴다.</summary>
        public void HideChargeLine()
        {
            if (_chargeLineRenderer == null) return;
            _chargeLineRenderer.gameObject.SetActive(false);
        }

        // ══════════════════════════════════════════════════════
        // Slam 예고 디스크
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Slam 예고 디스크를 지정 위치에 표시한다.
        ///
        /// [v1.1 기준 원칙]
        ///   radius = slamHitRadius (실제 히트 반경) 을 전달해야 함.
        ///   예고 디스크 크기 = 실제 피격 범위와 동일하게 표시.
        ///   플레이어가 디스크 안에 있으면 반드시 맞는 구조.
        ///
        ///   BossPattern_Slam.OnWarning 에서:
        ///     기존: _attackRange.ShowSlamDisc(target, _data.slamWarningRadius)
        ///     변경: _attackRange.ShowSlamDisc(target, _data.slamHitRadius)
        /// </summary>
        /// <param name="worldPos">플레이어 월드 위치 (Warning 시 스냅).</param>
        /// <param name="radius">히트 반경 (slamHitRadius).</param>
        /// <param name="discIndex">디스크 인덱스 (0 = 단일/첫 번째, 1 = 두 번째).</param>
        public void ShowSlamDisc(Vector2 worldPos, float radius, int discIndex = 0)
        {
            SpriteRenderer disc = discIndex == 0 ? _slamDisc0 : _slamDisc1;
            if (disc == null) return;

            disc.transform.position = new Vector3(worldPos.x, worldPos.y, 0f);
            float diameter = radius * 2f;
            disc.transform.localScale = new Vector3(diameter, diameter, 1f);

            Color c = _data != null ? _data.colorWarningRange : new Color(1f, 0f, 0f, 0.4f);
            disc.color = c;
            disc.gameObject.SetActive(true);
        }

        /// <summary>
        /// Slam Active 히트 순간 디스크를 밝게 플래시 후 제거한다.
        /// </summary>
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

        /// <summary>Slam 디스크를 즉시 숨긴다.</summary>
        public void HideSlamDisc(int discIndex = 0)
        {
            SpriteRenderer disc = discIndex == 0 ? _slamDisc0 : _slamDisc1;
            if (disc == null) return;
            disc.gameObject.SetActive(false);
        }

        // ══════════════════════════════════════════════════════
        // Sweep 예고 디스크
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Sweep 예고 디스크를 표시한다.
        ///
        /// [v1.1 기준 원칙]
        ///   radius = sweepHitRadius (실제 히트 반경) 을 전달.
        ///   디스크 크기 = 실제 피격 범위와 동일.
        ///
        ///   BossPattern_Sweep.OnWarning 에서:
        ///     기존: _attackRange.ShowSweepDisc(bossPos, _data.sweepWarningRadius)
        ///     변경: _attackRange.ShowSweepDisc(bossPos, _data.sweepHitRadius)
        /// </summary>
        /// <param name="bossPos">보스 월드 위치.</param>
        /// <param name="radius">히트 반경 (sweepHitRadius).</param>
        public void ShowSweepDisc(Vector2 bossPos, float radius)
        {
            if (_sweepDisc == null) return;

            _sweepDisc.transform.position = new Vector3(bossPos.x, bossPos.y, 0f);
            float diameter = radius * 2f;
            _sweepDisc.transform.localScale = new Vector3(diameter, diameter, 1f);

            Color c = _data != null ? _data.colorWarningRange : new Color(1f, 0f, 0f, 0.4f);
            _sweepDisc.color = c;
            _sweepDisc.gameObject.SetActive(true);
        }

        /// <summary>
        /// Sweep 디스크 위치를 보스 위치로 업데이트한다.
        /// Active 구간 중 매 프레임 호출.
        /// </summary>
        public void UpdateSweepDiscPosition(Vector2 bossPos)
        {
            if (_sweepDisc == null || !_sweepDisc.gameObject.activeSelf) return;
            _sweepDisc.transform.position = new Vector3(bossPos.x, bossPos.y, 0f);
        }

        /// <summary>Sweep 디스크를 숨긴다.</summary>
        public void HideSweepDisc()
        {
            if (_sweepDisc == null) return;
            _sweepDisc.gameObject.SetActive(false);
        }

        // ══════════════════════════════════════════════════════
        // GuardBreak 예고 디스크 (유지 — 추후 제거 가능)
        // ══════════════════════════════════════════════════════

        /// <summary>GuardBreak 직사각형 예고 디스크를 표시한다.</summary>
        public void ShowGuardBreakDisc(Vector2 bossPos, Vector2 direction, Vector2 size)
        {
            if (_guardBreakDisc == null) return;

            Vector2 offset = direction * (size.y * 0.5f);
            Vector3 pos = new Vector3(bossPos.x + offset.x, bossPos.y + offset.y, 0f);

            _guardBreakDisc.transform.position = pos;
            _guardBreakDisc.transform.localScale = new Vector3(size.x, size.y, 1f);

            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            _guardBreakDisc.transform.rotation = Quaternion.Euler(0f, 0f, angle - 90f);

            Color c = _data != null ? _data.colorWarningRange : new Color(1f, 0f, 0f, 0.4f);
            _guardBreakDisc.color = c;
            _guardBreakDisc.gameObject.SetActive(true);
        }

        /// <summary>GuardBreak 디스크를 숨긴다.</summary>
        public void HideGuardBreakDisc()
        {
            if (_guardBreakDisc == null) return;
            _guardBreakDisc.gameObject.SetActive(false);
        }

        // ══════════════════════════════════════════════════════
        // RageCharge 예고선 3개
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// RageCharge 3연 돌진 예고선 1개를 표시한다.
        /// 0.3초 간격으로 순차 호출.
        /// 밝기: 0번 1.0 / 1번 0.75 / 2번 0.5
        /// </summary>
        public void ShowRageChargeLine(
            int index,
            Vector2 startPos,
            Vector2 direction,
            float length)
        {
            if (index < 0 || index >= _rageChargeLines.Length) return;
            LineRenderer lr = _rageChargeLines[index];
            if (lr == null) return;

            lr.gameObject.SetActive(true);

            Vector3 start = new Vector3(startPos.x, startPos.y, 0f);
            Vector3 end = start + new Vector3(direction.x, direction.y, 0f) * length;
            lr.positionCount = 2;
            lr.SetPosition(0, start);
            lr.SetPosition(1, end);

            float alpha = 1.0f - index * 0.25f;
            Color baseColor = _data != null ? _data.colorWarningRange : new Color(1f, 0f, 0f, 0.4f);
            Color c = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * alpha);
            lr.startColor = c;
            lr.endColor = c;
        }

        /// <summary>RageCharge 예고선 전체를 숨긴다.</summary>
        public void HideAllRageChargeLines()
        {
            foreach (var lr in _rageChargeLines)
            {
                if (lr != null) lr.gameObject.SetActive(false);
            }
        }

        // ══════════════════════════════════════════════════════
        // 봉인 집행 가능 범위 점선 원
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 봉인 집행 가능 범위 점선 원을 표시한다.
        /// 부위 봉인도 100% 도달 시 SealReadyNotifier 에서 호출.
        /// </summary>
        public void ShowSealRange(Vector2 centerPos, float radius)
        {
            if (_sealRangeCircle == null) return;

            DrawDashedCircle(
                _sealRangeCircle,
                centerPos,
                radius,
                segments: 32,
                color: _data != null ? _data.ColorData.colorSealRange : Color.blue);

            _sealRangeCircle.gameObject.SetActive(true);
        }

        /// <summary>봉인 집행 범위 점선 원을 숨긴다.</summary>
        public void HideSealRange()
        {
            if (_sealRangeCircle == null) return;
            _sealRangeCircle.gameObject.SetActive(false);
        }

        // ══════════════════════════════════════════════════════
        // 코어 해제 가능 범위 점선 원
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 코어 해제 가능 범위 점선 원을 표시한다.
        /// 그로기 진입 + 코어 활성화 시 호출.
        /// </summary>
        public void ShowCoreRange(Vector2 centerPos, float radius)
        {
            if (_coreRangeCircle == null) return;

            DrawDashedCircle(
                _coreRangeCircle,
                centerPos,
                radius,
                segments: 32,
                color: _data != null ? _data.ColorData.colorCoreRange : Color.yellow);

            _coreRangeCircle.gameObject.SetActive(true);
        }

        /// <summary>코어 해제 범위 점선 원을 숨긴다.</summary>
        public void HideCoreRange()
        {
            if (_coreRangeCircle == null) return;
            _coreRangeCircle.gameObject.SetActive(false);
        }

        // ══════════════════════════════════════════════════════
        // 전체 숨기기
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 모든 예고 범위 오브젝트를 숨긴다.
        /// Awake 초기화 + 그로기/DilPhase 진입 시 호출.
        /// </summary>
        public void HideAll()
        {
            HideChargeLine();
            HideSlamDisc(0);
            HideSlamDisc(1);
            HideSweepDisc();
            HideGuardBreakDisc();
            HideAllRageChargeLines();
            HideSealRange();
            HideCoreRange();
        }

        // ══════════════════════════════════════════════════════
        // 내부 유틸 — 점선 원 그리기
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// LineRenderer 로 원을 그린다.
        /// useWorldSpace = true, Loop = true.
        /// </summary>
        private void DrawDashedCircle(
            LineRenderer lr,
            Vector2 center,
            float radius,
            int segments,
            Color color)
        {
            lr.positionCount = segments + 1;
            lr.startColor = color;
            lr.endColor = color;
            lr.loop = true;
            lr.useWorldSpace = true;

            for (int i = 0; i <= segments; i++)
            {
                float angle = i * 2f * Mathf.PI / segments;
                float x = center.x + Mathf.Cos(angle) * radius;
                float y = center.y + Mathf.Sin(angle) * radius;
                lr.SetPosition(i, new Vector3(x, y, 0f));
            }
        }
    }
}