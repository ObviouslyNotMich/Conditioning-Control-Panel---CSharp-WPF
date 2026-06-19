# 06 — Networking, Auth, Backend Sync, Content & Security Storage

Capability specs for the CCP networking/auth/backend cluster. Classified per
`openspec/PORTABILITY_RUBRIC.md`. Generated 2026-06-15.

**Cluster summary up front:** The transport layer is uniformly portable — every backend
call is `HttpClient` + Newtonsoft JSON against the Vercel proxy
`https://codebambi-proxy.vercel.app` (and `https://app.cclabs.app` for the Deeper
catalogue). OAuth uses a cross-platform `HttpListener` loopback on localhost, which is
portable in .NET 8. All cryptography in scope (AES-256-CBC, PBKDF2/`Rfc2898DeriveBytes`,
HMAC-SHA256, `RandomNumberGenerator`) is portable BCL. The genuine Windows blockers are
narrow and recurring:

1. **DPAPI** (`System.Security.Cryptography.ProtectedData`) in the four secure token/key
   stores — the only secrets-at-rest mechanism. **This is the #1 seam.**
2. **WebView2** (`Microsoft.Web.WebView2`) — embedded browser + HT auto-discovery.
3. **WPF imaging** (`BitmapImage` / `ImageSource` / `pack://` URIs) for thumbnails/previews.
4. **`DispatcherTimer` / `Application.Current.Dispatcher`** for poll/heartbeat timers and
   UI marshalling — appears in nearly every long-lived network service.
5. **WPF dialogs** woven into the account login orchestration (`AccountService`).

---

## Capability: Secure Secret Storage (DPAPI token & key stores)
**Files:** Services/SecureTokenStorage.cs, Services/DiscordTokenStorage.cs, Services/SecureAuthTokenStore.cs, Services/SecureApiKeyStore.cs
**Class:** OS-SPECIFIC
**Blocking deps:** DPAPI — `System.Security.Cryptography.ProtectedData.Protect/Unprotect` with `DataProtectionScope.CurrentUser`, in all four stores.
**Seam:** Introduce an `ISecretStore` abstraction (Store/Retrieve/Clear) and back it with DPAPI on Windows, libsecret/Keychain or an encrypted keyfile elsewhere. Everything around the DPAPI call (JSON serialization, per-store entropy strings, secure-overwrite-then-delete on clear, in-memory cache) is already portable.

These stores hold: Patreon OAuth tokens + cached subscription state (`patreon_auth.dat`/`patreon_cache.dat`), Discord OAuth tokens + cached user state (`discord_auth.dat`/`discord_cache.dat`), the V2 API auth token (`auth_token.dat`), and the OpenRouter API key (`api_key.dat`) — all under `%LOCALAPPDATA%/ConditioningControlPanel/`. Each store uses a distinct hardcoded entropy string (`..._Patreon_v1`, `..._Discord_v1`, `..._AuthToken_v1`, `..._ApiKey_v1`).

### Requirement: Encrypt secrets at rest bound to the current OS user.
The system SHALL persist OAuth tokens, the API auth token, and the OpenRouter API key encrypted such that they are unreadable by other OS users and unusable when copied to another machine/user profile.

#### Scenario: Decrypt fails for a different user
- WHEN an encrypted secret file is read but decryption throws `CryptographicException` (different user or corruption)
- THEN the store SHALL log a warning, clear the file, and return null (forcing re-auth) rather than crash.

#### Scenario: Secure logout
- WHEN tokens are cleared (logout)
- THEN the on-disk file SHALL be overwritten with cryptographic random bytes before deletion, and any in-memory plaintext SHALL be zeroed via `SecurityHelper.SecureClear`.

---

## Capability: Patreon OAuth & Subscription Validation
**Files:** Services/PatreonService.cs, Models/PatreonModels.cs
**Class:** MIXED
**Blocking deps:** None in the flow itself — `HttpListener` loopback (`http://localhost:47832/callback/`) is portable in .NET 8; `Process.Start(UseShellExecute=true)` to open the browser is portable (needs `xdg-open`/`open` mapping off-Windows). The Windows tie is the consumed DPAPI `SecureTokenStorage`.
**Seam:** Swap `SecureTokenStorage` for `ISecretStore`; add a browser-launch shim per OS.

