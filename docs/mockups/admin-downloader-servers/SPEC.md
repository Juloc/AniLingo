# Admin Downloader Servers — V1

Status: approved planning direction; current Server mockup is the visual baseline.

Shared contract: `docs/mockups/admin-downloader/SPEC.md`.

## Purpose

Configure, test and monitor NNTP/Usenet servers used by the native downloader.

## Server list

Columns:
- Name
- Host
- Port
- TLS
- active/max connections
- Priority
- Role
- Current speed
- Health
- Actions

Roles:
- Primary
- Backup/Fill
- Optional

Priority/failover semantics must be explicit.

## Server editor

Fields:
- Name
- Enabled
- Host
- Port
- TLS/SSL
- certificate verification policy
- Username
- Password
- Connections
- Priority
- Role
- Timeout
- Retry/backoff
- optional retention information
- optional provider quota
- quota period/reset
- optional expiry date
- IPv4/IPv6 preference where supported
- optional pipelining/articles-per-request tuning under Advanced

Secrets remain masked/write-only.

## Tests

Separate actions:
- Test connection/authentication
- Test TLS/certificate
- Test speed

Tests must not silently alter saved settings.

## Live statistics

Per server:
- health
- active/max connections
- current transfer
- transferred today
- transferred current quota period
- transferred total where retained
- average/median latency where meaningful
- connection/auth errors
- missing article rate
- retry/failover count
- last successful connection

## Quota behavior

If a server has a quota:
- show remaining amount
- reset date/period
- warning thresholds
- exhausted state

Quota exhaustion can move server to standby/unavailable according to policy.

## Priority / failover

UI must make clear:
- lower/higher numeric priority semantics
- when backup/fill server is used
- whether optional servers are skipped unless needed

Do not hide server-selection behavior behind unexplained numbers.

## Advanced

Advanced section may contain:
- socket timeout
- retry delay
- IPv4/IPv6 behavior
- certificate/SNI options
- pipelining/request batching
- per-server speed cap if supported

Keep Advanced collapsed by default.

## Actions

- add
- edit
- enable/disable
- test
- reorder/priority
- duplicate configuration
- remove

Removal warns if it leaves no usable server.

## States

Additional:
- authentication failed
- TLS failed
- quota exhausted
- server reachable but slow
- high missing-article rate
- standby
- disabled
- no usable server

## Must not implement

- No provider-specific hard-coded UI model.
- No cleartext password display.
- No server health based only on last ping.
- No hidden failover behavior.
