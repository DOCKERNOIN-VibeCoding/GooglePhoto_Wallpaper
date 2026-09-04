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
| Picker API | ❌ 매번 직접 선택 | ✅ 누구나 — 자동 반영이 안 돼 쓰지 않음 |
| Ambient API | ✅ 원래 이 용도 | ❌ [파트너 승인](https://developers.google.com/photos/partner-program/overview) 필요 (크롬캐스트가 쓰는 것) |

그래서 이 프로그램은 **앨범의 공유 링크를 직접 읽습니다.** 앨범을 "링크가 있는 모든 사용자"로
공유하면 그 페이지에 사진 목록이 들어 있고, 거기 있는 이미지 주소는 인증 없이 받아집니다.
소유자가 공개한 링크를 그대로 읽는 것이라 접근 제어를 우회하는 것은 아닙니다.

**되는 것**

- 앨범에 사진 추가 → 다음 확인 때 자동 반영 (기본 15분, 1분까지 조정 가능)
- Google 로그인도, Google Cloud 프로젝트도 필요 없음
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

### 깨졌을 때 쓸 방식: 로컬 폴더

폴더에 파일이 생기면 자동 반영됩니다. 파일 시스템만 읽으므로 **깨질 일이 없습니다.**
Google Takeout으로 받은 폴더나 OneDrive/Drive 동기화 폴더를 지정하세요.

> 공식 Picker API로 사진을 직접 고르는 방식도 만들었다가 걷어냈습니다. 자동 반영이 안 되는 데다
> 사용자마다 Google Cloud 프로젝트와 OAuth 클라이언트를 발급해야 해서, 얻는 것에 비해 부담이
> 컸습니다. 코드는 커밋 `d570be2`에 남아 있습니다.

---

## 세로 사진을 가로 모니터에 (`사진이 화면 비율과 다를 때`)

Windows의 기본 `채우기`는 화면을 꽉 채우는 대신 사진을 잘라냅니다. 세로 사진이면 위아래가
크게 날아갑니다. 그래서 이 프로그램은 필요할 때 **모니터 해상도에 맞는 이미지를 직접 합성**합니다.

| 모드 | 결과 |
|---|---|
| 채우기 (기본) | 화면을 꽉 채움. 가장자리가 잘림 |
| **전체 보이기 — 블러 배경** | 사진 전체 + 남는 여백을 같은 사진의 확대·블러본으로 채움 |
| 전체 보이기 — 검은 여백 | 사진 전체 + 검은 레터박스 |

합성은 **비율이 실제로 어긋날 때만** 일어납니다. 16:9 사진을 16:9 모니터에 올리면 원본을
그대로 씁니다. 결과는 `cache/fitted/`에 캐시되어 같은 사진·같은 해상도면 다시 만들지 않습니다.
(1920×1080 블러 합성 기준 최초 약 250ms, 이후 0ms)

## 모니터 한 대만 바꾸기

`지정한 모니터 한 대만 바꾸기`를 고르면 선택한 모니터에서만 사진이 돌아갑니다. 나머지
모니터는 **이 프로그램이 설정하지 않은 배경화면까지 그대로** 둡니다.

---

## 기능

- 공유 앨범 링크만 붙여넣으면 끝 — 로그인 없음
- 앨범에 사진을 추가하면 자동 반영, 새 사진만 내려받아 캐시 (이후 오프라인 동작)
- 로컬 폴더 소스 (하위 폴더 포함, 자동 재검색)
- 모니터별 개별 배경화면 — Windows `IDesktopWallpaper` 사용
- 모니터 배치 3가지 — 순차(한 칸씩) / 전체 동일 / **지정한 모니터 1대만**
- **세로 사진 처리** — 잘라내지 않고 전체를 보여주고 여백을 블러 배경이나 검은 레터박스로 채움
- 변경 주기 1분 ~ 24시간
- 순서 섞기 (섞어도 모니터 간 한 칸 차이는 유지)
- Windows 시작 시 자동 실행
- 트레이 아이콘에서 다음 사진 / 일시 정지 / 설정

## 개인정보와 배포

- **로그인이 없습니다.** 계정도, 토큰도, 자격증명도 저장하지 않습니다. 공유 앨범 링크는
  소유자가 공개한 주소이고, 사진은 인증 없이 받아집니다.
- 저장되는 것은 실행 파일 옆 데이터 폴더의 설정 파일과 내려받은 사진뿐입니다.
- 사진은 Google에서 직접 받아 로컬에만 저장됩니다. 외부로 보내는 것은 없습니다.
- 빌드에 넣을 자격증명 자체가 없으므로 그대로 자유롭게 배포할 수 있습니다.

> ⚠️ **공유 앨범 링크는 주소를 아는 사람이면 누구나 볼 수 있습니다.**
> 공개되어도 괜찮은 사진만 앨범에 담으세요.

## 설치해서 쓰기

1. [Releases](https://github.com/DOCKERNOIN-VibeCoding/GooglePhoto_Wallpaper/releases)에서
   `GooglePhotoWallpaper.exe` 내려받기 — **설치 없는 단일 파일, .NET 설치도 불필요**
2. Google Photos에서 배경화면용 앨범을 만들고 **공유 → 링크 만들기**
3. 프로그램을 실행하고 그 링크를 **Google Photos 공유 앨범** 칸에 붙여넣은 뒤 `지금 동기화`
4. 변경 주기와 모니터 배치 방식을 정하고 `저장`

이게 전부입니다. 로그인도, Google Cloud 설정도 필요 없습니다.
이후로는 앨범에 사진을 넣기만 하면 알아서 따라옵니다.

> 처음 실행할 때 Windows SmartScreen이 "알 수 없는 게시자" 경고를 띄웁니다.
> 코드 서명 인증서가 없어서 그렇습니다. `추가 정보 → 실행`을 누르면 됩니다.

### 포터블

설정과 사진은 **실행 파일 옆의 `GooglePhotoWallpaper-Data` 폴더**에 저장됩니다.

```
아무폴더\ (USB도 가능)
├─ GooglePhotoWallpaper.exe
└─ GooglePhotoWallpaper-Data\
   ├─ settings.json
   ├─ photos.json
   └─ cache\          내려받은 사진 + 합성본
```

- 폴더째 USB에 담아 다른 PC에서 실행하면 설정과 사진이 그대로 따라옵니다
- 그 폴더만 지우면 흔적이 남지 않습니다
- 실행 파일 위치에 쓸 수 없으면(예: `Program Files`) 자동으로
  `%LOCALAPPDATA%\GooglePhotoWallpaper\`를 씁니다
- 레지스트리를 쓰는 것은 `Windows 시작할 때 자동 실행` 하나뿐이고, 끄면 지워집니다

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
│  └─ DesktopWallpaperInterop.cs   IDesktopWallpaper COM (모니터별 배경화면)
├─ Services/
│  ├─ RotationEngine.cs            한 칸씩 미는 배치 계산 ← 핵심 로직
│  ├─ WallpaperRotator.cs          타이머와 적용
│  ├─ WallpaperService.cs          모니터 열거, 배경화면 지정
│  ├─ WallpaperComposer.cs         블러/레터박스 합성 + 캐시
│  ├─ FitGeometry.cs               맞춤 배치 계산
│  ├─ PhotoLibrary.cs              캐시 목록 관리
│  └─ Sources/                     IPhotoSource → 공유 앨범 / 로컬 폴더
│     └─ SharedAlbumParser.cs      공유 페이지 파싱 ← 깨지면 여기만 고치면 됩니다
└─ Views/SettingsWindow.xaml       설정창
tests/UnitTests/                   의존성 없는 콘솔 테스트 러너 (회전·앨범 파서·맞춤 계산)
```

사진 소스를 `IPhotoSource`로 분리해 두었습니다. 나중에 Ambient API 파트너 승인을 받으면
소스 하나만 추가하면 되고, 회전 로직과 배경화면 코드는 건드릴 필요가 없습니다.

## 요구 사항

- Windows 8 이상 (모니터별 배경화면 API가 Windows 8부터 제공)
- 배포용 단일 exe는 런타임 불필요 / 경량 빌드는 .NET 8 Desktop Runtime 필요

## 라이선스

MIT
