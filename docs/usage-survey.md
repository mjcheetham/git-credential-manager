# Usage survey

Git Credential Manager has an **optional, opt-in, default-off** usage survey system
that captures a tiny, fixed set of usage data about how GCM is used. The data
is intended to be aggregated and **published publicly** (in the spirit of the
Steam Hardware Survey) so the project and its users can see which providers,
platforms, and versions are in real use.

This page is the authoritative reference for what GCM's usage survey does. If
anything in the code disagrees with this page, it is a bug.

## TL;DR

- **Off by default.** GCM ships with usage survey disabled. Nothing is collected,
  buffered, or sent unless you explicitly opt in.
- **Opt in once:** `git credential-manager usage-survey on`.
- **Opt out any time:** `git credential-manager usage-survey off`.
- **See exactly what is being sent in real time:**
  `git credential-manager usage-survey show`.
- **Anonymous.** Events are linked to a single random GUID generated on this
  install (`~/.gcm/usage-survey/install-id`). There is no hashing of machine
  attributes, no IP, no username, no hostname, no remote URL.

## What is collected

When usage survey is enabled, GCM emits **one event per `get` credential
invocation** (i.e. once per `git credential fill` lookup against a host GCM
recognises). Each event contains exactly the following fields and nothing
else:

field|example|notes
-|-|-
`event`|`"get"`|event type
`event_version`|`1`|integer schema version of this event type; each event type owns its own version
`ts`|`"2026-06-09T15:58:50Z"`|UTC, second precision
`install_id`|`"3f2dceae-…-b9c1"`|random per-install GUID
`gcm_version`|`"2.6.1"`|GCM version string
`os`|`"macos"` / `"windows"` / `"linux"`|OS family only
`os_version`|`"14.5.1"` / `"10.0.22631"` / `"Ubuntu 24.04.1 LTS"`|see "OS version semantics" below
`arch`|`"x64"` / `"arm64"` / `"x86"`|CPU architecture
`provider`|`"github"` / `"bitbucket"` / `"azure-repos"` / `"gitlab"` / `"generic"`|which host provider answered
`auth_method`|`"oauth"` / `"pat"` / `"managed-identity"` / `"browser"` / `"basic"` / `"wia"`|host-provider-specific authentication mechanism; omitted when not reported (e.g. cache hit)
`from_cache`|`true` / `false`|true = credential was returned from the OS credential store; false = a fresh interactive auth ran

That is the complete, closed allow-list. Adding, removing, or changing the
semantics of any field for an event type is a breaking change for that type
and bumps its `event_version`. Each event type has its own version so types
can evolve independently — e.g. a future `diagnose` event could be at v1
while `get` is already at v2.

### OS version semantics

`os_version` carries the full string returned by GCM's existing platform
detection so we can distinguish meaningful sub-versions:

- **Windows**: `Major.Minor.Build` (e.g. `10.0.22631`) — the build number is
  what distinguishes Windows 10 from Windows 11 (both report `10.0`).
- **macOS**: `Major.Minor.Patch` (e.g. `14.5.1`).
- **Linux**: the distribution `PRETTY_NAME` read from `/etc/os-release`
  (e.g. `"Ubuntu 24.04.1 LTS"`, `"Fedora Linux 40 (Workstation Edition)"`).
  On non-systemd or minimal distributions this falls back to `uname -a`
  output or `"Unknown-Linux"`.

### auth_method values

The `auth_method` field is set by each host provider when it freshly
generates a credential, identifying the sub-mechanism used. Current values:

provider|values
-|-
`generic`|`oauth`, `basic`, `wia`
`github`|`browser`, `device`, `pat`, `basic`
`gitlab`|`browser`, `pat`, `basic`, `oauth-refresh`
`bitbucket`|`oauth`, `oauth-refresh`, `basic`
`azure-repos`|`oauth`, `pat`, `managed-identity`, `service-principal`, `wif`

The field is omitted entirely when `from_cache` is true (we don't know how
the cached credential was originally obtained) or when a provider does not
report a value.

## What is never collected

GCM usage survey explicitly does NOT capture, even indirectly:

- Hostnames, remote URLs, repo paths, organisation names, tenant IDs.
- Usernames, account identifiers, email addresses.
- Credential material of any kind (tokens, passwords, refresh tokens).
- IP addresses, geolocation, time zone, locale.
- Error messages, stack traces, file paths, command-line arguments.
- Per-repo or per-remote anything.

The `auth_method` field is the only host-provider-specific value sent and
is restricted to a small set of stable, non-identifying labels (see
"auth_method values" above).

## Where usage survey data lives on disk

Usage survey uses two roles within a single GCM executable:

