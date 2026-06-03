# BOSS_Warden_DevPlan.md
# Boss_Warden (간수) — 보스 개발 기획 및 구현 계획서

> 작성 기준: SEAL_README 전체 기획 + Boss 승급 완성본  
> 구현 원칙: Sprite / Animator / Particle 없음 — 프리미티브 도형 + 색상으로만 표현  
> 공격 예고 범위 피드백 최우선 — 플레이어를 죽이는 것이 아니라 패턴 회피를 유도  
> namespace: SEAL

---

## 목차

1. 보스 개요
2. 구현 원칙
3. 색상 상태 정의
4. 공격 예고 범위 피드백 설계
5. 전투 루프 전체 흐름
6. 페이즈 구조
7. 패턴 상세 설계
8. 부위 설계
9. 딜 페이즈 설계
10. 최종 봉인 설계
11. 이벤트 흐름
12. 스크립트 목록 및 역할
13. DataSO 수치표
14. Hierarchy 구조
15. 코드 구현 순서 및 진척 단계

---

## 1. 보스 개요

| 항목 | 내용 |
|---|---|
| 이름 | Warden (간수) |
| 분류 | 일반 보스 |
| 아키타입 | 기사형 (근접 중심 / 방향 공략 강조) |
| 페이즈 수 | 2페이즈 |
| 부위 수 | 2개 (왼팔 / 오른팔) |
| 패턴 수 | 1페이즈 4개 / 2페이즈 5개 |
| 코어 | 있음 (등 중앙) |
| 딜 페이즈 | 있음 |
| 최종 봉인 | 있음 |
| 비주얼 | 프리미티브 도형 + 색상 전용 |
| 구현 목표 | 봉인 집행 → 그로기 → 코어 해제 → 딜 페이즈 → 최종 봉인 플레이 루프 완전 구현 |

---

## 2. 구현 원칙

### 프리미티브 전용 구현

Sprite, Animator, Particle 사용 없음.  
모든 오브젝트는 Unity 내장 프리미티브(Circle/Square Sprite)로 생성한다.  
SpriteRenderer.color 와 DOTween 으로 모든 시각 피드백을 처리한다.

| 표현 수단 | 역할 |
|---|---|
| `SpriteRenderer` 단색 프리미티브 | 본체 / 부위 / 코어 / 예고 디스크 형태 표시 |
| `DOTween` DOColor | 상태 변화 색상 전환 — Animator 대체 |
| `DOTween` DOScale / DOLocalMove | 동작 모션 연출 — Animator 대체 |
| `DOTween` DOShake | 피격 / 충격 반응 연출 |
| `LineRenderer` | 공격 예고선 (돌진 방향, 스윕 궤적, 봉인 범위 점선) |
| `SpriteRenderer` 반투명 단색 | 공격 예고 범위 디스크 |

### DOTween 은 Animator 대체재다

Animator / AnimationClip 을 사용할 시간이 없기 때문에 DOTween 으로 대체한다.  
DOTween 의 목적은 단순 연출이 아니라 **캐릭터의 동작감 표현**이다.

```
패턴 예시 — Charge (돌진):
  Warning:  DOColor 주황 Pulse (긴장감)
  Active:   Rigidbody2D.linearVelocity 직접 제어 (빠른 돌진)
  Recovery: DOShakePosition (충격 여운) + DOColor 붉은 페이드 (취약 구간)

패턴 예시 — Slam (내려치기):
  Warning:  팔 오브젝트 DOLocalMoveY 위로 들어올림 (모션 준비)
  Active:   팔 오브젝트 DOLocalMoveY 빠르게 내려침 (OutBounce)
  Recovery: 팔 원위치 복귀 + 본체 DOShakeScale
```

### 히트 스탑은 점멸로 구현한다

Sprite / Particle 작업 시간 없음.  
공격 적중 피드백은 **SpriteRenderer 점멸** 방식으로 처리한다.

```
공격 적중 시:
  SpriteRenderer.color → Color.white (순간 전환)
  WaitForSecondsRealtime(0.05f ~ 0.1f)
  SpriteRenderer.color → 원래 상태 색상으로 DOColor 복귀
  Time.timeScale 소량 감소 병행 가능 (선택 — HitStop)
```

피격 오브젝트마다 독립적으로 실행한다.  
부위 피격 → 해당 부위 SpriteRenderer 점멸.  
본체 피격 → 본체 SpriteRenderer 점멸.

### 색상이 곧 정보다

플레이어가 색상만 보고 현재 상태를 즉시 파악할 수 있어야 한다.  
모든 상태는 고유한 색상을 가진다. 색상이 겹치면 안 된다.

### 공격 예고가 전투의 핵심이다

패턴의 목적은 플레이어를 죽이는 것이 아니라  
회피와 봉인 공략 사이의 긴장감을 조성하는 것이다.  
예고 범위는 항상 실제 히트박스보다 넉넉하게 표시한다.

### POC07 보스 코드 참고 원칙

POC07 에 보스 관련 코드가 존재한다. 구조 설계 시 반드시 먼저 정독한다.  
그대로 복사하지 않고 **탑뷰 / S키 봉인 / 봉인도 누적 방식**으로 변환하여 적용한다.

| POC07 파일 | 참고 포인트 | 변환 내용 |
|---|---|---|
| `TestBossCore.cs` | 그로기 / 코어 활성화 / 딜타임 / 루프 구조 | 코어 해제 = A키 홀드 → S키 홀드로 변환 |
| `TestBossAI.cs` | Idle/Chase/Warning/Active/Recovery 흐름 | 횡스크롤 이동 → 탑뷰 8방향 이동으로 변환 |
| `TestBossPatternBase.cs` | Warning/Active/Recovery 추상 구조 + Interrupt | 그대로 계승 |
| `TestBossArmPart.cs` | 부위 상태 관리 (ReLock / ForceUnlock) | 봉인도 누적 구조 추가 |
| `TestBossExecution.cs` | 홀드 입력 처리 + 이동 차단 + 완료 이벤트 | A키 → S키 / 자동이동 제거 / 범위 내 접근 방식으로 변환 |
| `TestBossFeedBack.cs` | DOTween 상태별 색상/스케일 연출 | 그대로 계승 + 봉인도 단계 색상 추가 |
| `TestBossShockwave.cs` | 충격파 발동 + 넉백 | 그대로 계승 |

---

## 3. 색상 상태 정의

### 본체 (Warden) 색상

| 상태 | 색상 | Hex | 설명 |
|---|---|---|---|
| Idle / Chase | 회색 | `#888888` | 기본 대기 / 추적 |
| Warning (패턴 예고) | 주황 | `#FF8800` | 패턴 준비 중 |
| Active (패턴 시전) | 흰색 | `#FFFFFF` | 공격 중 |
| Recovery (후딜) | 붉은색 | `#CC2200` | 취약 구간 |
| Groggy (그로기) | 노란색 | `#FFEE00` | 처형 / 봉인 집행 가능 |
| DilPhase (딜 페이즈) | 밝은 주황 | `#FF6600` | 코어 공격 구간 |
| 2페이즈 전환 | 진한 붉은색 | `#990000` | 페이즈 전환 분노 |
| Dead | 검정 | `#111111` | 처치 |

### 부위 (ArmPart) 색상

| 상태 | 색상 | Hex | 설명 |
|---|---|---|---|
| 봉인도 0% | 밝은 회색 | `#AAAAAA` | 미피격 |
| 봉인도 25% | 노랑 | `#FFDD00` | 균열 발생 |
| 봉인도 50% | 주황 | `#FF8800` | 사슬 생성 |
| 봉인도 75% | 빨강 | `#FF2200` | 사슬 증가 |
| 봉인도 100% | 파랑 빠른 Pulse | `#0088FF` | 봉인 집행 가능 |
| 봉인 완료 | 파랑 고정 | `#0044CC` | 봉인 완료 |
| 봉인 저항 1회 | 파랑 + 약한 광택 | `#0066EE` | 저항 획득 |
| 봉인 저항 2회+ | 파랑 + 보라 | `#4400AA` | 높은 저항 |

### 코어 색상

| 상태 | 색상 | Hex | 설명 |
|---|---|---|---|
| 비활성 | 비표시 (SetActive=false) | — | 평상시 숨김 |
| 활성 (코어 해제 대기) | 노란 빠른 Pulse | `#FFEE00` | 해제 가능 |
| 딜 페이즈 중 | 흰 빠른 Pulse | `#FFFFFF` | 공격 대상 |
| 최종 봉인 가능 | 청백 강한 Pulse | `#AADDFF` | 최종 봉인 가능 |

---

## 4. 공격 예고 범위 피드백 설계

> README 원칙: 패턴은 플레이어를 죽이기 위한 것이 아니라  
> 플레이어가 위험을 피하며 봉인을 수행하도록 만드는 것이다.

### 예고 표시 원칙

- 예고 범위는 실제 히트박스보다 **10~20% 넉넉하게** 표시한다
- 예고는 Warning 구간 시작 즉시 표시한다
- 예고 색상은 붉은 반투명으로 통일 (`#FF0000` 투명도 40%)
- 예고가 표시된 방향 반대편이 항상 안전 구간이어야 한다

### 패턴별 예고 표시 방식

#### Charge (돌진) 예고

```
표시 방식: LineRenderer 직선 예고선
           돌진 방향으로 긴 직선 + 양 끝 폭 표시
표시 범위: 돌진 방향 × 8 units 길이 / 좌우 0.8 units 폭
표시 시작: Warning 시작 즉시
색상: 붉은 반투명 LineRenderer
유지 시간: Warning 전체 구간 (Active 시작 시 제거)
안전 구간: 예고선 측면 / 후방
```

#### Slam (내려치기) 예고

```
표시 방식: 원형 반투명 디스크 (SpriteRenderer 원형 스케일 조정)
표시 범위: 플레이어 위치 기준 반경 3.0 units (실제 히트 반경 2.5 units)
표시 시작: Warning 시작 즉시 (플레이어 현재 위치에 스냅, 이후 고정)
색상: 붉은 반투명
유지 시간: Warning 전체 구간 (Active 시 밝기 증가 → 즉시 히트 → 제거)
안전 구간: 원 외부
```

#### Sweep (스윕) 예고

```
표시 방식: 반원형 扇形 디스크
표시 범위: Warden 중심 반경 3.5 units 반원 (실제 2.5 units)
표시 시작: Warning 시작 즉시
색상: 붉은 반투명 — 회전 방향 화살표 포함
유지 시간: Warning 구간 + Active 회전 중 함께 회전
안전 구간: 회전 반대편 후방
```

#### GuardBreak (강타) 예고

```
표시 방식: 정면 직사각형 (짧고 넓음)
표시 범위: 정면 1.5 units × 1.0 units (실제 1.0 × 0.8 units)
표시 시작: Warning 후반부 (0.8초 중 0.5초 경과 후 표시)
색상: 붉은 반투명
특이사항: Warning 전반부는 가드 자세만 표시 (예고선 없음) → 측면 공략 유도
안전 구간: 측면 / 후방
```

#### RageCharge (3연 돌진, 2페이즈) 예고

```
표시 방식: LineRenderer 직선 예고선 3개 순차 표시
표시 범위: 3회 돌진 방향 각각 예고선
표시 시작: Warning 시작 → 0.2초 간격으로 3개 순차 표시
색상: 붉은 반투명 (밝기 순차 증가)
안전 구간: 3개 예고선 사이 빈 공간
```

### 봉인 집행 가능 범위 피드백

```
표시 방식: 부위 주변 원형 점선 (LineRenderer 점선)
색상: 파란색 `#0088FF`
반경: 1.5 units (SealExecutionRange)
표시 조건: 부위 봉인도 100% 달성 시 표시
유지: 봉인 집행 완료 or 봉인도 감소 시 제거
```

### 코어 해제 가능 범위 피드백

```
표시 방식: 코어 주변 원형 점선
색상: 노란색 `#FFEE00`
반경: 1.5 units (CoreUnlockRange)
표시 조건: 그로기 진입 + 코어 활성화 시
```

---

## 5. 전투 루프 전체 흐름

```
전투 시작
  ↓
[패턴 루프] Warden 패턴 선택 → Warning → Active → Recovery
  ↓
플레이어: 예고 범위 회피 + 공격으로 부위 봉인도 누적
  ↓
부위 봉인도 100% → 파랑 Pulse + 집행 범위 표시
  ↓
S키 봉인 집행 (홀드 1.5초) → 부위 봉인 완료 → 연결 패턴 비활성
  ↓
[그로기 조건] 왼팔 + 오른팔 봉인 완료 동시 달성
  ↓
