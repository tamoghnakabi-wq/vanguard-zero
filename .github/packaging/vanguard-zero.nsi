; VANGUARD ZERO — NSIS Windows x64 Installer Script
; Compiled by CI via: makensis -DVERSION="1.0.0" -DSOURCE_DIR="path/to/build" -DOUT_FILE="VanguardZero-v1.0.0-Windows-x64-Setup.exe" vanguard-zero.nsi

!include "MUI2.nsh"

!ifndef VERSION
  !define VERSION "1.0.0"
!endif
!ifndef SOURCE_DIR
  !define SOURCE_DIR "builds/StandaloneWindows64"
!endif
!ifndef OUT_FILE
  !define OUT_FILE "VanguardZero-Windows-x64-Setup.exe"
!endif

Name "VANGUARD ZERO ${VERSION}"
OutFile "${OUT_FILE}"
Unicode True
InstallDir "$LOCALAPPDATA\VanguardZero"
RequestExecutionLevel user

!define MUI_ABORTWARNING
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN "$INSTDIR\VanguardZero.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Launch VANGUARD ZERO"
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

Section "VANGUARD ZERO Game Files" SecMain
  SetOutPath "$INSTDIR"
  File /r "${SOURCE_DIR}\*.*"

  CreateDirectory "$SMPROGRAMS\VANGUARD ZERO"
  CreateShortcut "$SMPROGRAMS\VANGUARD ZERO\VANGUARD ZERO.lnk" "$INSTDIR\VanguardZero.exe" "" "$INSTDIR\VanguardZero.exe" 0
  CreateShortcut "$SMPROGRAMS\VANGUARD ZERO\Uninstall VANGUARD ZERO.lnk" "$INSTDIR\Uninstall.exe"
  CreateShortcut "$DESKTOP\VANGUARD ZERO.lnk" "$INSTDIR\VanguardZero.exe" "" "$INSTDIR\VanguardZero.exe" 0

  WriteUninstaller "$INSTDIR\Uninstall.exe"
SectionEnd

Section "Uninstall"
  Delete "$DESKTOP\VANGUARD ZERO.lnk"
  RMDir /r "$SMPROGRAMS\VANGUARD ZERO"
  RMDir /r "$INSTDIR"
SectionEnd