1. A **producer** (the normal GCM process invoked by Git for credential
   lookups) appends a JSON line to a per-process file under
   `~/.gcm/usage-survey/events/`, then spawns a detached dispatcher if one isn't
   already running. The hot path adds well under a millisecond and never
   blocks Git.
2. A **dispatcher** (a separate, detached GCM process started by the producer)
   reads finalised queue files, ships them via the configured uploader, and
   deletes each file on success. It holds a pidfile so at most one dispatcher
   runs at a time. After ~15 minutes of inactivity it releases the pidfile
   and exits; the next event will start a fresh one.

path|purpose
-|-
`~/.gcm/usage-survey/install-id`|random GUID identifying this install
`~/.gcm/usage-survey/events/<ts>-<pid>-<seq>.jsonl`|shipped-to-dispatcher queue file (one event each)
`~/.gcm/usage-survey/events/<…>.jsonl.partial`|a queue file currently being written by a live producer (ignored by dispatcher)
`~/.gcm/usage-survey/sent/<ts>-<pid>-<seq>.jsonl`|archive of successfully-shipped events, retained for 24h then auto-purged
`~/.gcm/usage-survey/dispatcher.pid`|single-owner lock for the running dispatcher
`~/.gcm/usage-survey/dispatcher.log`|local log written by the v1 stub uploader

The contents are plain JSON Lines text. You can read them with any tool — for
example:

```shell
cat ~/.gcm/usage-survey/events/*.jsonl
cat ~/.gcm/usage-survey/sent/*.jsonl
tail -f ~/.gcm/usage-survey/dispatcher.log
```

The `sent/` archive retains the last 24 hours of shipped events so you can
inspect what was actually sent, even after the queue file has been removed.
The dispatcher auto-purges files in `sent/` older than 24 hours on each pass;
filenames embed the event timestamp so retention does not depend on
filesystem mtimes.

## Inspecting usage survey as it ships

The most direct way to see exactly what GCM is sending is:

```shell
git credential-manager usage-survey show
```

This runs the dispatcher in the foreground, holds the dispatcher pidfile while
running (so no parallel background dispatcher will start), and prints every
event being shipped to stdout. Press Ctrl-C to exit; the next time
GCM is invoked, a background dispatcher will resume draining the queue.

## Commands

command|what it does
-|-
`git credential-manager usage-survey on`|Opt in. Persists `credential.usageSurvey=true` in your global git config and ensures an Install ID exists.
`git credential-manager usage-survey off`|Opt out. Persists `credential.usageSurvey=false`. Queued events on disk are **not** deleted automatically.
`git credential-manager usage-survey status`|Show current state: enabled/disabled, Install ID, on-disk queue depth, dispatcher pid, count of events shipped in the past 24h, dispatcher log path.
`git credential-manager usage-survey show`|Foreground dispatcher — see "Inspecting usage survey as it ships" above.
`git credential-manager usage-survey id`|Print the persistent Install ID, or `(not generated)` if none exists. Does **not** create an Install ID.
`git credential-manager usage-survey id --reset`|Generate a brand new Install ID, breaking the link to previous events. Creates one if none existed.
`git credential-manager usage-survey purge`|Delete all queued and archived events on disk (both `events/` and `sent/`). Does not change opt-in state and does not reset the Install ID.

## Configuration

The on/off state is just a normal git config value plus an environment
variable override:

```shell
# Enable
git config --global credential.usageSurvey true

# Disable (overrides any system-level enable)
git config --global credential.usageSurvey false

# Process-level override (highest precedence)
GCM_USAGE_SURVEY=0 git push
```

See [`credential.usageSurvey`](configuration.md#credentialusagesurvey) and
[`GCM_USAGE_SURVEY`](environment.md#gcm_usage_survey) for the formal entries.

### Enterprise / system policy

System administrators can pin usage-survey off (or on) by writing the setting at
the system git config level. Per-user values override the system value, and
the per-process `GCM_USAGE_SURVEY` env var overrides both — there is no force
mode.

```shell
sudo git config --system credential.usageSurvey false
```

## Resetting your identity

If you want to start a fresh anonymous identity (for example, before sharing
a machine):

```shell
git credential-manager usage-survey id --reset
git credential-manager usage-survey purge   # optional: also drop queued/archived events
```

## Where the data goes

Currently the uploader is a local-file stub: shipped events are appended to
`~/.gcm/usage-survey/dispatcher.log` and nothing is sent off the machine. A
public ingestion endpoint and aggregated dashboard will be linked from this
page when available.

Until then, `usage-survey on` does materialise events on disk so you can audit
the schema and the data being captured, but no network upload occurs.
