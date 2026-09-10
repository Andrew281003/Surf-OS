# SurfCloud Google Drive publisher setup

This service adds a server-enforced 15 GiB storage quota for every SurfCloud user. It receives the existing SurfOS `publish` multipart request, validates the user's Google ID token, reserves quota in Firestore, uploads to one SurfCloud-owned Google Drive folder, and records the final file IDs and byte counts. A failed upload releases its reservation; publishing an updated package replaces the previous package record.

## Required Google setup

1. Use the existing `surfos-5b9af` Google Cloud project and enable **Cloud Run**, **Cloud Build**, **Artifact Registry**, and the **Google Drive API**.
2. Use its existing default Firestore Native database in `eur3`. The repository's `firebase.json`, `firestore.rules`, and `firestore.indexes.json` lock it down for server-only quota accounting; deploy them with `npx firebase-tools@latest deploy --only firestore` if rules are changed.
3. In the Google account that owns the SurfCloud Drive storage, create a folder for packages and copy its ID from the URL.
4. Create an OAuth 2.0 Web Application client. Its client ID is the audience accepted from SurfOS tokens.
5. Obtain a refresh token for that Drive owner with the `https://www.googleapis.com/auth/drive.file` scope. Store the client secret and refresh token only in Secret Manager; never in this repository or SurfOS.
6. Give the Cloud Run runtime service account the **Cloud Datastore User** role. It does not need Drive credentials because it exchanges the stored refresh token at runtime.

Google Drive files created with OAuth are owned by the authenticated Drive account and consume that account's storage. This is why the service uses the Drive owner's refresh token instead of a service account.

## Deploy

Run the following from the repository root after replacing the placeholders. Configure the two sensitive settings from Secret Manager rather than passing literal values on a shared shell.

```sh
gcloud run deploy surfcloud-drive-publisher \
  --source . \
  --region YOUR_REGION \
  --set-env-vars GOOGLE_CLOUD_PROJECT=YOUR_PROJECT_ID,SURFCLOUD_DRIVE_FOLDER_ID=YOUR_FOLDER_ID,SURFCLOUD_AUTH_AUDIENCE=YOUR_OAUTH_CLIENT_ID,DRIVE_OAUTH_CLIENT_ID=YOUR_OAUTH_CLIENT_ID \
  --set-secrets DRIVE_OAUTH_CLIENT_SECRET=drive-client-secret:latest,DRIVE_OAUTH_REFRESH_TOKEN=drive-refresh-token:latest
```

Copy the resulting HTTPS service URL and set it on the computer running SurfOS:

```sh
export SURFCLOUD_PUBLISH_URL="https://YOUR_SERVICE_URL/publish"
export SURFCLOUD_TOKEN="A_GOOGLE_ID_TOKEN_FOR_THE_SURFCLOUD_USER"
```

The service returns `413` with current usage, quota, and remaining bytes when an upload would exceed 15 GiB. The SurfOS client already displays non-successful publisher responses, so no client code change is required for quota rejection.

## Important operational notes

- `SURFCLOUD_TOKEN` must be a Google ID token whose audience is `SURFCLOUD_AUTH_AUDIENCE`; an arbitrary API token is rejected. SurfOS needs an OAuth sign-in/token-refresh flow before end users can publish without manually setting a short-lived token.
- The current SurfOS builder limits a package payload to 4 MB and its source snapshot to 32 MB, so the publisher limits a request to 96 MiB. The 15 GiB quota is total retained storage, not a per-upload limit.
- The Drive folder must remain private. This service currently stores files; serving private downloads should be added as a separate authenticated endpoint instead of making Drive files public.
- Do not delete Drive files manually. Add a delete endpoint that removes both the Drive files and the Firestore package record in one managed workflow, so `usedBytes` remains accurate.
