// ============================================================
// BossVFXManager.cs v3.0
// Boss_Warden VFX 완전 통합 관리자 — Step 19
//
// [Step 19]
//   Slam / Swing 2패턴 구조 대응. Swing은 기존 Sweep Disc 슬롯을 재사용한다.
// ============================================================

using System.Collections;
using UnityEngine;
using DG.Tweening;

namespace SEAL
{
    [DefaultExecutionOrder(-6)]
    public class BossVFXManager : MonoBehaviour
    {
        [Header("── Central Managers ──────────────────────")]
        [SerializeField] private BossDataManager _dataManager;
        [SerializeField] private BossPartManager _partManager;
        [SerializeField] private BossEventHub _eventHub;
        [SerializeField] private BossAttackManager _attackManager;

        [Header("── Body / Core Feedback ──────────────────────")]
        [SerializeField] private SpriteRenderer _bodyRenderer;
        [SerializeField] private SpriteRenderer _coreRenderer;
        [SerializeField] private Transform _deathScaleTarget;

        [Header("── Seal Complete Particles ──────────────────────")]
        [SerializeField] private ParticleSystem _armLSealParticle;
        [SerializeField] private ParticleSystem _armRSealParticle;

        [Header("── Charge Range ──────────────────────")]
        [SerializeField] private LineRenderer _chargeLineRenderer;

        [Header("── Slam Range ──────────────────────")]
        [SerializeField] private SpriteRenderer _slamDisc0;
        [SerializeField] private SpriteRenderer _slamDisc1;

        [Header("── Swing Range (기존 Sweep Disc 재사용) ──────────────────────")]
        [SerializeField] private SpriteRenderer _sweepDisc;

        [Header("── GuardBreak Range ──────────────────────")]
        [SerializeField] private SpriteRenderer _guardBreakDisc;

        [Header("── RageCharge Range ──────────────────────")]
        [SerializeField] private LineRenderer[] _rageChargeLines = new LineRenderer[3];

        [Header("── Seal Range Circles ──────────────────────")]
        [SerializeField] private LineRenderer _sealRangeCircle;
        [SerializeField] private LineRenderer _coreRangeCircle;

        [Header("── Warning Pulse ──────────────────────")]
        [Min(0.05f)]
        [SerializeField] private float _pulsePeriod = 0.35f;
        [Range(0f, 0.5f)]
        [SerializeField] private float _pulseMinAlpha = 0.05f;
        [Range(0.3f, 1f)]
        [SerializeField] private float _pulseMaxAlpha = 0.55f;

        [Header("── Shockwave ──────────────────────")]
        [SerializeField] private LayerMask _playerLayer;
        [SerializeField] private SpriteRenderer _shockwaveDiscRenderer;
        [SerializeField] private Transform _cameraTransform;
        [Min(0f)]
        [SerializeField] private float _cameraShakeStrength = 0.25f;
        [Min(0f)]
        [SerializeField] private float _cameraShakeDuration = 0.3f;

        [Header("── EventHub Bridge ──────────────────────")]
        [SerializeField] private bool _listenToEventHubRequests = true;
        [SerializeField] private bool _debugLog;

        private BossWardenDataSO _wardenData;
        private Transform _coreTransform;
        private PlayerInputHandler _input;
        private PlayerMoveController _playerMoveController;
        private PlayerAttackController _playerAttackController;
        private Coroutine _knockbackCoroutine;
        private bool _shockwaveBlockedInput;
        private bool _initialized;
        private bool _eventSubscribed;
        private bool _attackSubscribed;
        private bool _partSubscribed;

        private readonly Collider2D[] _overlapBuffer = new Collider2D[8];

        private Tween _bodyTween;
        private Tween _coreTween;
        private Tween _chargeLineTween;
        private Tween _slamDisc0Tween;
        private Tween _slamDisc1Tween;
        private Tween _sweepDiscTween;
        private Tween _guardBreakDiscTween;
        private readonly Tween[] _rageLineTweens = new Tween[3];
        private Tween _shockwaveDiscTween;

        public bool IsInitialized => _initialized;

