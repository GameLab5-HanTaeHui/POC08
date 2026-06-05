# BOSS_Warden_DevPlan — 업데이트 패치노트
## 최신 수정사항 반영 기준: 2026-06-05

---

## 📋 스크립트 목록 최신화

### 삭제된 파일 (대체됨)

| 파일명 | 사유 |
|---|---|
| `SealGaugeComponent.cs` | `SealableComponent.cs` 로 완전 대체 |
| `BossWardenCoreSealGauge.cs` | `SealableComponent.cs` 옵션으로 흡수 |
| `BossWardenSealExecutor.cs` | `SealExecutor.cs` 로 완전 대체 |

---

### 신규 추가 파일

| 파일명 | 버전 | 역할 |
|---|---|---|
| `SealGrade.cs` | v1.0 | 봉인 집행 등급 전역 enum (Normal / Part / Core) |
| `SealableComponent.cs` | v1.0 | 봉인도 수치 + 집행 승인 요청 통합 컴포넌트. SealGaugeComponent + BossWardenCoreSealGauge 통합 |
| `SealExecutor.cs` | v1.1 | 봉인 집행 관리자. 적 캐릭터 1개당 1개 보유. ISealable 기반 탐색 |

---

### 수정된 파일

| 파일명 | 이전 버전 | 현재 버전 | 주요 변경 |
|---|---|---|---|
| `BossWardenArmPart.cs` | v1.2 | v2.1 | SealableComponent 교체 / IsGuarding 정면 봉인도 무효 연동 / _playerTransform 캐싱 |
| `BossWardenCore.cs` | v1.1 | v2.0 | SealableComponent / SealExecutor 통합 / HandleCoreExecuted() 코어해제·최종봉인 분기 |
| `BossWardenFeedback.cs` | v1.x | v2.0 | SealableComponent 이벤트 구독 교체 / HandleStateChanged 시그니처 수정 |
| `BossWardenDataSO.cs` | v1.0 | v1.2 | partSealSlowTimeScale / partSealSlowDuration 필드 추가 |
| `BossPattern_Slam.cs` | v3.0 | v3.3 | 백스윙 DOLocalRotate 추가 / +90f 오프셋 수정 / localScale 캐싱 / SetParent Scale 왜곡 수정 |
| `BossPattern_Sweep.cs` | v3.0 | v3.2 | InverseTransformDirection 적용 / 팔 벌릴 때 DOLocalRotate 추가 / +90f 오프셋 수정 |
| `BossPattern_Charge.cs` | v2.0 | v2.3 | DOScale 제거 / _wallLayer ContactFilter2D 벽 충돌 정확 감지 / 팔 DOLocalRotate 추가 |
| `BossPattern_GuardBreak.cs` | v3.0 | v3.2 | 팔 방향 DOLocalRotate 추가 / Recovery·Interrupt 회전 복귀 추가 |

---

## 📐 Prefab 계층 구조 (Boss_Warden.prefab 최신)

```
Boss_Warden                     localScale = (1, 1, 1)
│  [Rigidbody2D]                GravityScale=0 / FreezeRotation Z / Dynamic
│  [CapsuleCollider2D]          isTrigger=false
│  [SpriteRenderer]             Layer=Enemy
│  [BossWardenCore]             ← 루트 통합 관리자
│  [BossWardenAI]               ← AI 상태머신
│  [BossWardenFeedback]         ← 시각 피드백 (1개만 부착)
│  [BossWardenAttackRange]      ← 예고 범위 표시
│  [SealExecutor]               ← 봉인 집행 관리자 (v1.1)
│
├─ Boss_WardenBody              localScale = (5, 7, 1)
│    [SpriteRenderer]
│
├─ LeftArm                      localScale = (2, 6, 1)
│  │  [SpriteRenderer]
│  │  [CapsuleCollider2D]       Layer=Enemy
│  │  [BossWardenArmPart]       ← 피격 감지 + AddGauge
│  │  [SealableComponent]       grade=Part / maxGauge=200
│  │
│  └─ LeftHurtBox
│       [CapsuleCollider2D]     Layer=EnemyAttackHitBox / isTrigger=true
│
├─ RightArm                     localScale = (2, 6, 1)
│  │  [SpriteRenderer]
│  │  [CapsuleCollider2D]       Layer=Enemy
│  │  [BossWardenArmPart]       ← 피격 감지 + IsGuarding 정면 체크
│  │  [SealableComponent]       grade=Part / maxGauge=200
│  │
│  └─ RightHurtBox
│       [CapsuleCollider2D]     Layer=EnemyAttackHitBox / isTrigger=true
│
├─ Core                         localScale = (2, 2, 1)
│  │  [SpriteRenderer]
│  │  [CapsuleCollider2D]       Layer=EnemyAttackHitBox / isTrigger=true
│  │  [SealableComponent]       grade=Core / isDilPhaseOnly=true / phaseTarget=DataSO값
│  │
│  └─ CoreHurtBox
│
├─ HurtBox                      localScale = (5, 7, 1)
│    [CapsuleCollider2D]        isTrigger=false
│
├─ Patterns                     localScale = (1, 1, 1)
│  ├─ BossPattern_Slam          [BossPattern_Slam]      v3.3
│  ├─ BossPattern_Sweep         [BossPattern_Sweep]     v3.2
│  ├─ BossPattern_Charge        [BossPattern_Charge]    v2.3
│  ├─ BossPattern_GuardBreak    [BossPattern_GuardBreak] v3.2
│  └─ BossPattern_RageCharge    [BossPattern_RageCharge] v2.0
│
└─ AttackVisualRange            localScale = (1, 1, 1)
   ├─ DiscSlam0                 localScale = (1, 1, 1)  ← 크기는 BossWardenAttackRange 코드에서 설정
   ├─ DiscSlam1                 localScale = (1, 1, 1)
   ├─ DiscSweep                 localScale = (1, 1, 1)
   ├─ DiscGuardBreak            localScale = (1, 1, 1)
   ├─ ChargeLine                localScale = (1, 1, 1)
   ├─ CoreRangeCircle           localScale = (1, 1, 1)
   ├─ SealRangeCircle           localScale = (1, 1, 1)
   ├─ RageChargeLine0           localScale = (1, 1, 1)
   ├─ RageChargeLine1           localScale = (1, 1, 1)
   └─ RageChargeLine2           localScale = (1, 1, 1)
```

