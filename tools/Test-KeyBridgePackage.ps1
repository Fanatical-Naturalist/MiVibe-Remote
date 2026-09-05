[CmdletBinding()]
param([switch]$Elevated)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$helperPath = Join-Path $projectRoot 'artifacts\keybridge\MiVibe.Remote.KeyBridge\MiVibe.Remote.KeyBridge.exe'
$name = 'MiVibeRemote.Keys.' + [Guid]::NewGuid().ToString('N')
$options = [IO.Pipes.PipeOptions]::Asynchronous -bor [IO.Pipes.PipeOptions]::CurrentUserOnly
$server = [IO.Pipes.NamedPipeServerStream]::new($name, [IO.Pipes.PipeDirection]::InOut, 1, [IO.Pipes.PipeTransmissionMode]::Byte, $options)
$helper = $reader = $writer = $null
$messages = [Collections.Generic.List[object]]::new()
try {
    $start = @{
        FilePath = $helperPath
        ArgumentList = @('--pipe', $name, '--parent-pid', $PID)
        WorkingDirectory = Split-Path -Parent $helperPath
        PassThru = $true
        WindowStyle = 'Hidden'
    }
    if ($Elevated) { $start.Verb = 'RunAs' }
    $helper = Start-Process @start
    if (-not $server.WaitForConnectionAsync().Wait(30000)) { throw 'Packaged helper did not connect.' }
    $reader = [IO.StreamReader]::new($server, [Text.UTF8Encoding]::new($false), $false, 1024, $true)
    $writer = [IO.StreamWriter]::new($server, [Text.UTF8Encoding]::new($false), 1024, $true)
    $writer.AutoFlush = $true
    if ($Elevated) { $writer.WriteLine('{"command":"ping"}') }
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    $pending = $reader.ReadLineAsync()
    while ([DateTime]::UtcNow -lt $deadline) {
        if (-not $pending.Wait(200)) { continue }
        $line = $pending.Result
        if ($null -eq $line) { break }
        $message = $line | ConvertFrom-Json
        $messages.Add($message)
        if ($message.type -eq 'key') { throw 'Unexpected key before calibration.' }
        $pending = $reader.ReadLineAsync()
    }
    if ($server.IsConnected -and -not $helper.HasExited) {
        try { $writer.WriteLine('{"command":"stop"}') } catch {
            if ($Elevated) { throw }
        }
    }
    if (-not $helper.WaitForExit(25000)) { throw 'Helper did not complete safe exit.' }
    $phases = @($messages | Where-Object type -eq 'status' | ForEach-Object phase)
    $heartbeats = @($messages | Where-Object type -eq 'heartbeat').Count
    if ($Elevated -and ('calibrating' -notin $phases -or $heartbeats -lt 2)) {
        throw ('Packaged observer did not become ready: ' + ($messages | ConvertTo-Json -Compress))
    }
    if (-not $Elevated -and 'administrator_required' -notin @($messages | ForEach-Object detail)) {
        throw 'Expected an explicit administrator-required result.'
    }
    [ordered]@{ elevated = [bool]$Elevated; phases = $phases; heartbeats = $heartbeats; keyActions = 0; safeExit = $true; exitCode = $helper.ExitCode } | ConvertTo-Json -Compress
}
finally {
    $server.Dispose()
    if ($helper -and -not $helper.HasExited) { [void]$helper.WaitForExit(25000) }
    if ($helper -and $helper.HasExited) { $helper.Dispose() }
    if ($writer) { try { $writer.Dispose() } catch {} }
    if ($reader) { try { $reader.Dispose() } catch {} }
}
