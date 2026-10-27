# AI Working Journal: Chubb Claims Backend

How we directed, challenged and overrode the AI agents, one entry per phase. Entries are written from the repo files and the human's messages. Anything the transcript does not show is marked **[confirm]** and is not claimed as fact. Agent roles are defined in `AGENTS.md`.

---

## 2026-10-28: Slice 1 (backend Identity, Gateway, scaffold) and frontend foundation

**Goal.** A runnable Phase 1 edge. `docker compose up` starts Postgres, Identity (RS256 JWT, six seeded users including one dual-role user) and the Gateway (YARP routing, JWT check, CORS, merged OpenAPI). Claims, Reporting and Notification exist as minimal hosts only. Frontend: the foundation (scaffold, auth, guards, interceptors, shell, shared timeline and badge, login). No Claims domain in this phase.

### Asked

- **Spec first.** Write `docs/specs/identity-gateway/SPEC.md` from the template and get architect review before code (AGENTS.md spec-first gate). Frontend: `docs/specs/foundation/SPEC.md`, and mockups 00 and 01 approved before any screen was built (frontend TASKS design gate).
- **Backend slice 1.** Solution scaffold and `Contracts`; Identity with seeded users including a dual-role user; Gateway with YARP routes, default-deny policies and an aggregated OpenAPI; compose with `jwt-keys`, Postgres, Identity and Gateway.
- **Frontend foundation.** Angular 20 scaffold, login per mockup 01 v2, token and problem-details interceptors, role guards, shell, shared timeline.
- **Gates.** `scripts/security-check.sh` run per phase; reviewer findings fixed before close.
- **Docs at close.** This entry, both READMEs, an ADR format check and a drift list for the architect.

### Agents and what they did

Taken from the files each agent owns. Items marked [confirm] are inferred, not shown in the transcript.

- **architect.** Wrote and maintains `docs/architecture/ARCHITECTURE.md` and ADR-001 to ADR-008. Amended ADR-003 and ADR-008 on 2026-10-28. Reviewed the identity-gateway SPEC (marked REVIEWED 2026-10-28) and the foundation SPEC (REVIEWED 2026-10-28, answers to Q-F3 to Q-F7). Updated `SECURITY-REQUIREMENTS.md` sections 2 and 6.
- **product-owner.** Backend and frontend task lists and the cut order. ARCHITECTURE section 11 carries a re-estimate note (row 0 from 30 to 60-75 minutes) that is waiting for a product-owner decision [confirm the decision].
- **ui-ux-designer.** Mockups 00 (shared patterns) and 01 (login, v1 then v2). `docs/mockups/APPROVALS.md` records both as APPROVED by the human on 2026-10-28.
- **backend-developer.** Wrote the identity-gateway SPEC (author field) and implemented Identity, the Gateway and the scaffold. SPEC section 14 lists eight deviations made during implementation.
- **frontend-developer.** Wrote the foundation SPEC (author field) and implemented the Angular foundation.
- **devops-engineer** [confirm]. Owns `docker-compose.yml`, the Dockerfiles, `docker/postgres/init-databases.sh`, `scripts/dev-keys.sh` and `scripts/security-check.sh` per the ownership matrix. The file headers do not name an author.
- **security-reviewer and code-reviewer.** Raised the four findings under "Challenged" below [confirm which reviewer raised which finding].
- **qa-test-engineer and frontend-qa-engineer** [confirm]. Own the test suites (86 backend tests, 92 frontend specs).
- **orchestrator.** No artifact from it in this phase [confirm whether it ran].
- **docs-journal-keeper.** This pass: both READMEs, this journal, the ADR format check, the drift list, and a run of every documented command.

### Accepted (kept as produced)

