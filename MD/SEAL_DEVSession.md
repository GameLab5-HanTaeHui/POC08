# SEAL_DEVSession.md
# 봉인(KEY) 프로젝트 개발 세션 기록

> 작성 기준: 변경사항 발생 시 해당 버전에 기록  
> 파일 역할: 세션 간 개발 연속성 유지용 기록 문서  
> 최신 코드 기준: 프로젝트 파일 내 파싱 파일 (outputs 폴더 아님)

---

## 버전 기록 목차

- [v0.0] 프로젝트 초기화 / 지침 이해
- [v0.1] 2D 탑뷰 8방향 플레이어 이동 구현
- [v0.1.1] 파일명 명칭 변경
- [v0.2] A키 공격 시스템 구현
- [v0.3] PlayerInputHandler 키 바인딩 수정 + 보조 입력 추가
- [v0.4] 무기 스윙 연출 재설계 (PlayerWeaponSwingController)
- [v0.5] ObjectDirectionController + PlayerAttackHitboxManager 구현
- [v0.6] 레이어 구조 재설계 + BossPattern_Charge 버그 수정 (Awake 강제설정)
- [v0.7] Boss_Warden 전체 버그 수정 이력 동기화 (BOSS_DevPlan 기준)
- [v0.8] BossPattern_Charge v1.2→v1.3 + BossPattern_RageCharge v1.1 수정
- [v0.9] PlayerInputHandler v1.2 IsSealHeld 추가

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

**단계**: 핵심 메카닉 구현 (1단계)  
**작업**: POC08 탑뷰 이동 시스템 첫 구현

### 생성 파일 목록

| 파일명 | 역할 | 위치 (POC08) |
|---|---|---|
| `PlayerDataSO.cs` | 이동 수치 ScriptableObject | Assets/SEAL/Player/Data/ |
| `PlayerInputHandler.cs` | New Input System 입력 관리자 | Assets/SEAL/Player/Input/ |
| `PlayerMoveController.cs` | 탑뷰 8방향 이동 + 대시 핵심 컴포넌트 | Assets/SEAL/Player/Movement/ |

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

#### PlayerDataSO.cs (수치 SO)

