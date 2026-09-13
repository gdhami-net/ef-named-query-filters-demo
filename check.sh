#!/usr/bin/env bash
# Runs the suite that backs the post's claims. Exits non-zero if any fail.
set -euo pipefail
cd "$(dirname "$0")"

echo "ef-named-query-filters-demo — checking the post's claims"
echo "  Ef9   EF Core 9.0.20: no named overload, and the second unnamed filter wins"
echo "  Ef10  EF Core 10.0.12:"
echo "        ReplacementTests        two unnamed filters, the SQL, and the rows that leak"
echo "        NamedFilterTests        two named filters, both conditions in the WHERE"
echo "        IgnoreFilterTests       dropping one filter by name vs dropping all of them"
echo "        MixingTests             a named and an unnamed filter on one entity type"
echo "        TenantCaptureTests      per-instance parameter vs a constant in the cached model"
echo "        WhatIsFilteredTests     which routes to the database carry the filters"
echo "        RequiredNavigationTests the INNER JOIN the docs warn about"
echo

dotnet test --logger "console;verbosity=normal"