- **Identity token.** RS256, 30-minute lifetime, issuer `chubb-identity`, one audience `chubb-claims-api`, claims `sub`, `role` (array), `name`, `market` (ADR-008 amendment).
- **Seed set.** Six users: two SG claimants, two officers, a manager, a dual-role user (`officer.manager@`), and an HK claimant (`claimant2@`) with a second officer. Checked: all six sign in; a wrong password returns 401.
- **Gateway routing.** `POST /auth/token` is anonymous; `/claims`, `/dashboard`, `/staff` and `/notifications` require authentication; unknown paths return 404; OpenAPI and Swagger are served only in Development. Checked on the running stack.
- **Compose layout.** `jwt-keys` one-shot generates the dev keys, so nothing is committed. Postgres has no published port. Identity sits on `edge` and `data`, the Gateway on `edge` only. App containers run read-only with all capabilities dropped.
- **Frontend dev-only picker.** The demo list is bundled only in the development build. The token goes only to the Gateway origin. `sessionStorage` is the documented dev fallback (foundation spec A-F4).
- **ADR set.** ADR-001 to ADR-008 kept as written.
- **Client generator choice (spec only, not built).** openapi-generator `typescript-angular` over NSwag (foundation spec A-F3).

### Challenged / overridden

**Challenged: reviewer findings.** Each finding changed code or config before this phase closed. The work is not committed yet, so the fixes live in the working tree.

1. **Shared env secrets.** Services received secrets through one shared env file. Fix: each compose service has its own `environment:` list, and the Gateway gets no database or Identity secret. The compose header now says the file is "NOT shared through an env_file". Checked: `docker compose config` is valid.
2. **Vulnerable SSH.NET.** Pulled in transitively by Testcontainers. Fix: `SSH.NET` 2026.0.0 pinned in `Directory.Packages.props` with central transitive pinning. Checked: `dotnet list package --vulnerable --include-transitive` reports no vulnerable packages.
3. **Header denylist.** The Gateway stripped a list of known identity headers, so an unlisted `X-User-*` or `X-Forwarded-User` header would have passed. Fix: an allowlist in `ProxyTransforms.cs`. Everything else is dropped, and the Gateway sets `X-Correlation-Id` itself. SPEC deviation 6 records the change.
4. **Demo emails in the production bundle.** The seeded emails were in every build. Fix: the list is imported only by `environment.development.ts`; production has an empty list. Checked: the production `dist` contains no `chubb.test`. The same check found that `npm run check:bundle` passes when `dist/` is missing (open, see Follow-ups).

**Challenged: architect decisions recorded in the docs.** These are design choices, not human decisions.

5. **Committed dev keys rejected.** They would trip the secret scan and normalise allowlisting key material. Replaced by the `jwt-keys` container and `scripts/dev-keys.sh` (ADR-008 amendment).
6. **One audience per service rejected.** The Gateway forwards one token unchanged, so one shared audience is used. Per-service audiences are the documented production path (ADR-008).
7. **Estimate challenged.** Row 0 was planned at 30 minutes. The architect re-estimated 60-75 minutes; the cut line moves 30-45 minutes later (ARCHITECTURE section 11). Open: the product-owner decides whether to absorb it from 1c or cut scope.
8. **Finance grouping.** The human asked for an own Payment service. The architect grouped Finance into one service with three modules (ADR-001) and asked the human to confirm. Open: the transcript does not show the confirmation.
9. **Frontend client generator.** NSwag rejected (its Blob-based responses would double-handle errors). A hand-written client rejected because it would invent contracts, which ADR-003 forbids (foundation spec A-F3).
10. **Spec deviations from implementation.** The backend-developer recorded eight deviations from the SPEC, including emails lower-cased in the domain with a plain unique index instead of a `lower(email)` index, and the OpenAPI bearer requirement added per operation. Open: awaiting architect re-confirmation (SPEC section 14).

**Overridden: human decisions.**

