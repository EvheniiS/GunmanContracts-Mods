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

## What the API cannot do

- Create a mod page, or edit its **description, summary, tags, category, images, permissions**. No endpoint exists in v1, v2 or v3. The page text is always pasted by hand.
- Replace or delete a changelog entry (append only).
- GraphQL v2 (`/v2/graphql`) is mostly read plus site features (comments, endorsements, collections). Its `createChangelog` is for collection revisions, not mod files.

## Consequences for the workflow

- The things that cost time and can be automated: building the zip, the hash and size, uploading the file, setting version + file description, posting the changelog, adding the requirements, and verifying afterwards that Nexus shows what was intended.
- The things that stay manual: pasting the BBCode description and summary, and images. The `-Docs` check tells the user exactly which pages need a re-paste.
- Every upload is public and cannot be silently undone. A release skill must be dry-run by default and confirm each upload with the user.