        private void Awake()
        {
            ResolveReferences();
            _input = PlayerInputHandler.Instance;

            if (_shockwaveDiscRenderer != null)
                _shockwaveDiscRenderer.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            RestoreShockwavePlayerControl(null);
            UnsubscribeEventHubRequests();
            UnsubscribeAttackManager();
            UnsubscribePartSealEvents();
            KillAllTweens();
        }

        public void Initialize(BossWardenDataSO data, Transform coreTransform = null)
        {
            ResolveReferences();

            _wardenData = data != null ? data : _dataManager != null ? _dataManager.WardenData : null;
            if (_wardenData == null || !_wardenData.IsValid())
            {
                Debug.LogError("[BossVFXManager] BossWardenDataSO 미연결 또는 유효하지 않음 — 초기화 중단.");
                return;
            }

            if (coreTransform != null)
                _coreTransform = coreTransform;
            else if (_coreTransform == null && _partManager != null && _partManager.CoreObject != null)
                _coreTransform = _partManager.CoreObject.transform;

            if (_bodyRenderer != null)
                _bodyRenderer.color = _wardenData.colorIdle;

            SubscribeAttackManager();
            SubscribePartSealEvents();

            if (_listenToEventHubRequests)
                SubscribeEventHubRequests();

            _initialized = true;

            if (_debugLog)
                Debug.Log("[BossVFXManager] v3.0 초기화 완료 — VFX + BossAttackManager 상태 이벤트 통합");
        }

        public void ConnectCore(Transform coreTransform)
        {
            if (coreTransform == null) return;
            _coreTransform = coreTransform;
        }

        // ============================================================
        // Boss state visual feedback
        // ============================================================

        public void HandleStateChanged(BossAttackManager.WardenAIState state, BossPatternBase pattern)
        {
            if (_wardenData == null || _bodyRenderer == null) return;

            float pulse = _wardenData.ColorData?.sealReadyPulseDuration ?? 0.4f;
            float lerp = _wardenData.ColorData?.colorLerpDuration ?? 0.15f;

            _bodyTween?.Kill();

            switch (state)
            {
                case BossAttackManager.WardenAIState.Warning:
                    _bodyTween = _bodyRenderer
                        .DOColor(_wardenData.colorWarning, pulse * 0.4f)
                        .SetLoops(-1, LoopType.Yoyo)
                        .SetUpdate(true);
                    break;

                case BossAttackManager.WardenAIState.Active:
                    _bodyRenderer.color = _wardenData.colorActive;
                    break;

                case BossAttackManager.WardenAIState.Recovery:
                    _bodyTween = _bodyRenderer
                        .DOColor(_wardenData.colorRecovery, pulse * 0.5f)
                        .SetLoops(-1, LoopType.Yoyo)
                        .SetUpdate(true);
                    break;

                default:
                    _bodyTween = _bodyRenderer
                        .DOColor(_wardenData.colorIdle, lerp)
                        .SetUpdate(true);
                    break;
            }
        }

        public void OnDilPhaseEnter()
        {
            if (_wardenData == null) return;

            float pulse = _wardenData.ColorData?.sealReadyPulseDuration ?? 0.4f;

            _bodyTween?.Kill();
            _bodyTween = _bodyRenderer?
                .DOColor(_wardenData.colorDilPhase, pulse * 0.5f)
                .SetLoops(-1, LoopType.Yoyo)
                .SetUpdate(true);

            _coreTween?.Kill();
            _coreTween = _coreRenderer?
                .DOColor(Color.white, pulse * 0.3f)
                .SetLoops(-1, LoopType.Yoyo)
                .SetUpdate(true);

            HideAllAttackRanges();
        }

        public void OnDilPhaseExit()
        {
            float lerp = _wardenData?.ColorData?.colorLerpDuration ?? 0.15f;

            _bodyTween?.Kill();
            _bodyTween = _bodyRenderer?
                .DOColor(_wardenData?.colorIdle ?? Color.gray, lerp)
                .SetUpdate(true);

            _coreTween?.Kill();

            if (_coreTransform != null)
                TriggerShockwave(_coreTransform.position);
        }

