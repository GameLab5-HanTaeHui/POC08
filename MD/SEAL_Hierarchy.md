# SEAL_Hierarchy.md
# KEY 프로젝트 — Unity Hierarchy 계층 구조 문서

> 기준: Boss_Warden.prefab 실제 파싱 + 이번 세션 수정사항 반영
> 최종 갱신: 2026-06-07
> 표기 규칙:
>   `[컴포넌트명]` = 부착 컴포넌트
>   `← SO` = ScriptableObject 연결 필요
>   `← 자동` = GetComponent/FindObjectsByType 자동 탐색
>   `⚠️` = 미부착 / 수동 추가 필요
>   `v숫자` = 현재 스크립트 버전

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
├─ InputSystem          [PlayerInputHandler v1.4]  ← 싱글턴
├─ PoolManager          [PoolManager]
├─ EventBus             [EventBus]
└─ TimeController       [TimeController]
```

### InputSystem 키 바인딩 (v1.4)

```
[PlayerInputHandler v1.4]
  이동         : WASD
  대시         : Space
  공격(기본)   : 마우스 좌클릭
  공격(강)     : 마우스 좌클릭 홀드 후 릴리즈
  봉인 집행    : F키 OR 마우스 우클릭
                 → OnSeal 이벤트 발행 (pressed 1회)
                 → IsSealHeld 프로퍼티 (홀드 여부)
  상호작용     : E
  취소         : LeftShift
  메뉴         : Escape
