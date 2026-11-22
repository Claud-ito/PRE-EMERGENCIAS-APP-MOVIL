# Helpers para la copia temporal del emulador usada por Verificar-Jorge.ps1.
function Invoke-TestAdb {
    & $AdbPath -s $AndroidSerial @args
    if ($LASTEXITCODE) { throw 'Falló una operación ADB en el emulador de ensayo.' }
}
function Get-TestNodes {
    $null = Invoke-TestAdb shell uiautomator dump /sdcard/jorge-test-ui.xml
    [xml]$tree = (Invoke-TestAdb shell cat /sdcard/jorge-test-ui.xml) -join ''
    $tree.SelectNodes('//node[@package="com.companyname.preemergenciasappmovil"]')
}
function Find-Control($label, $fieldIndex = -1) {
    $until = [datetime]::UtcNow.AddSeconds(18)
    do {
        $nodes = @(Get-TestNodes)
        if ($fieldIndex -ge 0) { $node = @($nodes | Where-Object class -eq 'android.widget.EditText')[$fieldIndex] }
        else {
            $node = $nodes | Where-Object { $_.'content-desc' -eq $label } | Select-Object -Last 1
            if (!$node) { $node = $nodes | Where-Object { $_.text -eq $label } | Select-Object -Last 1 }
        }
        if ($node) { return $node }
    } while ([datetime]::UtcNow -lt $until)
    throw "Control de ensayo no encontrado: $label"
}
function Tap-Control($node) {
    $points = [regex]::Matches($node.bounds, '\d+')
    $x = [int](([int]$points[0].Value + [int]$points[2].Value) / 2)
    $y = [int](([int]$points[1].Value + [int]$points[3].Value) / 2)
    $null = Invoke-TestAdb shell input tap $x $y
}
function Set-TestField($index, $value) {
    Tap-Control (Find-Control '' $index)
    $null = Invoke-TestAdb shell input keycombination 113 29
    $null = Invoke-TestAdb shell input text $value
    $null = Invoke-TestAdb shell input keyevent 4
}
function Open-TestInbox($username) {
    Tap-Control (Find-Control 'Cuenta')
    Set-TestField 0 $username
    Set-TestField 1 $env:DevelopmentAccounts__Password
    Tap-Control (Find-Control 'Ingresar')
    Tap-Control (Find-Control 'Avisos')
    $null = Find-Control 'Sin avisos.'
}
function Set-TestEndpoint {
    if ($AndroidSerial -notmatch '^emulator-\d+$') { throw 'Se requiere una instancia temporal de emulador.' }
    Tap-Control (Find-Control 'Inicio')
    Set-TestField 0 "http://10.0.2.2:$Port"
    Tap-Control (Find-Control 'Guardar dirección')
    Open-TestInbox 'siso.prueba'
    Tap-Control (Find-Control 'Turnos')
    $null = Find-Control 'Calendario compartido · hora de Bolivia'
    $until = [datetime]::UtcNow.AddSeconds(15)
    do {
        $covered = @(Get-TestNodes | Where-Object { $_.text -like '*Mi turno*' })
        if ($covered.Count) { break }
    } while ([datetime]::UtcNow -lt $until)
    if (!$covered.Count) { throw 'El calendario no mostró el turno de ensayo.' }
    Tap-Control (Find-Control 'Avisos')
    $null = Find-Control 'Sin avisos.'
}
function Save-VisibleNotice($eventType, $incidentId, $imageName) {
    $until = [datetime]::UtcNow.AddSeconds(18)
    do {
        $found = @(Get-TestNodes | Where-Object { $_.text -like "*$eventType*" -and $_.text -like "*$incidentId*" })
        if ($found.Count) {
            $folder = Join-Path (Get-Location) 'docs/evidencias'
            $null = Invoke-TestAdb shell screencap -p /sdcard/jorge-test-screen.png
            $null = Invoke-TestAdb pull /sdcard/jorge-test-screen.png (Join-Path $folder $imageName)
            return
        }
    } while ([datetime]::UtcNow -lt $until)
    throw "La pantalla no mostró $eventType para el incidente esperado."
}
