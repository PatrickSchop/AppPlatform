<#
.SYNOPSIS
    Gate D regression script: exercises MultiTenantSample's tenant isolation through a
    running Functions host. Covers checklist rows 3-9, 11-13 and 15-16 from
    .plans/multi-tenancy-v1/step-MT-07-gate-d.md. Rows 1, 2, 10, 14, 17 and 18 need a
    migrate run, a metadata check, a SQL query, a manual config edit, or a second sample
    respectively, and stay manual.

.PARAMETER Token
    A bearer token for the operator's identity, e.g.
    az account get-access-token --resource api://<api-client-id> --query accessToken -o tsv

.PARAMETER Contoso
    Contoso's tenant id (from appsettings.development.json's tenancy:devDirectory:tenants).

.PARAMETER Fabrikam
    Fabrikam's tenant id (from appsettings.development.json's tenancy:devDirectory:tenants).

.PARAMETER BaseUrl
    The running host's base URL. Defaults to the local func start address.
#>
param(
    [Parameter(Mandatory)] [string]$Token,
    [Parameter(Mandatory)] [string]$Contoso,
    [Parameter(Mandatory)] [string]$Fabrikam,
    [string]$BaseUrl = "http://localhost:7071"
)

$ErrorActionPreference = "Stop"
$script:failures = 0

function Invoke-Api {
    param(
        [string]$Method = "GET",
        [string]$Path,
        [hashtable]$Headers = @{},
        [string]$Body = $null
    )

    $uri = "$BaseUrl$Path"
    $params = @{ Method = $Method; Uri = $uri; Headers = $Headers; UseBasicParsing = $true }
    if ($Body) {
        $params.Body = $Body
        $params.ContentType = "application/json"
    }

    try {
        $response = Invoke-WebRequest @params
        return [pscustomobject]@{ StatusCode = [int]$response.StatusCode; Content = $response.Content }
    }
    catch {
        $webResponse = $_.Exception.Response
        if ($null -eq $webResponse) { throw }
        $status = [int]$webResponse.StatusCode
        $stream = $webResponse.GetResponseStream()
        $reader = New-Object System.IO.StreamReader($stream)
        $content = $reader.ReadToEnd()
        return [pscustomobject]@{ StatusCode = $status; Content = $content }
    }
}

function ConvertTo-JsonArray {
    # @($json | ConvertFrom-Json) double-wraps: ConvertFrom-Json emits a JSON array as one
    # pipeline object, so @() around the whole pipeline wraps that single array again instead
    # of normalizing it. Parsing first, then wrapping the already-materialized value, avoids it.
    param([string]$Json)
    $parsed = $Json | ConvertFrom-Json
    return @($parsed)
}

function Test-Check {
    param([string]$Name, [scriptblock]$Assertion)

    try {
        $ok = & $Assertion
    }
    catch {
        $ok = $false
        Write-Host "  Exception: $_"
    }

    if ($ok) {
        Write-Host "PASS  $Name" -ForegroundColor Green
    }
    else {
        Write-Host "FAIL  $Name" -ForegroundColor Red
        $script:failures++
    }
}

$authHeader = @{ Authorization = "Bearer $Token" }
$headerC = @{ Authorization = "Bearer $Token"; "X-Tenant-Id" = $Contoso }
$headerF = @{ Authorization = "Bearer $Token"; "X-Tenant-Id" = $Fabrikam }

# 3: GET /api/me/tenants, no token -> 401
$r = Invoke-Api -Path "/api/me/tenants"
Test-Check "3: GET /api/me/tenants no token -> 401" { $r.StatusCode -eq 401 }

# 4: GET /api/me/tenants, token, no header -> 200, both tenants with roles
$r = Invoke-Api -Path "/api/me/tenants" -Headers $authHeader
Test-Check "4: GET /api/me/tenants token, no header -> 200, both tenants" {
    if ($r.StatusCode -ne 200) { return $false }
    $json = $r.Content | ConvertFrom-Json
    $json.registered -eq $true -and $json.tenants.Count -eq 2
}

# 5: GET /api/projects, token, no header -> 409 tenant_required
$r = Invoke-Api -Path "/api/projects" -Headers $authHeader
Test-Check "5: GET /api/projects token, no header -> 409 tenant_required" {
    $r.StatusCode -eq 409 -and ($r.Content | ConvertFrom-Json).error -eq "tenant_required"
}

# 6: POST /api/projects, header C -> 200/201
$r = Invoke-Api -Method POST -Path "/api/projects" -Headers $headerC -Body '{"name":"C1"}'
Test-Check "6: POST /api/projects header C -> 200/201" { $r.StatusCode -eq 200 -or $r.StatusCode -eq 201 }