```
> 현재 구현 상태: ✅ v1.4 완료

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
└─ Player                                   Layer: Player
   │  [Rigidbody2D]        GravityScale=0 / FreezeRotation Z
   │  [CapsuleCollider2D]
   │  [SpriteRenderer]
   │  [PlayerController]
   │  [PlayerMoveController]
   │  [PlayerAttackController]
   │  [PlayerAttackHitboxManager]
   │      OnHit(col, sealAmount) 발행
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

## BossRoot ✅ — Boss_Warden.prefab 실제 구조

### 레이어 정의

| Layer 번호 | 이름 | 용도 |
|---|---|---|
| 0 | Default | 패턴 오브젝트, AttackVisualRange 등 |
| 20 | EnemyAttackHitBox | Boss_WardenBody, Core, HurtBox |
| 21 | (Enemy) | LeftArm, RightArm 본체 |
| 22 | BossHitbox | LeftHurtBox, RightHurtBox, CoreHurtBox |

### Physics2D Collision Matrix

| | Player | BossHitbox (22) |
|:---:|:---:|:---:|
| PlayerAttack | ✅ | ❌ |
| EnemyAttackHitBox (20) | ✅ | ❌ |

---

### BossRoot 전체 계층 구조 (실제 프리팹 기준)

```
BossRoot                                            Layer: Default
└─ Boss_Warden                                      Layer: Default
   │
   │  ══ 봉인 시스템 — 범용 (Boss_Warden Root) ═══════════════
   │  [BossWardenCore v4.0]                ← 초기화 허브 / DataSO 단일 주입
   │      _data           → BossWardenDataSO ← SO (필수)
   │      _armL           → LeftArm/BossWardenArmPart
   │      _armR           → RightArm/BossWardenArmPart
   │      _coreObject     → Core ← Inspector 연결
   │
   │  [SealStateManager v2.0]              ← 상태 총괄 (Groggy 제거됨)
   │      상태: Idle → DilPhase → FinalSeal → Dead
   │      _coreObject     → Core ← ConnectCore() 주입
   │
   │  [SealGaugeManager]                   ← 봉인도 수치 조율
   │      (하위 SealableComponent 자동 수집)
   │      수집 결과: Part 2개 / Core 1개
   │
   │  [SealManager]                        ← 봉인 규칙 (그로기 필요 수: 2)
   │
   │  [SealEffectManager]                  ← 이펙트/UI 총괄
   │      _coreTransform  → Core Transform ← 자동
   │
   │  [SealExecutionEvent]                 ← 집행 가능 목록 관리
   │      (하위 SealReadyNotifier 자동 수집)
   │
   │  [SealExecutionRunner v2.0]           ← F키 pressed 즉시 집행
   │      PlayerInputHandler.OnSeal 구독
   │      GetBestTarget() → 즉시 ExecuteSeal()
   │      홀드 타이머 없음 / BlockAll 없음
   │
   │  ══ Warden 전용 ════════════════════════════════════════
   │  [BossWardenAI v3.0]                  ← 이동 / 패턴 실행
   │      _data           → BossWardenDataSO ← SO
   │      _armL / _armR   → BossWardenArmPart
   │      _patterns       → Patterns 하위 패턴 컴포넌트들
   │      경로: BossWardenCore 브리지 방식
   │            OnDilPhaseEnter / OnDilPhaseExit / OnPhaseChanged / OnDead
   │
   │  [BossWardenFeedback v4.0]            ← 색상 / 시각 피드백
   │      _data           → BossWardenDataSO ← SO
   │      _ai             → BossWardenAI ← 자동
   │      _bodyRenderer   → Boss_WardenBody/SpriteRenderer
   │      _armLPart / _armRPart → BossWardenArmPart
   │
   │  [BossWardenAttackRange]              ← 공격 범위 표시
   │      _data           → BossWardenDataSO ← SO
   │
   │  [BossWardenShockwave]                ← 충격파
   │      _data           → BossWardenDataSO ← SO
   │
   │  ══ 본체 비주얼 ══════════════════════════════════════
   ├─ Boss_WardenBody                              Layer: EnemyAttackHitBox (20)
   │     [SpriteRenderer]
   │
   │  ══ 팔 부위 ══════════════════════════════════════════
   ├─ LeftArm                                      Layer: 21 (Enemy)
   │  │  [BossWardenArmPart v3.0]
   │  │      _partType        = LeftArm
   │  │      _ownCollider     → LeftHurtBox/BoxCollider2D
   │  │      _data            → BossWardenDataSO ← Initialize() 주입
   │  │      _guardBreakPattern → null (LeftArm은 GuardBreak 미연결)
   │  │
   │  │  [SealableComponent]
   │  │      grade            = Part
   │  │      MaxGauge         = 200
   │  │      _isDilPhaseOnly  = false
   │  │
   │  │  ⚠️ [SealReadyNotifier]            ← 수동 추가 필요
   │  │      (SealableComponent 자동 탐색)
   │  │      → 없으면 SealExecutionEvent 집행 목록에 등록 안 됨
   │  │
   │  └─ LeftHurtBox                               Layer: BossHitbox (22)
   │        [BoxCollider2D]    isTrigger=false
   │
   ├─ RightArm                                     Layer: 21 (Enemy)
   │  │  [BossWardenArmPart v3.0]
   │  │      _partType        = RightArm
   │  │      _ownCollider     → RightHurtBox/BoxCollider2D
   │  │      _data            → BossWardenDataSO ← Initialize() 주입
   │  │      _guardBreakPattern → Patterns/GuardBreak/BossPattern_GuardBreak
   │  │
   │  │  [SealableComponent]
   │  │      grade            = Part
   │  │      MaxGauge         = 200
   │  │      _isDilPhaseOnly  = false
   │  │
   │  │  ⚠️ [SealReadyNotifier]            ← 수동 추가 필요
   │  │
   │  └─ RightHurtBox                              Layer: BossHitbox (22)
   │        [BoxCollider2D]    isTrigger=false
   │
   │  ══ 코어 ══════════════════════════════════════════════
   ├─ Core                                         Layer: EnemyAttackHitBox (20)
   │  │  SetActive = false (DilPhase 진입 시 true)
   │  │
   │  │  [SealableComponent]
   │  │      grade            = Core
   │  │      MaxGauge         = 500
   │  │      _isDilPhaseOnly  = true   ← DilPhase 중만 봉인도 누적
   │  │
   │  │  ⚠️ [SealReadyNotifier]            ← 수동 추가 필요
   │  │
   │  └─ CoreHurtBox                              Layer: BossHitbox (22)
   │        [BoxCollider2D]    isTrigger=true
   │
   │  ══ 충돌 박스 ══════════════════════════════════════════
   ├─ HurtBox                                      Layer: EnemyAttackHitBox (20)
   │     [BoxCollider2D]    isTrigger=false  Size=(1, 0.5)  Offset=(0, -0.25)
   │     localScale = (5, 7, 1)
   │
   │  ══ 공격 범위 시각화 ══════════════════════════════════
   ├─ AttackVisualRange                            Layer: Default
   │  ├─ ChargeLine          [SpriteRenderer]      돌진 예고선
   │  ├─ DiscSlam0           [SpriteRenderer]      Slam 예고 디스크 0
   │  ├─ DiscSlam1           [SpriteRenderer]      Slam 예고 디스크 1
   │  ├─ DiscSweep           [SpriteRenderer]      Sweep 예고 디스크
   │  ├─ DiscGuardBreak      [SpriteRenderer]      GuardBreak 예고 디스크
   │  ├─ ReageChargeLine0    [SpriteRenderer]      RageCharge 예고선 0
   │  ├─ ReageChargeLine1    [SpriteRenderer]      RageCharge 예고선 1
   │  ├─ ReageChargeLine2    [SpriteRenderer]      RageCharge 예고선 2
   │  ├─ SealRangeCircle     [LineRenderer]        Part 집행 가능 범위 원
   │  └─ CoreRangeCircle     [LineRenderer]        Core 집행 가능 범위 원
   │
   │  ══ 충격파 시각화 ══════════════════════════════════════
   ├─ ShockWaveDisc                                Layer: Default
   │     [SpriteRenderer]
   │
   │  ══ 패턴 오브젝트 ══════════════════════════════════════
   │  SetActive = true 항상 유지 (코루틴 중단 방지)
   └─ Patterns                                     Layer: Default
      ├─ Slam                [BossPattern_Slam v3.3]
      │     _linkedArmPart   → LeftArm/BossWardenArmPart
      │     _armLTransform   → LeftArm Transform
      │     _armLRenderer    → LeftArm SpriteRenderer
      │     _triggerGroggyOnRecovery = false
      │
      ├─ Sweep               [BossPattern_Sweep v3.2]
      │     _linkedArmPart   → LeftArm/BossWardenArmPart
      │     _armLTransform   → LeftArm Transform
      │     _armRTransform   → RightArm Transform
      │     _triggerGroggyOnRecovery = false
      │
      ├─ GuardBreak          [BossPattern_GuardBreak v3.2]
      │     _linkedArmPart   → RightArm/BossWardenArmPart
      │     _armRTransform   → RightArm Transform
      │     _armLTransform   → LeftArm Transform
      │     _triggerGroggyOnRecovery = true (Awake에서 강제 설정)
      │     ⚠️ HandlePatternGroggy() 현재 빈 함수 — 그로기 유발 경로 없음
      │
      ├─ RageCharge          [BossPattern_RageCharge v2.0]
      │     _isPhase2Only    = true   ← 2페이즈 전용
      │     _linkedArmPart   = null   ← 독립 패턴
      │     _triggerGroggyOnRecovery = false
      │
      └─ Charge              [BossPattern_Charge v2.3]
            _linkedArmPart   → RightArm/BossWardenArmPart
            _armRTransform   → RightArm Transform
            _wallLayer       → Wall 레이어 (Inspector 설정 필수)
            _triggerGroggyOnRecovery = false
