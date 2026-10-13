# Decommission the monolith host

Precondition: all four contexts have been on their services for the full burn-in period with no
rollback trigger, and the gateway logs show no request proxied to `booking_modular_monolith` for
at least 7 days (`flip-route.sh status` shows every context on `service`).

1. Repoint the gateway's catch-all `monolith` cluster (non-module paths: `/connect`,
   `/.well-known`, IdentityServer UI) at the Identity service and confirm nothing else hits it.
   The destination keeps the name `monolith` so existing `...__Destinations__monolith__Address`
   overrides keep working.
2. Stop the monolith deployment; keep its databases read-only for the retention period (they are
   the source for any manual data recovery), then archive and drop them:
   `*_modular_monolith`, `*_modular_monolith_persist_message`, `*_modular_monolith_read`.
3. Remove the host from the code base (done in AB-505): `src/Api` (project, Dockerfile, tests),
   its solution entries, the monolith compose file, the monolith CI image, the Aspire monolith
   resource. Gateway defaults now point every cluster at its service.

After decommission there is no route back to the monolith: rollback means redeploying the
previous service version. `flip-route.sh` and the cut-over overlay stay in the repository for
history but require an image of the monolith (`MONOLITH_IMAGE`) to run.
