# Error / Permission / Availability States

Status: **Original J and Clean visual directions approved; binding planning specification.**

This shared surface covers consumer-facing access, availability and error states without creating a separate error implementation per feature.

## 1. Authentication redirect

A protected route opened without a valid authenticated Account session does **not** render a dedicated “Bitte einloggen” error page.

Instead:

1. redirect to the normal Login surface;
2. preserve a safe internal return target;
3. after successful Login/Profile resolution, return when the target is still permitted;
4. otherwise resolve to the appropriate state below.

A previously authenticated but expired session is different and uses the explicit Session Expired state.

## 2. Canonical states

The shared state family contains exactly:

- `NotFoundOrHidden`
- `Forbidden`
- `ModuleUnavailable`
- `ResourceMissing`
- `SessionExpired`
- `InternalError`

Do not make every feature invent its own 403/404/500 page.

## 3. Not found / hidden

Heading:
**Nicht gefunden**

Use when:
- route does not exist; or
- content/feature must be hidden from this Profile so its existence is not disclosed.

Copy:
`Diese Seite wurde nicht gefunden oder ist für dieses Profil nicht verfügbar.`

Actions:
- `Zur Startseite`
- `Zurück` when safe/history exists.

Original J illustration:
- dark-haired anime character looking confused;
- prominent `404` board/sign;
- visible question mark;
- scene reads immediately as “wrong/missing destination”.

Do not reveal role/capability names or hidden feature names.

## 4. Forbidden

Heading:
**Kein Zugriff**

Use when:
- the route/resource is intentionally visible/known;
- the authenticated Account/Profile lacks permission.

Copy:
`Du hast keine Berechtigung, diesen Bereich zu öffnen.`

Actions:
- `Zurück`
- `Zur Startseite`

Original J illustration:
- dark-haired character at a clearly blocked/locked entrance;
- lock/blocked symbol;
- stop/no gesture;
- no 404/question-mark metaphor.

Forbidden actions inside an otherwise permitted page are normally hidden. If permissions change while the page is open, use compact inline/dialog feedback unless the whole route becomes forbidden.

## 5. Module unavailable

Heading:
**Nicht verfügbar**

Use when:
- the feature/module exists;
- it is disabled or unavailable at instance level;
- this fact is appropriate to disclose.

Copy:
`Diese Funktion ist in dieser Instanz derzeit deaktiviert.`

Actions:
- `Zur Startseite`
- `Zurück`

Original J illustration:
- dark-haired character beside an **intact Jularr module/control panel**;
- obvious large OFF/power state;
- display/lamps dark;
- optional `Modul deaktiviert` label;
- character points at or tries the inactive control with mild disappointment/confusion.

Visual distinction:
- no lock -> not Forbidden;
- nothing broken -> not 500;
- no missing-item/404 metaphor -> not ResourceMissing/NotFound.

## 6. Resource missing

Heading:
**Inhalt nicht gefunden**

Use when:
- the route/surface is valid;
- the specific requested canonical item no longer exists/cannot resolve.

Examples:
- deleted Work;
- removed Collection;
- stale deep link to a specific item;
- deleted user-owned resource.

Copy:
`Der angeforderte Inhalt existiert nicht mehr oder wurde entfernt.`

Actions:
- `Zurück`
- `Zur Startseite`
- optional contextual parent such as `Zur Bibliothek` only when known and useful.

Original J illustration:
- dark-haired character looking at an **obviously empty expected container/place**;
- missing-object outline/empty box/pedestal;
- question mark allowed;
- instantly communicates “the thing is gone”, not “the whole page path is wrong”.

Do not expose database IDs or storage paths.

## 7. Session expired

Heading:
**Sitzung abgelaufen**

Use when a previously valid Account session expired or was revoked.

Copy:
`Deine Sitzung ist abgelaufen. Bitte melde dich erneut an.`

Primary:
- `Zum Login`

Optional secondary:
- `Zur Startseite` only when an unauthenticated Home/public surface is meaningful.

Original J illustration:
- dark-haired character with prominent hourglass/timer;
- tired/waiting expression;
- no lock/404/system-damage metaphor.

Preserve a validated safe return target where possible.

## 8. Internal error / 500

Heading:
**Interner Fehler**

Copy:
`Beim Laden ist ein interner Fehler aufgetreten. Bitte versuche es erneut.`

Actions:
- primary `Erneut versuchen`
- secondary `Zur Startseite`
- optional `Zurück` when safe.

Original J illustration:
- dark-haired character directly reacting to a **broken Jularr machine/terminal in the foreground**;
- cracked device/screen/chassis;
- loose cables, detached component, sparks and/or small smoke;
- large `500` on the broken device;
- character visibly frustrated/confused, possibly holding a broken part;
- background remains mostly intact.

The image must communicate “Jularr/system broke” immediately.

