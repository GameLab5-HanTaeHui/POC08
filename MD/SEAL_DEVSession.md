# SEAL_DEVSession.md
# 봉인(KEY) 프로젝트 개발 세션 기록

> 작성 기준: 변경사항 발생 시 해당 버전에 기록  
> 파일 역할: 세션 간 개발 연속성 유지용 기록 문서  
> 최신 코드 기준: 프로젝트 파일 내 파싱 파일 (outputs 폴더 아님)

---

## 버전 기록 목차

- [v0.0] 프로젝트 초기화 / 지침 이해
- [v0.1] 2D 탑뷰 8방향 플레이어 이동 구현

---

## v0.0 — 프로젝트 초기화 / 지침 이해

**날짜**: 개발 세션 시작  
**단계**: 핵심 메카닉 구현 (1단계)

### 확인된 프로젝트 정보

| 항목 | 내용 |
|---|---|
| 프로젝트명 | KEY |
| 엔진 | Unity 6000.3.10f1 |
| 렌더 파이프라인 | 2D Universal |
| 카메라 시점 | Top-View |
| namespace | SEAL |
| 패키지 | Cinemachine, DOTween (HOTween v2) |

### 확인된 개발 규칙

- `[SerializeField] private` / `public` 항상 명시
- 변수명 `_` 접두사 사용
- 모든 함수 / 변수에 `/// <summary>` Summary 필수
- `[Tooltip(...)]` 인스펙터 설명 필수
- POC07 = 참고 스크립트 (횡스크롤)
- POC08 = 실제 개발 파일 (탑뷰)
- DOTween 역동적 Rigidbody 움직임 적극 권장 (Sprite Sheet 없음)

### 확인된 POC07 구조 (참고 원본)

| 스크립트 | 역할 |
|---|---|
| `InputManager.cs` | 1D 이동 + 점프 + 대시 + 공격 입력 (횡스크롤) |
| `PlayerMover.cs` | 횡스크롤 Rigidbody2D 이동 처리 |
| `MovementSettings.cs` | 이동 수치 SO |
| `PlayerMovementFacade.cs` | 외부 단일 진입점 |
| `SealComponent.cs` | 봉인 상태 관리 (Dictionary + Queue) |
| `EnemyBossBase.cs` | 보스 추상 베이스 |
| `BossKnightAI.cs` | 10상태 보스 AI |

---

## v0.1 — 2D 탑뷰 8방향 플레이어 이동 구현

**날짜**: 세션 1  
**단계**: 핵심 메카닉 구현 (1단계)  
**작업**: POC08 탑뷰 이동 시스템 첫 구현

### 구현 목표

POC07의 횡스크롤 1D 이동 시스템을 참고하여  
탑뷰 2D 8방향 이동 시스템으로 전면 재설계.

### 생성 파일 목록

| 파일명 | 역할 | 위치 (POC08) |
|---|---|---|
| `PlayerTopViewDataSO.cs` | 이동 수치 ScriptableObject | Assets/SEAL/Player/Data/ |
| `PlayerInputHandler.cs` | New Input System 입력 관리자 | Assets/SEAL/Player/Input/ |
| `PlayerTopViewMover.cs` | 탑뷰 8방향 이동 + 대시 핵심 컴포넌트 | Assets/SEAL/Player/Movement/ |

### POC07 → POC08 변환 내용

| 항목 | POC07 (횡스크롤) | POC08 (탑뷰) |
|---|---|---|
| 이동 입력 | `1DAxis` float (-1 ~ 1) | `2DVector` Vector2 (8방향) |
| 이동 축 | X축만 (좌/우) | X/Y 평면 전체 |
| 점프 | O (Space, 2단 점프) | X (제거) |
| 중력 | O (GravityScale > 0) | X (GravityScale = 0) |
| 코요테 타임 | O | X (불필요) |
| 대시 | X축 방향 고정 | 입력 방향 자유 (8방향) |
| 방향 표현 | flipX 좌우만 | flipX 또는 Z축 회전 (토글) |
| InputAction 이동 타입 | `1DAxis` Composite | `2DVector` Composite |

### 스크립트별 상세 설명

#### PlayerTopViewDataSO.cs (수치 SO)

