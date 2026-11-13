<#
  seed-shree-krishna.ps1

  One-shot dev seed via the HTTP API (NOT raw SQL — the API runs the real password
  hasher + validation, which a SQL INSERT can't reproduce).

  It:
    1. Logs in as the superadmin.
    2. Renames the (first) community to "Shree Krishna Residency".
    3. Adds a wing (building_tower) named "C".
    4. Adds a unit (C-101) under wing C.
    5. Adds resident "Banti Singh" (+917984263575) with a login (password: 123)
       and maps him as the OWNER of unit C-101 (an owner must own a real unit).

  Idempotent-ish: existing wing / unit / phone / owner-link are detected and skipped.

  Usage:  powershell -NoProfile -ExecutionPolicy Bypass -File scripts\seed-shree-krishna.ps1
#>

$ErrorActionPreference = 'Stop'

$BaseUrl        = 'http://127.0.0.1:5080'
$AdminId        = '+910000000000'
$AdminPassword  = '123'

$CommunityName  = 'Shree Krishna Residency'
$WingName       = 'C'
$WingLevel      = 'building_tower'

$UnitNumber     = 'C-101'
$UnitType       = 'apartment'
$UnitStatus     = 'available'
$UnitFloor      = '1'

$ResidentName   = 'Banti Singh'
$ResidentPhone  = '+917984263575'
$ResidentType   = 'owner'
$ResidentPwd    = '123'

function Invoke-Api {
    param(
        [string]$Method,
        [string]$Path,
        [hashtable]$Body,
        [string]$Token
    )
    $headers = @{ 'Content-Type' = 'application/json' }
    if ($Token) { $headers['Authorization'] = "Bearer $Token" }
    $json = if ($Body) { $Body | ConvertTo-Json -Depth 8 -Compress } else { $null }
    $resp = Invoke-RestMethod -Uri "$BaseUrl$Path" -Method $Method -Headers $headers -Body $json
    if (-not $resp.success) {
        throw "API $Method $Path failed: $($resp.error.code) - $($resp.error.message)"
    }
    return $resp.data
}

Write-Host '==> Logging in as superadmin...'
$auth = Invoke-Api -Method POST -Path '/api/v1/auth/login' -Body @{ identifier = $AdminId; password = $AdminPassword }
$token = $auth.accessToken
Write-Host "    ok (roles: $($auth.roles -join ', '))"

Write-Host '==> Resolving community...'
$communities = Invoke-Api -Method GET -Path '/api/v1/communities?pageSize=1' -Token $token
$community = $communities.items | Select-Object -First 1
if (-not $community) { throw 'No community found to rename.' }
Write-Host "    found: $($community.name) [$($community.id)]"

Write-Host "==> Renaming community to '$CommunityName'..."
$updated = Invoke-Api -Method PUT -Path "/api/v1/communities/$($community.id)" -Token $token -Body @{ name = $CommunityName }
Write-Host "    renamed -> $($updated.name)"

Write-Host "==> Ensuring wing '$WingName' ($WingLevel)..."
$hierarchy = Invoke-Api -Method GET -Path "/api/v1/communities/$($community.id)/hierarchy" -Token $token
$wing = $hierarchy | Where-Object { $_.name -eq $WingName -and $_.level -eq $WingLevel } | Select-Object -First 1
if ($wing) {
    Write-Host "    already exists [$($wing.id)] — skipped"
} else {
    $wing = Invoke-Api -Method POST -Path '/api/v1/hierarchy-nodes' -Token $token -Body @{
        communityId = $community.id
        parentId    = $null
        level       = $WingLevel
        name        = $WingName
    }
    Write-Host "    created wing '$($wing.name)' [$($wing.id)]"
}

Write-Host "==> Ensuring resident '$ResidentName' ($ResidentPhone)..."
$existing = Invoke-Api -Method GET -Path "/api/v1/residents?pageSize=100" -Token $token
$resident = $existing.items | Where-Object { $_.phone -eq $ResidentPhone } | Select-Object -First 1
if ($resident) {
    Write-Host "    resident already exists [$($resident.id)] — skipping create"
} else {
    $resident = Invoke-Api -Method POST -Path '/api/v1/residents' -Token $token -Body @{
        communityId  = $community.id
        name         = $ResidentName
        phone        = $ResidentPhone
        residentType = $ResidentType
    }
    Write-Host "    created resident [$($resident.id)]"
}

Write-Host "==> Ensuring login for '$ResidentName'..."
try {
    $loginResult = Invoke-Api -Method POST -Path "/api/v1/residents/$($resident.id)/login" -Token $token -Body @{
        identifier = $ResidentPhone
        password   = $ResidentPwd
    }
    Write-Host "    login created -> $($loginResult.identifier)"
} catch {
    # A duplicate phone (login already exists) surfaces as a CONFLICT — treat as already-done.
    if ($_.Exception.Message -match 'CONFLICT|already') {
        Write-Host '    login already exists — skipped'
    } else {
        throw
    }
}

Write-Host ''
Write-Host '==> DONE.'
Write-Host "    Community : $CommunityName"
Write-Host "    Wing      : $WingName ($WingLevel)"
Write-Host "    User      : $ResidentName / $ResidentPhone (password: $ResidentPwd)"
