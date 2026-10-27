param([string]$Version = '1.2.0')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$releaseRoot = Join-Path $repoRoot 'release'
$packageName = "VK01Studio-v$Version-win-x64"
$packageRoot = Join-Path $releaseRoot $packageName
if (Test-Path -LiteralPath $packageRoot) { throw "Package already exists: $packageRoot. Choose a new version or inspect it before replacing." }
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repoRoot 'build\VK01Studio.exe') -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination $packageRoot
$quickKo = @'
VK01 Studio — ANTICATER VK01 Windows 커스텀 소프트웨어

1. USB로 연결한 VK01의 클릭 키를 ANTICATER 앱에서 F13으로 저장합니다.
   ANTICATER 설정 프로그램: https://anticater.com/pages/download
   동봉 2.4GHz 수신기는 클릭을 Media → Treble +로 저장하고,
   VK01 Studio의 '노브 입력 방식'에서 '2.4GHz 수신기 · Treble +'를 선택하세요.
2. VK01Studio.exe를 실행합니다. 우상단에서 EN / 한국어를 전환합니다.
3. 출력 장치에서 두 개 이상 선택하고 전환 순서를 정합니다.
4. 클릭별 동작과 시간을 조절하고 '저장 및 적용'을 누릅니다.
5. 저장된 설정은 실제 동작합니다. 실행 없이 시험하려면 연습 모드를 켜세요.
6. 왼쪽 아래에서 Windows 자동 시작을 켜고, Windows 시작 앱에서도 켜져 있는지 확인하세요.

기본값: 한 번=재생/일시정지, 두 번=다음 곡, 세 번=이전 곡, 길게=출력 전환.
설정 창을 닫아도 트레이에서 동작합니다. 종료는 트레이 메뉴에서 합니다.
시간/동작 설정은 PC에 저장되며 프로그램 실행 중에 적용됩니다.
데이터 위치: %LOCALAPPDATA%\VK01Studio
지원: Windows 10/11 x64, .NET Framework 4.8+. USB의 지정된 VK01 리비전에서 검증.
추가 드라이버는 필요하지 않습니다. 비공식 커뮤니티 앱입니다.
자동 시작을 사용한다면 실행 파일을 고정된 폴더에 보관하세요.

전체 설명 · 영어 README · 소스 · 문제 제보:
https://github.com/kwon1h/anticater-vk01-studio
'@
$quickEn = @'
VK01 Studio — Custom Windows software for ANTICATER VK01

1. Connect over USB. Map the knob click to F13 in the ANTICATER utility and save it.
   ANTICATER setup utility: https://anticater.com/pages/download
   For the supplied 2.4 GHz receiver, map click to Media → Treble + instead,
   then select '2.4 GHz receiver · Treble +' under Knob input in VK01 Studio.
2. Run VK01Studio.exe. English is default; use the top-right language switch for Korean.
3. In Audio outputs, select at least 2 devices and arrange their cycle order.
4. Set click actions/timing and click Save changes.
5. Saved settings are active. Enable Practice to test without real actions.
6. Enable Start with Windows below and ensure Windows startup apps also enables VK01 Studio.

Defaults: single=play/pause, double=next, triple=previous, hold=cycle outputs.
Closing settings keeps the app running in the tray. Exit from the tray menu to quit.
Gesture customization is stored on the PC and requires the app to run.
Local data: %LOCALAPPDATA%\VK01Studio
Requires Windows 10/11 x64 and .NET Framework 4.8+. Tested on the specified USB VK01 revision.
No extra driver required. Unofficial community software.
Keep the executable in a permanent folder when using autostart.

Full bilingual docs, source and issues:
https://github.com/kwon1h/anticater-vk01-studio
'@
[IO.File]::WriteAllText((Join-Path $packageRoot 'README.ko.txt'), $quickKo, (New-Object Text.UTF8Encoding($true)))
[IO.File]::WriteAllText((Join-Path $packageRoot 'README.en.txt'), $quickEn, (New-Object Text.UTF8Encoding($true)))
$zipPath = Join-Path $releaseRoot ($packageName + '.zip')
Compress-Archive -LiteralPath $packageRoot -DestinationPath $zipPath
$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $releaseRoot 'SHA256SUMS.txt'), ($hash + '  ' + $packageName + '.zip' + [Environment]::NewLine))
Write-Host $zipPath
