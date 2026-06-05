# SEAL_Hierarchy.md — BossRoot 섹션 최신화
## 기준: 2026-06-05 / Boss_Warden.prefab 최신 구조 반영

---

## BossRoot

```
BossRoot                                    Layer: Default
└─ Boss_Warden                              Layer: Enemy
   │  localScale = (1, 1, 1)               ← 반드시 균등 Scale 유지
   │                                           SetParent worldPositionStays 시
   │                                           비균등 Scale → 팔 Scale 왜곡 누적
   │
   │  ── 컴포넌트 ─────────────────────────────────────────────
   │  [Rigidbody2D]
   │      BodyType         = Dynamic
   │      GravityScale     = 0
   │      FreezeRotation Z = true
   │      CollisionDetection = Continuous
   │
   │  [CapsuleCollider2D]                   isTrigger = false
   │
   │  [SpriteRenderer]                      SortingLayer = Enemy
   │
   │  [BossWardenCore]                      v2.0
   │      _data            → BossWardenDataSO 에셋
   │      _armL            → LeftArm / BossWardenArmPart
   │      _armR            → RightArm / BossWardenArmPart
   │      _armLSealable    → LeftArm / SealableComponent       ← 신규 v2.0
   │      _armRSealable    → RightArm / SealableComponent      ← 신규 v2.0
   │      _coreSealable    → Core / SealableComponent          ← 신규 v2.0
   │      _sealExecutor    → Boss_Warden / SealExecutor        ← 신규 v2.0
   │      _coreObject      → Core GameObject
   │      _shockwave       → BossWardenShockwave
   │
   │  [BossWardenAI]
   │      _data            → BossWardenDataSO 에셋
   │      _patterns        → 각 BossPattern 컴포넌트 리스트
   │
   │  [BossWardenFeedback]                  v2.0  (1개만 부착)
   │      _data            → BossWardenDataSO 에셋
   │      _armLPart        → LeftArm / BossWardenArmPart
   │      _armRPart        → RightArm / BossWardenArmPart
   │      _bodyRenderer    → Boss_Warden / SpriteRenderer
   │      _armLRenderer    → LeftArm / SpriteRenderer
   │      _armRRenderer    → RightArm / SpriteRenderer
   │      _coreRenderer    → Core / SpriteRenderer
   │
   │  [BossWardenAttackRange]
   │  [BossWardenShockwave]
   │
   │  [SealExecutor]                        v1.1   ← 신규 (BossWardenSealExecutor 대체)
   │      _data            → BossWardenDataSO 에셋
   │      _attackRange     → BossWardenAttackRange (미연결 시 자동 탐색)
   │
   ├─ Boss_WardenBody                       localScale = (5, 7, 1)
   │    [SpriteRenderer]                    보스 본체 시각
   │
   ├─ LeftArm                               localScale = (2, 6, 1)
   │  │  [SpriteRenderer]
   │  │  [CapsuleCollider2D]               Layer = Enemy / isTrigger = false
   │  │
   │  │  [BossWardenArmPart]               v2.1
   │  │      _partType         = LeftArm
   │  │      _ownCollider      → LeftHurtBox / CapsuleCollider2D
   │  │      _spriteRenderer   → LeftArm / SpriteRenderer
   │  │      _guardBreakPattern = null      ← LeftArm은 null
   │  │
   │  │  [SealableComponent]               신규 (SealGaugeComponent 대체)
   │  │      grade             = Part
   │  │      _maxGauge         = 200
   │  │      _sealRange        = 1.5
   │  │      _sealHoldTime     = 1.5
   │  │      _isDilPhaseOnly   = false
   │  │
   │  └─ LeftHurtBox
   │       [CapsuleCollider2D]             Layer = EnemyAttackHitBox
   │                                        isTrigger = true
   │
   ├─ RightArm                              localScale = (2, 6, 1)
   │  │  [SpriteRenderer]
   │  │  [CapsuleCollider2D]               Layer = Enemy / isTrigger = false
   │  │
   │  │  [BossWardenArmPart]               v2.1
   │  │      _partType         = RightArm
   │  │      _ownCollider      → RightHurtBox / CapsuleCollider2D
   │  │      _spriteRenderer   → RightArm / SpriteRenderer
   │  │      _guardBreakPattern → Patterns/BossPattern_GuardBreak ← 신규 v2.1
   │  │
   │  │  [SealableComponent]               신규 (SealGaugeComponent 대체)
   │  │      grade             = Part
   │  │      _maxGauge         = 200
   │  │      _sealRange        = 1.5
   │  │      _sealHoldTime     = 1.5
   │  │      _isDilPhaseOnly   = false
   │  │
   │  └─ RightHurtBox
   │       [CapsuleCollider2D]             Layer = EnemyAttackHitBox
   │                                        isTrigger = true
   │
   ├─ Core                                  localScale = (2, 2, 1)
   │  │  [SpriteRenderer]
   │  │  [CapsuleCollider2D]               Layer = EnemyAttackHitBox
   │  │                                     isTrigger = true
   │  │  SetActive = false (기본)           BossWardenCore.EnterGroggy() 에서 활성
   │  │
   │  │  [SealableComponent]               신규 (BossWardenCoreSealGauge 대체)
   │  │      grade             = Core
   │  │      _maxGauge         = 500
   │  │      _sealRange        = DataSO.coreUnlockRange
   │  │      _sealHoldTime     = DataSO.coreUnlockHoldTime
   │  │      _isDilPhaseOnly   = true      ← 딜페이즈에서만 봉인도 누적
   │  │      _phaseTarget      = DataSO.phase1CoreSealTarget
   │  │
   │  └─ CoreHurtBox
   │
   ├─ HurtBox                               localScale = (5, 7, 1)
   │    [CapsuleCollider2D]                isTrigger = false
   │
   ├─ Patterns                              localScale = (1, 1, 1)
   │  ├─ BossPattern_Slam                  [BossPattern_Slam]       v3.3
   │  │    _armLTransform  → LeftArm Transform
   │  │    _armLRenderer   → LeftArm SpriteRenderer
   │  │    _bossTransform  → Boss_Warden Transform (자동 캐싱)
   │  │    _rigid2D        → Boss_Warden Rigidbody2D (자동 캐싱)
   │  │
   │  ├─ BossPattern_Sweep                 [BossPattern_Sweep]      v3.2
   │  │    _armLTransform  → LeftArm Transform
   │  │    _armRTransform  → RightArm Transform
   │  │
   │  ├─ BossPattern_Charge                [BossPattern_Charge]     v2.3
   │  │    _armRTransform  → RightArm Transform
   │  │    _rigid2D        → Boss_Warden Dynamic Rigidbody2D  ← Inspector 직접 연결
   │  │    _wallLayer      → Wall 레이어만 선택               ← Inspector 설정 필수
   │  │
   │  ├─ BossPattern_GuardBreak            [BossPattern_GuardBreak] v3.2
   │  │    _armRTransform  → RightArm Transform
   │  │    _armLTransform  → LeftArm Transform
   │  │    _bodyRenderer   → Boss_WardenBody SpriteRenderer
   │  │
   │  └─ BossPattern_RageCharge            [BossPattern_RageCharge] v2.0
   │
   └─ AttackVisualRange                     localScale = (1, 1, 1)
      │  ← 모든 자식 localScale = (1, 1, 1)
      │  ← 크기는 BossWardenAttackRange 코드에서 런타임 설정
      │
      ├─ DiscSlam0         [SpriteRenderer]  반투명 원형 예고 디스크
      ├─ DiscSlam1         [SpriteRenderer]
      ├─ DiscSweep         [SpriteRenderer]
      ├─ DiscGuardBreak    [SpriteRenderer]
      ├─ ChargeLine        [LineRenderer]
      ├─ CoreRangeCircle   [SpriteRenderer]  코어 해제 범위
      ├─ SealRangeCircle   [SpriteRenderer]  봉인 집행 범위
      ├─ RageChargeLine0   [LineRenderer]
      ├─ RageChargeLine1   [LineRenderer]
      └─ RageChargeLine2   [LineRenderer]
```

---

## 레이어 설정 (Physics2D Layer Collision Matrix)

| | Player | Enemy | EnemyAttackHitBox | Wall |
|---|---|---|---|---|
| Player | ❌ | ❌ | ✅ | ✅ |
| Enemy | ❌ | ❌ | ❌ | ✅ |
| EnemyAttackHitBox | ✅ | ❌ | ❌ | ❌ |

> `Enemy ↔ EnemyAttackHitBox : OFF` — 보스 본체와 팔 Collider 상호 충돌 방지
> `EnemyAttackHitBox ↔ EnemyAttackHitBox : OFF` — 팔끼리 충돌 방지
> `Enemy ↔ Wall : ON` — 보스 본체가 벽을 인식 (Charge 충돌용)

---

## 삭제된 컴포넌트 (Prefab에서 제거 필요)

| 컴포넌트 | 위치 | 대체 |
|---|---|---|
| `SealGaugeComponent` | LeftArm / RightArm | `SealableComponent` |
| `BossWardenCoreSealGauge` | Core | `SealableComponent` |
| `BossWardenSealExecutor` | Boss_Warden | `SealExecutor` |
| `BossWardenFeedback` 중복 | Boss_Warden | 1개만 유지 |