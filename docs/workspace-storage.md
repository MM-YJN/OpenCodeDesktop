# Workspace storage

OpenCode Desktop is a packaged Windows app. The App composition root obtains the current user's store through `Microsoft.Windows.Storage.ApplicationData.GetDefault()`; it does not construct a package-family path or share storage with unpackaged builds.

## Workspace settings

`LocalPath/workspace.v1.json` is a UTF-8 JSON snapshot with schema version `1`. For a normal package installation, `LocalPath` is the package's `%LOCALAPPDATA%/Packages/<package-family>/LocalState` directory. The API-resolved location is authoritative.

The document contains connection IDs, display names, validated local addresses, credential-saving preferences and opaque credential references. Each connection owns its project directories, project IDs, last selected directory, and each project's confirmed selected session ID. A null session ID means new-session context, without creating a server session. A null active connection ID disables automatic reconnection.

The format is bounded to 4 MiB, 128 connections, 256 projects per connection, and bounded strings. Invalid ownership, duplicate identities/addresses/directories, malformed documents and unsupported versions are rejected. Versioning is independent of the app's Git/build version. Future format migrations must be explicit; version 1 does not guess at newer schemas.

Core requests snapshots after profile changes and confirmed navigation, rather than relying on shutdown. Writes run off the UI thread and are serialized. A complete temporary file in the same directory is flushed to disk before atomic replacement. Failed or cancelled writes leave the previous document intact. Startup ignores uncommitted temporary files. The window flushes queued snapshots before normal close; a crash can still lose changes that had not yet committed.

Settings can participate in Windows app-data backup; they are not a conversation database or a synchronization mechanism. Conversation history, execution state and pending requests are re-read from OpenCode. Drafts, uncertain submissions, task state, pane positions and terminal processes are not persisted.

## Credentials

Remember credentials on this device defaults to enabled for new manual and discovered-local connections. The choice is saved per profile. An empty password field uses the saved credential when available. Opting out removes the profile's credential reference and, after a successful settings commit, deletes its protected entry. Automatic connection then requires the user to enter the password explicitly.

Infrastructure stores immutable entries at `LocalCachePath/Credentials/<opaque-guid>.bin`, encrypted through Windows DPAPI `ProtectedData` with `DataProtectionScope.CurrentUser` and an application purpose marker. `LocalCachePath` is excluded from Windows app-data backup/restore. No app-managed encryption key, plaintext password or authentication header is written to settings or diagnostics. Copying settings to a different installation does not guarantee that its credentials are available.

Passwords are saved only after successful authentication and compatibility checks. Replacement creates a new protected entry before settings reference it; the prior entry is deleted only after the new settings commit. Failed cleanup is reported and can be retried. A crash between credential creation and settings commit can leave an unreferenced encrypted file; it is never used for authentication automatically. Explicit disconnect retains the credential but clears the automatic connection target. Profile deletion or endpoint replacement discards the old credential reference and workspace ownership. Rename retains identity and navigation.

DPAPI protects data at rest for the Windows user; it is not isolation from other programs running as that user. Authentication requires a plaintext password in process memory. Temporary byte buffers owned by the credential implementation are cleared; managed strings cannot be guaranteed to be erased immediately.

## Startup and recovery

Only the last active connection is automatically probed. Authentication and version checks remain in the normal connection flow; no service is started. An unavailable initial server offers explicit Retry/connection selection after its bounded probe. Established connections keep WI-009's bounded stream/read recovery. Missing or rejected credentials require explicit user intervention without an automatic authentication loop.

Restoration validates the server directory/project and fetches the selected session by ID, including sessions outside the first list page. History, execution and pending requests use the existing authoritative loading/recovery path. No prompt or interaction decision is replayed. Missing sessions return to the validated project's list while retaining the remembered ID for Retry until the user confirms another selection. Missing projects retain their saved entries. User navigation, disconnect and shutdown cancel obsolete restoration; late responses cannot promote obsolete selections.

Unreadable, malformed or unsupported settings disable writes so that subsequent in-memory use cannot silently overwrite recoverable data. The app shows an explicit Reset saved settings action. Reset moves the original document to `workspace.v1.json.recovery-<guid>` in the same directory, then saves the current in-memory profiles and selections. It does not delete server work or silently remove protected files that might belong to the preserved document. If navigation supersedes the initial settings read, existing disk data is likewise preserved until restart or explicit reset.

Credential failures preserve settings and offer reconnection; resetting settings is not required. Storage-write failures keep the current workspace usable and expose Retry saving. After a Windows restore, credentials excluded from backup may need to be entered again.
