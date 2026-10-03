# Maintainability and AI Coding Discipline

Jularr uses the canonical Juloc maintainability rules defined in [Juloc/agent-control `docs/MAINTAINABILITY_CONVENTIONS.md`](https://github.com/Juloc/agent-control/blob/main/docs/MAINTAINABILITY_CONVENTIONS.md).

They are mandatory for every implementation task and for all new or intentionally touched code. They are not optional recommendations.

Jularr-specific enforcement summary:

- Before creating a new service, store, helper, interface, provider, manager, handler, configuration path or dependency, inspect the existing Jularr owner/pattern first.
- Keep the modular-monolith ownership model. Do not create parallel feature/data/configuration paths or microservices for responsibilities that already belong in Jularr.
- UI/page code does not become a second owner of backend business rules or persistence behavior.
- Keep coherent control flow readable top-to-bottom. Do not manufacture tiny helpers, wrappers or forwarding methods to make metrics look cleaner.
- Names and effects must match. Reads/validation do not hide unrelated mutations.
- One business rule has one canonical backend/domain owner.
- Do not swallow errors, use broad catch-all fallbacks, or silently continue with defaults/legacy paths.
- Propagate cancellation and use real timeouts for I/O-bound work.
- Avoid N+1 queries, whole-table application filtering, unnecessary mappings/allocations and speculative abstraction in hot paths.
- New dependencies/technologies require a concrete present need.
- Comments explain why, constraints and non-obvious intent; they do not narrate obvious code.
- Behavior and regression tests matter more than tests of implementation wiring.
- Do not accumulate Legacy/Old/V2/fallback runtime branches. Migrate and remove obsolete paths within the supported upgrade window.
- When a structural rule is objectively and safely machine-checkable, add or extend a guard/test so CI can reject regressions.
- The canonical maintainability completion gate must be run before any AI agent reports work complete.

A change that introduces a new violation in intentionally touched code is not complete.