```

---

### DataSO 에셋 연결 체계

```
BossWardenDataSO                    ← Inspector 연결은 BossWardenCore._data 하나만
  ├─ SealDataSO                     봉인도 수치 / 슬로우 / 저항
  └─ SealColorDataSO                봉인 색상 / DOTween / 파티클
```

BossWardenCore.Start() → InjectData() → 모든 하위 컴포넌트에 자동 주입

---

### 이번 세션 수정사항 반영 체크리스트

| 항목 | 내용 | 상태 |
|---|---|---|
| SealStateManager v2.0 | Groggy 제거 → DilPhase 직결 | ✅ 코드 완료 |
| IBossCore v2.0 | OnGroggyEnter/Exit 제거 | ✅ 코드 완료 |
| BossWardenCore v4.0 | Groggy 브리지 제거 | ✅ 코드 완료 |
| BossWardenAI v3.0 | OnGroggyEnter/Exit 제거 / CheckChaseTransition 가드 추가 | ✅ 코드 완료 |
| BossWardenFeedback v4.0 | OnGroggyEnter/Exit 제거 / OnDilPhaseEnter 통합 | ✅ 코드 완료 |
| SealExecutionRunner v2.0 | 홀드 타이머 제거 / F키 pressed 즉시 집행 | ✅ 코드 완료 |
| SealReadyNotifier | LeftArm / RightArm / Core 수동 추가 필요 | ⚠️ Prefab 미적용 |
| Warning 중 Chase 전환 차단 | _currentPattern != null 가드 추가 | ✅ 코드 완료 |
| DilPhase 중 패턴 선택 차단 | _isStopped 가드 추가 | ✅ 코드 완료 |

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
├─ Canvas_Gameplay
│  ├─ PlayerHUD
│  ├─ SealGaugeUI
│  ├─ CoreGaugeUI
│  └─ BossStatusUI
├─ Canvas_Menu
├─ Canvas_Reward
└─ Canvas_Minimap
```
> 현재 구현 상태: 미구현 (추후 개발)

---

## DebugRoot

```
DebugRoot
└─ Boss_Warden (임시 부착)
      [SealDebugTracker v1.0]     ← 버티컬 슬라이스 이후 제거 예정
            봉인도 수치 실시간 표시
            집행 가능 대상 추적
            F키 홀드 상태 감지
            ContextMenu 디버그 명령
```

---

## 하이어라키 설계 원칙

- 런타임 생성 오브젝트는 반드시 전용 Root 하위에 생성한다.
- Manager와 System은 구분한다.
- Player, Enemy, Boss는 서로 다른 Root에 둔다.
- UI는 UIRoot 하위에서만 관리한다.
- 디버그 오브젝트는 DebugRoot 하위에 둔다.
- Patterns 오브젝트는 SetActive=true 항상 유지 (코루틴 중단 방지).
- SealReadyNotifier 는 SealableComponent 와 같은 오브젝트에 반드시 부착.
- BossWardenCore._data 하나만 Inspector 연결 — 모든 컴포넌트는 InjectData() 로 수신.