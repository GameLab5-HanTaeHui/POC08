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