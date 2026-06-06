// ============================================================
// BossWardenAttackRange.cs  v1.0
// Boss_Warden 공격 예고 범위 표시 전담 컴포넌트
//
// [설계 원칙 — README #20 패턴 설계 원칙]
//   패턴의 목적은 플레이어를 죽이는 것이 아니라
//   패턴 회피와 봉인 공략 사이의 긴장감 조성이다.
//   따라서 예고 범위는 항상 실제 히트박스보다 10~20% 넉넉하게 표시한다.
//
// [예고 범위 종류]
//   Charge     : LineRenderer 직선 예고선
//   Slam       : SpriteRenderer 원형 반투명 디스크 (1~2개)
//   Sweep      : SpriteRenderer 원형 반투명 디스크 (회전)
//   GuardBreak : SpriteRenderer 직사각형 반투명 디스크
//   RageCharge : LineRenderer 직선 예고선 3개 순차
//   SealRange  : LineRenderer 점선 원 (봉인 집행 가능 범위)
//   CoreRange  : LineRenderer 점선 원 (코어 해제 가능 범위)
//
// [모든 예고 오브젝트]
//   기본 SetActive = false.
//   각 패턴의 Warning 시작 시 Show() 호출 → SetActive true.
//   Active 시작 or Warning 종료 시 Hide() 호출 → SetActive false.
//
// [쿼터뷰 / 비스듬한 탑뷰 고려]
//   예고 디스크는 바닥에 표시된다고 가정.
//   SpriteRenderer SortingLayer = Ground 로 설정하여 캐릭터 아래에 표시.
//
// [namespace]
//   namespace : SEAL
// ============================================================

using UnityEngine;
using DG.Tweening;

namespace SEAL
{
    /// <summary>
    /// Boss_Warden 공격 예고 범위 표시 전담 컴포넌트. (v1.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [패턴 스크립트에서 호출 예시]
    ///   // Charge Warning 시작
    ///   _attackRange.ShowChargeLine(bossPos, direction, data.chargeWarningSize.y);
    ///   // Charge Active 시작
    ///   _attackRange.HideChargeLine();
    ///
    ///   // Slam Warning 시작 (플레이어 위치 고정)
    ///   _attackRange.ShowSlamDisc(targetWorldPos, data.slamWarningRadius);
    ///   // Slam Active 히트 후
    ///   _attackRange.HideSlamDisc(0);
    ///
    ///   // 봉인 집행 가능 범위 (ArmPart 에서 호출)
    ///   _attackRange.ShowSealRange(armPartPos, data.sealExecutionRange);
    ///   _attackRange.HideSealRange();
    /// ────────────────────────────────────────────────────
    /// </summary>
    public class BossWardenAttackRange : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        // Inspector — 오브젝트 연결
        // ══════════════════════════════════════════════════════

        [Header("── Charge 예고선 ──────────────────────")]

        /// <summary>
        /// Charge 돌진 방향 직선 예고 LineRenderer.
        /// AttackRangeVisuals/ChargeLine 오브젝트.
        /// </summary>
        [Tooltip("Charge 돌진 예고 LineRenderer.")]
        [SerializeField] private LineRenderer _chargeLineRenderer;

        [Header("── Slam 예고 디스크 ──────────────────────")]

        /// <summary>
        /// Slam 예고 원형 디스크 0번 (1페이즈 단일 / 2페이즈 첫 번째).
        /// AttackRangeVisuals/DiscSlam0 오브젝트.
        /// </summary>
        [Tooltip("Slam 예고 디스크 0번.")]
        [SerializeField] private SpriteRenderer _slamDisc0;

        /// <summary>
        /// Slam 예고 원형 디스크 1번 (2페이즈 두 번째).
        /// AttackRangeVisuals/DiscSlam1 오브젝트.
        /// </summary>
        [Tooltip("Slam 예고 디스크 1번 (2페이즈용).")]
        [SerializeField] private SpriteRenderer _slamDisc1;

        [Header("── Sweep 예고 디스크 ──────────────────────")]

        /// <summary>
        /// Sweep 원형 예고 디스크.
        /// AttackRangeVisuals/DiscSweep 오브젝트.
        /// </summary>
        [Tooltip("Sweep 예고 디스크.")]
        [SerializeField] private SpriteRenderer _sweepDisc;

