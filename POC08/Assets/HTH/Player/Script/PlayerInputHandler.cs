// ============================================================
// PlayerInputHandler.cs  v1.0
// 탑뷰 플레이어 입력 통합 관리 컴포넌트
//
// [POC07 참고 스크립트]
//   InputManager.cs (횡스크롤 1D 이동 + 점프 + 대시 + 공격)
//   → 1D 이동(좌우만) 제거
//   → 2D Vector2 이동(8방향) 으로 전환
//   → 점프 / 중력 관련 입력 제거
//   → 봉인 집행(S키) / 공격(A키) / 대시(LShift) 유지
//   → New Input System 의 InputActionMap 코드 베이스 방식 적용
//
// [ActionMap 구조]
//   _inGameMap : Move(Vector2) / Dash / Attack / Seal
//
// [키 바인딩 기본값]
//   이동   : WASD + 방향키 (8방향 Vector2)
//   대시   : LShift / 게임패드 East
//   공격   : A키 / 게임패드 West
//   봉인   : S키 / 게임패드 South
//
// [입력 차단 시스템]
//   _moveBlocked / _dashBlocked / _actionBlocked
//   외부(StateMachine 상태 등)에서 Block/Unblock 호출로 제어.
//
// [싱글턴]
//   씬 내 단일 인스턴스. PlayerTopViewMover 에서 참조.
//
// [네임스페이스]
//   namespace : SEAL
// ============================================================