OAuth via the proxy: `/patreon/authorize`, `/patreon/token`, `/patreon/refresh`, `/patreon/validate`, plus display-name endpoints. CSRF state is a hex-encoded 16-byte `RandomNumberGenerator` value compared with `SecurityHelper.SecureCompare`. Subscription tier / whitelist cached 24h. `Models/PatreonModels.cs` is pure POCO (PORTABLE).

### Requirement: Authenticate via Patreon OAuth and resolve subscription tier.
The system SHALL complete an OAuth authorization-code flow through a localhost loopback redirect, exchange the code for tokens, persist them encrypted, and resolve the user's tier and whitelist status.

#### Scenario: CSRF state mismatch
- WHEN the OAuth callback returns a `state` value that does not match the generated one
- THEN the flow SHALL abort with a security exception and no tokens are stored.

#### Scenario: OAuth timeout
- WHEN no callback arrives within 5 minutes
- THEN the listener SHALL be stopped and a timeout surfaced to the caller.

---

## Capability: Discord OAuth, Validation & Community Webhooks
**Files:** Services/DiscordService.cs, Models/DiscordModels.cs
**Class:** MIXED
**Blocking deps:** None in the flow — `HttpListener` loopback on `http://localhost:47833/callback/` (distinct port from Patreon), `Process.Start(UseShellExecute=true)`. Windows tie is the consumed DPAPI `DiscordTokenStorage`.
**Seam:** `ISecretStore` + browser-launch shim (same as Patreon).

Near-identical twin of PatreonService: `/discord/authorize`, `/discord/token`, `/discord/refresh`, `/discord/validate`, `/discord/community-webhook`, `/user/*-discord`. Posts achievement/level-up announcements to a community webhook. `Models/DiscordModels.cs` is pure POCO (PORTABLE).

### Requirement: Authenticate via Discord OAuth and broadcast community events.
The system SHALL authenticate the Discord identity, persist tokens encrypted, and optionally post level-up/achievement announcements to the community webhook.

#### Scenario: Webhook post when not linked
- WHEN a community announcement is requested but no valid Discord token exists
- THEN the post SHALL be skipped without error.

---

## Capability: V2 Unified Auth API Client
**Files:** Services/V2AuthService.cs, Services/V2DeviceCodeService.cs
**Class:** PORTABLE
**Blocking deps:** None. Pure `HttpClient` + Newtonsoft against the proxy; `X-Auth-Token` header read from settings. (Note: the auth token it consumes originates from the DPAPI store, but this service has no direct Windows dependency.)

V2AuthService: `/v2/auth/{discord,patreon,register,login,link}`, `/v2/user/{profile,update,heartbeat,delete-account}`. Handles monthly-season system, OG recognition, legacy migration, and "take higher" XP merge. Redacts `auth_token`/`password` from debug logs. V2DeviceCodeService: SP3 device-code login (`/v2/auth/device/initiate` + `/poll`) — desktop shows a code, polls until the web confirms (HTTP 200/202/410/429/503 state machine).

### Requirement: Authenticate against the V2 API across multiple flows.
The system SHALL support Discord/Patreon OAuth-token exchange, invite-code registration, password login, provider linking, and device-code pairing, returning a unified account identity and auth token.

#### Scenario: Local progress is higher than server
- WHEN applying server user data and the local total XP exceeds the server total XP
- THEN local level/XP SHALL be kept (no progress loss) and a note logged.

#### Scenario: Device-code expired
- WHEN polling a device code returns HTTP 410
- THEN the poll SHALL report `Expired` and the client stops polling.

---

## Capability: Unified Account Orchestration
**Files:** Services/AccountService.cs
**Class:** MIXED
**Blocking deps:** WPF — `using System.Windows;`, `Window owner` parameters, `MessageBox.Show`, and `ShowDialog()` on WPF dialogs (`UsernamePickerDialog`, `DisplayNameDialog`). The REST orchestration is portable; the UI prompts are woven into the control flow.
**Seam:** Extract an `IAccountInteraction` (prompt-for-name, show-conflict, confirm) interface so the lookup→register/link/claim→sync sequence stops referencing `System.Windows`.

Static orchestrator over both V1 (`/auth/lookup`, `/auth/register`, `/auth/link-provider`, `/auth/claim`) and V2 endpoints; called post-OAuth to detect existing accounts and prompt registration.

