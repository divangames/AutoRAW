; Inno Setup 6 — AutoRAW (русский UI, x64)
; Сборка: из корня репозитория запустите build-installer.ps1 (не компилируйте .iss вручную без /D — останутся заглушки 0.0.0).
; Версии: при сборке build-installer.ps1 передаёт /DMyAppVersion и /DMyAppVerFull (из CHANGELOG).
; Без /D компилятор подставит заглушки ниже — не перезаписывать через #define без #ifndef.

#define MyAppName      "AutoRAW"
#ifndef MyAppVersion
#define MyAppVersion   "0.0.0"
#endif
#ifndef MyAppVerFull
#define MyAppVerFull   "0.0.0.0"
#endif
#define MyAppPublisher "WhiteCube"
#define MyAppURL       "https://xn--e1aahbcnhejb1c.xn--p1ai/"
#define MyAppExeName   "AutoRAW.exe"
#define MyDotNetExe    "dotnet-sdk-8.0.421-win-x64.exe"

; ----------------------------------------------------------------
[Setup]
AppId={{E7C3A9B1-82F4-4D6A-9C1E-3F5A8B2D6C90}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
VersionInfoVersion={#MyAppVerFull}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Installer
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=no
DisableWelcomePage=no
OutputDir=..\dist
OutputBaseFilename=AutoRAW-Setup-{#MyAppVersion}-ru
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Баннер слева (164x314) — Welcome и Finish страницы
WizardImageFile=..\assets\images\installer_banner.png
WizardImageStretch=yes
; Маленький логотип справа вверху (55x55) — все остальные страницы
WizardSmallImageFile=..\assets\images\installer_small.png
SetupIconFile=..\assets\images\setup.ico
ArchitecturesInstallIn64BitMode=x64os
ArchitecturesAllowed=x64compatible
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=yes
RestartApplications=no
MinVersion=10.0
; Для будущих обновлений через GitHub
AppMutex=AutoRAWSetupMutex

; ----------------------------------------------------------------
[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

; ----------------------------------------------------------------
[Messages]
; Текст на странице приветствия — версия и описание
WelcomeLabel1=Добро пожаловать в мастер установки%nAutoRAW {#MyAppVersion}
WelcomeLabel2=Этот мастер установит AutoRAW {#MyAppVersion} на ваш компьютер.%n%nПрограмма выполняет пакетное кадрирование RAW и изображений по референсу и по технологии «Zona» — собственному режиму кропа по красному маркёру (см. документацию).%n%nРазработчик: Радыгин Иван | Команда WhiteCube%n%nРекомендуется закрыть все запущенные программы перед продолжением.

; ----------------------------------------------------------------
[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

; ----------------------------------------------------------------
[Files]
; Основное приложение
Source: "..\dist\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"
; Бандл .NET SDK — не копируется автоматически, только через ExtractTemporaryFile в коде
Source: "{#MyDotNetExe}"; Flags: dontcopy noencryption

; ----------------------------------------------------------------
[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"

; ----------------------------------------------------------------
[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent unchecked

; ----------------------------------------------------------------
[Code]

{ ---------- Вспомогательные функции ---------- }

{ Проверка подраздела реестра: есть ли дочерние ключи, начинающиеся на "8." }
function RegHasVersion8Key(const RootKey: Integer; const SubKey: string): Boolean;
var
  Names: TArrayOfString;
  I: Integer;
begin
  Result := False;
  if not RegGetSubkeyNames(RootKey, SubKey, Names) then
    Exit;
  for I := 0 to GetArrayLength(Names) - 1 do
    if (Length(Names[I]) >= 2) and (Names[I][1] = '8') and (Names[I][2] = '.') then
    begin
      Result := True;
      Exit;
    end;
end;

{ Проверка наличия папки 8.x.x в указанном каталоге (для случаев без реестра) }
function DirHasVersion8Subdir(const BaseDir: string): Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  if not DirExists(BaseDir) then
    Exit;
  if FindFirst(BaseDir + '\8.*', FindRec) then
  begin
    try
      repeat
        if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
        begin
          Result := True;
          Break;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

{ Основная проверка: установлен ли .NET 8 Windows Desktop Runtime }
function IsDotNet8DesktopRuntimeInstalled: Boolean;
begin
  { 1. Реестр — первый стандартный путь }
  if RegHasVersion8Key(HKLM64, 'SOFTWARE\dotnet\shared\Microsoft.WindowsDesktop.App') then
  begin
    Result := True;
    Exit;
  end;

  { 2. Реестр — второй путь (Setup/InstalledVersions) }
  if RegHasVersion8Key(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App') then
  begin
    Result := True;
    Exit;
  end;

  { 3. Реестр — HKCU (пользовательская установка) }
  if RegHasVersion8Key(HKCU64, 'SOFTWARE\dotnet\shared\Microsoft.WindowsDesktop.App') then
  begin
    Result := True;
    Exit;
  end;

  { 4. Файловая система — Program Files\dotnet\shared\Microsoft.WindowsDesktop.App\8.x }
  if DirHasVersion8Subdir(ExpandConstant('{pf}\dotnet\shared\Microsoft.WindowsDesktop.App')) then
  begin
    Result := True;
    Exit;
  end;

  { 5. Файловая система — %LocalAppData%\Microsoft\dotnet (per-user установка) }
  if DirHasVersion8Subdir(ExpandConstant('{localappdata}\Microsoft\dotnet\shared\Microsoft.WindowsDesktop.App')) then
  begin
    Result := True;
    Exit;
  end;

  Result := False;
end;

{ Код возврата считается успехом установки }
function DotNetInstallSucceeded(const Code: Integer): Boolean;
begin
  { 0=OK, 1638=уже установлен, 3010=нужна перезагрузка, 1641=установщик сам перезагрузит }
  Result := (Code = 0) or (Code = 1638) or (Code = 3010) or (Code = 1641);
end;

{ ---------- Главная страница подготовки ---------- }

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
  DestPath: string;
begin
  NeedsRestart := False;

  { .NET уже есть — ничего делать не нужно }
  if IsDotNet8DesktopRuntimeInstalled then
  begin
    Log('.NET 8 Desktop Runtime найден — установка пропущена.');
    Result := '';
    Exit;
  end;

  { .NET не найден — устанавливаем из бандла }
  Log('.NET 8 Desktop Runtime не найден — запускаем встроенный установщик...');

  WizardForm.PreparingLabel.Caption :=
    'Устанавливается .NET 8 SDK (встроенный пакет)...' + #13#10 +
    'Пожалуйста, подождите — это может занять несколько минут.';

  try
    ExtractTemporaryFile('{#MyDotNetExe}');
  except
    Result := 'Не удалось извлечь встроенный пакет .NET 8 SDK из установщика AutoRAW.';
    Exit;
  end;

  DestPath := ExpandConstant('{tmp}\{#MyDotNetExe}');
  if not FileExists(DestPath) then
  begin
    Result := 'Встроенный файл установщика .NET 8 SDK не найден: ' + DestPath;
    Exit;
  end;

  if not Exec(DestPath, '/install /quiet /norestart', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
  begin
    if FileExists(DestPath) then DeleteFile(DestPath);
    Result := 'Не удалось запустить установщик .NET 8 SDK.' + #13#10 +
              'Установите вручную: https://dotnet.microsoft.com/download/dotnet/8.0';
    Exit;
  end;

  if FileExists(DestPath) then DeleteFile(DestPath);

  if not DotNetInstallSucceeded(ResultCode) then
  begin
    Result := 'Установка .NET 8 SDK завершилась с ошибкой (код ' + IntToStr(ResultCode) + ').' + #13#10 +
              'Установите вручную: https://dotnet.microsoft.com/download/dotnet/8.0';
    Exit;
  end;

  if (ResultCode = 3010) or (ResultCode = 1641) then
    NeedsRestart := True;

  Log('.NET 8 SDK успешно установлен (код ' + IntToStr(ResultCode) + ').');

  WizardForm.PreparingLabel.Caption :=
    '.NET 8 SDK успешно установлен.' + #13#10 +
    'Продолжение установки AutoRAW...';

  Result := '';
end;

{ ---------- Инициализация UI ---------- }

procedure InitializeWizard;
begin
  { Показать версию в заголовке окна }
  WizardForm.Caption := 'Установка — {#MyAppName} {#MyAppVersion}';
end;

