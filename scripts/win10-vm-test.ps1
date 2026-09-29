# Tests the installer on a clean, throwaway Windows 10 in a Hyper-V virtual machine without any network adapter,
# with the same checks as the Windows Sandbox test (sandbox-inner.ps1), which can only run the host's Windows 11.
# Needs: Hyper-V, an elevated PowerShell (the script restarts itself elevated, so Windows asks once), about 35 GB
# free, and Microsoft's Windows 10 disc image (-Iso), for example Win10_22H2_English_x64v1.iso from
# https://www.microsoft.com/software-download/windows10ISO. Windows 10 Pro is applied from the image straight onto a
# new virtual disk, with an answer file that accepts Microsoft's licence terms, creates a local test account and runs
# the checks at first sign-in; it is not activated (for testing only). The disk and virtual machine are deleted
# afterwards unless -Keep is given; the image is kept. Results: <Work>\results (results.txt, done.txt, captures).
param([Parameter(Mandatory)][string]$Iso, [string]$Installer, [string]$Work = (Join-Path $env:USERPROFILE 'PlainViewerWin10Test'),
  [string]$Edition = 'Windows 10 Pro', [int]$TimeoutMinutes = 120, [switch]$Keep)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$Iso = (Resolve-Path -LiteralPath $Iso).Path
if (-not $Installer) {
  $Installer = Get-ChildItem (Join-Path $repoRoot 'artifacts\installer') -Filter 'PlainViewer-Setup-*-x64.exe' | Sort-Object LastWriteTime | Select-Object -Last 1 -ExpandProperty FullName
}
if (-not $Installer -or -not (Test-Path -LiteralPath $Installer)) { throw 'No installer found: run scripts\package.ps1 first.' }
$Installer = (Resolve-Path -LiteralPath $Installer).Path