```
CreateAssetMenu: SEAL/Player/TopView Player Data
주요 수치:
  - MoveSpeed: 5f (이동 속도)
  - MoveAcceleration: 50f (가속도, 0=즉시)
  - MoveDeceleration: 80f (감속도)
  - NormalizeMovement: true (대각선 속도 정규화)
  - DashSpeed: 18f
  - DashDuration: 0.15f
  - DashCooldown: 0.6f
  - MaxDashCount: 1
  - DashInvincible: true
  - DashPunchScale: 0.2f (DOTween 스케일 펀치)
  - MoveSquashAmount: 0.08f (이동 스쿼시)
```

#### PlayerInputHandler.cs (입력 관리자)

```
싱글턴: PlayerInputHandler.Instance
ActionMap: InGame (코드 기반, .inputactions 파일 없음)

Action 구성:
  Move   : 2DVector Composite (WASD + 방향키 + 게임패드 좌스틱)
  Dash   : Button (LShift + 게임패드 East)
  Attack : Button (J키 + 게임패드 West)
  Seal   : Button (K키 + 게임패드 South)

키 바인딩:
  이동 Up    : W
  이동 Down  : S
  이동 Left  : A
  이동 Right : D
  대시       : LShift
  공격       : J  (A키는 이동에 사용하므로 분리)
  봉인       : K

이벤트:
  OnMove(Vector2)      : 이동 입력 변경 시 발행 (폴링 방식)
  OnDash               : 대시 버튼 눌림 시 1회
  OnAttack             : 공격 버튼 눌림 시 1회
  OnAttackReleased     : 공격 버튼 뗌 시 1회 (강공격 릴리즈)
  OnSeal               : 봉인 집행 버튼 눌림 시 1회

차단 API:
  BlockMove / UnblockMove
  BlockDash / UnblockDash
  BlockAction / UnblockAction
  BlockAll / UnblockAll
```

#### PlayerTopViewMover.cs (이동 핵심)

```
RequireComponent: Rigidbody2D, SpriteRenderer
Rigidbody2D 필수 설정:
  GravityScale = 0 (탑뷰, 중력 없음)
  Freeze Rotation Z = true

이동 처리:
  FixedUpdate 에서 Rigidbody2D.linearVelocity 직접 설정
  가속도 있으면 Vector2.MoveTowards 보간
  가속도 0이면 즉시 최고속도

대시 처리:
  코루틴(DashRoutine) 기반
  방향: 현재 입력 → 없으면 _lastMoveDirection
  대시 중 일반 이동 차단 (_isMoveLocked = true)
  쿨타임 코루틴(DashCooldownRoutine) 으로 충전 회복

DOTween 피드백:
  PlayDashPunch: 대시 시작 시 PunchScale
  PlayMoveSquash: 이동 방향 전환 시 Squash & Stretch

방향 표현:
  _rotateTowardsMoveDirection = false: flipX 좌우만
  _rotateTowardsMoveDirection = true : Z축 회전 (8방향)

이벤트:
  OnDashStarted / OnDashEnded
  OnFacingChanged(Vector2)

외부 API:
  SetMoveLocked(bool)    : 이동 잠금
  ForceStopDash()        : 대시 강제 중단
  RestoreAllDash()       : 대시 충전 즉시 회복 (어빌리티 연동)

프로퍼티:
  FacingDirection : Vector2 (현재 바라보는 방향)
  IsDashing       : bool
  IsMoving        : bool
  RemainingDashCount : int
```

### Unity 씬 설정 방법

```
1. PlayerRoot 하위에 Player 오브젝트 생성

2. Player 오브젝트 컴포넌트:
   - Rigidbody2D
     GravityScale = 0
     Freeze Rotation Z = true
     Collision Detection = Continuous
   - SpriteRenderer
   - PlayerTopViewMover (_data 연결 필수)

3. Systems 하위에 InputSystem 오브젝트 생성
   - PlayerInputHandler 컴포넌트 부착

4. Project 창에서 PlayerTopViewDataSO 에셋 생성:
   Create → SEAL/Player → TopView Player Data
   PlayerTopViewMover._data 에 연결

5. Hierarchy 구조:
   GameRoot
   ├─ Systems
   │  └─ InputSystem [PlayerInputHandler]
   └─ PlayerRoot
      └─ Player [Rigidbody2D, SpriteRenderer, PlayerTopViewMover]
         └─ Visual (선택 — _visualTransform 연결)
```

### 다음 작업 예정