# 7: POST /api/projects, header F -> 403 (viewer in Fabrikam)
$r = Invoke-Api -Method POST -Path "/api/projects" -Headers $headerF -Body '{"name":"F1"}'
Test-Check "7: POST /api/projects header F -> 403 (viewer)" { $r.StatusCode -eq 403 }

# 8: GET /api/projects, header C sees C1, header F sees none
$rc = Invoke-Api -Path "/api/projects" -Headers $headerC
$rf = Invoke-Api -Path "/api/projects" -Headers $headerF
Test-Check "8: GET /api/projects header C/F isolation" {
    $jc = ConvertTo-JsonArray $rc.Content
    $jf = ConvertTo-JsonArray $rf.Content
    (@($jc | Where-Object { $_.name -eq "C1" })).Count -ge 1 -and $jf.Count -eq 0
}

# 9: GET /api/projects, header = random GUID -> 403 tenant_forbidden
$headerBad = @{ Authorization = "Bearer $Token"; "X-Tenant-Id" = [guid]::NewGuid().ToString() }
$r = Invoke-Api -Path "/api/projects" -Headers $headerBad
Test-Check "9: GET /api/projects header=random GUID -> 403 tenant_forbidden" {
    $r.StatusCode -eq 403 -and ($r.Content | ConvertFrom-Json).error -eq "tenant_forbidden"
}

# 11: GET /api/countries, header F -> both seeded countries (application-wide)
$r = Invoke-Api -Path "/api/countries" -Headers $headerF
Test-Check "11: GET /api/countries header F -> both seeded countries" {
    $json = ConvertTo-JsonArray $r.Content
    $r.StatusCode -eq 200 -and $json.Count -eq 2
}

# 12: POST /api/public/fabrikam/feedback, no token -> 200; visible to F only
$feedbackText = "gate-d-$([guid]::NewGuid())"
$r = Invoke-Api -Method POST -Path "/api/public/fabrikam/feedback" -Body "{`"text`":`"$feedbackText`"}"
$ok12Submit = $r.StatusCode -eq 200
$rf = Invoke-Api -Path "/api/feedback" -Headers $headerF
$rc = Invoke-Api -Path "/api/feedback" -Headers $headerC
Test-Check "12: anonymous feedback via slug isolated to Fabrikam" {
    $jf = ConvertTo-JsonArray $rf.Content
    $jc = ConvertTo-JsonArray $rc.Content
    $ok12Submit -and
        (@($jf | Where-Object { $_.text -eq $feedbackText })).Count -eq 1 -and
        (@($jc | Where-Object { $_.text -eq $feedbackText })).Count -eq 0
}

# 13: GET /api/reports/projects-per-tenant, header C -> 403 (no reporter role)
$r = Invoke-Api -Path "/api/reports/projects-per-tenant" -Headers $headerC
Test-Check "13: GET /api/reports/projects-per-tenant header C -> 403 (no reporter)" { $r.StatusCode -eq 403 }

# 15: POST /api/projects/count, header C; poll GET /api/tasks header C until Completed
$rcProjects = Invoke-Api -Path "/api/projects" -Headers $headerC
$expectedCount = (ConvertTo-JsonArray $rcProjects.Content).Count

$r = Invoke-Api -Method POST -Path "/api/projects/count" -Headers $headerC
$taskId = ($r.Content | ConvertFrom-Json).taskId

$completedTask = $null
for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Seconds 1
    $rt = Invoke-Api -Path "/api/tasks" -Headers $headerC
    $tasks = ConvertTo-JsonArray $rt.Content
    $task = $tasks | Where-Object { $_.id -eq $taskId }
    if ($task -and $task.status -eq "Completed") {
        $completedTask = $task
        break
    }
}

Test-Check "15: projects/count task completes with Contoso-only count" {
    $null -ne $completedTask -and $completedTask.statusMessage -eq "$expectedCount project(s)"
}

# 16: GET /api/tasks, header F -> does not contain Contoso's task
$rf = Invoke-Api -Path "/api/tasks" -Headers $headerF
Test-Check "16: GET /api/tasks header F excludes Contoso's task" {
    $tasksF = ConvertTo-JsonArray $rf.Content
    (@($tasksF | Where-Object { $_.id -eq $taskId })).Count -eq 0
}

Write-Host ""
if ($script:failures -gt 0) {
    Write-Host "$($script:failures) check(s) FAILED" -ForegroundColor Red
    exit 1
}

Write-Host "All automated checks PASSED" -ForegroundColor Green
exit 0
