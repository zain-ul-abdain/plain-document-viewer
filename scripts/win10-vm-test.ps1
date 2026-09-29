# Tests the installer on a clean, throwaway Windows 10 in a Hyper-V virtual machine without any network adapter,
# with the same checks as the Windows Sandbox test (sandbox-inner.ps1), which can only run the host's Windows 11.
# Needs: Hyper-V, an elevated PowerShell (the script restarts itself elevated, so Windows asks once), about 35 GB
# free, and Microsoft's Windows 10 disc image (-Iso), for example Win10_22H2_English_x64v1.iso from
# https://www.microsoft.com/software-download/windows10ISO.
# The virtual machine boots Microsoft's Windows Setup from that image. A second, small disc image made here holds the
# answer file (autounattend.xml: accept Microsoft's licence terms, Windows 10 Pro chosen with Microsoft's generic
# installation key, which does not activate Windows; a local test account that signs in automatically) and the test
# files; at first sign-in they are copied to C:\Test and the checks run. Windows is not activated (testing only).
# The virtual machine, its disk and the second image are deleted afterwards unless -Keep is given; the Windows image
# is kept. Results: <Work>\results (results.txt, done.txt, captures).
param([Parameter(Mandatory)][string]$Iso, [string]$Installer, [string]$Work = (Join-Path $env:USERPROFILE 'PlainViewerWin10Test'),
  [string]$Edition = 'Windows 10 Pro', [int]$TimeoutMinutes = 150, [switch]$Keep)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$Iso = (Resolve-Path -LiteralPath $Iso).Path
if (-not $Installer) {
  $Installer = Get-ChildItem (Join-Path $repoRoot 'artifacts\installer') -Filter 'PlainViewer-Setup-*-x64.exe' | Sort-Object LastWriteTime | Select-Object -Last 1 -ExpandProperty FullName
}
if (-not $Installer -or -not (Test-Path -LiteralPath $Installer)) { throw 'No installer found: run scripts\package.ps1 first.' }
$Installer = (Resolve-Path -LiteralPath $Installer).Path