### Requirement: Drive the post-auth unified-account flow.
The system SHALL, after a provider OAuth completes, look up an existing unified account, and either link the provider, register a new account, or claim a legacy account, then trigger profile sync.

#### Scenario: User cancels registration prompt
- WHEN the registration/display-name prompt is dismissed
- THEN the flow SHALL return false and leave no partial account state.

---

## Capability: Cloud Profile Sync & Heartbeat
**Files:** Services/ProfileSyncService.cs
**Class:** MIXED
**Blocking deps:** `System.Windows.Threading.DispatcherTimer` (120s heartbeat) and `Application.Current.Dispatcher.BeginInvoke` casting to `MainWindow` to call `TryPresentSeasonRecap()`. REST + HMAC-SHA256 request signing (`SignRequest`) are portable.
**Seam:** Replace `DispatcherTimer` with `PeriodicTimer`/`System.Threading.Timer`; abstract the season-recap UI callback behind an event/interface.

~2960 LOC. Syncs XP/level/achievements/skills/quests/companion with take-higher merge, anti-cheat clamps, season-reset healing. Endpoints `/v2/user/sync`, `/v2/user/heartbeat`, plus V1 `/user/{profile,sync,heartbeat}{,-discord}`. Tolerates either Patreon or Discord auth.

### Requirement: Reconcile local and cloud progression without loss or cheating.
The system SHALL periodically sync progression to the cloud, signing requests with HMAC-SHA256, applying take-higher merge semantics, and clamping implausible values.

#### Scenario: Heartbeat while offline
- WHEN the heartbeat timer fires but the network request fails
- THEN the failure SHALL be swallowed (no crash) and retried on the next interval.

---

## Capability: Leaderboard & User Lookup
**Files:** Services/LeaderboardService.cs
**Class:** MIXED
**Blocking deps:** `System.Windows.Threading.DispatcherTimer` (30-min auto-refresh). Otherwise pure `HttpClient` + JSON + percentile math.
**Seam:** Swap `DispatcherTimer` for a portable timer — then PORTABLE.

Fetches/caches the leaderboard (`/v3/leaderboard`) and per-user lookups (`/user/lookup`).

### Requirement: Provide cached ranked leaderboard data.
The system SHALL fetch and cache leaderboard standings and compute the local user's percentile rank.

#### Scenario: Stale cache served on fetch failure
- WHEN a refresh request fails
- THEN the last cached leaderboard SHALL remain available.

---

## Capability: Deeper Catalogue Submission & Lookup
**Files:** Services/CatalogueService.cs, Services/CatalogueLookupService.cs
**Class:** CatalogueService = PORTABLE; CatalogueLookupService = MIXED
**Blocking deps:** CatalogueService — none (HttpClient + JObject + SemaphoreSlim; in-memory Supabase Bearer cache, never persisted). CatalogueLookupService — `Application.Current.Dispatcher` to marshal the injected "open in player" callback.
**Seam (lookup):** The opener is already a `Func<string,bool>`; route the dispatcher hop through it.

CatalogueService: `POST /api/auth/token-exchange` (CCP token → Supabase Bearer), `POST /api/enhancements`, `GET /api/enhancements/mine` on `app.cclabs.app`. CatalogueLookupService: `GET /api/enhancements/by-ht-url`, downloads `.ccpenh.json` bundles locally (with Windows reserved-name filename sanitization — harmless cross-platform).

### Requirement: Submit and discover community enhancements.
The system SHALL exchange the local auth token for a backend bearer, submit enhancement metadata to the catalogue, and look up published enhancements by HypnoTube URL.

#### Scenario: Token exchange cached
- WHEN multiple submissions occur within the bearer lifetime
- THEN the bearer SHALL be reused from memory rather than re-exchanged.

---

## Capability: Content Pack Download / Decrypt / Install
**Files:** Services/ContentPackService.cs, Services/PackEncryptionService.cs, Models/ContentPack.cs
**Class:** MIXED
**Blocking deps:** ContentPackService — `System.Windows.Media.Imaging.BitmapImage` for thumbnails/previews (`GetPackFileThumbnail`, `GetPackPreviewImages`); `Models/ContentPack.cs` also exposes `List<BitmapImage> PreviewImages`. `FileAttributes.Hidden` is a no-op off-Windows. Download/extract/encrypt/manifest are portable.
**Seam:** Extract an `IThumbnailDecoder` (returns a stream/abstract image); decrypt-to-stream paths are already platform-neutral.

