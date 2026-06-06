# SEAL_Hierarchy.md
# KEY 프로젝트 — Unity Hierarchy 계층 구조 문서

> 기준: SEAL_README #43 + POC08 봉인 시스템 리팩토링 반영  
> 갱신 기준: 새 오브젝트/컴포넌트 추가 시 반드시 업데이트  
> 표기 규칙: `[컴포넌트명]` = 부착 컴포넌트 / `← SO` = ScriptableObject 연결 필요 / `← 자동` = GetComponent 자동 탐색

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

```
Systems
├─ InputSystem          [PlayerInputHandler]   ← 싱글턴
├─ PoolManager          [PoolManager]
├─ EventBus             [EventBus]
└─ TimeController       [TimeController]
```

### InputSystem 상세

```
InputSystem
└─ [PlayerInputHandler]
      키 바인딩:
        _keyMoveUp / Down / Left / Right : 방향키
        _keyDash    : Space
        _keyAttack  : A
        _keySeal    : S        ← IsSealHeld 폴링 (봉인 집행용)
        _keyInteract: E
        _keyCancel  : LeftShift
        _keyMenu    : Escape
```
> 현재 구현 상태: ✅ PlayerInputHandler v1.2 완료

---

## CameraRoot

```
CameraRoot
├─ Main Camera          [Camera] [AudioListener]
└─ CinemachineCamera    [CinemachineCamera] [CinemachineFollow]
```
> 현재 구현 상태: 미구현 (추후 개발)

---

## PlayerRoot ✅

```
PlayerRoot
└─ Player                               Layer: Player
   │  [Rigidbody2D]   GravityScale=0 / FreezeRotation Z
   │  [CapsuleCollider2D]
   │  [SpriteRenderer]
   │  [PlayerController]
   │  [PlayerMoveController]
   │  [PlayerAttackController]
   │  [PlayerAttackHitboxManager]        ← OnHit(col, sealAmount) 발행
   │
   ├─ Visual
   ├─ HurtBox
   └─ WeaponPivot
         └─ Weapon
               └─ WeaponHitbox_1 / 2 / 3
```
> 현재 구현 상태: ✅ v0.1~v0.4 완료

---

## StageRoot

```
StageRoot
├─ Tilemap
├─ Walls
├─ Obstacles
└─ SpawnPoints
```
> 현재 구현 상태: 미구현 (추후 개발)

---

## EnemyRoot

```
EnemyRoot
├─ NormalEnemies
├─ EliteEnemies
└─ MiniBosses
```
> 현재 구현 상태: 미구현 (추후 개발)

---

## BossRoot ✅ (신규 봉인 시스템 반영)

### 레이어 정의

| Layer | 오브젝트 | 용도 |
|---|---|---|
| `Enemy` | Boss_Warden, LeftArm, RightArm, Core | 플레이어 공격 감지 |
| `Player` | Player 본체 | 보스 패턴 히트박스 감지 |
| `BossHitbox` | 패턴 HitboxCollider | 플레이어 피격 판정 |
| `Default` | 예고 범위 비주얼 | 충돌 불필요 |

**Physics2D Collision Matrix:**

| | Enemy | Player | BossHitbox |
|:---:|:---:|:---:|:---:|
| Enemy | ❌ | ❌ | ❌ |
| Player | ❌ | ❌ | ✅ |
| BossHitbox | ❌ | ✅ | ❌ |

---

### BossRoot 전체 계층 구조