using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SEAL
{
    /// <summary>
    /// 탑뷰 플레이어 입력 통합 관리 컴포넌트. (v1.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [외부 구독 예시 — PlayerTopViewMover]
    ///   PlayerInputHandler.Instance.OnMove   += HandleMove;
    ///   PlayerInputHandler.Instance.OnDash   += HandleDash;
    ///   PlayerInputHandler.Instance.OnAttack += HandleAttack;
    ///   PlayerInputHandler.Instance.OnSeal   += HandleSeal;
    ///
    /// [입력 차단 예시 — 봉인 집행 상태]
    ///   PlayerInputHandler.Instance.BlockMove();
    ///   PlayerInputHandler.Instance.BlockAction();
    ///   // ... 봉인 집행 처리 ...
    ///   PlayerInputHandler.Instance.UnblockMove();
    ///   PlayerInputHandler.Instance.UnblockAction();
    /// ────────────────────────────────────────────────────
    /// </summary>
    public class PlayerInputHandler : MonoBehaviour
    {
        // ──────────────────────────────────────────
        // 싱글턴
        // ──────────────────────────────────────────

        /// <summary>
        /// 전역 단일 인스턴스.
        /// 씬 전환 시 파괴되면 null 로 초기화.
        /// </summary>
        public static PlayerInputHandler Instance { get; private set; }

        // ──────────────────────────────────────────
        // Inspector — 키 바인딩
        // ──────────────────────────────────────────

        [Header("── 키 바인딩 ──────────────────────")]

        /// <summary>
        /// 이동 Up 키. 기본 W.
        /// WASD Composite 의 Up 방향.
        /// </summary>
        [Tooltip("이동 Up 키. 기본 W.")]
        [SerializeField] private Key _keyMoveUp = Key.W;

        /// <summary>
        /// 이동 Down 키. 기본 S.
        /// </summary>
        [Tooltip("이동 Down 키. 기본 S.")]
        [SerializeField] private Key _keyMoveDown = Key.S;

        /// <summary>
        /// 이동 Left 키. 기본 A.
        /// </summary>
        [Tooltip("이동 Left 키. 기본 A.")]
        [SerializeField] private Key _keyMoveLeft = Key.A;

        /// <summary>
        /// 이동 Right 키. 기본 D.
        /// </summary>
        [Tooltip("이동 Right 키. 기본 D.")]
        [SerializeField] private Key _keyMoveRight = Key.D;

        /// <summary>
        /// 대시 키. 기본 LShift.
        /// </summary>
        [Tooltip("대시 키. 기본 LShift.")]
        [SerializeField] private Key _keyDash = Key.LeftShift;

        /// <summary>
        /// 공격 키. 기본 J.
        /// 탑뷰에서 A키는 이동에 사용하므로 J로 분리.
        /// </summary>
        [Tooltip("공격 키. 기본 J. (이동 A와 충돌 방지)")]
        [SerializeField] private Key _keyAttack = Key.J;

        /// <summary>
        /// 봉인 집행 키. 기본 K.
        /// 봉인도 100% 부위에 접근 후 입력.
        /// </summary>
        [Tooltip("봉인 집행 키. 기본 K. 봉인도 100% 도달 시 사용.")]
        [SerializeField] private Key _keySeal = Key.K;

        // ──────────────────────────────────────────
        // InputAction 인스턴스 (코드 기반 ActionMap)
        // ──────────────────────────────────────────

        /// <summary> 인게임 ActionMap. Move / Dash / Attack / Seal 포함. </summary>
        private InputActionMap _inGameMap;

        /// <summary>
        /// 이동 입력 Action. Vector2 Value 타입.
        /// WASD + 방향키 2DVector Composite 방식.
        /// </summary>
        private InputAction _actionMove;

        /// <summary> 대시 입력 Action. Button 타입. </summary>
        private InputAction _actionDash;

        /// <summary> 공격 입력 Action. Button 타입. </summary>
        private InputAction _actionAttack;

        /// <summary>
        /// 봉인 집행 입력 Action. Button 타입.
        /// 조건: 대상 부위 봉인도 100% 도달.
        /// </summary>
        private InputAction _actionSeal;

        // ──────────────────────────────────────────
        // 내부 상태 — 입력 차단 플래그
        // ──────────────────────────────────────────

        /// <summary>
        /// 이동 입력 차단 플래그.
        /// true 시 OnMove 이벤트 발행 안 함.
        /// 봉인 집행 / 피격 경직 등 상태에서 true 설정.
        /// </summary>
        private bool _moveBlocked;

        /// <summary>
        /// 대시 입력 차단 플래그.
        /// true 시 OnDash 이벤트 발행 안 함.
        /// </summary>
        private bool _dashBlocked;

        /// <summary>
        /// 공격 / 봉인 입력 차단 플래그.
        /// true 시 OnAttack / OnSeal 이벤트 발행 안 함.
        /// </summary>
        private bool _actionBlocked;

        // ──────────────────────────────────────────
        // 내부 상태 — 현재 입력값 (프레임 간 유지)
        // ──────────────────────────────────────────

        /// <summary>
        /// 현재 이동 입력 벡터. 매 프레임 Update 에서 읽음.
        /// 외부에서 MoveInput 프로퍼티로 접근.
        /// </summary>
        private Vector2 _rawMoveInput;

        /// <summary>
        /// 현재 공격 버튼 홀드 여부.
        /// 강공격(홀드) 구현 시 사용.
        /// </summary>
        private bool _isAttackHeld;

        // ──────────────────────────────────────────
        // 이벤트 — 외부 구독
        // ──────────────────────────────────────────

        /// <summary>
        /// 이동 입력 변경 시 발행.
        /// 파라미터: 정규화된 방향 Vector2 (8방향).
        /// 이동 없음 = Vector2.zero.
        /// _moveBlocked 시 발행 안 함.
        /// </summary>
        public event Action<Vector2> OnMove;

        /// <summary>
        /// 대시 버튼 입력 시 1회 발행.
        /// _dashBlocked 시 발행 안 함.
        /// </summary>
        public event Action OnDash;

        /// <summary>
        /// 공격 버튼 눌림 시 1회 발행.
        /// _actionBlocked 시 발행 안 함.
        /// </summary>
        public event Action OnAttack;

        /// <summary>
        /// 공격 버튼 뗌 시 1회 발행.
        /// 강공격(차지) 릴리즈 처리용.
        /// </summary>
        public event Action OnAttackReleased;

        /// <summary>
        /// 봉인 집행 버튼 입력 시 1회 발행.
        /// _actionBlocked 시 발행 안 함.
        /// 조건 판정은 PlayerSealExecutor 에서 수행.
        /// </summary>
        public event Action OnSeal;

        // ──────────────────────────────────────────
        // 프로퍼티
        // ──────────────────────────────────────────

        /// <summary>
        /// 현재 이동 입력 벡터 (정규화된 방향).
        /// 외부에서 현재 방향을 읽을 때 사용.
        /// 예: 대시 방향 결정, 무기 회전 방향 등.
        /// </summary>
        public Vector2 MoveInput => _rawMoveInput;

        /// <summary>
        /// 공격 버튼 현재 홀드 여부.
        /// 강공격(A홀드) 판정에 사용.
        /// </summary>
        public bool IsAttackHeld => _isAttackHeld;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            // ── 싱글턴 설정 ──────────────────────
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // ── ActionMap 빌드 ──────────────────────
            BuildInGameMap();
        }

        private void OnEnable()
        {
            // 씬 활성화 시 InputActionMap 활성화
            _inGameMap?.Enable();
        }

        private void OnDisable()
        {
            // 비활성화 시 ActionMap 비활성화 (메모리 누수 방지)
            _inGameMap?.Disable();
        }

        private void OnDestroy()
        {
            // ActionMap 해제 (이벤트 콜백 정리)
            _inGameMap?.Dispose();

            if (Instance == this)
                Instance = null;
        }

        private void Update()
        {
            // 매 프레임 이동 입력 벡터를 읽어 이벤트 발행
            // (performed/canceled 콜백 방식 대신 폴링 방식으로 안정성 확보)
            ReadMoveInput();
        }

        // ══════════════════════════════════════════════════════
        // ActionMap 빌드
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// InGame ActionMap 을 코드로 생성한다.
        /// Move(2DVector Composite) / Dash / Attack / Seal 4개 Action 등록.
        ///
        /// [왜 코드 방식 ActionMap 인가?]
        ///   .inputactions 에셋 파일 없이 작동.
        ///   키 바인딩을 Inspector 에서 직접 조절 가능.
        ///   POC07 InputManager 와 동일한 방식.
        /// </summary>
        private void BuildInGameMap()
        {
            _inGameMap = new InputActionMap("InGame");

            // ── 이동 (2DVector Composite) ──────────────────────
            // WASD 4방향 + 방향키 4방향 동시 지원
            // 두 입력이 결합되어 Vector2 로 전달됨
            _actionMove = _inGameMap.AddAction("Move", InputActionType.Value);

            // WASD 바인딩
            _actionMove.AddCompositeBinding("2DVector")
                .With("Up", KeyToPath(_keyMoveUp))
                .With("Down", KeyToPath(_keyMoveDown))
                .With("Left", KeyToPath(_keyMoveLeft))
                .With("Right", KeyToPath(_keyMoveRight));

            // 방향키 추가 바인딩 (WASD 와 동시 지원)
            _actionMove.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/rightArrow");

            // 게임패드 좌측 스틱
            _actionMove.AddBinding("<Gamepad>/leftStick");

            // ── 대시 ──────────────────────
            _actionDash = _inGameMap.AddAction("Dash", InputActionType.Button);
            _actionDash.AddBinding(KeyToPath(_keyDash));
            _actionDash.AddBinding("<Gamepad>/buttonEast"); // 게임패드 East (B/○)

            // ── 공격 ──────────────────────
            _actionAttack = _inGameMap.AddAction("Attack", InputActionType.Button);
            _actionAttack.AddBinding(KeyToPath(_keyAttack));
            _actionAttack.AddBinding("<Gamepad>/buttonWest"); // 게임패드 West (X/□)

            // ── 봉인 집행 ──────────────────────
            _actionSeal = _inGameMap.AddAction("Seal", InputActionType.Button);
            _actionSeal.AddBinding(KeyToPath(_keySeal));
            _actionSeal.AddBinding("<Gamepad>/buttonSouth"); // 게임패드 South (A/×)

            // ── 콜백 등록 ──────────────────────
            RegisterCallbacks();
        }

        /// <summary>
        /// 각 Action 에 콜백 함수를 등록한다.
        /// Awake 에서 BuildInGameMap() 직후 호출.
        /// </summary>
        private void RegisterCallbacks()
        {
            // 대시 — 누름 시 1회 발행
            _actionDash.performed += _ =>
            {
                if (!_dashBlocked)
                    OnDash?.Invoke();
            };

            // 공격 — 누름 / 뗌 분리 (강공격 홀드 대응)
            _actionAttack.performed += _ =>
            {
                _isAttackHeld = true;
                if (!_actionBlocked)
                    OnAttack?.Invoke();
            };
            _actionAttack.canceled += _ =>
            {
                _isAttackHeld = false;
                OnAttackReleased?.Invoke();
            };

            // 봉인 집행 — 누름 시 1회 발행
            // 실제 집행 가능 여부는 PlayerSealExecutor 에서 판정
            _actionSeal.performed += _ =>
            {
                if (!_actionBlocked)
                    OnSeal?.Invoke();
            };
        }

        // ══════════════════════════════════════════════════════
        // 이동 입력 폴링
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 매 프레임 이동 입력을 폴링하여 OnMove 이벤트를 발행한다.
        ///
        /// [폴링 방식을 선택한 이유]
        ///   performed/canceled 콜백은 버튼 변화 순간에만 호출.
        ///   이동은 매 프레임 연속적으로 필요 → 폴링이 더 안정적.
        ///   _moveBlocked 시 Vector2.zero 발행하여 즉시 멈춤 보장.
        /// </summary>
        private void ReadMoveInput()
        {
            if (_actionMove == null) return;

            Vector2 input = _actionMove.ReadValue<Vector2>();

            // 이동 차단 시 강제로 zero 발행 (캐릭터 즉시 정지)
            if (_moveBlocked)
                input = Vector2.zero;

            _rawMoveInput = input;
            OnMove?.Invoke(input);
        }

        // ══════════════════════════════════════════════════════
        // 외부 API — 입력 차단 / 해제
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 이동 입력을 차단한다.
        /// 봉인 집행 상태, 피격 경직, 연출 중에 호출.
        /// </summary>
        public void BlockMove() => _moveBlocked = true;

        /// <summary>
        /// 이동 입력 차단을 해제한다.
        /// 상태 종료 시 반드시 호출.
        /// </summary>
        public void UnblockMove() => _moveBlocked = false;

        /// <summary>
        /// 대시 입력을 차단한다.
        /// </summary>
        public void BlockDash() => _dashBlocked = true;

        /// <summary>
        /// 대시 입력 차단을 해제한다.
        /// </summary>
        public void UnblockDash() => _dashBlocked = false;

        /// <summary>
        /// 공격 / 봉인 입력을 차단한다.
        /// </summary>
        public void BlockAction() => _actionBlocked = true;

        /// <summary>
        /// 공격 / 봉인 입력 차단을 해제한다.
        /// </summary>
        public void UnblockAction() => _actionBlocked = false;

        /// <summary>
        /// 모든 입력을 차단한다.
        /// 컷씬, 페이즈 전환 등 전체 차단 시 사용.
        /// </summary>
        public void BlockAll()
        {
            _moveBlocked = true;
            _dashBlocked = true;
            _actionBlocked = true;
        }

        /// <summary>
        /// 모든 입력 차단을 해제한다.
        /// </summary>
        public void UnblockAll()
        {
            _moveBlocked = false;
            _dashBlocked = false;
            _actionBlocked = false;
        }

        // ══════════════════════════════════════════════════════
        // 내부 유틸리티
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Unity Key 열거형 → InputSystem 경로 문자열로 변환.
        /// 예: Key.W → "&lt;Keyboard&gt;/w"
        /// POC07 InputManager.cs 와 동일한 방식.
        /// </summary>
        /// <param name="key">변환할 Key 열거형 값.</param>
        /// <returns>InputSystem 바인딩 경로 문자열.</returns>
        private static string KeyToPath(Key key)
        {
            // Key 열거형의 ToString() 이 InputSystem 경로명과 대부분 일치
            // 단, 일부 키는 소문자 변환 필요
            return $"<Keyboard>/{key.ToString().ToLower()}";
        }
    }
}