PackEncryptionService (PORTABLE crypto): AES-256-CBC + PBKDF2 (`Rfc2898DeriveBytes`, 100k iters, SHA256). Key is **machine-bound** by deriving from `Environment.MachineName + Environment.UserName` — portable API, but the binding means packs are tied to a machine/user (not a Windows-only dependency). ContentPackService: resumable HTTP Range download (10 retries), `ZipFile` extract, AES encrypt-at-rest, decrypt-to-memory on use. Endpoints: `/packs/manifest`, `/pack/download-url`, `/pack/status`; CDN `ccp-packs.b-cdn.net`.

### Requirement: Download, encrypt-at-rest, and serve content packs.
The system SHALL download packs resumably, store their files AES-encrypted on disk, and decrypt them only to memory at use time (never plaintext to disk).

#### Scenario: Resume interrupted download
- WHEN a pack download is interrupted and retried
- THEN it SHALL resume from the last byte offset via HTTP Range rather than restart.

#### Scenario: Pack copied to another machine
- WHEN encrypted pack files are copied to a different machine/user
- THEN decryption SHALL fail because the AES key derives from machine + user identity.

---

## Capability: Asset Import
**Files:** Services/AssetImportService.cs
**Class:** PORTABLE
**Blocking deps:** None. Pure `System.IO` + `System.IO.Compression`.

Imports images/videos from files/folders/ZIPs into the assets tree (extension routing, pack-name subfoldering, collision-safe copy).

### Requirement: Import user media into the assets tree.
The system SHALL route imported files by extension into image/video subfolders, unpacking ZIP archives and avoiding name collisions.

#### Scenario: Duplicate filename on import
- WHEN an imported file's name already exists at the destination
- THEN a non-colliding name SHALL be chosen rather than overwriting.

---

## Capability: Mod System (install / validate / resolve)
**Files:** Services/ModService.cs, Services/ModResourceResolver.cs, Models/ModManifest.cs
**Class:** ModService = PORTABLE; ModResourceResolver = MIXED
**Blocking deps:** ModService — none (`System.IO`/`Compression`/`Regex`/JSON; theme colors stored as hex strings, parsed locally). ModResourceResolver — `System.Windows.Media.ImageSource`/`BitmapImage` and `pack://application:,,,/Resources/...` WPF URIs in `ResolveImage`. `Models/ModManifest.cs` is pure POCO (PORTABLE).
**Seam (resolver):** Abstract image resolution behind an interface; `ResolveAudioPath`/`ResolveUri`/`HasModOverride` are already pure path logic and portable.

ModService installs/validates/sanitizes `.ccpmod` archives (zip + `mod.json`), activates a mod, and exposes fallback-chain data accessors. ModResourceResolver overrides resources from the active mod, falling back to embedded resources.

### Requirement: Install and sanitize mods, resolving resources with active-mod override.
The system SHALL validate and sanitize a `.ccpmod` manifest before activation, and resolve images/sounds from the active mod when present, otherwise from embedded defaults.

#### Scenario: Malformed manifest
- WHEN an installed `.ccpmod` has an invalid/oversized manifest
- THEN installation SHALL be rejected and the partial install cleaned up.

---

## Capability: Remote Control (poll-based companion controller)
**Files:** Services/RemoteControlService.cs
**Class:** MIXED
**Blocking deps:** `System.Windows.Threading.DispatcherTimer` (poll loop), `DispatcherHelper.RunOnUISync`, `MainWindow MainWindowRef`, and a large `ExecuteCommand` switch driving WPF services (Flash/Overlay/Wallpaper/WebView2). **Confirmed: no local HttpListener / no inbound server** — the desktop is a pure HTTP polling client.
**Seam:** Replace `DispatcherTimer` with `PeriodicTimer`; abstract command execution behind an `IRemoteCommandSink`. The transport (POST + 5s poll + 429 backoff + idle disconnect) is fully portable.

Endpoints: `/v2/remote/{start,stop,poll,status,emote}`, `/v2/directory/opt-in`. 4-digit `Random` PIN (not crypto-RNG).

### Requirement: Accept remote commands via server polling without an inbound listener.
The system SHALL register a session, poll the proxy for queued controller commands, dispatch them to local effects, and push throttled status back.

