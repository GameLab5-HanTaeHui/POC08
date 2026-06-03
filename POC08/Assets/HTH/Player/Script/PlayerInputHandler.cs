// ============================================================
// PlayerInputHandler.cs  v1.2
// 탑뷰 플레이어 입력 통합 관리 컴포넌트
//
// [v1.2 변경 — IsSealHeld 프로퍼티 추가]
//   _isSealHeld 내부 상태 변수 추가.
//   RegisterCallbacks() 봉인 콜백에 performed/canceled 로 갱신.
//   IsSealHeld 프로퍼티 추가.
//
//   [추가 이유]
//     BossWardenSealExecutor 가 S키 홀드 상태를 매 프레임 폴링해야 함.
//     IsAttackHeld 와 동일한 방식으로 구현.
//     OnSeal 이벤트(1회 발행)와 IsSealHeld(지속 상태) 는 용도가 다름:
//       OnSeal      = 눌린 순간 트리거 (기존 유지)
//       IsSealHeld  = 현재 누르고 있는지 여부 (신규, BossWardenSealExecutor 폴링용)
//
// [v1.1 변경]
//   SEAL_README 키 할당 기준으로 전면 수정.
//   이동   : WASD 제거 → 방향키(↑↓←→) 전용
//   대시   : LShift → Space
//   공격   : J → A (Button / 홀드 릴리즈 분리)
//   봉인   : K → S (봉인 집행 / 코어 해제 겸용)
//   추가   : 상호작용(E) / 회피·취소(Shift) / 메뉴(Esc) Action + 이벤트
//   헤더 주석 전체 갱신.
//
// [ActionMap 구조]
//   _inGameMap : Move / Dash / Attack / Seal / Interact / Cancel / Menu
//
// [키 바인딩 — SEAL_README 기준]
//   이동         : 방향키 (↑↓←→)
//   대시         : Space
//   기본 공격    : A
//   강공격       : A 홀드 후 릴리즈 (OnAttackReleased)
//   봉인 / 코어  : S
//   상호작용     : E
//   회피 / 취소  : Shift
//   메뉴         : Esc
//
// [입력 차단 시스템]
//   _moveBlocked / _dashBlocked / _actionBlocked
//   외부(StateMachine 상태 등)에서 Block/Unblock 호출로 제어.
//
// [싱글턴]
//   씬 내 단일 인스턴스. PlayerMoveController 등에서 참조.
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
    /// 탑뷰 플레이어 입력 통합 관리 컴포넌트. (v1.1)
    ///
    /// ────────────────────────────────────────────────────
    /// [외부 구독 예시]
    ///   PlayerInputHandler.Instance.OnMove      += HandleMove;
    ///   PlayerInputHandler.Instance.OnDash      += HandleDash;
    ///   PlayerInputHandler.Instance.OnAttack    += HandleAttack;
    ///   PlayerInputHandler.Instance.OnSeal      += HandleSeal;
    ///   PlayerInputHandler.Instance.OnInteract  += HandleInteract;
    ///   PlayerInputHandler.Instance.OnCancel    += HandleCancel;
    ///   PlayerInputHandler.Instance.OnMenu      += HandleMenu;
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

        [Header("── 이동 키 ──────────────────────")]

        /// <summary>
        /// 이동 Up 키. 기본 UpArrow.
        /// README: 이동 = 방향키 전용.
        /// </summary>
        [Tooltip("이동 Up 키. 기본 ↑ (UpArrow).")]
        [SerializeField] private Key _keyMoveUp = Key.UpArrow;

        /// <summary>
        /// 이동 Down 키. 기본 DownArrow.
        /// </summary>
        [Tooltip("이동 Down 키. 기본 ↓ (DownArrow).")]
        [SerializeField] private Key _keyMoveDown = Key.DownArrow;

        /// <summary>
        /// 이동 Left 키. 기본 LeftArrow.
        /// </summary>
        [Tooltip("이동 Left 키. 기본 ← (LeftArrow).")]
        [SerializeField] private Key _keyMoveLeft = Key.LeftArrow;

        /// <summary>
        /// 이동 Right 키. 기본 RightArrow.
        /// </summary>
        [Tooltip("이동 Right 키. 기본 → (RightArrow).")]
        [SerializeField] private Key _keyMoveRight = Key.RightArrow;

        [Header("── 전투 키 ──────────────────────")]

        /// <summary>
        /// 대시 키. 기본 Space.
        /// README: 대시 = Space.
        /// </summary>
        [Tooltip("대시 키. 기본 Space.")]
        [SerializeField] private Key _keyDash = Key.Space;

        /// <summary>
        /// 공격 키. 기본 A.
        /// 탭 → 기본 공격 / 홀드 릴리즈 → 강공격.
        /// README: 기본 공격 = A / 강공격 = A 홀드 후 릴리즈.
        /// </summary>
        [Tooltip("공격 키. 기본 A. 탭=기본 공격 / 홀드 릴리즈=강공격.")]
        [SerializeField] private Key _keyAttack = Key.A;

        /// <summary>
        /// 봉인 집행 / 코어 해제 키. 기본 S.
        /// 봉인도 100% 부위에 접근 후 입력 → 봉인 집행.
        /// 그로기 코어 접근 후 입력 → 코어 해제.
        /// README: 봉인 집행 = S / 코어 해제 = S.
        /// </summary>
        [Tooltip("봉인 집행 / 코어 해제 키. 기본 S.")]
        [SerializeField] private Key _keySeal = Key.S;

        [Header("── 보조 키 ──────────────────────")]

        /// <summary>
        /// 상호작용 키. 기본 E.
        /// 쉼터, 상점, 이벤트 노드 등 상호작용.
        /// README: 상호작용 = E.
        /// </summary>
        [Tooltip("상호작용 키. 기본 E.")]
        [SerializeField] private Key _keyInteract = Key.E;

        /// <summary>
        /// 회피 / 취소 키. 기본 LeftShift.
        /// 봉인 집행 취소, UI 뒤로가기 등.
        /// README: 회피 / 취소 = Shift.
        /// </summary>
        [Tooltip("회피 / 취소 키. 기본 Shift.")]
        [SerializeField] private Key _keyCancel = Key.LeftShift;

        /// <summary>
        /// 메뉴 키. 기본 Escape.
        /// 일시정지, 옵션 메뉴 열기.
        /// README: 메뉴 = Esc.
        /// </summary>
        [Tooltip("메뉴 / 일시정지 키. 기본 Esc.")]
        [SerializeField] private Key _keyMenu = Key.Escape;

        // ──────────────────────────────────────────
        // InputAction 인스턴스 (코드 기반 ActionMap)
        // ──────────────────────────────────────────

        /// <summary>
        /// 인게임 ActionMap.
        /// Move / Dash / Attack / Seal / Interact / Cancel / Menu 포함.
        /// </summary>
        private InputActionMap _inGameMap;

        /// <summary>
        /// 이동 입력 Action. Vector2 Value 타입.
        /// 방향키 2DVector Composite + 게임패드 좌스틱.
        /// </summary>
        private InputAction _actionMove;

        /// <summary>
        /// 대시 Action. Button 타입.
        /// Space / 게임패드 East.
        /// </summary>
        private InputAction _actionDash;

        /// <summary>
        /// 공격 Action. Button 타입.
        /// performed = 기본 공격 / canceled = 강공격 릴리즈 판정.
        /// </summary>
        private InputAction _actionAttack;

        /// <summary>
        /// 봉인 집행 / 코어 해제 Action. Button 타입.
        /// S키 / 게임패드 South.
        /// </summary>
        private InputAction _actionSeal;

        /// <summary>
        /// 상호작용 Action. Button 타입.
        /// E키 / 게임패드 North.
        /// </summary>
        private InputAction _actionInteract;

        /// <summary>
        /// 회피 / 취소 Action. Button 타입.
        /// Shift키 / 게임패드 West.
        /// </summary>
        private InputAction _actionCancel;

        /// <summary>
        /// 메뉴 Action. Button 타입.
        /// Esc키 / 게임패드 Start.
        /// </summary>
        private InputAction _actionMenu;

        // ──────────────────────────────────────────
        // 내부 상태 — 입력 차단 플래그
        // ──────────────────────────────────────────

        /// <summary>
        /// 이동 입력 차단 플래그.
        /// true 시 OnMove 이벤트 Vector2.zero 강제 발행.
        /// 봉인 집행 / 피격 경직 / 컷씬 등에서 true.
        /// </summary>
        private bool _moveBlocked;

        /// <summary>
        /// 대시 입력 차단 플래그.
        /// true 시 OnDash 이벤트 발행 안 함.
        /// </summary>
        private bool _dashBlocked;

        /// <summary>
        /// 공격 / 봉인 / 상호작용 입력 차단 플래그.
        /// true 시 OnAttack / OnSeal / OnInteract 이벤트 발행 안 함.
        /// 메뉴(OnMenu) / 취소(OnCancel) 는 차단하지 않음.
        /// </summary>
        private bool _actionBlocked;

        // ──────────────────────────────────────────
        // 내부 상태 — 현재 입력값
        // ──────────────────────────────────────────

        /// <summary>
        /// 현재 이동 입력 벡터. 매 프레임 Update 폴링으로 읽음.
        /// 외부에서 MoveInput 프로퍼티로 접근.
        /// </summary>
        private Vector2 _rawMoveInput;

        /// <summary>
        /// 공격 버튼 현재 홀드 여부.
        /// performed = true / canceled = false.
        /// PlayerAttackController 에서 강공격 홀드 시간 계산에 사용.
        /// </summary>
        private bool _isAttackHeld;

        /// <summary>
        /// 봉인 버튼(S) 현재 홀드 여부.
        /// performed = true / canceled = false.
        /// BossWardenSealExecutor 에서 매 프레임 폴링하여 홀드 시간 누적.
        /// _isAttackHeld 와 동일한 방식으로 관리.
        ///
        /// [OnSeal 이벤트와의 차이]
        ///   OnSeal       = 눌린 순간 1회 발행 (트리거)
        ///   IsSealHeld   = 현재 누르고 있는지 여부 (지속 상태 폴링)
        /// </summary>
        private bool _isSealHeld;

        // ──────────────────────────────────────────
        // 이벤트 — 외부 구독
        // ──────────────────────────────────────────

        /// <summary>
        /// 이동 입력 변경 시 매 프레임 발행 (폴링 방식).
        /// 파라미터: 방향 Vector2. 입력 없음 = Vector2.zero.
        /// _moveBlocked 시 Vector2.zero 강제 발행.
        /// </summary>
        public event Action<Vector2> OnMove;

        /// <summary>
        /// 대시 버튼 눌림 시 1회 발행.
        /// _dashBlocked 시 발행 안 함.
        /// </summary>
        public event Action OnDash;

        /// <summary>
        /// 공격 버튼(A) 눌림 시 1회 발행.
        /// _actionBlocked 시 발행 안 함.
        /// README: 기본 공격 = A.
        /// </summary>
        public event Action OnAttack;

        /// <summary>
        /// 공격 버튼(A) 뗌 시 1회 발행.
        /// 차단 여부와 무관하게 항상 발행 (강공격 취소 보장).
        /// PlayerAttackController 에서 홀드 시간 확인 후 강공격 판정.
        /// README: 강공격 = A 홀드 후 릴리즈.
        /// </summary>
        public event Action OnAttackReleased;

        /// <summary>
        /// 봉인 집행 / 코어 해제 버튼(S) 눌림 시 1회 발행.
        /// _actionBlocked 시 발행 안 함.
        /// 실제 집행 가능 여부는 PlayerSealExecutor 에서 판정.
        /// README: 봉인 집행 = S / 코어 해제 = S.
        /// </summary>
        public event Action OnSeal;

        /// <summary>
        /// 상호작용 버튼(E) 눌림 시 1회 발행.
        /// _actionBlocked 시 발행 안 함.
        /// 쉼터, 상점, 이벤트 노드 등 상호작용 처리.
        /// README: 상호작용 = E.
        /// </summary>
        public event Action OnInteract;

        /// <summary>
        /// 회피 / 취소 버튼(Shift) 눌림 시 1회 발행.
        /// 차단 여부와 무관하게 항상 발행 (취소는 언제든 가능해야 함).
        /// 봉인 집행 취소, UI 뒤로가기 등.
        /// README: 회피 / 취소 = Shift.
        /// </summary>
        public event Action OnCancel;

        /// <summary>
        /// 메뉴 버튼(Esc) 눌림 시 1회 발행.
        /// 차단 여부와 무관하게 항상 발행 (메뉴는 언제든 가능해야 함).
        /// 일시정지, 옵션 메뉴 열기.
        /// README: 메뉴 = Esc.
        /// </summary>
        public event Action OnMenu;

        // ──────────────────────────────────────────
        // 프로퍼티
        // ──────────────────────────────────────────

        /// <summary>
        /// 현재 이동 입력 벡터 (방향키 기준).
        /// 대시 방향 결정, 공격 방향 보조 등에 사용.
        /// </summary>
        public Vector2 MoveInput => _rawMoveInput;

        /// <summary>
        /// 공격 버튼 현재 홀드 여부.
        /// 강공격 홀드 시간 계산에 사용.
        /// </summary>
        public bool IsAttackHeld => _isAttackHeld;

        /// <summary>
        /// 봉인 버튼(S) 현재 홀드 여부.
        /// BossWardenSealExecutor 에서 매 프레임 폴링하여 홀드 시간 누적.
        ///
        /// [사용 예시 — BossWardenSealExecutor]
        ///   if (PlayerInputHandler.Instance.IsSealHeld)
        ///       _holdTimer += Time.unscaledDeltaTime;
        ///   else
        ///       ResetHoldTimer();
        /// </summary>
        public bool IsSealHeld => _isSealHeld;

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
            _inGameMap?.Enable();
        }

        private void OnDisable()
        {
            _inGameMap?.Disable();
        }

        private void OnDestroy()
        {
            _inGameMap?.Dispose();

            if (Instance == this)
                Instance = null;
        }

        private void Update()
        {
            // 이동은 매 프레임 폴링 (performed/canceled 콜백보다 안정적)
            ReadMoveInput();
        }

        // ══════════════════════════════════════════════════════
        // ActionMap 빌드
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// InGame ActionMap 을 코드로 생성한다.
        /// Move / Dash / Attack / Seal / Interact / Cancel / Menu 등록.
        ///
        /// [코드 기반 ActionMap 이유]
        ///   .inputactions 에셋 없이 작동.
        ///   Inspector 에서 키 바인딩 직접 조절 가능.
        ///   POC07 InputManager.cs 와 동일한 방식.
        /// </summary>
        private void BuildInGameMap()
        {
            _inGameMap = new InputActionMap("InGame");

            // ── 이동 (방향키 전용 + 게임패드) ──────────────────────
            // README: 이동 = 방향키(↑↓←→) 전용
            _actionMove = _inGameMap.AddAction("Move", InputActionType.Value);

            _actionMove.AddCompositeBinding("2DVector")
                .With("Up", KeyToPath(_keyMoveUp))
                .With("Down", KeyToPath(_keyMoveDown))
                .With("Left", KeyToPath(_keyMoveLeft))
                .With("Right", KeyToPath(_keyMoveRight));

            // 게임패드 좌측 스틱
            _actionMove.AddBinding("<Gamepad>/leftStick");

            // ── 대시 ──────────────────────
            // README: 대시 = Space
            _actionDash = _inGameMap.AddAction("Dash", InputActionType.Button);
            _actionDash.AddBinding(KeyToPath(_keyDash));
            _actionDash.AddBinding("<Gamepad>/buttonEast");

            // ── 공격 ──────────────────────
            // README: 기본 공격 = A / 강공격 = A 홀드 후 릴리즈
            _actionAttack = _inGameMap.AddAction("Attack", InputActionType.Button);
            _actionAttack.AddBinding(KeyToPath(_keyAttack));
            _actionAttack.AddBinding("<Gamepad>/buttonWest");

            // ── 봉인 집행 / 코어 해제 ──────────────────────
            // README: 봉인 집행 = S / 코어 해제 = S
            _actionSeal = _inGameMap.AddAction("Seal", InputActionType.Button);
            _actionSeal.AddBinding(KeyToPath(_keySeal));
            _actionSeal.AddBinding("<Gamepad>/buttonSouth");

            // ── 상호작용 ──────────────────────
            // README: 상호작용 = E
            _actionInteract = _inGameMap.AddAction("Interact", InputActionType.Button);
            _actionInteract.AddBinding(KeyToPath(_keyInteract));
            _actionInteract.AddBinding("<Gamepad>/buttonNorth");

            // ── 회피 / 취소 ──────────────────────
            // README: 회피 / 취소 = Shift
            _actionCancel = _inGameMap.AddAction("Cancel", InputActionType.Button);
            _actionCancel.AddBinding(KeyToPath(_keyCancel));
            _actionCancel.AddBinding("<Gamepad>/rightShoulder");

            // ── 메뉴 ──────────────────────
            // README: 메뉴 = Esc
            _actionMenu = _inGameMap.AddAction("Menu", InputActionType.Button);
            _actionMenu.AddBinding(KeyToPath(_keyMenu));
            _actionMenu.AddBinding("<Gamepad>/start");

            // ── 콜백 등록 ──────────────────────
            RegisterCallbacks();
        }

        /// <summary>
        /// 각 Action 에 콜백을 등록한다.
        /// BuildInGameMap() 내부에서 마지막에 호출.
        /// </summary>
        private void RegisterCallbacks()
        {
            // 대시 — 차단 가능
            _actionDash.performed += _ =>
            {
                if (!_dashBlocked) OnDash?.Invoke();
            };

            // 공격 — 누름(기본공격) / 뗌(강공격 릴리즈) 분리
            // OnAttackReleased 는 차단 무관 항상 발행 (강공격 취소 보장)
            _actionAttack.performed += _ =>
            {
                _isAttackHeld = true;
                if (!_actionBlocked) OnAttack?.Invoke();
            };
            _actionAttack.canceled += _ =>
            {
                _isAttackHeld = false;
                OnAttackReleased?.Invoke(); // 차단 무관
            };

            // 봉인 / 코어 해제 — 차단 가능 (OnSeal 이벤트)
            // _isSealHeld 는 차단 무관 항상 갱신
            // (BossWardenSealExecutor 가 홀드 여부를 직접 폴링하므로)
            _actionSeal.performed += _ =>
            {
                _isSealHeld = true;
                if (!_actionBlocked) OnSeal?.Invoke();
            };
            _actionSeal.canceled += _ =>
            {
                _isSealHeld = false;
            };

            // 상호작용 — 차단 가능
            _actionInteract.performed += _ =>
            {
                if (!_actionBlocked) OnInteract?.Invoke();
            };

            // 회피 / 취소 — 항상 발행 (취소는 언제든 가능해야 함)
            _actionCancel.performed += _ => OnCancel?.Invoke();

            // 메뉴 — 항상 발행 (일시정지는 언제든 가능해야 함)
            _actionMenu.performed += _ => OnMenu?.Invoke();
        }

        // ══════════════════════════════════════════════════════
        // 이동 입력 폴링
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 매 프레임 이동 입력을 폴링하여 OnMove 이벤트를 발행한다.
        ///
        /// [폴링 방식 선택 이유]
        ///   performed/canceled 콜백은 변화 순간에만 호출.
        ///   이동은 매 프레임 연속 처리 필요 → 폴링이 더 안정적.
        ///   _moveBlocked 시 zero 강제 발행 → 캐릭터 즉시 정지 보장.
        /// </summary>
        private void ReadMoveInput()
        {
            if (_actionMove == null) return;

            Vector2 input = _actionMove.ReadValue<Vector2>();

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
        /// 봉인 집행 상태 / 피격 경직 / 컷씬 등에서 호출.
        /// </summary>
        public void BlockMove() => _moveBlocked = true;

        /// <summary> 이동 입력 차단 해제. 상태 종료 시 반드시 호출. </summary>
        public void UnblockMove() => _moveBlocked = false;

        /// <summary> 대시 입력을 차단한다. </summary>
        public void BlockDash() => _dashBlocked = true;

        /// <summary> 대시 입력 차단 해제. </summary>
        public void UnblockDash() => _dashBlocked = false;

        /// <summary>
        /// 공격 / 봉인 / 상호작용 입력을 차단한다.
        /// 메뉴(OnMenu) 와 취소(OnCancel) 는 차단하지 않음.
        /// </summary>
        public void BlockAction() => _actionBlocked = true;

        /// <summary> 공격 / 봉인 / 상호작용 입력 차단 해제. </summary>
        public void UnblockAction() => _actionBlocked = false;

        /// <summary>
        /// 모든 입력을 차단한다.
        /// 컷씬, 페이즈 전환 등 전체 차단 시 사용.
        /// 주의: 메뉴와 취소는 코드 내부에서 항상 발행됨.
        /// </summary>
        public void BlockAll()
        {
            _moveBlocked = true;
            _dashBlocked = true;
            _actionBlocked = true;
        }

        /// <summary> 모든 입력 차단 해제. </summary>
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
        /// Unity Key 열거형 → InputSystem 바인딩 경로 문자열로 변환.
        ///
        /// [1차] Keyboard.current 컨트롤 순회 → keyCode 일치 경로 반환
        /// [2차] 폴백 — Digit1~9 → "1"~"9" / 나머지 camelCase 변환
        ///
        /// POC07 InputManager.KeyToPath 와 동일한 방식.
        /// </summary>
        /// <param name="key">변환할 Key 열거형 값.</param>
        /// <returns>InputSystem 바인딩 경로 문자열.</returns>
        private static string KeyToPath(Key key)
        {
            // 1차: 런타임 Keyboard 컨트롤에서 일치하는 경로 탐색
            if (Keyboard.current != null)
            {
                foreach (var control in Keyboard.current.allControls)
                {
                    if (control is UnityEngine.InputSystem.Controls.KeyControl kc
                        && kc.keyCode == key)
                        return control.path;
                }
            }

            // 2차: 폴백 변환
            string name = key.ToString();

            // Digit1~9 → "1"~"9"
            if (name.StartsWith("Digit"))
                name = name.Substring(5);

            // 나머지: 첫 글자 소문자 변환
            return $"<Keyboard>/{char.ToLower(name[0]) + name.Substring(1)}";
        }
    }
}