# Claude Code Game Studios — Antigravity 2.0 Edition

Indie & Hybrid-Casual game development managed through 49 coordinated AI subagents and 74 specialized skills, native-adapted for **Google Antigravity 2.0**.

## Technology Stack

- **Engine**: Unity 6 (Universal Render Pipeline - URP) *(or Godot 4 / Unreal Engine 5 via `/setup-engine`)*
- **Language**: C# *(or GDScript / C++ / Blueprint)*
- **Version Control**: Git with trunk-based development

## Project Structure

@[Directory Structure](.claude/docs/directory-structure.md)

## Engine Version Reference

@[Engine Version Reference](docs/engine-reference/unity/VERSION.md)

## Technical Preferences

`project.yaml` at the repo root is the primary config store — engine, specialists, naming, platform, performance, modes. Skills resolve it via `resolve_config` (see `.claude/docs/config-resolution.md`).

## Coordination Rules

@[Coordination Rules](.claude/docs/coordination-rules.md)

## Antigravity 2.0 Runtime & Tool Mapping

When executing any skill in `.agents/skills/` (or `.claude/skills/`) and delegating to agents in `.claude/agents/`, apply this exact tool mapping for Antigravity 2.0:

1. **Subagent Delegation (`Agent` tool in skills $\rightarrow$ `define_subagent` + `invoke_subagent`):**
   - To spawn a studio specialist (e.g., `subagent_type: unity-specialist`, `game-designer`, `gameplay-programmer`, `qa-tester`):
     1. Read its definition file at `.claude/agents/<agent-name>.md` using `read_file`.
     2. Register it (once per conversation) via `define_subagent` with `Name: "<agent-name>"`, `SystemPrompt` set to the markdown body of that file, `EnableWriteTools: true`, and `EnableMCPTools: true`.
     3. Launch it via `invoke_subagent` with `TypeName: "<agent-name>"`, `Model: "inherit"`, and the distilled brief + return contract in `Prompt`.
2. **Interactive Decisions (`AskUserQuestion` $\rightarrow$ `ask_question`):**
   - Whenever a skill specifies `AskUserQuestion`, call the native Antigravity `ask_question` tool to present structured selectable options to the user.
3. **Config Resolution & Helper Scripts (`` !`bash ...` `` $\rightarrow$ `run_command`):**
   - Inline shell directives like `` !`bash "${CLAUDE_SKILL_DIR}/../../hooks/yaml-helper.sh" resolve_config ...` `` must be executed via `run_command` as:
     `bash .claude/hooks/yaml-helper.sh resolve_config ...`
4. **File & Shell Operations:**
   - `Read` / `Glob` / `Grep` $\rightarrow$ `read_file` / `run_command`
   - `Write` / `Edit` $\rightarrow$ `write_file` / `edit_file`
   - `Bash` / `PowerShell` $\rightarrow$ `run_command`
5. **Unity MCP Integration:**
   - When working on a Unity project with the Unity MCP server active, prefer inspecting scenes, GameObjects, prefabs, console logs, and running tests directly through the `unity` MCP tools alongside `docs/engine-reference/unity/`.

## Collaboration Protocol

**User-driven collaboration, not autonomous execution.**
Every task follows: **Question -> Options -> Decision -> Draft -> Approval**

- Agents MUST ask "May I write this to [filepath]?" before using `write_file`/`edit_file` (exempt: `active.md` appends, agent memory, or a subagent's new file at the path its orchestrating skill named; see `.claude/rules/agent-memory.md`).
- Agents MUST show drafts or summaries before requesting approval.
- Multi-file changes require explicit approval for the full changeset.
- No commits without user instruction.

## Coding Standards

@[Coding Standards](.claude/docs/coding-standards.md)

## Context Management

- **`production/session-state/active.md` is the session checkpoint.** The file is the memory, not the conversation. Read it first after any compaction, crash, or new session.
- **Helpers in `.claude/scripts/` emit observations, never verdicts.**
