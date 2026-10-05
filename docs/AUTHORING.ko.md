# 팩 제작

게임에서 **F6 → 상세 → 템플릿 저장**을 누릅니다. `BepInEx/plugins/SephiriaSkins/Export`에 `catalog.json`, 빈 `skin.json`, `animations.json`이 생깁니다. 제작하려는 메뉴·인벤토리·대화·HUD를 연 뒤 내보내면 해당 동적 UI 경로도 수집합니다. 템플릿을 별도 폴더로 복사해 작업하세요. 다시 내보내면 Export의 템플릿 파일이 갱신됩니다.

## 간편형

`examples/simple-pack`에서 시작해 ID·이름·제작자·버전을 바꿉니다. PNG와 WAV/OGG를 팩 폴더에 넣고 `resources`에서 참조합니다. 영역과 상태를 선언하지 않으면 원본을 사용합니다. 이 예제는 앞 방향 대기 상태만 교체하므로 동작별 원본 사용을 확인할 수 있습니다. `packs/hachiware/skin.json`은 무기·효과·UI·오디오를 함께 연결하는 예입니다.

스프라이트의 `rect`는 Unity 좌표계인 왼쪽 아래 기준 `[x,y,width,height]`, `pivot`은 0~1, `pixelsPerUnit`은 Unity 단위당 픽셀 수입니다. 하나의 시트를 여러 rect로 참조할 수 있습니다. UI 패널은 `border: [left,bottom,right,top]`와 `imageType: "sliced"`로 모서리를 유지합니다.

`body/weapons/effects`의 키는 `catalog.animations`의 키를 사용합니다. `frameIndices`는 원본 배열의 **순서까지** 같아야 합니다. 정렬되지 않은 배열도 그대로 사용합니다. `frames`에 같은 수의 스프라이트 ID를 연결합니다. 같은 이미지를 여러 번 참조할 수 있습니다. FPS·상태 전이·프레임 이벤트는 게임에서 실행합니다. 원본의 null 프레임은 교체하지 않습니다.

`visuals`는 정적인 무기 렌더러에 연결합니다. `ui`는 카탈로그의 참조 경로와 컴포넌트 키로 지정합니다. 게임의 기존 UI 컴포넌트에 `sprite/material/color/anchoredPosition/sizeDelta/font/fontSize/existingFont/imageType`을 선택적으로 적용하며 텍스트 내용과 메뉴 기능은 유지합니다. RawImage는 전체 PNG만 사용할 수 있습니다. 기존 LayoutGroup이 제어하는 화면의 배치 변경은 그 레이아웃에 맞춰 확인하세요.

`audio`의 키는 `guid:{...}` FMOD 이벤트 ID입니다. 카탈로그 `path`는 읽을 수 있는 `event:/...` 경로입니다. WAV/OGG와 `scope(local/client)`, `channel(sfx/music/ambience)`, `loop`, `volume`을 연결합니다. 음악과 환경음은 client 범위입니다. 소유 문맥이 확인되지 않는 local 이벤트는 원본으로 재생합니다. 게임의 음량·음소거·정지·일시 정지·피치를 따릅니다.

설치 ZIP의 PackTool은 .NET 8 Runtime이 필요합니다. ZIP을 푼 폴더에서 실행합니다.

```powershell
tools/PackTool/PackTool.exe validate catalog/catalog-1.0.33.json MySkin
tools/PackTool/PackTool.exe zip catalog/catalog-1.0.33.json MySkin MySkin.zip
tools/PackTool/PackTool.exe template catalog/catalog-1.0.33.json NewSkin
```

소스 ZIP과 .NET SDK를 사용하는 경우 같은 기능을 `dotnet run --project tools/PackTool -- ...`로 실행합니다. CLI는 구조·헤더·경로·프레임·번들 메타데이터를 검사합니다. 실제 디코딩·Unity 자산 로딩은 게임 적용 준비 단계에서 검사합니다. ZIP 루트에 skin.json이 있어야 하며, 같은 ID의 폴더와 ZIP을 동시에 설치하지 마세요.

## Unity 제작

소스 ZIP을 풀고 **Unity 6000.3.21f1**로 `unity` 프로젝트를 엽니다. .NET SDK 8.0.425로 Core를 빌드해 `unity/Assets/Editor/Lib`에 복사합니다. TMP Essential Resources도 가져옵니다. 이 과정과 예제 내보내기는 다음 스크립트로 자동 실행할 수 있습니다.

```powershell
powershell -File tools/BuildUnity.ps1
```

`Create → SephiriaSkins → Export recipe`로 레시피를 만들고 기본 manifest TextAsset, 카탈로그 경로, 출력 폴더, 리소스 ID와 에셋을 연결합니다. 레시피를 선택해 **SephiriaSkins → Export selected recipe**로 내보냅니다. Sprite·TMP_FontAsset·Material·파티클 GameObject는 Windows64 번들에 넣습니다. AudioClip은 원본 WAV/OGG를 옆에 복사하고 FMOD용 리소스로 연결합니다. 본체 애니메이션은 Animator Controller가 아닌 동일한 프레임 목록으로 작성합니다.

파티클에는 Transform/ParticleSystem/ParticleSystemRenderer만 허용하며 collision/trigger 모듈은 비활성화합니다. 제작자 C#·DLL·물리·콜라이더·오디오 컴포넌트는 팩에 포함하지 않습니다. TMP_FontAsset의 Unity 제공 형식 참조는 허용합니다. 새 글꼴에는 게임 글꼴 fallback이 연결되므로 한국어 등 원래 지원하던 글리프를 사용할 수 있습니다.

**SephiriaSkins → Build example pack**은 `examples/unity-pack`에 새 Sprite·글꼴·재질·파티클·OGG 예제를 생성합니다. CRC·SHA256·Unity 버전·플랫폼을 기록하고 실제 팩 로더로 재검사합니다. 다른 Unity 버전에서 만든 번들은 지원 버전으로 다시 내보내세요.

Python 도구의 의존성은 `tools/requirements.txt`에 있습니다. Unity 예제를 다시 내보낸 후 `python tools/build_demo_pack.py`를 실행하면 내장 팩의 글꼴 번들 참조와 팬아트 연결을 갱신합니다. 생성된 글꼴과 리소스의 라이선스도 함께 배포하세요.