#### Scenario: Poll rate limited
- WHEN a poll returns HTTP 429
- THEN the client SHALL back off before the next poll rather than hammer the server.

---

## Capability: Embedded Browser (WebView2)
**Files:** Services/BrowserService.cs
**Class:** OS-SPECIFIC
**Blocking deps:** WebView2 — `Microsoft.Web.WebView2.Core` + `.Wpf`. Entire class is WebView2 glue (navigation, zoom, fullscreen, ad/tracker domain blocking, partner-site bypass for hypnotube.com).
**Seam:** Would require an alternative embedded-browser engine (CEF/WebKitGTK) behind an `IEmbeddedBrowser` interface — effectively a rewrite of this service.

### Requirement: Provide an embedded ad-filtered browser.
The system SHALL host an embedded browser that blocks known ad/tracker domains while bypassing filtering on partner sites.

#### Scenario: Blocked domain requested
- WHEN the embedded browser requests a resource from a blocklisted domain
- THEN the request SHALL be denied (unless on a partner site).

---

## Capability: Deeper Enhancement Fetch / Scrape / SSRF Guard
**Files:** Services/Deeper/EnhancementFetcher.cs, Services/Deeper/HtMetadataFetcher.cs, Services/Deeper/UrlSafety.cs, Services/Deeper/EnhancementValidator.cs, Services/Deeper/EnhancementSerializer.cs
**Class:** PORTABLE
**Blocking deps:** None across all five. Pure `System.Net`/`HttpClient`/`Regex`/Newtonsoft.

EnhancementFetcher: fetches/caches `.ccpenh.json` with SSRF-guarded handler, 256KB cap, 10s timeout, schema sniff. HtMetadataFetcher: HypnoTube page scraper (OpenGraph → JSON-LD → regex), host-allowlisted, LRU cached. UrlSafety: SSRF defense — host allowlist, private/reserved/cloud-metadata IP rejection, DNS-rebind-closing `SocketsHttpHandler.ConnectCallback`, path-traversal/UNC guards (a reusable portable shim). EnhancementValidator: model validation (non-finite/timeline/range checks). EnhancementSerializer: Newtonsoft load/save with `MaxDepth=64`.

### Requirement: Safely fetch and parse untrusted enhancement metadata.
The system SHALL fetch user-supplied enhancement/metadata URLs only after SSRF validation, enforce size/time/depth caps, and reject private/reserved network targets including cloud-metadata IPs.

#### Scenario: SSRF attempt to internal IP
- WHEN a fetch targets a private/reserved/link-local address (e.g. 169.254.169.254)
- THEN the connection SHALL be refused before any data is read.

#### Scenario: Redirect to disallowed host
- WHEN an allowed URL redirects to a non-allowlisted host
- THEN the redirect target SHALL be re-validated and rejected if unsafe.

---

## Capability: Deeper Library & Browser Auto-Discovery
**Files:** Services/Deeper/EnhancementLibrary.cs, Services/Deeper/BrowserAutoDiscovery.cs
**Class:** EnhancementLibrary = MIXED; BrowserAutoDiscovery = OS-SPECIFIC
**Blocking deps:** EnhancementLibrary — `DispatcherTimer` (debounce) + `Application.Current.Dispatcher` (marshal `LibraryChanged`); `FileSystemWatcher` itself is cross-platform. BrowserAutoDiscovery — WebView2 (`Microsoft.Web.WebView2`) + `ExecuteScriptAsync` to scrape HT pages, bound to `BrowserVideoTimeSource`.
**Seam (library):** Portable timer + a synchronization-context abstraction; core file I/O and matching are portable. BrowserAutoDiscovery is WebView2 glue (rewrite to port).

### Requirement: Maintain a hot-reloaded local enhancement library and auto-bind enhancements to the active browser video.
The system SHALL watch the library folder for changes (debounced) and, when the embedded browser opens a recognized video, fetch and bind its enhancement.

#### Scenario: Library file edited externally
- WHEN a `.ccpenh.json` file changes on disk
- THEN the in-memory library SHALL reload after a debounce interval and notify listeners.

---

