# 설치와 문제 해결

지원 기준: 세피리아 1.0.33, Unity 6000.3.21f1, Windows x64, Mono, BepInEx 5.4.23.5. 다른 게임 DLL/Unity 버전이면 팩 적용이 비활성화되고 이유를 표시합니다.

게임을 종료한 뒤 ZIP을 게임 실행 파일 옆에 풉니다. 기존 BepInEx 폴더에 합쳐 설치합니다. `BepInEx/plugins/SephiriaSkins/SephiriaSkins.Plugin.dll`이 있어야 합니다. BepInEx 자체는 [공식 배포](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5)에서 Windows x64용을 설치합니다.

F6로 선택창을 열고 팩을 적용합니다. 선택은 `BepInEx/config/dev.sephiria.skins.cfg`에 저장됩니다. `Keys.Selector`를 다른 Input System Key 이름으로 바꿀 수 있습니다. Build Overlay 기본 단축키는 F7/F8/F9이므로 기본 F6과 겹치지 않습니다. 다른 모드가 F6을 사용하면 키를 바꾸세요.

외부 팩의 ZIP 최상단은 skin.json이어야 합니다. `MySkin/skin.json`을 포함한 ZIP은 설치 루트 규격과 다릅니다. 같은 ID의 폴더와 ZIP을 동시에 설치하면 두 팩 모두 적용 대상에서 제외합니다.

`도트 감성`은 본체·무기·이펙트를 도트 또는 부드러운 원본 해상도로 표시합니다. `게임 UI`를 OFF로 하면 현재 스킨을 유지하면서 UI를 원본으로 복원합니다. 두 설정은 즉시 적용되고 다음 실행에도 유지됩니다. 설정 파일의 `Appearance.PixelArt`, `Appearance.GameUi`에 해당합니다. 하치와레의 모든 무기는 파란 사스마타로 표시되며 공격·방어·투사체·판정은 원래 무기를 따릅니다.

문제가 생기면 F6 → 원본을 누릅니다. 선택창은 테마 적용 대상에 포함되지 않습니다. 새로고침은 현재 선택 ID를 재검사하며, 오류가 있으면 기존 정상 테마를 유지합니다. 상세에서 제작자·버전·오류를 확인하고 템플릿을 저장할 수 있습니다. 선택창을 사용할 수 없는 경우 게임을 종료하고 설정 파일의 `Selected =`를 비웁니다. 모드 제거는 게임 종료 후 `plugins/SephiriaSkins` 폴더를 제거하면 됩니다.

로그는 `BepInEx/LogOutput.log`의 `Sephiria Skins` 항목입니다. 오류에는 문제 파일, 프레임, 카탈로그 키 또는 번들 정보가 표시됩니다. Unity 버전 불일치 번들은 그 버전의 에디터로 다시 내보내야 합니다. PNG 디코딩/오디오 디코딩 오류는 적용 준비 단계에서 현재 팩을 유지합니다.

협동 외형은 내 클라이언트에서만 변경됩니다. 다른 참가자가 같은 스킨을 보는 기능이나 스킨 파일을 전송하는 기능은 없습니다. 현재 검증 범위는 VERIFICATION.md를 확인하세요.
