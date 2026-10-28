# ADR-005: Maker-checker enforced in the domain, not in controllers

Status: Accepted. Owner: architect.

## Context
Decisions and settlements are money-affecting and need two different people (BR-31 to BR-39). A user may hold both Officer and Manager roles, so a role check alone is not enough: the checker must not be the maker of that same proposal.

## Decision
- The **Claim aggregate** owns the rule. Commands are `ProposeDecision(actor, outcome, amount, reason)`, `RequestSettlement(actor)`, `Confirm(actor, proposalId)`, `SendBack(actor, proposalId, reason)`, `Withdraw(actor, proposalId, reason)`. `actor` is a value object built from the validated token (`userId`, roles), never from the request body.
- `Confirm` and `SendBack` require role Manager **and** `actor.userId != proposal.makerId` (compared on user id). A maker-equals-checker violation returns the trailed refusal 403 `maker-cannot-check` (a `Result`, not an exception, per guidelines section 6). **Amended 2026-10-28 (code review): the role is checked twice.** The API policy is the primary check: its 403 is logged as a security event and is neither trailed nor stored (ADR-009 decision 1). The aggregate repeats it as defence in depth, using the actor's real roles: every command needs its role (Claimant for submit and answer; Officer for pick-up, assess, request information, propose and withdraw; Manager for confirm and send-back; Officer or Manager for reassign and correction). A missing role returns an **untrailed** 403 `forbidden` before any other check, so it behaves exactly like the policy 403. `ActorRole` on every trail entry comes from the actor's roles and is never hard-coded. So a misconfigured policy cannot let an Officer-only user confirm, and cannot make the trail falsely name a Manager (BR-31, BR-11). Alternative rejected: leave the role to the policy alone, as spec 1 section 3.9 originally said. One wrong policy would then both allow the act and falsify the audit record, and the guard costs one line per command. `Confirm` has no editable payload: it applies the stored proposal exactly; it carries only `proposalId`, so a checker confirms what they saw and a stale id gives 409.
- A pending proposal is a child entity (proposalId, type, outcome, amount, makerId, state). At most one open proposal per claim (partial unique index). While open, edit, assess and second-proposal commands return 409. Reassigning the maker auto-withdraws it.
- **Backstops:** DB `CHECK (checker_id IS NULL OR checker_id <> maker_id)` on the proposal table; `rowVersion` makes confirm vs withdraw vs confirm a single winner (409 for the loser).
- A refused attempt by an authenticated user writes an Internal trail entry and no state change (BR-04). **Amended 2026-10-28 by ADR-009:** the entry is written after `ROLLBACK TO SAVEPOINT` inside the command's own transaction (not a separate transaction), so it commits atomically with the stored idempotency response. Which 403s count as a refused attempt is DECIDED (human, 2026-10-28) in ADR-009: only resource-rule refusals after the role policy passed (non-assigned officer, maker equals checker, non-maker withdraw) are trailed; role-policy 403s are logged only.
- Controllers only apply coarse role policies (default deny); the rule is not duplicated there.

## Alternatives considered
- **Controller/attribute check:** easy to bypass from a second entry point (a consumer, a job); untestable without HTTP. Rejected.
- **Workflow engine (Elsa, Camunda):** a framework for two states. Rejected.
- **DB trigger only:** catches it too late and gives poor errors. Kept only as a backstop.
- **Separate Approval service:** one more service and cross-service consistency for a rule that belongs to the claim aggregate. Rejected.

## Consequences
- Unit tests cover every transition pair, dual-role user, stale proposalId, concurrent confirms.
- Pending* are real states, so queues and metrics (checker turnaround) fall out of the trail.
- Dual-role accounts must exist in seed data to prove the rule.
