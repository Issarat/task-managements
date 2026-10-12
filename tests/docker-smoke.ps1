# Windows + PowerShell 7 smoke test for the published Linux image.
# Build first: docker build -f src/TaskManager.WebUI/Dockerfile -t taskflow:local .
# Run: pwsh -File tests/docker-smoke.ps1 [-RenderProxy]
# No database is connected. This does not test task CRUD or board synchronization.
param([string]$Image = 'taskflow:local', [switch]$RenderProxy)

$ErrorActionPreference = 'Stop'
$containerName = 'taskflow-smoke-' + [Guid]::NewGuid().ToString('N')
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$certificateDirectory = Join-Path $temporaryRoot $containerName
$certificatePath = Join-Path $certificateDirectory 'localhost.pfx'
$certificatePassword = [Convert]::ToHexString(
    [Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$containerStarted = $false
$script:checkCount = 0
$client = $null
$socket = $null
$socketTimeout = $null

function Assert-Smoke([bool]$Condition, [string]$Name) {
    if (-not $Condition) { throw "FAIL: $Name" }
    $script:checkCount++
    Write-Output "PASS: $Name"
}

function Read-HubMessage {
    $buffer = [byte[]]::new(4096)
    $message = [Text.StringBuilder]::new()
    do {
        $result = $socket.ReceiveAsync([ArraySegment[byte]]::new($buffer),
            $socketTimeout.Token).GetAwaiter().GetResult()
        if ($result.MessageType -eq [Net.WebSockets.WebSocketMessageType]::Close) {
            throw 'WebSocket closed before the expected reply.'
        }
        [void]$message.Append([Text.Encoding]::UTF8.GetString($buffer, 0, $result.Count))
    } while (-not $result.EndOfMessage)
    return ($message.ToString().Split([char]30)[0] | ConvertFrom-Json)
}

function Send-HubMessage([string]$Message) {
    $bytes = [Text.Encoding]::UTF8.GetBytes($Message + [char]30)
    [void]$socket.SendAsync([ArraySegment[byte]]::new($bytes),
        [Net.WebSockets.WebSocketMessageType]::Text, $true,
        $socketTimeout.Token).GetAwaiter().GetResult()
}

try {
    docker image inspect $Image --format '{{.Id}}'
    if ($LASTEXITCODE -ne 0) { throw "Build image $Image before running this check." }
    # Bind only to loopback on an unused host port. Mount the certificate read-only;
    # never include it in the image or connect to the existing TaskManager database.
    $dockerArguments = @('run', '--detach', '--rm', '--name', $containerName,
        '--env', 'ASPNETCORE_ENVIRONMENT=Production',
        '--env', 'ExpiredDataCleanup__Enabled=false',
        '--env', 'ConnectionStrings__DefaultConnection=Server=127.0.0.1,1;Database=TaskFlow_DockerSmoke_NotConnected;User Id=unused;Password=NotARealCredential;Encrypt=True;Connect Timeout=1')
    if ($RenderProxy) {
        $containerPort = '8080/tcp'
        $urlScheme = 'http'
        $dockerArguments += @('--publish', '127.0.0.1::8080',
            '--env', 'Hosting__BehindReverseProxy=true',
            '--env', 'ASPNETCORE_HTTPS_PORT=443')
    } else {
        $containerPort = '8081/tcp'
        $urlScheme = 'https'
        [void](New-Item -ItemType Directory -Path $certificateDirectory)
        dotnet dev-certs https --export-path $certificatePath --password $certificatePassword
        if ($LASTEXITCODE -ne 0) { throw 'Unable to export the local development certificate.' }
        $dockerArguments += @('--publish', '127.0.0.1::8081',
            '--mount', "type=bind,source=$certificateDirectory,target=/https,readonly",
            '--env', 'ASPNETCORE_HTTPS_PORTS=8081',
            '--env', 'ASPNETCORE_Kestrel__Certificates__Default__Path=/https/localhost.pfx',
            '--env', "ASPNETCORE_Kestrel__Certificates__Default__Password=$certificatePassword")
    }
    $dockerArguments += $Image
    docker @dockerArguments
    if ($LASTEXITCODE -ne 0) { throw 'Unable to start the test container.' }
    $containerStarted = $true
    $portMapping = docker port $containerName $containerPort
    if ($LASTEXITCODE -ne 0 -or $portMapping -notmatch '^127\.0\.0\.1:(\d+)$') {
        throw 'Unable to determine the HTTPS port.'
    }
    $baseUrl = "${urlScheme}://localhost:$($Matches[1])"
    $handler = [Net.Http.HttpClientHandler]::new()
    $handler.UseCookies = $false
    $handler.AllowAutoRedirect = $false
    $client = [Net.Http.HttpClient]::new($handler)
    $client.Timeout = [TimeSpan]::FromSeconds(5)
    if ($RenderProxy) { $client.DefaultRequestHeaders.Add('X-Forwarded-Proto', 'https') }

    $landing = $null
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        try {
            $landing = $client.GetAsync($baseUrl).GetAwaiter().GetResult()
            break
        } catch { Start-Sleep -Milliseconds 1000 }
    }
    $landingCheck = if ($RenderProxy) { 'forwarded HTTPS scheme renders without a redirect loop' }
        else { 'HTTPS landing returns 200 with trusted TLS' }
    Assert-Smoke ($null -ne $landing -and [int]$landing.StatusCode -eq 200) $landingCheck
    $health = $client.GetAsync($baseUrl + '/health').GetAwaiter().GetResult()
    Assert-Smoke ([int]$health.StatusCode -eq 200) 'liveness endpoint works without a database'
    if ($RenderProxy) {
        [void]$client.DefaultRequestHeaders.Remove('X-Forwarded-Proto')
        $rawHealth = $client.GetAsync($baseUrl + '/health').GetAwaiter().GetResult()
        Assert-Smoke ([int]$rawHealth.StatusCode -eq 200) 'internal HTTP health probe does not redirect'
        $redirect = $client.GetAsync($baseUrl).GetAwaiter().GetResult()
        Assert-Smoke ([int]$redirect.StatusCode -eq 307 -and $redirect.Headers.Location.Scheme -eq 'https') 'unforwarded HTTP still redirects to HTTPS'
        $client.DefaultRequestHeaders.Add('X-Forwarded-Proto', 'https')
    }
    $html = $landing.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    Assert-Smoke ($html.Contains('TaskFlow')) 'published Razor page renders TaskFlow'
    Assert-Smoke ($html.Contains('__RequestVerificationToken')) 'antiforgery form token is rendered'
    foreach ($path in @('/css/site.css', '/js/site.js', '/js/board.js',
        '/lib/microsoft/signalr/dist/browser/signalr.js')) {
        $response = $client.GetAsync($baseUrl + $path).GetAwaiter().GetResult()
        Assert-Smoke ([int]$response.StatusCode -eq 200) "published static asset $path"
        $response.Dispose()
    }
    $formatted = $client.GetStringAsync($baseUrl + '/dates/format?value=2026-10-13').GetAwaiter().GetResult() | ConvertFrom-Json
    Assert-Smoke ($formatted.value -eq '2026-10-13' -and $formatted.full -eq '13 ตุลาคม 2569') 'Thai date formatting works on Linux'
    $invalidDate = $client.GetAsync($baseUrl + '/dates/format?value=invalid').GetAwaiter().GetResult()
    Assert-Smoke ([int]$invalidDate.StatusCode -eq 400) 'invalid dates are rejected'
    $content = [Net.Http.StringContent]::new('')
    $negotiation = $client.PostAsync($baseUrl + '/hubs/board/negotiate?negotiateVersion=1', $content).GetAwaiter().GetResult()
    Assert-Smoke ([int]$negotiation.StatusCode -eq 200) 'SignalR negotiation endpoint responds'
    $negotiated = $negotiation.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
    Assert-Smoke (@($negotiated.availableTransports.transport) -contains 'WebSockets') 'WebSocket transport is available'

    $socket = [Net.WebSockets.ClientWebSocket]::new()
    if ($RenderProxy) { $socket.Options.SetRequestHeader('X-Forwarded-Proto', 'https') }
    $socketTimeout = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(15))
    [void]$socket.ConnectAsync([Uri]($baseUrl.Replace('https:', 'wss:').Replace('http:', 'ws:') +
        '/hubs/board?id=' + [Uri]::EscapeDataString($negotiated.connectionToken)),
        $socketTimeout.Token).GetAwaiter().GetResult()
    Send-HubMessage '{"protocol":"json","version":1}'
    $handshake = Read-HubMessage
    Assert-Smoke (-not $handshake.PSObject.Properties['error']) 'real WebSocket SignalR handshake succeeds'
    Send-HubMessage '{"type":1,"invocationId":"smoke","target":"JoinBoard","arguments":["smoke-board"]}'
    $completion = Read-HubMessage
    Assert-Smoke ($completion.type -eq 3 -and $completion.invocationId -eq 'smoke' -and
        $completion.error.Contains('กรุณาเริ่มทดลองใช้งานก่อน')) 'JoinBoard rejects a connection without a guest cookie'
    Write-Output "$script:checkCount Docker smoke checks passed. No database was connected."
} catch {
    if ($containerStarted) { docker logs --tail 45 $containerName }
    throw
} finally {
    if ($socket) { $socket.Dispose() }
    if ($socketTimeout) { $socketTimeout.Dispose() }
    if ($client) { $client.Dispose() }
    if ($containerStarted) {
        docker stop --timeout 5 $containerName
        if ($LASTEXITCODE -ne 0) { Write-Warning "Check test container $containerName manually." }
    }
    # Exact generated targets only; never recurse into a temp or workspace root.
    if (-not [IO.Path]::GetFullPath($certificateDirectory).StartsWith(
        $temporaryRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe cleanup path.' }
    if (Test-Path -LiteralPath $certificatePath) { Remove-Item -LiteralPath $certificatePath }
    if (Test-Path -LiteralPath $certificateDirectory) { Remove-Item -LiteralPath $certificateDirectory }
}
