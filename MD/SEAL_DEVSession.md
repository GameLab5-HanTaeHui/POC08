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