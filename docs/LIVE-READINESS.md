# Local relay readiness

## Request path and HTTPS

The deployed pattern is browser/mobile → HTTPS Worker → authenticated WebSocket relay → loopback HTTP Kestrel. Inspection of the recovery Worker showed that it copies request headers except `cf-*`, passes the original host, and waits up to 60 seconds. The relay client preserves Host and other end-to-end headers, removes hop-by-hop headers, and uses a 55-second local timeout. Neither component independently reconstructs the public scheme for ASP.NET Core.

Previously the application did not run Forwarded Headers Middleware. Kestrel therefore generated an `http://` cookie challenge from the local transport even when the original request was HTTPS. Scheme forwarding now runs before authentication and HSTS. Only `X-Forwarded-Proto`, one hop, and the framework's loopback trusted proxies are used; `X-Forwarded-Host` is not trusted. Do not clear KnownNetworks/KnownProxies or bind this relay-only backend publicly.

If an older relay does not include `X-Forwarded-Proto`, set `ReverseProxy__PublicOrigin=https://hyundai.cloudapps.trade`. The fallback only changes the scheme when both the transport peer is loopback and the request Host matches that configured origin. Local `http://127.0.0.1:5169` continues to work. Authentication cookies use Secure for the reconstructed HTTPS request.

The historical HTTP-login 500 cannot be attributed to a .NET exception from the supplied report. A read-only reproduction from the build environment returned `Server: Pingora` on HTTP and an edge 403 on HTTPS. There is no evidence of a separate login-view crash: HTTP and forwarded-HTTPS login rendering and the antiforgery/sign-in flow pass integration tests. Removing the downgrade avoids that failing HTTP hop; public confirmation is still required after updating the owner's backend.

## Location validation

Counts and VIN scanning had a separate range-only validator which accepted `(0,0)` and non-finite doubles. Missing-part requests already used the stricter validator. All four location-writing paths (count, VIN scan, recount submission, missing-part request) now share the existing Arabic validation: missing/non-finite, out-of-range, and the `(0,0)` pair fail with 400 and no writes. Zero latitude or longitude alone remains valid. Count requests also reject an explicitly mismatched warehouse instead of silently ignoring it.

## Approval latency and contention

The supplied test recorded one final approval exceeding a 30-second client timeout, but later observed the successful mutation. No SQL trace or request timing was captured, so a definitive historical cause is **not established**. Inspection shows fixed primary-key lookups and a conditional update, not an N+1 loop or an obvious missing-index query in that approval path.

SQLite has one writer and its default busy timeout is 30 seconds. Contention is reproducible with a second connection holding a write transaction. The application now defaults SQLite to a **5-second** busy timeout (an explicitly configured Default Timeout is respected), and final-approval validation of terminal/out-of-scope requests happens before obtaining the writer transaction. A busy database gives a clear Arabic failure asking the operator to refresh the request state before retrying. A successful transaction still atomically updates the request, stock, change log, and audit; conditional updates prevent double approval. Structured logs record the quantity request ID and service duration, without credentials.

Tests hold a real SQLite writer transaction, verify bounded failure without partial stock/audit changes, release it and approve successfully, and verify only one final approval under simultaneous submissions. An interceptor asserts the successful approval uses a bounded 3–10 SQL commands, and an uncontended approval must finish within five seconds in the test environment. These are regression budgets, not a public-network SLA. The one historical slow request may still have been relay/network latency; do not claim that the original delay has been conclusively root-caused.

## Demo safety and verification limits

See `TEST-SCENARIO.ar.md` for startup commands, isolated-database option, accounts, expected quantities, negative cases and device acceptance. The development fixtures are additive, transactional, versioned and opt-in. Existing accounts, passwords, scopes, counts and sessions are not overwritten. If another session is already live, all three demo sessions are historical rather than starting a second live session. Repeated startup does not reopen completed sessions or restore edited fixtures.

The dashboard selects a live session before the newest historical session so it stays aligned with the mobile current-session lookup even when historical fixtures are imported later.

Automated coverage uses .NET 8, SQLite, MVC cookie/antiforgery authentication, JWT API requests, workflow services, concurrent approvals and seed safety. Real Android camera/OCR/zoom/location permissions, APK UI, external relay behavior after deployment, and SQL Server runtime are not covered by those tests. No production data or running deployment is modified by the repository change.

The legacy HTTP smoke harness is now portable (explicit test-admin environment variables, no private credential-file lookup, configurable output path) and aligned with current import headers, missing-part workflow and dashboard markup. Run it only against a disposable empty database. It performs 150 assertions across imports, forms, JWT, recounts, corrections, exports and access controls.