## Capability: Moderation Guard
**Files:** Services/Moderation/PromptValidator.cs, Services/Moderation/ModerationLog.cs, Services/Moderation/ProhibitedCategories.cs (and sibling Moderation/* logic)
**Class:** PORTABLE
**Blocking deps:** None. `System.Text.RegularExpressions` + `System.IO` only. `%APPDATA%` resolved via `Environment.GetFolderPath` (cross-platform).

PromptValidator: soft regex detection of jailbreak/prompt-extraction patterns (warns, does not block). ModerationLog: append-only pipe-delimited log to `logs/moderation.log` with 10MB×5 rotation. ProhibitedCategories: enum of CCBill prohibited categories.

### Requirement: Detect and log prohibited prompt patterns.
The system SHALL scan AI prompts for jailbreak/extraction patterns and append moderation events to a rotating log.

#### Scenario: Log rotation
- WHEN the moderation log exceeds its size cap
- THEN it SHALL roll to an archive and continue writing a fresh log.

---

## Capability: Bug Reporting
**Files:** Services/BugReportService.cs
**Class:** PORTABLE
**Blocking deps:** None. `HttpClient` + `HMACSHA256` (portable BCL) + file tail-reads. `Environment.OSVersion` is reported but cross-platform.

Collects an allowlisted metadata set, scrubs logs, HMAC-signs the payload with an embedded secret, and `POST`s `/bug/upload`. (Note: `EmbeddedClientSecret` is a hardcoded constant — a security observation, not a portability one.)

### Requirement: Submit signed, scrubbed bug reports.
The system SHALL collect a fixed allowlist of diagnostic metadata, scrub sensitive content from logs, sign the payload with HMAC-SHA256, and upload it.

#### Scenario: Log scrubbing
- WHEN a bug report includes log excerpts
- THEN tokens/keys/PII SHALL be redacted before upload.

---

## Capability: Discord Rich Presence
**Files:** Services/DiscordRichPresenceService.cs
**Class:** MIXED
**Blocking deps:** `System.Windows.Threading.DispatcherTimer` (15s presence refresh) + `Application.Current.Dispatcher`. The underlying `DiscordRPC` library uses **named pipes**, which are cross-platform (the lib supports the Discord IPC socket on Linux/mac), so the IPC itself is NOT a hard Windows blocker.
**Seam:** Replace `DispatcherTimer` with a portable timer and drop the dispatcher marshalling — then PORTABLE.

Publishes the user's current activity (session/video/flash/idle/level) to Discord every 15s, gated by `OfflineMode`.

### Requirement: Reflect app activity as Discord Rich Presence.
The system SHALL connect to the local Discord client over IPC and update presence with the current activity, suppressing it in offline mode.

#### Scenario: Discord not running
- WHEN the Discord client is unavailable
- THEN connection failure SHALL be logged and presence updates skipped without crashing.

---

## Appendix: classification table

| Capability | Class | Blocking dep | ~LOC |
|---|---|---|---|
| Secure Secret Storage (4 stores) | OS-SPECIFIC | DPAPI ProtectedData | ~600 |
| Patreon OAuth | MIXED | DPAPI store (flow itself portable) | ~915 |
| Discord OAuth + webhooks | MIXED | DPAPI store (flow itself portable) | ~945 |
| V2 Unified Auth API | PORTABLE | none | ~830 |
| Account Orchestration | MIXED | WPF dialogs/MessageBox | ~808 |
| Cloud Profile Sync | MIXED | DispatcherTimer + Dispatcher→MainWindow | ~2960 |
| Leaderboard & Lookup | MIXED | DispatcherTimer | ~493 |
| Catalogue Submit/Lookup | PORTABLE / MIXED | Dispatcher (lookup only) | ~816 |
| Content Pack DL/Decrypt | MIXED | WPF BitmapImage thumbnails | ~1640 |
| Asset Import | PORTABLE | none | ~463 |
| Mod System | PORTABLE / MIXED | WPF ImageSource/pack:// (resolver) | ~1710 |
| Remote Control (poll) | MIXED | DispatcherTimer + WPF command dispatch | ~1305 |
| Embedded Browser | OS-SPECIFIC | WebView2 | ~600 |
| Deeper Fetch/Scrape/SSRF | PORTABLE | none | ~1320 |
| Deeper Library / AutoDiscovery | MIXED / OS-SPECIFIC | Dispatcher / WebView2 | ~710 |
| Moderation Guard | PORTABLE | none | ~400 |
| Bug Reporting | PORTABLE | none | ~410 |
| Discord Rich Presence | MIXED | DispatcherTimer (IPC pipes portable) | ~415 |
