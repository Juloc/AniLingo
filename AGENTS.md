# Juloc Agent Bootstrap

Before doing substantial work in this repository:

1. Read `.agent/project.yaml` for repository-specific commands, protected paths and related repositories.
2. Read the canonical current operating rules from `Juloc/agent-control/AGENTS.md` on `main` using the available GitHub access.
3. Follow those rules, including resolving the active coordination ledger through `Juloc/agent-control` issue #1 before checking or creating claims.
4. Read any additional repository-specific instruction file named in `.agent/project.yaml` notes.

Do not ask the user to repeat the Agent Control protocol in their prompt.

## Mandatory AI implementation discipline

The central `Juloc/agent-control` engineering and C# conventions are binding for every agent. The following project rule makes the required application style explicit and does not weaken the central baseline:

- Apply current Microsoft/.NET naming and layout conventions to every new or intentionally touched C# member: PascalCase/camelCase naming, four spaces and Allman braces.
- Put a concise responsibility/constraint comment immediately above every non-generated method, constructor, local function and test method. Use XML documentation for public or reusable API contracts; use a short `//` comment for an internal implementation member. For a method with multiple non-obvious stages, add one short comment before each logical stage.
- Do not split coherent control flow into artificial mini-methods. Introduce a helper only when it owns real domain behavior, validation/policy, complexity, reuse or an architectural/framework boundary. Never add a forwarding wrapper that merely calls another method unchanged.
- Keep method calls, constructors/object creation, conditions, signatures and LINQ pipelines on one physical line whenever the resulting line is at most 230 characters and remains readable. Do not turn ordinary calls into vertically fragmented argument lists. Lines over 280 characters are not allowed; between 231 and 280, prefer the clearest structure and restructure before fragmenting ordinary control flow.
- Review the edited C# area against these rules before declaring work complete. Generated, vendored and tool-owned code is exempt where manual edits would be overwritten.
## Fallback if the central repository cannot be read

Continue safely without blocking repository recovery:
- inspect this repository's open Issues and PRs before editing,
- avoid work that overlaps an active branch/PR,
- use GitHub Issues as the backlog instead of creating manual status/backlog Markdown,
- keep changes scoped and run the repository's configured validation,
- never put credentials or secrets in code, docs, issues or comments,
- record that cross-agent coordination could not be verified.

The Agent Hub is optional. GitHub remains the durable source of truth.
