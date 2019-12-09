# ACT.Hojoring
[![Downloads](https://img.shields.io/github/downloads/cosmolen/ACT.Hojoring_KR/total.svg)](https://github.com/cosmolen/ACT.Hojoring_KR/releases)
[![License](https://img.shields.io/badge/license-BSD--3--Clause-blue.svg)](https://github.com/anoyetta/ACT.Hojoring/blob/master/LICENSE)

"호조링"
Advanced Combat Tracker에서 사용할 수 있는 FFXIV용 플러그인 패키지입니다.
스페스페/울트라스카우터/TTS윳쿠리를 패키징하여 "호조링"이라는 명칭으로 릴리즈합니다.

### ACT.Hojoring 한국 서버 지원 버전
ACT.Hojoring 최신 버전을 한국 서버에서도 사용할 수 있도록 수정하여 릴리즈합니다.
UI를 한국어로 번역한 것 외에도 내부 동작을 한국 서버에 맞도록 수정하였습니다.

#### [SpecialSpellTimer](https://github.com/anoyetta/ACT.Hojoring/wiki/SpecialSpellTimer)
통칭 "스페스페"
스킬의 재사용 대기시간을 표시하기 위한 플러그인 입니다. 기본적으로 로그에 대한 트리거로서 작동합니다.

#### [UltraScouter](https://github.com/anoyetta/ACT.Hojoring/wiki/UltraScouter)
통칭 "스카우터"
대상의 HP나 거리, 주의의 몹을 감지하는 등의 기능을 가진 플러그인입니다. 실시간 메모리 정보를 읽어들여 표시합니다.

#### [TTSYukkuri](https://github.com/anoyetta/ACT.Hojoring/wiki/Yukkuri)
통칭 "윳쿠리"
ACT 자체 TTS기능을 윳쿠리 실황으로 유명한 AquesTalk&trade; 등으로 변경합니다. 거의 모든 TTS엔진을 지원합니다. 또한 TTS를 디스코드 BOT을 경유하여 디스코드의 음성 채팅으로 송출하는 기능도 있습니다.

## 최신 릴리즈
### **[DOWNLOAD Lastest-Release](https://github.com/cosmolen/ACT.Hojoring_KR/releases/latest)**
[pre-release](https://github.com/cosmolen/ACT.Hojoring_KR/releases)

## 설치
### 수동 설치
1. 런타임 설치하기
**[Visual Studio 2017용 Microsoft Visual C++ 재배포 가능 패키지](https://go.microsoft.com/fwlink/?LinkId=746572)**  
**[.NET Framework 4.7.2](https://www.microsoft.com/net/download/thank-you/net472)**  
를 설치합니다.

2. 최신 버전 다운로드하기
[최신 릴리즈](https://github.com/cosmolen/ACT.Hojoring_KR/releases/latest)에서 다운로드 합니다.

3. 압축 풀기
다운로드한 플러그인을 원하는 폴더에 압축 해제합니다.

4. ACT에 추가하기
ACT에 플러그인으로 추가합니다. 각 플러그인은 따로 등록해야 합니다.
필요한 플러그인만 등록해주세요. 물론 모두 등록해도 괜찮습니다. 
    * ACT.SpecialSpellTimer.dll
    * ACT.UltraScouter.dll
    * ACT.TTSYukkuri.dll

자세한 순서는 **[anoyetta の開発記録 - ACTおよび補助輪のインストール（完全版）](https://www.anoyetta.com/entry/hojoring-setup)**를 읽어주세요 (일본어).

### 작동환경
* [Windows 10](https://www.microsoft.com/software-download/windows10) 이상 (Windows 7/8/8.1에서는 동작하지 않습니다)
* .NET Framework 4.7.1 이상

## 사용법
**기본적인 사용법은 [Wiki](https://github.com/anoyetta/ACT.Hojoring/wiki)를 참고해주세요 (일본어).**

##### Hojoring 설정 파일에 대해
1. 저장 위치
```
%APPDATA%\anoyetta\ACT
```  
에 모두 저장됩니다. 따라서 OS 재설치 시에는 이 폴더를 통째로 백업하여 재설치 후에 복원하면 재설치 전의 설정을 유지할 수 있습니다.

2. 설정 파일
```
ACT.SpecialSpellTimer.config
ACT.TTSYukkuri.config
ACT.UltraScouter.config
```
각각 스페스페, 윳쿠리, 스카우터 설정 파일입니다.

3. 스페스페 트리거 설정 파일
```
ACT.SpecialSpellTimer.Panels.xml
ACT.SpecialSpellTimer.Spells.xml
ACT.SpecialSpellTimer.Telops.xml
ACT.SpecialSpellTimer.Tags.xml
```  
스페스페 트리거 설정 파일입니다. 각각 스펠 패널, 스펠, 티커, 태그 설정 파일입니다.

## 라이센스
[3-Clause BSD License](LICENSE)  
&copy; 2014-2018 anoyetta  

단 다음과 같은 행위는 금지합니다.
* 배포된 바이너리에 대해 리버스 엔지니어링을 하여 내부 해석하는 행위
* 배포된 바이너리의 전체 또는 일부를 본래 목적과 다른 목적으로 사용하는 행위

## 문의
### 오류가 발생했다면
개발자에게 질문할 경우 아래 정보를 첨부해주세요.
* ACT 본체 로그 파일
* 해당 플러그인 로그 파일
* (있다면) 오류 메시지 스크린샷

##### [Help] → [지원 정보 저장] 버튼을 누르면 필요한 정보를 저장할 수 있습니다.
![help](https://github.com/cosmolen/ACT.Hojoring_KR/blob/master/images/help.png?raw=true)

##### ACT나 플러그인 자체가 실행되지 않는 등 UI로 정보를 저장할 수 없을 경우 다음과 같은 폴더에서 수집할 수 있습니다.
```
%APPDATA%\Advanced Combat Tracker\Advanced Combat Tracker.log
%APPDATA%\anoyetta\ACT\logs\ACT.Hojoring.YYYY-MM-DD.log
```

### 스펠이 동작하지 않아요
앞서 설명한 정보에 추가로 다음과 같은 정보가 필요합니다.
* 등록하고 싶은 로그 (스킬 이름, 버프 이름 등)
* 해당 스펠이나 티커 설정

### 문의 서식
**[New Issue](https://github.com/anoyetta/ACT.Hojoring/issues/new/choose)**  
에서 티켓을 등록해주세요. [issues](https://github.com/cosmolen/ACT.Hojoring_KR/issues)에서 기존 이슈, 현재 상황을 확인할 수 있습니다.  
중복 질문은 자제 부탁드립니다. 

### DONATION (원본 개발자 anoyetta님)
I can receive only  **[Amazon eGift Card](https://www.amazon.com/dp/B004LLIKVU)**   
sendto: anoyetta(at)gmail.com

---
기재된 회사명, 제품명, 시스템명 등은 각 회사의 상표 또는 등록상표입니다.
Copyright &copy; 2010 - 2019 SQUARE ENIX CO., LTD. All Rights Reserved.