```
[ ] PlayerStateMachine 구현 (Idle / Move / Dash / Attack / Seal / Hit / Dead)
[ ] IState 인터페이스 + StateMachine 클래스 작성
[ ] PlayerIdleState / PlayerMoveState 구현
[ ] PlayerDashState 구현 (현재 Mover 내 코루틴 → State 로 이관 검토)
[ ] 기본 공격(PlayerAttackState) 구현
[ ] 봉인 집행(PlayerSealExecutionState) 구현
[ ] 테스트용 적 더미 배치 후 충돌 확인
```

### 알려진 이슈 / 주의사항

```
1. KeyToPath() 함수
   Key 열거형 일부가 InputSystem 경로명과 다를 수 있음.
   테스트 중 바인딩 오류 발생 시 케이스별로 수동 경로 지정 필요.
   예: Key.LeftShift → "<Keyboard>/leftShift" (대소문자 확인 필요)

2. Rigidbody2D.linearVelocity
   Unity 6 에서 velocity → linearVelocity 로 변경됨.
   POC07 코드에 velocity 사용 부분 있으면 linearVelocity 로 교체 필요.

3. 이동 입력 방식 (폴링 vs 콜백)
   Move 입력은 콜백 대신 Update 폴링 방식 채택.
   performed/canceled 만으로는 연속 이동 처리 불안정.
   현재 PlayerInputHandler.Update() → ReadMoveInput() 매 프레임 발행.

4. 대시 방향
   입력 없을 때 _lastMoveDirection (마지막 이동 방향) 사용.
   캐릭터 생성 직후 _lastMoveDirection = Vector2.right (오른쪽 기본값).
```

---

*이 파일은 새 채팅 세션 시작 시 "봉인 업데이트 확인" 명령으로 정독합니다.*  
*변경사항 발생 시 "봉인 업데이트 요청" 명령으로 해당 버전 항목을 추가합니다.*

---

## v0.2 — A키 공격 시스템 구현

**날짜**: 세션 2  
**단계**: 핵심 메카닉 구현 (1단계)  
**작업**: 기본 공격 + 강공격 + DOTween 연출 + 히트스톱 + 봉인도 이벤트

### 구현 목표

SEAL_README 기준:
- A 탭 → 기본 공격 (최대 3콤보, 봉인도 누적)
- A 홀드 릴리즈 → 강공격 (높은 봉인도 누적, 더 큰 히트박스)
- 공격 방향 = 이동 방향 기준 (마우스 조준 없음)
- 적중 시 히트스톱 + OnHitTarget 이벤트 발행

### 생성 파일 목록

| 파일명 | 역할 | 위치 (POC08) |
|---|---|---|
| `PlayerAttackDataSO.cs` | 공격 수치 ScriptableObject | Assets/SEAL/Player/Data/ |
| `PlayerAttackController.cs` | 기본 공격 + 강공격 + DOTween 연출 | Assets/SEAL/Player/Attack/ |

### 스크립트별 상세 설명

#### PlayerAttackDataSO.cs (공격 수치 SO)

```
CreateAssetMenu: SEAL/Player/Player Attack Data

기본 공격 수치:
  BasicSealGaugeAmount : 10f  (기본 공격 봉인도 누적량)
  BasicHitboxDuration  : 0.1f (히트박스 활성 시간)
  BasicAttackDuration  : 0.35f (전체 모션 시간)
  MaxComboCount        : 3    (최대 콤보)
  ComboWindowStartRatio: 0.5  (콤보 윈도우 시작 비율)
  ComboSealMultipliers : [1.0, 1.2, 1.5] (콤보별 봉인도 배율)

강공격 수치:
  ChargeMinHoldTime      : 0.35f (최소 홀드 시간)
  ChargeSealGaugeAmount  : 30f  (강공격 봉인도 누적량)
  ChargeHitboxScale      : 1.5f (히트박스 크기 배율)
  ChargeHitStopDuration  : 0.1f (강공격 히트스톱)

히트스톱:
  HitStopDuration  : 0.05f (실시간 초)
  HitStopTimeScale : 0.02f (정지 중 TimeScale)

DOTween 연출:
  BackswingDuration : 0.07f  (백스윙 시간)
  SwingDuration     : 0.09f  (스윙 시간)
  RecoverDuration   : 0.15f  (복귀 시간)
  BackswingDistance : 0.3f   (백스윙 당기기 거리)
  SwingDistance     : 0.8f   (전방 스윙 거리)
  SwingPunchScale   : 0.3f   (타격 스케일 펀치)
  ChargePulseScale  : 0.15f  (강공격 홀드 맥동 강도)
  ChargePulsePeriod : 0.25f  (맥동 주기)
  AttackLungeDistance: 0.2f  (공격 시 플레이어 전진)
  AttackLungeDuration: 0.07f (전진 시간)
```

