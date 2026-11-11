# Test Infrastructure

**Engine**: Unity 6.6 (`6000.6.8f1`, URP) + Photon Fusion 2 Shared Mode (`60Hz`)
**Test Framework**: Unity Test Framework (`com.unity.test-framework` `1.8.0` — NUnit EditMode & PlayMode)
**CI**: `.github/workflows/tests.yml`
**Setup date**: 2026-11-11

## Directory Layout

Unit and integration tests live under Unity's compiled test root (`Assets/Tests/`), because Unity compiles only `Assets/` and `Packages/`. `tests/` keeps this `README.md` and `smoke/`:

```text
Assets/
  Scripts/
    Game.asmdef                # Runtime game assembly (woven by Photon Fusion 2)
    Core/
      AssemblyInfo.cs          # InternalsVisibleTo EditModeTests & PlayModeTests
  Tests/
    EditMode/                  # Isolated NUnit unit tests (formulas, state machines, logic)
      EditModeTests.asmdef
      CharacterCombatConfigTests.cs
    PlayMode/                  # Runtime scene, pooling, and integration tests
      PlayModeTests.asmdef
      ArenaMatchSmokeTests.cs

tests/
  README.md                    # Test framework documentation
  smoke/
    critical-paths.md          # Critical path test list for /smoke-check gate

production/qa/
  evidence/                    # Retained screenshot logs and manual test sign-off records
```

> **Manual evidence lives under `production/qa/evidence/`, not `tests/`** — that
> is where `/story-done`, `/smoke-check`, `/test-evidence-review`, and `/qa-plan` read it.

> **Editor Scripts Note**: Any future `Editor/` folder created under `Assets/Scripts/` must be given its own `.asmdef` with `"includePlatforms": ["Editor"]` and `"references": ["Game"]` so `using UnityEditor;` never enters the runtime `Game` assembly.

## Running Tests

Use the Unity 6.6 Editor executable directly (`Unity` on `PATH` may refer to Unity's separate CLI rather than the Editor):

```powershell
# Delete stale XML results first so a failed compile is never misread as a previous pass
Remove-Item -Force test-results/editmode.xml, test-results/playmode.xml -ErrorAction SilentlyContinue

"C:\Program Files\Unity\Hub\Editor\6000.6.8f1\Editor\Unity.exe" -batchmode -runTests -projectPath . -testPlatform EditMode -testResults test-results/editmode.xml
"C:\Program Files\Unity\Hub\Editor\6000.6.8f1\Editor\Unity.exe" -batchmode -runTests -projectPath . -testPlatform PlayMode -testResults test-results/playmode.xml
```

### Exit Codes & Result Verification
- Exit **`0`**: Every test passed.
- Exit **`2`**: One or more tests failed.
- Exit **`1`**, **`3`**, **`4`**, or any other non-zero code: **FAIL** (`1` = compile error, writes no results file; `3` = failed run; `4` = unknown `-testPlatform`).
- Always delete `test-results/editmode.xml` and `test-results/playmode.xml` before each run.
- Inspect `<test-run>` in the XML output: `testcasecount="0"` means nothing was compiled or matched — treat as **NOT ASSESSED**, never as a pass.
- Do **not** append `-quit`; `-runTests` exits automatically upon completion.

## Test Naming

Names follow Unity C# conventions (`.claude/rules/test-standards.md`):
- **Unity**: class `[System]Tests` in `[System]Tests.cs`, method `[Scenario]_[Expected]`
  — e.g., `CharacterCombatConfigTests.cs` → `MobilityDerivedTicks_WithCanonical60HzConfig_ReturnsExactTicksAndDistances()`

## Story Type → Test Evidence

| Story Type | Required Evidence | Location |
|---|---|---|
| Logic | Automated EditMode unit test — must pass | `Assets/Tests/EditMode/` |
| Integration | Automated PlayMode test OR documented playtest | `Assets/Tests/PlayMode/` |
| Visual/Feel | Retained screenshot + lead sign-off | `production/qa/evidence/` |
| UI | Retained screenshot of each screen touched | `production/qa/evidence/` |
| Config/Data | Smoke check pass | `production/qa/smoke-*.md` |

## Local Test Gate & CI Policy

GitHub Actions auto-triggers (`push` / `pull_request`) in `.github/workflows/tests.yml` are intentionally disabled (`on: workflow_dispatch` only) to consume **0 GitHub Actions quota**; no `UNITY_LICENSE` secret setup is required. All EditMode, PlayMode, and `/smoke-check` verification runs **100% locally** via the Unity 6.6 Editor Test Runner, Unity MCP `run_tests`, or the local `-batchmode -runTests` commands above.
