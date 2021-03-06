﻿; This script requires the following environment variables:
;
;   NF_BUILD            - The build output folder
;   NF_CACHE            - The build cache folder
;   NF_KUBE_VERSION     - The Kubernetes version
;   NF_PRODUCT_VERSION  - The neonKUBE product version

[Setup]
AppName=neonKUBE
AppVersion={#GetEnv("NF_PRODUCT_VERSION")}
DefaultDirName={pf}\neonKUBE
DefaultGroupName=neonKUBE
; UninstallDisplayIcon={app}\neonKUBE.Windows\neonKUBE-win.exe
MinVersion=10.0.16299
Compression=lzma2
SolidCompression=no
OutputDir={#GetEnv("NF_BUILD")}
OutputBaseFilename=neonKUBE-setup-{#GetEnv("NF_PRODUCT_VERSION")}
; "ArchitecturesAllowed=x64" specifies that Setup cannot run on
; anything but x64.
ArchitecturesAllowed=x64
; "ArchitecturesInstallIn64BitMode=x64" requests that the install be
; done in "64-bit mode" on x64, meaning it should use the native
; 64-bit Program Files directory and the 64-bit view of the registry.
ArchitecturesInstallIn64BitMode=x64
AppPublisher=neonFORGE, LLC
AppPublisherURL=https://neonKUBE.com
ChangesEnvironment=yes
PrivilegesRequired=admin

[Files]

; Common files
Source: {#GetEnv("NF_CACHE")}\windows\kubectl\{#GetEnv("NF_KUBE_VERSION")}\kubectl.exe; DestDir: {app}; Flags: recursesubdirs replacesameversion
Source: {#GetEnv("NF_CACHE")}\windows\powershell\*.*; DestDir: {app}\powershell; Flags: recursesubdirs replacesameversion

; WinDesktop
Source: {#GetEnv("NF_BUILD")}\win-desktop\*.*; DestDir: {app}\win-desktop; Flags: recursesubdirs replacesameversion

; neon-cli
Source: {#GetEnv("NF_BUILD")}\neon.cmd; DestDir: {app}; Flags: recursesubdirs replacesameversion
Source: {#GetEnv("NF_BUILD")}\neon\*.*; DestDir: {app}\neon; Flags: recursesubdirs replacesameversion

[Icons]
Name: "{group}\neonKUBE Desktop"; Filename: "{app}\win-desktop\neonKUBE-win.exe"

[Registry]
Root: HKCU; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "neonKUBE Desktop"; ValueData: "{app}\win-desktop\neonKUBE-win.exe"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Environment"; ValueType: string; ValueName: "NEONKUBE_PROGRAM_FOLDER"; ValueData: "{app}"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\win-desktop\neonKUBE-win.exe"; Description: "neonKUBE Desktop"; Flags: postinstall nowait

[UninstallRun]
; Kill the neonKUBE Desktop app if it is running.
Filename: "{cmd}"; Parameters: "/C ""taskkill /im neonKUBE-win.exe /f /t"

[Code]

{ ============================================================================= }
{ Low level Windows API wrappers                                                }
{ ============================================================================= }

function GetPhysicallyInstalledSystemMemory(var physicalRamKBytes: Int64): BOOL;
  external 'GetPhysicallyInstalledSystemMemory@kernel32.dll stdcall';

{ ============================================================================= }
{ These functions manage the PATH environment variable.                         }
{ ============================================================================= }

function Replace(Dest, SubStr, Str: string): string;
var
  Position: Integer;
  Ok: Integer;
begin
  Ok := 1;
  while Ok > 0 do
  begin
    Position:=Pos(SubStr, Dest);
    if Position > 0 then
    begin
      Delete(Dest, Position, Length(SubStr));
      Insert(Str, Dest, Position);
    end else
      Ok := 0;
  end;
  Result:=Dest;
end;

procedure PrependToPath();
var
  V: string;
  Str: string;
begin
  RegQueryStringValue(HKCU, 'Environment', 'Path', V);
  Str := ExpandConstant('{app}');
  V := Replace(V, Str, '');
  V := Str + ';' + V;
  V := Replace(V,';;',';');
  RegWriteStringValue(HKCU, 'Environment', 'Path', V);
end;

procedure AppendToPath();
var
  V: string;
  Str: string;
begin
  RegQueryStringValue(HKCU, 'Environment', 'Path', V);
  Str := ExpandConstant('{app}');
  V := Replace(V, Str, '');
  V := V + ';' + Str;
  V := Replace(V,';;',';');
  RegWriteStringValue(HKCU, 'Environment', 'Path', V);
end;

procedure RemoveFromPath();
var
  V: string;
  Str: string;
begin
  RegQueryStringValue(HKCU, 'Environment', 'Path', V);
  Str := ExpandConstant('{app}');
  V := Replace(V, Str, '');
  V := Replace(V,';;',';');
  RegWriteStringValue(HKCU, 'Environment', 'Path', V);
end;

{ ============================================================================= }
{ EVENT Handlers                                                                }
{ ============================================================================= }

function InitializeSetup: Boolean;
var
    physicalRamKBytes: Int64;
begin

    { Verify that the machine has at least 4GB of RAM }

    GetPhysicallyInstalledSystemMemory(physicalRamKBytes);

    if physicalRamKBytes/(1024*1024) < 4 then
    begin
        MsgBox('Cannot install MINIKUBE.  You have less than 4GB physical memory available.', mbError, MB_OK);
        Result := False;
    end;

    Result := True;
end;

procedure DeinitializeSetup();
begin
    
  { Prepend the program folder to the PATH }

  PrependToPath();
end;

procedure DeinitializeUninstall();
begin

  { Remove the program folder from the PATH }

  RemoveFromPath();
end;
