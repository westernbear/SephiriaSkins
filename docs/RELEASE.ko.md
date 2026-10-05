# 빌드와 배포

설치 ZIP과 소스 ZIP은 `powershell -File tools/BuildRelease.ps1`로 만듭니다. .NET SDK 8.0.425, rg, 게임 1.0.33 및 BepInEx 5.4.23.5의 로컬 DLL 참조가 필요합니다. 게임 경로가 다르면 `-GamePath`를 지정합니다. 게임 DLL이나 BepInEx를 소스 저장소에 추가하지 않습니다.

직접 빌드와 검사:

```powershell
dotnet build -c Release
dotnet test tests/SephiriaSkins.Tests -c Release
dotnet run --project tools/PackTool -- validate catalog/catalog-1.0.33.json examples/simple-pack
```

게임 경로가 기본 Steam 경로와 다르면 빌드에 `-p:GamePath="D:\SteamLibrary\steamapps\common\Sephiria"`를 전달합니다. Core는 Unity 없는 규격·로더, Plugin은 게임 연동, PackTool은 팩 검사·ZIP·빈 템플릿 도구입니다.

스크립트는 Release 빌드, 단위 검사, 세 팩 검사, PackTool의 Windows x64 게시 및 실행 검사를 거칩니다. 설치 ZIP에는 명시된 프로젝트/Json.NET DLL만 넣습니다. 소스 ZIP은 `.gitignore`에 따라 로컬 조사 파일·빌드 출력·Unity 캐시·게임 데이터를 제외합니다. `dist`에 두 ZIP과 SHA256SUMS가 생깁니다.

Unity 예제를 재생성하려면 `powershell -File tools/BuildUnity.ps1` 후 `python tools/build_demo_pack.py`를 실행합니다. 정확한 에디터 버전과 번들 해시를 검사하고 팩을 갱신합니다. 공개 소스에는 Unity Editor 소스와 프로젝트 설정, Python 의존성, 두 예제, 카탈로그, 라이선스와 검증 기록이 포함됩니다.

카탈로그 재생성에는 `tools/requirements.txt`의 Python 의존성을 설치합니다. 게임에서 내보낸 같은 설치본의 catalog.json을 `--runtime`으로 전달하면 UI·FMOD 경로를 합칩니다. DLL 해시·Unity 버전·카탈로그 ID가 다르면 합치지 않습니다. 본체·무기의 역할은 정적 조사 결과를 유지합니다.

```powershell
python tools/generate_catalog.py "C:\Program Files (x86)\Steam\steamapps\common\Sephiria" --runtime "C:\Program Files (x86)\Steam\steamapps\common\Sephiria\BepInEx\plugins\SephiriaSkins\Export\catalog.json"
```

GitHub 저장소는 [westernbear/SephiriaSkins](https://github.com/westernbear/SephiriaSkins)입니다. 추적 소스를 main에 올리고 Releases에 설치 ZIP·소스 ZIP·SHA256SUMS를 첨부합니다. 0.1.0은 프리릴리스로 표시합니다. 게임 설치본을 새로 조사한 뒤 카탈로그를 갱신한 경우 런타임 검증을 다시 수행하세요.

진단 실행은 일반 설치에 필요하지 않습니다. `--skins-probe --skins-probe-host --skins-probe-lobby --skins-probe-packs --skins-probe-zip --skins-probe-exit`는 게임의 호스트 시작·로비 생성·팩 적용·복원 기능을 실제로 실행하고 Export에 JSON을 기록합니다. 게임이 선택 프로필을 정상 로드·자동 저장하므로 진단용 프로필에서 사용하세요. `--skins-probe-baseline`은 스킨 Harmony 패치와 팩 적용을 생략하는 비교용 옵션입니다. 게임이 종료된 상태에서 실행하고 로그를 별도 지정합니다.

출시 판정과 아직 필요한 플레이 검사는 [검증 상태](VERIFICATION.md)에 있습니다. 규격 버전 1을 지원하는 것과 전체 테마의 정식 v1 출시 판정은 구분합니다.
