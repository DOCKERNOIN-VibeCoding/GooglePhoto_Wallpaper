# Google Photo Wallpaper

Google Photos에서 고른 사진을 Windows 바탕화면으로 돌려주는 트레이 프로그램입니다.
모니터가 여러 대면 각 모니터에 사진을 **한 칸씩 밀어서** 이어 붙입니다.

```
        모니터 1     모니터 2
tick 0     A            B
tick 1     B            C
tick 2     C            D
tick 3     D            E
```

모니터 2가 지금 보여주는 사진이 다음 차례에 모니터 1로 넘어옵니다. 모니터가 3대면
A·B·C → B·C·D 로 같은 규칙이 그대로 확장됩니다.

---

## 앨범 자동 동기화 — 되긴 되는데, 비공식 경로입니다

목표는 "Google Photos 앨범에 사진을 넣으면 바탕화면 목록이 알아서 갱신되는 것"입니다.
**공식 API로는 전부 막혀 있습니다.**

| API | 앨범을 따라가나 | 쓸 수 있나 |
|---|---|---|
| ~~Library API~~ | ❌ | 앱이 직접 올린 사진만 조회 가능 (2025-03-31 변경) |
| ~~공유(sharing) 스코프~~ | ❌ | 같은 날 삭제됨 |
| **Picker API** | ❌ 사용자가 그때그때 선택 | ✅ 누구나 |
| Ambient API | ✅ 원래 이 용도 | ❌ [파트너 승인](https://developers.google.com/photos/partner-program/overview) 필요 (크롬캐스트가 쓰는 것) |

그래서 이 프로그램은 **앨범의 공유 링크를 직접 읽습니다.** 앨범을 "링크가 있는 모든 사용자"로
공유하면 그 페이지에 사진 목록이 들어 있고, 거기 있는 이미지 주소는 인증 없이 받아집니다.
소유자가 공개한 링크를 그대로 읽는 것이라 접근 제어를 우회하는 것은 아닙니다.

**되는 것**

- 앨범에 사진 추가 → 다음 확인 때 자동 반영 (기본 15분, 1분까지 조정 가능)
- Google 로그인 불필요, Google Cloud 프로젝트 불필요, `client_secret.json` 불필요
- 이미 받은 사진은 다시 받지 않음 (새 사진만 다운로드)

**한계**

- **공식 지원 인터페이스가 아닙니다.** Google이 페이지 구조를 바꾸면 동작이 멈출 수 있습니다.
  그럴 때는 아래 `로컬 폴더` 방식으로 전환하면 됩니다.
- 앨범을 링크 공유 상태로 둬야 합니다. 링크를 아는 사람은 볼 수 있습니다.
- 푸시 알림은 없어서 **폴링**입니다. 진짜 실시간은 불가능합니다.
- 확인 1회당 약 209 KB가 듭니다. Google이 ETag를 주지 않아 매번 전체를 받아야 합니다.

| 확인 주기 | 하루 트래픽 |
|---|---|
| 1분 | 약 300 MB |
| 5분 | 약 60 MB |
| **15분 (기본)** | **약 20 MB** |
| 30분 | 약 10 MB |

### 다른 두 가지 방식

- **로컬 폴더** — 폴더에 파일이 생기면 자동 반영. 가장 안정적이고 절대 깨지지 않습니다.
  Google Takeout으로 받은 폴더나 OneDrive/Drive 동기화 폴더를 지정하세요.
- **Google Photos 피커** — 공식 API. 브라우저에서 사진을 직접 고릅니다(앨범 이름으로 검색 가능,
  최대 2000장). 자동 반영은 안 되고, Google Cloud에서 OAuth 클라이언트를 발급해야 합니다.
  → [발급 가이드](docs/GOOGLE-CLOUD-SETUP.md)

---

## 기능

- Google Photos에서 사진 선택 → 로컬로 내려받아 캐시 (이후 오프라인 동작)
- 로컬 폴더 소스 (하위 폴더 포함, 자동 재검색)
- 모니터별 개별 배경화면 — Windows `IDesktopWallpaper` 사용
- 순차 배치 / 모든 모니터 동일 중 선택
- 변경 주기 1분 ~ 24시간
- 순서 섞기 (섞어도 모니터 간 한 칸 차이는 유지)
- 화면 맞춤 방식 선택 (채우기/맞춤/늘이기/가운데/바둑판/확장)
- Windows 시작 시 자동 실행
- 트레이 아이콘에서 다음 사진 / 일시 정지 / 설정

## 개인정보와 배포

- **프로그램에는 어떤 자격증명도 들어 있지 않습니다.** 공개 빌드의
  `OAuthClientConfig.BundledClientId`는 빈 문자열입니다.
- OAuth 클라이언트는 사용자가 직접 발급해 설정창에서 등록합니다 →
  **[발급 방법 (5분)](docs/GOOGLE-CLOUD-SETUP.md)**
- 발급받은 토큰은 **DPAPI로 암호화**되어 `%LOCALAPPDATA%\GooglePhotoWallpaper\token.dat`에만
  저장됩니다. 현재 Windows 사용자 계정으로만 복호화되며, `settings.json`이나 로그에는 절대
  기록되지 않습니다.
- 사진은 Google에서 직접 받아 로컬에만 저장됩니다. 외부로 보내는 것은 없습니다.

배포 방식과 Google OAuth 검증에 대해서는 **[docs/DISTRIBUTION.md](docs/DISTRIBUTION.md)** 참고.

## 설치해서 쓰기

1. [Releases](https://github.com/DOCKERNOIN-VibeCoding/GooglePhoto_Wallpaper/releases)에서
   `GooglePhotoWallpaper.exe` 내려받기 (설치 불필요, 단일 파일)
2. Google Photos에서 배경화면용 앨범을 만들고 **공유 → 링크 만들기**
3. 프로그램을 실행하고 그 링크를 **Google Photos 공유 앨범** 칸에 붙여넣은 뒤 `지금 동기화`
4. 변경 주기와 모니터 배치 방식을 정하고 `저장`

이게 전부입니다. 로그인도, Google Cloud 설정도 필요 없습니다.
이후로는 앨범에 사진을 넣기만 하면 알아서 따라옵니다.

## 직접 빌드하기

.NET 8 SDK가 필요합니다.

```powershell
git clone https://github.com/DOCKERNOIN-VibeCoding/GooglePhoto_Wallpaper.git
cd GooglePhoto_Wallpaper

# 테스트
dotnet run --project tests/UnitTests

# 배포용 단일 exe (.NET 런타임 불필요, 약 62MB)
./build.ps1

# .NET 8 데스크톱 런타임이 이미 있다면 (약 330KB)
./build.ps1 -FrameworkDependent
```

결과물은 `publish/GooglePhotoWallpaper.exe`에 생깁니다.

## 구조

```
src/GooglePhotoWallpaper/
├─ Interop/
│  ├─ DesktopWallpaperInterop.cs   IDesktopWallpaper COM (모니터별 배경화면)
│  └─ DataProtection.cs            DPAPI P/Invoke (토큰 암호화)
├─ Services/
│  ├─ RotationEngine.cs            한 칸씩 미는 배치 계산 ← 핵심 로직
│  ├─ WallpaperRotator.cs          타이머와 적용
│  ├─ WallpaperService.cs          모니터 열거, 배경화면 지정
│  ├─ PhotoLibrary.cs              캐시 목록 관리
│  ├─ Google/                      OAuth(PKCE) + Picker API
│  └─ Sources/                     IPhotoSource → 공유 앨범 / 피커 / 로컬 폴더
└─ Views/SettingsWindow.xaml       설정창
tests/UnitTests/                   의존성 없는 콘솔 테스트 러너 (회전 로직 + 앨범 파서)
```

사진 소스를 `IPhotoSource`로 분리해 두었습니다. 나중에 Ambient API 파트너 승인을 받으면
소스 하나만 추가하면 되고, 회전 로직과 배경화면 코드는 건드릴 필요가 없습니다.

## 요구 사항

- Windows 8 이상 (모니터별 배경화면 API가 Windows 8부터 제공)
- 단일 exe 빌드는 런타임 불필요 / 경량 빌드는 .NET 8 Desktop Runtime 필요

## 라이선스

MIT
