# 0010 - Uploaded media lives in an S3-compatible bucket

Date: 2026-10-02

## Status

Accepted. Supersedes the "local disk for now" position implied by ADR 0004's storage note.

## Context

`IFileStorage` has had exactly one implementation since the beginning: `LocalDiskFileStorage`,
writing under the API's own working directory and serving the files back through
`UseStaticFiles`.

That is fine on a development machine and wrong everywhere we would actually deploy. Fly, Railway,
Render, Cloud Run and App Service all give a container an ephemeral filesystem: it is recreated on
every deploy, every restart, and every crash. Shipping as-is would mean **every seller's logo and
every product photograph disappearing silently, repeatedly**, with no error anywhere - just broken
images on live storefronts.

## Decision

A second implementation, `S3FileStorage`, selected by `FileStorage:Provider`. The interface does
not change, and neither does anything that calls it.

### Cloudflare R2 as the intended target

Any S3-compatible bucket works - MinIO locally, B2, S3 itself - but the configuration defaults and
the startup checks are written for R2, because storefront images are almost entirely egress and R2
charges nothing for it. The same traffic on S3 is billed per gigabyte, which for this product is
the dominant cost rather than a rounding error.

### No ACL is set on upload

R2 does not implement per-object ACLs and rejects a request carrying one. Modern S3 buckets default
to `Object Ownership = bucket owner enforced`, where ACLs are ignored. On both, public read is a
property of the bucket - R2's public URL or a custom domain, a bucket policy on S3 - not of the
object.

So the uploader sets no ACL at all. The bucket must be configured for public read. Nothing private
is ever written there: these are images a seller publishes on a storefront that anyone can open.

### Key rules are shared, not duplicated

`StorageKeys` owns both providers' naming and validation. A key written by one must be readable by
the other, so switching providers does not orphan what is already stored, and the rule that stops a
crafted key escaping its prefix is one piece of code rather than two that can drift.

Validation matters more here than on disk. `../` means nothing to S3 - it is just a character in an
opaque key - so a traversal attempt would not fail, it would quietly address a different object.

### Misconfiguration fails at startup

If the provider is `S3` and the endpoint, bucket, key or secret is missing, the API refuses to
start. There is no sensible "storage is switched off" state for a product whose sellers upload
photographs, so unlike the AI provider this does not degrade - it stops.

One specific mistake gets its own check: setting `PublicBaseUrl` to the R2 **API** endpoint rather
than the bucket's public URL. The two are different hosts, the API one requires a signed request,
and the symptom is every image on every storefront returning 401 with nothing in our own logs. That
is worth naming precisely at boot rather than debugging later.

## Consequences

- The API is no longer in the media path. Browsers fetch from the bucket, which is faster for
  everyone and removes image traffic from the application's bandwidth entirely.
- `LocalMediaHosting` stays, gated to the local provider, so development needs no bucket and no
  credentials.
- Two more secrets to hold: the access key and secret. Environment variables only.
- **Existing local files are not migrated.** Development uploads stay on disk and will 404 if the
  provider is switched without re-uploading. That is accepted: there is no production data yet. If
  that changes before launch, a copy script is needed - the keys are identical on both sides, so it
  is a bulk upload rather than a transformation.
- Deleting a seller's media now depends on a network call that can fail. `DeleteAsync` surfaces the
  error rather than swallowing it; an orphaned object costs a fraction of a cent and is preferable
  to a delete that reports success it did not achieve.