```
BossRoot                                        Layer: Default
└─ Boss_Warden                                  Layer: Enemy
   │
   │  ══ 물리 / 렌더 ════════════════════════════════════════
   │  [Rigidbody2D]
   │      GravityScale = 0
   │      FreezeRotation Z = true
   │      CollisionDetection = Continuous
   │  [CapsuleCollider2D]   isTrigger=false  Size=(0.8, 1.2)
   │  [SpriteRenderer]      Sprite=Knob  Color=#888888
   │      SortingLayer = Enemy
   │
   │  ══ 봉인 시스템 — Root Layer (범용) ════════════════════
   │  [SealStateManager]                        ← 상태 총괄
   │      _bossData       → BossWardenDataSO ← SO
   │      _coreObject     → Core ← Inspector 연결
   │
   │  [SealGaugeManager]                        ← 봉인도 전체 조율
   │      _bossData       → BossWardenDataSO ← SO
   │      (하위 SealableComponent 자동 수집)
   │
   │  [SealManager]                             ← 봉인 규칙 정의
   │      _requiredSealCountForGroggy = 0 (자동=전체 Part 수)
   │
   │  [SealEffectManager]                       ← 이펙트/UI 총괄
   │      _coreTransform  → Core Transform ← Inspector 연결
   │      _shockwave      → BossWardenShockwave ← 자동
   │
   │  [SealExecutionEvent]                      ← 집행 목록 관리
   │      (하위 SealReadyNotifier 자동 수집)
   │
   │  [SealExecutionRunner]                     ← S키 홀드 집행 실행
   │      _bossData       → BossWardenDataSO ← SO
   │      _executionEvent → SealExecutionEvent ← 자동
   │
   │  ══ Warden 전용 ════════════════════════════════════════
   │  [BossWardenAI]
   │      _data           → BossWardenDataSO ← SO
   │
   │  [BossWardenFeedback]
   │      _data           → BossWardenDataSO ← SO
   │
   │  [BossWardenAttackRange]
   │      _data           → BossWardenDataSO ← SO
   │
   │  [BossWardenShockwave]
   │      _data           → BossWardenDataSO ← SO
   │
   │  [BossWardenCore]                          ← 총괄 초기화 / DataSO 주입
   │      _data           → BossWardenDataSO ← SO  (필수, 단일 연결 지점)
   │      _coreObject     → Core ← Inspector 연결
   │      _shockwave      → BossWardenShockwave ← 자동
   │
   │
   │  ══ 패턴 오브젝트 ════════════════════════════════════════
   ├─ Patterns                                  SetActive=true 항상 유지
   │  ├─ BossPattern_Charge    [BossPattern_Charge]
   │  ├─ BossPattern_Slam      [BossPattern_Slam]
   │  ├─ BossPattern_Sweep     [BossPattern_Sweep]
   │  ├─ BossPattern_GuardBreak[BossPattern_GuardBreak]
   │  └─ BossPattern_RageCharge[BossPattern_RageCharge]   ← _isPhase2Only=true
   │
   │  ══ 팔 부위 ═══════════════════════════════════════════
   ├─ LeftArm                                   Layer: Enemy
   │  │  [SpriteRenderer]      Sprite=Square  Color=#AAAAAA
   │  │  [CapsuleCollider2D]   isTrigger=false
   │  │
   │  │  ── 봉인 시스템 — Part Layer (범용) ──────────────
   │  │  [SealableComponent]
   │  │      grade          = Part
   │  │      _bossData      → BossWardenDataSO ← SO
   │  │      _sealRange     = 1.5
   │  │      _sealHoldTime  = 1.5
   │  │      _isDilPhaseOnly = false
   │  │
   │  │  [SealReadyNotifier]                    ← 집행 가능 신호 + 범위 원
   │  │      (SealableComponent 자동 탐색)
   │  │
   │  │  [SealExecutionEffect]                  ← Arc 게이지 + 완료/취소 연출
   │  │      (SealableComponent 자동 탐색)
   │  │
   │  │  ── Warden 전용 ─────────────────────────────────
   │  │  [BossWardenArmPart]
   │  │      _data          → BossWardenDataSO ← SO
   │  │
   │  └─ SealRangeCircle    [LineRenderer]      ← SealReadyNotifier 자동 생성 가능
   │     ExecutionArcGauge  [LineRenderer]      ← SealExecutionEffect 자동 생성 가능
   │
   ├─ RightArm                                  Layer: Enemy
   │  │  [SpriteRenderer]      Sprite=Square  Color=#AAAAAA
   │  │  [CapsuleCollider2D]   isTrigger=false
   │  │
   │  │  [SealableComponent]   grade=Part
   │  │  [SealReadyNotifier]
   │  │  [SealExecutionEffect]
   │  │  [BossWardenArmPart]
   │  │
   │  └─ SealRangeCircle    [LineRenderer]
   │     ExecutionArcGauge  [LineRenderer]
   │
   │  ══ 코어 ════════════════════════════════════════════
   └─ Core                                      Layer: Enemy
         SetActive = false (그로기 진입 시 true)
      │  [SpriteRenderer]      Sprite=Knob  Color=#FFE600
      │  [CircleCollider2D]    isTrigger=false
      │
      │  [SealableComponent]
      │      grade          = Core
      │      _bossData      → BossWardenDataSO ← SO
      │      _sealRange     = 1.5
      │      _sealHoldTime  = 2.0   (최종 봉인 홀드 시간)
      │      _isDilPhaseOnly = true  (딜페이즈 중만 봉인도 누적)
      │      _phaseTarget   = 250   (1페이즈: phase1CoreSealTarget)
      │
      │  [SealReadyNotifier]
      │  [SealExecutionEffect]
      │
      └─ CoreRangeCircle   [LineRenderer]      ← SealEffectManager 자동 생성 가능
```

---

### DataSO 에셋 연결 체계

```
BossWardenDataSO  ← Inspector 연결은 이 하나만 (BossWardenCore._data)
  ├─ SealDataSO         봉인도 수치 / 집행 / 슬로우 / 너프
  └─ SealColorDataSO    봉인 색상 / DOTween / 파티클
```

BossWardenCore.Initialize() 가 시작 시 모든 하위 컴포넌트에 주입.

---

### 제거된 구버전 컴포넌트 (신버전으로 대체)