> ⚠️ `Boss_Warden.localScale = (1, 1, 1)` 고정 필수
> SetParent(null, worldPositionStays:true) 시 비균등 Scale이 있으면
> 재부착 후 팔 Scale 왜곡이 누적됨

---

## 🔌 Inspector 연결 체크리스트

### BossWardenCore

| 필드 | 연결 대상 |
|---|---|
| `_data` | BossWardenDataSO 에셋 |
| `_armL` | LeftArm / BossWardenArmPart |
| `_armR` | RightArm / BossWardenArmPart |
| `_armLSealable` | LeftArm / SealableComponent ✅ 신규 |
| `_armRSealable` | RightArm / SealableComponent ✅ 신규 |
| `_coreSealable` | Core / SealableComponent ✅ 신규 |
| `_sealExecutor` | Boss_Warden / SealExecutor ✅ 신규 |
| `_coreObject` | Core GameObject |
| `_shockwave` | BossWardenShockwave |

### SealableComponent (LeftArm / RightArm)

| 필드 | 값 |
|---|---|
| `grade` | Part |
| `_maxGauge` | 200 (DataSO.armSealGaugeMax) |
| `_sealRange` | 1.5 |
| `_sealHoldTime` | 1.5 |
| `_isDilPhaseOnly` | false |

### SealableComponent (Core)

| 필드 | 값 |
|---|---|
| `grade` | Core |
| `_maxGauge` | 500 (DataSO.coreSealGaugeMax) |
| `_sealRange` | DataSO.coreUnlockRange |
| `_sealHoldTime` | DataSO.coreUnlockHoldTime |
| `_isDilPhaseOnly` | true |
| `_phaseTarget` | DataSO.phase1CoreSealTarget |

### BossWardenArmPart (RightArm 전용)

| 필드 | 연결 대상 |
|---|---|
| `_guardBreakPattern` | Patterns/BossPattern_GuardBreak ✅ 신규 |

> LeftArm의 `_guardBreakPattern`은 null로 두기 (체크 자동 스킵)

### SealExecutor

| 필드 | 연결 대상 |
|---|---|
| `_data` | BossWardenDataSO |
| `_attackRange` | BossWardenAttackRange (미연결 시 자동 탐색) |

### BossPattern_Charge

| 필드 | 연결 대상 |
|---|---|
| `_wallLayer` | Wall 레이어만 선택 |
| `_rigid2D` | Boss_Warden Dynamic Rigidbody2D |

---

## 🔴 수정된 버그 목록

| 버그 | 원인 | 수정 |
|---|---|---|
| Slam 팔 Scale 왜곡 | SetParent(null, worldPositionStays:true) 시 localScale 자동 변경 | _armOriginLocalScale 캐싱 + 재부착 시 복구 |
| Slam/Sweep/Charge/GuardBreak 팔 방향 없음 | DOLocalRotate 없음 | Atan2(dir)×Rad2Deg + 90f DOLocalRotate 추가 |
| Slam/Sweep 날아갈 때 오방향 | -90f 오프셋(Vector.Up 기준) | +90f 오프셋(Vector.Down 기준)으로 수정 |
| Charge DOScale 크기 변경 | Warning/Recovery/Interrupt에 DOScale 존재 | DOScale 전부 제거 |
| Charge 벽 오인 감지 | linearVelocity 속도 0만 체크 → Enemy/Ground 등 오인 | ContactFilter2D + Wall 레이어만 OverlapCollider 확인 |
| HandleStateChanged 시그니처 불일치 | string 파라미터 vs Action<WardenAIState, BossPatternBase> | 시그니처 수정 |
| BossWardenFeedback 중복 부착 | Prefab에 2개 부착됨 | Inspector에서 1개 삭제 |
| 코어 해제/최종봉인 경로 동일 | HandleFinalSealed() 항상 Die() | HandleCoreExecuted(): _isGroggy→EnterDilPhase / _isDilPhase→Die() |
| OnForceReleased 람다 해제 불가 | 지역함수 클로저 매번 새 델리게이트 | Dictionary<SealableComponent, Action> 캐싱 |
| GuardBreak 정면 봉인도 무효 미연동 | IsGuarding 체크 코드 없음 | HandlePlayerHit에 IsPlayerFacingFront() 체크 추가 |
| Boss_Warden Scale 왜곡 | Boss_Warden.localScale=(5,7,1) 비균등 | Boss_Warden.localScale=(1,1,1) 고정 |

