# SEAL_Hierarchy.md
# KEY 프로젝트 — Unity Hierarchy 계층 구조 문서

> 기준: SEAL_README #43 Unity Hierarchy 구조 + POC08 현재 구현 반영  
> 갱신 기준: 새 오브젝트/컴포넌트 추가 시 반드시 업데이트  
> 표기 규칙: `[컴포넌트명]` = 해당 오브젝트에 부착된 컴포넌트 / `← SO` = ScriptableObject 연결 필요

---

## 전체 씬 구조

```
GameRoot
├─ Managers
├─ Systems
├─ CameraRoot
├─ PlayerRoot
├─ StageRoot
├─ EnemyRoot
├─ BossRoot
├─ ProjectileRoot
├─ EffectRoot
├─ UIRoot
└─ DebugRoot
```

---

## Managers

전역 관리자 오브젝트를 모아둔다.  
씬 전환 후에도 유지되는 싱글턴 Manager 컴포넌트를 부착.

```
Managers
├─ GameManager          [GameManager]
├─ DungeonManager       [DungeonManager]
├─ StageManager         [StageManager]
├─ BattleManager        [BattleManager]
├─ PlayerManager        [PlayerManager]
├─ EnemyManager         [EnemyManager]
├─ BossManager          [BossManager]
├─ UIManager            [UIManager]
├─ AudioManager         [AudioManager]
└─ SaveManager          [SaveManager]
```

> 현재 구현 상태: 미구현 (추후 개발)

---

## Systems

런타임 시스템 오브젝트를 모아둔다.  
Manager 와 달리 씬 내 시스템 컴포넌트.

```
Systems
├─ InputSystem          [PlayerInputHandler]   ← 싱글턴, 키 바인딩 SO 없음 (코드 기반)
├─ PoolManager          [PoolManager]
├─ EventBus             [EventBus]
└─ TimeController       [TimeController]
```

### InputSystem 상세

```
InputSystem
└─ [PlayerInputHandler]
      키 바인딩 (Inspector):
        _keyMoveUp    : UpArrow
        _keyMoveDown  : DownArrow
        _keyMoveLeft  : LeftArrow
        _keyMoveRight : RightArrow
        _keyDash      : Space
        _keyAttack    : A
        _keySeal      : S
        _keyInteract  : E
        _keyCancel    : LeftShift
        _keyMenu      : Escape
      이벤트:
        OnMove / OnDash / OnAttack / OnAttackReleased
        OnSeal / OnInteract / OnCancel / OnMenu
```

> 현재 구현 상태: PlayerInputHandler ✅ 구현 완료 (v1.1)

---

## CameraRoot

카메라 관련 오브젝트를 모아둔다.

```
CameraRoot
├─ Main Camera          [Camera] [AudioListener]
└─ CinemachineCamera    [CinemachineCamera] [CinemachineFollow]
                         ← Player.Transform 타겟 연결 필요
```

> 현재 구현 상태: 미구현 (추후 개발)

---

## PlayerRoot ✅ 핵심 구현 영역

플레이어 관련 오브젝트를 모아둔다.  
현재 v0.1 ~ v0.4 구현 내용이 반영된 구조.

```
PlayerRoot
└─ Player
   │  [Rigidbody2D]          GravityScale=0 / FreezeRotation Z=true / Continuous
   │  [SpriteRenderer]
   │  [CapsuleCollider2D]    플레이어 본체 충돌
   │  [PlayerMoveController] ← PlayerDataSO 연결
   │  [PlayerAttackController]  ← PlayerAttackDataSO 연결
   │  [PlayerWeaponSwingController] ← PlayerAttackDataSO 연결
   │  [ObjectDirectionController]   ← Visual SpriteRenderer 연결
   │  [PlayerAttackHitboxManager]   ← Enemy 레이어 + 히트박스 콜라이더
   │
   ├─ Visual                 [SpriteRenderer] ← 스쿼시/스트레치 DOTween 대상
   │                          _visualTransform 참조 오브젝트
   │
   ├─ HitBox                 [Collider2D] isTrigger=true  ← 적 공격 피격 수신
   │
   ├─ HurtBox                [Collider2D] isTrigger=true  ← 적 공격 판정 감지
   │
   ├─ WeaponPivot            공격 방향으로 Z회전하는 피벗
   │  │  (컴포넌트 없음 — PlayerWeaponSwingController 가 제어)
   │  │  _weaponPivot 참조 오브젝트
   │  │
   │  └─ Weapon              실제 무기 스프라이트 오브젝트
   │        [SpriteRenderer]
   │        (DOLocalMove + DOLocalRotate 연출 대상)
   │        _weapon 참조 오브젝트
   │
   └─ StateMachine           (추후 StateMachine 컴포넌트 부착 예정)
```

### Player 오브젝트 컴포넌트 연결 상세

