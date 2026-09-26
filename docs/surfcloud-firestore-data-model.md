# SurfCloud Firestore quota data

The SurfCloud Drive Publisher uses the existing default Standard Firestore database in Firebase project `surfos-5b9af` (location `eur3`). The database is server-only: the Cloud Run publisher uses Application Default Credentials, while Firestore Security Rules deny every direct client read and write.

## Collections

```text
surfcloudUsers/{google-subject}
  usedBytes: number
  reservedBytes: number
  quotaBytes: number              # 16106127360, or 15 GiB
  packages/{package-id}
    id: string
    totalBytes: number
    packageDriveFileId: string
    sourceDriveFileId: string | null
    visibility: "private" | "public"
    publishedAt: timestamp
```

`google-subject` is the `sub` claim from a validated Google ID token. The publisher creates these documents automatically on the first successful reservation; Firestore collections do not need to be created ahead of time.

## Quota transaction

Before upload, the publisher runs a Firestore transaction that checks the retained bytes plus pending reservations against 15 GiB. It reserves the new package size only if there is room. After Drive accepts the files, a second transaction replaces the prior package record and converts the reservation into retained usage. If Drive upload fails, the publisher releases the reservation.

No composite indexes are required because the publisher reads records by their exact document paths and does not run filtered collection queries.