```
CreateAssetMenu: SEAL/Player/Player Data
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

#### PlayerMoveController.cs (이동 핵심)

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

---

## v0.1.1 — 파일명 명칭 변경

**작업**: 파일명/클래스명 변경 (직접 수정)

| 변경 전 | 변경 후 |
|---|---|
| `PlayerTopViewDataSO.cs` | `PlayerDataSO.cs` |
| `PlayerTopViewMover.cs` | `PlayerMoveController.cs` |

내부 클래스명, Summary, CreateAssetMenu 명칭 모두 동일하게 변경.

---

## v0.2 — A키 공격 시스템 구현

**단계**: 핵심 메카닉 구현 (1단계)  
**작업**: 기본 공격 + 강공격 + DOTween 연출 + 히트스톱 + 봉인도 이벤트

### 생성 파일 목록

| 파일명 | 역할 | 위치 (POC08) |
|---|---|---|
| `PlayerAttackDataSO.cs` | 공격 수치 ScriptableObject | Assets/SEAL/Player/Data/ |
| `PlayerAttackController.cs` | 기본 공격 + 강공격 + DOTween 연출 | Assets/SEAL/Player/Attack/ |

### 구현 내용

```
A 탭      → 기본 공격 (최대 3콤보, 봉인도 누적)
A 홀드 릴리즈 → 강공격 (높은 봉인도, 큰 히트박스, 긴 히트스톱)
공격 방향 → PlayerMoveController.FacingDirection (마우스 없음)
적중 시   → 히트스톱 + OnHitTarget(hitPos, sealAmount) 발행
```

### DOTween 연출 흐름

```
① 백스윙  — 무기를 공격 반대 방향으로 당김  (OutQuad)
② 스윙    — 전방으로 뻗음 + 스케일 펀치     (OutCubic + PunchScale)
③ 전진    — Visual 소량 전진 후 복귀         (OutQuad → InOutSine)
④ 복귀    — 무기 원점으로 돌아옴             (InOutSine)
강공격 홀드 중 → 무기 맥동(Pulse) Yoyo 루프
```

### PlayerAttackDataSO.cs 주요 수치

```
BasicSealGaugeAmount   : 10f    (기본 공격 봉인도)
ChargeSealGaugeAmount  : 30f    (강공격 봉인도)
MaxComboCount          : 3      (최대 콤보)
ComboSealMultipliers   : [1.0, 1.2, 1.5]
HitboxRadius           : 1.0f
ChargeHitboxScale      : 1.5f
HitStopDuration        : 0.05f
ChargeHitStopDuration  : 0.1f
ChargeMinHoldTime      : 0.35f
SwingDistance          : 0.8f
ChargeSwingDistanceMultiplier : 1.8f
```

### 봉인도 연동 예정

```
OnHitTarget(hitPos, sealAmount)
→ 추후 SealGaugeSystem 구독 → 봉인도 처리
현재는 Debug.Log 로 적중 확인
```

---

## v0.3 — PlayerInputHandler 키 바인딩 수정 + 보조 입력 추가

**단계**: 핵심 메카닉 구현 (1단계)  
**작업**: SEAL_README 키 할당 기준 전면 수정 + 상호작용/취소/메뉴 추가

### 변경 파일

| 파일명 | 버전 | 변경 내용 |
|---|---|---|
| `PlayerInputHandler.cs` | v1.0 → v1.1 | 키 바인딩 수정 + 보조 입력 추가 |

### 키 바인딩 변경 내역

| 행동 | v1.0 (잘못된 값) | v1.1 (README 기준) |
|---|---|---|
| 이동 | WASD + 방향키 | **방향키 전용 (↑↓←→)** |
| 대시 | LShift | **Space** |
| 공격 | J | **A** |
| 봉인/코어 | K | **S** |
| 상호작용 | 없음 | **E (신규)** |
| 회피/취소 | 없음 | **Shift (신규)** |
| 메뉴 | 없음 | **Esc (신규)** |

### 추가된 이벤트

| 이벤트 | 차단 가능 | 설명 |
|---|:---:|---|
| `OnInteract` | O | E키 — 쉼터/상점/이벤트 상호작용 |
| `OnCancel` | X | Shift키 — 봉인 취소 / UI 뒤로가기. 항상 발행 |
| `OnMenu` | X | Esc키 — 일시정지 / 옵션. 항상 발행 |

### 차단 정책 정리

```
BlockMove   → OnMove 차단 (Vector2.zero 강제 발행)
BlockDash   → OnDash 차단
BlockAction → OnAttack / OnSeal / OnInteract 차단
              OnAttackReleased / OnCancel / OnMenu 는 항상 발행
BlockAll    → 위 세 가지 모두 차단 (OnCancel / OnMenu 는 여전히 발행)
```

### 알려진 이슈 / 주의사항

```
1. KeyToPath() 개선 (v1.1)
   런타임 Keyboard.current 컨트롤 순회로 1차 경로 탐색.
   실패 시 Digit 접두사 제거 + camelCase 폴백.
   POC07 InputManager.KeyToPath 와 동일 방식.

2. 이동 입력 방식
   Move 는 폴링 방식 (Update → ReadMoveInput()).
   performed/canceled 만으로는 연속 이동 처리 불안정.

3. 강공격 홀드 판정
   OnAttackReleased 는 차단 무관 항상 발행.
   PlayerAttackController 에서 홀드 시간 측정 후 강공격/일반 분기.