정식 그로기 진입 (5초)
  → Warden 노란 Pulse + 완전 정지
  → 코어 SetActive=true + 노란 빠른 Pulse
  → 코어 해제 범위 표시 (점선 원)
  ↓
플레이어 코어 접근 → S키 코어 해제 (홀드 3초)
  → 슬로우 모션 (Time.timeScale 0.3)
  → 완료 시 딜 페이즈 진입
  ↓
딜 페이즈 (최대 10초)
  → Warden 밝은 주황 Pulse + 완전 정지
  → 코어 흰 빠른 Pulse (공격 대상)
  → 플레이어 코어 공격 → 코어 봉인도 누적
  ↓
[1페이즈 종료 조건] 코어 봉인도 50% or 10초 경과
  → 딜 페이즈 종료
  → 충격파 발생 (플레이어 넉백)
  → 부위 봉인 해제 + 봉인도 초기화
  → 2페이즈 진입 (패턴 강화 + RageCharge 추가)
  → 패턴 루프 재시작
  ↓
[2페이즈] 동일 루프 반복
  ↓
[2페이즈 종료 조건] 코어 봉인도 100% 도달
  → 딜 페이즈 즉시 종료
  → 코어 청백 강한 Pulse + 최종 봉인 표시
  ↓
S키 최종 봉인 (홀드 2초)
  → 강한 슬로우 (Time.timeScale 0.1)
  → 완료 시 Warden 처치 연출
  ↓
전투 종료 → 보상 발생
```

---

## 6. 페이즈 구조

### 1페이즈

| 항목 | 내용 |
|---|---|
| 코어 봉인도 목표 | 0% → 50% (내부 0 → 250) |
| 사용 패턴 | Charge / Slam / Sweep / GuardBreak |
| 이동 속도 | 3.5 units/s |
| 그로기 조건 | 왼팔 + 오른팔 동시 봉인 |
| 딜 페이즈 지속 | 최대 10초 |
| 딜 페이즈 종료 | 10초 경과 or 코어 봉인도 50% 도달 |

### 2페이즈 (강화)

| 항목 | 내용 |
|---|---|
| 코어 봉인도 목표 | 50% → 100% (내부 250 → 500) |
| 사용 패턴 | Charge(강화) / Slam(강화) / Sweep(강화) / GuardBreak(강화) / RageCharge(신규) |
| 이동 속도 | 4.5 units/s |
| 그로기 조건 | 동일 (왼팔 + 오른팔 동시 봉인) |
| 딜 페이즈 지속 | 최대 10초 |
| 딜 페이즈 종료 | 10초 경과 or 코어 봉인도 100% → 최종 봉인 전환 |

### 페이즈 전환 충격파

```
발생 시점: 1페이즈 딜 페이즈 종료 직후
중심: Warden 본체 위치
반경: 6 units
넉백: (플레이어 - Warden).normalized × 12f (DOTween DOMove)
취소 효과: S키 홀드 중이면 집행 강제 취소 / A키 홀드 강공격 강제 취소
연출:
  - Warden 본체 흰 플래시 → 진한 붉은 전환 (DOColor)
  - 원형 반투명 디스크 Scale 0 → 충격파 반경으로 빠르게 확장 (DOScale OutQuart)
  - 카메라 DOShake
  - 부위 봉인 해제 (파랑 → 회색으로 DOColor)
```

---

## 7. 패턴 상세 설계

### Pattern 1 — Charge (돌진) [오른팔 연결]

| 항목 | 1페이즈 | 2페이즈 |
|---|---|---|
| 예고 시간 | 0.8초 | 0.8초 |
| 돌진 속도 | 12 units/s | 16 units/s |
| 돌진 거리 | 8 units | 8 units |
| 후딜 시간 | 0.8초 | 0.5초 → Slam 연계 |
| 히트박스 | 정면 직사각형 (1.0 × 8.0) | 동일 |
| 예고 범위 | 1.6 × 9.6 (20% 넉넉) | 동일 |
| 그로기 유발 | Recovery 완료 시 (OnPatternGroggy) | 동일 |

**Warning 처리:**
- 방향 = `(playerPos - bossPos).normalized` 계산 후 고정
- LineRenderer 돌진 예고선 표시
- Warden 본체 주황 Pulse (DOColor Yoyo)

**Active 처리:**
- `Rigidbody2D.linearVelocity = direction × chargeSpeed`
- 예고선 제거
- 돌진 거리 도달 or 벽 충돌 시 종료

**Recovery 처리:**
- DOShakePosition (충격 연출)
- 붉은 페이드 (DOColor)
- 2페이즈: Recovery 없이 즉시 Slam 연계

---

### Pattern 2 — Slam (내려치기) [왼팔 연결]

| 항목 | 1페이즈 | 2페이즈 |
|---|---|---|
| 예고 시간 | 1.0초 | 1.0초 |
| 히트 반경 | 2.5 units | 2.5 units × 2회 |
| 히트 위치 | Warning 시작 시 플레이어 위치 | 첫 번째 + 0.5초 후 두 번째 위치 |
| 후딜 시간 | 0.7초 | 0.5초 |
| 예고 범위 | 반경 3.0 units 디스크 | 2개 디스크 |
| 그로기 유발 | 없음 (Charge와 역할 분리) | 없음 |

**Warning 처리:**
- 플레이어 현재 위치에 붉은 원형 디스크 생성 (고정)
- 왼팔 오브젝트 DOLocalMoveY 위로 들어올림 연출
- Warden 본체 주황 Pulse

**Active 처리:**
- 빠른 왼팔 내려침 DOLocalMoveY (OutBounce)
- 원형 디스크 밝기 순간 증가 → 즉시 제거
- OverlapCircle 히트박스 체크
- 2페이즈: 0.5초 후 두 번째 내려치기 (새 위치 디스크)

**Recovery 처리:**
- 왼팔 원위치 DOLocalMoveY
- 본체 DOShakeScale

---

### Pattern 3 — Sweep (스윕) [왼팔 연결]

| 항목 | 1페이즈 | 2페이즈 |
|---|---|---|
| 예고 시간 | 0.6초 | 0.6초 |
| 회전 각도 | 360° 1회 | 360° 2회 |
| 회전 속도 | 180°/s | 270°/s |
| 히트 반경 | 2.5 units | 2.5 units |
| 후딜 시간 | 0.5초 | 0.3초 |
| 예고 범위 | 반경 3.0 units 반원 디스크 | 반경 3.5 units |
| 그로기 유발 | 없음 | 없음 |

**Warning 처리:**
- 반원형 디스크 생성 (회전 시작 방향 기준)
- 회전 방향 화살표 LineRenderer
- Warden 본체 느리게 자전 시작 DORotate

**Active 처리:**
- `transform.DORotate(Vector3.forward × 360°, duration)` RotateMode.FastBeyond360
- 히트박스: OverlapCircle 매 프레임 체크
- 디스크도 함께 회전
- 2페이즈: 2회전

**Recovery 처리:**
- 회전 정지
- 자연스러운 방향 전환

---

### Pattern 4 — GuardBreak (강타) [오른팔 연결]

| 항목 | 1페이즈 | 2페이즈 |
|---|---|---|
| 예고 시간 | 1.2초 | 0.9초 |
| 가드 구간 | 예고 전반부 0.8초 | 0.5초 |
| 예고 표시 시작 | 0.8초 경과 후 | 0.5초 경과 후 |
| 히트박스 | 정면 (1.0 × 0.8) | 정면 (1.2 × 0.8) |
| 예고 범위 | 1.5 × 1.0 | 1.8 × 1.0 |
| 후딜 시간 | 1.0초 | 0.7초 |
| 그로기 유발 | Recovery 완료 시 (OnPatternGroggy) | Recovery 완료 시 |
| 특이사항 | 가드 중 정면 봉인도 무효 | 가드 중 정면 봉인도 무효 |

**Warning 처리:**
- 전반부: Warden 본체 흰 발광 (가드 자세) — 예고선 없음
- 후반부: 정면 직사각형 예고 표시
- 이동 속도 0 고정

**Active 처리:**
- 오른팔 빠른 전방 타격 DOLocalMove (OutExpo)
- OverlapBox 히트박스 체크
- 측면/후방 공격은 봉인도 정상 누적 (방향 공략 유도)

**Recovery 처리:**
- 긴 후딜 (공략 기회)
- 봉인도 누적 ×1.5 적용 (취약 구간 — BossWardenAI.SetArmsRecoveryVuln 이 처리)
- OnPatternGroggy 발행 → 그로기 진입 유도

**`IsGuarding` 플래그 연동 — 구현 방식:**

```
BossPattern_GuardBreak.IsGuarding (public bool)
  - Warning 전반부 진입 시: IsGuarding = true
  - Warning 후반부 진입 시: IsGuarding = false
  - Interrupt() 호출 시:    IsGuarding = false (강제 해제)

BossWardenArmPart.HandlePlayerHit()
  현재 구현: IsGuarding 체크 없음 → 정면 피격도 봉인도 누적됨
  
[STEP 09 전 수정 필요]
  BossWardenArmPart.HandlePlayerHit() 에 가드 체크 추가:
  
  // RightArm (오른팔) 에만 적용
  if (_partType == WardenPartType.RightArm)
  {
      var gb = GetComponentInParent<BossPatternBase>() 로 탐색 불가
      → BossWardenArmPart 에 [SerializeField] BossPattern_GuardBreak _guardBreakPattern 추가
      → HandlePlayerHit 내에서:
         if (_guardBreakPattern != null && _guardBreakPattern.IsGuarding)
         {
             // 정면 방향 체크 — 플레이어가 보스 정면에 있으면 봉인도 무효
             // 측면/후방은 정상 누적
             return;
         }
  }

  임시 처리 (STEP 09 테스트): IsGuarding 체크 없이 전방향 봉인도 누적 허용
```

---

### Pattern 5 — RageCharge (3연 돌진, 2페이즈 전용)

| 항목 | 내용 |
|---|---|
| 연결 부위 | 없음 (독립 패턴) |
| 예고 시간 | 0.8초 |
| 돌진 횟수 | 3회 연속 |
| 돌진 속도 | 18 units/s |
| 돌진 간격 | 0.2초 |
| 후딜 시간 | 1.2초 |
| 예고 범위 | 3개 LineRenderer 예고선 순차 표시 |
| 그로기 유발 | 없음 |
| 취약 구간 | Recovery 1.2초 (봉인도 ×1.5) |

**Warning 처리:**
- 0.0초: 첫 번째 돌진 예고선 표시 (밝기 100%)
- 0.3초: 두 번째 돌진 예고선 표시 (밝기 80%)
- 0.6초: 세 번째 돌진 예고선 표시 (밝기 60%)
- Warden 전신 붉은 발광 DOColor Yoyo

**Active 처리:**
- 3회 돌진 순차 실행
- 각 돌진 방향은 Warning 시 계산한 방향 순서 유지
- 3번째 돌진 벽 충돌 시 추가 경직 0.3초

**Recovery 처리:**
- DOShakePosition (피로감 연출)
- 봉인도 누적 ×1.5

---

## 8. 부위 설계

### 왼팔 (LeftArm)

| 항목 | 내용 |
|---|---|
| 연결 패턴 | Slam / Sweep |
| 봉인 완료 효과 | Slam / Sweep 패턴 비활성 |
| 봉인도 내부 요구치 | 200 (UI 100%) |
| 봉인도 누적 단위 | 기본 공격 +10 / 강공격 +30 |
| UI 증가율 | 기본 공격 +5% / 강공격 +15% |
| 봉인 유지 | 딜 페이즈 종료 시까지 유지 → 이후 강제 해제 |
| 봉인 저항 | 1회:×1.0 / 2회:×0.8 / 3회:×0.6 / 4회+:×0.4 |

**봉인도 단계별 색상 전환:**
```
0%   → #AAAAAA (밝은 회색)
25%  → #FFDD00 (노랑)     DOColor 전환
50%  → #FF8800 (주황)     DOColor 전환 + 약한 Pulse 시작
75%  → #FF2200 (빨강)     DOColor 전환 + 강한 Pulse
100% → #0088FF (파랑)     DOColor 전환 + 빠른 Yoyo Pulse + DOPunchScale
```

**봉인 집행 조건:**
```
- 봉인도 100% 달성
- 플레이어가 부위 범위 내 (반경 1.5 units)
- S키 홀드 1.5초
```

### 오른팔 (RightArm)

| 항목 | 내용 |
|---|---|
| 연결 패턴 | Charge / GuardBreak |
| 봉인 완료 효과 | Charge / GuardBreak 패턴 비활성 |
| 봉인도 내부 요구치 | 200 (UI 100%) |
| 봉인도 누적 단위 | 기본 공격 +10 / 강공격 +30 |
| UI 증가율 | 기본 공격 +5% / 강공격 +15% |
| 봉인 유지 | 딜 페이즈 종료 시까지 유지 → 이후 강제 해제 |
| 봉인 저항 | 동일 |

---

## 9. 딜 페이즈 설계

```
진입 조건: 코어 해제 S키 홀드 3초 완료

