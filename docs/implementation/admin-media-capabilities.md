# Admin Media Capabilities — implementation slice

Status: in progress

## Canonical sources

- Product/UX spec: `docs/mockups/admin-users-permissions/SPEC.md`
- Canonical policy store: `MediaCapabilityStore`
- Runtime resolver: `IMediaCapabilityService`
- Instance availability: `IInstanceModuleService`

## This slice

Polish the existing `/Admin/Capabilities` matrix without creating another permission model.

Required behavior:
- role defaults remain the base policy;
- per-user overrides remain sparse and win over role defaults;
- Owner remains unrestricted;
- disabled instance media modules are not offered in the editor;
- hiding a disabled media row must **not clear its stored policy** when another row is saved;
- the shared Book capability family remains configurable when either Books or Audiobooks is enabled;
- when both Books and Audiobooks are disabled, the Book capability family disappears;
- the matrix remains usable on narrow screens through one shared responsive wrapper.

## Important model constraint

Audiobooks currently use `WorkMediaType.Book` for authorization while having their own `InstanceModule.Audiobook` availability switch. Therefore the Book capability family is active when **Book OR Audiobook** is enabled. Individual Book/Audiobook surfaces still enforce their own instance module before capabilities.

## Out of scope

- custom groups/custom roles;
- new capability levels;
- request quotas;
- Learning/AI policy editors;
- per-device/session permissions.
