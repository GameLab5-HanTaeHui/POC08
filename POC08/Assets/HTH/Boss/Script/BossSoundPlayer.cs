// ============================================================
// BossSoundPlayer.cs  v1.0
// 보스 전투 사운드 재생 컴포넌트 — 모든 보스 공용 (간략 구현)
//
// [역할]
//   IBossCore 이벤트 + SealableComponent 이벤트 수신
//   → AudioSource.PlayOneShot() 으로 SFX 재생
//
// [간략 구현 원칙]
//   AudioManager 미사용 — AudioSource 직접 제어
//   AudioClip Inspector 연결 방식 — 추후 AudioManager 연동 시 이 컴포넌트만 수정
//   PlayOneShot 사용 — 겹쳐서 재생 가능 (타격감 유지)
//
// [연결 방법]
//   씬의 독립 오브젝트 (Managers 또는 BossRoot) 에 부착
//   _bossCoreObject : IBossCore 구현 MonoBehaviour 연결
//   _sealables      : 봉인도 SFX 수신할 SealableComponent 목록 연결
//   각 AudioClip 필드에 사운드 에셋 연결
//
// [namespace] SEAL
// ============================================================

using System.Collections.Generic;
using UnityEngine;

namespace SEAL
{
    /// <summary>
    /// 보스 전투 사운드 재생 컴포넌트. 모든 보스 공용. (v1.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [재생 이벤트 목록]
    ///   그로기 진입     → _groggyEnterClip
    ///   딜 페이즈 진입  → _dilPhaseEnterClip
    ///   Part 봉인 완료  → _partSealCompleteClip
    ///   Core 봉인 완료  → _coreSealCompleteClip
    ///   봉인도 단계변화 → _sealStageClips[단계]
    ///   보스 처치       → _deadClip
    /// ────────────────────────────────────────────────────
    /// </summary>
    public class BossSoundPlayer : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── AudioSource ──────────────────────")]

        /// <summary>
        /// SFX 재생 AudioSource.
        /// 미연결 시 GetComponent 자동 탐색.
        /// PlayOneShot 사용 — 겹쳐서 재생 가능.
        /// </summary>
        [Tooltip("SFX 재생 AudioSource. 미연결 시 자동 탐색.")]
        [SerializeField] private AudioSource _audioSource;

        [Header("── 보스 코어 연결 ──────────────────────")]

        /// <summary>
        /// IBossCore 구현 MonoBehaviour.
        /// BossWardenCore 또는 다른 보스 코어 연결.
        /// </summary>
        [Tooltip("IBossCore 구현 MonoBehaviour. BossWardenCore 등 연결.")]
        [SerializeField] private MonoBehaviour _bossCoreObject;

        [Header("── SealableComponent 목록 ──────────────────────")]

        /// <summary>
        /// 봉인 SFX 를 수신할 SealableComponent 목록.
        /// LeftArm / RightArm / Core 의 SealableComponent 연결.
        /// GetComponentsInChildren 으로 자동 수집도 가능 (Start 에서 처리).
        /// </summary>
        [Tooltip("봉인 SFX 수신 SealableComponent 목록. 미연결 시 자식에서 자동 수집.")]
        [SerializeField] private List<SealableComponent> _sealables = new List<SealableComponent>();

        [Header("── 보스 상태 SFX ──────────────────────")]

        /// <summary>그로기 진입 SFX.</summary>
        [Tooltip("그로기 진입 SFX.")]
        [SerializeField] private AudioClip _groggyEnterClip;

        /// <summary>딜 페이즈 진입 SFX (코어 해제 완료).</summary>
        [Tooltip("딜 페이즈 진입 SFX.")]
        [SerializeField] private AudioClip _dilPhaseEnterClip;

        /// <summary>보스 처치 SFX.</summary>
        [Tooltip("보스 처치 SFX.")]
        [SerializeField] private AudioClip _deadClip;

        [Header("── 봉인 SFX ──────────────────────")]

        /// <summary>
        /// Part 등급 봉인 완료 SFX.
        /// 팔 봉인 완료 순간 재생.
        /// </summary>
        [Tooltip("Part 봉인 완료 SFX. 팔 봉인 완료 순간 재생.")]
        [SerializeField] private AudioClip _partSealCompleteClip;

        /// <summary>
        /// Core 등급 봉인 완료 SFX.
        /// 코어 최종 봉인 완료 순간 재생.
        /// </summary>
        [Tooltip("Core 봉인 완료 SFX. 최종 봉인 순간 재생.")]
        [SerializeField] private AudioClip _coreSealCompleteClip;

        /// <summary>
        /// 봉인도 단계 변화 SFX.
        /// 인덱스 0=25% / 1=50% / 2=75% / 3=100% 에 대응.
        /// 각 단계에 맞는 AudioClip 연결. null 이면 해당 단계 스킵.
        /// </summary>
        [Tooltip("봉인도 단계 변화 SFX. [0]=25% [1]=50% [2]=75% [3]=100%.")]
        [SerializeField] private AudioClip[] _sealStageClips = new AudioClip[4];