        [Header("── GuardBreak 예고 디스크 ──────────────────────")]

        /// <summary>
        /// GuardBreak 직사각형 예고 디스크.
        /// AttackRangeVisuals/DiscGuardBreak 오브젝트.
        /// </summary>
        [Tooltip("GuardBreak 예고 직사각형 디스크.")]
        [SerializeField] private SpriteRenderer _guardBreakDisc;

        [Header("── RageCharge 예고선 ──────────────────────")]

        /// <summary>
        /// RageCharge 3연 돌진 예고 LineRenderer 배열 (3개).
        /// </summary>
        [Tooltip("RageCharge 예고선 3개. 순서대로 연결.")]
        [SerializeField] private LineRenderer[] _rageChargeLines = new LineRenderer[3];

        [Header("── 범위 점선 ──────────────────────")]

        /// <summary>
        /// 봉인 집행 가능 범위 점선 LineRenderer.
        /// 부위 봉인도 100% 도달 시 표시.
        /// </summary>
        [Tooltip("봉인 집행 가능 범위 점선 LineRenderer.")]
        [SerializeField] private LineRenderer _sealRangeCircle;

        /// <summary>
        /// 코어 해제 가능 범위 점선 LineRenderer.
        /// 그로기 진입 + 코어 활성화 시 표시.
        /// </summary>
        [Tooltip("코어 해제 가능 범위 점선 LineRenderer.")]
        [SerializeField] private LineRenderer _coreRangeCircle;

        [Header("── DataSO ──────────────────────")]

        /// <summary>
        /// BossWardenDataSO.
        /// 색상 / 예고선 두께 참조.
        /// </summary>
        [Tooltip("BossWardenDataSO.")]
        [SerializeField] private BossWardenDataSO _data;

        // ══════════════════════════════════════════════════════
        // 내부 Tween 핸들 (Slam 밝기 증가용)
        // ══════════════════════════════════════════════════════

