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

## 먼저 알아야 할 것: 앨범 자동 동기화는 불가능합니다

Google이 **2025년 3월 31일자로 `photoslibrary.readonly` 스코프를 삭제**했습니다. 앱이 사용자의
앨범 목록을 읽거나 앨범 내용을 스스로 가져오는 API는 더 이상 존재하지 않습니다(기존 호출은
`403 PERMISSION_DENIED`).

남은 방법은 세 가지고, 이 프로그램은 그중 실제로 쓸 수 있는 것을 씁니다.

| API | 앨범을 지정해두면 자동 반영 | 일반 개발자가 쓸 수 있나 |
|---|---|---|
| ~~Library API~~ | ❌ | 자기 앱이 만든 사진만 접근 가능 → 무의미 |
| **Picker API** ← 이 앱이 사용 | ❌ 사용자가 직접 선택 | ✅ |
| Ambient API | ✅ (원래 이 용도) | ❌ [파트너 프로그램](https://developers.google.com/photos/partner-program/overview) 승인 필요 |

**앨범 안의 사진만 골라오는 것은 됩니다.** Picker 화면에 앨범 탭이 따로 보이지는 않지만,
검색창에 앨범 이름을 입력하면 그 앨범의 사진이 나오고 거기서 선택하면 됩니다. Google 공식
문서도 앱이 사용자에게 "앨범을 검색하라"고 안내할 것을 권장합니다. 한 번에 최대 2000장.

안 되는 건 딱 하나입니다 — **나중에 그 앨범에 사진을 추가해도 자동으로 따라오지 않습니다.**
새 사진을 넣으려면 설정에서 다시 선택해야 합니다.

> 크롬캐스트·구글 TV의 대기화면이 앨범을 자동으로 따라가는 건 Google 자사 기능이거나
> Ambient API를 쓰는 것이고, Ambient API는 파트너 승인을 받은 기기 제조사에만 열려 있습니다.

**자동 반영이 꼭 필요하다면 `로컬 폴더` 소스를 쓰세요.** 폴더에 파일이 추가되면 다음 변경
때 자동으로 잡힙니다. Google Takeout으로 내려받은 앨범 폴더나 동기화 폴더를 지정하면 됩니다.

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
2. 실행하면 설정창이 열립니다
3. **[OAuth 클라이언트 발급 가이드](docs/GOOGLE-CLOUD-SETUP.md)** 대로 `client_secret.json`을
   만들어 `파일 선택`으로 등록
4. `계정 연결` → 브라우저에서 Google 로그인
5. `Google Photos에서 사진 선택` → 검색창에 **앨범 이름** 입력 → 사진 선택 → `완료`
6. 변경 주기와 모니터 배치 방식을 정하고 `저장`

## 직접 빌드하기

.NET 8 SDK가 필요합니다.

```powershell
git clone https://github.com/DOCKERNOIN-VibeCoding/GooglePhoto_Wallpaper.git
cd GooglePhoto_Wallpaper

# 테스트
dotnet run --project tests/RotationTests

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
│  └─ Sources/                     IPhotoSource → Google Photos / 로컬 폴더
└─ Views/SettingsWindow.xaml       설정창
tests/RotationTests/               의존성 없는 콘솔 테스트 러너
```

사진 소스를 `IPhotoSource`로 분리해 두었습니다. 나중에 Ambient API 파트너 승인을 받으면
소스 하나만 추가하면 되고, 회전 로직과 배경화면 코드는 건드릴 필요가 없습니다.

## 요구 사항

- Windows 8 이상 (모니터별 배경화면 API가 Windows 8부터 제공)
- 단일 exe 빌드는 런타임 불필요 / 경량 빌드는 .NET 8 Desktop Runtime 필요

## 라이선스

MIT