```

### 다음 작업 예정

```
[ ] PlayerStateMachine 구현 (Idle / Move / Dash / Attack / Seal / Hit / Dead)
[ ] IState 인터페이스 + StateMachine 클래스 작성
[ ] PlayerIdleState / PlayerMoveState / PlayerDashState 구현
[ ] 봉인 집행(S키) PlayerSealExecutor 구현
[ ] 테스트용 적 더미 배치 + SealGaugeSystem 연동
```

---

*이 파일은 새 채팅 세션 시작 시 "봉인 업데이트 확인" 명령으로 정독합니다.*  
*변경사항 발생 시 "봉인 업데이트 요청" 명령으로 해당 버전 항목을 추가합니다.*

---

## v0.4 — 무기 연출 전면 재설계 (DOTween 역동성 강화)

**단계**: 핵심 메카닉 구현 (1단계)  
**작업**: POC07 PlayerWeaponMover 구조 참고, 탑뷰 무기 스윙 연출 재설계

### 변경 원인

기존 PlayerAttackController 의 무기 연출:
- DOLocalMove 로 단순 위치 이동만 존재
- Z축 회전(DOLocalRotate) 없음 → 무기가 슬라이딩처럼 보임
- 콤보별 궤적 차이 없음
- POC07과 비교 시 역동성 완전 부재

### 변경 파일

| 파일명 | 버전 | 변경 내용 |
|---|---|---|
| `PlayerAttackDataSO.cs` | v1.0 → v2.0 | 콤보별 절대 위치 + Z회전값 추가 |
| `PlayerWeaponSwingController.cs` | 신규 | 무기 DOTween 연출 전담 컴포넌트 |
| `PlayerAttackController.cs` | v1.0 → v2.0 | SwingController 위임 구조로 재작성 |

### POC07 구조 계승 내용

| POC07 | POC08 탑뷰 버전 |
|---|---|
| facing=+1/-1 로 X좌표 반전 | WeaponPivot 을 공격 방향 각도로 Z회전 |
| Weapon.DOLocalMove (고정축) | Weapon.DOLocalMove (WeaponPivot 로컬 기준) |
| Weapon.DOLocalRotate (Z축) | Weapon.DOLocalRotate (Z축, 동일) |
| InsertCallback 으로 히트박스 타이밍 | Action 콜백으로 히트박스 타이밍 (동일 방식) |
| Combo3 후 히트스톱 | Combo3 / 강공격 후 히트스톱 (동일) |

### 핵심 구조 — WeaponPivot 방식

```
Player
└─ WeaponPivot  ← 공격 방향 각도로 즉시 Z회전
   └─ Weapon    ← DOLocalMove + DOLocalRotate
```

탑뷰에서 8방향 공격 방향을 각도로 변환:
```csharp
float angle = Mathf.Atan2(attackDir.y, attackDir.x) * Mathf.Rad2Deg;
weaponPivot.rotation = Quaternion.Euler(0f, 0f, angle);
```
이후 Weapon 은 WeaponPivot 로컬 기준 (공격방향=+X) 으로 이동하므로
POC07 의 횡스크롤 좌표계와 동일한 방식 적용 가능.

### 콤보별 연출 설계

| 콤보 | 궤적 | 타격 이즈 | 복귀 이즈 |
|---|---|---|---|
| Combo1 횡베기 | 측면 → 전방 수평 스윙 | InOutCubic | OutQuart |
| Combo2 내리찍기 | 위 → 전방 아래 수직 | InCubic | **OutBounce** (반동) |
| Combo3 찌르기 | 후방 → 전방 직선 + 진동 | OutExpo | **OutBack** (오버슈트) |
| 강공격 회전강타 | 크게 당겨 원호 스윙 | InOutQuart | **OutElastic** (탄성 여운) |

### PlayerAttackDataSO v2.0 주요 수치

```
콤보별 위치 (WeaponPivot 로컬, +X=공격방향):
  Combo1BackPos   : (-0.5,  0.6)  / Combo1AttackPos : (1.2, -0.3)
  Combo1RotBack   :  60°          / Combo1RotAtk    : -30°
  Combo2BackPos   : (-0.3,  0.9)  / Combo2AttackPos : (1.0, -0.6)
  Combo2RotBack   :  100°         / Combo2RotAtk    : -20°
  Combo3BackPos   : (-0.8,  0.0)  / Combo3AttackPos : (1.6,  0.0)
  ChargeBackPos   : (-1.0,  0.5)  / ChargeAttackPos : (1.5, -0.5)
  ChargeRotBack   :  150°         / ChargeRotAtk    : -90°

