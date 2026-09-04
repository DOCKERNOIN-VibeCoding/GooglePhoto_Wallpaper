# OAuth 클라이언트 발급 가이드

이 프로그램에는 자격증명이 들어 있지 않습니다. 그래서 처음 한 번, 본인 명의의 OAuth 클라이언트를
만들어 등록해야 합니다. 5분 정도 걸리고, 한 번 하면 다시 할 일은 없습니다.

> **이건 구글 계정 비밀번호와 무관합니다.** "이 프로그램이 내 Google Photos에 접근해도 된다"는
> 것을 Google에 등록하는 절차이고, 발급물은 `client_secret.json`이라는 파일 하나입니다.

---

## 1. Google Cloud 프로젝트 만들기

1. <https://console.cloud.google.com/projectcreate> 접속
2. 프로젝트 이름에 아무거나 (예: `Photo Wallpaper`) 입력하고 **만들기**
3. 생성이 끝나면 화면 위쪽에서 그 프로젝트가 선택되어 있는지 확인

## 2. Photos Picker API 켜기

1. <https://console.cloud.google.com/apis/library/photospicker.googleapis.com> 접속
2. **사용** 버튼 클릭

> 여기서 켜는 건 **Photos Picker API**입니다. 이름이 비슷한 "Photos Library API"가 아닙니다.

## 3. OAuth 동의 화면 설정

1. <https://console.cloud.google.com/auth/overview> 접속 → **시작하기**
2. 앱 이름: 아무거나 (예: `Photo Wallpaper`) / 사용자 지원 이메일: 본인 메일
3. 대상(Audience): **외부(External)** 선택
4. 연락처 이메일 입력 후 저장

### 3-1. 본인을 테스트 사용자로 추가

**대상(Audience)** 화면 → **테스트 사용자** → **Add users** → 본인 Gmail 주소 추가.

### 3-2. ⚠️ 중요: 앱을 "프로덕션"으로 게시하세요

**대상(Audience)** 화면에서 **앱 게시(Publish app)** 를 누릅니다.

이걸 안 하고 "테스트" 상태로 두면 **7일마다 재로그인해야 합니다.** Google이 테스트 상태 앱에
발급하는 refresh token은 7일 뒤 만료되기 때문입니다.

게시할 때 "확인되지 않은 앱" 경고가 뜨는데, 본인이 만들어 본인만 쓰는 앱이므로 그대로
진행하면 됩니다. 검증(verification)을 받을 필요는 없습니다. 나중에 로그인할 때
"Google에서 확인하지 않은 앱입니다" 화면이 나오면 **고급 → (안전하지 않음) 계속** 을
누르면 됩니다. 본인 앱이니 정상입니다.

## 4. 클라이언트 만들고 JSON 내려받기

1. <https://console.cloud.google.com/auth/clients> 접속 → **클라이언트 만들기**
2. 애플리케이션 유형: **데스크톱 앱**
3. 이름: 아무거나 → **만들기**
4. 생성된 클라이언트 오른쪽 **JSON 다운로드** 클릭 → `client_secret_xxxxx.json` 저장

## 5. 프로그램에 등록

1. Google Photo Wallpaper 설정창 열기
2. **Google 연결에 쓸 OAuth 클라이언트** 항목의 **파일 선택**
3. 방금 받은 `client_secret_*.json` 선택
4. **계정 연결** → 브라우저에서 로그인 및 권한 허용

등록한 파일은 `%LOCALAPPDATA%\GooglePhotoWallpaper\client_secret.json`으로 복사되므로,
원본은 옮기거나 지워도 됩니다.

---

## 사진 고르기

**Google Photos에서 사진 선택**을 누르면 브라우저에 Google 사진 선택 화면이 열립니다.

- 앨범 탭은 따로 보이지 않습니다. **검색창에 앨범 이름을 입력**하세요.
- 그 앨범의 사진들이 나오면 원하는 만큼 선택하고 **완료**를 누릅니다.
- 한 번에 최대 2000장.
- 동영상은 배경화면으로 쓸 수 없어 자동으로 제외됩니다.

사진은 선택 직후 전부 로컬로 내려받습니다(Google이 주는 링크가 1시간 뒤 만료되기 때문).
이후에는 인터넷 없이도 배경화면이 계속 돌아갑니다.

---

## 문제 해결

**"저장된 인증이 만료되었습니다"**
→ OAuth 동의 화면이 아직 "테스트" 상태입니다. 위 3-2를 따라 **프로덕션으로 게시**하세요.

**"액세스 차단됨: 앱이 Google의 인증 절차를 완료하지 않았습니다"**
→ 3-1의 테스트 사용자에 본인 계정이 없거나, 3-2의 게시를 안 한 상태입니다.

**"Google에서 확인하지 않은 앱입니다" 경고**
→ 정상입니다. **고급 → (안전하지 않음) 계속** 을 누르세요. 본인이 만든 클라이언트입니다.

**"refresh token을 받지 못했습니다"**
→ 이미 이 앱에 권한을 준 적이 있어서입니다.
<https://myaccount.google.com/permissions> 에서 해당 앱 권한을 삭제하고 다시 연결하세요.

**403 PERMISSION_DENIED**
→ 2단계의 **Photos Picker API**가 꺼져 있습니다.

**사진 다운로드가 실패함**
→ 선택 후 1시간이 지나면 Google이 주는 링크가 만료됩니다. 다시 선택하세요.
