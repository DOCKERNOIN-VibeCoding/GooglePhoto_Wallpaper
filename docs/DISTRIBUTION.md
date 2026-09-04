# 배포 방식과 OAuth 자격증명

이 프로그램은 **자격증명을 담지 않은 채로 자유롭게 배포**되도록 설계되어 있습니다.
이 문서는 그 이유와, 나중에 방식을 바꾸고 싶을 때 무엇이 필요한지를 정리합니다.

## 두 가지를 구분해야 합니다

| | 무엇인가 | 어디에 저장되나 |
|---|---|---|
| **사용자 계정 / 토큰** | 사용자가 로그인해서 얻는 접근 권한 | 각 PC의 `%LOCALAPPDATA%`, **DPAPI 암호화** |
| **OAuth 클라이언트** | "이 앱"이 Google에 등록된 신분 | 배포 방식에 따라 다름 (아래) |

토큰은 어떤 경우에도 프로그램에 포함되지 않습니다. 문제는 OAuth 클라이언트 쪽입니다.
모든 OAuth 앱은 클라이언트가 하나 있어야 하고, 그걸 어떻게 조달하느냐에 선택지가 갈립니다.

> **단, 기본 방식은 이 문제 자체를 비켜갑니다.** 공유 앨범 소스와 로컬 폴더 소스는 OAuth를
> 전혀 쓰지 않습니다. 아래 내용은 Google Photos **피커** 소스에만 해당합니다.

---

## 방식 A: 사용자가 직접 발급 (현재 기본값)

각 사용자가 자기 Google Cloud 프로젝트에서 클라이언트를 만들어 설정창에 등록합니다.
→ [발급 가이드](GOOGLE-CLOUD-SETUP.md)

**장점**
- 프로그램에 자격증명이 전혀 없음 → 소스와 바이너리를 아무 제약 없이 공개·배포 가능
- Google OAuth 검증 절차가 **필요 없음**
- API 할당량이 사용자별로 분리됨 (한 사람이 많이 써도 남에게 영향 없음)
- 사용자가 자기 데이터가 어디로 가는지 직접 확인 가능

**단점**
- 최초 1회 Google Cloud 콘솔 설정이 필요해 진입장벽이 있음
- 사용자의 앱이 "테스트" 상태로 남으므로 인증이 7일마다 만료됨

두 번째 항목은 실제로는 거의 체감되지 않습니다. 토큰이 필요한 건 사진을 새로 고를 때뿐이고
(순환은 로컬 캐시만 읽습니다), 그때는 `GoogleOAuthService.EnsureAccessTokenAsync`가
만료를 감지해 로그인 화면을 먼저 띄운 뒤 그대로 사진 선택으로 이어갑니다.

"프로덕션으로 게시"는 일반적인 해법이 아닙니다. Google이 홈페이지 URL과 개인정보처리방침
URL을 요구하고, 두 URL 모두 Search Console로 소유 인증된 도메인에 있어야 하기 때문입니다.
Workspace 조직 계정이라면 "내부(Internal)"로 설정해 만료를 없앨 수 있습니다.

코드상으로는 `OAuthClientConfig.BundledClientId`가 빈 문자열이고,
`AppServices.ResolveOAuthClient()`가 사용자 파일을 우선합니다.

---

## 방식 B: 클라이언트를 빌드에 포함

`src/GooglePhotoWallpaper/Services/Google/OAuthClientConfig.cs`의 상수를 채우면 됩니다.

```csharp
public const string BundledClientId = "xxxxx.apps.googleusercontent.com";
public const string BundledClientSecret = "GOCSPX-xxxxx";
```

값이 채워지면 설정창의 "이 빌드에 포함된 클라이언트 사용" 옵션이 자동으로 활성화됩니다.

**주의: 이 상태로 배포하려면 Google OAuth 검증을 받아야 합니다.**

검증 없이 배포하면 이렇게 됩니다.

| 게시 상태 | 결과 |
|---|---|
| 테스트(Testing) | 테스트 사용자 100명만 사용 가능, **refresh token이 7일 뒤 만료** → 배포 불가 |
| 프로덕션 + 미검증 | 모든 사용자에게 "확인되지 않은 앱" 경고 화면 노출 |
| 프로덕션 + 검증 완료 | 정상 배포 가능 |

`photospicker.mediaitems.readonly`는 민감한 범위라 검증에 브랜드 확인과 심사가 따릅니다.
개인 프로젝트에서 이 과정을 밟을 이유는 거의 없어서 기본값을 방식 A로 두었습니다.

> 데스크톱 앱의 `client_secret`은 실제로는 비밀이 아닙니다. Google도 설치형 앱에서는
> 바이너리에서 추출 가능하다고 전제하며, 그래서 이 앱은 PKCE(S256)를 함께 씁니다.
> 그렇더라도 클라이언트를 포함하면 **개발자 프로젝트의 할당량과 책임**이 따라오므로,
> 배포 전에 검증을 받는 것이 맞습니다.

---

## 방식 C: 공유 앨범 링크 (현재 자동 동기화 수단)

`Services/Sources/SharedAlbumSource.cs`. OAuth를 쓰지 않고, 앨범의 공개 공유 페이지에서
사진 목록을 파싱한 뒤 이미지 주소로 직접 내려받습니다. 소유자가 공개한 링크만 읽으므로
접근 제어를 우회하지 않습니다.

공식 지원 인터페이스가 아니므로 Google이 페이지 구조를 바꾸면 깨집니다. 파싱은
`SharedAlbumSource.ParseEntries`의 정규식 한 곳에 모여 있어 그때 그 부분만 고치면 됩니다.
깨진 동안에는 로컬 폴더 소스로 전환하면 됩니다.

---

## 방식 D: Ambient API (공식 자동 동기화)

앨범을 지정해두면 새 사진이 자동으로 따라오는 유일한 공식 경로입니다.
크롬캐스트·스마트TV·디지털 액자의 대기화면이 이 API를 씁니다.

**[파트너 프로그램](https://developers.google.com/photos/partner-program/overview) 승인이
필수**이고, 승인되려면 두 가지 심사를 통과해야 합니다.

1. OAuth 검증
2. Google Photos API 통합 리뷰

또 OAuth 클라이언트 유형이 "TVs and Limited Input devices"여야 해서, 데스크톱 앱보다는
기기를 겨냥한 API입니다. 신청 자체는 폼으로 누구나 할 수 있습니다.

승인을 받게 되면 `Services/Sources/IPhotoSource`를 구현한 클래스를 하나 추가하고
`AppServices.CreateSource()`에 분기를 더하면 됩니다. 회전 로직(`RotationEngine`)과
배경화면 코드(`WallpaperService`)는 손댈 필요가 없습니다.

---

## 빌드 산출물

```powershell
./build.ps1                      # 단일 exe, 런타임 포함 (약 62MB) - 배포용
./build.ps1 -FrameworkDependent  # 단일 exe, 런타임 필요 (약 330KB)
```

self-contained 빌드는 .NET 런타임 팩을 NuGet에서 받아야 하므로 최초 1회 인터넷이 필요합니다.
저장소의 `nuget.config`가 nuget.org를 지정해 두어, 전역 NuGet 설정이 없는 PC에서도 빌드됩니다.

앱 자체는 NuGet 패키지 의존성이 **0개**입니다. DPAPI도
`System.Security.Cryptography.ProtectedData` 대신 `crypt32.dll`을 직접 호출합니다.