타이밍:
  BackswingDuration: 0.08f / AttackDuration: 0.08f / ReturnDuration: 0.15f

봉인도:
  Combo1: 10f / Combo2: 12f / Combo3: 18f / Charge: 30f
```

### 씬 설정 추가 사항

```
Player
└─ WeaponPivot  [PlayerWeaponSwingController._weaponPivot 연결]
   └─ Weapon    [PlayerWeaponSwingController._weapon 연결]
└─ Visual       [PlayerWeaponSwingController._visualTransform 연결]

Player 오브젝트에 추가 컴포넌트:
  PlayerWeaponSwingController (_data, _weaponPivot, _weapon, _visualTransform 연결)
```

### 다음 작업 예정

```
[ ] PlayerStateMachine 구현 (Idle / Move / Dash / Attack / Seal / Hit / Dead)
[ ] IState 인터페이스 + StateMachine 클래스 작성
[ ] 봉인 집행(S키) PlayerSealExecutor 구현
[ ] 테스트용 적 더미 배치 + SealGaugeSystem 연동
```

---

## v0.5 — ObjectDirectionController + PlayerAttackHitboxManager 구현

**단계**: 핵심 메카닉 구현 (1단계)  
**작업**: POC07 ObjectFlipController + PlayerWeaponHitboxManager 탑뷰 변환

### 생성 파일 목록

| 파일명 | 역할 | POC07 원본 |
|---|---|---|
| `ObjectDirectionController.cs` | 탑뷰 방향 동기화 컴포넌트 | `ObjectFlipController.cs` |
| `PlayerAttackHitboxManager.cs` | 탑뷰 무기 히트박스 관리 | `PlayerWeaponHitboxManager.cs` |

### POC07 → POC08 변환 핵심 차이

#### ObjectFlipController → ObjectDirectionController

| 항목 | POC07 (횡스크롤) | POC08 (탑뷰) |
|---|---|---|
| 방향 타입 | float +1/-1 (좌/우) | Vector2 8방향 |
| 이벤트 소스 | PlayerMover.OnFlipped(float) | **PlayerMoveController.OnFacingChanged(Vector2)** |
| localPosition.x 반전 | ✅ 핵심 기능 | ⚠️ 선택 (_syncTargets) |
| WeaponPivot 처리 | SyncOrigin(dir) 호출 | ❌ 불필요 (SwingController가 직접 Z회전 처리) |
| 스윙 취소 | ❌ 없음 | ✅ `_cancelSwingOnDirectionChange` 토글 |
| 상/하 방향 flipX | ❌ 없음 | ✅ X성분만 참조, 상/하는 변경 없음 |

```
flipX 결정 규칙:
  _invertFlipX = false (기본): X < 0 → flipX = true
  _invertFlipX = true        : X > 0 → flipX = true
  |X| < 0.01 (상/하 방향)   : 변경 없음
```

#### PlayerWeaponHitboxManager → PlayerAttackHitboxManager

| 항목 | POC07 (횡스크롤) | POC08 (탑뷰) |
|---|---|---|
| 히트박스 인덱스 | Combo1/2/3/AirAttack | **Combo1/2/3/Charge** (AirAttack 제거) |
| 레이어 분기 | Enemy + EnemyLock + EnemyShield | **Enemy 단일** |
| 피격 처리 | LockComponent/IDamageable 직접 호출 | **OnHit 이벤트만 발행** (처리는 외부) |
| 8방향 대응 | ❌ 횡스크롤 고정 | ✅ WeaponPivot Z회전으로 자동 대응 |
| 히트 중복 방지 | HashSet<Collider2D> | **동일 방식 계승** |

### 씬 적용 — PlayerAttackHitboxManager Hierarchy

```
Player
└─ WeaponPivot
   └─ Weapon
      ├─ HitboxCollider_Combo1   [BoxCollider2D] isTrigger=true
      ├─ HitboxCollider_Combo2   [BoxCollider2D] isTrigger=true
      ├─ HitboxCollider_Combo3   [BoxCollider2D] isTrigger=true
      └─ HitboxCollider_Charge   [BoxCollider2D] isTrigger=true (더 넓게)