진입 연출:
  Time.timeScale → 0.3
  코어 DOScale Punch + 흰 발광 폭발
  Warden 본체 밝은 주황 전환 DOColor
  Time.timeScale → 1.0

딜 페이즈 중 규칙:
  Warden 완전 정지 (AI 정지)
  코어만 공격 유효 (본체 피격 무효)
  코어 흰 빠른 Pulse 유지

코어 봉인도 누적 (딜 페이즈 전용):
  기본 공격 적중: 내부 +25 / UI +5%
  강공격 적중:   내부 +75 / UI +15%

종료 조건:
  A. 10초 경과 → 일반 종료
  B. 1페이즈: 코어 봉인도 50% → 일반 종료 → 2페이즈 전환
  C. 2페이즈: 코어 봉인도 100% → 최종 봉인 전환

일반 종료 연출 (A / B 케이스):
  코어 축소 비활성 (DOScale → 0)
  충격파 발생 (BossWardenShockwave)
  부위 봉인 해제 (파랑 → 회색 DOColor)
  부위 봉인도 초기화
  AI 재개
```

---

## 10. 최종 봉인 설계

```
진입 조건: 딜 페이즈 중 코어 봉인도 100% 도달

진입 연출:
  딜 페이즈 즉시 종료
  Time.timeScale → 0.1 (강한 슬로우)
  코어 청백 강한 Pulse (#AADDFF)
  UI "S — 최종 봉인" 표시

최종 봉인 실행:
  S키 홀드 2초 (슬로우 중에도 실시간 측정)
  홀드 진행 중:
    무기 오브젝트 코어 방향 DOMove 연출
    사슬 LineRenderer 순차 전개
    자물쇠 오브젝트 DOScale Punch
  홀드 완료:
    Time.timeScale → 1.0
    Warden 처치 연출:
      DOColor → #111111 (검정)
      DOScale → 0 (OutBack)
      OnDead 이벤트 발행
    전투 종료 → 보상 발생

중단 조건:
  S키 해제 시 진행도 리셋 (최종 봉인은 재도전 가능)
  그로기 종료 타이머 없음 (최종 봉인 중에는 Warden 완전 정지)
```

---

## 11. 이벤트 흐름

> 아래는 실제 구현된 코드 기준의 이벤트 흐름이다.
> 기획서 초안과 다른 부분은 **[구현 변경]** 으로 표시한다.

```
[봉인도 누적 흐름 — PlayerAttackHitboxManager 기반]
PlayerAttackHitboxManager.OnHit(Collider2D col, float sealAmount)
  → BossWardenArmPart.HandlePlayerHit(col, sealAmount)
      col == _ownCollider 확인 후
      Recovery 취약 구간 배율 적용
      → SealGaugeComponent.AddGauge(rawAmount)
        → OnStageChanged(int stage) → BossWardenFeedback: 봉인도 단계 색상 전환
        → OnSealReady() → BossWardenSealExecutor: 집행 가능 등록 + ShowSealRange()

[부위 봉인 집행 흐름]
BossWardenSealExecutor.DetectSealInput() 루프
  → DetermineTarget() → PartSeal 집행 대상 확인
  → S키 홀드 완료 (sealExecutionHoldTime)
  → SealGaugeComponent.ExecuteSeal()
    → OnSealed() → BossWardenArmPart.HandleSealed()
      → OnPartSealed(WardenPartType) → BossWardenCore.HandlePartSealed()
        → _sealedArmCount++
        → CheckGroggyCondition() : armL.IsSealed && armR.IsSealed 확인
          → EnterGroggy()
  → BossWardenFeedback.HandleArmLSealed / HandleArmRSealed: 파랑 고정 색상

[그로기 흐름]
BossWardenCore.EnterGroggy()
  → ActivateCore() : _coreObject.SetActive(true)
  → OnGroggyEnter 발행
    → BossWardenAI.HandleGroggyEnter(): _isStopped = true + InterruptCurrentPattern()
    → BossWardenFeedback.HandleGroggyEnter(): 노란 빠른 Pulse + 코어 노란 Pulse
    → BossWardenSealExecutor.HandleGroggyEnter(): _isCoreUnlockActive = true + ShowCoreRange()
  → GroggyRoutine(groggyDuration) 시작

[코어 해제 흐름]
BossWardenSealExecutor.DetectSealInput() 루프
  → DetermineTarget() → CoreUnlock 집행 대상 확인
  → S키 홀드 + Time.timeScale 슬로우 (dilPhaseSlowTimeScale)
  → 홀드 완료 → CompleteExecution()
    → OnCoreUnlocked 발행
      → BossWardenCore.HandleCoreUnlocked()
        → GroggyCoroutine Stop (타이머 취소)
        → _isGroggy = false
        → EnterDilPhase()

[딜 페이즈 흐름]
BossWardenCore.EnterDilPhase()
  → BossWardenCoreSealGauge.ActivateGauge(true)
  → OnDilPhaseEnter 발행
    → BossWardenAI.HandleDilPhaseEnter(): _isStopped = true
    → BossWardenFeedback.HandleDilPhaseEnter(): 밝은 주황 Pulse + 코어 흰 Pulse
    → BossWardenSealExecutor.HandleGroggyExit() 수신으로 코어 해제 감지 비활성
  → DilPhaseRoutine(dilPhaseDuration) 시작

BossWardenCoreSealGauge.OnPhase1TargetReached
  → BossWardenCore.HandlePhase1TargetReached() → ExitDilPhase(false)

BossWardenCoreSealGauge.OnPhase2TargetReached
  → BossWardenCore.HandlePhase2TargetReached() → ExitDilPhase(true)

[딜 페이즈 종료 — 일반 (isFinalSeal = false)]
BossWardenCore.ExitDilPhase(false)
  → ActivateGauge(false)
  → DeactivateCore()
  → _armL.ForceRelease(false) + _armR.ForceRelease(false)
  → _shockwave.Trigger(transform.position)   ← [구현 변경] 이벤트 구독 아님, 직접 호출
      → BossWardenShockwave: OverlapCircleNonAlloc → ApplyKnockbackRoutine()
  → OnDilPhaseExit 발행   ← [구현 변경] bool 파라미터 없음, Action 타입
    → BossWardenAI.HandleDilPhaseExit(): _isStopped = false + Idle 복귀
    → BossWardenFeedback.HandleDilPhaseExit(): Idle 색상 복귀
    → BossWardenSealExecutor.HandleDilPhaseExit(): _isFinalSealActive = false
  → _currentPhase == 1 → OnPhaseChanged(2) 발행
    → BossWardenAI.HandlePhaseChanged(2): phase2MoveSpeed + UnlockPhase2() 전체
    → BossWardenFeedback.HandlePhaseChanged(2): 진한 붉은 전환 연출

[딜 페이즈 종료 — 최종 봉인 (isFinalSeal = true)]
BossWardenCore.ExitDilPhase(true)
  → ActivateGauge(false)
  → OnFinalSealReady 발행
    → BossWardenSealExecutor.HandleFinalSealReady(): _isFinalSealActive = true

[최종 봉인 흐름]
BossWardenSealExecutor.DetectSealInput() 루프
  → DetermineTarget() → FinalSeal 집행 대상 확인
  → S키 홀드 + 강한 슬로우 (finalSealSlowTimeScale)
  → 홀드 완료 → CompleteExecution()
    → OnFinalSealCompleted 발행
      → BossWardenCore.HandleFinalSealCompleted() → Die()

[처치 흐름]
BossWardenCore.Die()
  → StopAllCoroutines()
  → _rigid2D.linearVelocity = 0
  → DeactivateCore() + ActivateGauge(false)
  → _attackRange.HideAll()
  → OnDead 발행
    → BossWardenAI.HandleDead(): enabled = false
    → BossWardenFeedback.HandleDead(): 검정 DOColor + DOScale 0

[그로기 실패 흐름 — 타이머 만료]
GroggyRoutine 완료 → ExitGroggyFailure()
  → DeactivateCore()
  → _armL.ForceRelease(false) + _armR.ForceRelease(false)
  → OnGroggyExit 발행
    → BossWardenAI.HandleGroggyExit(): _isStopped = false + Idle 복귀
    → BossWardenFeedback.HandleGroggyExit(): Idle 색상 복귀
    → BossWardenSealExecutor.HandleGroggyExit(): _isCoreUnlockActive = false
```

---

## 12. 스크립트 목록 및 역할

### 플레이어 측 연동 스크립트 (POC08 기존 구현)

| 파일명 | 버전 | 역할 | 보스 연동 내용 |
|---|---|---|---|
| `PlayerInputHandler.cs` | v1.2 | 키 입력 통합 관리 | `IsSealHeld` 프로퍼티 추가 — BossWardenSealExecutor 가 S키 홀드 폴링 |
| `PlayerAttackHitboxManager.cs` | v1.0 | 무기 히트박스 관리 | `OnHit(Collider2D col, float sealAmount)` — BossWardenArmPart / CorsSealGauge 가 구독 |
| `PlayerMoveController.cs` | v1.0 | 8방향 이동 + 대시 | BossWardenShockwave 넉백 시 BlockAll / UnblockAll 연동 |

> `PlayerAttackHitboxManager.OnHit` 이벤트가 보스 봉인도 누적의 유일한 진입점이다.
> 패턴 히트박스(OverlapXX)는 플레이어 피격 감지 전용이며 봉인도와 무관하다.

### 공용 (재사용 가능)

| 파일명 | 역할 | 위치 |
|---|---|---|
| `SealGaugeComponent.cs` | 부위 봉인도 수치 관리 / 단계별 이벤트 발행 / 봉인 저항 배율 적용 | Assets/SEAL/Shared/ |
| `BossPatternBase.cs` | 패턴 추상 베이스 (Warning/Active/Recovery/Interrupt/Phase2) | Assets/SEAL/Boss/Pattern/ |

### Warden 전용

| 파일명 | 역할 | 주요 특이사항 |
|---|---|---|
| `BossWardenDataSO.cs` | 모든 수치 + 색상 ScriptableObject. 모든 컴포넌트의 단일 수치 소스 | BossWardenCore.Start() 에서 전체 주입 |
| `BossWardenCore.cs` | 루트 통합 관리 — 그로기/코어 활성/딜페이즈/페이즈전환/최종봉인/처치 | RequireComponent 5종. DEBUG ContextMenu 포함 |
| `BossWardenAI.cs` | 탑뷰 8방향 AI — Idle/Chase/Warning/Active/Recovery + 2페이즈 강화 | OnFacingChanged(Vector2) 이벤트로 방향 외부 위임 |
| `BossWardenFeedback.cs` | 상태별 DOTween 색상/연출 전담. SpriteRenderer 점멸 포함 | SetUpdate(true) 전체 적용 — 슬로우 중 연출 유지 |
| `BossWardenArmPart.cs` | 팔 부위 — `PlayerAttackHitboxManager.OnHit` 구독으로 피격 감지 → SealGaugeComponent.AddGauge() | 히트 점멸 / Recovery 배율 / UpdateBaseColor() 포함 |
| `BossWardenSealExecutor.cs` | S키 봉인 집행(3단계) — 부위봉인 / 코어해제 / 최종봉인 | 슬로우 Time.timeScale 제어. UnscaledDeltaTime 홀드 측정 |
| `BossWardenCoreSealGauge.cs` | 코어 봉인도 누적 전담 — 딜 페이즈 중만 활성. ActivateGauge(bool) | PlayerAttackHitboxManager.OnHit 구독 방식 (ArmPart 와 동일) |
| `BossWardenShockwave.cs` | 딜 페이즈 종료 시 BossWardenCore 에서 직접 호출 — OverlapCircleNonAlloc + 넉백 코루틴 | WaitForFixedUpdate 후 velocity 설정 (POC07 v1.3 교훈 적용) |
| `BossWardenAttackRange.cs` | 공격 예고 범위 / 봉인 집행 범위 / 코어 해제 범위 표시 전담 | HideAll() — 그로기/딜페이즈 진입 시 일괄 정리 |

### 패턴 스크립트

| 파일명 | 역할 | 연결 부위 | 특이사항 |
|---|---|---|---|
| `BossPattern_Charge.cs` | 돌진 / 2페이즈 Recovery 스킵 + Slam 연계 | 오른팔 | Interrupt 오버라이드 — linearVelocity 즉시 0 |
| `BossPattern_Slam.cs` | 내려치기 / 2페이즈 2연속 (0.5초 간격) | 왼팔 | Warning 시 플레이어 위치 스냅 (이후 고정) |
| `BossPattern_Sweep.cs` | 360° 회전 스윕 / 2페이즈 2회전 | 왼팔 | DORotate FastBeyond360 + 매 프레임 UpdateSweepDiscPosition |
| `BossPattern_GuardBreak.cs` | 가드 자세 → 정면 강타 / 2페이즈 가드 단축 | 오른팔 | `IsGuarding` public — 가드 중 봉인도 누적 무효 신호 |
| `BossPattern_RageCharge.cs` | 3연 돌진 (2페이즈 전용) | 없음 | `_isPhase2Only = true` 자체 설정. 0.3초 간격 순차 예고선 |

### 플레이어 피격 처리 — 현재 구현 상태

> 보스 패턴이 플레이어를 감지해도 실제 피격 처리 컴포넌트가 아직 구현되지 않았다.
> 각 패턴의 OverlapXX 에서 플레이어 감지 시 현재는 `Debug.Log` 만 출력한다.
> STEP 09 테스트 시 플레이어 피격 처리는 다음 방식으로 임시 처리한다.

```
임시 처리 방안 (STEP 09):
  패턴 OverlapXX 감지 시 → Debug.Log("[패턴명] 플레이어 피격!")
  플레이어 넉백은 BossWardenShockwave 의 넉백 코루틴 구조를 참조하여
  별도 테스트 스크립트로 임시 구현 가능.
  
정식 구현 예정 (이후 단계):
  PlayerHealth.cs — 체력 / 피격 무적 / 넉백 처리
  각 패턴 Active 구간에서 OnHitPlayer 이벤트 발행 → PlayerHealth.TakeDamage() 호출
```

---

## 13. DataSO 수치표

| 변수명 | 값 | 설명 |
|---|---|---|
| `Phase1CoreSealTarget` | 250f | 1페이즈 코어 봉인도 목표 (내부) |
| `Phase2CoreSealTarget` | 500f | 2페이즈 코어 봉인도 목표 (내부) |
| `CoreSealGaugeMax` | 500f | 코어 봉인도 최대치 (내부) |
| `ArmSealGaugeMax` | 200f | 팔 부위 봉인도 최대치 (내부) |
| `DilPhaseDuration` | 10.0f | 딜 페이즈 지속 시간 (초) |
| `GroggyDuration` | 5.0f | 그로기 지속 시간 (초) |
| `CoreUnlockHoldTime` | 3.0f | 코어 해제 S키 홀드 시간 |
| `FinalSealHoldTime` | 2.0f | 최종 봉인 S키 홀드 시간 |
| `SealExecutionHoldTime` | 1.5f | 부위 봉인 집행 홀드 시간 |
| `CoreUnlockRange` | 1.5f | 코어 해제 접근 감지 반경 |
| `SealExecutionRange` | 1.5f | 봉인 집행 접근 감지 반경 |
| `ShockwaveRadius` | 6.0f | 충격파 반경 |
| `ShockwaveKnockbackForce` | 12.0f | 충격파 넉백 강도 |
| `MoveSpeed` | 3.5f | 1페이즈 이동 속도 |
| `Phase2MoveSpeed` | 4.5f | 2페이즈 이동 속도 |
| `PatternRange` | 5.0f | 패턴 발동 감지 범위 |
| `ChargeSpeed` | 12.0f | 1페이즈 돌진 속도 |
| `Phase2ChargeSpeed` | 16.0f | 2페이즈 돌진 속도 |
| `ChargeDistance` | 8.0f | 돌진 거리 |
| `SlamRadius` | 2.5f | 내려치기 히트 반경 |
| `SweepRadius` | 2.5f | 스윕 히트 반경 |
| `RageChargeSpeed` | 18.0f | 3연 돌진 속도 |
| `DilPhaseSlowTimeScale` | 0.3f | 코어 해제 슬로우 배율 |
| `FinalSealSlowTimeScale` | 0.1f | 최종 봉인 슬로우 배율 |
| `RecoveryVulnMultiplier` | 1.5f | Recovery 구간 봉인도 누적 배율 |
| `SealResist1` | 1.0f | 1회 봉인 저항 배율 |
| `SealResist2` | 0.8f | 2회 봉인 저항 배율 |
| `SealResist3` | 0.6f | 3회 봉인 저항 배율 |
| `SealResist4` | 0.4f | 4회+ 봉인 저항 배율 |

---

## 14. Hierarchy 구조

### 프리미티브 구현 전제

> Sprite 에셋 없음. 모든 오브젝트는 Unity 기본 프리미티브로 생성한다.

**[README #1, #2, #10 시점 정의]**

이 게임의 시점은 **완전 수직 정수리 탑뷰가 아니다.**  
이동/판정 구조는 탑다운이지만, 캐릭터·보스의 **비주얼 표현은 비스듬한 탑뷰 / 쿼터뷰**에 가깝다.  
캐릭터의 상체, 팔, 무기, 부위 방향성이 보여야 한다.  
순수 원형 탑뷰 스프라이트가 아니라, **2D 사이드뷰처럼 실루엣이 읽히는** 세로형 프리미티브를 사용한다.

이 시점을 사용하는 이유:
- 열쇠 무기의 실루엣을 명확하게 보여주기 위해
- 정면 / 측면 / 후방 공략을 시각적으로 구분하기 위해 (GuardBreak 정면 가드 → 측면 공략)
- 보스의 팔, 머리, 부위를 식별하기 위해
- 봉인 집행 시 열쇠를 꽂는 연출을 보여주기 위해

| 오브젝트 | 프리미티브 형태 | 비율 / 생성 방법 | 비고 |
|---|---|---|---|
| Boss_Warden 본체 | **세로 직사각형** | Scale (0.8, 1.4, 1) | 쿼터뷰 상체 실루엣 — 세로로 긴 형태 |
| LeftArm | **가로 직사각형** | Scale (0.7, 0.25, 1) | 왼쪽 팔 — 가로로 뻗은 형태 |
| RightArm | **가로 직사각형** | Scale (0.7, 0.25, 1) | 오른쪽 팔 |
| Core | **소형 원형** | Scale (0.35, 0.35, 1) | 등 중앙 발광체 |
| HurtBox | 본체와 동일 크기 Collider만 | SpriteRenderer 없음 | 충돌 감지 전용 |
| DiscSlam / DiscSweep | **원형** | radius × 2 Scale | 바닥 표시 — SortingLayer = Ground |
| DiscGuardBreak | **직사각형** | guardBreakWarningSize | 바닥 표시 — SortingLayer = Ground |
| 예고선 | **LineRenderer** | width 0.08 | 별도 프리미티브 불필요 |

**SortingLayer 설정 기준:**

| SortingLayer | 대상 |
|---|---|
| `Ground` | 공격 예고 디스크 (바닥 표시 — 캐릭터 아래) |
| `Enemy` | 보스 본체 / 부위 / 코어 |
| `Effect` | 봉인 범위 점선 LineRenderer |

모든 SpriteRenderer 는 Sprite 를 Unity 내장 `Knob` 으로 설정한다.  
색상은 SpriteRenderer.color 로만 제어하며, 모든 시각 피드백은 DOTween DOColor 로 처리한다.

---

### 레이어 정의

> Unity Project Settings → Tags and Layers 에서 사전 등록 필요

| Layer 이름 | 용도 | 설정 오브젝트 |
|---|---|---|
| `Enemy` | 보스 본체 / 부위 콜라이더 — 플레이어 공격 감지 대상 | Boss_Warden, LeftArm, RightArm, Core |
| `Player` | 플레이어 본체 — 보스 패턴 히트박스 감지 대상 | Player 오브젝트 |
| `BossHitbox` | 보스 패턴 공격 히트박스 — 플레이어 피격 판정 | HitboxCollider 오브젝트들 |
| `Default` | 예고 범위 표시 오브젝트 (충돌 불필요) | AttackRangeVisuals 하위 전체 |

**Physics2D Layer Collision Matrix 설정:**

| | Enemy | Player | BossHitbox |
|---|---|---|---|
| Enemy | ❌ | ❌ | ❌ |
| Player | ❌ | ❌ | ✅ |
| BossHitbox | ❌ | ✅ | ❌ |

---

### 전체 계층 구조

```
BossRoot                                    Layer: Default
└─ Boss_Warden                              Layer: Enemy
   │
   │  ── 컴포넌트 목록 ──────────────────────────────────────
   │  [Rigidbody2D]
   │      GravityScale = 0
   │      FreezeRotation Z = true
   │      CollisionDetection = Continuous
   │
   │  [CapsuleCollider2D]                   ← 본체 물리 충돌 (isTrigger = false)
   │      Direction = Vertical
   │      Size = (0.8, 1.2)
   │
   │  [SpriteRenderer]                      ← 프리미티브 Circle/Capsule
   │      Sprite = Unity 내장 Knob
   │      Color = #888888 (기본 회색)
   │      SortingLayer = Enemy
   │
   │  [BossWardenCore]
   │      _data            → BossWardenDataSO (Inspector 연결)
   │      _armL            → LeftArm.BossWardenArmPart (Inspector 연결)
   │      _armR            → RightArm.BossWardenArmPart (Inspector 연결)
   │      _coreObject      → Core GameObject (Inspector 연결)
   │      _coreSealGauge   → Core.BossWardenCoreSealGauge (Inspector 연결)
   │      _shockwave       → BossWardenShockwave (GetComponent — 동일 오브젝트)
   │
   │  [BossWardenAI]
   │      _data            → BossWardenDataSO (Inspector 연결)
   │      _core            → BossWardenCore (GetComponent)
   │      _rigid2D         → Rigidbody2D (GetComponent)
   │      _spriteRenderer  → SpriteRenderer (GetComponent)
   │      _playerTransform → Player Transform (FindObjectsByType or Inspector)
   │      _patterns        → BossPattern 리스트 (Inspector 연결)
   │
   │  [BossWardenFeedback]
   │      _bodyRenderer    → SpriteRenderer (GetComponent)
   │      _armL            → LeftArm.SpriteRenderer (Inspector 연결)
   │      _armR            → RightArm.SpriteRenderer (Inspector 연결)
   │      _coreRenderer    → Core.SpriteRenderer (Inspector 연결)
   │      _ai              → BossWardenAI (GetComponent)
   │      _core            → BossWardenCore (GetComponent)
   │
   │  [BossWardenSealExecutor]
   │      _data            → BossWardenDataSO (Inspector 연결)
   │      _core            → BossWardenCore (GetComponent)
   │      _playerTransform → Player Transform (Inspector 연결)
   │      _playerInput     → PlayerInputHandler (FindObjectsByType or Inspector)
   │      _armL            → LeftArm.BossWardenArmPart (Inspector 연결)
   │      _armR            → RightArm.BossWardenArmPart (Inspector 연결)
   │      _coreObject      → Core GameObject (Inspector 연결)
   │      _coreSealGauge   → Core.BossWardenCoreSealGauge (Inspector 연결)
   │
   │  [BossWardenShockwave]
   │      _data            → BossWardenDataSO (Inspector 연결)
   │      _playerLayer     → Player 레이어 마스크 (Inspector 설정)
   │      _discRenderer    → ShockwaveDisc.SpriteRenderer (Inspector 연결)
   │      _cameraTransform → Main Camera Transform (선택, 미연결 시 셰이크 스킵)
   │
   │  [BossWardenAttackRange]
   │      _chargeLineRenderer  → AttackRangeVisuals/ChargeLine.LineRenderer (Inspector)
   │      _discSlam            → AttackRangeVisuals/DiscSlam.SpriteRenderer (Inspector)
   │      _discSweep           → AttackRangeVisuals/DiscSweep.SpriteRenderer (Inspector)
   │      _discGuardBreak      → AttackRangeVisuals/DiscGuardBreak.SpriteRenderer (Inspector)
   │  ─────────────────────────────────────────────────────
   │
   ├─ LeftArm                              Layer: Enemy
   │   │  Position: (-0.6, 0.0, 0)         ← 본체 왼쪽 오프셋
   │   │
   │   │  [SpriteRenderer]
   │   │      Sprite = Unity 내장 Knob (Square)
   │   │      Color = #AAAAAA (봉인도 0% 기본 밝은 회색)
   │   │      SortingLayer = Enemy
   │   │
   │   │  [CapsuleCollider2D]              isTrigger = true / Layer = Enemy
   │   │      Size = (0.4, 0.8)
   │   │
   │   │  [BossWardenArmPart]
   │   │      _partType        = LeftArm (enum)
   │   │      _spriteRenderer  → SpriteRenderer (GetComponent)
   │   │      _sealGauge       → SealGaugeComponent (GetComponent)
   │   │
   │   └─ [SealGaugeComponent]
   │          _maxGauge        = 200f
   │          _resistValues    = [1.0, 0.8, 0.6, 0.4]
   │
   ├─ RightArm                             Layer: Enemy
   │   │  Position: (0.6, 0.0, 0)          ← 본체 오른쪽 오프셋
   │   │
   │   │  [SpriteRenderer]
   │   │      Sprite = Unity 내장 Knob (Square)
   │   │      Color = #AAAAAA
   │   │      SortingLayer = Enemy
   │   │
   │   │  [CapsuleCollider2D]              isTrigger = true / Layer = Enemy
   │   │      Size = (0.4, 0.8)
   │   │
   │   │  [BossWardenArmPart]
   │   │      _partType        = RightArm (enum)
   │   │      _spriteRenderer  → SpriteRenderer (GetComponent)
   │   │      _sealGauge       → SealGaugeComponent (GetComponent)
   │   │
   │   └─ [SealGaugeComponent]
   │          _maxGauge        = 200f
   │          _resistValues    = [1.0, 0.8, 0.6, 0.4]
   │
   ├─ Core                                 Layer: Enemy
   │   │  Position: (0.0, -0.5, 0)         ← 본체 등 중앙 (탑뷰 기준 하단 오프셋)
   │   │  SetActive = false                 ← 기본 비활성, 그로기 진입 시 활성
   │   │
   │   │  [SpriteRenderer]
   │   │      Sprite = Unity 내장 Knob (Circle)
   │   │      Color = #FFEE00 (노랑)
   │   │      SortingLayer = Enemy
   │   │      Scale = (0.4, 0.4, 1)        ← 소형 원형
   │   │
   │   │  [CircleCollider2D]               isTrigger = true / Layer = Enemy
   │   │      Radius = 0.3
   │   │
   │   └─ [BossWardenCoreSealGauge]
   │          _maxGauge        = 500f
   │          _spriteRenderer  → SpriteRenderer (GetComponent)
   │          _core            → BossWardenCore (GetComponentInParent)
   │
   ├─ HurtBox                              Layer: Enemy
   │      Position: (0.0, 0.0, 0)          ← 본체와 동일 위치
   │
   │      [CapsuleCollider2D]              isTrigger = true / Layer = Enemy
   │          Size = (0.8, 1.2)            ← 본체 CapsuleCollider2D 와 동일 크기
   │          ※ SpriteRenderer 없음 — 충돌 감지 전용
   │          ※ 플레이어 PlayerAttackHitboxManager 의 Enemy 레이어 감지 대상
   │
   ├─ AttackRangeVisuals                   Layer: Default (충돌 없음)
   │   │  ※ 모든 자식 오브젝트는 기본 SetActive = false
   │   │  ※ 각 패턴 Warning 시작 시 BossWardenAttackRange 가 활성화
   │   │  ※ SortingLayer = Ground (캐릭터 아래 바닥에 표시)
   │   │
   │   ├─ ChargeLine                       LineRenderer 전용 오브젝트
   │   │      [LineRenderer]
   │   │          Color = #FF000066 (붉은 반투명)
   │   │          Width = 0.08
   │   │          UseWorldSpace = true
   │   │
   │   ├─ DiscSlam0                        Slam 원형 예고 디스크 (1페이즈 단일 / 2페이즈 첫 번째)
   │   │      [SpriteRenderer]
   │   │          Sprite = Unity 내장 Knob (Circle)
   │   │          Color = #FF000066 (붉은 반투명)
   │   │          Scale = (6.0, 6.0, 1)    ← 반경 3.0 units 시각화
   │   │          SortingLayer = Ground
   │   │
   │   ├─ DiscSlam1                        Slam 원형 예고 디스크 (2페이즈 두 번째)
   │   │      [SpriteRenderer]
   │   │          Sprite = Unity 내장 Knob (Circle)
   │   │          Color = #FF000066
   │   │          Scale = (6.0, 6.0, 1)
   │   │          SortingLayer = Ground
   │   │
   │   ├─ DiscSweep                        Sweep 원형 예고 디스크 (회전)
   │   │      [SpriteRenderer]
   │   │          Sprite = Unity 내장 Knob (Circle)
   │   │          Color = #FF000066
   │   │          Scale = (7.0, 7.0, 1)    ← 반경 3.5 units
   │   │          SortingLayer = Ground
   │   │
   │   ├─ DiscGuardBreak                   GuardBreak 직사각형 예고
   │   │      [SpriteRenderer]
   │   │          Sprite = Unity 내장 Knob (Square)
   │   │          Color = #FF000066
   │   │          Scale = (1.5, 1.0, 1)
   │   │          SortingLayer = Ground
   │   │
   │   ├─ RageChargeLine0                  RageCharge 예고선 1번 (가장 밝음)
   │   │      [LineRenderer] Width = 0.08 / UseWorldSpace = true
   │   │
   │   ├─ RageChargeLine1                  RageCharge 예고선 2번
   │   │      [LineRenderer] Width = 0.08 / UseWorldSpace = true
   │   │
   │   ├─ RageChargeLine2                  RageCharge 예고선 3번
   │   │      [LineRenderer] Width = 0.08 / UseWorldSpace = true
   │   │
   │   ├─ SealRangeCircle                  봉인 집행 가능 범위 점선 원
   │   │      [LineRenderer]
   │   │          Color = #0088FF (파랑)
   │   │          Width = 0.05 / Loop = true
   │   │          SortingLayer = Effect
   │   │
   │   └─ CoreRangeCircle                  코어 해제 가능 범위 점선 원
   │          [LineRenderer]
   │              Color = #FFEE00 (노랑)
   │              Width = 0.05 / Loop = true
   │              SortingLayer = Effect
   │
   ├─ ShockwaveDisc                        충격파 확장 디스크 (BossWardenShockwave 연출용)
   │      Layer: Default
   │      [SpriteRenderer]
   │          Sprite = Unity 내장 Knob (Circle)
   │          Color = #FF333366 (붉은 반투명)
   │          SetActive = false           ← 기본 비활성. 충격파 발동 시 DOScale 확장
   │          SortingLayer = Ground
   │      ※ BossWardenShockwave._discRenderer 에 Inspector 연결
   │
   └─ Patterns                             Layer: Default — SpriteRenderer / Collider 없음
         ※ 히트박스는 각 패턴이 런타임에 Physics2D.OverlapXX 로 직접 처리
         ※ Awake 에서 BossWardenAI._patterns 리스트에 자동 등록

         [BossPattern_Charge]
             _ai              → BossWardenAI (GetComponentInParent)
             _data            → BossWardenDataSO (Inspector 연결)
             _attackRange     → BossWardenAttackRange (GetComponentInParent)
             _linkedArmPart   → RightArm.BossWardenArmPart (Inspector 연결)
             _playerLayer     = Player 레이어 마스크

         [BossPattern_Slam]
             _ai              → BossWardenAI (GetComponentInParent)
             _data            → BossWardenDataSO (Inspector 연결)
             _attackRange     → BossWardenAttackRange (GetComponentInParent)
             _linkedArmPart   → LeftArm.BossWardenArmPart (Inspector 연결)
             _playerLayer     = Player 레이어 마스크

         [BossPattern_Sweep]
             _ai              → BossWardenAI (GetComponentInParent)
             _data            → BossWardenDataSO (Inspector 연결)
             _attackRange     → BossWardenAttackRange (GetComponentInParent)
             _linkedArmPart   → LeftArm.BossWardenArmPart (Inspector 연결)
             _playerLayer     = Player 레이어 마스크

         [BossPattern_GuardBreak]
             _ai              → BossWardenAI (GetComponentInParent)
             _data            → BossWardenDataSO (Inspector 연결)
             _attackRange     → BossWardenAttackRange (GetComponentInParent)
             _linkedArmPart   → RightArm.BossWardenArmPart (Inspector 연결)
             _playerLayer     = Player 레이어 마스크

         [BossPattern_RageCharge]           ← 2페이즈 진입 시 CanExecute = true 로 전환
             _ai              → BossWardenAI (GetComponentInParent)
             _data            → BossWardenDataSO (Inspector 연결)
             _attackRange     → BossWardenAttackRange (GetComponentInParent)
             _linkedArmPart   = null (독립 패턴)
             _playerLayer     = Player 레이어 마스크
             _isPhase2Only    = true          ← 1페이즈에서 CanExecute 강제 false
```

---

### 컴포넌트 참조 방식 규칙

| 참조 방식 | 사용 조건 | 예시 |
|---|---|---|
| `GetComponent<T>()` | 동일 오브젝트에 부착된 컴포넌트 | `_rigid2D = GetComponent<Rigidbody2D>()` |
| `GetComponentInParent<T>()` | 부모 오브젝트에 부착된 컴포넌트 | 패턴 스크립트 → BossWardenAI |
| `GetComponentInChildren<T>()` | 자식 오브젝트에 부착된 컴포넌트 | BossWardenCore → ArmPart (비권장, Inspector 직접 연결 우선) |
| `Inspector 직접 연결` | 다른 계층의 컴포넌트 참조 | BossWardenCore._armL, BossWardenSealExecutor._playerTransform |
| `FindObjectsByType<T>()` | 씬 전체에서 단 1개 존재하는 컴포넌트 | PlayerInputHandler, PlayerMoveController |

**금지 사항:**
- `GameObject.Find()` 사용 금지 (문자열 의존, 리팩토링 취약)
- `FindObjectOfType<T>()` 반복 호출 금지 (성능 문제) — Awake/Start 에서 1회만 캐싱

---

## 15. 코드 구현 순서 및 진척 단계

> 단계별 구현 원칙:
> - 각 단계는 독립 테스트 가능해야 한다
> - 이전 단계가 완료되지 않으면 다음 단계로 넘어가지 않는다
> - 프리미티브 + 색상만으로 즉시 확인 가능한 구조 유지

---

### STEP 01 — DataSO + 기본 구조 준비

**목표:** 수치 데이터와 공용 베이스 클래스 준비

| 파일 | 작업 내용 | 상태 |
|---|---|---|
| `BossWardenDataSO.cs` | 수치 SO 전체 필드 정의 | ✅ 구현 완료 |
| `SealGaugeComponent.cs` | 봉인도 누적 / 100% 이벤트 / 저항 배율 | ✅ 구현 완료 (v1.1 버그수정) |
| `BossPatternBase.cs` | Warning/Active/Recovery 추상 구조 + Interrupt | ✅ 구현 완료 (v1.1 버그수정) |
| `BossWardenArmPart.cs` | 팔 부위 — 피격 감지 + SealGauge 연동 + 히트 점멸 | ✅ 구현 완료 (v1.1 버그수정) |

**완료 조건:** DataSO Inspector에서 수치 확인 가능 / SealGaugeComponent 단독 테스트 통과

### STEP 01 버그 수정 내역 (v1.1)

| 번호 | 파일 | 문제 | 수정 내용 |
|---|---|---|---|
| 🔴 버그1 | `SealGaugeComponent.cs` | `ForceRelease` 기본값 `resetSealCount: true` → 저항 무의미 | 기본값 `false`로 변경 + 설계 원칙 주석 추가 |
| 🔴 버그2 | `BossPatternBase.cs` | `ExecuteRecovery`에서 `OnPatternEnd` 중복 발행 | `_isInterrupted` 체크 후 발행하도록 수정 |
| 🔴 버그3 | `BossWardenArmPart.cs` | `Initialize()` 이벤트 중복 구독 위험 | `-=` 먼저 실행 후 `+=` 구독으로 수정 |
| 🔴 버그4 | `BossWardenArmPart.cs` | `OnTriggerEnter2D` 방식이 POC08 플레이어 공격 시스템과 충돌 | `Start()`에서 `PlayerAttackHitboxManager.OnHit` 구독 방식으로 전환. `HandlePlayerHit(col, sealAmount)`에서 `_ownCollider` 대조 후 처리 |
| 🟡 경고1 | `SealGaugeComponent.cs` | `ForceRelease` vs `ResetGaugeOnly` 역할 구분 불명확 | 각 함수 주석에 사용 케이스 명확히 구분 기재 |
| 🟡 경고2 | `BossPatternBase.cs` | `SetActive=false` 시 코루틴 중단 동작 미명시 | Summary 주석에 "Patterns 오브젝트는 항상 SetActive=true 유지 필요" 명시 |

### STEP 01 구현 노트

```
BossWardenDataSO.cs
  - POC07 TestBossDataSO 구조 기반으로 탑뷰 봉인 시스템 전면 재설계
  - HP 개념 제거 (봉인도 100% → 최종 봉인 → 처치)
  - S키 봉인 집행 / 코어 해제 / 최종 봉인 수치 추가
  - 봉인 저항 배율 배열 (GetSealResistMultiplier 유틸 메서드 포함)
  - 부위/코어/예고범위 전용 색상 필드 (DOTween 색상 전환에 직접 사용)
  - ArmGaugeToUIPercent / CoreGaugeToUIPercent 변환 유틸 포함

SealGaugeComponent.cs (v1.1)
  - POC07 TestBossArmPart 이진 상태 → 연속 수치(0~max) 구조로 재설계
  - 봉인도 단계(0/25/50/75/100%) 변화 시 OnStageChanged 이벤트 발행
  - OnSealReady / OnSealed / OnReleased / OnGaugeChanged 이벤트 4종
  - 봉인 저항 배율 자동 적용 (AddGauge 내부에서 처리)
  - [수정] ForceRelease 기본값 false (저항 횟수 유지)
  - ForceRelease vs ResetGaugeOnly 역할 명확히 구분

BossPatternBase.cs (v1.1)
  - POC07 TestBossPatternBase 구조 계승
  - 봉인 투사체 감지 로직 제거 (투사체 시스템 없음)
  - _linkedArmPart → IsAvailable 체크 (연결 팔 봉인 시 패턴 비활성)
  - _isPhase2Only + UnlockPhase2() — 2페이즈 전용 패턴 지원
  - WaitForPattern() — 중단 체크만 (투사체 감지 없음)
  - [수정] ExecuteRecovery OnPatternEnd 중복 발행 방지
  - [수정] SetActive=false 코루틴 중단 주의사항 주석 추가

BossWardenArmPart.cs (v1.1)
  - [수정] OnTriggerEnter2D 제거 → PlayerAttackHitboxManager.OnHit 구독 방식으로 전환
  - Start()에서 FindObjectsByType으로 HitboxManager 탐색 후 OnHit 구독
  - HandlePlayerHit: hitCol == _ownCollider 대조로 이 부위 적중 확인
  - [수정] Initialize() 이벤트 중복 구독 방지 (-= 먼저 후 +=)
  - ForceRelease 기본값 false로 통일 (SealGaugeComponent 와 일치)
  - 히트 스탑 점멸: DOColor 흰색 → 기본 색상 (SetUpdate=true)
```

---

### STEP 02 — AI 이동 + 상태 머신

**목표:** Warden이 플레이어를 8방향으로 추적하고 패턴 범위에 진입하면 정지

| 파일 | 작업 내용 | 상태 |
|---|---|---|
| `BossWardenAI.cs` | Idle/Chase 상태 + 탑뷰 8방향 플레이어 추적 이동 | ✅ 구현 완료 |
| `BossWardenAI.cs` | Warning/Active/Recovery 패턴 실행 코루틴 흐름 | ✅ 구현 완료 |
| `BossWardenAI.cs` | _isStopped 플래그 / Recovery 취약 구간 팔 전달 / 2페이즈 속도 갱신 | ✅ 구현 완료 |
| `BossWardenFeedback.cs` | 상태별 색상 전환 + 봉인도 단계 색상 + 그로기/딜페이즈/처치 연출 | ✅ 구현 완료 |

**완료 조건:** Warden이 플레이어를 추적하다 범위 내 진입 시 정지하는 것 확인

### STEP 02 구현 노트

```
BossWardenAI.cs  (v1.0)
  - POC07 TestBossAI 구조 계승 — 5상태 (Idle/Chase/Warning/Active/Recovery)
  - 횡스크롤 X축 이동 → 탑뷰 Rigidbody2D.linearVelocity 8방향 이동으로 전환
  - flipX 제거 → OnFacingChanged(Vector2) 이벤트로 방향 정보 외부 위임
  - 이벤트 구독: SubscribeCoreEvents / SubscribePatternEvents 함수 분리
    → 모든 구독부 -= 먼저 후 += (중복 구독 원천 차단)
  - ExecutePattern(): _isStopped 체크로 각 단계 진입 전 중단 감지
  - Recovery 구간: SetArmsRecoveryVuln(true/false) — 봉인도 배율 자동 적용
  - InterruptCurrentPattern(): Interrupt() → StopCoroutine() 순서 명시
  - HandlePhaseChanged(2): 이동 속도 갱신 + 전체 패턴 UnlockPhase2()
  - OnDrawGizmosSelected: 패턴 범위 / 방향 벡터 시각화

BossWardenFeedback.cs  (v1.0)
  - POC07 TestBossFeedBack 구조 계승 + 탑뷰 재설계
  - 구독 대상: AI OnStateChanged / Core 6개 이벤트 / 팔 SealGauge 6개 이벤트
  - 모든 구독부 -= 먼저 후 += (중복 구독 원천 차단)
  - 본체 상태별 색상: Idle(회색) / Warning(주황 Pulse) / Active(흰 순간) / Recovery(붉은 페이드)
  - 그로기: 노란 빠른 Pulse / 딜페이즈: 밝은 주황 Pulse / 처치: 검정 페이드 + Scale 0
  - 팔 봉인도 단계(0~4): _armStageColors 배열로 DOColor 전환
  - Stage 4(100%): 파랑 빠른 Yoyo Pulse (집행 가능 강조)
  - 봉인 완료: 파랑 고정 DOColor
  - UpdateBaseColor() 동기화: 색상 변경 시 ArmPart 히트 점멸 복귀 색상 동기화
  - SetUpdate(true): 모든 Tween 에 적용 — TimeScale 영향 무관 연출 유지
```

---

### STEP 03 — 패턴 + 예고 범위 구현

**목표:** 4개 기본 패턴 + 공격 예고 범위 시각화

| 파일 | 작업 내용 | 상태 |
|---|---|---|
| `BossWardenAttackRange.cs` | LineRenderer / Disc 표시 제어 공용 API | ✅ 구현 완료 |
| `BossPattern_Charge.cs` | Warning(예고선) + Active(돌진) + Recovery | ✅ 구현 완료 |
| `BossPattern_Slam.cs` | Warning(원형 디스크) + Active(히트) + Recovery | ✅ 구현 완료 |
| `BossPattern_Sweep.cs` | Warning(반원 디스크) + Active(회전 히트) + Recovery | ✅ 구현 완료 |
| `BossPattern_GuardBreak.cs` | Warning(가드 자세 → 예고) + Active(강타) + Recovery | ✅ 구현 완료 |
| `BossPattern_RageCharge.cs` | 3연 돌진 (2페이즈 전용) Warning 순차 예고선 | ✅ 구현 완료 |

**완료 조건:** 4개 패턴이 예고 범위 표시 → 실제 히트박스 순서로 정상 동작 확인

### STEP 03 구현 노트

```
BossWardenAttackRange.cs  (v1.0)
  - 공격 예고 범위 표시 전담 분리 — 패턴 스크립트가 직접 그리지 않음
  - ShowChargeLine: LineRenderer 직선 예고 (방향 × 길이)
  - ShowSlamDisc: SpriteRenderer 원형 디스크 월드좌표 이동 (SortingLayer=Ground)
  - FlashAndHideSlamDisc: Active 히트 시 흰 플래시 후 비활성 DOTween
  - ShowSweepDisc + UpdateSweepDiscPosition: 보스와 함께 회전
  - ShowGuardBreakDisc: 방향 각도로 직사각형 디스크 Z회전
  - ShowRageChargeLine: 인덱스별 순차 밝기 감소 (1.0 / 0.75 / 0.5)
  - ShowSealRange / ShowCoreRange: DrawDashedCircle (32점 LineRenderer)
  - HideAll(): Awake 초기화 + 그로기/DilPhase 진입 시 일괄 숨김
  - 쿼터뷰 고려: SortingLayer=Ground 권장 (캐릭터 아래에 예고 디스크 표시)

BossPattern_Charge.cs  (v1.0)
  - Warning: FacingDir 방향 고정 + ShowChargeLine
  - Active: linearVelocity 직접 제어 + OverlapBox 히트박스
  - Recovery: DOShakePosition + triggerGroggyOnRecovery = true
  - 2페이즈: phase2ChargeSpeed + Recovery 스킵 (Slam 연계)
  - Interrupt 오버라이드: linearVelocity 즉시 0 + HideChargeLine

BossPattern_Slam.cs  (v1.0)
  - Warning: 플레이어 위치 스냅 + ShowSlamDisc + DOLocalMoveY 팔 들어올림
  - Active: ExecuteSlam 코루틴 (팔 내려침 + FlashAndHide + OverlapCircle)
  - 2페이즈: 0.5초 후 두 번째 디스크 + 두 번째 내려치기
  - triggerGroggyOnRecovery = false (Charge와 역할 분리)

BossPattern_Sweep.cs  (v1.0)
  - Warning: ShowSweepDisc (보스 위치)
  - Active: DORotate FastBeyond360 + 매 프레임 UpdateSweepDiscPosition + OverlapCircle
  - 2페이즈: 2회전 + sweepWarningRadius 확장
  - Interrupt 오버라이드: Kill + HideSweepDisc

BossPattern_GuardBreak.cs  (v1.0)
  - Warning 전반부: IsGuarding = true (정면 봉인도 무효 신호)
  - Warning 후반부: ShowGuardBreakDisc (방향 계산 직사각형)
  - Active: DOLocalMove 전방 타격 + OverlapBox
  - triggerGroggyOnRecovery = true (Recovery 완료 시 그로기 유발)
  - 2페이즈: guardDuration 0.5초 + 히트박스 확장

BossPattern_RageCharge.cs  (v1.0)
  - _isPhase2Only = true (1페이즈에서 CanExecute 차단)
  - Warning: 붉은 Pulse DOColor + 0.3초 간격 3개 예고선 순차 표시
  - Active: 3회 순차 돌진 (direction 배열 유지) + 간격 0.2초
  - Recovery: DOShakePosition 피로감 연출
  - Interrupt: Pulse Kill + linearVelocity 즉시 0
```

---

### STEP 04 — 부위 봉인도 + 봉인 집행

**목표:** 공격 적중 시 부위 봉인도 누적 + S키 봉인 집행 완전 구현

| 파일 | 작업 내용 | 상태 |
|---|---|---|
| `BossWardenArmPart.cs` | 피격 수신 → SealGaugeComponent 전달 | ✅ STEP01 완료 |
| `SealGaugeComponent.cs` | 봉인도 단계별 색상 전환 이벤트 → BossWardenFeedback 처리 | ✅ STEP01 완료 |
| `BossWardenSealExecutor.cs` | S키 봉인 집행 / 코어 해제 / 최종 봉인 3단계 구조 | ✅ 구현 완료 |
| `BossWardenCoreSealGauge.cs` | 코어 봉인도 누적 + 페이즈별 목표 도달 이벤트 | ✅ 구현 완료 |
| `BossWardenAttackRange.cs` | 봉인 가능 범위 점선 원 + 코어 해제 범위 점선 원 | ✅ STEP03 완료 |

**완료 조건:** 공격 → 봉인도 색상 변화 → 100% 도달 → S키 집행 → 부위 파랑 고정 확인

### STEP 04 구현 노트

```
BossWardenSealExecutor.cs  (v1.0)
  - POC07 TestBossExecution (v1.1) 구조 계승
  - 핵심 차이: A키 자동이동 제거 → S키 홀드 + 범위 내 접근 방식
  - 3단계 집행 구조:
      ① PartSeal  : SealGauge.IsSealReady + sealExecutionRange 내 + S키 홀드
      ② CoreUnlock: _isCoreUnlockActive + coreUnlockRange 내 + S키 홀드
      ③ FinalSeal : _isFinalSealActive + coreUnlockRange 내 + S키 홀드
  - 우선순위: FinalSeal > CoreUnlock > PartSeal
  - 슬로우 모션: CoreUnlock = 0.3 / FinalSeal = 0.1 (Time.timeScale)
  - 홀드 시간: Time.unscaledDeltaTime 사용 (슬로우 중에도 일정 속도)
  - 중단 조건: S키 해제 or 범위 이탈 → 즉시 취소 + TimeScale 복구
  - 재집행 방지: _mustReleaseKey (S키 뗀 후 재누름 확인)
  - BlockAll / UnblockAll: 집행 중 플레이어 이동/대시/공격 차단
  - OnDestroy: Time.timeScale = 1.0f 보호 처리
  - 이벤트: OnPartSealed / OnCoreUnlocked / OnFinalSealCompleted

BossWardenCoreSealGauge.cs  (v1.0)
  - 딜 페이즈 전용 코어 봉인도 누적 컴포넌트
  - PlayerAttackHitboxManager.OnHit 구독 방식 (BossWardenArmPart 와 동일)
  - ActivateGauge(bool): 딜 페이즈 진입/종료 시 BossWardenCore 에서 호출
  - 강/약 공격 구분: sealAmount >= 25 → coreChargeAttackGain / 미만 → coreBasicAttackGain
  - 페이즈별 목표 이벤트: OnPhase1TargetReached / OnPhase2TargetReached (각 1회)
  - 코어 봉인도는 페이즈 전환 후에도 초기화되지 않음
  - 피격 점멸: DOColor 흰색 → _baseColor (SetUpdate=true)

⚠️ PlayerInputHandler 추가 필요 항목:
  IsSealHeld 프로퍼티 (bool) — S키 홀드 상태 폴링
  _isAttackHeld 와 동일한 방식으로 _isSealHeld 추가 필요
```

---

### STEP 05 — 그로기 + 코어 활성화

**목표:** 양팔 봉인 완료 시 그로기 진입 + 코어 SetActive

| 파일 | 작업 내용 | 상태 |
|---|---|---|
| `BossWardenCore.cs` | 양팔 봉인 완료 감지 → EnterGroggy() | ✅ 구현 완료 |
| `BossWardenCore.cs` | 그로기 진입 → 코어 SetActive(true) | ✅ 구현 완료 |
| `BossWardenCore.cs` | 그로기 타이머 + OnGroggyExit (실패 처리) | ✅ 구현 완료 |
| `BossWardenFeedback.cs` | 그로기 노란 Pulse + 코어 노란 Pulse | ✅ STEP02 완료 |
| `BossWardenAttackRange.cs` | 코어 해제 범위 점선 원 표시 | ✅ STEP03 완료 |

**완료 조건:** 양팔 봉인 → Warden 정지 + 노란 Pulse + 코어 표시 확인 / 5초 후 해제 확인

---

### STEP 06 — 코어 해제 + 딜 페이즈

**목표:** S키 코어 해제 → 딜 페이즈 진입 → 코어 봉인도 누적

| 파일 | 작업 내용 | 상태 |
|---|---|---|
| `BossWardenSealExecutor.cs` | 코어 해제 범위 감지 + S키 홀드 + 슬로우 | ✅ STEP04 완료 |
| `BossWardenCore.cs` | 딜 페이즈 진입 → OnDilPhaseEnter 발행 | ✅ 구현 완료 |
| `BossWardenCoreSealGauge.cs` | 딜 페이즈 중 공격 수신 → 코어 봉인도 누적 | ✅ STEP04 완료 |
| `BossWardenCoreSealGauge.cs` | 50% / 100% 도달 이벤트 발행 | ✅ STEP04 완료 |
| `BossWardenFeedback.cs` | 딜 페이즈 밝은 주황 + 코어 흰 Pulse | ✅ STEP02 완료 |

**완료 조건:** 코어 해제 → 딜 페이즈 진입 → 코어 공격 → 봉인도 증가 확인

---

### STEP 07 — 딜 페이즈 종료 + 페이즈 전환

**목표:** 딜 페이즈 정상 종료 + 충격파 + 2페이즈 전환

| 파일 | 작업 내용 | 상태 |
|---|---|---|
| `BossWardenCore.cs` | 딜 페이즈 종료 조건 판정 (시간 / 봉인도) | ✅ 구현 완료 |
| `BossWardenShockwave.cs` | 충격파 범위 + 플레이어 넉백 + 연출 | ✅ 구현 완료 |
| `BossWardenCore.cs` | 부위 봉인 해제 + 봉인도 초기화 | ✅ 구현 완료 |
| `BossWardenCore.cs` | OnPhaseChanged(2) 발행 → AI 2페이즈 강화 | ✅ 구현 완료 |
| `BossWardenAI.cs` | 2페이즈 패턴 강화 적용 + RageCharge 추가 | ✅ STEP02 완료 |
| `BossPattern_RageCharge.cs` | 3연 돌진 패턴 구현 | ✅ STEP03 완료 |

**완료 조건:** 딜 페이즈 종료 → 충격파 → 부위 해제 → 2페이즈 패턴 변화 확인

---

### STEP 08 — 최종 봉인 + 처치

**목표:** 코어 봉인도 100% → 최종 봉인 → 처치 연출

| 파일 | 작업 내용 | 상태 |
|---|---|---|
| `BossWardenSealExecutor.cs` | 최종 봉인 S키 홀드 + 강한 슬로우 + 연출 | ✅ STEP04 완료 |
| `BossWardenCore.cs` | OnFinalSealCompleted 수신 → Die() | ✅ 구현 완료 |
| `BossWardenFeedback.cs` | 처치 연출 (DOScale 0 + 검정 DOColor) | ✅ STEP02 완료 |
| `BossWardenCore.cs` | OnDead 발행 → BattleManager 연결 | ✅ 구현 완료 |

**완료 조건:** 코어 봉인도 100% → 최종 봉인 실행 → Warden 축소 소멸 확인

### STEP 05~08 구현 노트

```
BossWardenCore.cs  (v1.0)
  - POC07 TestBossCore 전체 구조 계승 — HP 제거, 2페이즈 + 최종봉인 추가
  - RequireComponent: BossWardenAI / BossWardenFeedback / BossWardenSealExecutor / AttackRange
  - Start(): DataSO 를 모든 하위 컴포넌트에 주입하는 단일 연결 지점
  - SubscribeAll(): 모든 이벤트 구독을 한 함수에 집약 (중복 방지 -= 후 +=)
  - 그로기 흐름:
      CheckGroggyCondition() → armL.IsSealed && armR.IsSealed → EnterGroggy()
      EnterGroggy(): ActivateCore() + OnGroggyEnter 발행 + GroggyRoutine 시작
      GroggyRoutine(): WaitForSecondsRealtime(groggyDuration) → ExitGroggyFailure()
      ExitGroggyFailure(): 코어 비활성 + ForceRelease(resetSealCount:false) + OnGroggyExit
  - 코어 해제 흐름:
      HandleCoreUnlocked() → GroggyCoroutine Stop → EnterDilPhase()
  - 딜 페이즈 흐름:
      EnterDilPhase(): ActivateGauge(true) + OnDilPhaseEnter + DilPhaseRoutine 시작
      DilPhaseRoutine(): WaitForSecondsRealtime(dilPhaseDuration) → ExitDilPhase(false)
      HandlePhase1TargetReached() → ExitDilPhase(false) → 페이즈 전환
      HandlePhase2TargetReached() → ExitDilPhase(true) → OnFinalSealReady
  - ExitDilPhase(false): 코어 비활성 + ForceRelease + 충격파 + OnDilPhaseExit + OnPhaseChanged(2)
  - ExitDilPhase(true): OnFinalSealReady 발행 (최종 봉인 진입)
  - HandleFinalSealCompleted() → Die()
  - Die(): StopAllCoroutines + 물리 정지 + 코어 정리 + AttackRange.HideAll + OnDead
  - DEBUG ContextMenu: 그로기/딜페이즈 강제 진입 / 봉인도 즉시 채우기
  - WaitForSecondsRealtime: 슬로우 모션 중에도 타이머 정상 동작

  [봉인 카운트 중복 방지 설계]
    BossWardenArmPart.OnPartSealed (SealGaugeComponent.OnSealed 래핑) → HandlePartSealed
    BossWardenSealExecutor.OnPartSealed → HandleExecutorPartSealed (카운트 없음)
    카운트는 HandlePartSealed 에서만 증가 → 중복 카운트 방지

BossWardenShockwave.cs  (v1.0)
  - POC07 TestBossShockwave v1.3 의 WaitForFixedUpdate 넉백 교훈 적용
  - 탑뷰 변환: 수직 힘 제거 → X/Y 평면 방향 × knockbackForce 만 적용
  - 넉백 코루틴:
      BlockAll 즉시 → WaitForFixedUpdate → linearVelocity 설정
      → WaitForSecondsRealtime(shockwaveKnockbackDuration) → UnblockAll
  - 시각 연출: SpriteRenderer 디스크 Scale 0 → 반경 × 2 DOScale(OutQuart) + DOColor 페이드
  - SetUpdate(true): DOTween Sequence — TimeScale 슬로우 중에도 정상 동작
  - SortingLayer = Ground 권장 (캐릭터 아래에 표시)
  - OverlapCircleNonAlloc: GC 없이 플레이어 감지
  - 카메라 DOShakePosition: 미연결 시 스킵
```

---

### STEP 09 — 전체 루프 통합 테스트

**목표:** 1페이즈 → 딜 페이즈 → 2페이즈 → 최종 봉인 전체 루프 1회 완주

| 항목 | 내용 | 상태 |
|---|---|---|
| 전체 루프 1회 완주 | 패턴 → 봉인 → 그로기 → 코어 → 딜 → 전환 → 최종 봉인 | ⬜ 미완료 |
| 봉인 저항 동작 확인 | 동일 부위 반복 봉인 시 저항 배율 적용 확인 | ⬜ 미완료 |
| 그로기 실패 처리 확인 | 코어 해제 실패 시 부위 해제 + 루프 재시작 확인 | ⬜ 미완료 |
| 예고 범위 피드백 전체 확인 | 5개 패턴 예고 범위 모두 정상 표시 확인 | ⬜ 미완료 |
| 색상 상태 전환 전체 확인 | 모든 상태의 색상이 혼동 없이 구분되는지 확인 | ⬜ 미완료 |

### 씬 조립 순서 절차 (STEP 09 진입 전)

**1단계 — Project Settings 설정**

```
Project Settings → Tags and Layers:
  Layer 9  : Enemy
  Layer 10 : Player
  Layer 11 : BossHitbox
  Layer 12 : Ground  (예고 디스크 SortingLayer 용)

Physics2D → Layer Collision Matrix:
  Enemy    ↔ Player     : OFF  (보스 본체가 플레이어를 밀치지 않음)
  Enemy    ↔ BossHitbox : OFF
  Player   ↔ BossHitbox : ON   (보스 패턴이 플레이어를 감지)
  Player   ↔ Enemy      : OFF
```

**2단계 — BossWardenDataSO 에셋 생성**

```
Assets 우클릭 → Create → SEAL/Boss → BossWardenDataSO
파일명: BossWardenDataSO_Default

수치 설정 (13. DataSO 수치표 참조):
  armSealGaugeMax = 200
  coreSealGaugeMax = 500
  groggyDuration = 5.0
  dilPhaseDuration = 10.0
  (나머지 기본값 유지)
```

**3단계 — Hierarchy 오브젝트 생성 순서**

```
① BossRoot (빈 GameObject)

② BossRoot 하위에 Boss_Warden (빈 GameObject)
   컴포넌트 부착 순서:
   1. Rigidbody2D       GravityScale=0 / FreezeRotation Z=true / Continuous
   2. CapsuleCollider2D Size=(0.8, 1.4) / Layer=Enemy
   3. SpriteRenderer    Sprite=Knob / Color=#888888 / Scale=(0.8,1.4,1)
   4. BossWardenAttackRange    ← 패턴 전에 먼저 부착 (패턴 스크립트가 참조)
   5. BossWardenFeedback
   6. BossWardenAI
   7. BossWardenSealExecutor
   8. BossWardenShockwave
   9. BossWardenCore      ← 반드시 마지막 (RequireComponent 로 위 컴포넌트 필요)

③ Boss_Warden 하위에 LeftArm
   컴포넌트: SpriteRenderer(Knob, #AAAAAA, Scale=(0.7,0.25,1))
             CapsuleCollider2D(isTrigger=true, Layer=Enemy)
             SealGaugeComponent
             BossWardenArmPart  (PartType=LeftArm, Layer=Enemy)
   위치: (-0.6, 0, 0)

④ Boss_Warden 하위에 RightArm (LeftArm 과 동일, PartType=RightArm)
   위치: (0.6, 0, 0)

⑤ Boss_Warden 하위에 Core
   컴포넌트: SpriteRenderer(Knob, #FFEE00, Scale=(0.35,0.35,1))
             CircleCollider2D(isTrigger=true, Layer=Enemy)
             BossWardenCoreSealGauge
   SetActive: false  ← 반드시
   위치: (0, -0.5, 0)

⑥ Boss_Warden 하위에 HurtBox
   컴포넌트: CapsuleCollider2D(isTrigger=true, Size=(0.8,1.4), Layer=Enemy)
   SpriteRenderer 없음

⑦ Boss_Warden 하위에 AttackRangeVisuals (빈 GameObject, Layer=Default)
   하위에 다음 자식 생성 (기본 SetActive=false):
   - ChargeLine        [LineRenderer] Color=#FF000066, Width=0.08
   - DiscSlam0         [SpriteRenderer] Knob/Circle, #FF000066
   - DiscSlam1         [SpriteRenderer] Knob/Circle, #FF000066
   - DiscSweep         [SpriteRenderer] Knob/Circle, #FF000066
   - DiscGuardBreak    [SpriteRenderer] Knob/Square, #FF000066
   - RageChargeLine0/1/2  [LineRenderer] 각각
   - SealRangeCircle   [LineRenderer] Color=#0088FF
   - CoreRangeCircle   [LineRenderer] Color=#FFEE00
   - ShockwaveDisc     [SpriteRenderer] Knob/Circle (충격파 연출용)

⑧ Boss_Warden 하위에 Patterns (빈 GameObject)
   하위에 패턴 스크립트 부착:
   - BossPattern_Charge
   - BossPattern_Slam
   - BossPattern_Sweep
   - BossPattern_GuardBreak
   - BossPattern_RageCharge
```

**4단계 — Inspector 연결**

```
BossWardenCore:
  _data          → BossWardenDataSO 에셋
  _armL          → LeftArm.BossWardenArmPart
  _armR          → RightArm.BossWardenArmPart
  _coreObject    → Core GameObject
  _coreSealGauge → Core.BossWardenCoreSealGauge
  _shockwave     → Boss_Warden.BossWardenShockwave

BossWardenAI:
  _data          → BossWardenDataSO 에셋
  _patterns      → [Charge, Slam, Sweep, GuardBreak, RageCharge] 리스트
  _armL          → LeftArm.BossWardenArmPart
  _armR          → RightArm.BossWardenArmPart

BossWardenFeedback:
  _data          → BossWardenDataSO 에셋
  _bodyRenderer  → Boss_Warden.SpriteRenderer
  _armLRenderer  → LeftArm.SpriteRenderer
  _armRRenderer  → RightArm.SpriteRenderer
  _coreRenderer  → Core.SpriteRenderer
  _armLPart      → LeftArm.BossWardenArmPart
  _armRPart      → RightArm.BossWardenArmPart

BossWardenSealExecutor:
  _data          → BossWardenDataSO 에셋
  _armL          → LeftArm.BossWardenArmPart
  _armR          → RightArm.BossWardenArmPart
  _coreObject    → Core GameObject
  _coreSealGauge → Core.BossWardenCoreSealGauge
  _attackRange   → Boss_Warden.BossWardenAttackRange

BossWardenShockwave:
  _data          → BossWardenDataSO 에셋
  _playerLayer   → Player 레이어 마스크
  _discRenderer  → AttackRangeVisuals/ShockwaveDisc.SpriteRenderer

BossWardenAttackRange:
  _data               → BossWardenDataSO 에셋
  _chargeLineRenderer → AttackRangeVisuals/ChargeLine.LineRenderer
  _slamDisc0          → AttackRangeVisuals/DiscSlam0.SpriteRenderer
  _slamDisc1          → AttackRangeVisuals/DiscSlam1.SpriteRenderer
  _sweepDisc          → AttackRangeVisuals/DiscSweep.SpriteRenderer
  _guardBreakDisc     → AttackRangeVisuals/DiscGuardBreak.SpriteRenderer
  _rageChargeLines    → [RageChargeLine0, 1, 2] LineRenderer 배열
  _sealRangeCircle    → AttackRangeVisuals/SealRangeCircle.LineRenderer
  _coreRangeCircle    → AttackRangeVisuals/CoreRangeCircle.LineRenderer

각 패턴 스크립트:
  _data          → BossWardenDataSO 에셋
  _ai            → Boss_Warden.BossWardenAI
  _attackRange   → Boss_Warden.BossWardenAttackRange
  _playerLayer   → Player 레이어 마스크
  _linkedArmPart → 해당 팔 BossWardenArmPart (Charge/GuardBreak=RightArm, Slam/Sweep=LeftArm)
  팔 DOMove 패턴 (Slam/GuardBreak):
    _armLTransform / _armRTransform → LeftArm / RightArm Transform

BossWardenArmPart (LeftArm, RightArm 각각):
  _data              → BossWardenDataSO 에셋
  _playerAttackLayer → BossHitbox 레이어 마스크
  ※ 플레이어의 무기 히트박스 Collider2D 가 BossHitbox 레이어에 있어야 함
  ※ PlayerAttackHitboxManager 가 OverlapCollider 로 Enemy 레이어 감지 후
     OnHit(col, sealAmount) 발행 → BossWardenArmPart 가 col 대조로 수신
  ※ _playerAttackLayer 는 현재 BossWardenArmPart 에서 직접 사용하지 않음
     (PlayerAttackHitboxManager.OnHit 구독 방식으로 피격 감지하므로)
     — 레이어 마스크 필드는 향후 직접 감지 방식 전환 시를 위해 유지
```

**5단계 — 플레이어 씬 설정 확인**

```
Player 오브젝트:
  Layer = Player
  PlayerMoveController 부착 확인
  PlayerAttackHitboxManager 부착 확인
    → _enemyLayer 에 Enemy 레이어 선택
    → 히트박스 Collider2D 들 Layer = BossHitbox 설정
  PlayerInputHandler (Systems/InputSystem):
    v1.2 확인 — IsSealHeld 프로퍼티 존재 여부 확인
```

**6단계 — DEBUG ContextMenu 로 구간별 테스트**

```
테스트 순서:

① 패턴 단독 테스트
   Boss_Warden 선택 → BossWardenAI 확인
   Play 후 Warden 이 플레이어 추적 + 패턴 발동하는지 확인
   예고 범위 표시 확인

② 봉인도 누적 테스트
   플레이어 공격으로 팔 봉인도 누적되는지 확인
   팔 색상 단계 변화 확인 (회색→노랑→주황→빨강→파랑 Pulse)

③ 봉인 집행 테스트
   봉인도 100% 후 점선 원 표시 확인
   S키 홀드로 봉인 집행 완료 확인
   봉인 완료 시 해당 패턴 비활성 확인

④ 그로기 강제 테스트 (DEBUG ContextMenu)
   Boss_Warden 선택 → BossWardenCore → 우클릭
   "DEBUG: 그로기 강제 진입" 클릭
   → Warden 정지 + 노란 Pulse + 코어 활성 확인

⑤ 딜 페이즈 강제 테스트 (DEBUG ContextMenu)
   "DEBUG: 딜 페이즈 강제 진입" 클릭
   → 코어 흰 Pulse + 밝은 주황 연출 확인
   → 코어 공격으로 봉인도 증가 확인

⑥ 전체 루프 완주 테스트
   양팔 봉인 → 그로기 → S키 코어 해제 → 딜 페이즈 → 1페이즈 종료
   → 충격파 확인 → 2페이즈 전환 → 동일 루프 반복
   → 코어 봉인도 100% → 최종 봉인 S키 → Warden 소멸 확인
```

### ⚠️ 알려진 미구현 / 임시 처리 항목

```
[ ] 플레이어 피격 처리 미구현
    현재: 보스 패턴 OverlapXX 감지 시 Debug.Log 만 출력
    임시: 테스트 중 플레이어가 패턴에 맞아도 아무 반응 없음 (의도적 생략)

[ ] GuardBreak IsGuarding 정면 봉인도 무효 미연동
    현재: IsGuarding = true 여도 봉인도 정상 누적
    임시: 테스트에서 측면/후방 공략 전략만 확인

[ ] PlayerAttackHitboxManager.CurrentSealAmount 미정의
    현재: BossWardenArmPart 폴백값 10f 사용
    임시: 강공격/약공격 구분 없이 10f 고정 누적
    수정: PlayerAttackHitboxManager 에 CurrentSealAmount 프로퍼티 추가 필요

[ ] BattleManager 미구현
    현재: OnDead 발행 후 연결 없음
    임시: Debug.Log("[BossWardenCore] 보스 처치!") 로 확인
```

---

### 🔧 전체 코드 버그 수정 이력 (STEP 재체크)

| 번호 | 파일 | 버전 | 심각도 | 문제 | 수정 내용 |
|---|---|---|---|---|---|
| 🔴 버그1 | `BossWardenSealExecutor.cs` | v1.0→v1.1 | 크리티컬 | `SubscribeArmEvents()` 람다 구독 → 해제 불가 + 중복 호출 | `_onArmLSealReady` 등 `Action` 필드에 캐싱 → `-=` 정상 해제 보장 |
| 🔴 버그2 | `BossWardenSealExecutor.cs` | v1.0→v1.1 | 크리티컬 | `Update()` 에서 `_holdTimer` 리셋 조건 역전 (`_isExecuting = true` 일 때 리셋) | 해당 블록 제거 — `_holdTimer` 는 `DetectSealInput` 코루틴에서만 관리 |
| 🔴 버그3 | `SealGaugeComponent.cs` | v1.1→v1.2 | 낮음 | `AddGauge()` 주석에 구버전 `OnTriggerEnter2D` 내용 잔존 | 주석을 현재 구현(`PlayerAttackHitboxManager.OnHit`) 기준으로 수정 |
| 🔴 버그4 | `BossWardenCore.cs` | v1.0→v1.1 | 높음 | `Start()` 실행 순서 미보장 → `BossWardenFeedback.SubscribeArmGauge()` 시점에 `SealGauge` 미초기화 가능 | `[DefaultExecutionOrder(-10)]` 추가 + `Initialize()` 를 `Awake()` 로 이동 |
| 🔴 버그5 | `BossWardenAI.cs` | v1.0→v1.1 | 중간 | `TrySelectPattern()` 에서 `Idle` 상태 매 프레임 `new List<>` 생성 → GC 압박 | `_availablePatterns` 멤버 변수로 캐싱 → `Clear()` 후 재사용 |
| 🔴 버그6 | `BossPattern_Charge.cs` | v1.0→v1.2 | 크리티컬 | `Awake()` 에서 `_triggerGroggyOnRecovery = true` 강제 설정 → Inspector 값 덮어쓰기 → Recovery 마다 그로기 무한 발행 | `Awake()` 강제 설정 코드 제거 (프로젝트 파일 기준 v1.1에서 수정 완료) |
| 🔴 버그7 | `BossPattern_Charge.cs` | v1.1→v1.2 | 크리티컬 | `transform.position` 사용 → Patterns 자식 오브젝트 기준 → 거리 계산 항상 0 → 무한루프 | `_rigid2D.position` 으로 교체 (프로젝트 파일 기준 v1.2에서 수정 완료) |
| 🔴 버그8 | `BossPattern_RageCharge.cs` | v1.0→v1.1 | 크리티컬 | 동일 — `transform.position` → 무한루프 | `_rigid2D.position` 으로 교체 (프로젝트 파일 기준 v1.1에서 수정 완료) |
| 🟡 경고1 | `BossWardenCoreSealGauge.cs` | v1.0→v1.1 | 낮음 | 프로젝트 파일에 `_core` 필드(미사용) 존재 | 프로젝트 파일에서 `_core` 필드 및 `GetComponentInParent` 참조 제거 권장 |
| 🟡 경고2 | `BossWardenAI.cs` | - | 낮음 | `RageCharge` 에 불필요한 그로기 이벤트 구독 | 동작 영향 없음. 향후 정리 |
| 🟡 경고3 | `BossWardenFeedback.cs` | - | 낮음 | `PlayBodyHitFlash()` 미연결 | 플레이어 피격 처리 구현 시 연결 필요 |

---

### 진척 상태 범례

| 아이콘 | 상태 |
|---|---|
| ⬜ | 미구현 |
| 🟨 | 구현 중 |
| ✅ | 구현 완료 / 테스트 통과 |
| ❌ | 구현 실패 / 재작업 필요 |

---

*이 파일은 Boss_Warden 구현의 유일한 기준 문서입니다.*  
*구현 진척 시 각 STEP의 상태 아이콘을 업데이트합니다.*  
*SEAL_DEVSession.md 와 연동하여 버전 기록을 유지합니다.*