; FolderIcon+ installer  (build with:  makensis installer.nsi)
; Expects the published exe at  publish\FolderIconPlus.exe  (see build.cmd)

Unicode true
SetCompressor /SOLID lzma

!define APPNAME   "FolderIcon+"
!define APPEXE    "FolderIconPlus.exe"
!define VERSION   "1.0.0"
!define PUBLISHER "FolderIcon+"
!define UNINSTKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\FolderIconPlus"
!define CLASSES   "Software\Classes"

!include "MUI2.nsh"

Name "${APPNAME} ${VERSION}"
OutFile "FolderIconPlus_Setup.exe"
InstallDir "$PROGRAMFILES64\${APPNAME}"
InstallDirRegKey HKLM "Software\${APPNAME}" "InstallDir"
RequestExecutionLevel admin
BrandingText "${APPNAME} ${VERSION}"

; !define MUI_ICON "FolderIconPlus.ico"        ; uncomment if you add an icon
; !define MUI_UNICON "FolderIconPlus.ico"

!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

; ---------------------------------------------------------------------------
; Cascading context menu:  FolderIcon+  >  4 entries
;   BASE  = key under HKLM\Software\Classes
;   TOKEN = %1 (folder that was right-clicked) or %V (folder whose empty background was right-clicked)
; Sub-items sort alphabetically by key name, hence the 01/02/03/04 prefixes.
; CommandFlags 0x40 = separator after the item.
; ---------------------------------------------------------------------------
!macro AddMenu BASE TOKEN
  WriteRegStr   HKLM "${CLASSES}\${BASE}" "MUIVerb"     "FolderIcon+"
  WriteRegStr   HKLM "${CLASSES}\${BASE}" "Icon"        "$INSTDIR\${APPEXE},0"
  WriteRegStr   HKLM "${CLASSES}\${BASE}" "SubCommands" ""

  WriteRegStr   HKLM "${CLASSES}\${BASE}\shell\01change" "MUIVerb" "Change Selected Folder Icon"
  WriteRegStr   HKLM "${CLASSES}\${BASE}\shell\01change\command" "" '"$INSTDIR\${APPEXE}" --select "${TOKEN}"'

  WriteRegStr   HKLM "${CLASSES}\${BASE}\shell\02recursive" "MUIVerb" "Change Folder Icons Recursively"
  WriteRegStr   HKLM "${CLASSES}\${BASE}\shell\02recursive\command" "" '"$INSTDIR\${APPEXE}" --recursive "${TOKEN}"'

  WriteRegStr   HKLM "${CLASSES}\${BASE}\shell\03ui" "MUIVerb" "Open FolderIcon+ UI"
  WriteRegDWORD HKLM "${CLASSES}\${BASE}\shell\03ui" "CommandFlags" 0x40
  WriteRegStr   HKLM "${CLASSES}\${BASE}\shell\03ui\command" "" '"$INSTDIR\${APPEXE}" --ui "${TOKEN}"'

  WriteRegStr   HKLM "${CLASSES}\${BASE}\shell\04reset" "MUIVerb" "Reset folder icon to default"
  WriteRegStr   HKLM "${CLASSES}\${BASE}\shell\04reset\command" "" '"$INSTDIR\${APPEXE}" --reset "${TOKEN}"'
!macroend

Function .onInit
  SetRegView 64
FunctionEnd

Function un.onInit
  SetRegView 64
FunctionEnd

Section "Install"
  SetRegView 64
  SetShellVarContext all

  SetOutPath "$INSTDIR"
  File "publish\${APPEXE}"
  CreateDirectory "$INSTDIR\IconPacks"
  WriteUninstaller "$INSTDIR\Uninstall.exe"

  ; right-click a folder / right-click empty space inside a folder
  !insertmacro AddMenu "Directory\shell\FolderIconPlus"            "%1"
  !insertmacro AddMenu "Directory\Background\shell\FolderIconPlus" "%V"

  CreateShortCut "$SMPROGRAMS\${APPNAME}.lnk" "$INSTDIR\${APPEXE}" "--ui"

  ; Add/Remove Programs entry
  WriteRegStr   HKLM "Software\${APPNAME}" "InstallDir" "$INSTDIR"
  WriteRegStr   HKLM "${UNINSTKEY}" "DisplayName"     "${APPNAME}"
  WriteRegStr   HKLM "${UNINSTKEY}" "DisplayVersion"  "${VERSION}"
  WriteRegStr   HKLM "${UNINSTKEY}" "Publisher"       "${PUBLISHER}"
  WriteRegStr   HKLM "${UNINSTKEY}" "DisplayIcon"     "$INSTDIR\${APPEXE}"
  WriteRegStr   HKLM "${UNINSTKEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr   HKLM "${UNINSTKEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegDWORD HKLM "${UNINSTKEY}" "NoModify" 1
  WriteRegDWORD HKLM "${UNINSTKEY}" "NoRepair" 1

  System::Call 'shell32::SHChangeNotify(i 0x08000000, i 0, p 0, p 0)'   ; SHCNE_ASSOCCHANGED
SectionEnd

Section "Uninstall"
  SetRegView 64
  SetShellVarContext all

  DeleteRegKey HKLM "${CLASSES}\Directory\shell\FolderIconPlus"
  DeleteRegKey HKLM "${CLASSES}\Directory\Background\shell\FolderIconPlus"
  DeleteRegKey HKLM "${UNINSTKEY}"
  DeleteRegKey HKLM "Software\${APPNAME}"

  Delete "$SMPROGRAMS\${APPNAME}.lnk"
  Delete "$INSTDIR\${APPEXE}"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir  "$INSTDIR"
  RMDir  "$INSTDIR\IconPacks"

  System::Call 'shell32::SHChangeNotify(i 0x08000000, i 0, p 0, p 0)'
SectionEnd