Player 오브젝트:
  [PlayerAttackHitboxManager]
    _hitboxes[0] : HitboxCollider_Combo1
    _hitboxes[1] : HitboxCollider_Combo2
    _hitboxes[2] : HitboxCollider_Combo3
    _hitboxes[3] : HitboxCollider_Charge
    _enemyLayer  : Enemy 레이어 선택
```

### 씬 적용 — ObjectDirectionController

```
Player 오브젝트:
  [ObjectDirectionController]
    _sourceType          : PlayerMoveController
    _spriteRenderers[0]  : Visual 오브젝트의 SpriteRenderer
    _swingController     : PlayerWeaponSwingController (선택)
    _cancelSwingOnDirectionChange : false (기본)
    _syncTargets         : [] (WeaponPivot 연결 불필요)
```

---

## v0.6 — 레이어 구조 재설계 + BossPattern_Charge 버그 수정

**단계**: 핵심 메카닉 구현 (1단계) / STEP 09 진입  
**작업**: 레이어 구조 적용 + Charge 패턴 그로기 무한 발행 버그 수정

### 신규 레이어 구조 정의

| Layer | 역할 | 부착 오브젝트 |
|---|---|---|
| `PlayerAttack` | 플레이어 공격을 보내는 쪽 | PlayerWeapon (무기 히트박스) |
| `PlayerAttackHitBox` | 플레이어가 피격받는 공간 | Player HurtBox |
| `EnemyAttack` | 적 공격을 보내는 쪽 (OverlapXX 감지 기준) | 패턴 스크립트 _playerLayer 로 감지 |
| `EnemyAttackHitBox` | 적이 피격받는 공간 (봉인도 누적 대상) | LeftArm / RightArm / Core |

**Physics2D Collision Matrix:**

| | PlayerAttack | PlayerAttackHitBox | EnemyAttack | EnemyAttackHitBox |
|---|:---:|:---:|:---:|:---:|
| PlayerAttack | ❌ | ❌ | ❌ | ✅ |
| PlayerAttackHitBox | ❌ | ❌ | ✅ | ❌ |
| EnemyAttack | ❌ | ✅ | ❌ | ❌ |
| EnemyAttackHitBox | ✅ | ❌ | ❌ | ❌ |

### 변경 파일 목록

| 파일 | 버전 | 변경 내용 |
|---|---|---|
| `PlayerAttackHitboxManager.cs` | v1.0 → v1.1 | `_enemyLayer` → EnemyAttackHitBox 레이어 명시 |
| `BossPattern_Charge.cs` | v1.0 → v1.1 | 🔴 버그 수정 + _playerLayer → PlayerAttackHitBox 명시 |

### 🔴 BossPattern_Charge 버그 수정 상세

```
버그: Awake() 에서 _triggerGroggyOnRecovery = true 강제 설정
  → Inspector 직렬화값(Prefab: false) 을 Awake 에서 덮어씀
  → Charge Recovery 완료마다 OnPatternGroggy 발행
  → BossWardenCore.EnterGroggy() 무한 호출
  → Warden 상태 루프 충돌 → 고장

수정: Awake() 에서 _triggerGroggyOnRecovery = true; 코드 제거
  → Inspector / Prefab 직렬화값 그대로 사용
  → Prefab 에서 직접 설정 가능
