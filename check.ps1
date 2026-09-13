# Runs the suite that backs the post's claims. Exits non-zero if any fail.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

Write-Host 'ef-named-query-filters-demo - checking the post''s claims'
Write-Host '  Ef9   EF Core 9.0.20: no named overload, and the second unnamed filter wins'
Write-Host '  Ef10  EF Core 10.0.12:'
Write-Host '        ReplacementTests        two unnamed filters, the SQL, and the rows that leak'
Write-Host '        NamedFilterTests        two named filters, both conditions in the WHERE'
Write-Host '        IgnoreFilterTests       dropping one filter by name vs dropping all of them'
Write-Host '        MixingTests             a named and an unnamed filter on one entity type'
Write-Host '        TenantCaptureTests      per-instance parameter vs a constant in the cached model'
Write-Host '        WhatIsFilteredTests     which routes to the database carry the filters'
Write-Host '        RequiredNavigationTests the INNER JOIN the docs warn about'
Write-Host ''

dotnet test --logger 'console;verbosity=normal'
exit $LASTEXITCODE