Do not use only:
- distant lightning/storm;
- destroyed landscape;
- closed door/shop;
- missing-object metaphor.

Retry must not blindly repeat a non-idempotent write that may already have succeeded. Operation-aware retry semantics are required.

## 9. Original J visual contract

Approved visual direction:

Canonical mascot reference:
`docs/assets/original-j/jularr-mascot-reference.png`

- Original Jularr black/red brand mark;
- warm cream base;
- Japanese ink/watercolor scenery;
- cherry blossoms;
- **the canonical Jularr mascot consistently used as the main character when a character is present**;
- state-specific emotion/action;
- red/pink default accent;
- accent/decorative elements use global theme tokens and remain compatible with hue shifting.

The mascot identity is fixed by the canonical reference: face, long black hair, red/pink eyes, floral hair ornaments and black/red/white kimono-inspired outfit family remain recognizably the same character. Pose, expression and state-specific props may vary.

The mascot identity should not itself be recolored by accent hue shifting.

Each state must be visually understandable before reading the explanatory copy.

The approved Original J mockup belongs in this folder.

## 10. Clean visual contract

Clean remains strictly separate from Original J.

Required direction:
- Clean Jularr mark;
- plain neutral Light/Dark background;
- purple default accent;
- modern minimal system/abstract illustrations or iconography;
- restrained geometry/gradient/elevation;
- no anime character;
- no sakura;
- no torii/pagoda/ink scenery;
- no Original-J decorative motifs.

Clean uses the same state semantics, copy and actions; its approved visual reference remains strictly separate from Original J.

Do not mix Original J art into Clean.

## 11. Hue shifting / themes

Both skins use shared semantic theme tokens.

Original J:
- red/pink accent/decorative theme elements may hue-shift;
- artwork must remain coherent when accent changes;
- character hair/skin and semantic illustration meaning must not be recolored blindly.

Clean:
- purple default accent can hue-shift through the same token system;
- remains minimal/neutral.

Semantic error/success/warning meaning is not arbitrarily recolored.

## 12. Desktop

Desktop uses:
- normal Jularr shell when surrounding app context remains valid;
- centered state content/card;
- illustration dominant enough to communicate the state;
- one heading;
- one concise explanation;
- one primary and at most one useful secondary action.

If the full app shell cannot safely load, use a minimal brand shell.

## 13. Mobile

Mobile uses:
- single-column state;
- portrait recomposition of the same state metaphor;
- short copy;
- touch-sized actions;
- safe-area awareness.

Do not squeeze a Desktop sidebar into a route that no longer has valid app-shell context.

## 14. Tablet / TV

Tablet adapts Desktop/Mobile by width.

TV:
- one clear image/icon;
- heading;
- one-line explanation;
- primary action;
- optional Back;
- remote-safe focus.

No dense troubleshooting text.

## 15. Dark mode

Original J and Clean both support Light/Dark/System.

Dark mode preserves state distinction and readability.

Original J may use darker ink/night treatment without making every error look catastrophic.

Clean remains neutral and never gains anime/sakura artwork in Dark mode.

## 16. HTTP / application semantics

Typical mapping:

- unauthenticated protected route -> redirect / API 401;
- hidden/not-found -> 404;
- visible but forbidden -> 403;
- module unavailable -> product-specific unavailable or 404 according to disclosure policy;
- missing canonical resource -> 404;
- expired session -> session-expired/authentication handling;
- unexpected internal failure -> 500.

UI does not parse raw exception strings.

The server/application layer returns normalized error contracts.

## 17. Security / privacy

- hidden resources do not leak existence;
- Forbidden does not expose raw capability/role/policy internals;
- InternalError never shows stack trace, SQL, filesystem paths, secrets or raw exception messages;
- return URLs are restricted to safe internal destinations;
- diagnostic correlation IDs may only appear in a future explicit diagnostic/details affordance, not default consumer UI.

## 18. Accessibility

- semantic page heading;
- visible keyboard/focus treatment;
- state not conveyed by color alone;
- artwork is decorative when heading/copy already conveys all required meaning;
- if artwork adds required meaning, provide concise alt text;
- screen readers do not receive duplicate text embedded inside illustration;
- actions have explicit accessible labels.

## 19. Shared component contract

Recommended primitives:

- `AppErrorState`
- `ErrorStateIllustration`
- `ErrorStateActions`

One shared implementation adapts visual skin and responsive layout.

## 20. Must not implement

- no standalone “Bitte einloggen” error page for a normal unauthenticated protected route;
- no permission dumps;
- no disabled forbidden navigation clutter;
- no lock metaphor for ModuleUnavailable;
- no broken-machine metaphor for ResourceMissing;
- no background-only disaster metaphor for 500;
- no Clean/Original visual mixing;
- no separate functional behavior per visual skin;
- no raw exception text;
- no unsafe blind retry.

Text specification wins over imagery on conflict.
