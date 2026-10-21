param(
    [string]$PublicOrigin = 'http://127.0.0.1:4200'
)
$ErrorActionPreference = 'Stop'
try {
    if (![OperatingSystem]::IsWindows()) { throw 'Windows is required for this local setup.' }
    $projectPath = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
    $configPath = Join-Path $projectPath 'backend/F1RaceLens.Api/appsettings.Local.json'
    $privateDirectory = Join-Path $projectPath '.local'
    $originUri = [Uri]$PublicOrigin
    if (!$originUri.IsAbsoluteUri -or ($originUri.Scheme -ne 'https' -and !($originUri.Scheme -eq 'http' -and $originUri.IsLoopback)) -or $originUri.AbsolutePath -ne '/' -or $originUri.Query -or $originUri.Fragment -or $originUri.UserInfo) { throw 'Use HTTPS, or HTTP loopback for local development only.' }
    Write-Host 'Gmail SMTP setup for F1 Race Lens. Your main Google password is NOT needed.'
    Write-Host 'Enable Google 2-Step Verification and create an app password named F1 Race Lens:'
    Write-Host 'https://myaccount.google.com/apppasswords'
    $senderAddress = (Read-Host 'Gmail sending address').Trim()
    $mailAddress = [System.Net.Mail.MailAddress]::new($senderAddress)
    if ($mailAddress.Address -ne $senderAddress -or $mailAddress.Host -notin @('gmail.com', 'googlemail.com')) { throw 'Enter a personal Gmail address.' }
    $secureAppPassword = Read-Host '16-character Google APP password (hidden; never paste it into chat)' -AsSecureString
    $passwordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureAppPassword)
    try { $appPassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordPointer) -replace '\s', '' }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordPointer) }
    if ($appPassword -notmatch '^[a-zA-Z0-9]{16}$') { throw 'Expected the 16-character app password, not your Google account password.' }
    $configuration = if (Test-Path -LiteralPath $configPath) { Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json -AsHashtable } else { @{} }
    $configuration.PasswordRecovery = @{
        PublicOrigin = $originUri.GetLeftPart([UriPartial]::Authority)
        SmtpHost = 'smtp.gmail.com'; SmtpPort = 587
        From = $senderAddress; Username = $senderAddress; Password = $appPassword
    }
    New-Item -ItemType Directory -Path $privateDirectory -Force | Out-Null
    $stagingPath = Join-Path $privateDirectory ('gmail-config-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    New-Item -ItemType File -Path $stagingPath | Out-Null
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $privateAcl = [Security.AccessControl.FileSecurity]::new()
    $privateAcl.SetOwner($identity)
    $privateAcl.SetAccessRuleProtection($true, $false)
    $privateAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($identity, 'FullControl', 'Allow'))
    $privateAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new('S-1-5-18'), 'FullControl', 'Allow'))
    Set-Acl -LiteralPath $stagingPath -AclObject $privateAcl
    $configuration | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $stagingPath -Encoding utf8
    Move-Item -LiteralPath $stagingPath -Destination $configPath -Force
    Write-Host 'Private SMTP settings saved. Existing database settings preserved. Restart the API.'
    $sendTest = Read-Host 'Send one test message to your own Gmail address now? [y/N]'
    if ($sendTest -eq 'y') {
        $smtpClient = [Net.Mail.SmtpClient]::new('smtp.gmail.com', 587)
        $smtpClient.EnableSsl = $true
        $smtpClient.UseDefaultCredentials = $false
        $smtpClient.Credentials = [Net.NetworkCredential]::new($senderAddress, $appPassword)
        $smtpClient.Timeout = 15000
        $testMessage = [Net.Mail.MailMessage]::new($senderAddress, $senderAddress, 'F1 Race Lens SMTP test', 'Gmail SMTP is configured for F1 Race Lens. This is a test message, without reset tokens or passwords.')
        try { $smtpClient.Send($testMessage); Write-Host 'Gmail accepted the test message. Check your inbox and spam folder.' }
        catch { Write-Host 'Test delivery failed. Check your app password, network and Gmail account settings. No secrets were printed.' }
        finally { $testMessage.Dispose(); $smtpClient.Dispose() }
    }
} catch {
    Write-Host 'Setup was not completed. Check the entered address, app password, origin and local file permissions. No secrets were printed.'
    exit 1
} finally {
    $appPassword = $null; $configuration = $null
    if ($secureAppPassword) { $secureAppPassword.Dispose() }
    if ($stagingPath -and (Test-Path -LiteralPath $stagingPath)) { Remove-Item -LiteralPath $stagingPath -Force }
}