```

### Prefab 레이어 변경 필요 항목 (Unity 에서 직접 수정)

**Player.prefab:**
```
PlayerWeapon (무기) → Layer: PlayerAttack
HurtBox            → Layer: PlayerAttackHitBox
```

**BossRoot_Warden.prefab:**
```
LeftArm   → Layer: EnemyAttackHitBox
RightArm  → Layer: EnemyAttackHitBox
Core      → Layer: EnemyAttackHitBox
HurtBox   → Layer: EnemyAttackHitBox
```

**Inspector 레이어마스크 변경:**
```
PlayerAttackHitboxManager._enemyLayer → EnemyAttackHitBox 선택
BossPattern_Charge._playerLayer       → PlayerAttackHitBox 선택
BossPattern_Slam._playerLayer         → PlayerAttackHitBox 선택
BossPattern_Sweep._playerLayer        → PlayerAttackHitBox 선택
BossPattern_GuardBreak._playerLayer   → PlayerAttackHitBox 선택
BossPattern_RageCharge._playerLayer   → PlayerAttackHitBox 선택
BossWardenShockwave._playerLayer      → PlayerAttackHitBox 선택
```

### 미수정 항목 (코드 변경 불필요 — Unity Inspector 에서만 수정)

```
BossPattern_Slam._playerLayer Tooltip    → "PlayerAttackHitBox 레이어 선택"
BossPattern_Sweep._playerLayer Tooltip   → "PlayerAttackHitBox 레이어 선택"  
BossPattern_GuardBreak._playerLayer Tooltip → "PlayerAttackHitBox 레이어 선택"
BossPattern_RageCharge._playerLayer Tooltip → "PlayerAttackHitBox 레이어 선택"

※ Tooltip 은 코드 힌트일 뿐, 실제 동작에 영향 없음
  Unity Inspector 에서 레이어마스크 값만 변경하면 됨
```

### 다음 작업 예정

```
[ ] STEP 09 — 전체 루프 통합 테스트
    ① 패턴 예고 범위 + 피격 확인
    ② 봉인도 누적 + 색상 단계 변화 확인
    ③ S키 봉인 집행 + 그로기 진입 확인
    ④ 코어 해제 + 딜 페이즈 확인
    ⑤ 충격파 + 2페이즈 전환 확인
    ⑥ 최종 봉인 + 처치 확인
```

---

## v0.7 — Boss_Warden 전체 버그 수정 이력 동기화

**단계**: 핵심 메카닉 구현 (1단계) / STEP 09 진입  
**작업**: BOSS_Warden_DevPlan 에 기록된 버그 수정 이력을 DEV 세션에 동기화  
**기준**: POC08 프로젝트 파일 파싱 결과 (outputs 폴더 아님)

### 프로젝트 파일 실제 버전 현황

| 파일 | 프로젝트 파일 버전 |
|---|---|
| `BossWardenSealExecutor.cs` | v1.1 |
| `BossWardenCore.cs` | v1.1 |
| `BossWardenAI.cs` | v1.2 |
| `SealGaugeComponent.cs` | v1.2 |
| `BossPatternBase.cs` | v1.2 |
| `BossPattern_Charge.cs` | v1.3 |
| `BossPattern_RageCharge.cs` | v1.1 |

### 전체 버그 수정 이력 (BOSS_DevPlan 기준)

| 번호 | 파일 | 버전 | 심각도 | 문제 | 수정 내용 |
|---|---|---|---|---|---|
| 🔴 버그1 | `BossWardenSealExecutor.cs` | v1.0→v1.1 | 크리티컬 | 람다 구독 → 해제 불가 + 중복 호출 | `Action` 필드에 캐싱 → `-=` 정상 해제 |
| 🔴 버그2 | `BossWardenSealExecutor.cs` | v1.0→v1.1 | 크리티컬 | `_holdTimer` 리셋 조건 역전 | 해당 블록 제거 — 코루틴에서만 관리 |
| 🔴 버그3 | `SealGaugeComponent.cs` | v1.1→v1.2 | 낮음 | 구버전 주석 잔존 | 주석을 현재 구현 기준으로 수정 |
| 🔴 버그4 | `BossWardenCore.cs` | v1.0→v1.1 | 높음 | Start 실행 순서 미보장 → FeedBack 구독 시점에 SealGauge 미초기화 | `[DefaultExecutionOrder(-10)]` + Initialize를 Awake로 이동 |
| 🔴 버그5 | `BossWardenAI.cs` | v1.0→v1.1 | 중간 | `TrySelectPattern()` 매 프레임 `new List<>` GC 압박 | `_availablePatterns` 멤버 변수 캐싱 → Clear 후 재사용 |
| 🔴 버그6 | `BossPattern_Charge.cs` | v1.0→v1.1 | 크리티컬 | `Awake()` 에서 `_triggerGroggyOnRecovery = true` 강제 설정 → Inspector 값 덮어씀 → Recovery마다 그로기 무한 발행 | 강제 설정 코드 제거 |
| 🔴 버그7 | `BossPattern_Charge.cs` | v1.1→v1.2 | 크리티컬 | `transform.position` 사용 → Patterns 자식 오브젝트 기준 → 거리 항상 0 → while(true) 무한루프 | `_rigid2D.position` 으로 완전 교체 |
| 🔴 버그8 | `BossPattern_RageCharge.cs` | v1.0→v1.1 | 크리티컬 | 동일 — `transform.position` → 무한루프 | `_rigid2D.position` 으로 완전 교체 |
| 🔴 버그9 | `BossWardenAI.cs` | v1.1→v1.2 | 크리티컬 | `ExecutePattern()` 에서 `_isStopped` 단독 체크 → Active 내부 무한루프 시 탈출 불가 | `_currentState` 이중 체크 추가 + 단계별 디버그 로그 추가 |
| 🔴 버그10 | `BossPatternBase.cs` | v1.1→v1.2 | 높음 | Warning/Active/Recovery 단계 전환 로그 없어 정지 위치 추적 불가 | 각 단계 진입/종료 + `_isInterrupted` 상세 로그 추가 |

---

## v0.8 — BossPattern_Charge v1.3 안전장치 + RageCharge v1.1 수정

**단계**: 핵심 메카닉 구현 (1단계) / STEP 09 진행 중  
**작업**: 돌진 패턴 while(true) 루프 탈출 보장 안전장치 추가

### BossPattern_Charge v1.2 → v1.3

v1.2에서 `_rigid2D.position` 교체 후에도 루프 미탈출 케이스 대응.  
벽/장애물 충돌 시 `linearVelocity` 가 물리 엔진에 의해 0이 되어도  
거리 조건이 충족되지 않으면 루프가 종료되지 않는 문제.

**추가된 안전장치 3종:**

```
① 타임아웃
   maxDuration = (chargeDistance / speed) × 1.5
   경과 시간 초과 시 강제 yield break

