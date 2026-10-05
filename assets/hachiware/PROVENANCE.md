# 하치와레 리소스 출처

이 팩의 PNG는 이 프로젝트를 위해 imagegen 내장 도구로 새로 생성한 하치와레 팬아트입니다. 원작: 나가노의 치이카와. 공식 스킨이 아닙니다. 캐릭터 권리는 해당 권리자에게 있습니다. 이전 atlas.png와 effects.png는 소스의 참고 이력에만 남기며 현재 팩은 body-unarmed.png, sasumata.png, effects-clean.png, ui.png를 사용합니다.

사용한 프롬프트는 prompts.txt에 기록했습니다. 생성 결과를 수정하거나 게임 이미지를 추출해 사용하지 않았습니다. PNG 알파를 그대로 유지하고 skin.json의 rect로 시트 영역을 참조합니다.

0.1.2의 body-unarmed.png, sasumata.png, effects-clean.png도 imagegen 내장 도구로 생성했습니다. 본체의 무기·마법을 분리하고 각 셀에 여백을 두었습니다. 사스마타의 형태·색은 [공식 하치와레 사스마타](https://chiikawamarket.jp/products/4970093800046)를 참고했으며 공식 상품 이미지는 포함하지 않습니다. 런타임은 개별 셀을 독립 텍스처로 읽고, 도트 ON일 때 픽셀 커버리지 샘플링을 적용합니다. 생성 프롬프트는 같은 파일에 추가 기록했습니다.

audio/*.wav는 tools/generate_audio.py가 합성한 새 비언어 데모 발성과 효과음, 오리지널 음악 루프입니다. 실제 캐릭터 성우의 녹음을 사용하지 않았습니다. 음성 자연스러움과 게임 상황별 음악 연결은 출시 검토 대상입니다.

theme.bundle의 새 TMP 글꼴은 Liberation Sans에서 제작한 에셋이며 한국어 등은 게임 글꼴 fallback을 사용합니다. SIL OFL 라이선스는 THIRD_PARTY_NOTICES.md와 LiberationSans-OFL.txt에 있습니다.
