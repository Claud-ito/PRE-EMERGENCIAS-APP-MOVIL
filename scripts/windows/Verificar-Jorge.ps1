# Ejecutar en un proceso PowerShell desde el repositorio. Requiere LocalDB, sqlcmd y dotnet-ef.
param([int]$Port = 5098, [string]$AndroidSerial, [string]$AdbPath = 'adb')
if ($AndroidSerial) { . (Join-Path $PSScriptRoot 'Jorge-AndroidEnsayo.ps1') }
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '../..')
$apiProject = 'src/Univalle.Emergencias.Api'
$apiDirectory = (Resolve-Path $apiProject).Path
$apiDll = Join-Path $apiDirectory 'bin/Release/net10.0/Univalle.Emergencias.Api.dll'
$dbName = 'JorgeFlowTest_' + [guid]::NewGuid().ToString('N')
$server = '(localdb)\MSSQLLocalDB'
$baseUrl = "http://127.0.0.1:$Port"
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ConnectionStrings__DefaultConnection = "Server=$server;Database=$dbName;Integrated Security=true;TrustServerCertificate=true"
$env:ASPNETCORE_URLS = $baseUrl
$env:Logging__LogLevel__Default = 'Warning'
$env:DevelopmentAccounts__Password = if ($env:JORGE_MOBILE_TEST_PASSWORD) { $env:JORGE_MOBILE_TEST_PASSWORD } else { [guid]::NewGuid().ToString('N') }
$process = $null
function Start-TestApi {
    $p = Start-Process dotnet -ArgumentList ('"' + $apiDll + '"') -WorkingDirectory $apiDirectory -WindowStyle Hidden -PassThru
    try {
        for ($attempt = 0; $attempt -lt 45; $attempt++) {
            if ($p.HasExited) { throw 'La API de ensayo no pudo iniciar.' }
            try {
                $spec = Invoke-RestMethod "$baseUrl/openapi/v1.json" -TimeoutSec 2
                if ($spec.paths.'/api/v1/notifications/mine') { return $p }
            } catch { Start-Sleep -Milliseconds 350 }
        }
        throw 'La API de ensayo no respondió.'
    } catch { if (!$p.HasExited) { $p.Kill(); $p.WaitForExit() }; throw }
}
function Request($method, $route, $token, $payload) {
    $parameters = @{ Method=$method; Uri="$baseUrl/api/v1/$route"; TimeoutSec=15; ErrorAction='Stop' }
    if ($token) { $parameters.Headers = @{ Authorization="Bearer $token" } }
    if ($null -ne $payload) {
        $parameters.ContentType='application/json; charset=utf-8'
        $parameters.Body=[Text.Encoding]::UTF8.GetBytes(($payload | ConvertTo-Json -Depth 8))
    }
    Invoke-RestMethod @parameters
}
function Expect-Denied($method, $route, $token, $status) {
    try { $null = Request $method $route $token $null }
    catch {
        if ($_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq $status) { return }
        throw
    }
    throw "La ruta $route debía responder con $status."
}
function Login($name) {
    Request Post 'auth/login' $null @{username=$name; password=$env:DevelopmentAccounts__Password}
}
try {
    if ([Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners().Port -contains $Port) {
        throw "El puerto $Port ya está ocupado."
    }
    dotnet build $apiProject -c Release -m:1
    if ($LASTEXITCODE) { throw 'Falló la compilación.' }
    dotnet ef database update --project $apiProject --configuration Release --no-build
    if ($LASTEXITCODE) { throw 'Falló la migración de ensayo.' }
    Push-Location $apiDirectory
    try { dotnet $apiDll --seed-development-users; if ($LASTEXITCODE) { throw 'Falló la creación de usuarios de ensayo.' } }
    finally { Pop-Location }
    $process = Start-TestApi
    $staff = Login 'siso.prueba'
    $lead = Login 'lider.prueba'
    $staffToken = $staff.accessToken
    $leadToken = $lead.accessToken
    $witness = Login 'testigo.prueba'
    Expect-Denied Get 'notifications/mine' $null 401
    Expect-Denied Get 'notifications/mine' $witness.accessToken 403
    $module = Request Post 'locations/modules' $staffToken @{code='JO-TEST';name='Ensayo JO'}
    $floor = Request Post "locations/modules/$($module.moduleId)/floors" $staffToken @{name='Planta de ensayo'}
    $null = Request Post 'brigades' $staffToken @{name='Brigada de ensayo';leaderUserId=$lead.user.userId
        members=@(@{userId=$lead.user.userId;specialty='Primeros auxilios'});moduleIds=@($module.moduleId)}
    $today = [DateTimeOffset]::UtcNow.ToOffset([TimeSpan]::FromHours(-4)).ToString('yyyy-MM-dd')
    $null = Request Post 'turns/exceptions' $staffToken @{date=$today; isAvailable=$true; startTime='00:00:00'; endTime='23:59:59.9999999'}
    $null = Request Put 'coordination/auto' $staffToken @{enabled=$true}
    if ($AndroidSerial) { Set-TestEndpoint }
    $incident = Request Post 'incidents/reports' $staffToken @{clientRequestId=[guid]::NewGuid();type='Incendio'
        moduleId=$module.moduleId; floorId=$floor.floorId; preliminarySeverity='Leve'}
    $initial = @(Request Get 'notifications/mine' $staffToken $null)[0]
    if ($initial.incidentId -ne $incident.incidentId -or $initial.type -ne 'incident.initial-alert') { throw 'Falta la alerta inicial.' }
    Expect-Denied Patch "notifications/$($initial.eventId)/read" $leadToken 404
    Write-Output "Alerta inicial: sistema -> $($staff.user.userId) @ $($initial.registeredAtUtc)"
    if ($AndroidSerial) {
        Save-VisibleNotice 'Alerta inicial' $incident.incidentId 'JO02-Android-alerta.png'
        $displayed = @(Request Get 'notifications/mine' $staffToken $null)[0]
        if (!$displayed.deliveredAtUtc) { throw 'El dispositivo no confirmó la recepción.' }
        $lag = [datetimeoffset]::Parse($displayed.deliveredAtUtc) - [datetimeoffset]::Parse($initial.registeredAtUtc)
        Write-Output "Android, registro a confirmación: $($lag.TotalSeconds) s"
        Tap-Control (Find-Control 'Marcar como leído')
        $limit = [datetime]::UtcNow.AddSeconds(12)
        do {
            $receipt = @(Request Get 'notifications/mine' $staffToken $null)[0]
            if ($receipt.readAtUtc) { break }
            Start-Sleep -Milliseconds 250
        } while ([datetime]::UtcNow -lt $limit)
        if (!$receipt.readAtUtc) { throw 'La acción de lectura no llegó al servidor.' }
    } else { $null = Request Patch "notifications/$($initial.eventId)/read" $staffToken $null }
    $process.Kill(); $process.WaitForExit()
    $process = Start-TestApi
    $recovered = @(Request Get 'notifications/mine' $staffToken $null)[0]
    if ($recovered.eventId -ne $initial.eventId -or !$recovered.readAtUtc) { throw 'No se conservó la lectura tras el reinicio.' }
    if ($AndroidSerial) {
        Tap-Control (Find-Control 'Cuenta')
        Tap-Control (Find-Control 'Cerrar sesión')
        Open-TestInbox 'lider.prueba'
    }
    $deadline = [datetimeoffset]::Parse($incident.reportedAtUtc).AddSeconds(120)
    $timeout = $deadline.AddSeconds(20)
    do {
        $alerts = @(Request Get 'notifications/mine' $leadToken $null | Where-Object { $_ })
        if ($alerts.Count) { break }
        Start-Sleep -Seconds 1
    } while ([datetimeoffset]::UtcNow -lt $timeout)
    if ($alerts.Count -ne 1 -or $alerts[0].type -ne 'incident.auto' -or $alerts[0].incidentId -ne $incident.incidentId) { throw 'Falta el único aviso AUTO.' }
    if ($AndroidSerial) { Save-VisibleNotice 'Convocatoria AUTO' $incident.incidentId 'JO03-Android-auto.png' }
    $sent = [datetimeoffset]::Parse($alerts[0].registeredAtUtc)
    if ($sent -lt $deadline) { throw 'AUTO se adelantó al plazo.' }
    $assigned = @(Request Get 'incidents/assigned' $staffToken $null)
    if ($assigned.incidentId -notcontains $incident.incidentId) { throw 'Cambió el mando del incidente.' }
    Write-Output "AUTO: sistema -> $($lead.user.userId) @ $sent; demora desde la alerta: $(($sent - $deadline).TotalSeconds + 120) s"
    Write-Output "Flujo verificado para $($incident.incidentId) con reinicio del proceso."
} finally {
    if ($process -and !$process.HasExited) { $process.Kill(); $process.WaitForExit() }
    if ($dbName -match '^JorgeFlowTest_[0-9a-f]{32}$') {
        sqlcmd -S $server -E -b -Q "IF DB_ID('$dbName') IS NOT NULL BEGIN ALTER DATABASE [$dbName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$dbName]; END"
    }
}