② 속도 감지 (벽 충돌 감지)
   elapsed > 0.1초 이후
   linearVelocity.magnitude < 0.5 → 벽 충돌로 판단 → 강제 yield break

③ 상세 로그
   Active 진입 시: 방향/속도/최대거리/타임아웃 출력
   30프레임마다: 현재 거리/속도/경과 시간 출력
   종료 시: 정상종료/속도0/타임아웃 구분 출력
```

**변경 파일:**

| 파일 | 버전 | 내용 |
|---|---|---|
| `BossPattern_Charge.cs` | v1.2 → v1.3 | 안전장치 3종 + 상세 로그 추가 |
| `BossPattern_RageCharge.cs` | v1.0 → v1.1 | `transform.position` → `_rigid2D.position` 3곳 교체 |

### transform.position 버그 근본 원인

```
구조: Boss_Warden (부모) → Patterns (자식) → BossPattern_Charge (컴포넌트)

문제:
  startPos = transform.position  ← Patterns 기준 월드 위치 저장
  dist = Distance(startPos, transform.position)
  Boss_Warden 이동 → Patterns도 함께 이동
  → startPos와 현재 위치가 항상 같음 → dist = 0 → 조건 미충족 → 무한루프

해결:
  _rigid2D = GetComponentInParent<Rigidbody2D>()  (Boss_Warden 본체)
  startPos = _rigid2D.position  ← 실제 월드 위치
  dist = Distance(startPos, _rigid2D.position)  → 실제 이동 거리 반영
```

---

## v0.9 — PlayerInputHandler v1.2 IsSealHeld 추가

**단계**: 핵심 메카닉 구현 (1단계) / STEP 09 진행 중  
**작업**: BossWardenSealExecutor 의 S키 홀드 폴링 지원

### 변경 파일

| 파일 | 버전 | 내용 |
|---|---|---|
| `PlayerInputHandler.cs` | v1.1 → v1.2 | `_isSealHeld` 변수 + `IsSealHeld` 프로퍼티 + 콜백 분리 |

### 변경 상세

```csharp
// 추가된 내부 상태
private bool _isSealHeld;