        public void OnFinalSealReady()
        {
            if (_wardenData == null) return;

            float pulse = _wardenData.ColorData?.sealReadyPulseDuration ?? 0.4f;

            _bodyTween?.Kill();
            _bodyTween = _bodyRenderer?
                .DOColor(_wardenData.ColorData.colorCoreFinalSeal, pulse * 0.3f)
                .SetLoops(-1, LoopType.Yoyo)
                .SetUpdate(true);

            _coreTween?.Kill();
            _coreTween = _coreRenderer?
                .DOColor(Color.cyan, pulse * 0.2f)
                .SetLoops(-1, LoopType.Yoyo)
                .SetUpdate(true);
        }

        public void OnPhaseChanged(int newPhase)
        {
            if (_wardenData == null || newPhase != 2) return;

            float lerp = _wardenData.ColorData?.colorLerpDuration ?? 0.15f;

            _bodyTween?.Kill();
            _bodyTween = _bodyRenderer?
                .DOColor(_wardenData.colorPhase2, 0.1f)
                .SetLoops(6, LoopType.Yoyo)
                .OnComplete(() =>
                {
                    _bodyTween = _bodyRenderer?
                        .DOColor(_wardenData.colorIdle, lerp)
                        .SetUpdate(true);
                })
                .SetUpdate(true);
        }

        public void OnDead()
        {
            KillAllTweens();
            HideAll();

            _bodyRenderer?.DOColor(Color.black, 0.5f).SetUpdate(true);

            Transform target = _deathScaleTarget != null ? _deathScaleTarget : transform.root;
            target.DOScale(Vector3.zero, 0.8f)
                .SetEase(Ease.InBack)
                .SetUpdate(true)
                .OnComplete(() => target.gameObject.SetActive(false));
        }

        // ============================================================
        // Attack range API — Patterns call these directly
        // ============================================================

        public void ShowChargeLine(Vector2 startPos, Vector2 direction, float length, float width = 0f)
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

            Color c = WarningRangeColor();
            c.a = _pulseMaxAlpha;
            _chargeLineRenderer.startColor = c;
            _chargeLineRenderer.endColor = c;
        }

        public void StartChargePulse()
        {
            if (_chargeLineRenderer == null) return;
            _chargeLineTween?.Kill();

            Color baseColor = _chargeLineRenderer.startColor;
            Color min = baseColor; min.a = _pulseMinAlpha;
            Color max = baseColor; max.a = _pulseMaxAlpha;
            _chargeLineRenderer.startColor = max;
            _chargeLineRenderer.endColor = max;

            _chargeLineTween = DOTween.To(
                    () => _chargeLineRenderer.startColor,
                    c => { _chargeLineRenderer.startColor = c; _chargeLineRenderer.endColor = c; },
                    min,
                    _pulsePeriod * 0.5f)
                .SetLoops(-1, LoopType.Yoyo)
                .SetEase(Ease.InOutSine)
                .SetUpdate(true);
        }

        public void HideChargeLine()
        {
            _chargeLineTween?.Kill();
            if (_chargeLineRenderer != null)
                _chargeLineRenderer.gameObject.SetActive(false);
        }

        public void ShowSlamDisc(Vector2 worldPos, float radius, int discIndex = 0)
        {
            SpriteRenderer disc = discIndex == 0 ? _slamDisc0 : _slamDisc1;
            if (disc == null) return;

            disc.transform.position = new Vector3(worldPos.x, worldPos.y, 0f);
            float diameter = radius * 2f;
            disc.transform.localScale = new Vector3(diameter, diameter, 1f);

            Color c = WarningRangeColor();
            c.a = _pulseMaxAlpha;
            disc.color = c;
            disc.gameObject.SetActive(true);
        }

        public void StartSlamPulse(int discIndex = 0)
        {
            SpriteRenderer disc = discIndex == 0 ? _slamDisc0 : _slamDisc1;
            ref Tween tween = ref (discIndex == 0 ? ref _slamDisc0Tween : ref _slamDisc1Tween);
            StartSpritePulse(disc, ref tween);
        }

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