# Hyper-V and disk images need administrator rights: restart elevated and wait (the output goes to host.log).
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
$vmName = 'PlainViewer-Win10-Test'
$vhd = Join-Path $Work 'win10.vhdx'
$results = Join-Path $Work 'results'
$exit = 1
try {
  if (Get-VM -Name $vmName -ErrorAction SilentlyContinue) { Stop-VM -Name $vmName -TurnOff -Force -ErrorAction SilentlyContinue; Remove-VM -Name $vmName -Force }
  if (Test-Path -LiteralPath $vhd) { Remove-Item -LiteralPath $vhd -Force }
  if (Test-Path -LiteralPath $results) { Remove-Item -LiteralPath $results -Recurse -Force }
  $free = (Get-PSDrive ((Split-Path $Work -Qualifier).TrimEnd(':'))).Free
  if ($free -lt 30GB) { throw "Only $([math]::Round($free / 1GB, 1)) GB free; the virtual machine needs about 30 GB." }

  # 1. The Windows image from the disc image.
  $mounted = Mount-DiskImage -ImagePath $Iso -PassThru
  $isoDrive = ($mounted | Get-Volume).DriveLetter + ':'
  $image = @('install.wim', 'install.esd') | ForEach-Object { Join-Path $isoDrive "sources\$_" } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
  $index = (Get-WindowsImage -ImagePath $image | Where-Object ImageName -eq $Edition).ImageIndex
  if (-not $index) { throw "The image has no edition named '$Edition'." }
  Log "Image $image, edition '$Edition' (index $index)"

  # 2. A new virtual disk with a UEFI layout, and Windows applied onto it.
  New-VHD -Path $vhd -SizeBytes 40GB -Dynamic | Out-Null
  $disk = Mount-VHD -Path $vhd -Passthru | Get-Disk
  Initialize-Disk -Number $disk.Number -PartitionStyle GPT
  $system = New-Partition -DiskNumber $disk.Number -Size 260MB -GptType '{c12a7328-f81f-11d2-ba4b-00a0c93ec93b}' -AssignDriveLetter
  Format-Volume -Partition $system -FileSystem FAT32 -NewFileSystemLabel 'System' -Confirm:$false | Out-Null
  New-Partition -DiskNumber $disk.Number -Size 16MB -GptType '{e3c9e316-0b5c-4db8-817d-f92df00215ae}' | Out-Null
  $windows = New-Partition -DiskNumber $disk.Number -UseMaximumSize -AssignDriveLetter
  Format-Volume -Partition $windows -FileSystem NTFS -NewFileSystemLabel 'Windows' -Confirm:$false | Out-Null
  $systemDrive = (Get-Partition -DiskNumber $disk.Number -PartitionNumber $system.PartitionNumber).DriveLetter + ':'
  $windowsDrive = (Get-Partition -DiskNumber $disk.Number -PartitionNumber $windows.PartitionNumber).DriveLetter + ':'
  Log "Applying Windows to $windowsDrive (takes a few minutes)"
  Expand-WindowsImage -ImagePath $image -Index $index -ApplyPath "$windowsDrive\" | Out-Null
  & "$env:WINDIR\System32\bcdboot.exe" "$windowsDrive\Windows" /s $systemDrive /f UEFI | Out-Null
  if ($LASTEXITCODE -ne 0) { throw "bcdboot failed ($LASTEXITCODE)." }

  # 3. The answer file: accept the licence terms, skip the setup questions, a local administrator that signs in
  #    automatically and runs the checks. User Account Control is off, as in Windows Sandbox, so the installer's
  #    firewall option does not wait for someone to click a prompt. The password is random and only for this machine.
  $password = -join ((48..57) + (65..90) + (97..122) | Get-Random -Count 20 | ForEach-Object { [char]$_ })
  $component = 'processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS"'
  $unattend = @"
<?xml version="1.0" encoding="utf-8"?>
<unattend xmlns="urn:schemas-microsoft-com:unattend" xmlns:wcm="http://schemas.microsoft.com/WMIConfig/2002/State">
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
        <SynchronousCommand wcm:action="add"><Order>1</Order><CommandLine>powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\Test\input\sandbox-inner.ps1</CommandLine><Description>Plain Viewer checks</Description></SynchronousCommand>
      </FirstLogonCommands>
    </component>
  </settings>
</unattend>
"@
  New-Item -ItemType Directory -Force -Path "$windowsDrive\Windows\Panther" | Out-Null
  Set-Content -LiteralPath "$windowsDrive\Windows\Panther\unattend.xml" -Value $unattend -Encoding utf8

  # 4. The test files, as sandbox-test.ps1 provides them.
  $inputs = New-Item -ItemType Directory -Force -Path "$windowsDrive\Test\input"
  New-Item -ItemType Directory -Force -Path "$windowsDrive\Test\results" | Out-Null
  Copy-Item -LiteralPath $Installer -Destination $inputs
  Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'sandbox-inner.ps1'), (Join-Path $PSScriptRoot 'keyboard-check.ps1'), (Join-Path $PSScriptRoot 'high-contrast-capture.ps1') -Destination $inputs
  & robocopy (Join-Path $repoRoot 'tests\corpus') (Join-Path $inputs 'corpus') /E /XD generated node_modules generate /NFL /NDL /NJH /NJS /NP | Out-Null
  if ($LASTEXITCODE -ge 8) { throw 'Copying the test files failed.' }
  Dismount-VHD -Path $vhd
  Dismount-DiskImage -ImagePath $Iso | Out-Null

  # 5. The virtual machine: no network adapter at all, so nothing in it can reach a network.
  New-VM -Name $vmName -Generation 2 -MemoryStartupBytes 4GB -VHDPath $vhd | Out-Null
  Set-VMProcessor -VMName $vmName -Count 2
  Set-VMMemory -VMName $vmName -DynamicMemoryEnabled $false
  Get-VMNetworkAdapter -VMName $vmName | Remove-VMNetworkAdapter
  Set-VM -Name $vmName -CheckpointType Disabled -AutomaticCheckpointsEnabled $false
  Log 'Starting the virtual machine (Windows setup, then the checks; it shuts down when done)'
  Start-VM -Name $vmName
  $deadline = (Get-Date).AddMinutes($TimeoutMinutes)
  while ((Get-VM -Name $vmName).State -ne 'Off' -and (Get-Date) -lt $deadline) { Start-Sleep -Seconds 20 }
  if ((Get-VM -Name $vmName).State -ne 'Off') { Log "Not finished after $TimeoutMinutes minutes: turning it off."; Stop-VM -Name $vmName -TurnOff -Force }

  # 6. The results, read from the virtual disk.
  $disk = Mount-VHD -Path $vhd -ReadOnly -Passthru | Get-Disk
  $drive = (Get-Partition -DiskNumber $disk.Number | Where-Object { $_.Type -eq 'Basic' -and $_.Size -gt 10GB } | Select-Object -First 1)
  if (-not $drive.DriveLetter) { $drive | Add-PartitionAccessPath -AssignDriveLetter; $drive = Get-Partition -DiskNumber $disk.Number -PartitionNumber $drive.PartitionNumber }
  Copy-Item -LiteralPath "$($drive.DriveLetter):\Test\results" -Destination $results -Recurse
  Dismount-VHD -Path $vhd
  Get-Content -LiteralPath (Join-Path $results 'results.txt') -ErrorAction SilentlyContinue | ForEach-Object { Log $_ }
  $done = Join-Path $results 'done.txt'
  if ((Test-Path -LiteralPath $done) -and (Get-Content -LiteralPath $done -Raw).Trim() -eq 'PASS') { Log "Windows 10 test passed. Results: $results"; $exit = 0 }
  else { Log "Windows 10 test failed or did not finish. Results: $results" }
}
catch { Log "ERROR $($_.Exception.Message)" }
finally {
  Dismount-DiskImage -ImagePath $Iso -ErrorAction SilentlyContinue | Out-Null
  if (Test-Path -LiteralPath $vhd) { Dismount-VHD -Path $vhd -ErrorAction SilentlyContinue }
  if (-not $Keep) {
    if (Get-VM -Name $vmName -ErrorAction SilentlyContinue) { Stop-VM -Name $vmName -TurnOff -Force -ErrorAction SilentlyContinue; Remove-VM -Name $vmName -Force }
    if (Test-Path -LiteralPath $vhd) { Remove-Item -LiteralPath $vhd -Force }
    Log 'Virtual machine and its disk deleted.'
  }
}
exit $exit