| 컴포넌트 | 연결 필드 | 연결 대상 |
|---|---|---|
| `PlayerMoveController` | `_data` | `PlayerDataSO` 에셋 |
| `PlayerMoveController` | `_visualTransform` | `Visual` 오브젝트 |
| `PlayerAttackController` | `_data` | `PlayerAttackDataSO` 에셋 |
| `PlayerAttackController` | `_visualTransform` | `Visual` 오브젝트 |
| `PlayerWeaponSwingController` | `_data` | `PlayerAttackDataSO` 에셋 |
| `PlayerWeaponSwingController` | `_weaponPivot` | `WeaponPivot` 오브젝트 |
| `PlayerWeaponSwingController` | `_weapon` | `Weapon` 오브젝트 |
| `PlayerWeaponSwingController` | `_visualTransform` | `Visual` 오브젝트 |
| `ObjectDirectionController` | `_spriteRenderers` | `Visual` SpriteRenderer |
| `ObjectDirectionController` | `_swingController` | `PlayerWeaponSwingController` (선택) |
| `PlayerAttackHitboxManager` | `_hitboxes` | Weapon 하위 4개 Collider2D |
| `PlayerAttackHitboxManager` | `_enemyLayer` | Enemy 레이어 마스크 |

### PlayerDataSO 에셋 주요 수치

| 항목 | 값 | 설명 |
|---|---|---|
| MoveSpeed | 5f | 이동 속도 |
| MoveAcceleration | 50f | 가속도 (0=즉시) |
| MoveDeceleration | 80f | 감속도 |
| NormalizeMovement | true | 대각선 정규화 |
| DashSpeed | 18f | 대시 속도 |
| DashDuration | 0.15f | 대시 지속 시간 |
| DashCooldown | 0.6f | 대시 쿨타임 |
| MaxDashCount | 1 | 최대 대시 충전 |
| DashPunchScale | 0.2f | 대시 스케일 펀치 |
| MoveSquashAmount | 0.08f | 이동 스쿼시 |

### PlayerAttackDataSO 에셋 주요 수치

| 항목 | 값 | 설명 |
|---|---|---|
| BackswingDuration | 0.08f | 백스윙 시간 |
| AttackDuration | 0.08f | 타격 시간 |
| ReturnDuration | 0.15f | 복귀 시간 |
| Combo1SealAmount | 10f | 1콤보 봉인도 |
| Combo2SealAmount | 12f | 2콤보 봉인도 |
| Combo3SealAmount | 18f | 3콤보 봉인도 (피니셔) |
| ChargeSealAmount | 30f | 강공격 봉인도 |
| HitboxRadius | 0.8f | 히트박스 반경 |
| HitboxOffset | 1.0f | 히트박스 오프셋 |
| HitStopDuration | 0.05f | 기본 히트스톱 |
| ChargeHitStopDuration | 0.12f | 강공격 히트스톱 |
| ChargeMinHoldTime | 0.35f | 강공격 최소 홀드 |

### Rigidbody2D 필수 설정

| 항목 | 값 |
|---|---|
| Gravity Scale | **0** (탑뷰 — 중력 없음) |
| Freeze Rotation | **Z 체크** (물리 회전 방지) |
| Collision Detection | **Continuous** |
| Interpolate | Interpolate |

> 현재 구현 상태:
> - PlayerInputHandler ✅ v1.1
> - PlayerDataSO ✅ v1.1
> - PlayerMoveController ✅ v1.1
> - PlayerAttackDataSO ✅ v2.0
> - PlayerAttackController ✅ v2.0
> - PlayerWeaponSwingController ✅ v1.0

---

## StageRoot

현재 스테이지 구조물을 모아둔다.

```
StageRoot
├─ Tilemap              [Tilemap] [TilemapRenderer] [TilemapCollider2D]
├─ Walls                벽 콜라이더 오브젝트 모음
├─ Obstacles            장애물 오브젝트 모음
├─ SpawnPoints          적 스폰 포인트 모음
├─ NodeEntrance         노드 진입 포인트
└─ NodeExit             노드 탈출 포인트
```

> 현재 구현 상태: 미구현 (추후 개발)

---

## EnemyRoot

일반 몬스터, 엘리트, 미니보스를 런타임에 생성하여 모아둔다.

```
EnemyRoot
├─ NormalEnemies        일반 몬스터 런타임 생성 부모
├─ EliteEnemies         엘리트 런타임 생성 부모
└─ MiniBosses           미니보스 런타임 생성 부모
```

### 일반 몬스터 Prefab 예시 구조 (추후)

```
Enemy_Normal
│  [Rigidbody2D]
│  [SpriteRenderer]
│  [CapsuleCollider2D]
│  [EnemyBase]        ← EnemyDataSO 연결
│  [EnemyAI]
│
├─ Visual             [SpriteRenderer]
├─ HitBox             [Collider2D] isTrigger=true
└─ SealOverlay        [SpriteRenderer]  봉인 오버레이 아이콘
```