        public void HideSlamDisc(int discIndex = 0)
        {
            SpriteRenderer disc = discIndex == 0 ? _slamDisc0 : _slamDisc1;
            ref Tween tween = ref (discIndex == 0 ? ref _slamDisc0Tween : ref _slamDisc1Tween);
            tween?.Kill();
            if (disc != null) disc.gameObject.SetActive(false);
        }

        public void ShowSweepDisc(Vector2 worldPos, float radius)
        {
            if (_sweepDisc == null) return;

            _sweepDisc.transform.position = new Vector3(worldPos.x, worldPos.y, 0f);
            float diameter = radius * 2f;
            _sweepDisc.transform.localScale = new Vector3(diameter, diameter, 1f);

            Color c = WarningRangeColor();
            c.a = _pulseMaxAlpha;
            _sweepDisc.color = c;
            _sweepDisc.gameObject.SetActive(true);
        }

        public void UpdateSweepDiscPosition(Vector2 worldPos)
        {
            if (_sweepDisc == null || !_sweepDisc.gameObject.activeSelf) return;
            _sweepDisc.transform.position = new Vector3(worldPos.x, worldPos.y, 0f);
        }

        public void StartSweepPulse()
        {
            StartSpritePulse(_sweepDisc, ref _sweepDiscTween);
        }

        public void HideSweepDisc()
        {
            _sweepDiscTween?.Kill();
            if (_sweepDisc != null) _sweepDisc.gameObject.SetActive(false);
        }

        // Step19 — Swing 패턴용 API. 기존 Sweep Disc 슬롯을 재사용한다.
        public void ShowSwingDisc(Vector2 worldPos, float radius)
        {
            ShowSweepDisc(worldPos, radius);
        }

        public void UpdateSwingDiscPosition(Vector2 worldPos)
        {
            UpdateSweepDiscPosition(worldPos);
        }

        public void StartSwingPulse()
        {
            StartSweepPulse();
        }

        public void FlashAndHideSwingDisc()
        {
            if (_sweepDisc == null || !_sweepDisc.gameObject.activeSelf) return;

            _sweepDiscTween?.Kill();
            _sweepDisc.color = Color.white;
            _sweepDiscTween = _sweepDisc.DOColor(new Color(1f, 1f, 1f, 0f), 0.1f)
                .SetEase(Ease.OutCubic)
                .SetUpdate(true)
                .OnComplete(() => _sweepDisc.gameObject.SetActive(false));
        }

        public void HideSwingDisc()
        {
            HideSweepDisc();
        }

        public void ShowGuardBreakDisc(Vector2 worldPos, Vector2 size, float angle)
        {
            if (_guardBreakDisc == null) return;

            _guardBreakDisc.transform.position = new Vector3(worldPos.x, worldPos.y, 0f);
            _guardBreakDisc.transform.localScale = new Vector3(size.x, size.y, 1f);
            _guardBreakDisc.transform.eulerAngles = new Vector3(0f, 0f, angle);

            Color c = WarningRangeColor();
            c.a = _pulseMaxAlpha;
            _guardBreakDisc.color = c;
            _guardBreakDisc.gameObject.SetActive(true);
        }

        public void StartGuardBreakPulse()
        {
            StartSpritePulse(_guardBreakDisc, ref _guardBreakDiscTween);
        }

        public void HideGuardBreakDisc()
        {
            _guardBreakDiscTween?.Kill();
            if (_guardBreakDisc != null) _guardBreakDisc.gameObject.SetActive(false);
        }

        public void ShowRageChargeLine(int index, Vector2 startPos, Vector2 direction, float length)
        {
            if (index < 0 || index >= _rageChargeLines.Length) return;
            LineRenderer line = _rageChargeLines[index];
            if (line == null) return;

            line.gameObject.SetActive(true);
            Vector3 start = new Vector3(startPos.x, startPos.y, 0f);
            Vector3 end = start + new Vector3(direction.x, direction.y, 0f) * length;

            line.positionCount = 2;
            line.SetPosition(0, start);
            line.SetPosition(1, end);

            Color c = WarningRangeColor();
            c.a = _pulseMaxAlpha;
            line.startColor = c;
            line.endColor = c;
        }

