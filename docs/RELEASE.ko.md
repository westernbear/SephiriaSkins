# 빌드와 배포

설치 ZIP과 소스 ZIP은 `powershell -File tools/BuildRelease.ps1`로 만듭니다. .NET SDK 8.0.425, rg, 게임 1.0.33 및 BepInEx 5.4.23.5의 로컬 DLL 참조가 필요합니다. 게임 경로가 다르면 `-GamePath`를 지정합니다. 게임 DLL이나 BepInEx를 소스 저장소에 추가하지 않습니다.

직접 빌드와 검사:

```powershell
dotnet build -c Release
dotnet test tests/SephiriaSkins.Tests -c Release
dotnet run --project tools/PackTool -- validate catalog/catalog-1.0.33.json examples/simple-pack
```

게임 경로가 기본 Steam 경로와 다르면 빌드에 `-p:GamePath="D:\SteamLibrary\steamapps\common\Sephiria"`를 전달합니다. Core는 Unity 없는 규격·로더, Plugin은 게임 연동, PackTool은 팩 검사·ZIP·빈 템플릿 도구입니다.

스크립트는 Release 빌드, 단위 검사, `packs`·`examples`의 모든 팩 자동 발견 검사, PackTool의 Windows x64 게시 및 실행 검사를 거칩니다. 설치 ZIP에 모든 내장 팩을 포함하고 내장 팩별 ZIP도 생성·검사합니다. 공식 BepInEx 5.4.23.5 x64 ZIP을 고정한 SHA256으로 받아 `winhttp.dll`·`doorstop_config.ini`·`BepInEx/core`와 함께 포함합니다. Doorstop의 대응 소스 ZIP과 런타임 라이선스도 포함합니다. 설치 ZIP을 다시 열어 개별 런타임 해시·루트 부트스트랩·내장 팩·허용 DLL을 검사하며 게임 DLL·사용자 설정·캐시는 거부합니다. 소스 ZIP은 `.gitignore`에 따라 로컬 조사 파일·빌드 출력·Unity 캐시·게임 데이터를 제외합니다. `dist`에 설치·소스·개별 스킨 ZIP과 전체 SHA256SUMS가 생깁니다. 버전은 `Directory.Build.props`에서 읽습니다.

Unity 예제를 재생성하려면 `powershell -File tools/BuildUnity.ps1` 후 `python tools/build_demo_pack.py`를 실행합니다. 정확한 에디터 버전과 번들 해시를 검사하고 팩을 갱신합니다. 공개 소스에는 Unity Editor 소스와 프로젝트 설정, Python 의존성, 두 예제, 카탈로그, 라이선스와 검증 기록이 포함됩니다.

카탈로그 재생성에는 `tools/requirements.txt`의 Python 의존성을 설치합니다. 게임에서 내보낸 같은 설치본의 catalog.json을 `--runtime`으로 전달하면 UI·FMOD 경로를 합칩니다. DLL 해시·Unity 버전·카탈로그 ID가 다르면 합치지 않습니다. 본체·무기의 역할은 정적 조사 결과를 유지합니다.

```powershell
python tools/generate_catalog.py "C:\Program Files (x86)\Steam\steamapps\common\Sephiria" --runtime "C:\Program Files (x86)\Steam\steamapps\common\Sephiria\BepInEx\plugins\SephiriaSkins\Export\catalog.json"
```

