# Nexus Mods API: what it can do for this repository

Verified Oct 3 2026 against the live API and the official OpenAPI spec (`https://api.nexusmods.com/openapi.yaml`, "Nexus Mods API 3.0.0"). Re-fetch the spec before building anything on it; it is versioned and still beta in parts.

Auth: personal API key from nexusmods.com/settings/api-keys, sent as header `apikey`. Add `Application-Name` / `Application-Version` headers. Key lives in the git-ignored `.env` as `NEXUS_API_KEY`. Game domain: `gunmancontractsstandalone`. Account: Evgeeso (user id 175274424).

## v1 REST (`https://api.nexusmods.com/v1/`): read side

| Endpoint | Gives |
|---|---|
| `games/{game}/mods/{id}.json` | name, **summary**, **description** (BBCode with `<br />` per line), downloads, endorsements, status, `updated_timestamp`. `version` is a Nexus counter, not the mod version. |
| `games/{game}/mods/{id}/files.json` | every file: `name` (e.g. "GrabFix 1.1.1"), `category_name` (MAIN / OLD_VERSION ...), `uploaded_timestamp`, `is_primary`, `file_name` |
| `games/{game}/mods/{id}/changelogs.json` | changelog entries per page-version counter |
| `games/{game}/mods/updated.json?period=1d` | recently updated mods |
| `users/validate.json` | key check |

Limits: **2,000 requests/hour and 20,000/day** (response headers `x-rl-hourly-remaining`, `x-rl-daily-remaining`; an earlier note here said 2,500/day then 100/hour, which was wrong). `Check-Nexus.ps1` prints what is left. Only published mods are visible.

## v3 REST (`https://api.nexusmods.com/v3/`): the Upload API (open beta)

This is the write side. Same personal API key.

| Step | Endpoint | Notes |
|---|---|---|
| 1 | `POST /uploads` `{size_bytes, filename, md5}` | Returns `presigned_url`. Files over 100 MiB use `POST /uploads/multipart`. `md5` becomes required on or after 2026-12-01; send it now. |
| 2 | `PUT <presigned_url>` with the zip | Must send `Content-Disposition: attachment; filename="<filename>"` and `Content-MD5` (base64 of the binary md5). |
| 3 | `POST /uploads/{id}/finalise`, then poll `GET /uploads/{id}` until `state = available` | |
| 4a | `POST /mod-files/{id}/versions` `{upload_id, name, version, file_category, description, update_mod_version, archive_existing_file, primary_mod_manager_download, allow_mod_manager_download, show_requirements_pop_up}` | New version of an existing file (the normal update). `update_mod_version: true` also sets the page's version. |
| 4b | `POST /mod-files` `{upload_id, mod_id, name, version, file_category, ...}` | A brand-new file on an existing page. |
| 5 | `POST /mods/{id}/changelogs` `{version, changelog}` | Changelog text for a version. **Append-only**: calling twice adds more text, it does not replace. Max 65,535 chars. |

Also available: `PUT /mod-files/{id}` (rename a file only), `GET/PUT /mod-file-versions/{id}/dependencies/ranges` (requirements between your mods), `GET /mods/{id}/files`, `GET /games/{game}/mods/{id}` (name only, no description). `POST /mod-file-update-groups/{group_id}/versions` is deprecated (removal on or after 2026-09-09); use `/mod-files/{id}/versions`.

Field rules: file/version `name` max 50 chars, `[a-zA-Z0-9 _'().-]` only; `version` max 50 chars, `[a-zA-Z0-9.-]` only. IDs: the v3 mod-file id is shown under "Advanced" in the page's Files tab or in Manage Files > edit.

The official GitHub Action `Nexus-Mods/upload-action` wraps steps 1-5 (beta). It can only add a version to an existing file id, never create a page.

### Verified with this key (Oct 3 2026, read-only calls)

The personal key reaches v3. Three id spaces are involved and they are not interchangeable:

| Id | Example (GrabFix) | Where it comes from |
|---|---|---|
| page number (v1 `game_scoped_id`) | `37` | the URL, `Tools/nexus-mods.json` `mods` |
| v3 mod id (long) | `43654047596581` | `GET /v3/games/{game}/mods/{page number}` -> `data.id` |
| v3 mod-file id (the "file group") | `8051487` | `GET /v3/mods/{v3 mod id}/files` -> `data.mod_files[].id` |

`GET /v3/mods/37/files` with the page number is a 404 ("Mod not found"); it needs the long id. `Update-Nexus.ps1 -Resolve` looks all of them up and stores them in the `v3` block of `nexus-mods.json`.

`GET /v3/mod-files/{fileId}/versions` returns the chain, newest first by `position` (a decimal string): each entry has `id`, `name`, `category` (`main` / `old_version`), `uploaded_at`, `is_primary`. Its `version` field is a counter ("1", "2"), not the mod version, so the real version is read from the name. The mod file's own name stays the FIRST version's name ("GrabFix 1.1.0" while the live main file is "GrabFix 1.1.1"). The version records do not expose the file description, so the description is always whatever the script sends.

A page can hold several mod files (EnemyAwarenessFix: the Fix and the Log).

Upload half proven Oct 3 2026 (`Update-Nexus.ps1 -Apply -UploadOnly`, GrabFix 1.1.2 zip): create upload, PUT, finalise, state `available`. One undocumented requirement found by the first attempt (HTTP 403 `SignatureDoesNotMatch`): the presigned URL signs `content-type` too, so the PUT must send **`Content-Type: application/octet-stream`** besides `Content-Disposition` and `Content-MD5`. Retrying a PUT on the same URL is fine.

Whole update path proven Oct 3 2026 with GrabFix 1.1.2 (live): create version, changelog, verify all work. Findings:
- `archive_existing_file: true` sets the previous main file's category to **`archived`**, whereas the site's manual "Update" flow left the earlier file as **`old_version`** (v1 `OLD_VERSION`, visible under old files). **Confirmed behaviour (GrabFix 1.1.2, Oct 3 2026)**: the Files tab listed only 1.1.2 (Main) and 1.1.0 (Old files); the archived 1.1.1 is not in those lists but still appears, with an Archived badge and its download count, under the file's **Version history** link. So it is tucked away, not deleted. The user is fine with this (Oct 3 2026), so `Update-Nexus.ps1` archives by default (`-NoArchive` opts out). There is no API call that changes a version's category (the move endpoints only reorder versions between mod files). What the old file becomes when the flag is omitted (`-NoArchive`) has not been observed.
- A changelog posted through the API is keyed by the **version string** in v1 `changelogs.json` (`"1.1.2": [...]`), manual uploads are keyed by the page's version counter (`"2": ["Grab Fix 1.1.1", ...]`). With `update_mod_version` the v1 file `version` becomes the version string too.
- The site's own UI calls `POST /v3/uploads/multipart` (even for a 14 KB file) with cookie auth, application name "Nexus Next"; single-part uploads work fine with the API key.
- Official docs (Nexus-Mods/upload-action README): `file_id` = the existing mod-file id, plus `version`, `archive_existing_version`, `update_mod_version`, `changelog` (needs mod id), `description`, `category`, `primary_mod_manager_download`. It documents no endpoint order; the OpenAPI spec is the real reference. There is no endpoint to edit an existing version's description or category (`PUT /mod-files/{id}` renames the file only).

## What the API cannot do

- Create a mod page, or edit its **description, summary, tags, category, images, permissions**. No endpoint exists in v1, v2 or v3. The page text is always pasted by hand.
- Replace or delete a changelog entry (append only).
- GraphQL v2 (`/v2/graphql`) is mostly read plus site features (comments, endorsements, collections). Its `createChangelog` is for collection revisions, not mod files.

## Consequences for the workflow

- The things that cost time and can be automated: building the zip, the hash and size, uploading the file, setting version + file description, posting the changelog, adding the requirements, and verifying afterwards that Nexus shows what was intended.
- The things that stay manual: pasting the BBCode description and summary, and images. The `-Docs` check tells the user exactly which pages need a re-paste.
- Every upload is public and cannot be silently undone. A release skill must be dry-run by default and confirm each upload with the user.