        public void StartRageChargePulse()
        {
            for (int i = 0; i < _rageChargeLines.Length; i++)
            {
                LineRenderer line = _rageChargeLines[i];
                if (line == null) continue;

                _rageLineTweens[i]?.Kill();

                Color baseColor = line.startColor;
                Color min = baseColor; min.a = _pulseMinAlpha;
                Color max = baseColor; max.a = _pulseMaxAlpha;

                line.startColor = max;
                line.endColor = max;

                int idx = i;
                _rageLineTweens[idx] = DOTween.To(
                        () => _rageChargeLines[idx].startColor,
                        c => { _rageChargeLines[idx].startColor = c; _rageChargeLines[idx].endColor = c; },
                        min,
                        _pulsePeriod * 0.5f)
                    .SetLoops(-1, LoopType.Yoyo)
                    .SetEase(Ease.InOutSine)
                    .SetUpdate(true);
            }
        }

        public void HideAllRageChargeLines()
        {
            for (int i = 0; i < _rageChargeLines.Length; i++)
            {
                _rageLineTweens[i]?.Kill();
                if (_rageChargeLines[i] != null)
                    _rageChargeLines[i].gameObject.SetActive(false);
            }
        }

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

        public void ShowSealRange(Vector2 center, float radius)
        {
            DrawDashedCircle(_sealRangeCircle, center, radius,
                _wardenData != null ? _wardenData.ColorData?.colorSealRange ?? Color.cyan : Color.cyan);
        }

        public void ShowCoreRange(Vector2 center, float radius)
        {
            DrawDashedCircle(_coreRangeCircle, center, radius,
                _wardenData != null ? _wardenData.ColorData?.colorCoreRange ?? Color.yellow : Color.yellow);
        }

        public void HideSealRange()
        {
            if (_sealRangeCircle != null) _sealRangeCircle.gameObject.SetActive(false);
        }

        public void HideCoreRange()
        {
            if (_coreRangeCircle != null) _coreRangeCircle.gameObject.SetActive(false);
        }

        public void HideAllAttackRanges()
        {
            StopAllPulse();
            HideChargeLine();
            HideSlamDisc(0);
            HideSlamDisc(1);
            HideSweepDisc();
            HideGuardBreakDisc();
            HideAllRageChargeLines();
        }

        public void HideAllSealEffects()
        {
            HideSealRange();
            HideCoreRange();
        }

        public void HideAll()
        {
            HideAllAttackRanges();
            HideAllSealEffects();
        }

        // ============================================================
        // Shockwave
        // ============================================================

        public void TriggerShockwave(Vector3 origin)
        {
            if (_wardenData == null) return;

            PlayShockwaveDisc(origin);
            ShakeCamera();
            ApplyShockwaveKnockback(origin);

            if (_debugLog)
                Debug.Log($"[BossVFXManager] 충격파 발동 — 반경:{_wardenData.shockwaveRadius}");
        }

        public void Trigger(Vector3 origin) => TriggerShockwave(origin);

        // ============================================================
        // Event subscription
        // ============================================================

        private void SubscribeEventHubRequests()
        {
            if (_eventSubscribed || _eventHub == null) return;
            _eventHub.OnRequestShockwave += HandleRequestShockwave;
            _eventSubscribed = true;
        }

        private void UnsubscribeEventHubRequests()
        {
            if (!_eventSubscribed || _eventHub == null) return;
            _eventHub.OnRequestShockwave -= HandleRequestShockwave;
            _eventSubscribed = false;
        }

        private void HandleRequestShockwave(Vector3 origin)
        {
            TriggerShockwave(origin);
        }

        private void SubscribeAttackManager()
        {
            if (_attackSubscribed || _attackManager == null) return;
            _attackManager.OnStateChanged += HandleStateChanged;
            _attackSubscribed = true;
        }

        private void UnsubscribeAttackManager()
        {
            if (!_attackSubscribed || _attackManager == null) return;
            _attackManager.OnStateChanged -= HandleStateChanged;
            _attackSubscribed = false;
        }