        // ══════════════════════════════════════════════════════
        // 내부 상태
        // ══════════════════════════════════════════════════════

        private IBossCore _bossCore;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            if (_audioSource == null)
                _audioSource = GetComponent<AudioSource>();

            if (_audioSource == null)
            {
                _audioSource = gameObject.AddComponent<AudioSource>();
                _audioSource.playOnAwake = false;
                _audioSource.spatialBlend = 0f; // 2D 사운드
            }
        }

        private void Start()
        {
            // IBossCore 캐스팅
            if (_bossCoreObject != null)
            {
                _bossCore = _bossCoreObject as IBossCore;
                if (_bossCore == null)
                    Debug.LogError($"[BossSoundPlayer] {_bossCoreObject.name} 이(가) IBossCore 를 구현하지 않습니다.");
                else
                    SubscribeCoreEvents();
            }
            else
                Debug.LogWarning("[BossSoundPlayer] _bossCoreObject 미연결.");

            // SealableComponent 자동 수집 (Inspector 미연결 시)
            if (_sealables.Count == 0)
            {
                var found = GetComponentsInChildren<SealableComponent>(includeInactive: true);
                _sealables.AddRange(found);
            }

            // SealableComponent 이벤트 구독
            foreach (var s in _sealables)
                SubscribeSealableEvents(s);
        }

        private void OnDestroy()
        {
            UnsubscribeCoreEvents();

            foreach (var s in _sealables)
                UnsubscribeSealableEvents(s);
        }

        // ══════════════════════════════════════════════════════
        // 이벤트 구독 / 해제
        // ══════════════════════════════════════════════════════

        private void SubscribeCoreEvents()
        {
            if (_bossCore == null) return;

            _bossCore.OnDilPhaseEnter += HandleDilPhaseEnter;
            _bossCore.OnDead += HandleDead;
        }

        private void UnsubscribeCoreEvents()
        {
            if (_bossCore == null) return;

            _bossCore.OnDilPhaseEnter -= HandleDilPhaseEnter;
            _bossCore.OnDead -= HandleDead;
        }

        private void SubscribeSealableEvents(SealableComponent s)
        {
            if (s == null) return;

            s.OnSealCompleted += () => HandleSealCompleted(s.Grade);
            s.OnStageChanged += HandleStageChanged;
        }

        private void UnsubscribeSealableEvents(SealableComponent s)
        {
            if (s == null) return;

            // ※ 람다로 구독했으므로 -= 해제 불가
            // OnDestroy 에서 오브젝트가 파괴되므로 이벤트 자동 소멸
        }

        // ══════════════════════════════════════════════════════
        // 이벤트 핸들러
        // ══════════════════════════════════════════════════════

        private void HandleGroggyEnter()
        {
            Play(_groggyEnterClip);
        }

        private void HandleDilPhaseEnter()
        {
            Play(_dilPhaseEnterClip);
        }

        private void HandleDead()
        {
            Play(_deadClip);
        }

        private void HandleSealCompleted(SealGrade grade)
        {
            switch (grade)
            {
                case SealGrade.Part:
                    Play(_partSealCompleteClip);
                    break;
                case SealGrade.Core:
                    Play(_coreSealCompleteClip);
                    break;
                    // Normal: 사운드 없음
            }
        }

        private void HandleStageChanged(int stage)
        {
            // stage: 0~4 / 0=0% 는 초기화이므로 스킵
            // _sealStageClips[0]=25% / [1]=50% / [2]=75% / [3]=100%
            int clipIndex = stage - 1;
            if (clipIndex < 0 || clipIndex >= _sealStageClips.Length) return;

            Play(_sealStageClips[clipIndex]);
        }

        // ══════════════════════════════════════════════════════
        // 재생 유틸
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// AudioClip 을 PlayOneShot 으로 재생.
        /// null 체크 포함. 겹쳐서 재생 가능.
        /// </summary>
        private void Play(AudioClip clip)
        {
            if (clip == null || _audioSource == null) return;
            _audioSource.PlayOneShot(clip);
        }

        // ══════════════════════════════════════════════════════
        // 외부 API
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 런타임 중 다른 보스로 교체.
        /// 기존 구독 해제 → 새 보스 구독.
        /// </summary>
        public void SetBossCore(IBossCore newCore)
        {
            UnsubscribeCoreEvents();
            _bossCore = newCore;
            SubscribeCoreEvents();
        }

        /// <summary>
        /// 외부에서 직접 SFX 재생.
        /// AudioManager 연동 전 임시 사용 가능.
        /// </summary>
        public void PlayOneShot(AudioClip clip)
        {
            Play(clip);
        }
    }
}