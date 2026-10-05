# AI와 함께 스킨 만들기

이 문서를 AI 코딩 도구에 전달하면 이 저장소의 실제 규격으로 외부 스킨 팩을 제작할 수 있습니다. PNG 제작은 Unity 없이 가능합니다. 글꼴·재질·파티클을 추가할 때만 Unity 제작 경로를 사용합니다.

## AI에게 전달할 요청문

아래를 복사하고 대괄호 내용을 바꾸세요.

```text
SephiriaSkins 저장소의 docs/AI-SKIN-GUIDE.ko.md, docs/SPEC-v1.md,
docs/AUTHORING.ko.md와 catalog/catalog-1.0.33.json을 먼저 읽어 줘.

캐릭터: [이름과 참고 자료]
팩 ID: [my.character처럼 고유한 영문 소문자 ID]
제작자: [이름]
범위: [본체 / 본체와 무기 / 전체 테마]
분위기와 색: [원하는 스타일]
무기: [캐릭터의 실제 무기 / 없음]

packs/[팩 폴더]에 skin.json과 새 자산을 만들어 줘.
키·상태·프레임 수·frameIndices를 추측하지 말고 실제 카탈로그에서 가져와.
본체 그림에 무기와 이펙트를 함께 그리지 말아 줘.
캐릭터에게 무기가 있으면 모든 세피리아 무기를 그 무기로 표시하고,
없으면 무기 영역을 생략해 줘. 공격 방식과 판정은 바꾸지 마.
도트 감성과 게임 UI ON/OFF는 기존 모드 설정을 그대로 따르게 해 줘.
외부 팩에 실행 코드나 게임 DLL을 넣지 마.
로더로 폴더와 ZIP을 모두 검증하고, 사용 가능한 경우 게임에서 확인해 줘.
실게임을 실행하지 못했다면 검증한 범위와 남은 검사를 구분해서 알려 줘.
```

## AI의 작업 순서

1. [규격](SPEC-v1.md), [제작 방법](AUTHORING.ko.md), `examples/simple-pack/skin.json`, `packs/hachiware/skin.json`을 읽습니다. 설치본이 다르면 게임 선택창의 **상세 → 템플릿 저장**으로 현재 카탈로그를 얻습니다. 지원하지 않는 게임 버전에 호환 ID만 억지로 맞추지 않습니다.
2. 제작할 영역을 정하고 고유 ID로 새 폴더를 만듭니다. 기존 팩을 덮어쓰지 않습니다. 미선언 영역은 게임 원본이므로 필요 없는 UI·오디오·무기를 추가하지 않습니다.
3. 카탈로그에서 해당 영역의 모든 상태와 프레임 배열을 읽고, 필요한 포즈·앞/뒤 방향·무기·효과를 목록으로 만듭니다. 표시 이름만으로 키를 합치지 않습니다.
4. 새 PNG/WAV/OGG 자산을 만들고 출처·라이선스·생성 프롬프트를 기록합니다. PNG는 실제 투명 알파를 사용합니다. 이미지 제작 도구가 없다면 자료가 필요한 사실을 알리고 완성 자산인 것처럼 빈 이미지나 도형을 제출하지 않습니다.
5. `skin.json`에 실제 리소스와 카탈로그 키를 연결합니다. 프레임 배열과 `frameIndices`는 원본 순서까지 보존합니다.
6. 아래 명령으로 폴더와 ZIP을 검사합니다. 오류를 수정한 뒤 게임에서 적용·원본 복원·재로드를 확인합니다.

## 꼭 지킬 자산 규칙