        private void SubscribePartSealEvents()
        {
            if (_partSubscribed || _partManager == null) return;

            SubscribeSealComplete(_partManager.LeftArm, HandleArmLSealed);
            SubscribeSealComplete(_partManager.RightArm, HandleArmRSealed);

            _partSubscribed = true;
        }

        private void UnsubscribePartSealEvents()
        {
            if (!_partSubscribed || _partManager == null) return;

            UnsubscribeSealComplete(_partManager.LeftArm, HandleArmLSealed);
            UnsubscribeSealComplete(_partManager.RightArm, HandleArmRSealed);

            _partSubscribed = false;
        }

        private void SubscribeSealComplete(BossWardenPart part, System.Action handler)
        {
            if (part == null || handler == null) return;
            SealableComponent sealable = part.GetComponent<SealableComponent>();
            if (sealable == null) return;
            sealable.OnSealCompleted -= handler;
            sealable.OnSealCompleted += handler;
        }

        private void UnsubscribeSealComplete(BossWardenPart part, System.Action handler)
        {
            if (part == null || handler == null) return;
            SealableComponent sealable = part.GetComponent<SealableComponent>();
            if (sealable == null) return;
            sealable.OnSealCompleted -= handler;
        }

        private void HandleArmLSealed()
        {
            _armLSealParticle?.Play();
        }

        private void HandleArmRSealed()
        {
            _armRSealParticle?.Play();
        }

        // ============================================================
        // Internal helpers
        // ============================================================

        private void ResolveReferences()
        {
            Transform root = transform.root;

            if (_dataManager == null) _dataManager = root.GetComponentInChildren<BossDataManager>(true);
            if (_partManager == null) _partManager = root.GetComponentInChildren<BossPartManager>(true);
            if (_eventHub == null) _eventHub = root.GetComponentInChildren<BossEventHub>(true);
            if (_attackManager == null) _attackManager = root.GetComponentInChildren<BossAttackManager>(true);

            if (_coreTransform == null && _partManager != null && _partManager.CoreObject != null)
                _coreTransform = _partManager.CoreObject.transform;

            if (_deathScaleTarget == null)
                _deathScaleTarget = root;

            if (_input == null)
                _input = PlayerInputHandler.Instance;

            if (_playerMoveController == null)
            {
                var players = FindObjectsByType<PlayerMoveController>(FindObjectsSortMode.None);
                if (players != null && players.Length > 0)
                    _playerMoveController = players[0];
            }

            if (_playerAttackController == null && _playerMoveController != null)
                _playerAttackController = _playerMoveController.GetComponentInParent<PlayerAttackController>();
        }

        private Color WarningRangeColor()
        {
            return _wardenData != null
                ? _wardenData.colorWarningRange
                : new Color(1f, 0f, 0f, 0.4f);
        }

        private void StartSpritePulse(SpriteRenderer renderer, ref Tween tween)
        {
            if (renderer == null) return;

            tween?.Kill();

            Color max = renderer.color;
            max.a = _pulseMaxAlpha;
            Color min = renderer.color;
            min.a = _pulseMinAlpha;

            renderer.color = max;
            tween = renderer.DOColor(min, _pulsePeriod * 0.5f)
                .SetLoops(-1, LoopType.Yoyo)
                .SetEase(Ease.InOutSine)
                .SetUpdate(true);
        }

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

        private void PlayShockwaveDisc(Vector3 origin)
        {
            if (_shockwaveDiscRenderer == null || _wardenData == null) return;

            _shockwaveDiscTween?.Kill();
            _shockwaveDiscRenderer.transform.position = origin;
            _shockwaveDiscRenderer.transform.localScale = Vector3.zero;
            _shockwaveDiscRenderer.gameObject.SetActive(true);

            Color c = _wardenData.ColorData != null
                ? _wardenData.ColorData.colorCoreRange
                : new Color(1f, 1f, 1f, 0.45f);
            c.a = 0.45f;
            _shockwaveDiscRenderer.color = c;

            float diameter = _wardenData.shockwaveRadius * 2f;
            _shockwaveDiscRenderer.transform
                .DOScale(new Vector3(diameter, diameter, 1f), 0.25f)
                .SetEase(Ease.OutQuart)
                .SetUpdate(true);

            _shockwaveDiscTween = _shockwaveDiscRenderer
                .DOColor(new Color(c.r, c.g, c.b, 0f), 0.35f)
                .SetEase(Ease.OutCubic)
                .SetUpdate(true)
                .OnComplete(() => _shockwaveDiscRenderer.gameObject.SetActive(false));
        }

