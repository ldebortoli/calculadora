# Calculadora 0.9.17: .NET 10

Validated on Windows x64 on 2026-10-23. Development uses SDK 10.0.402;
the self-contained package includes .NET and Windows Desktop Runtime 10.0.13.
No global runtime installation is required. C# remains at version 12 to keep
the compiler language semantics used by the .NET 8 release.

## Compatibility and verification

- The full .NET 8 baseline passed after isolating test storage: 29 core tests,
  Windows/WPF/market suites, 4968/4968 lines and 2546/2546 branches.
- The .NET 10 gate passed: the same suites, 4966/4966 lines and 2542/2542
  branches, with no new exclusions and the existing 100% thresholds unchanged.
- Removed two redundant UI sorts: the calculator already orders all options
  by debit and method, and filtering preserves that order. .NET 10 skips the
  key selector for a singleton, exposing the redundant selector in coverage.
- Existing fixtures cover legacy JSON migration, decimal fees and budgets,
  retirement dates/leap years, empty collections, reopen, failures, nine
  languages, chart interactions, splash cancellation and window sizing.
- Rendered WPF presentation fixtures were inspected. HTTP services use stub
  handlers; tests do not connect to accounts, quotes, payments or other services.
- `CASHFLOW_DATA_DIRECTORY` redirects scenario and language files for a process.
  The test entry point selects a unique temporary directory before any UI code.
  Default-path assertions inspect strings only. Without the variable, the
  established user-data location is unchanged. JSON schema remains version 9.
- Release assembly/file versions are checked by `tools/Test-Coverage.ps1`.
  The published runtime configuration records both included frameworks at 10.0.13.

Run `tools/Test-Coverage.ps1` and `tools/Publish-Windows.ps1` with the pinned SDK.
The coverage tool remains 18.10.0; its own runtime configuration permits major
roll-forward, so an isolated .NET 10 SDK can run it.

## Adoption and rollback

Keep the previous 0.9.16 executable and shortcut before pointing the shortcut at
the new versioned directory. Test only synthetic scenarios in a separate data
directory. No live user data is migrated or modified by this delivery.
To roll back, restore the saved shortcut/previous executable; for source rollback,
revert this migration with a normal commit. The previous source is `559e955`.
Never reset or force-push history. Back up live data before any future operation
that actually modifies it; these tests and package adoption do not do so.

The SDK ZIP SHA-512 was checked against Microsoft's release metadata and the
extracted host has a valid Microsoft Authenticode signature. Private local
receipts retain hashes, logs, baseline reports and binary/shortcut rollback copies.
Temporary build outputs and SDK files can be reviewed for later cleanup; retain
rollback copies and evidence until explicitly released.

## Sources and limits

- [Microsoft downloads](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- [Official release metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json)
- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
- [.NET 9 compatibility](https://learn.microsoft.com/en-us/dotnet/core/compatibility/9.0)
- [.NET 10 compatibility](https://learn.microsoft.com/en-us/dotnet/core/compatibility/10)

.NET 8.0.32 is the current .NET 8 patch on the validation date; its support ends
2026-11-10. Age alone does not establish a specific vulnerability. This migration
extends runtime support and passes the listed local checks; it does not certify
all application security or real financial integrations. Remote CI status must be
reported separately from local validation.