---

## 📊 봉인 시스템 3단계 구조

```
[1단계] SealGrade.cs
  enum SealGrade { Normal, Part, Core }
  전역 정의 — 모든 파일에서 참조

[2단계] SealableComponent.cs
  적 부위/코어에 각각 부착
  봉인도 수치 (0~_maxGauge)
  저항 배율 적용
  100% → OnSealRequested(this) 발행 → SealExecutor 전달
  grade 필드로 자신의 등급 선언 (Inspector)

[3단계] SealExecutor.cs
  적 캐릭터 1개당 1개 보유
  Start() 에서 GetComponentsInChildren<SealableComponent>() 자동 수집
  DetermineTarget(): Grade 우선순위(Core>Part>Normal) + 거리 탐색
  ExecuteSeal(): 등급별 슬로우 분기
    Normal → 슬로우 없음
    Part   → 집행 완료 후 짧은 슬로우 (partSealSlowDuration)
    Core   → 홀드 중 내내 강한 슬로우
```

---

## 📊 봉인도 흐름 (최신 기준)

```
PlayerAttackDataSO
  BasicSealGaugeAmount=10f / ChargeSealGaugeAmount=30f
       ↓
PlayerAttackController.PlaySwing(combo, dir, sealAmount)
       ↓
PlayerWeaponSwingController.EnableHitbox(index, sealAmount)
       ↓
PlayerAttackHitboxManager.OnHit(hitCol, sealAmount) 발행
       ↓
BossWardenArmPart.HandlePlayerHit(hitCol, sealAmount)
  ① hitCol == _ownCollider(HurtBox) 확인
  ② IsGuarding + IsPlayerFacingFront() → 정면이면 차단 (RightArm만)
  ③ RecoveryVuln / SlamVuln 배율 적용
       ↓
SealableComponent.AddGauge(rawAmount)
  ① 저항 배율 적용 (_sealCount 기반)
  ② 단계 변화 감지 → OnStageChanged → BossWardenFeedback 색상 전환
  ③ 100% 도달 → OnSealRequested(this) 발행
       ↓
SealExecutor.HandleSealRequested(sealable)
  → _sealReadyList 등록
  → ShowSealRange() / ShowCoreRange() (BossWardenAttackRange)
       ↓
플레이어 S키 홀드 (PlayerInputHandler.IsSealHeld)
  + 범위 내 접근 (sealable.SealRange)
       ↓
SealExecutor.ExecuteSeal(target)
  Grade = Part  → 홀드 완료 후 짧은 슬로우 (0.5×, 0.25초)
  Grade = Core  → 홀드 중 강한 슬로우 (0.3×)
       ↓
SealableComponent.ExecuteSeal()
  → OnSealCompleted 발행
       ↓
BossWardenCore 구독
  HandleArmLSealed / HandleArmRSealed → CheckGroggyCondition()
  HandleCoreExecuted()
    _isGroggy = true  → EnterDilPhase()
    _isDilPhase = true → Die()
```

---

## 🔄 STEP 진행 현황 (최신)

| 항목 | 상태 |
|---|---|
| 봉인 시스템 3단계 구조 (SealGrade / SealableComponent / SealExecutor) | ✅ 완료 |
| SealExecutor BossWardenAttackRange 범위 표시 연동 | ✅ 완료 |
| BossWardenArmPart IsGuarding 정면 봉인도 무효 | ✅ 완료 |
| sealAmount DataSO→HitboxManager→ArmPart 연결 | ✅ 완료 (기존 구현) |
| BossWardenCore 코어해제/최종봉인 분기 | ✅ 완료 |
| HandleStateChanged 시그니처 수정 | ✅ 완료 |
| Slam / Sweep / GuardBreak 팔 방향 회전 (+90f) | ✅ 완료 |
| Charge DOScale 제거 | ✅ 완료 |
| Charge 벽 충돌 정확 감지 (Wall 레이어) | ✅ 완료 (미테스트) |
| Boss_Warden localScale=(1,1,1) Scale 왜곡 수정 | ✅ 완료 |
| 봉인도 UI 게이지 | ⬜ 미구현 (버티컬 슬라이스 이후) |
| 집행 홀드 진행 UI | ⬜ 미구현 (버티컬 슬라이스 이후) |
| 전체 루프 통합 테스트 | ⬜ 미완료 |