// 추가된 프로퍼티
public bool IsSealHeld => _isSealHeld;

// 콜백 분리 (기존 performed 만 → performed + canceled 분리)
_actionSeal.performed += _ => { _isSealHeld = true;  if (!_actionBlocked) OnSeal?.Invoke(); };
_actionSeal.canceled  += _ => { _isSealHeld = false; };
```

**`_isSealHeld` 는 `_actionBlocked` 차단과 무관하게 항상 갱신.**  
`OnSeal` (누른 순간 1회 트리거) 과 `IsSealHeld` (지속 상태 폴링) 는 용도가 다름.

### BossWardenSealExecutor 연동 방식

```csharp
// BossWardenSealExecutor.Update() 에서 매 프레임 폴링
if (PlayerInputHandler.Instance.IsSealHeld)
    _holdTimer += Time.unscaledDeltaTime;
else
    ResetHoldTimer();
```

---

## 현재 전체 스크립트 버전 현황 (v0.9 기준)

### 플레이어 시스템

| 파일 | 버전 | 비고 |
|---|---|---|
| `PlayerDataSO.cs` | v1.1 | 이동 수치 SO |
| `PlayerInputHandler.cs` | v1.2 | IsSealHeld 추가 |
| `PlayerMoveController.cs` | v1.1 | 8방향 이동 + 대시 |
| `PlayerAttackDataSO.cs` | v2.1 | Clockwise bool + CalcSwingDelta |
| `PlayerAttackController.cs` | v2.0 | 기본공격 + 강공격 + 콤보 |
| `PlayerWeaponSwingController.cs` | v1.1 | DOTween 스윙 + RotateWeaponDelta |
| `ObjectDirectionController.cs` | v1.0 | 방향 동기화 |
| `PlayerAttackHitboxManager.cs` | v1.1 | EnemyAttackHitBox 레이어 명시 |

### Boss_Warden 시스템

| 파일 | 버전 | 비고 |
|---|---|---|
| `BossWardenDataSO.cs` | v1.0 | 수치 SO |
| `SealGaugeComponent.cs` | v1.2 | 봉인도 누적 / 이벤트 |
| `BossPatternBase.cs` | v1.2 | 단계 로그 추가 |
| `BossWardenCore.cs` | v1.1 | DefaultExecutionOrder 추가 |
| `BossWardenAI.cs` | v1.2 | GC 최적화 + 이중 체크 |
| `BossWardenFeedback.cs` | v1.0 | 상태별 색상 연출 |
| `BossWardenArmPart.cs` | v1.1 | OnHit 구독 방식 |
| `BossWardenSealExecutor.cs` | v1.1 | 람다 캐싱 + holdTimer 수정 |
| `BossWardenCoreSealGauge.cs` | v1.0 | 코어 봉인도 |
| `BossWardenShockwave.cs` | v1.0 | 충격파 + 넉백 |
| `BossWardenAttackRange.cs` | v1.0 | 예고 범위 전담 |
| `BossPattern_Charge.cs` | v1.3 | 안전장치 3종 |
| `BossPattern_Slam.cs` | v1.0 | 내려치기 |
| `BossPattern_Sweep.cs` | v1.0 | 회전 스윕 |
| `BossPattern_GuardBreak.cs` | v1.0 | 가드브레이크 |
| `BossPattern_RageCharge.cs` | v1.1 | 3연 돌진 (_rigid2D 수정) |

### STEP 09 진행 현황

| 항목 | 상태 |
|---|---|
| 초기화 + 순환 패턴 동작 | 🟨 진행 중 (Charge 루프 버그 수정 완료) |
| 봉인도 누적 + 색상 단계 변화 | ⬜ |
| S키 봉인 집행 + 그로기 진입 | ⬜ |
| 코어 해제 + 딜 페이즈 | ⬜ |
| 충격파 + 2페이즈 전환 | ⬜ |
| 최종 봉인 + 처치 | ⬜ |