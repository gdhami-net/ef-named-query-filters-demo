# ef-named-query-filters-demo

Companion repo for **"The second query filter replaces the first"**
([gdhami.net](https://gdhami.net) — link added when the post is live).

An EF Core global query filter adds a condition to every LINQ query for an
entity type. Two of them are common in the same codebase: a tenant filter
(`TenantId == currentTenant`) and a soft-delete filter (`!IsDeleted`). Written
as two unnamed `HasQueryFilter` calls on the same entity type, only the second
one survives — and if the soft-delete call is the later one, the condition that
disappears is the tenant condition.

The point of this repo is that you do not have to take that on trust. Every test
asserts the WHERE clause EF generates **and** the rows that come back, so the
leak is visible as data rather than as a missing predicate.

Two tenants are seeded, each with one live and one deleted invoice:

| Invoice | Tenant   | Deleted |
|---------|----------|---------|
| A-1     | `acme`   | no      |
| A-2     | `acme`   | yes     |
| G-1     | `globex` | no      |
| G-2     | `globex` | yes     |

Queried as `acme`, the answer should be `[A-1]`.

## The two runs, side by side

Both filters unnamed (`TwoUnnamedFiltersContext`):

```text
SELECT "i"."Id", "i"."CustomerId", "i"."IsDeleted", "i"."Number", "i"."TenantId"
FROM "Invoices" AS "i"
WHERE NOT ("i"."IsDeleted")
ORDER BY "i"."Id"

rows: A-1, G-1        <- one of them belongs to globex
```

Both filters named (`TwoNamedFiltersContext`):

```text
.param set @ef_filter__CurrentTenant 'acme'

SELECT "i"."Id", "i"."CustomerId", "i"."IsDeleted", "i"."Number", "i"."TenantId"
FROM "Invoices" AS "i"
WHERE "i"."TenantId" = @ef_filter__CurrentTenant AND NOT ("i"."IsDeleted")
ORDER BY "i"."Id"

rows: A-1
```

## What each test proves

### `Ef9` — EF Core 9.0.20, `net9.0`

| Test | Claim |
|---|---|
| `There_is_no_named_HasQueryFilter_overload_on_this_build` | reflection over `EntityTypeBuilder<T>` finds no `HasQueryFilter(string, …)`, and every `IgnoreQueryFilters` overload takes one argument |
| `The_second_unnamed_filter_replaces_the_first_here_too` | the WHERE holds only `NOT ("i"."IsDeleted")`, and `acme` gets `[A-1, G-1]` |
| `Nothing_is_logged_about_the_filter_that_was_dropped` | with `LogTo` at Trace, no warning and no message mentioning a query filter |
| `The_model_holds_exactly_one_filter` | `IEntityType.GetQueryFilter()` returns the soft-delete expression and nothing else |
| `The_one_expression_workaround_keeps_both_conditions` | `i => i.TenantId == CurrentTenant && !i.IsDeleted` produces both conditions |
| `IgnoreQueryFilters_is_all_or_nothing_on_this_build` | the no-argument call returns all four rows |
| `Report_the_build_this_suite_is_running_against` | prints and asserts `Microsoft.EntityFrameworkCore` 9.0.20 |

### `Ef10` — EF Core 10.0.12, `net10.0`

| Test | Claim |
|---|---|
| `ReplacementTests.The_second_unnamed_filter_replaces_the_first` | EF 10 has not changed the unnamed behaviour: the WHERE is `NOT ("i"."IsDeleted")` alone |
| `ReplacementTests.The_replacement_returns_the_other_tenants_live_rows` | `[A-1, G-1]`, one row from another tenant |
| `ReplacementTests.The_call_that_wins_is_the_last_one` | with the calls swapped, the tenant condition survives and the soft-delete one is lost |
| `ReplacementTests.Nothing_is_logged_when_a_filter_is_replaced` | Trace-level logging, no warning, nothing mentioning a query filter |
| `ReplacementTests.The_model_holds_exactly_one_filter_for_the_entity_type` | `GetDeclaredQueryFilters()` returns a single entry whose `Key` is null |
| `ReplacementTests.One_combined_expression_keeps_both_conditions` | the EF 9 workaround still works on EF 10 |
| `NamedFilterTests.Both_named_filters_reach_the_sql` | `WHERE "i"."TenantId" = @ef_filter__CurrentTenant AND NOT ("i"."IsDeleted")` |
| `NamedFilterTests.The_conditions_appear_in_the_order_the_filters_were_configured` | the same two filters configured soft-delete first produce `NOT (…) AND "i"."TenantId" = …` |
| `NamedFilterTests.Only_the_current_tenants_live_rows_come_back` | `[A-1]`, and zero rows from another tenant |
| `NamedFilterTests.The_model_holds_both_filters_under_their_names` | two entries, keyed `Tenant` and `SoftDelete` |
| `NamedFilterTests.Two_filters_under_one_name_still_replace_each_other` | a name scopes replacement, it does not prevent it |
| `NamedFilterTests.The_filter_applies_to_an_included_navigation_too` | `Include(c => c.Invoices)` carries both conditions into the subquery |
| `IgnoreFilterTests.Ignoring_SoftDelete_by_name_keeps_the_tenant_condition` | `[A-1, A-2]`, zero rows from another tenant |
| `IgnoreFilterTests.Ignoring_Tenant_by_name_keeps_the_soft_delete_condition` | `[A-1, G-1]` |
| `IgnoreFilterTests.The_no_argument_call_still_drops_every_filter_including_the_tenant` | no WHERE at all, all four rows |
| `IgnoreFilterTests.A_name_that_matches_nothing_is_accepted_silently` | `IgnoreQueryFilters(["SoftDeleted"])` — a typo — throws nothing, warns nothing, changes nothing |
| `IgnoreFilterTests.Ignoring_one_name_applies_to_the_included_navigation_as_well` | the soft-delete condition is gone from both the outer query and the navigation's subquery, and the tenant condition is still on both |
| `MixingTests.Constructing_the_context_does_not_throw` | `OnModelCreating` has not run yet |
| `MixingTests.Touching_the_model_throws_with_the_message_the_post_quotes` | the exact `InvalidOperationException` text, asserted with `Assert.Equal` |
| `MixingTests.The_first_query_throws_the_same_thing` | same message from the first query instead of `context.Model` |
| `MixingTests.The_order_of_the_two_calls_does_not_matter` | unnamed-then-named fails identically |
| `MixingTests.The_restriction_is_per_entity_type_not_per_model` | a named filter on `Invoice` and an unnamed one on `Customer` is fine |
| `TenantCaptureTests.Reading_the_tenant_off_the_context_gives_a_parameter_per_instance` | one SQL string, two tenants, `[A-1]` and `[G-1]` |
| `TenantCaptureTests.The_parameter_is_named_after_the_captured_member_not_the_filter` | filter `Tenant`, property `CurrentTenant`, parameter `@ef_filter__CurrentTenant` |
| `TenantCaptureTests.A_local_copy_of_the_tenant_is_frozen_into_the_cached_model` | capture a local instead of the context and `'acme'` is inlined; a later `globex` context reuses the cached model and reads acme's row |
| `TenantCaptureTests.An_IEntityTypeConfiguration_can_reach_the_tenant_through_an_unassigned_field` | the workaround from Microsoft's docs, measured |
| `WhatIsFilteredTests.FromSql_on_a_DbSet_is_composed_over_and_stays_filtered` | EF wraps the raw SQL in a subquery and applies both filters outside it |
| `WhatIsFilteredTests.ExecuteUpdate_carries_the_filters_into_the_UPDATE_statement` | one row affected, and the filters are in the UPDATE's WHERE |
| `WhatIsFilteredTests.ExecuteDelete_carries_them_too` | one row affected |
| `WhatIsFilteredTests.SqlQuery_is_not_an_entity_query_and_is_not_filtered` | `Database.SqlQuery<string>` returns all four rows |
| `WhatIsFilteredTests.ExecuteSqlRaw_goes_straight_to_the_database` | `Database.ExecuteSql` updates all four rows |
| `RequiredNavigationTests.Without_the_Include_every_invoice_comes_back` | four rows, because `Invoice` has no filter in that context |
| `RequiredNavigationTests.With_the_Include_the_inner_join_drops_the_other_tenants_invoices` | `Include` on a required navigation makes it an INNER JOIN and two rows disappear |
| `VersionTests.Report_the_build_this_suite_is_running_against` | prints and asserts `Microsoft.EntityFrameworkCore` 10.0.12 |
| `VersionTests.The_named_overloads_exist_on_this_build` | `HasQueryFilter(string, …)` and `IgnoreQueryFilters(IReadOnlyCollection<string>)` are present |

42 tests, all passing.

## Run it

```bash
dotnet test
```

Or `./check.sh` (bash) / `./check.ps1` (PowerShell), which run the same suite and
print what each group covers. `dotnet test` at the root builds and runs both
projects, so you need the .NET 9 and .NET 10 runtimes on the machine.

To see the SQL each test measured:

```bash
dotnet test --logger "console;verbosity=detailed"
```

Every test that asserts a WHERE clause also writes the full `ToQueryString()`
output, so the SQL quoted in the post can be diffed against your own run.

## Determinism

There are no file watchers, no timers and no network here. Each test class opens
its own `Filename=:memory:` SQLite connection, creates the schema, seeds the four
invoices above and throws the database away afterwards, so tests can run in
parallel without sharing state.

One test does depend on ordering, and it says so: EF caches the model per context
type, so `A_local_copy_of_the_tenant_is_frozen_into_the_cached_model` needs to be
the first thing to build `LocalCaptureContext`'s model. That context type is used
by that test and by nothing else, which makes it deterministic.

## Versions

Built and measured on .NET SDK 10.0.201, on Windows 11.

- `Ef10` targets `net10.0` with `Microsoft.EntityFrameworkCore.Sqlite` 10.0.12,
  which was the newest 10.x on 2026-09-13. Ran on runtime 10.0.5.
- `Ef9` targets `net9.0` with `Microsoft.EntityFrameworkCore.Sqlite` 9.0.20, the
  newest 9.x on the same day. Ran on runtime 9.0.14.

Both projects print those numbers during the run rather than leaving you to trust
the csproj. The generated SQL is SQLite's; another provider quotes identifiers
differently and may order the conditions differently, so read the WHERE-clause
assertions as SQLite-specific and the row counts as not. I have not run this
against SQL Server, PostgreSQL or Cosmos.

The exception message asserted in `MixingTests` is not part of any API contract.
It is asserted here so that a change in a future release shows up as a failing
test rather than as a wrong sentence in a blog post.

MIT licensed. Argue with it.