| 영역 | 규칙 |
|---|---|
| 본체 | 앞/뒤·대기·이동·공격·피격·사망 등 선언한 상태에 필요한 프레임을 연결합니다. 몸에 무기·마법·그림자를 합쳐 그리지 않습니다. |
| 시트 | 정확한 셀 배치와 충분한 투명 여백을 사용합니다. 이웃 셀의 눈·발·반짝이가 rect 안에 들어오지 않아야 합니다. 좌표는 Unity의 왼쪽 아래 기준입니다. |
| 무기 | 캐릭터의 무기를 별도 이미지로 만듭니다. `weapons`는 애니메이션, `visuals`는 정적 렌더러입니다. 무기가 없으면 둘의 무기 바인딩을 생략합니다. |
| 보조 무기 레이어 | 전체 새 무기를 중복해서 그리지 않습니다. 장식용 AddOn에는 `hide: true`를 사용할 수 있습니다. 스텐실/마스크는 기본 렌더러와 같은 외형·정렬을 유지합니다. |
| 이펙트 | 원본 프레임마다 다른 위상 이미지를 연결합니다. `fitOriginal: true`로 원본의 크기·중심점을 따를 수 있습니다. 빈 원본 프레임과 생성·종료 타이밍은 유지됩니다. |
| UI | 기존 Image/RawImage/TMP를 꾸밉니다. 텍스트의 의미·숫자·현지화와 버튼 기능은 바꾸지 않습니다. 선택창은 테마 대상에서 제외됩니다. |
| 글꼴·재질·파티클 | 정확한 Unity 버전의 번들로 제작합니다. 새 TMP 글꼴에는 게임 fallback이 연결됩니다. 파티클에 스크립트·물리·충돌·오디오 컴포넌트를 넣지 않습니다. |
| 오디오 | WAV/OGG만 참조합니다. 로컬 음성과 효과음은 `scope: local`, 음악과 UI 소리는 용도에 맞게 `scope: client`를 사용합니다. |

`fitOriginal`과 `hide`의 기본값은 false입니다. `hide: true`에는 sprite/material/fitOriginal을 같이 선언하지 않습니다. 자세한 허용값은 [SPEC-v1](SPEC-v1.md)이 기준입니다. 도트 ON은 런타임에서 세계 스프라이트를 픽셀로 변환하고, OFF는 원본 해상도를 사용합니다. 두 종류의 자산을 따로 만들 필요는 없습니다. 게임 UI 적용 여부는 사용자가 선택합니다.

## 카탈로그 연결 예시

다음 Python 예시는 실제 본체 상태의 프레임 배열을 읽는 방법입니다. `choose_sprite`는 제작한 포즈 목록에 맞춰 직접 구현해야 합니다.

```python
import json
from pathlib import Path

catalog = json.loads(Path("catalog/catalog-1.0.33.json").read_text(encoding="utf-8"))
for key, state in catalog["animations"].items():
    if state["role"] != "body":
        continue
    print(key, state["state"], state["frameIndices"], state["spriteNames"])
    # manifest["body"][key] = {
    #     "frames": [choose_sprite(state, n) for n in range(len(state["frameIndices"]))],
    #     "frameIndices": state["frameIndices"],
    # }
```

하치와레의 실제 생성기는 [tools/build_demo_pack.py](../tools/build_demo_pack.py)입니다. 이 파일을 새 캐릭터 작업에서 그대로 실행하면 기존 내장 팩을 갱신하므로, 새 팩용 생성기를 별도 파일로 만드세요. 예제 팩의 ID를 그대로 복사하면 중복 ID로 둘 다 제외됩니다.

## 검증과 전달

소스 체크아웃에서 .NET 8 SDK로 실행합니다. 생성·검증 도구는 게임 DLL 없이 사용할 수 있습니다.

```powershell
dotnet run --project tools/PackTool -- template catalog/catalog-1.0.33.json packs/my-character
# 위에서 만든 skin.json과 자산을 편집한 다음:
dotnet run --project tools/PackTool -- validate catalog/catalog-1.0.33.json packs/my-character
dotnet run --project tools/PackTool -- zip catalog/catalog-1.0.33.json packs/my-character my-character.zip
dotnet run --project tools/PackTool -- validate catalog/catalog-1.0.33.json my-character.zip
```

`template`은 새 폴더 또는 빈 폴더에서만 실행합니다. `zip` 출력 경로도 새 파일이어야 합니다. 배포 ZIP에는 skin.json이 최상단에 있어야 하며 게임 DLL·실행 코드·로컬 경로를 포함하지 않습니다. 설치 ZIP의 `tools/PackTool/PackTool.exe`로도 같은 인자를 사용할 수 있습니다.

게임에 설치한 뒤 F6로 적용하고 앞/뒤·이동·기본/대시/특수 공격·피격·사망/부활을 확인합니다. 무기 중복, 공중의 조각, 과도한 이펙트, 반사, 언어와 UI를 살펴봅니다. 두 ON/OFF 스위치와 원본 복원·재로드도 확인합니다. 단순히 로더가 성공했다고 플레이 검증을 완료한 것으로 보고하지 않습니다.

AI의 최종 전달물은 팩 폴더/ZIP, 제작 자산 출처, 실행한 검증 명령과 결과, 실게임 확인 범위입니다. 실패한 팩은 기존 정상 팩을 유지하므로 오류를 숨기지 말고 로그의 파일·카탈로그 키를 이용해 수정합니다.
