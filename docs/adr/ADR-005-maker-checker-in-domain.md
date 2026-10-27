# ADR-005: Maker-checker enforced in the domain, not in controllers

Status: Accepted. Owner: architect.

## Context
Decisions and settlements are money-affecting and need two different people (BR-31 to BR-39). A user may hold both Officer and Manager roles, so a role check alone is not enough: the checker must not be the maker of that same proposal.

## Decision
- The **Claim aggregate** owns the rule. Commands are `ProposeDecision(actor, outcome, amount, reason)`, `RequestSettlement(actor)`, `Confirm(actor, proposalId)`, `SendBack(actor, proposalId, reason)`, `Withdraw(actor, proposalId, reason)`. `actor` is a value object built from the validated token (`userId`, roles), never from the request body.
- `Confirm` and `SendBack` require role Manager **and** `actor.userId != proposal.makerId` (compared on user id). Violation throws a domain exception mapped to 403 `maker-cannot-check`. `Confirm` has no editable payload: it applies the stored proposal exactly; it carries only `proposalId`, so a checker confirms what they saw and a stale id gives 409.
- A pending proposal is a child entity (proposalId, type, outcome, amount, makerId, state). At most one open proposal per claim (partial unique index). While open, edit, assess and second-proposal commands return 409. Reassigning the maker auto-withdraws it.
- **Backstops:** DB `CHECK (checker_id IS NULL OR checker_id <> maker_id)` on the proposal table; `rowVersion` makes confirm vs withdraw vs confirm a single winner (409 for the loser).
- A refused attempt by an authenticated user writes an Internal trail entry in its own small transaction (the main one rolls back) and no state change (BR-04).
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