| 구버전 (제거 대상) | 신버전 (대체) | 비고 |
|---|---|---|
| `SealGaugeComponent` | `SealableComponent` | 통합 컴포넌트로 대체 |
| `BossWardenCoreSealGauge` | `SealableComponent` (grade=Core) | 코어도 동일 컴포넌트 |
| `BossWardenSealExecutor` | `SealExecutionEvent` + `SealExecutionRunner` | 역할 분리 |
| `SealExecutor` (구버전) | `SealExecutionEvent` + `SealExecutionRunner` | 범용화 |

---

## AttackRangeVisuals (BossWardenAttackRange 자동 관리)

```
Boss_Warden
└─ AttackRangeVisuals                           Layer: Default
   ├─ ChargeLine        [LineRenderer]           Charge 예고선
   ├─ SlamDisc_0        [SpriteRenderer]         Slam 예고 원 1
   ├─ SlamDisc_1        [SpriteRenderer]         Slam 예고 원 2 (2페이즈)
   ├─ SweepDisc         [SpriteRenderer]         Sweep 예고 원
   ├─ GuardBreakDisc    [SpriteRenderer]         GuardBreak 예고 사각
   ├─ RageChargeLine_0  [LineRenderer]           RageCharge 예고선 1
   ├─ RageChargeLine_1  [LineRenderer]           RageCharge 예고선 2
   ├─ RageChargeLine_2  [LineRenderer]           RageCharge 예고선 3
   └─ (SealRange, CoreRange은 SealReadyNotifier / SealEffectManager 가 자동 생성)
```

---

## ProjectileRoot

```
ProjectileRoot
├─ PlayerProjectiles
├─ EnemyProjectiles
└─ BossProjectiles
```
> 현재 구현 상태: 미구현 (추후 개발)

---

## EffectRoot

```
EffectRoot
├─ HitEffects
├─ SealEffects
├─ CoreEffects
└─ ShockwaveEffects
```
> 현재 구현 상태: 미구현 (추후 개발)

---

## UIRoot

```
UIRoot
├─ Canvas_Gameplay      [Canvas] [CanvasScaler] [GraphicRaycaster]
│  ├─ PlayerHUD
│  │  ├─ HP_Bar
│  │  └─ DashCharge_Icons
│  ├─ SealGaugeUI           봉인도 게이지 (공격 중인 부위만 표시)
│  │                        ← SealEffectManager.OnPartGaugeChanged 구독
│  ├─ CoreGaugeUI           코어 봉인도 게이지 (딜 페이즈 중만 표시)
│  │                        ← SealEffectManager.OnCoreGaugeChanged 구독
│  └─ BossStatusUI
│     ├─ BossPhaseIndicator ← SealEffectManager.OnPhaseUIChanged 구독
│     ├─ PartStatusIcons
│     └─ GroggyIndicator
│
├─ Canvas_Menu
├─ Canvas_Reward
└─ Canvas_Minimap
```
> 현재 구현 상태: 미구현 (추후 개발)

---

## DebugRoot

```
DebugRoot
├─ DebugText            [TextMeshPro]
├─ StateViewer          현재 SealBossState 표시
├─ HitBoxViewer
└─ SealGaugeViewer      봉인도 수치 표시
```
> 현재 구현 상태: 미구현 (추후 개발)

---

## 현재 구현 상태 요약

| Root | 구현 상태 | 비고 |
|---|:---:|---|
| Managers | ❌ | 추후 개발 |
| Systems / InputSystem | ✅ | PlayerInputHandler v1.2 |
| CameraRoot | ❌ | 추후 개발 |
| PlayerRoot | ✅ | v0.1~v0.4 완료 |
| StageRoot | ❌ | 추후 개발 |
| EnemyRoot | ❌ | 추후 개발 |
| BossRoot | 🟨 | 봉인 시스템 리팩토링 완료, 유니티 조립 필요 |
| ProjectileRoot | ❌ | 추후 개발 |
| EffectRoot | ❌ | 추후 개발 |
| UIRoot | ❌ | 추후 개발 |
| DebugRoot | ❌ | 추후 개발 |

---

## 하이어라키 설계 원칙

- 런타임 생성 오브젝트는 반드시 전용 Root 하위에 생성한다.
- Manager 와 System 은 구분한다.
- Player / Enemy / Boss 는 서로 다른 Root 에 둔다.
- UI 는 UIRoot 하위에서만 관리한다.
- 디버그 오브젝트는 DebugRoot 하위에 둔다.
- 씬에서 오브젝트를 찾기 쉽게 이름을 명확하게 작성한다.
- 봉인 시스템 범용 컴포넌트(SealableComponent 등)는 Warden 전용 컴포넌트보다 먼저 부착한다.

---

*이 파일은 새 오브젝트/컴포넌트 추가 시 반드시 업데이트합니다.*  
*SEAL_DEVSession 의 "봉인 업데이트 요청" 과 함께 갱신합니다.*