- **Login field and endpoint.** Mockup 01 v1 used an email-or-username field and a different endpoint. The human approved v2: Email only, `POST /auth/token` with `{ email, password }` (mockup 01 v2 header; `APPROVALS.md`; foundation SPEC Q-F1). Reason: [human to add].
- **Demo-account picker, dev only.** The human resolved Q-F2: the picker is dev only, fills the email only, and the password comes from the README (foundation SPEC Q-F2; mockup 01 v2). Reason: [human to add].
- **"Area: X" label.** The human added the label to the shell toolbar (mockup 00; `shell.component.html`). Reason: [human to add].
- **Monorepo for the backend only.** The backend is one repo holding all services; the frontend is its own repo. Reason: [human to add].
- **gitleaks skipped for now.** This overrides the AGENTS.md Definition of Done ("`security-check.sh` exits 0 before a slice is accepted") and SECURITY-REQUIREMENTS section 4. The gate exits 1 on the dev machine as a result. The transcript does not record the human's reason. [human to add]
- **Finance deferred to Phase 2** under the 5-hour cap (ARCHITECTURE section 0, "human decision: 5-hour cap kept"). Accepted.

### Outcome

Checked on 2026-10-28:

- **Backend tests.** `dotnet test ChubbBackEnd.sln`: 86 passed (Identity 36, Gateway 47, Hosts 3). One earlier run exited 1 after all 36 Identity tests passed, reporting a Testcontainers cleanup cancellation. The re-run exited 0. Not investigated.
- **Frontend tests and build.** 92 of 92 specs pass in ChromeHeadless. `npm ci` reports 0 vulnerabilities. The production build succeeds, and `check:bundle` passes.
- **Security gates (both exit 1).** Backend: gitleaks MISSING; dependency audit PASS; format PASS; trivy not installed, so the image scan is skipped. Frontend: gitleaks MISSING; npm audit, Prettier and the dist check PASS.
- **Compose stack.** `docker compose up --build` built and started `jwt-keys`, `postgres`, `identity` and `gateway`. Health is 200; all six users sign in; a wrong password returns 401; `/swagger/` returns 200; `/openapi/v1.json` lists only `/auth/token`; `/claims` returns 502 because Claims is not in compose.
- **Local run without Docker.** Identity and the Gateway started with `dotnet run` against a throwaway PostgreSQL 16 container. Both were ready and the local Gateway issued a token. The container and processes were then removed.
- **ADR format.** 8 of 8 ADRs have Context, Decision, Alternatives considered and Consequences. No edits were needed.
- **Incident.** After one detached `compose up`, the Chubb containers and network were gone, though the volumes remained. The stack was started again in the foreground and verified. The cause was not identified.
- **Not verified.** A clean clone (the work is not committed); a first-run compose start (existing volumes were reused); `docker compose down -v`; rendering of the copied Mermaid diagram.

### Follow-ups

- **Gate.** Install gitleaks so `security-check.sh` can exit 0, or record the next override.
- **Runtime.** .NET 8 support ends 2026-11-10. The move to a supported version needs planning.
- **Architect.** Confirm the eight SPEC deviations and the Finance grouping (ADR-001). Correct ARCHITECTURE section 0 item 6, which says Phase 2 routes return 404 in Phase 1: `/claims/{id}/payee-details` is forwarded to Claims today. Correct section 10, which says compose seeds policies; no Claims service exists yet.
- **Frontend.** Make `check:bundle` fail when `dist/` is missing. Change the picker hint, which says "see the README" but points at the frontend README. Wire the generated client (foundation step 10).
- **Build order.** Claims domain next, then its OpenAPI contract in `contracts/openapi/`, then Reporting and Notification.
- **Journal backfill.** The planning commits (`595e0dc` to `26e3f83`) have no entries. None were written, because the transcript for them is not in this pass.
- **Open for the human.** Reasons for the monorepo and gitleaks decisions; which reviewer raised each finding; whether the identity SPEC review came before the first code. The SPEC is marked REVIEWED on 2026-10-28, but the transcript does not show the order. Finance grouping confirmation.
