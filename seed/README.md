# Seed files

Database seed data lives in [`database/seed/`](../database/seed/). This folder holds **file-based** fixtures.

## `custodian-files/` (scenario S12)

Daily position files as the on-prem feed agent sends them to `CustodianFeedService.SubmitPositionFile`. They cover firm 2 (Groupe Financier Laurentien) accounts `LFG000002001` to `LFG000002012`, as of 2026-09-30.

| File | Number format | Notes |
|---|---|---|
| `NBIN_20260930_POS.txt` | `1052590.68` | Invariant decimal point |
| `NBIN_20260930_POS_FR.txt` | `1 052 590,68` | fr-CA: comma decimal separator, **NBSP (0xA0)** group separator |

Both files:

- **Encoding:** Windows-1252 (the agent runs on French Windows), so accented names such as `Côté` and `Hélène` are single bytes (`0xF4`, `0xE9`, ...).
- **Line endings:** CRLF, with no trailing newline.
- **Header:** the first line is a header and is skipped.
- **Layout:** fixed width, per the 2014 custodian spec.

| Columns | Field |
|---|---|
| 0–11 | Account number |
| 12–39 | Account name |
| 40–57 | Market value (right-aligned) |
| 58–67 | As-of date (`yyyy-MM-dd`) |

The files are byte-exact fixtures. `.gitattributes` marks them `binary` so git never normalizes line endings or encoding. Don't open and re-save them in an editor.