# Hyper-V needs administrator rights: restart elevated and wait (the output goes to host.log).
$elevated = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
New-Item -ItemType Directory -Force -Path $Work | Out-Null
$hostLog = Join-Path $Work 'host.log'
if (-not $elevated) {
  if (Test-Path -LiteralPath $hostLog) { Remove-Item -LiteralPath $hostLog -Force }
  $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-Iso', "`"$Iso`"", '-Installer', "`"$Installer`"", '-Work', "`"$Work`"",
    '-Edition', "`"$Edition`"", '-TimeoutMinutes', $TimeoutMinutes) + $(if ($Keep) { @('-Keep') } else { @() })
  $process = Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $arguments -Wait -PassThru
  Get-Content -LiteralPath $hostLog -ErrorAction SilentlyContinue
  exit $process.ExitCode
}

function Log([string]$message) { $line = "$(Get-Date -Format 'HH:mm:ss') $message"; Add-Content -LiteralPath $hostLog -Value $line; Write-Output $line }

# Writes a folder as a disc image with Windows' own image mastering API (IMAPI2), UDF with Joliet names.
Add-Type -TypeDefinition @'
using System; using System.IO; using System.Runtime.InteropServices.ComTypes;
public static class IsoWriter {
  public static void Write(string path, object image, int blockSize, int blocks) {
    var stream = (IStream)image; var buffer = new byte[blockSize];
    using (var output = File.Create(path)) { for (int i = 0; i < blocks; i++) { stream.Read(buffer, blockSize, IntPtr.Zero); output.Write(buffer, 0, blockSize); } }
  }
}
'@
function New-IsoFile([string]$source, [string]$path, [string]$label) {
  $image = New-Object -ComObject IMAPI2FS.MsftFileSystemImage
  $image.FileSystemsToCreate = 7            # ISO 9660, Joliet and UDF
  $image.FreeMediaBlocks = 2000000          # up to about 4 GB
  $image.VolumeName = $label
  $image.Root.AddTree($source, $false)
  $result = $image.CreateResultImage()
  [IsoWriter]::Write($path, $result.ImageStream, $result.BlockSize, $result.TotalBlocks)
}

$vmName = 'PlainViewer-Win10-Test'
$vhd = Join-Path $Work 'win10.vhdx'
$answers = Join-Path $Work 'answers'
$answersIso = Join-Path $Work 'answers.iso'
$results = Join-Path $Work 'results'
$exit = 1
try {
  if (Get-VM -Name $vmName -ErrorAction SilentlyContinue) { Stop-VM -Name $vmName -TurnOff -Force -ErrorAction SilentlyContinue; Remove-VM -Name $vmName -Force }
  foreach ($old in $vhd, $answersIso) { if (Test-Path -LiteralPath $old) { Remove-Item -LiteralPath $old -Force } }
  foreach ($old in $answers, $results) { if (Test-Path -LiteralPath $old) { Remove-Item -LiteralPath $old -Recurse -Force } }
  $free = (Get-PSDrive ((Split-Path $Work -Qualifier).TrimEnd(':'))).Free
  if ($free -lt 30GB) { throw "Only $([math]::Round($free / 1GB, 1)) GB free; the virtual machine needs about 30 GB." }

  # 1. The answer file. User Account Control is switched off, as in Windows Sandbox, so the installer's firewall
  #    option does not wait for someone to click a prompt. The password is random and only for this machine.
  $password = -join ((48..57) + (65..90) + (97..122) | Get-Random -Count 20 | ForEach-Object { [char]$_ })
  $component = 'processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS"'
  $launch = 'cmd.exe /c for %d in (D E F G H I) do if exist %d:\Test\start.ps1 powershell.exe -NoProfile -ExecutionPolicy Bypass -File %d:\Test\start.ps1'
  New-Item -ItemType Directory -Force -Path (Join-Path $answers 'Test\input') | Out-Null
  Set-Content -LiteralPath (Join-Path $answers 'autounattend.xml') -Encoding utf8 -Value @"
<?xml version="1.0" encoding="utf-8"?>
<unattend xmlns="urn:schemas-microsoft-com:unattend" xmlns:wcm="http://schemas.microsoft.com/WMIConfig/2002/State">
  <settings pass="windowsPE">
    <component name="Microsoft-Windows-International-Core-WinPE" $component>
      <SetupUILanguage><UILanguage>en-US</UILanguage></SetupUILanguage><InputLocale>en-US</InputLocale><SystemLocale>en-US</SystemLocale><UILanguage>en-US</UILanguage><UserLocale>en-US</UserLocale>
    </component>
    <component name="Microsoft-Windows-Setup" $component>
      <DiskConfiguration><Disk wcm:action="add"><DiskID>0</DiskID><WillWipeDisk>true</WillWipeDisk>
        <CreatePartitions>
          <CreatePartition wcm:action="add"><Order>1</Order><Type>EFI</Type><Size>260</Size></CreatePartition>
          <CreatePartition wcm:action="add"><Order>2</Order><Type>MSR</Type><Size>16</Size></CreatePartition>
          <CreatePartition wcm:action="add"><Order>3</Order><Type>Primary</Type><Extend>true</Extend></CreatePartition>
        </CreatePartitions>
        <ModifyPartitions>
          <ModifyPartition wcm:action="add"><Order>1</Order><PartitionID>1</PartitionID><Format>FAT32</Format><Label>System</Label></ModifyPartition>
          <ModifyPartition wcm:action="add"><Order>2</Order><PartitionID>2</PartitionID></ModifyPartition>
          <ModifyPartition wcm:action="add"><Order>3</Order><PartitionID>3</PartitionID><Format>NTFS</Format><Label>Windows</Label><Letter>C</Letter></ModifyPartition>
        </ModifyPartitions>
      </Disk></DiskConfiguration>
      <ImageInstall><OSImage>
        <InstallFrom><MetaData wcm:action="add"><Key>/IMAGE/NAME</Key><Value>$Edition</Value></MetaData></InstallFrom>
        <InstallTo><DiskID>0</DiskID><PartitionID>3</PartitionID></InstallTo>
      </OSImage></ImageInstall>
      <UserData><AcceptEula>true</AcceptEula><ProductKey><Key>VK7JG-NPHTM-C97JM-9MPGT-3V66T</Key><WillShowUI>Never</WillShowUI></ProductKey></UserData>
    </component>
  </settings>
  <settings pass="specialize">
    <component name="Microsoft-Windows-Shell-Setup" $component><ComputerName>PV-WIN10-TEST</ComputerName><TimeZone>UTC</TimeZone></component>
    <component name="Microsoft-Windows-Deployment" $component>
      <RunSynchronous>
        <RunSynchronousCommand wcm:action="add"><Order>1</Order><Path>reg add HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System /v EnableLUA /t REG_DWORD /d 0 /f</Path></RunSynchronousCommand>
      </RunSynchronous>
    </component>
  </settings>
  <settings pass="oobeSystem">
    <component name="Microsoft-Windows-International-Core" $component><InputLocale>en-US</InputLocale><SystemLocale>en-US</SystemLocale><UILanguage>en-US</UILanguage><UserLocale>en-US</UserLocale></component>
    <component name="Microsoft-Windows-Shell-Setup" $component>
      <OOBE><HideEULAPage>true</HideEULAPage><HideLocalAccountScreen>true</HideLocalAccountScreen><HideOnlineAccountScreens>true</HideOnlineAccountScreens><HideWirelessSetupInOOBE>true</HideWirelessSetupInOOBE><ProtectYourPC>3</ProtectYourPC></OOBE>
      <UserAccounts><LocalAccounts><LocalAccount wcm:action="add"><Name>tester</Name><Group>Administrators</Group><Password><Value>$password</Value><PlainText>true</PlainText></Password></LocalAccount></LocalAccounts></UserAccounts>
      <AutoLogon><Enabled>true</Enabled><Username>tester</Username><LogonCount>3</LogonCount><Password><Value>$password</Value><PlainText>true</PlainText></Password></AutoLogon>
      <FirstLogonCommands>
        <SynchronousCommand wcm:action="add"><Order>1</Order><CommandLine>$launch</CommandLine><Description>Plain Viewer checks</Description></SynchronousCommand>
      </FirstLogonCommands>
    </component>
  </settings>
</unattend>
"@

  # 2. The test files, as sandbox-test.ps1 provides them, and a starter that copies them to C:\Test first
  #    (sandbox-inner.ps1 reads C:\Test\input and writes C:\Test\results).
  $inputs = Join-Path $answers 'Test\input'
  Copy-Item -LiteralPath $Installer -Destination $inputs
  Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'sandbox-inner.ps1'), (Join-Path $PSScriptRoot 'keyboard-check.ps1'), (Join-Path $PSScriptRoot 'high-contrast-capture.ps1') -Destination $inputs
  & robocopy (Join-Path $repoRoot 'tests\corpus') (Join-Path $inputs 'corpus') /E /XD generated node_modules generate /NFL /NDL /NJH /NJS /NP | Out-Null
  if ($LASTEXITCODE -ge 8) { throw 'Copying the test files failed.' }
  Set-Content -LiteralPath (Join-Path $answers 'Test\start.ps1') -Encoding utf8 -Value @'
$source = Split-Path $PSScriptRoot -Parent
New-Item -ItemType Directory -Force -Path C:\Test\results | Out-Null
Copy-Item -LiteralPath (Join-Path $source 'Test\input') -Destination C:\Test\input -Recurse -Force
Get-ChildItem C:\Test -Recurse -File | ForEach-Object { $_.IsReadOnly = $false }
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\Test\input\sandbox-inner.ps1
'@
  Log 'Writing the answer disc image'
  New-IsoFile $answers $answersIso 'PVTEST'

  # 3. The virtual machine: no network adapter at all. It starts from the Windows image; Windows Setup reads the
  #    answer file from the second disc.
  New-VHD -Path $vhd -SizeBytes 40GB -Dynamic | Out-Null
  New-VM -Name $vmName -Generation 2 -MemoryStartupBytes 4GB -VHDPath $vhd | Out-Null
  Set-VMProcessor -VMName $vmName -Count 2
  Set-VMMemory -VMName $vmName -DynamicMemoryEnabled $false
  Get-VMNetworkAdapter -VMName $vmName | Remove-VMNetworkAdapter
  Set-VM -Name $vmName -CheckpointType Disabled -AutomaticCheckpointsEnabled $false
  $dvd = Add-VMDvdDrive -VMName $vmName -Path $Iso -Passthru
  Add-VMDvdDrive -VMName $vmName -Path $answersIso
  Set-VMFirmware -VMName $vmName -FirstBootDevice $dvd
  Log 'Starting the virtual machine (Windows Setup, then the checks; it shuts down when done)'
  Start-VM -Name $vmName
  # The Windows disc asks "Press any key to boot from CD or DVD": answer it during the first start only.
  $machine = Get-CimInstance -Namespace root\virtualization\v2 -ClassName Msvm_ComputerSystem -Filter "ElementName='$vmName'"
  $keyboard = Get-CimAssociatedInstance -InputObject $machine -ResultClassName Msvm_Keyboard
  for ($i = 0; $i -lt 20; $i++) { Start-Sleep -Milliseconds 700; Invoke-CimMethod -InputObject $keyboard -MethodName TypeKey -Arguments @{ keyCode = 13 } -ErrorAction SilentlyContinue | Out-Null }
  $deadline = (Get-Date).AddMinutes($TimeoutMinutes)
  while ((Get-VM -Name $vmName).State -ne 'Off' -and (Get-Date) -lt $deadline) { Start-Sleep -Seconds 20 }
  if ((Get-VM -Name $vmName).State -ne 'Off') { Log "Not finished after $TimeoutMinutes minutes: turning it off."; Stop-VM -Name $vmName -TurnOff -Force }

  # 4. The results, read from the virtual disk.
  $disk = Mount-VHD -Path $vhd -ReadOnly -Passthru | Get-Disk
  $partition = Get-Partition -DiskNumber $disk.Number | Where-Object { $_.Type -eq 'Basic' -and $_.Size -gt 10GB } | Select-Object -First 1
  if (-not $partition) { Dismount-VHD -Path $vhd; throw 'Windows was not installed on the virtual disk.' }
  if (-not $partition.DriveLetter) { $partition | Add-PartitionAccessPath -AssignDriveLetter; $partition = Get-Partition -DiskNumber $disk.Number -PartitionNumber $partition.PartitionNumber }
  if (Test-Path -LiteralPath "$($partition.DriveLetter):\Test\results") { Copy-Item -LiteralPath "$($partition.DriveLetter):\Test\results" -Destination $results -Recurse }
  $panther = "$($partition.DriveLetter):\Windows\Panther\setupact.log"
  if (-not (Test-Path -LiteralPath $results) -and (Test-Path -LiteralPath $panther)) { New-Item -ItemType Directory -Force -Path $results | Out-Null; Copy-Item -LiteralPath $panther -Destination $results }
  Dismount-VHD -Path $vhd
  Get-Content -LiteralPath (Join-Path $results 'results.txt') -ErrorAction SilentlyContinue | ForEach-Object { Log $_ }
  $done = Join-Path $results 'done.txt'
  if ((Test-Path -LiteralPath $done) -and (Get-Content -LiteralPath $done -Raw).Trim() -eq 'PASS') { Log "Windows 10 test passed. Results: $results"; $exit = 0 }
  else { Log "Windows 10 test failed or did not finish. Results: $results" }
}
catch { Log "ERROR $($_.Exception.Message)" }
finally {
  if (Test-Path -LiteralPath $vhd) { Dismount-VHD -Path $vhd -ErrorAction SilentlyContinue }
  if (-not $Keep) {
    if (Get-VM -Name $vmName -ErrorAction SilentlyContinue) { Stop-VM -Name $vmName -TurnOff -Force -ErrorAction SilentlyContinue; Remove-VM -Name $vmName -Force }
    foreach ($old in $vhd, $answersIso) { if (Test-Path -LiteralPath $old) { Remove-Item -LiteralPath $old -Force } }
    if (Test-Path -LiteralPath $answers) { Remove-Item -LiteralPath $answers -Recurse -Force }
    Log 'Virtual machine, its disk and the answer image deleted.'
  }
}
exit $exit