        private void ShakeCamera()
        {
            if (_cameraTransform == null || _cameraShakeStrength <= 0f) return;

            _cameraTransform.DOKill();
            _cameraTransform.DOShakePosition(
                    _cameraShakeDuration,
                    strength: new Vector3(_cameraShakeStrength, _cameraShakeStrength, 0f),
                    vibrato: 20,
                    randomness: 90f)
                .SetUpdate(true);
        }

        private void ApplyShockwaveKnockback(Vector3 origin)
        {
            if (_wardenData == null) return;

            if (_knockbackCoroutine != null)
            {
                StopCoroutine(_knockbackCoroutine);
                _knockbackCoroutine = null;
                RestoreShockwavePlayerControl(null);
            }

            int count = Physics2D.OverlapCircleNonAlloc(
                origin,
                _wardenData.shockwaveRadius,
                _overlapBuffer,
                _playerLayer);

            for (int i = 0; i < count; i++)
            {
                Collider2D col = _overlapBuffer[i];
                if (col == null) continue;

                Rigidbody2D rb = col.GetComponentInParent<Rigidbody2D>();
                if (rb == null) continue;

                Vector2 dir = (Vector2)col.transform.position - (Vector2)origin;
                if (dir.sqrMagnitude < 0.001f)
                    dir = Vector2.right;

                dir.Normalize();

                _knockbackCoroutine = StartCoroutine(ApplyKnockbackRoutine(rb, dir));
                break;
            }
        }

        private IEnumerator ApplyKnockbackRoutine(Rigidbody2D rb, Vector2 direction)
        {
            ResolveReferences();

            _playerAttackController?.CancelAttack();
            _playerMoveController?.SetAttackMove(false, Vector2.zero, 0f);

            if (_input != null)
            {
                _input.BlockAll();
                _shockwaveBlockedInput = true;
            }

            yield return new WaitForFixedUpdate();

            if (rb != null && _wardenData != null)
            {
                rb.linearVelocity = direction * _wardenData.shockwaveKnockbackForce;
                if (_debugLog)
                    Debug.Log($"[BossVFXManager] 넉백 velocity 설정 → {rb.linearVelocity}");
            }

            float duration = _wardenData != null ? _wardenData.shockwaveKnockbackDuration : 0.2f;
            yield return new WaitForSecondsRealtime(duration);

            RestoreShockwavePlayerControl(rb);
            _knockbackCoroutine = null;
        }

        private void RestoreShockwavePlayerControl(Rigidbody2D rb)
        {
            if (rb != null)
                rb.linearVelocity = Vector2.zero;

            _playerMoveController?.SetAttackMove(false, Vector2.zero, 0f);
            _playerMoveController?.SetMoveLocked(false);

            if (_shockwaveBlockedInput && _input != null)
                _input.UnblockAll();

            _shockwaveBlockedInput = false;
        }

        private void KillAllTweens()
        {
            _bodyTween?.Kill();
            _coreTween?.Kill();
            _chargeLineTween?.Kill();
            _slamDisc0Tween?.Kill();
            _slamDisc1Tween?.Kill();
            _sweepDiscTween?.Kill();
            _guardBreakDiscTween?.Kill();
            _shockwaveDiscTween?.Kill();

            for (int i = 0; i < _rageLineTweens.Length; i++)
                _rageLineTweens[i]?.Kill();
        }

#if UNITY_EDITOR
        [ContextMenu("DEBUG: VFX HideAll")]
        private void DEBUG_HideAll()
        {
            HideAll();
        }
#endif
    }
}
