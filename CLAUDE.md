# Working rules for this repository

## Versioning (requested by the product owner — always apply, no need to be reminded)

Every change delivered to this repository bumps the product version:

1. Update `<Version>` in `Directory.Build.props` (semantic versioning):
   - new feature or module → minor (`1.8.0` → `1.9.0`)
   - fix, small UI/UX improvement, docs-only release → patch (`1.8.0` → `1.8.1`)
   - breaking data/contract change → major
2. Add an entry at the **top** of `CHANGELOG.md` in the existing format:
   `## <version> — <Jalali date, e.g. 1405/07/12> — <short Persian title>` followed by `- ` bullet lines (Persian).
   The top entry must equal `<Version>`; `tests/Crm.WebTests/VersionWebChecks.cs` fails otherwise.
3. The version is shown in the UI (sidebar brand, top-bar badge linking to `/system/version`, login page);
   it comes from the assembly, so no view needs editing when bumping.
4. At the end of every task, tell the user (in Persian) the new version number and what changed in it.

## Conversation

- Reply to the user in Persian.
- Do not create pull requests unless asked; push to the designated branch.