        private Tween _slamDisc0Tween;
        private Tween _slamDisc1Tween;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            // 시작 시 모든 예고 오브젝트 비활성
            HideAll();
        }

        private void OnDestroy()
        {
            _slamDisc0Tween?.Kill();
            _slamDisc1Tween?.Kill();
        }

        // ══════════════════════════════════════════════════════
        // 초기화 — BossWardenCore 에서 주입
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
        // Charge 예고선
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Charge 돌진 예고선을 표시한다.
        ///
        /// [표시 방식]
        ///   LineRenderer 를 시작점(bossPos)에서 방향으로 length 만큼 그린다.
        ///   실제 히트박스보다 20% 넉넉한 chargeWarningSize.y 를 length 로 사용.
        ///
        /// [쿼터뷰 고려]
        ///   탑다운 판정이므로 Z=0 월드 좌표 기준.
        /// </summary>
        /// <param name="startPos">보스 월드 위치.</param>
        /// <param name="direction">돌진 방향 (정규화).</param>
        /// <param name="length">예고선 길이 (= chargeWarningSize.y).</param>
        public void ShowChargeLine(Vector2 startPos, Vector2 direction, float length)
        {
            if (_chargeLineRenderer == null) return;

            _chargeLineRenderer.gameObject.SetActive(true);

            Vector3 start = new Vector3(startPos.x, startPos.y, 0f);
            Vector3 end = start + new Vector3(direction.x, direction.y, 0f) * length;

            _chargeLineRenderer.positionCount = 2;
            _chargeLineRenderer.SetPosition(0, start);
            _chargeLineRenderer.SetPosition(1, end);

            // 색상 설정
            Color c = _data != null ? _data.colorWarningRange : new Color(1f, 0f, 0f, 0.4f);
            _chargeLineRenderer.startColor = c;
            _chargeLineRenderer.endColor = c;
        }

        /// <summary>
        /// Charge 예고선을 숨긴다.
        /// Active 구간 시작 시 호출.
        /// </summary>
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
        /// [표시 방식]
        ///   원형 디스크 SpriteRenderer 를 플레이어 위치에 월드 좌표로 이동.
        ///   Scale = radius × 2 (직경 = 반경 × 2).
        ///   Warning 시작 시 플레이어 위치에 스냅 후 고정 (Active 중 이동 없음).
        ///
        /// [SortingLayer]
        ///   캐릭터 아래에 표시되도록 SortingLayer = Ground 로 설정 필요.
        /// </summary>
        /// <param name="worldPos">플레이어 월드 위치 (Warning 시작 시 스냅).</param>
        /// <param name="radius">예고 반경 (= slamWarningRadius).</param>
        /// <param name="discIndex">디스크 인덱스 (0=단일/첫 번째, 1=두 번째).</param>
        public void ShowSlamDisc(Vector2 worldPos, float radius, int discIndex = 0)
        {
            SpriteRenderer disc = discIndex == 0 ? _slamDisc0 : _slamDisc1;
            if (disc == null) return;

            // 월드 위치로 이동 (부모 오브젝트 이동 없이 직접 설정)
            disc.transform.position = new Vector3(worldPos.x, worldPos.y, 0f);

            // 스케일 = 직경
            float diameter = radius * 2f;
            disc.transform.localScale = new Vector3(diameter, diameter, 1f);

            // 색상
            Color c = _data != null ? _data.colorWarningRange : new Color(1f, 0f, 0f, 0.4f);
            disc.color = c;

            disc.gameObject.SetActive(true);
        }

        /// <summary>
        /// Slam Active 히트 순간 디스크를 밝게 플래시 후 제거한다.
        /// </summary>
        /// <param name="discIndex">디스크 인덱스 (0 or 1).</param>
        public void FlashAndHideSlamDisc(int discIndex = 0)
        {
            SpriteRenderer disc = discIndex == 0 ? _slamDisc0 : _slamDisc1;
            ref Tween tween = ref (discIndex == 0 ? ref _slamDisc0Tween : ref _slamDisc1Tween);

            if (disc == null || !disc.gameObject.activeSelf) return;

            tween?.Kill();

            // 흰색 순간 플래시 → 비활성
            disc.color = Color.white;
            tween = disc.DOColor(new Color(1f, 1f, 1f, 0f), 0.1f)
                .SetEase(Ease.OutCubic)
                .SetUpdate(true)
                .OnComplete(() => disc.gameObject.SetActive(false));
        }

        /// <summary>
        /// Slam 디스크를 즉시 숨긴다.
        /// </summary>
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
        /// 보스 위치에 배치하고 Active 중 함께 회전.
        /// </summary>
        /// <param name="bossPos">보스 월드 위치.</param>
        /// <param name="radius">예고 반경 (= sweepWarningRadius).</param>
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
        /// Active 구간 중 매 프레임 호출하여 보스와 함께 회전.
        /// </summary>
        public void UpdateSweepDiscPosition(Vector2 bossPos)
        {
            if (_sweepDisc == null || !_sweepDisc.gameObject.activeSelf) return;
            _sweepDisc.transform.position = new Vector3(bossPos.x, bossPos.y, 0f);
        }

        /// <summary>
        /// Sweep 디스크를 숨긴다.
        /// </summary>
        public void HideSweepDisc()
        {
            if (_sweepDisc == null) return;
            _sweepDisc.gameObject.SetActive(false);
        }

        // ══════════════════════════════════════════════════════
        // GuardBreak 예고 디스크
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// GuardBreak 직사각형 예고 디스크를 표시한다.
        ///
        /// [방향 처리]
        ///   direction 각도로 디스크를 회전시켜 보스가 바라보는 방향 정면에 표시.
        ///   쿼터뷰 기준 정면 = 보스 → 플레이어 방향.
        /// </summary>
        /// <param name="bossPos">보스 월드 위치.</param>
        /// <param name="direction">보스 정면 방향 (정규화).</param>
        /// <param name="size">예고 범위 크기 (guardBreakWarningSize).</param>
        public void ShowGuardBreakDisc(Vector2 bossPos, Vector2 direction, Vector2 size)
        {
            if (_guardBreakDisc == null) return;

            // 보스 정면 방향으로 오프셋
            Vector2 offset = direction * (size.y * 0.5f);
            Vector3 pos = new Vector3(
                bossPos.x + offset.x,
                bossPos.y + offset.y,
                0f);

            _guardBreakDisc.transform.position = pos;
            _guardBreakDisc.transform.localScale = new Vector3(size.x, size.y, 1f);

            // 방향 각도 회전 (Z축)
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            _guardBreakDisc.transform.rotation = Quaternion.Euler(0f, 0f, angle - 90f);

            Color c = _data != null ? _data.colorWarningRange : new Color(1f, 0f, 0f, 0.4f);
            _guardBreakDisc.color = c;

            _guardBreakDisc.gameObject.SetActive(true);
        }

        /// <summary>
        /// GuardBreak 디스크를 숨긴다.
        /// </summary>
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
        /// Warning 중 0.3초 간격으로 순차 호출.
        ///
        /// [밝기 순서]
        ///   0번: alpha 1.0 (가장 밝음)
        ///   1번: alpha 0.7
        ///   2번: alpha 0.5
        /// </summary>
        /// <param name="index">예고선 인덱스 (0~2).</param>
        /// <param name="startPos">보스 위치.</param>
        /// <param name="direction">돌진 방향 (정규화).</param>
        /// <param name="length">예고선 길이.</param>
        public void ShowRageChargeLine(int index, Vector2 startPos, Vector2 direction, float length)
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

            // 순차 밝기
            float alpha = 1.0f - index * 0.25f;
            Color baseColor = _data != null ? _data.colorWarningRange : new Color(1f, 0f, 0f, 0.4f);
            Color c = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * alpha);
            lr.startColor = c;
            lr.endColor = c;
        }

        /// <summary>
        /// RageCharge 예고선 전체를 숨긴다.
        /// </summary>
        public void HideAllRageChargeLines()
        {
            foreach (var lr in _rageChargeLines)
            {
                if (lr != null)
                    lr.gameObject.SetActive(false);
            }
        }

        // ══════════════════════════════════════════════════════
        // 봉인 집행 가능 범위 점선 원
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 봉인 집행 가능 범위 점선 원을 표시한다.
        /// 부위 봉인도 100% 도달 시 BossWardenSealExecutor 에서 호출.
        /// </summary>
        /// <param name="centerPos">부위 월드 위치.</param>
        /// <param name="radius">봉인 집행 범위 (sealExecutionRange).</param>
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

        /// <summary>
        /// 봉인 집행 범위 점선 원을 숨긴다.
        /// </summary>
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
        /// 그로기 진입 + 코어 활성화 시 BossWardenSealExecutor 에서 호출.
        /// </summary>
        /// <param name="centerPos">코어 월드 위치.</param>
        /// <param name="radius">코어 해제 범위 (coreUnlockRange).</param>
        public void ShowCoreRange(Vector2 centerPos, float radius)
        {
            if (_coreRangeCircle == null) return;

            DrawDashedCircle(
                _coreRangeCircle,
                centerPos,
                radius,
                segments: 32,
                color: _data != null ? _data.ColorData.colorSealRange : Color.yellow);

            _coreRangeCircle.gameObject.SetActive(true);
        }

        /// <summary>
        /// 코어 해제 범위 점선 원을 숨긴다.
        /// </summary>
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
        /// LineRenderer 로 점선 원을 그린다.
        ///
        /// [구현 방식]
        ///   segments 개수만큼 점을 계산하여 LineRenderer 에 적용.
        ///   Loop = true 로 닫힌 원 형태.
        ///   점선 효과는 LineRenderer 의 widthCurve 와 material 을 통해 구현 가능.
        ///   현재는 단순 실선 원으로 구현 (추후 점선 material 교체 가능).
        /// </summary>
        private void DrawDashedCircle(
            LineRenderer lr,
            Vector2 center,
            float radius,
            int segments,
            Color color)
        {
            if (lr == null) return;

            lr.positionCount = segments + 1;
            lr.loop = true;
            lr.startColor = color;
            lr.endColor = color;

            float angleStep = 360f / segments;
            for (int i = 0; i <= segments; i++)
            {
                float angle = i * angleStep * Mathf.Deg2Rad;
                float x = center.x + Mathf.Cos(angle) * radius;
                float y = center.y + Mathf.Sin(angle) * radius;
                lr.SetPosition(i, new Vector3(x, y, 0f));
            }
        }
    }
}