GitHub 저장소는 [westernbear/SephiriaSkins](https://github.com/westernbear/SephiriaSkins)입니다. 추적 소스를 main에 올리고 Releases에 설치 ZIP·소스 ZIP·개별 스킨 ZIP·SHA256SUMS를 첨부합니다. 다음 배포 0.2.0은 프리릴리스로 표시합니다. 게임 설치본을 새로 조사한 뒤 카탈로그를 갱신한 경우 런타임 검증을 다시 수행하세요.

진단 실행은 일반 설치에 필요하지 않습니다. 게임이 종료된 상태에서 Steam 시작 옵션에 아래 중 하나를 넣고 실행합니다. 실행 후 시작 옵션을 비웁니다. 각 진단은 메모리 전용 프로필을 만들며 기존 슬롯과 Steam Cloud에 저장하지 않습니다. 선택 설정·언어·해상도·입력 설정은 종료 시 복원합니다. 기존 Build Overlay 설정은 그대로 함께 로드합니다.

| 시작 옵션 | 기록 |
|---|---|
| `--skins-playtest --skins-probe-exit` | 설치본의 모든 활성 WeaponSimple 무기의 원본/테마 비교, 코스튬·층 이동·사망/부활·언어·해상도·25회 교체·호스트 재시작 |
| `--skins-playtest --skins-playtest-controls --skins-probe-exit` | 가상 Input System 기기의 이동/공격, F6/Esc, 긴 이름 말줄임 |
| `--skins-playtest --skins-playtest-visual --skins-probe-exit` | 모든 활성 무기의 실제 렌더러·이펙트 도트 ON/OFF 캡처, 앞/뒤·부활, 도트/UI 버튼·설정 저장, UI OFF 상태의 재로드 |
| `--skins-playtest --skins-playtest-encounter --skins-probe-exit` | 카타나 특수 공격과 실제 던전 적 AI 전투·음악 전환 |
| `--skins-playtest --skins-cycle-packs=팩.ID,다른팩.ID --skins-test-weapons=defaults --skins-probe-exit` | 지정 순서로 두 바퀴 즉시 교체, 기존 무기 인스턴스·새 장착·마스크·UI OFF 재로드·자원 해제 |
| `--skins-playtest --skins-playtest-persisted --skins-test-pack=팩.ID --skins-probe-exit` | 미리 저장한 선택·도트/UI 설정을 새 프로세스의 일반 시작 경로로 로드 |
| `--skins-playtest --skins-playtest-combat-only --skins-test-weapons=무기ID목록 --skins-probe-exit` | 지정된 네이티브 공격 입력의 원본/테마 비교와 종료·복원만 실행 |
| `--skins-probe --skins-probe-host --skins-probe-lobby --skins-probe-packs --skins-probe-zip --skins-probe-exit` | 설치된 유효 팩의 폴더/ZIP, 본체 출력 fixture·Unity 리소스·UI·오디오·풀 복원 |

모든 시작 옵션에 `--skins-test-pack=팩.ID`를 추가할 수 있습니다. 플레이·화면 진단의 기본값은 `fan.hachiware`이며, 출력 진단은 팩 ID를 생략하면 설치된 모든 유효 팩을 검사합니다. 같은 ID의 폴더와 ZIP을 동시에 설치하지 않습니다. JSON과 게임 렌더 캡처는 `BepInEx/plugins/SephiriaSkins/Export/diagnostics/<팩 ID>/<UTC 시각-고유 실행 ID>`에 기록되어 다른 실행을 덮어쓰지 않습니다. `run.json`에는 실행 인자, `manifest-<팩 ID>.json`에는 실제 기대 리소스가 기록됩니다. 플레이 진단은 `play-results.json`, 출력 진단은 `probe-results.json`·`probe-cleanup.json`을 남깁니다. 무기 목록에는 비활성·누락 prefab·지원하지 않는 장착 방식과 아직 검사하지 않은 변형의 이유를 기록합니다. 기본/대시/특수 3입력의 비교를 모든 연속 콤보·충전 시간·지뢰 지연 기폭까지 검사했다고 보고하지 않습니다.

가상 기기는 네이티브 Input System을 통과하며 진단에서만 백그라운드 입력을 허용합니다. 무기별 비교는 게임의 PlayerAvatar 입력 API를 사용합니다. 위치 이동·무기 선택·MP/대시 충전은 검사 준비 단계이며 실제 공격·충돌·명중·AI·네트워크 루프는 게임이 실행합니다. 전투 비교는 준비된 팩을 재사용하고 재로드·자원 해제는 별도 fixture로 검사합니다.

짧은 회귀 검사에는 `--skins-test-weapons=defaults`, 문제 재현에는 `--skins-test-weapons=19,12`처럼 선택할 수 있습니다. 선택 범위와 전체 무기 목록은 결과에 함께 기록하며 제한된 실행을 전체 무기 통과로 보고하지 않습니다. 네이티브 입력 메서드가 빈 기본 구현인 레거시 무기/입력은 실제 클래스와 제외 사유를 기록합니다. 강화 카타나는 기본 공격 중에 특수 입력을 보내며, 골렘은 애니메이션 콜백 대신 실제 네이티브 투사체 생성 이벤트를 관찰합니다. 강화 스킬이 초기 프로필의 50 MP보다 많은 비용을 요구할 수 있어 전투·화면 fixture에서는 메모리 전용 플레이어 MP 상한을 임시로 1000으로 준비하고 검사 뒤 복원합니다. 진단을 중단하려면 해당 실행 폴더에 빈 `cancel.request` 파일을 만듭니다. 진단은 복원을 수행하고 취소 사실을 실패로 기록합니다.

`python tools/verify_combat_results.py <실행 폴더>/play-results.json`은 전투 전용 실행의 선언된 범위와 실제 종료를 검사합니다. `--skins-probe-baseline`으로 스킨 Harmony 패치를 끈 원본끼리의 비교에는 판정기에도 `--baseline`을 지정합니다. `python tools/audit_combat_results.py <결과 파일> --output <새 감사 JSON>`은 중단 자료까지 포함하여 일치·실패·미실행 항목을 모두 열거하며, 출시 통과 판정 대신 조사에 사용합니다. 원본끼리도 다른 항목의 차이를 테마의 동일성 통과로 바꾸지 않습니다.

공개한 이전 플레이 원자료의 판정 명령입니다. 최신 판정기는 `Private` 로비 타입도 요구하므로 이전 `Invisible` 기록은 이 조건에서 거부합니다. 이전 공격 비교·언어·자원 검사는 원자료에 남아 있으며, 이번 재검증과 합쳐 최종 빌드의 전체 통과로 보고하지 않습니다.

```powershell
python tools/verify_play_results.py docs/evidence/play-0.1.2-20261005.json --controls docs/evidence/controls-0.1.2-20261005.json --encounter docs/evidence/encounter-0.1.2-20261006.json --output-probe docs/evidence/runtime-probe-0.1.2-20261005.json --cleanup docs/evidence/runtime-cleanup-0.1.2-20261005.json
python tools/verify_visual_results.py docs/evidence/visual-0.1.2-20261005.json docs/evidence/visual-cleanup-0.1.2-20261005.json --manifest packs/hachiware/skin.json
```

`--skins-probe-baseline`은 출력 진단의 스킨 Harmony 패치와 팩 적용을 생략하는 비교용 옵션입니다. 진단 중 새 플레이를 직접 시작하거나 두 진단을 동시에 실행하지 않습니다.

`--skins-playtest-with-visual`, `--skins-playtest-with-encounter`, `--skins-playtest-with-controls`는 같은 메모리 호스트에서 해당 검사를 전체 플레이 진단에 추가합니다. `--skins-soak-minutes=60`은 이후 60분 동안 기본 무기의 네이티브 공격·이동, 네 가지 도트/UI 조합, 주기적 재로드를 반복합니다. `soak-actions.jsonl`과 `soak-progress.json` 및 최종 플레이 결과에 실제 시간·행동 수·오류·완료 여부를 남깁니다. 자동 입력을 사용한 마을 호스트 내 연속 진단이며 수동 던전 탐험이나 원격 참가자 검증을 뜻하지 않습니다.

원본에서도 재현된 오류를 합의하여 제외하는 경우 `--skins-skip-trials=19:special --skins-skip-reason=native-greatsword-NullReferenceException-without-skin-hooks`처럼 정확한 무기 ID/입력과 사유를 함께 지정합니다. 범위 밖 ID, 알 수 없는 입력, 비어 있는 사유는 거부합니다. 무기 목록의 `skippedModes`와 `run.json`에 제외 항목을 남기며, 해당 입력은 통과한 공격 수에 넣지 않습니다. 폴더/ZIP·출력/복원은 `python tools/verify_output_results.py <probe-results.json> <probe-cleanup.json>`으로 플레이 결과와 별도로 판정할 수 있습니다.

순환 검사는 `python tools/verify_cycle_results.py <play-results.json>`으로 판정합니다. 재실행 검사는 게임이 꺼진 상태에서 모드 설정을 백업하고 선택 ID와 두 스위치를 준비한 뒤 실행합니다. `python tools/verify_persisted_results.py <play-results.json> --pixel off --ui off`처럼 실제 준비값을 별도로 전달합니다. 프로세스가 끝나면 원래 설정 파일을 복원하고 기존 세이브 해시가 같은지 확인합니다. 순환·재실행 fixture는 전체 공격 검사나 실제 적 전투를 대신하지 않습니다.

설치한 내장 팩 전체의 반복 절차는 `powershell -File tools/TestGamePacks.ps1 -SoakMinutes 60`으로 실행할 수 있습니다. 게임을 종료하고 Steam에 로그인한 상태에서 사용합니다. 소스와 설치된 manifest·리소스 해시를 먼저 비교하고 팩별 기본 18개 공격, 도트 ON/OFF 렌더 캡처, 실제 적 전투, 선택창, 15개 언어·두 해상도, 두 번의 팩 순환, 전체 폴더/ZIP 출력 검사를 순서대로 수행합니다. 마지막 팩에서 지정한 연속 실행을 수행합니다. 각 팩의 두 스위치 ON/ON 및 OFF/OFF는 설정 파일을 백업한 뒤 별도 새 프로세스에서 확인하고 원래 파일을 복원합니다. 기존 세이브 해시가 달라지거나 판정기가 실패하면 중단하고 로컬 증거를 유지합니다. `-PackIds fan.momonga`처럼 특정 팩만 선택할 수 있으며 `-SkipPlay`, `-SkipCycle`, `-SkipOutput`, `-SkipPersisted`로 생략한 단계는 완료로 보고하지 않습니다. 이 스크립트도 네이티브 API와 가상 기기를 사용하는 자동 진단이며 화면 구성은 생성된 PNG를 별도로 열어 확인해야 합니다.

짧은 적용·교체 회귀 검사에는 `powershell -File tools/TestGamePacks.ps1 -SkipPlay -SkipOutput -SkipPersisted`를 사용합니다. 전체 팩을 한 호스트에서 교체하고 UI OFF 재로드·원본 무기/마스크·자원 복원을 확인합니다. 폴더/ZIP과 오디오·본체 출력만 확인하려면 `-SkipPlay -SkipCycle -SkipPersisted`를 사용합니다. 이 짧은 실행은 팩별 전체 공격·실제 적 전투·장시간 플레이·설정 재실행 검사로 계산하지 않습니다.

출시 판정과 확인 범위는 [검증 상태](VERIFICATION.md)에 있습니다. 0.2.0은 사용자가 줄인 검증 범위를 기록한 프리릴리스이며, 규격 버전 1 지원을 전체 콘텐츠 검증으로 확대하지 않습니다.
