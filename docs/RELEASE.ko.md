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

GitHub 저장소는 [westernbear/SephiriaSkins](https://github.com/westernbear/SephiriaSkins)입니다. 추적 소스를 main에 올리고 Releases에 설치 ZIP·소스 ZIP·SHA256SUMS를 첨부합니다. 0.1.1은 프리릴리스로 표시합니다. 게임 설치본을 새로 조사한 뒤 카탈로그를 갱신한 경우 런타임 검증을 다시 수행하세요.

진단 실행은 일반 설치에 필요하지 않습니다. 게임이 종료된 상태에서 Steam 시작 옵션에 아래 중 하나를 넣고 실행합니다. 실행 후 시작 옵션을 비웁니다. 각 진단은 메모리 전용 프로필을 만들며 기존 슬롯과 Steam Cloud에 저장하지 않습니다. 선택 설정·언어·해상도·입력 설정은 종료 시 복원합니다. 기존 Build Overlay 설정은 그대로 함께 로드합니다.

| 시작 옵션 | 기록 |
|---|---|
| `--skins-playtest --skins-probe-exit` | 기본 무기 6종의 원본/테마 비교, 코스튬·층 이동·사망/부활·언어·해상도·25회 교체·호스트 재시작 |
| `--skins-playtest --skins-playtest-controls --skins-probe-exit` | 가상 Input System 기기의 이동/공격, F6/Esc, 긴 이름 말줄임 |
| `--skins-playtest --skins-playtest-encounter --skins-probe-exit` | 카타나 특수 공격과 실제 던전 적 AI 전투·음악 전환 |
| `--skins-probe --skins-probe-host --skins-probe-lobby --skins-probe-packs --skins-probe-zip --skins-probe-exit` | 세 팩의 폴더/ZIP, 본체 출력 fixture·Unity 리소스·UI·오디오·풀 복원 |

JSON과 게임 렌더 캡처는 `BepInEx/plugins/SephiriaSkins/Export`에 기록됩니다. 플레이 진단은 `play-results.json`을 덮어쓰므로 각 실행 후 파일을 따로 복사합니다. 출력 진단은 `probe-results.json`, `probe-cleanup.json`을 남깁니다. 가상 기기는 네이티브 Input System을 통과하며 진단에서만 백그라운드 입력을 허용합니다. 무기별 비교는 게임의 PlayerAvatar 입력 API를 사용합니다. 위치 이동·무기 선택·MP/대시 충전은 검사 준비 단계이며, 실제 공격·충돌·명중·AI·네트워크 루프는 게임이 실행합니다.

공개한 플레이 원자료의 판정 재현:

```powershell
python tools/verify_play_results.py docs/evidence/play-20261005.json --controls docs/evidence/controls-20261005.json --encounter docs/evidence/encounter-20261005.json --output-probe docs/evidence/runtime-probe-0.1.1-20261005.json --cleanup docs/evidence/runtime-cleanup-20261005.json
```

`--skins-probe-baseline`은 출력 진단의 스킨 Harmony 패치와 팩 적용을 생략하는 비교용 옵션입니다. 진단 중 새 플레이를 직접 시작하거나 두 진단을 동시에 실행하지 않습니다.

출시 판정과 아직 필요한 플레이 검사는 [검증 상태](VERIFICATION.md)에 있습니다. 규격 버전 1을 지원하는 것과 전체 테마의 정식 v1 출시 판정은 구분합니다.