#### PlayerAttackController.cs (공격 핵심)

```
RequireComponent: PlayerMoveController

공격 흐름:
  A 탭 누름
  → HandleAttackPress()
  → !_isAttacking → BasicAttackRoutine() 코루틴 시작
    1. SetMoveLocked(true)
    2. PlayWeaponBackswing (DOTween — 공격 반대 방향 당김)
    3. PlayWeaponSwing + PlayPlayerLunge (DOTween — 전방 뻗기 + 플레이어 전진)
    4. CheckHit (OverlapCircleNonAlloc) → 적중 시 HitStopRoutine + OnHitTarget
    5. PlayWeaponRecover (DOTween — 원점 복귀)
    6. 콤보 윈도우 열기 → 다음 입력 대기
    7. 콤보 예약 있으면 다음 콤보 / 없으면 SetMoveLocked(false) + ComboResetTimer

A 홀드 릴리즈 (>= ChargeMinHoldTime)
  → HandleAttackRelease()
  → !_isAttacking → ChargeAttackRoutine()
    기본 공격과 동일 흐름이나 SwingDistance * ChargeSwingDistanceMultiplier
    히트박스 반경 * ChargeHitboxScale / 봉인도 ChargeSealGaugeAmount 적용

DOTween 연출:
  PlayWeaponBackswing : DOLocalMove (반대 방향으로 당김)
  PlayWeaponSwing     : DOLocalMove + DOPunchScale (전방 뻗기 + 타격 펀치)
  PlayWeaponRecover   : DOLocalMove + DOScale (원점 복귀)
  PlayPlayerLunge     : DOLocalMove (Visual 소량 전진 후 복귀)
  PlayChargePulse     : DOScale Yoyo 루프 (강공격 홀드 맥동)

히트스톱:
  HitStopRoutine 코루틴
  Time.timeScale = HitStopTimeScale
  WaitForSecondsRealtime(duration) → 복귀

이벤트:
  OnHitTarget(Vector2 hitPos, float sealAmount) : 적중 시 발행
  OnAttackStarted                                : 기본 공격 시작 시
  OnChargeAttackStarted                          : 강공격 시작 시

외부 API:
  IsAttacking  : bool
  CurrentCombo : int

히트박스 방식:
  OverlapCircleNonAlloc (GC 방지)
  중심: transform.position + attackDir * HitboxOffset
  반경: HitboxRadius (강공격: * ChargeHitboxScale)
```

### 공격 방향 결정 방식

```
GetAttackDirection()
  → PlayerMoveController.FacingDirection
  README: 공격 방향 = 이동 방향 기준 (마우스 조준 없음)
         입력 없으면 마지막 이동 방향 유지
```

### Unity 씬 적용 순서

```
1. Player 오브젝트에 PlayerAttackController 부착
2. PlayerAttackDataSO 에셋 생성
   → Create > SEAL/Player > Player Attack Data
3. PlayerAttackController._data 에 연결
4. _weaponTransform : Player 하위 WeaponPivot 또는 Weapon 오브젝트
5. _visualTransform : Player 하위 Visual 오브젝트 (없으면 자신)
6. PlayerAttackDataSO.HitLayer 에 Enemy 레이어 등록
```

### 봉인도 연동 예정

```
OnHitTarget(hitPos, sealAmount) 이벤트
→ 추후 SealGaugeSystem 이 구독
→ 대상 부위의 봉인도 += sealAmount
현재는 Debug.Log 로 적중 확인
```

### 다음 작업 예정

```
[ ] PlayerStateMachine 구현 (Idle / Move / Dash / Attack / Seal / Hit / Dead)
[ ] IState 인터페이스 + StateMachine 클래스 작성
[ ] 봉인 집행(S키) PlayerSealExecutor 구현
[ ] 테스트용 적 더미 (봉인도 수신 가능한 더미) 배치
[ ] SealGaugeSystem 구현 (OnHitTarget 수신 → 봉인도 관리)
```

---

*이 파일은 새 채팅 세션 시작 시 "봉인 업데이트 확인" 명령으로 정독합니다.*  
*변경사항 발생 시 "봉인 업데이트 요청" 명령으로 해당 버전 항목을 추가합니다.*