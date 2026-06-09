// ============================================================
// PlayerInputHandler.cs  v1.4
// 탑뷰 플레이어 입력 통합 관리 컴포넌트
//
// [v1.4 변경 — 마우스 기반 조작으로 전면 변경]
//
//   [키 바인딩 변경]
//     이동         : 방향키(↑↓←→) → WASD
//     공격         : A키           → 마우스 좌클릭
//     봉인 집행    : S키           → F키 + 마우스 우클릭 (둘 다 인식)
//     대시         : Space         → 유지
//     상호작용     : E             → 유지
//     취소/메뉴    : Shift/Esc     → 유지
//
//   [마우스 추가]
//     _actionMousePosition : 마우스 스크린 좌표 Value Action
//     _actionMouseLeft     : 마우스 좌클릭 Button (공격)
//     _actionMouseRight    : 마우스 우클릭 Button (봉인 입력)
//
//     MouseScreenPosition  : Vector2 스크린 좌표 프로퍼티
//     MouseWorldPosition   : Vector2 월드 좌표 프로퍼티
//       → Camera.main.ScreenToWorldPoint 변환
//       → Cinemachine Follow 카메라 지원 (Camera.main 사용)
//
// [v1.5 변경 — 봉인 유지 상태 제거, OnSeal 즉시 입력만 사용]
// [v1.1 변경 — SEAL_README 키 할당 기준 전면 수정]
//
// [키 바인딩 — v1.4 확정]
//   이동         : WASD
//   대시         : Space
//   공격(기본)   : 마우스 좌클릭
//   공격(강)     : 마우스 좌클릭 홀드 후 릴리즈
//   봉인 집행    : F 또는 마우스 우클릭
//   상호작용     : E
//   취소         : LeftShift
//   메뉴         : Esc
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
    /// 탑뷰 플레이어 입력 통합 관리 컴포넌트. (v1.4)
    ///
    /// ────────────────────────────────────────────────────
    /// [외부 구독 예시]
    ///   PlayerInputHandler.Instance.OnMove      += HandleMove;
    ///   PlayerInputHandler.Instance.OnDash      += HandleDash;
    ///   PlayerInputHandler.Instance.OnAttack    += HandleAttack;
    ///   PlayerInputHandler.Instance.OnSeal      += HandleSeal;
    ///   PlayerInputHandler.Instance.OnInteract  += HandleInteract;
    ///
    /// [마우스 월드 좌표]
    ///   PlayerInputHandler.Instance.MouseWorldPosition
    ///   → PlayerMoveController 에서 FacingDirection 계산에 사용
    /// ────────────────────────────────────────────────────
    /// </summary>
    public class PlayerInputHandler : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        // 싱글턴
        // ══════════════════════════════════════════════════════

        /// <summary>전역 단일 인스턴스.</summary>
        public static PlayerInputHandler Instance { get; private set; }

        // ══════════════════════════════════════════════════════
        // Inspector — 키 바인딩
        // ══════════════════════════════════════════════════════

        [Header("── 이동 키 (WASD) ──────────────────────")]

        [Tooltip("이동 Up 키. 기본 W.")]
        [SerializeField] private Key _keyMoveUp = Key.W;

        [Tooltip("이동 Down 키. 기본 S.")]
        [SerializeField] private Key _keyMoveDown = Key.S;

        [Tooltip("이동 Left 키. 기본 A.")]
        [SerializeField] private Key _keyMoveLeft = Key.A;

        [Tooltip("이동 Right 키. 기본 D.")]
        [SerializeField] private Key _keyMoveRight = Key.D;

        [Header("── 전투 키 ──────────────────────")]

        [Tooltip("대시 키. 기본 Space.")]
        [SerializeField] private Key _keyDash = Key.Space;

        /// <summary>
        /// 봉인 집행 키보드 키. 기본 F.
        /// 마우스 우클릭과 함께 즉시 봉인 입력으로 사용.
        /// </summary>
        [Tooltip("봉인 집행 키. 기본 F. 마우스 우클릭도 동일하게 즉시 입력 처리.")]
        [SerializeField] private Key _keySeal = Key.F;

        [Header("── 보조 키 ──────────────────────")]

        [Tooltip("상호작용 키. 기본 E.")]
        [SerializeField] private Key _keyInteract = Key.E;

        [Tooltip("취소 키. 기본 LeftShift.")]
        [SerializeField] private Key _keyCancel = Key.LeftShift;

        [Tooltip("메뉴 키. 기본 Esc.")]
        [SerializeField] private Key _keyMenu = Key.Escape;

        // ══════════════════════════════════════════════════════
        // InputAction 인스턴스
        // ══════════════════════════════════════════════════════

        private InputActionMap _inGameMap;

        // 이동
        private InputAction _actionMove;

        // 대시
        private InputAction _actionDash;

        // 공격 — 마우스 좌클릭
        private InputAction _actionAttack;

        // 봉인 집행 — F키
        private InputAction _actionSeal;

        // 상호작용
        private InputAction _actionInteract;

        // 취소 / 메뉴
        private InputAction _actionCancel;
        private InputAction _actionMenu;

        // 마우스 좌표
        private InputAction _actionMousePosition;

        // 마우스 우클릭 (봉인 집행 보조)
        private InputAction _actionMouseRight;

        // ══════════════════════════════════════════════════════
        // 내부 상태 — 입력 차단
        // ══════════════════════════════════════════════════════

        /// <summary>이동 입력 차단. true 시 OnMove zero 강제 발행.</summary>
        private bool _moveBlocked;

        /// <summary>대시 입력 차단.</summary>
        private bool _dashBlocked;

        /// <summary>공격 / 봉인 / 상호작용 입력 차단.</summary>
        private bool _actionBlocked;

        // ══════════════════════════════════════════════════════
        // 내부 상태 — 현재 입력값
        // ══════════════════════════════════════════════════════

        /// <summary>현재 이동 입력 벡터. 매 프레임 폴링.</summary>
        private Vector2 _rawMoveInput;

        /// <summary>공격 버튼 홀드 여부. (마우스 좌클릭)</summary>
        private bool _isAttackHeld;

        /// <summary>마우스 스크린 좌표. 매 프레임 갱신.</summary>
        private Vector2 _mouseScreenPosition;

        // ══════════════════════════════════════════════════════
        // 이벤트
        // ══════════════════════════════════════════════════════

        /// <summary>이동 입력 변경 시 매 프레임 발행.</summary>
        public event Action<Vector2> OnMove;

        /// <summary>대시 버튼 눌림 시 1회 발행.</summary>
        public event Action OnDash;

        /// <summary>공격 버튼(마우스 좌클릭) 눌림 시 1회 발행.</summary>
        public event Action OnAttack;

        /// <summary>공격 버튼 뗌 시 1회 발행. 차단 무관 항상 발행.</summary>
        public event Action OnAttackReleased;

        /// <summary>봉인 집행 버튼(F 또는 우클릭) 눌림 시 1회 발행.</summary>
        public event Action OnSeal;

        /// <summary>상호작용 버튼 눌림 시 1회 발행.</summary>
        public event Action OnInteract;

        /// <summary>취소 버튼 눌림 시 1회 발행. 차단 무관 항상 발행.</summary>
        public event Action OnCancel;

        /// <summary>메뉴 버튼 눌림 시 1회 발행. 차단 무관 항상 발행.</summary>
        public event Action OnMenu;

        // ══════════════════════════════════════════════════════
        // 프로퍼티
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 현재 이동 입력 벡터.
        /// 이동 잠금(_moveBlocked) 중에도 실제 눌린 키 반환.
        /// 대시 방향, 콤보 방향 결정에 사용.
        /// </summary>
        public Vector2 MoveInput => _rawMoveInput;

        /// <summary>공격 버튼(마우스 좌클릭) 홀드 여부.</summary>
        public bool IsAttackHeld => _isAttackHeld;

        public bool IsActionBlocked => _actionBlocked;

        /// <summary>
        /// 마우스 스크린 좌표.
        /// </summary>
        public Vector2 MouseScreenPosition => _mouseScreenPosition;

        /// <summary>
        /// 마우스 월드 좌표.
        /// Camera.main.ScreenToWorldPoint 변환.
        /// Cinemachine Follow 카메라 지원.
        ///
        /// [사용 위치]
        ///   PlayerMoveController.UpdateFacingDirection()
        ///   → 플레이어 → 마우스 방향 = FacingDirection
        /// </summary>
        public Vector2 MouseWorldPosition
        {
            get
            {
                if (Camera.main == null) return Vector2.zero;
                Vector3 world = Camera.main.ScreenToWorldPoint(
                    new Vector3(_mouseScreenPosition.x, _mouseScreenPosition.y, 0f));
                return new Vector2(world.x, world.y);
            }
        }

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            BuildInGameMap();
        }

        private void OnEnable() => _inGameMap?.Enable();
        private void OnDisable() => _inGameMap?.Disable();

        private void OnDestroy()
        {
            _inGameMap?.Dispose();
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            ReadMoveInput();
            ReadMousePosition();
        }

        // ══════════════════════════════════════════════════════
        // ActionMap 빌드
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// InGame ActionMap 코드 기반 생성.
        ///
        /// [v1.4 변경]
        ///   이동: WASD 2DVector Composite
        ///   공격: 마우스 좌클릭
        ///   봉인: F키 + 마우스 우클릭 별도 Action (즉시 입력)
        ///   마우스 좌표: Position 읽기 전용 Value Action
        /// </summary>
        private void BuildInGameMap()
        {
            _inGameMap = new InputActionMap("InGame");

            // ── 이동 (WASD + 게임패드) ──────────────────────
            _actionMove = _inGameMap.AddAction("Move", InputActionType.Value);
            _actionMove.AddCompositeBinding("2DVector")
                .With("Up", KeyToPath(_keyMoveUp))
                .With("Down", KeyToPath(_keyMoveDown))
                .With("Left", KeyToPath(_keyMoveLeft))
                .With("Right", KeyToPath(_keyMoveRight));
            _actionMove.AddBinding("<Gamepad>/leftStick");

            // ── 대시 (Space + 게임패드) ──────────────────────
            _actionDash = _inGameMap.AddAction("Dash", InputActionType.Button);
            _actionDash.AddBinding(KeyToPath(_keyDash));
            _actionDash.AddBinding("<Gamepad>/buttonEast");

            // ── 공격 (마우스 좌클릭 + 게임패드) ──────────────────────
            // performed = 기본 공격 / canceled = 강공격 릴리즈 판정
            _actionAttack = _inGameMap.AddAction("Attack", InputActionType.Button);
            _actionAttack.AddBinding("<Mouse>/leftButton");
            _actionAttack.AddBinding("<Gamepad>/buttonWest");

            // ── 봉인 집행 (F키) ──────────────────────
            // 마우스 우클릭은 _actionMouseRight 에서 별도 처리
            _actionSeal = _inGameMap.AddAction("Seal", InputActionType.Button);
            _actionSeal.AddBinding(KeyToPath(_keySeal));
            _actionSeal.AddBinding("<Gamepad>/buttonSouth");

            // ── 마우스 우클릭 (봉인 집행 보조) ──────────────────────
            _actionMouseRight = _inGameMap.AddAction("MouseRight", InputActionType.Button);
            _actionMouseRight.AddBinding("<Mouse>/rightButton");

            // ── 마우스 좌표 ──────────────────────
            _actionMousePosition = _inGameMap.AddAction("MousePosition", InputActionType.Value);
            _actionMousePosition.AddBinding("<Mouse>/position");

            // ── 상호작용 ──────────────────────
            _actionInteract = _inGameMap.AddAction("Interact", InputActionType.Button);
            _actionInteract.AddBinding(KeyToPath(_keyInteract));
            _actionInteract.AddBinding("<Gamepad>/buttonNorth");

            // ── 취소 ──────────────────────
            _actionCancel = _inGameMap.AddAction("Cancel", InputActionType.Button);
            _actionCancel.AddBinding(KeyToPath(_keyCancel));
            _actionCancel.AddBinding("<Gamepad>/rightShoulder");

            // ── 메뉴 ──────────────────────
            _actionMenu = _inGameMap.AddAction("Menu", InputActionType.Button);
            _actionMenu.AddBinding(KeyToPath(_keyMenu));
            _actionMenu.AddBinding("<Gamepad>/start");

            RegisterCallbacks();
        }

        /// <summary>
        /// 각 Action 콜백 등록.
        /// </summary>
        private void RegisterCallbacks()
        {
            // ── 대시 ──────────────────────
            _actionDash.performed += _ =>
            {
                if (!_dashBlocked) OnDash?.Invoke();
            };

            // ── 공격 (마우스 좌클릭) ──────────────────────
            // OnAttackReleased 는 차단 무관 항상 발행 (강공격 취소 보장)
            _actionAttack.performed += _ =>
            {
                _isAttackHeld = true;
                OnAttack?.Invoke();
            };
            _actionAttack.canceled += _ =>
            {
                _isAttackHeld = false;
                OnAttackReleased?.Invoke();
            };

            // ── 봉인 집행 — F키 / 마우스 우클릭 ──────────────────────
            // 봉인 집행은 입력 1회 즉시 실행이다.
            _actionSeal.performed += _ =>
            {
                if (!_actionBlocked) OnSeal?.Invoke();
            };

            _actionMouseRight.performed += _ =>
            {
                if (!_actionBlocked) OnSeal?.Invoke();
            };

            // ── 상호작용 ──────────────────────
            _actionInteract.performed += _ =>
            {
                if (!_actionBlocked) OnInteract?.Invoke();
            };

            // ── 취소 / 메뉴 — 항상 발행 ──────────────────────
            _actionCancel.performed += _ => OnCancel?.Invoke();
            _actionMenu.performed += _ => OnMenu?.Invoke();
        }

        // ══════════════════════════════════════════════════════
        // 이동 / 마우스 폴링
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 매 프레임 이동 입력 폴링.
        /// _moveBlocked 시 zero 강제 발행.
        /// MoveInput 은 잠금 무관 항상 갱신 (대시/콤보 방향 결정용).
        /// </summary>
        private void ReadMoveInput()
        {
            if (_actionMove == null) return;

            Vector2 input = _actionMove.ReadValue<Vector2>();
            _rawMoveInput = input;  // 잠금 무관 갱신

            if (_moveBlocked) input = Vector2.zero;
            OnMove?.Invoke(input);
        }

        /// <summary>
        /// 매 프레임 마우스 스크린 좌표 갱신.
        /// MouseWorldPosition 프로퍼티에서 월드 좌표로 변환.
        /// </summary>
        private void ReadMousePosition()
        {
            if (_actionMousePosition == null) return;
            _mouseScreenPosition = _actionMousePosition.ReadValue<Vector2>();
        }

        // ══════════════════════════════════════════════════════
        // 외부 API — 입력 차단 / 해제
        // ══════════════════════════════════════════════════════

        /// <summary>이동 입력 차단.</summary>
        public void BlockMove() => _moveBlocked = true;

        /// <summary>이동 입력 차단 해제.</summary>
        public void UnblockMove() => _moveBlocked = false;

        /// <summary>대시 입력 차단.</summary>
        public void BlockDash() => _dashBlocked = true;

        /// <summary>대시 입력 차단 해제.</summary>
        public void UnblockDash() => _dashBlocked = false;
        /// <summary>공격 / 봉인 / 상호작용 입력 차단.</summary>
        public void BlockAction()
        {
            Debug.Log($"[PlayerInputHandler] BlockAction 호출 — {System.Environment.StackTrace}");
            _actionBlocked = true;
            _isAttackHeld = false;
        }

        /// <summary>공격 / 봉인 / 상호작용 입력 차단 해제.</summary>
        public void UnblockAction() => _actionBlocked = false;

        /// <summary>
        /// 모든 입력 차단.
        /// BlockAction() 포함.
        /// </summary>
        public void BlockAll()
        {
            _moveBlocked = true;
            _dashBlocked = true;
            BlockAction();
        }

        /// <summary>모든 입력 차단 해제.</summary>
        public void UnblockAll()
        {
            _moveBlocked = false;
            _dashBlocked = false;
            _actionBlocked = false;
        }

        // ══════════════════════════════════════════════════════
        // 유틸리티
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Unity Key 열거형 → InputSystem 바인딩 경로 문자열 변환.
        /// </summary>
        private string KeyToPath(Key key)
        {
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                foreach (var control in keyboard.allKeys)
                {
                    if (control.keyCode == key)
                        return $"<Keyboard>/{control.name}";
                }
            }

            // 폴백 — Digit 및 기타 변환
            string name = key.ToString();
            if (name.StartsWith("Digit"))
                return $"<Keyboard>/{name.Substring(5)}";

            return $"<Keyboard>/{char.ToLower(name[0])}{name.Substring(1)}";
        }
    }
}