> 현재 구현 상태: 미구현 (추후 개발)

---

## BossRoot

보스 전투 전용 오브젝트를 모아둔다.

```
BossRoot
└─ Boss
   │  [Rigidbody2D]
   │  [SpriteRenderer]
   │  [EnemyBossBase]     ← BossDataSO 연결
   │  [BossAI]
   │
   ├─ Visual
   ├─ Parts
   │  ├─ HeadPart         [BossPartComponent] ← 봉인 가능 부위
   │  ├─ LeftArmPart      [BossPartComponent]
   │  ├─ RightArmPart     [BossPartComponent]
   │  └─ LegPart          [BossPartComponent]
   │
   ├─ Core                [CoreSealTarget]  그로기 시 활성화
   ├─ PatternPoints       패턴 기준점 오브젝트 모음
   └─ StateMachine        (보스 StateMachine 컴포넌트)
```

> 현재 구현 상태: 미구현 (추후 개발)

---

## ProjectileRoot

투사체와 탄막을 런타임 풀링으로 생성한다.

```
ProjectileRoot
├─ PlayerProjectiles    플레이어 투사체 풀
├─ EnemyProjectiles     적 투사체 풀
└─ BossProjectiles      보스 탄막 풀
```

> 현재 구현 상태: 미구현 (추후 개발)

---

## EffectRoot

이펙트 오브젝트를 풀링으로 관리한다.

```
EffectRoot
├─ HitEffects           타격 이펙트 풀
├─ SealEffects          봉인 이펙트 풀 (봉인 파편, 자물쇠 생성)
├─ CoreEffects          코어 이펙트 풀
└─ ShockwaveEffects     충격파 이펙트 풀
```

> 현재 구현 상태: 미구현 (추후 개발)

---

## UIRoot

UI 오브젝트를 모아둔다. Canvas 는 Screen Space - Overlay 사용.

```
UIRoot
├─ Canvas_Gameplay      [Canvas] [CanvasScaler] [GraphicRaycaster]
│  ├─ PlayerHUD
│  │  ├─ HP_Bar
│  │  └─ DashCharge_Icons
│  ├─ SealGaugeUI       봉인도 게이지 (공격 중인 부위만 표시)
│  ├─ CoreGaugeUI       코어 봉인도 게이지 (딜 페이즈 중만 표시)
│  └─ BossStatusUI
│     ├─ BossPhaseIndicator
│     ├─ PartStatusIcons
│     └─ GroggyIndicator
│
├─ Canvas_Menu          일시정지 / 옵션
├─ Canvas_Reward        보상 선택 화면
└─ Canvas_Minimap       미니맵 / 노드 선택 화면
```

> 현재 구현 상태: 미구현 (추후 개발)

---

## DebugRoot

개발 중 디버그 표시. 빌드 시 비활성화.

```
DebugRoot
├─ DebugText            [TextMeshPro]  상태 텍스트 표시
├─ StateViewer          현재 StateMachine 상태 표시
├─ HitBoxViewer         히트박스 시각화
└─ SealGaugeViewer      봉인도 수치 표시
```

> 현재 구현 상태: 미구현 (추후 개발)

---

## 하이어라키 설계 원칙 (SEAL_README #43)

- 런타임 생성 오브젝트는 반드시 전용 Root 하위에 생성한다.
- Manager 와 System 은 구분한다.
- Player / Enemy / Boss 는 서로 다른 Root 에 둔다.
- UI 는 UIRoot 하위에서만 관리한다.
- 디버그 오브젝트는 DebugRoot 하위에 둔다.
- 씬에서 오브젝트를 찾기 쉽게 이름을 명확하게 작성한다.

---

## 현재 구현 상태 요약 (v0.4 기준)

| Root | 구현 상태 | 비고 |
|---|:---:|---|
| Managers | ❌ | 추후 개발 |
| Systems / InputSystem | ✅ | PlayerInputHandler v1.1 |
| CameraRoot | ❌ | 추후 개발 |
| PlayerRoot | ✅ | v0.1~v0.4 구현 완료 |
| StageRoot | ❌ | 추후 개발 |
| EnemyRoot | ❌ | 추후 개발 |
| BossRoot | ❌ | 추후 개발 |
| ProjectileRoot | ❌ | 추후 개발 |
| EffectRoot | ❌ | 추후 개발 |
| UIRoot | ❌ | 추후 개발 |
| DebugRoot | ❌ | 추후 개발 |

---

*이 파일은 새 오브젝트/컴포넌트 추가 시 반드시 업데이트합니다.*  
*SEAL_DEVSession 의 "봉인 업데이트 요청" 과 함께 갱신합니다.*