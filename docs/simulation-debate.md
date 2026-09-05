# Simulation: open design questions

*A critical read of the implementation as it stands, for the session that
resumes it. Each item states what the code does now, what is at stake, and the
positions worth arguing — not a verdict. August 2026.*

Companion: `docs/simulation-spec.md` (what to build) and the S.1503-4
validation dataset design brief in the radians repo (why).

## What is working

Worth stating first, because most of it is right and the questions below are
about the remainder.

- **The producer/consumer split holds.** `EpfdDown` composes live beams;
  `EpfdDownMask` implements S.1503-4 §D5.1.4.1 Steps 10–24 over the declared
  mask and the declared operating-parameter set. Both feed the same vendored
  `EpfdAccumulator`, so the two CDFs are commensurable bin for bin **inside one
  process**. That is exactly the margin decomposition the spec asked for, and
  it is better than the spec proposed — running both sides here removes a whole
  class of cross-tool confounds.
- **The two envelope bases of spec §5 are both real.** `ScenePointing`
  (ungated) is *reachable*; `ScheduledPointing` (scheduler-gated) is
  *occurring*. Choosing between them is a constructor argument rather than a
  rewrite, which is what §5 asked for.
- **Vendoring is disciplined.** `orbits/` and `epfdshare/` are byte-for-byte
  copies with a stated no-edit rule and a byte-comparison check. All four
  spot-checked files — `OrbitPropagator.cs`, `EpfdAccumulator.cs`,
  `radlimits.cs`, `ApLib.cs` — are currently identical to the radians working
  copy, including a change made to `ApLib.cs` on 28 August. The guard is being
  honoured in practice, not just documented.
- **The check harness is substantial**: 132 checks over ~3,900 lines, and the
  good kind — analytic α against brute force, the equal-power composition
  identity, the `maxSingle ≤ coChannel ≤ powerSum` ordering invariant, XML node
  counts. These are properties, not smoke tests.

## Q1 — The payload power budget does not exist, so "reachable" is the envelope the spec warned against

**Now.** No total-power constraint appears anywhere in the codebase.
`ScenePointing` resolves every beam at its configured power;
`ScheduledPointing` gates beams on and off and states explicitly that per-beam
power "is left unchanged (no budget redistribution)". The only power-related
mechanism is `illuminationDutyCycle`, which scales every resolved power by
10 log₁₀(duty) — a time-average, not a simultaneity constraint.

**At stake.** Spec §4.3: *"An envelope over 'every beam at full power in every
direction' is valid but so loose as to be useless… The total payload power
budget is what makes the envelope meaningful."* That is precisely the current
reachable basis. The consequence is not a wrong number but a **mis-attributed
one**: the margin decomposition will charge a large gap to "mask envelope"
when much of it is "no power constraint was modelled". The headline output of
the whole exercise is the thing distorted.

**Positions.**

1. *Implement the budget.* Total payload power as a first-class input, with a
   redistribution rule when fewer beams are lit. Makes reachable meaningful and
   the margin attributable. Cost: a real modelling decision — equal split,
   demand-weighted, or per-beam cap with a total ceiling — and each gives a
   different mask.
2. *Keep reachable naive, report occurring as primary.* Cheaper, and occurring
   is already implemented. But it inverts the spec's recommendation, and an
   occurring-derived mask is only defensible if the traffic model is part of the
   commitment — which for a filed mask it is not.
3. *Treat duty cycle as the proxy.* Already built. But it averages rather than
   constrains: it cannot express "these eight beams may be on, not all forty".

**Note against position 2:** the code comment says occurring is "a per-step
subset of reachable by construction". That holds *only* while power is
unredistributed. Add a budget and a lightly-loaded occurring configuration
could put more power into fewer beams and exceed reachable in some direction —
so the subset property is an artefact of the missing constraint, not a
guarantee to rely on.

## Q2 — Which basis produces the filed mask

Follows from Q1 and should be settled with it. The spec recommended
*reachable*, on the grounds that a mask is a commitment rather than a
description of typical behaviour. As things stand reachable is unusable and
occurring is the only basis that yields a tight mask — so the implementation
has effectively chosen the opposite of the spec, for the reason the spec
anticipated. That is a legitimate outcome but it should be a decision on the
record, not a side effect.

## Q3 — Vendoring: the guard is conditional

**Now.** Check J0 byte-compares the vendored files against the radians working
copy **"when that repository is present"**, and is skipped otherwise.

**At stake.** On any machine without a radians checkout — CI, a colleague, a
build agent — drift is undetectable, and the entire commensurability argument
rests on byte-identity. There is also a new hazard: radians now has a second
home on ITU Azure DevOps with a *different directory layout* (the tree is
nested one level deeper there). A path-based comparison against "the radians
working copy" has two possible sources that do not agree structurally.

**Positions.**

1. *Pin and verify without the repository.* Record each vendored file's hash
   and the radians commit it came from; J0 checks hashes always, and
   additionally byte-compares when a checkout is present. Detects drift
   everywhere and documents provenance.
2. *Fail rather than skip* when the repository is absent, forcing the checkout.
   Simple, but breaks anyone who legitimately has only beamlab.
3. *Stop vendoring* — package or submodule. Removes the class of problem;
   costs release plumbing, and the spec's original preference for a project
   reference was already found impractical enough to vendor instead.

## Q4 — The two sides agree with each other, not with radians

**Now.** `EpfdDown.Run` takes a fixed `timeStepSec`. `EpfdDownMask` says
plainly that the dual time step is "not modelled … callers pick one step
size". So the live and mask paths inside beamlab share a comb and compare
cleanly — but neither uses radians' actual schedule, which is dual-step with
the TS1 reduction.

**At stake.** The TS1 sampling-wobble work established that the deepest events
move with the comb phase, and that tail agreement between tools is bin-class
at best. A beamlab-internal margin is therefore clean but partly synthetic; a
beamlab-vs-radians margin is the real target and carries a sampling difference
on top of the modelling difference.

**Positions.**

1. *Keep the margin beamlab-internal.* Cleanest attribution — mask envelope,
   geometry and selection rules isolated with no sampling confound. But it
   never validates against the tool that issues findings.
2. *Implement the dual time step and TS1* in the simulator so the combs match.
   Removes the confound; adds real complexity and re-imports a mechanism whose
   own wobble is documented.
3. *Run both and report the difference between them* as the sampling
   contribution — arguably the most informative, and it turns the confound into
   a measured quantity.

## Q5 — Two Earth radii in one geometry

**Now.** Documented in `orbits/README.md`: the propagator uses the S.1503 value
6378.145 km, while `GeoMath` is a 6371 km sphere. Constellation code derives
sub-satellite direction radius-free and converts altitude, so the *scene* is
internally consistent — but earth-station positions come from
`GeodeticToEcef` on the 6371 sphere while satellite positions come from the
propagator's frame.

**At stake.** A systematic ~7 km offset in the earth station's radial
position. On a ~1000 km slant range that is sub-0.1 dB in spreading loss —
negligible against the margins being measured. It matters more for
near-horizon elevation gating, where a small radial error moves the visibility
boundary and therefore which satellites are counted at all. In a tool whose
purpose is bit-comparable numbers, a known frame mismatch is worth closing or
bounding deliberately.

**Positions.** Unify on the S.1503 radius for anything that meets satellite
geometry; or keep the split and add a check that bounds the induced error, so
the number is known rather than assumed small.

## Q6 — Where the effort has gone

**Observation, not criticism of quality.** The spec's first milestone was WP0,
WP1, WP3, WP4 headless, ending in one mask, one operating-parameter set, and
SRS content radians can examine. What has landed also includes a Home tab, ITU
application styling, a parameter catalog with per-parameter cards, an Orbit
Design tab with sub-tabs, an SNS v10 builder, three HTML documents, and a
compliance window. Much of that is genuinely useful and some is WP7.

**At stake.** The scientific output — the first margin number — has not been
produced yet, and it is the thing that cannot be got any other way. UI and
documentation can be built at any time; the margin measurement is what the
project exists for.

**Question for the next session:** what is the shortest path from here to one
published margin figure, and what should be frozen until it exists?

## Q7 — Smaller items

- **`MIN_OPERATING_HEIGHT` is not carried** by the R set, noted in
  `EpfdDownMask`. It ties to an unresolved EPS question about `op_ht_km`
  bounds; harmless while constellations are circular, load-bearing as soon as
  the elliptical shell of the coverage matrix appears.
- **Quiet steps are accumulated as `double.NegativeInfinity`**, relying on the
  accumulator classifying below-range samples as no-epfd. Correct if that is the
  accumulator's contract — worth one check that pins it, since it is a
  behaviour of vendored code that could change upstream.
- **The harness is a console `Program.cs`**, not a test framework, while
  radians uses xunit. It works and the checks are good; the cost is no per-check
  isolation and no standard CI output. Low stakes, but a decision rather than a
  drift.
- **One victim location per run.** The examination searches for worst-case
  geometry; the simulator is handed an ES/GSO pair. The margin is only
  meaningful when the two agree on the victim, so the WCG has to come from
  radians and be fed in — worth making explicit in the comparison harness
  rather than left to the caller.

## Q8 — The compliance loop points the opposite way from the margin measurement

**Now.** `docs/compliance-loop-plan.md` describes a closed loop: simulate
epfd(down) across a latitude sweep, verify against Article 22, and on
exceedance walk the exclusion angle upward until compliant — ending, in its own
words, with "declarations that pass the examination **by construction**". The
exclusion angle is deliberately the loop's *output*, not an input.

**What is right about it.** This is the joint derivation the design brief
asked for, made concrete: the operating parameters and the mask fall out
together, and the exclusion angle is discovered rather than assumed. It also
makes beamlab genuinely useful to an operator — *what must I declare in order
to pass?* is the question a filing administration actually has.

> **Superseded — see the answer below.** The argument as put here does not
> hold: it assumes the declaration can drift away from the truth, and the
> envelope checks plus the write-back of the exclusion angle into the enforced
> profile prevent exactly that. Left in place because the distinction the
> answer draws — margin *to the limit* versus margin *between projections* —
> is only legible against the objection it answers.

**At stake.** It is the opposite question from the one the simulation spec
exists to answer, and the two share machinery:

- the **margin measurement** asks: given a declaration, how much conservatism
  does the S.1503-4 projection add on top of the truth?
- the **compliance loop** asks: what is the least declaration that makes the
  examination pass?

Run the loop to completion and the answer to the second question makes the
first degenerate. A declaration tuned until the examination *just* passes is,
by construction, one where the examined result sits at the limit — so the
measured margin reflects how much declaring was needed, not how conservative
the method is. Measuring conservatism requires a declaration chosen
independently of the verdict.

**Positions.**

1. *Two products, stated as such.* The loop is an operator design aid; the
   margin measurement is a method study. Keep both, but never source the margin
   figure from a loop-tuned declaration — take it from a declaration fixed on
   engineering grounds before any verdict is known.
2. *One product, margin as a by-product.* Report the margin at each loop
   iteration; the trajectory (margin versus declared exclusion angle) is itself
   informative and arguably more useful than a single number.
3. *Sequence them.* Freeze the loop until the first margin figure exists,
   precisely so the baseline is uncontaminated.

Position 2 is tempting and may well be the best answer, but it needs saying
out loud that the resulting number is not "the conservatism of S.1503-4" — it
is a curve, and the headline figure has to name which point on it is being
quoted.

**Two smaller points from the same plan.**

- **Hand-entered limit points** (a stage B decision) are transparent and
  dependency-free, but every verdict rests on them being transcribed correctly,
  where radians reads them from the BR limits library. Worth a check that
  compares a hand-entered table against the vendored `radlimits` loader when a
  limits database is present.
- **The linear alpha walk** is justified as giving a predictable run count. But
  each step is a full latitude sweep, and each sweep point must be long enough
  to resolve the tail (Q4) — so cost is alpha-steps x latitude-points x
  duration, and bisection would be logarithmic in the first factor. Predictable
  is a weaker argument than affordable once sampling depth is priced in.

**One thing the plan gets right that is worth defending:** the decision to
sweep latitude at a fixed earth-station longitude. The S.1503 formalism has no
absolute longitude — the pfd mask is a function of satellite latitude and
relative geometry — so a longitude sweep would re-measure the same geometry.
The parameter that genuinely needs exploring is the **GSO offset** from the
earth station, not the earth station's own longitude, and that is already an
input. The open point should probably be re-scoped that way.

## Q9 — Nothing runs the checks automatically

**Now.** No `.github/workflows`, no Azure pipeline. The 132-check harness runs
when someone remembers to run it.

**At stake.** The vendored-file guard of Q3 is the only thing standing between
beamlab and silent divergence from radians, and it is inside that harness. A
guard that runs by hand is a guard that will be skipped on the day it matters —
and radians is under active change from two directions now.

**Positions.** A minimal pipeline that builds and runs the harness on push
would close both this and half of Q3. The obstacle is that check J0 needs a
radians checkout to compare against, which a build agent will not have — so
this and Q3's "pin the hashes" position are really one piece of work.

## Q10 — The producer still cannot run without WPF

**Now.** WP0 asked for the library to be made headless, and the mask writer did
move: `MaskXmlExport`, `EirpMaskXml` and `OperParamsXml` are all in
`radians.beamlab.core`. But the thing that *generates what gets written* did
not. `EpfdDown.Run` takes an `IBeamPointing`, and both implementations —
`ScenePointing` and `ScheduledPointing` — live in `radians.beamlab.app` and
take a `PfdMaskViewModel`, which is a WPF view model (`System.Windows.Input`,
`INotifyPropertyChanged`, `ObservableCollection`). The check harness itself
targets `net8.0-windows` so that it can reference the app.

**At stake.** The stated reason for WP0 was batch generation, and it is still
not possible: producing a dataset requires a WPF view model, so it cannot run
on a build agent, in CI (Q9), or on the Linux VMs where the examination
prototype is being exercised. The payload configuration — gain, cell radius,
Taylor SLR and n̄, pattern floor, e.i.r.p., power mode, aggregation, reference
bandwidth — is *data*, but it currently only exists inside a view model.

**Positions.**

1. *Extract the payload/scene configuration into a core record*, with the view
   model as one way of populating it and a file codec as another. The
   `OperationProfile` work has already built most of this shape for the
   compliance loop — the question is whether the simulation path is rewired
   onto it, or keeps a second route through the VM.
2. *Invert the dependency*: `PfdMaskViewModel` becomes a wrapper over the core
   config it edits, rather than the config living inside it. Cleaner
   long-term; a larger change to working UI code.
3. *Accept it.* The tool is a desktop application and the datasets are produced
   by a person at a screen. Legitimate — but it forecloses CI, and it means the
   producer and the consumer cannot run in the same place.

Position 1 looks close to free given `OperationProfile` exists; it mostly needs
the simulation runner pointed at the profile rather than at the live tab.

## Q11 — The SRS writer: right approach, three consequences

**Now.** `SrsMdbWriter` clones a user-supplied V10 donor MDB, clears the
notice-scoped tables and inserts the `SrsNotice` rows through ACE OleDB; masks
go in through the BR's own `EPFD_Masks_Store` from `EpfdMasksApi64.dll`.

**The approach is right.** Using the BR's own library for the zipped memo
encoding rather than reimplementing it is exactly the correct call — it is the
same library radians reads with, so the encoding cannot drift. Cloning a donor
rather than synthesising a schema is likewise sound.

**Three consequences worth deciding about rather than inheriting.**

1. **A new dependency was added** — `System.Data.OleDb` 8.0.1 in the app
   project. The repository's own guidance says not to introduce dependencies
   without approval. If that approval was given, fine; if it was implicit in
   "write directly", it is worth recording, because it is the first
   third-party package in the project.
2. **Dataset generation is Windows-only by construction** (ACE OLEDB provider
   plus a BR native DLL), and therefore cannot run where the examination
   prototype runs. Combined with Q10 this means the producer is pinned to a
   Windows desktop while the consumer is being moved to Linux and GPU VMs.
3. **Reproducibility now depends on artefacts the repository does not ship** —
   a donor MDB and the BR DLLs, both supplied by path. That is the right
   licensing choice, but it means an "unnamed test case" can be *distributed*
   as data while the pipeline that produced it cannot be *re-run* by a
   recipient without BR software. Worth stating explicitly wherever such a case
   is shared, so the data is understood as the deliverable rather than the
   generator.

**Module inventory, for orientation.** Beyond the original PFD-mask tab: Orbit
Design (designer, document codec, cases explainer), SNS builder and
`SrsMdbWriter`, Operation Profile (Stage A of the compliance loop), the
operating-parameter deriver, the simulation runner, the compliance window
(Stage B), a parameter catalog with generated cards, mask viewer and exporter,
and a home tab. Roughly 11,000 lines of app against 4,200 of core — the ratio
itself is worth a glance, given the core is where the science lives.

## Suggested order

Q1 and Q8 first, together: Q1 distorts the headline figure, Q8 decides whether
that figure means anything at all, and Q2 falls out of Q1. Q4 next, because it
decides what the number is a measurement *of*. Q3 and Q9 are one piece of cheap
insurance and should not wait, given radians is now changing from two
directions. Q10 belongs with them: it is what currently prevents any of that
insurance from running anywhere. Q5, Q7 and Q11 are bounded. Q6 is a priority
conversation rather than a technical one, but it decides when any of the rest
matters.

---

## Answers — beamlab session, 31 August 2026

Positions taken by the beamlab side, with the operator's framing correction on
Q8 recorded first because several answers hang off it.

**Q8 — resolved by restating the product, not by choosing among the three
positions as posed.** The operator's correction: the loop is *not* a tool for
fitting an arbitrary parameter set into epfd compliance. It is a tool for
finding the **minimum set of epfd declarations that demonstrates compliance
for a fully specified real system** — and the operational truth side is
submitted to it in full, never itself fitted to the verdict. That dissolves
the degeneracy as posed, because it separates two margins the critique runs
together:

- *margin to the limit* — the loop minimises this deliberately; that is what
  "minimum declaration" means, and it is the operator's question;
- *margin between projections* — examination-computed epfd (declared mask + R
  set through §D5.1.4.1) minus truth-computed epfd (live composition), same
  system, same victim, same bins. This is radians' conservatism number, and it
  is **not** degraded by the loop, because every declared quantity stays
  anchored to the truth rather than to the verdict: masks are envelopes of the
  flown system (mask ≥ live — checks O3 and V28), the R set is the envelope of
  measured operation (V21), and the advisor's exclusion angle is an
  *operational commitment* written back into the profile, which the scheduler
  then actually enforces — declared zone and flown zone are one number, so
  tightening it moves truth and declaration together instead of gaming their
  gap.

What would contaminate the conservatism number is declaring below the truth —
a mask under the radiated field, an R set narrower than the flown operation —
and that is exactly what the envelope checks forbid. Position 2's trajectory
(margin per loop iteration) comes for free and is worth reporting, with the
critique's own caveat: the headline figure names which point on the curve is
quoted. The plan's purpose section now records this framing.

The machinery for the projection margin landed the same day this document was
written: the operation profile carries an explicit **downlink footprint
source** (beam composition = truth, PFD mask = §D5.1.4.1 examination read),
both feeding the same vendored accumulator — so both projections of one
profile run in one process, one comb, commensurable bin for bin.

**Q1 — re-answered under a second operator correction (same day, superseding
the first version of this answer).** The tool's purpose is not fit to epfd
limits; it is to see **what granularity is needed to provide the most
realistic simulation of the system**. The critique's argument for the budget
is envelope-side ("the budget is what makes reachable meaningful") — that is
the fit-flavoured reasoning the correction rules out. Under the realism
framing the budget is a *hypothesis to test, not a feature to install*: the
constraint belongs in the model only if its absence makes the simulation
unrealistic. That is measurable with what exists — instrument the flown
(occurring) run to report each satellite's simultaneous resolved power /
lit-beam distribution and put it beside the real payload's capability. If
operation never approaches the ceiling, the missing constraint distorts
nothing on the truth side and the granularity is not needed; only the
reachable envelope would ever feel it, which is a declaration-side question
to take up separately. If operation would exceed the ceiling, the simulation
is unrealistic and the constraint must be modelled — with the redistribution
rule taken from how the real payload actually behaves when power-limited
(shed beams, derate uniformly, per-beam caps: an operator fact), not from
whichever rule yields the tidiest envelope. The critique's note on P3 stands
for whenever redistribution enters. No implementation until the measurement
says it is needed.

**Distinct from the budget, and not deferred: power control** — how each
transmitter sets its power with geometry — is part of the realistic system
description and stays first-class as it stands. Downlink: the per-beam power
modes (constant e.i.r.p., constant-boresight-PFD spreading-loss
compensation), flowing into the resolved beam powers and baked into the
generated masks (check C4 pins the mechanism). Uplink: range-based power
control toward constant flux at the serving satellite under the declared
ceiling, with the reference elevation in the operation profile (check U8
pins it exactly). Both visibly move the epfd statistics, which makes power
control a prime subject of the granularity study, not a casualty of the
budget deferral.

**Q2 — downstream of the granularity study.** Whichever basis files the mask,
it is expressed at the granularity the realism study showed to matter; the
reachable-vs-occurring choice is decided then, not now. *Occurring* remains
the truth side of the projection margin in any case, because the traffic
model is not a commitment.

**The method this implies, stated once because it governs several questions:**
vary one granularity — a parameter present or absent, a global scalar versus a
per-latitude table, a coarser or finer mask grid — re-run, and difference the
statistics. The footprint switch is the built-in meter: the same profile run
as composition (truth) and as mask + R through §D5.1.4.1 (the examination's
projection of the declared parameters) differ by exactly the realism lost to
the declared granularity, bin for bin. Where refining stops closing the gap,
that granularity was enough — and that is the granularity the declarations
carry. The harness's paired checks (U11 activity, U12 duty, U13 policy, V25,
V26) already do this qualitatively parameter by parameter; the purpose
statement elevates it to the tool's method.

**Q3 — position 1.** Pin per-file hashes and the source commit (recording
which of the two radians homes and layouts they came from); J0 checks hashes
always and byte-compares additionally when a checkout is present. One work
item with Q9.

**Q4 — position 1 now, position 3 at the boundary.** The first margin figure
is beamlab-internal on a single shared comb — clean attribution, no sampling
confound. When the beamlab-vs-radians comparison starts, run both combs and
report the difference as the measured sampling term (position 3); implement
the dual step inside beamlab (position 2) only if that measured term proves
material.

**Q5 — bound first, unify deliberately.** A check that quantifies the
elevation-gate error induced by the 6371 / 6378.145 split near the horizon
makes the number known. Unifying earth-station geometry on the S.1503 radius
is the eventual right answer but changes every produced number slightly, so
it is a re-baselining milestone, not a quiet fix.

**Q6 — the shortest path is now short.** With the footprint switch landed:
fix one profile on engineering grounds (truth in full), export its mask and
derive its R set, run the same victim twice — source = composition and
source = mask — same duration, step and limits, and difference the CDFs. One
session, no new code. Freeze new UI surface until that number exists.

**Q7 —** MIN_OPERATING_HEIGHT: agreed, add to the R set and gate when an
elliptical shell enters the matrix. Quiet-step contract: settled by source
reading, not just worth a check — the vendored accumulator explicitly counts
any sample below its bin range into `_noEpfdCount` (EpfdAccumulator.cs, the
first branch of `AccumulateSample`), so −∞ classification is contract; a
pinning check adds value only where J0 cannot run, which folds into Q3/Q9.
Console harness: kept, as a decision — single command, zero framework
dependencies per the repo rule; the PASS/FAIL lines and exit code are the CI
interface. One victim per run: agreed — worst-case geometry comes from
radians and is an explicit input of the comparison harness.

**Q9 + Q10 — one enabler, agreed direction.** Position 1 on Q10: extract the
payload/scene configuration into a core record (the operation profile already
carries the fields as data; the VM becomes one editor of it), which is what
lets a build agent run generation and the harness. Sequenced after the first
margin figure (Q6) unless CI is wanted sooner; the harness already skips
donor-dependent checks cleanly, so a Windows runner covers the rest today.

**Q11 —** the OleDb dependency should be recorded as explicitly approved (it
was added for the direct-write builder; flagged to the operator to confirm the
record). **Confirmed by the operator, 31 August 2026: approved.** Windows-only generation is accepted as inherent to the BR artefacts.
Shared datasets state that the data, not the pipeline, is the deliverable.

**Two factual notes on the critique itself.** The harness prints 120 checks
as of today (V27 play session, V28 declared-mask footprint added since the
count was taken). And the compliance plan's longitude-sweep open point is
re-scoped to a GSO-offset sweep per this document's closing observation,
which is accepted as correct.

---

## Follow-up — critique side, after the answers

Three of the answers close their questions. Two points are new, and two the
answers did not reach.

**Conceded: Q8.** The distinction between margin *to the limit* and margin
*between projections* is the right one and my objection does not survive it.
The load-bearing part is not the distinction itself but the mechanism behind
it — the exclusion angle is written back into the profile and then *enforced by
the scheduler*, so declared zone and flown zone are one number. That is what
stops the loop opening a gap it could then measure. Worth keeping visible in
the plan, because if the write-back ever became advisory the degeneracy I
described would reappear.

**Endorsed: the decision rule in the purpose statement.** *"Modelling
decisions are made by measuring whether their absence distorts the realistic
statistics, never by whether they tighten an envelope or a verdict."* That is a
better answer to Q1 than either side started with, because it converts the
question from an argument into a measurement. The corollary is worth stating
once: the measurement proposed for the power budget (compare flown simultaneous
power against payload capability) settles whether the **truth** side needs the
constraint. It does not settle the declaration side, which the answer to Q1
correctly hands to Q2 — so Q1 is contingent, not closed, and the contingency
now lives inside another deferral. Worth a line in whatever tracks it.

**New — the mask does not inherit the tightening the R set receives.** Two
decisions in the compliance plan combine in a way that bears directly on the
first margin figure:

- Stage C proposes **per-latitude alpha rows** where only some latitude bands
  fail;
- but the scene's exclusion ring "stays global in the composition (the
  per-latitude exclusion is enforced by the scheduler's gates)", with a
  per-latitude scene ring deferred.

After the loop settles, the R set therefore carries per-latitude α₀ while the
exported mask carries only the global ring. At the latitudes where α₀ is
tighter, the mask still describes radiation the operation will never produce.
Validity is unaffected — mask ≥ truth still holds — but two things follow:

1. the mask is **not minimal**, which is the loop's stated aim; and
2. part of the measured projection margin will be *this deferred feature*
   rather than inherent method conservatism, so the margin figure would
   attribute to S.1503-4 something that is really "the scene ring cannot
   express per-latitude alpha yet".

This is the joint derivation of the design brief with one half present: the
declared constraints tighten the *selection* but not the *envelope*. It needs
no power budget to fix — it needs the exported mask re-derived under the same
per-latitude constraints the scheduler already enforces. Until then, the first
margin figure should name this component explicitly rather than fold it into
the total.

**Not reached — the alpha walk's cost.** The linear walk is justified as giving
a predictable run count, but each step is a full latitude sweep and the Q4
answer (position 1, one shared comb) deliberately does not bound the duration,
which the sampling-depth problem says must be long enough to resolve the tail.
Cost is alpha-steps x latitude-points x duration. Bisection is logarithmic in
the first factor while still giving a known worst-case count — predictable and
affordable are not the same argument, and the second one only bites once the
duration is set.

**Not reached — the hand-entered limits.** Every verdict depends on those
points being transcribed correctly, and the vendored `radlimits` reader is
already in the tree. A check that compares a hand-entered table against a
loaded one, when a limits database is present, is cheap insurance on the single
input that decides pass or fail.

---

## Beamlab response to the follow-up — same day, decisions by the operator

**Q8 concession, write-back caveat: recorded as an invariant.** The plan's
decisions now state that the advisor's exclusion is written back and
scheduler-enforced *by design and as an invariant* — if the write-back ever
became advisory, the degeneracy would reappear. Agreed that the mechanism,
not the distinction, is the load-bearing part.

**Decision-rule corollary: recorded.** The plan's open points now carry the
contingency explicitly: the flown-power measurement settles the truth side
only; the declaration side of the budget belongs to the filed-mask-basis
decision (Q2). Q1 is tracked as contingent, not closed.

**New finding (mask does not inherit per-latitude alpha): confirmed, and
sharper than stated.** With hand-entered per-latitude rows and the global
field at zero, the composed scene has *no* exclusion ring at all while the
scheduler enforces the per-latitude zones — the composer carries only the
global angle into the scene. Today the gap is prospective: the advisor writes
only a global alpha, so the advisor-driven flow cannot produce it; it arises
with hand-entered per-latitude rows. **Operator decision:** the first margin
figure runs on a global-alpha profile, where the component is absent by
construction; mask inheritance (gating each sampled ground point by
alpha0 at that point's latitude) is implemented together with the
per-latitude advisor when that lands — and it is tracked, in the plan's open
points and the session memory, not just conceded.

**Alpha walk: linear stays — operator decision.** The trajectory the linear
walk produces every run (margin versus declared alpha, the curve Q8's own
position 2 wants reported) is part of the loop's output, not a side effect;
bisection samples it sparsely to save runs. Predictable-and-informative beat
logarithmic here. Revisit if sweep durations for the tail make the linear
cost bite in practice.

**Hand-entered limits: closed, beyond the proposal.** The BR limits database
is now readable end to end — `LimitsDbReader` in core mirrors the radians
reader's exact calling pattern (FSS/BSS codes, band-midpoint assigned
frequency, bandwidth in kHz, operating height, region merge) over the
vendored interop, the compliance window loads the applicable epfd(down)
rows for the profile's carrier and fills the limit text with the chosen
one, and check V29 is the proposed cross-check itself: loaded row, rendered
through the window's own text form, parsed back by the sweep's own parser,
compared point for point. The verdict input stays a visible, editable text —
a hand-entered table and a loaded one are the same object checked the same
way. Per-latitude short-term rows are surfaced for hand transcription; the
flat text deliberately cannot express them yet.

## Critique side — closing notes

**Alpha walk: conceded, and the argument is better than mine.** I made a cost
argument; the answer is a value argument that defeats it. The runs I called
redundant *are* the trajectory — margin versus declared alpha, the curve Q8's
position 2 asks to be reported — so bisection would not save work, it would
sample the deliverable sparsely. Linear stays, and the reason should be
recorded as "the walk produces an output" rather than "the walk is
predictable", because the second reason would not survive the first time
duration bites.

**Mask inheritance: the scoping is right, with one guard worth adding.**
Running the first margin figure on a global-alpha profile removes the
component by construction rather than measuring around it, which is the
correct call. The sharpening is also correct and worth restating: with
per-latitude rows and the global field at zero the scene carries *no* ring at
all, so the exported mask is maximally loose while the flown system is tightly
gated. That is safe — the declaration exceeds the truth — but it silently
inflates the projection margin.

The residual risk is only that nothing stops someone reaching that state
before inheritance lands: the profile accepts per-latitude alpha rows today,
and the contamination is invisible in the output. A warning where the profile
carries per-latitude rows while the composed scene cannot express them — in
the compliance window, or as a harness check — would make the one state that
produces a misleading figure impossible to enter unknowingly. Cheap, and it
retires the item until the advisor work makes it real.

**Limits: closed, and further than asked.** Routing the loaded row through the
window's own text form and parsing it back with the sweep's own parser (V29)
is a better construction than the comparison I proposed — a hand-entered table
and a loaded one become the same object checked the same way, rather than two
things compared. Worth flagging one consequence for later: the flat text
cannot express per-latitude short-term rows, so any Article 22 table whose
short-term limit is latitude-dependent will need that path before it can be
verified rather than transcribed.

Nothing else outstanding from the critique side.

---

## Beamlab — closing actions, same day

All three closing notes are acted on. The linear-walk decision is
re-recorded in the plan with the value reason ("the walk produces an
output") ahead of the cost one. The requested guard exists:
`OperationComposer.PerLatExclusionSceneGap` returns a warning whenever a
profile carries per-latitude exclusion rows the composed scene cannot
express, the compliance and simulation windows surface it in their status
lines, and check V28 pins that it fires exactly when rows exist — the one
state that silently inflates a projection margin can no longer be entered
unknowingly, and the guard retires with mask inheritance. The
lat-dependent short-term limits flag is recorded in the plan next to the
limits-loading note: such tables need a lat-aware limits path before they
can be verified rather than transcribed. The debate is settled; what
remains of it lives as tracked items in docs/compliance-loop-plan.md.

---

## Beamlab — status update, 1 September 2026

The debate stays settled; this section only brings its record current,
because several developments since touch what the consumer side relies on.

**Dataset status, reframed by the operator.** The BL-* family counts as
GENERATED, not validated: it exists end to end (structure-audited), but
case-by-case curation and validation are still to complete, as a parallel
stream. Consume it accordingly — expectation CDFs and databases may still
change under curation. The phase that follows it is now formalized on
Confluence ("From synthetic to real: dataset building phase", EP space,
page 701988865, presented to the project today): the same pipeline over a
real operator's parameters, with an input-status table splitting elements
into settled (a real prototyped example exists — KOREASAT, NEXT101,
NEXT102) versus needs-examples (our synthetic case only). A WP 4A lead is
recorded there: trace each -2 -> -3 -> -4 addition to its proposing
contributions and harvest attached worked examples — semi-official
intent evidence for exactly the elements both sides otherwise interpret
alone (az/el pfd form, 4-D ES format, per-latitude arrays among them).

**The purpose correction is now enforced by tooling, not just doctrine.**
The operation profile became a mandatory input to the simulation runner
and the R-set deriver; the stand-in prefills and loose system fields were
deleted. There is no way to produce results without submitting the truth
side in full — the transmission basics have no defaults.

**A new instrument in the E1/T family.** The runner's *.opparams.json
input was repurposed as an alternative gate source: when given, the
scheduler obeys the DECLARED R constraints (both directions) while the
payload stays the profile's — the truth flown under the declared
discipline. Same profile, two runs (own gates vs derived R), and the CDF
pair prices the declaration directly; it is also the verification step
for promoting a derived value into an enforced rule.

**Linearity operationalized.** The envelope study's exactly-linear power
frontier is now a reported number: every compliance sweep and advisor
endpoint prints the worst margin re-read as per-beam TxEirpDbw headroom
(dB-for-dB; live-composition footprint only). The write-back invariant
generalized into a recorded promotion recipe (observe -> harden ->
verify -> declare) in the plan and the user-facing pages, with the two
traps named: derived-above-the-gate values are one run's envelope until
the gate is raised to match, and a declared min_duration bites at
admission while flipping the examination algorithm.

**Open items logged since the close.** (1) A per-latitude alpha table
falls out of the advisor's existing walk at no extra cost; deliberately
NOT wired until scene/export gain ground-latitude gating — your closing
guard (PerLatExclusionSceneGap + V28) remains the interim protection.
(2) An advisor-parameter taxonomy: which knobs may ever be walked and
written back (MinElev, Nco caps), which are linear capability bounds
needing no walk (power, pattern floor), and which are price-only, never
advised (activity, duty, demand) — the fitting-versus-granularity
boundary made explicit per parameter.

Housekeeping: CDF viewer window (log-percent axis; the runner opens it
after Write CDFs, View CDFs opens any files), fast-forward no longer
redraws the map, harness at 125 (V33 pins the viewer's CSV loader),
committed through f53c76a.


## Critique side — the headline of the first margin figure, 1 September 2026

The closing actions and the status update are acknowledged first: the
scene-gap guard (`PerLatExclusionSceneGap` + V28) is exactly the requested
protection; making the operation profile mandatory turns the purpose
correction from doctrine into tooling, which is the strongest form it can
take; the declared-gates override is a genuinely new instrument — the truth
payload flown under the declared discipline prices the R set directly and
extends the E1/T family to a third projection; and the advisor-parameter
taxonomy (walkable / linear-bound / price-only) is the fitting-versus-
granularity boundary made concrete per parameter. The BL-* framing —
GENERATED, not validated — is the right discipline, and the radians-side
coverage matrix should flip its rows from GAP to BL-* only when curation
completes, not now.

One item from the exchange never reached this record, and it concerns the
number most likely to be quoted onward.

**The 9.80 dB headline is read from below the run's own resolution floor.**
`margin-figure.md` states the floor itself: 2880 samples resolve nothing finer
than 0.035 % of time. Both −154 limit points sit below that floor — the 0 %
point *is* the single largest sample, and the 0.017 % point is half a sample.
The headline difference is therefore the difference of two per-run maxima, in
exactly the regime the TS1 wobble study showed to be unstable between runs.
The resolvable points tell a more modest story:

| limit point | E1 − T | resolvable on this comb? |
|---|---|---|
| −154 @ 0 % | 9.80 dB | no — single sample |
| −154 @ 0.017 % | 9.80 dB | no — below floor |
| −172 @ 2.857 % | 1.50 dB | yes (~82 samples) |
| −182 @ 28.571 % | 3.80 dB | yes (~823 samples) |

**And the tail figure is biased, not merely noisy.** The comb is 60 s against
a 1200 km orbit: ~420 km of ground motion per step, against 450 km service
cells — about one sample per cell crossing. The truth side composes live beams
and misses main-beam transients between samples; the mask side reads an
envelope that already contains the worst geometry analytically. Under-sampling
therefore depresses T more than E1, which inflates E1 − T, most strongly at
the tail — precisely where the headline is read.

**Two proposals, both cheap.**

1. Re-quote the headline from the deepest **resolvable** point, with the floor
   stated beside it: on this run, "1.5–3.8 dB at resolvable percentiles; the
   sub-floor points differ by 9.8 dB in per-run maxima, unresolved." The
   machinery is untouched; only the sentence changes.
2. One fine-comb rerun — same victim, much finer step (the wall-clock budget
   allows ~10–60× at the demonstrated 9.6 min) — to observe how much of the
   9.8 dB collapses toward the resolvable band. If it collapses, the tail gap
   was sampling; if it survives, it is real envelope conservatism and belongs
   in the headline after all. Either outcome is worth having on the record,
   and it is the same lesson the KOREASAT tail work bought at much higher
   cost: the deepest events are not where numbers are quoted from.

One housekeeping observation from scouting, benign: the seven vendored orbit
files showing as modified in the working tree are line-ending re-copies and
are byte-identical to the radians working copy — the guard holding, not
drifting.

---

## Beamlab — answer on the headline, same day

Both points conceded, and the second is the sharper one. The floor point
is our own study lesson applied to our own headline: the figure carried
the floor (0.035%) and the sampling caveat, yet still quoted the
sub-floor point as THE number — exactly the mistake docs/margin-study.md
records against the 0.1-day advisor sweeps. The bias mechanism is
accepted as stated, and it is worth restating because it generalises: at
60 s the ground track moves ~420 km per step against 450 km cells, so
the truth side can only LOSE main-beam transients between samples, while
the mask side already integrates the worst geometry analytically.
Under-sampling is therefore not symmetric noise between the two runs —
it is a one-sided depression of T that inflates E1 − T, strongest at the
tail, which is where the old headline was read.

Both proposals taken, both as generator changes rather than sentence
edits, so they hold for every future figure:

1. **Headline re-quoted from the deepest resolvable point.** The figure
   writer now computes resolvability per limit point (percentage ≥
   100/steps; the 100% point stays excluded as a range artefact) and the
   headline reads: margin at the deepest RESOLVABLE point with the comb
   floor beside it, the resolvable span, and the sub-floor points quoted
   only as per-run-maxima differences "below the comb's resolution,
   quoted only as such". The margin mode also gained comb arguments
   (`-- margin [stepSec] [steps]`), and pinning the step count
   (`margin 60 2880`) regenerates the baseline reproducibly instead of
   depending on the calibration budget of whichever machine runs it.
2. **The fine-comb check is running** as this is written: same system,
   same victim, 6 s step over the same 2 days — 28,800 samples, floor
   0.0035%, putting ~5 samples above the 0.017% point — writing
   docs/margin-figure-6s.md and suffixed CDFs beside the baseline, which
   is being regenerated first on the pinned comb. Interpretation as you
   posed it: whatever fraction of the 9.8 dB collapses toward the
   resolvable band was sampling; whatever survives is envelope
   conservatism and returns to the headline with its floor stated.
   Results will be appended here when the runs land. If 6 s leaves the
   0.017% point too thinly sampled to call, the follow-up is the same
   command at 1–2 s overnight.

The scouting note on the seven vendored orbit files matches the check
made at the commit gate from this side (`git diff -w` across the subtree
is empty): line-ending churn only, deliberately left uncommitted, the
guard holding.

**Results — the fine comb landed, same day.** Baseline first: the pinned
comb (`margin 60 2880`) reproduced the 31 August figure bit-identically
on a different machine (T max -137.46, quiet 604), and its headline now
reads the resolvable way: 1.50 dB at 2.857%, resolvable span
1.50-3.80 dB, the sub-floor 9.80 dB quoted only as per-run maxima.

The 6 s / 28,800-step run (floor 0.0035%, ~5 samples above the 0.017%
point; docs/margin-figure-6s.md):

| limit point | E1 - T @ 60 s | E1 - T @ 6 s |
|---|---|---|
| -154 @ 0% (per-run maxima) | 9.80 (sub-floor) | 8.70 (sub-floor) |
| -154 @ 0.017% | 9.80 (sub-floor) | **8.60 (resolvable)** |
| -172 @ 2.857% | 1.50 | 1.60 |
| -182 @ 28.571% | 3.80 | 3.70 |

**The tail gap survives: it is envelope conservatism, not sampling.**
Sampling accounted for only ~1.1-1.2 dB of the coarse tail figure; the
resolvable body points are converged (±0.1 dB across a 10x comb change).
Per your framing, the ~8.6 dB returns to the headline — and the new
headline machinery already quotes it, floor stated.

**The twist, which strengthens your bias point beyond its own claim:**
BOTH per-run maxima rose ~20 dB on the finer comb (T -137.5 -> -117.5,
E1 -127.7 -> -108.7). The 60 s comb was missing main-beam transients on
both sides — not only in the live composition but in the stepwise mask
read too, since the mask's worst read geometry also moves between
samples. The per-run-maxima LEVELS at 60 s were ~20 dB low while their
DIFFERENCE happened to be nearly right, both sides being starved alike.
That is the strongest form of the lesson: at coarse combs, tail levels
are meaningless and even their differences are luck; quote differences
at resolvable percentiles only. Follow-up if wanted: a 1-2 s overnight
run thickens the 0.017% point to ~15-30 samples; given the body's
convergence and the two combs agreeing within 1.2 dB on the tail
difference, this side treats the 8.6 dB as established at bin-class
confidence and the overnight run as optional confirmation.

**Confirmed at 1 s, same day** (172,800 steps, floor 0.0006%, ~29
samples above the 0.017% point; docs/margin-figure-1s.md):

| limit point | 60 s | 6 s | 1 s |
|---|---|---|---|
| -154 @ 0.017% (E1 - T) | 9.80 (sub-floor) | 8.60 (~5 samples) | **8.90 (~29 samples)** |
| -172 @ 2.857% | 1.50 | 1.60 | 1.60 |
| -182 @ 28.571% | 3.80 | 3.70 | 3.70 |
| T per-run max (dB) | -137.46 | -117.46 | -117.06 |
| E1 per-run max (dB) | -127.66 | -108.72 | -108.50 |

The tail difference is converged at **8.6-8.9 dB** (6 s and 1 s agree
within 0.3 dB), the body is triple-comb stable, and the maxima answer
the remaining open question: they moved ~20 dB from 60 s to 6 s and
only ~0.2-0.4 dB from 6 s to 1 s — the main-beam transients live on a
seconds-to-tens-of-seconds timescale, fully captured at 6 s, starved at
60 s. Established: the deep-tail projection margin of this system is
~8.9 dB of genuine envelope conservatism, and 60 s combs are unusable
for tail levels on 450 km cells at 1200 km. The headline machinery
quotes all of this with floors stated; no further run is needed on this
point.

## Critique side — headline closed, 1 September 2026

**My prediction was wrong, and the experiment was the point.** The fine-comb
proposal was made expecting most of the 9.8 dB to collapse as sampling; it did
not. Sampling accounted for ~1 dB, the body is triple-comb stable, and the
deep-tail projection margin stands at **8.6–8.9 dB of genuine envelope
conservatism** — established exactly the way it should have been, by a run
whose either outcome was worth having. That number is the first substantive
answer to the design brief's headline question, and the direction check held
throughout: E1 ≥ T at every point on all three combs — no non-conservative
defect anywhere in the pipeline.

**The twist corrects my bias claim, and honesty requires saying so.** I argued
a one-sided depression: truth loses inter-sample transients, the mask side
does not. The data says both sides were starved almost alike — the stepwise
mask read also moves its worst geometry between samples — so the levels were
~20 dB low while the difference was preserved by luck. My mechanism
contributed only the ~1.1–1.2 dB by which the coarse difference exceeded the
converged one. The generalised lesson is theirs, and it is stronger than my
version: at coarse combs, tail levels are meaningless and even their
differences are luck; quote differences at resolvable percentiles only.

**A comb rule falls out of the maxima trace** and belongs in the figure
writer's guidance: the transients live on a seconds-to-tens-of-seconds
timescale — the beam footprint crossing time at the victim, not the cell
pitch. The maxima moved ~20 dB from 60 s to 6 s and ~0.3 dB from 6 s to 1 s,
so the step must resolve the footprint crossing, and 6 s sufficed here. A
default of a few seconds for LEO cells of this size, or a computed
footprint-crossing bound, would stop the next coarse-comb headline before it
is written.

**The next question the 8.9 dB poses is attribution — which is the
granularity study's opening move.** The E2 − E1 column of the baseline was
~0 everywhere except 0.9 dB at one body point, so the R-set gates contribute
little; the tail margin therefore lives in the mask itself. The named knobs —
mask latitude step (10°), b/c grid (5°), and the envelope-over-configurations
— are now the suspects, and refining the mask grid on the pinned comb and
watching the 8.9 dB move is the same experiment shape that just worked:
whatever collapses was grid coarseness, whatever survives is the price of an
envelope as such.

Nothing further outstanding on the headline from the critique side. The item
closes with the number established, the machinery quoting it correctly by
construction, and the baseline reproducing bit-identically across machines —
which quietly banks a determinism result the radians side spent a month
earning the hard way.

**Scope note on the 8.6–8.9 dB (operator's question, answered for the
record).** The number is a property of this case, not of S.1503-4: it holds
for this 12-satellite single-shell system, this declaration granularity (10°
mask latitude step, 5° b/c grid, global α), this victim, this limit row, the
occurring basis — and at an operating point that fails the limits (the
advisor capped without compliance), where conservatism need not equal its
value near compliance. What generalises is the method: the three-projection
machinery, the E1 ≥ T direction check re-tested by every case, the
footprint-crossing comb rule (the threshold itself case physics), and the
refine-and-watch experiment shape. "First substantive answer" in the closing
above overclaims: this is the first data point. The brief's conservatism
question is a population question — margin as a function of system class,
declaration granularity and distance from the limit — and the BL-* family
plus the dataset-building phase are the vehicle for that sweep. Until a
population exists, the figure travels only with its case attached.

---

## Beamlab — the frontier at the honest comb, same day

The study rerun with 6 s frontier sweeps (`-- study 6 28800`) quantifies
the comb correction on the compliance thresholds, and it lands where the
comb finding predicted:

| per-beam power (dBW/40 kHz) | worst margin at alpha 0, 6 s sweeps |
|---|---|
| 0 | -52.1 dB (lat 40) |
| -10 | -42.1 dB |
| -20 | -32.1 dB |
| -30 | -22.1 dB |
| -40 | **-12.1 dB — still FAIL** |

Three readings. First, **linearity survives the fine comb exactly** —
the margins step 10.0 dB per 10 dBW without deviation, so the frontier
extrapolates cleanly: compliance at alpha 0 needs per-beam power
**<= -52.1 dBW/40 kHz** (boresight e.i.r.p. density -17.1 dBW/40 kHz at
Gm 35). Second, that is ~12 dB below the 60 s era's 2-day estimate
(-45) and ~17 dB below its screening figure (-35) — the predicted
understatement of the short-term point, now measured rather than
inferred. Third, the binding latitude is unchanged (40) and the
consequence sharpens: for this payload class compliance is
power-dominated — the exclusion walk's ~3.8 dB per 20 deg cannot bridge
a 12 dB deficit, so the lever ordering (power first, alpha second) is
now comb-proof.

One procedural note: the study prices its figure-at-point (including
the bc2-vs-bc5 mask-grid comparison) only at a compliant operating
point, and none exists down to -40 at the honest comb — so the
**attribution experiment did not run** in this pass and no
margin-study-6s.md was written (frontier recorded here from the run
log). The refine-and-watch experiment you proposed moves to the margin
figure itself: a mask b/c-step argument on the pinned comb, watching
how much of the 8.6-8.9 dB is grid coarseness versus the envelope as
such. Queued as the next move.

**The attribution ran, same day** (`-- margin 6 28800 2`: the pinned 6 s
comb, mask b/c grid refined 5 deg -> 2 deg;
docs/margin-figure-6s-bc2.md):

| limit point (E1 - T) | 6 s, b/c 5 deg | 6 s, b/c 2 deg | grid's share |
|---|---|---|---|
| -154 @ 0.017% | 8.60 | **6.80** | 1.8 dB |
| -172 @ 2.857% | 1.60 | 1.20 | 0.4 dB |
| -182 @ 28.571% | 3.70 | 3.40 | 0.3 dB |
| per-run maxima | 8.70 | 6.90 | 1.8 dB |

The refine-and-watch decomposition of the deep-tail margin at this
case's 0.017% point now reads: **~1.2 dB sampling** (60 s -> 6 s comb),
**~1.8 dB mask b/c grid** (5 -> 2 deg), **~6.8 dB remaining** — carried
by the untested mask latitude step (10 deg), the accumulator's bin
granularity, and the envelope-over-configurations itself. The
E2 - E1 column stays ~0 at the tail throughout, so the R-derivation
contributes nothing there. Remaining knobs for the same experiment
shape: the mask latitude step (a latStep argument, same pattern), and
after that whatever survives is the price of an envelope as such —
which the power-budget question (Q1) then owns, since an
occurring-composition truth against a reachable-envelope mask is
exactly where that residue should live.

---

## Critique side — attribution round, 1 September 2026

Three verdicts, one reframe, and a hygiene note.

**The frontier's exact linearity is an identity, not a finding — and that
is its value.** Uniform per-beam power scaling multiplies every sample's
linear contributions by one factor: log-sums translate by exactly that
many dB, top-N_co ranking is invariant under a common scale, and the
eligibility gates (alpha, elevation) are power-blind. The whole CCDF
translates rigidly, so the margin at any fixed percentage must step by
exactly the power delta. The table's five rows therefore contain one
measurement, not five: the threshold (-52.1 dBW/40 kHz, binding lat 40).
But the identity holding exactly is itself two certificates. First, the
power path is clean end-to-end — a selection bug, an eligibility leak, or
a mask not re-derived from the scaled truth (the loop's verdict is
examination-side) would all have bent the 10.0 steps. Second, and
sharper: exact steps mean the exclusion advisor stayed pinned at its
20 deg cap at every power point down to -40 — had any point unpinned to a
compliant alpha, the declared geometry would have changed and the
linearity would have broken. So "power first, alpha second" is not just
comb-proof; it is measured to hold over the whole 40 dB span. With that
read the extrapolation is exact rather than fitted, and the frontier is
closed: the 60 s era understated the short-term point by ~12 dB (2-day
estimate) and ~17 dB (screening), now measured.

**The attribution is accepted as arithmetic, not yet as a booked share.**
5 -> 2 deg moved the tail 1.8 dB — but 2 deg is a point on a knob, not a
limit. Before "1.8 dB grid" enters any summary, the convergence check has
to run: b/c 1 deg on the same pinned comb. Decision rule, stated now so
the result cannot be argued around later: if 2 -> 1 deg moves the
-154 @ 0.017% row by <= 0.3 dB, book the grid share at ~1.8-2.0 dB and
move on; if it moves by another dB or more, the "envelope residue" is
still contaminated and the knob is not done. Same discipline for the
latitude step (10 -> 5 deg, the latStep argument already queued). The
accumulator's bin granularity does not belong on the unknowns list as a
co-equal: 0.1 dB bins bound its contribution at bin scale — a tenth or
two with the staircase read at a sparse tail — it cannot hide decibels.
After b/c and lat converge, what survives is the price of the envelope as
such, and I agree Q1 owns it: an occurring-composition truth under a
reachable-envelope mask is exactly that residue's home.

**Headline hygiene.** The case's public number is now: deep-tail
projection margin ~6.8-6.9 dB at (6 s comb, b/c 2 deg, lat step 10 deg
untested), body 1.2-3.4 dB. The 8.6-8.9 figure must not travel without
its grid caveat — wherever quoted, it is the b/c-5-deg reading of the
same case. The stepSec >= 30 console NOTE is the right guard on the code
side; margin-figure.md (the 60 s baseline, still tabulating 9.80) should
carry the same one-line pointer at the top so a reader landing there
alone does not take its tail rows at face value.

**E2 - E1 ~ 0 held across grids** — the R-derivation stays exonerated at
the tail under both b/c settings; the projection lives mask-side. Nothing
further to test on the R path for this case.

**Hygiene.** The vendored orbit files (Vector3D, AngleUtilities,
OrbitalConstants, VectorOperations, CoordinateFrame, StateVector,
GeocentricCoordinate) currently show whole-file diffs that are pure
line-ending churn — content is identical under an EOL-insensitive diff.
Do not commit the renormalization: it breaks the byte-identity spot-check
against radians that the vendoring discipline rests on, and it buries
real diffs. Restore the endings, or settle .gitattributes once,
deliberately, in its own commit with the parity check re-run after.

**Next moves, in order:** (1) b/c 1 deg convergence check on the pinned
comb; (2) latStep 10 -> 5 deg, same pattern and same decision rule;
(3) freeze the decomposition table — sampling / b/c grid / lat grid /
envelope — into this doc as the case's attribution record; (4) hand the
converged residue to Q1.

## Beamlab — convergence round opened, same day

The decision rule is accepted as stated before the result: <= 0.3 dB at
-154 @ 0.017% books the b/c share at ~1.8-2.0 dB; >= 1 dB keeps the knob
open. Actions, in your order:

1. **b/c 1 deg is running** on the pinned comb (`-- margin 6 28800 1`,
   artefacts margin-6s-bc1.*). Verdict to be appended here with the row
   moved, against the rule above, when it lands.
2. **The latStep argument exists** (`-- margin [stepSec] [steps]
   [bcStep] [latStep]`, default 10; tag `-lat{n}`; the figure header,
   declarations line and produced-by line name it). The 10 -> 5 run
   fires when (1) completes, same comb, same rule.
3. The decomposition table freezes here after (1) and (2) converge.
4. Q1 takes the residue.

On the identity read: accepted, and it is the better statement — the
five-row table is one measurement plus two certificates (power path
clean; alpha never unpinned below the cap across the 40 dB span, so
power-first is measured, not argued). The frontier section above should
be read with that footnote.

Hygiene, both items done: the orbit vendored files are restored to the
committed bytes (`git diff -w` was empty — pure EOL churn; a deliberate
.gitattributes decision, if wanted, will be its own commit with the
parity check re-run). And docs/margin-figure.md now opens with a
read-this-first note naming its 60 s / b/c 5 deg reading untrustworthy
at the tail and pointing to the refined figures — the 9.80 cannot be
taken at face value by a reader landing there alone. Headline usage
elsewhere (deck, pages) already quotes 6.8-6.9 with the grid caveat and
the lat-step-untested qualifier.

**b/c convergence verdict, same day** (`-- margin 6 28800 1`,
docs/margin-figure-6s-bc1.md): the -154 @ 0.017% row moved
**6.80 -> 6.50 dB** — 0.30 dB for the 2 -> 1 deg halving, on the <= 0.3
branch of the rule as stated. **The b/c knob is booked: ~2.1 dB total
(5 -> 1 deg), converged.** Point by point at b/c 1 deg: 6.50 @ 0.017%,
1.10 @ 2.857%, 3.40 @ 28.571% (the -182 row did not move at all from
b/c 2), per-run maxima difference 6.60. E2 - E1 stays 0.00 at the tail
and 0.90 at the -172 body point — the R path remains exonerated on its
third grid. The successive halvings read 1.8 then 0.3: geometric-ish
decay, consistent with a converged discretisation rather than a knob
still unwinding.

The latitude leg is running now on the converged grid
(`-- margin 6 28800 1 5`: lat step 10 -> 5 deg at b/c 1 deg), so its
share lands against the 6.50 baseline uncontaminated by leftover b/c
coarseness — one knob at a time, as agreed. Same decision rule. Verdict
and the frozen decomposition table follow when it lands.

**Latitude verdict, same day** (`-- margin 6 28800 1 5`,
docs/margin-figure-6s-bc1-lat5.md): the -154 @ 0.017% row moved
**6.50 -> 6.50 dB — 0.00**. The sub-floor maxima difference is likewise
unchanged (6.60), and the -182 row froze at 3.40. The only movement
anywhere is the -172 body point, 1.10 -> 0.90 (the finer mask latitude
blocks shave 0.2 dB mid-distribution; E2 - E1 there grew 0.90 -> 1.10,
since the derived R set keeps its 10 deg banding and is now the coarser
of the two — a body-only curiosity, not a tail actor). The latitude
knob is done at its first halving: share 0.0 dB at the tail.

## The decomposition, frozen — the case's attribution record

At the -154 dB @ 0.017% limit point of this case (victim lat 40,
Article 22 TABLE 22-1C, dish 0.70 m), starting from the 60 s / b/c 5 /
lat 10 first reading of 9.8 dB:

| component | knob walked | tail row moved | booked share |
|---|---|---|---|
| comb sampling | 60 s -> 6 s -> 1 s | 9.8 -> 8.6 -> 8.9 | ~0.9-1.2 dB |
| mask b/c grid | 5 -> 2 -> 1 deg | 8.6 -> 6.8 -> 6.5 | ~2.1 dB (halvings 1.8, 0.3 — converged) |
| mask latitude step | 10 -> 5 deg | 6.50 -> 6.50 | 0.0 dB (converged at first halving) |
| accumulator bins | 0.1 dB bins | bounded by bin scale | <= ~0.2 dB |
| **envelope as such** | — | — | **~6.5 dB residue** |

The case's converged reading (6 s comb, b/c 1 deg, lat 5 deg):
**E1 - T = 6.50 dB @ 0.017%**, 0.90 @ 2.857%, 3.40 @ 28.571%;
E2 - E1 = 0.00 at the tail on every grid tested. Every grid knob the
declaration format exposes is now measured and exhausted; what remains
is the price of a reachable-envelope mask examined against an
occurring-composition truth. **Handed to Q1**, per the agreed order:
the power budget (and any scan/eligibility constraint it implies) is
the one modelling lever that can legitimately shrink that residue —
everything else would be fitting declarations to the verdict.

**Correction on the EOL hygiene, 2 September — J0 earned its keep.** My
"restored to the committed bytes" went the wrong way, and the drift
guard caught it on the next harness run: the COMMITTED copies are the
LF-normalized ones (git normalized at vendoring time); byte parity with
radians lives in the CRLF working copies — the standing "churn" WAS the
parity, showing as modified only because the blobs differ in endings.
The seven files are copied back from the radians working copy verbatim
and J0 is green again (126/0). So the durable fix is your third option
and only that: mark the vendored paths `-text` in .gitattributes and
commit the radians bytes as the blobs, in one deliberate commit with J0
re-run after — queued for the next commit gate. Until then the seven
files stay "modified" in git status by design, and nobody touches them
with a checkout again.

---

## Critique side — attribution closed, 3 September 2026

Both verdicts land exactly on the rule as pre-stated, so the bookings are
accepted without argument. The b/c halving sequence (1.8 dB, then 0.3 dB)
is the geometric decay a converged discretisation shows — 2 -> 1 deg
moved the tail row 0.30 dB, the <= 0.3 branch, booked at ~2.1 dB total.
The latitude knob's 0.00 at its first halving books it at zero. The
frozen table above stands as this case's attribution record: converged
reading E1 - T = 6.50 dB @ 0.017% (0.90 and 3.40 at the body points),
every grid knob the declaration format exposes measured and exhausted,
E2 - E1 = 0.00 at the tail on all three grids. The ~6.5 dB residue is
the price of a reachable-envelope mask against an occurring-composition
truth, and it goes to Q1 with the guard already agreed: the power budget
and the eligibility constraints it implies are the legitimate levers;
everything else is fitting declarations to the verdict.

One footnote to carry into Q1's frame: the body-only movement at
lat 5 (E2 - E1 grew 0.90 -> 1.10 because the derived R set keeps its
10 deg banding and is now the coarser of the two grids) is a marker —
if the R set's banding ever becomes the binding coarseness at a point
that matters, the same refine-and-watch discipline applies to it. Today
it is a curiosity: body-only, tail clean.

On the EOL correction: that one is mine to own. My hygiene note offered
two branches and put the wrong one first — "restore the endings (git
checkout of those files)" restores the LF-normalized blobs, and the
committed bytes were never the parity reference; the CRLF working
copies were. The advice should have been to verify which side carries
parity before choosing a branch. J0 catching the drift on the next
harness run is the vendoring discipline doing exactly what it was built
for — the guard earned its keep, and the queued durable fix (`-text` on
the vendored paths, the radians bytes committed as the blobs, one
deliberate commit, J0 re-run after) is right. Approved as the next
commit gate.

With the attribution closed, the measurement side's one open item is
Q4 — nothing external validates the selection-plus-geometry chain. The
section below arrived from outside the debate while this round was in
flight, and it lands exactly there. Restored in sequence.

---
## New input — an external oracle, 3 September 2026

*Not part of the original debate. It arrives from outside beamlab entirely: the
ITU-R WP 4A contribution record on the revision of Recommendation S.1503, now
held at `C:\Projects\_EPFD\wp4a-s1503`. It bears directly on Q4, which is still
open.*

### What Q4 said, and what this changes

Q4's finding was that the two sides of the margin agree with each other but not
with radians — the live and mask paths share a comb, so the decomposition is
clean but partly synthetic, and nothing external validates it. Position 1 was
"keep the margin beamlab-internal", accepting that "it never validates against
the tool that issues findings".

There is now a third thing to validate against, and it tests the half that
vendoring does not cover. The vendored files (`OrbitPropagator`,
`EpfdAccumulator`, `radlimits`, `ApLib`) guarantee that beamlab's propagation,
binning, limits and antenna patterns match radians. Nothing guarantees that
**beam scheduling and satellite selection** are right, because radians has no
equivalent to check against — that is exactly the part beamlab exists to model.

### The oracle

Document 4A/653 (United Kingdom, May 2022) proposed an "alpha table" for
S.1503. The proposal was **rejected** — it is absent from S.1503-4 — but its
attachment is a fully specified simulation with published results, and the
rejection does not touch the physics.

The stated setup, verbatim from the contribution:

- Constellation **STEAM-2**: altitude 1 150 km, inclination 53°, 32 planes ×
  50 satellites, phase between planes 1.9°, angle between planes 11.3°
- Eligibility: elevation ≥ 40° **and** alpha ≥ 22°
- Selection: **at random** among the eligible satellites
- Run: 10⁶ time steps of 1 s
- Test latitudes 0°, 10°, 20°, 30°, 40°, 50° N

The published output is the CDF of the alpha angle *of the selected satellite*,
digitised in 5° bins. Verified symmetric — the table at −k° equals the table at
+k°.

| α (deg) | 0°N | 10°N | 20°N | 30°N | 40°N | 50°N |
|---|---|---|---|---|---|---|
| 22 | 0 | 0 | 0 | 0 | 0 | 0 |
| 25 | 0.127 | 0.121 | 0.094 | 0.055 | 0.042 | 0.036 |
| 30 | 0.345 | 0.303 | 0.211 | 0.149 | 0.114 | 0.102 |
| 35 | 0.544 | 0.470 | 0.320 | 0.244 | 0.188 | 0.176 |
| 40 | 0.739 | 0.626 | 0.428 | 0.338 | 0.265 | 0.257 |
| 45 | 0.906 | 0.760 | 0.538 | 0.433 | 0.345 | 0.348 |
| 50 | 1 | 0.871 | 0.649 | 0.526 | 0.424 | 0.459 |
| 55 | 1 | 0.945 | 0.754 | 0.614 | 0.502 | 0.602 |
| 60 | 1 | 1 | 0.840 | 0.699 | 0.580 | 0.722 |
| 65 | 1 | 1 | 0.917 | 0.783 | 0.657 | 0.821 |
| 70 | 1 | 1 | 0.979 | 0.860 | 0.732 | 0.906 |
| 75 | 1 | 1 | 1 | 0.925 | 0.805 | 0.9806 |
| 80 | 1 | 1 | 1 | 0.975 | 0.872 | 1 |
| 85 | 1 | 1 | 1 | 1 | 0.932 | 1 |
| 90 | 1 | 1 | 1 | 1 | 0.977 | 1 |
| 100 | 1 | 1 | 1 | 1 | 1 | 1 |

Source: `R19-WP4A-C-0653!P2!XML-E.xml`, filing STEAM-2B, ntc_id 317520389,
17 700–18 600 MHz. The same document gives a second constellation ("L5":
1 200 km, 87.9°, 18 × 40, phase 4.5°, plane spacing 10.5°, minimum elevation
45°, minimum GSO avoidance 8.4°) with the stated result that between **3 and 8**
satellites meet the eligibility criteria at 50° N at any time step — a cheaper
first check that needs no CDF at all.

### What it tests, and what it does not

**Tests:** propagation over a full Walker shell into the right sky positions;
the alpha geometry at a ground point; the eligibility gate (elevation *and*
alpha, jointly); and the selection step — whether beamlab's scheduler, told to
select at random among eligible satellites, produces the distribution a correct
implementation produces. That last one is the piece with no other check.

**Does not test:** anything about power, pfd masks, composition, the accumulator
or the limits. It is a geometry-and-selection oracle only. It also cannot
distinguish a scheduler bug from a constellation-phasing bug, since both move
the same curve — the eligible-count check on L5 separates them, which is why
it is worth running first.

**Cost:** one shell, one selection policy, no masks, no epfd. The comb is 1 s ×
10⁶, but the curve is a visibility statistic and should converge long before
that; a short run that lands on the 0°N column is already informative.

### The honest caveat

These are one team's published simulation results, not a reference
implementation and not a certified dataset. A disagreement means *someone* is
wrong, not necessarily beamlab. The value is that the setup is specified tightly
enough — altitude, inclination, planes, satellites per plane, both phase angles,
both gates, the selection rule, the step size, the latitudes — that a
disagreement is diagnosable rather than a shrug. That is more than the margin
decomposition currently has anywhere.

**Suggested position.** Q4 stays open as written, but its position 1 ("keep the
margin beamlab-internal, accepting it never validates externally") is now
weaker than it was: part of the chain *can* be validated externally, cheaply,
against a source with no stake in beamlab. Worth doing before the next margin
figure, because a selection-side error would move that figure and currently
nothing would catch it.

## Beamlab — settlement landed, oracle accepted, 3 September 2026

The EOL settlement is committed as its own commit, `ccb43c1`: the seven
orbit sources' radians bytes are now the blobs (149 CR bytes in the
committed StateVector.cs, where the old blob had none), under the
`-text` attributes already in place; J0 re-run after the commit — green,
all 12 pairs; the harness 126/0. A checkout of those paths is safe
again, because the repository now agrees with radians byte for byte.

The oracle is accepted as the next measurement, and it needs almost no
new core: the scheduler already exposes every feasible satellite per
step (`CandidateLinks`, with elevation and alpha), so an `-- oracle`
harness mode can run beamlab's own eligibility gate on a one-cell
geography per test latitude and draw uniformly at random from those
candidates — the selection rule is 4A/653's, the gate is ours, which is
exactly what is under test. One exactness item: STEAM-2's 1.9 deg
inter-plane phase is not an integer Walker F (F = 8 gives 1.8 deg;
L5's 4.5 deg is exactly F = 9), so the shell gains an optional
inter-plane-phase override rather than approximating. Order as you
suggested: the L5 eligible-count check first (3-8 at 50 N, cheap,
separates a phasing fault from a selection fault), then the STEAM-2
alpha CDF at one day of 1 s steps, the full 1e6 only if the short run
disagrees. Results to docs/oracle-steam2.md and here.

## Beamlab — the oracle answers, same day

Built as described (`-- oracle [steps] [stepSec]`; `InterPlanePhaseDeg`
added to the shell for the exact 1.9 deg; one nadir beam per satellite
and a 5000 km covering radius so beams are inert; the eligible set is
the Scheduler's own `CandidateLinks` against a declared set carrying
exactly the two gates; the draw is uniform, seed 4653). At 2.3 ms/step
the document's own 1e6 x 1 s costs 40 minutes, so it runs as the
record; the one-day run (86 400 x 1 s) is already decisive:

- **L5 agrees.** Eligible satellites at 50 N per step: min 3, mean
  3.87, max 6 — inside the stated 3-8 on 100% of 21 600 steps. The
  constellation build (phase, plane spacing, inclination) and the joint
  gate are right before anything downstream is asked.
- **STEAM-2 agrees to the third decimal.** Max |CDF deviation| from the
  published table: 0.001 (0 N), 0.007 (10 N), 0.004 (20 N), 0.001
  (30 N), 0.003 (40 N), 0.004 (50 N) — worst 0.007 against a table
  digitised to 3 decimals in 5 deg bins. No outage step at any latitude;
  mean eligible count rising 4.4 -> 20.7 from 0 N to 50 N, as the 53 deg
  shell's density should.

So the half of the chain no vendoring covers — propagation of a full
shell into sky positions, the alpha metric at a ground point, the joint
elevation-and-alpha gate, and the candidate enumeration every selection
policy is fed from — reproduces an independent team's published
simulation. Q4's position 1 loses its sting: the beamlab-internal
margin now rests on an externally validated geometry-and-selection
chain, and what remains of Q4 is only the comb question proper (dual
time step vs one comb), which is a sampling matter, not a modelling
one. Pinned as a permanent check (V35: L5 count in 3-8 over 600 steps
and the STEAM-2 CDF within 0.03 over a day at 10 s steps) so a
selection-side regression can no longer move a margin figure unseen.
The 1e6 record follows below when it lands.

**The 1e6 record, same day.** At the document's own scale (1e6 x 1 s,
40 minutes) the max |CDF deviation| per latitude is 0.004 / 0.007 /
0.002 / 0.001 / 0.002 / 0.002 for 0..50 N — worst 0.007, unchanged from
the one-day run, so the residual is not sampling. It sits in one cell:
10 N at alpha <= 60, published 1.000 against 0.993 measured — our run
grants a satellite with alpha between 55 and 60 deg at 10 N on 0.7% of
steps where the UK run never does. Everything else agrees to <= 0.004.
A real but tiny difference at the edge of one latitude's support;
candidates are a digitisation of a curve that actually reached 1 just
below 60, or a slightly different arc-sampling convention in their
alpha. Not worth chasing before Q1; recorded so nobody rounds it away.

**Random is now a scheduler policy**, not an oracle-side draw:
`SelectionPolicy.Random` ranks the feasible candidates by a fresh
seeded key each step (the argmax of iid uniforms is uniform), so with
no hold it is 4A/653's memoryless rule and with a hold it becomes
random-at-setup — the shape operators described as close to their
practice. The oracle and V35 now read the scheduler's granted link
instead of drawing privately (V35: worst 0.007 on the 10 s comb;
127/0). On the R-set question: **random maps to nothing**, exactly as
every strategy does — the format has no field for selection, which is
the projection discarding the selection rule; the 4A/653 alpha table
was the one proposal to carry a selection statistic into the format,
and it was rejected. What a random-selecting operation leaves in the
derived R set is only that its envelopes hug the gates (it uses the
whole feasible set), where a highest-elevation operation leaves a
derived minimum elevation above the enforced floor.

**STEAM-2 as an operational description.** The oracle's system is now
a case in the toolchain's own terms — `dataset/_src/STEAM-2.orbitdesign
.json` + `STEAM-2.opprofile.json` — and V35 proves the design document
reproduces the oracle shell satellite for satellite at the epoch (the
exact 1.9 deg phase now travels through the design-document codec).
What is real in it, from 4A/653 and its R-set XML: the shell, the band
(17.7-18.6 GHz), alpha 22 deg (filed), Nco 4 (filed), the typical-ES
deployment (es_distance 183 km, es_density 3e-5 /km2 — self-consistent,
so the service pitch is given), es_lat +/-90, min_duration unused, and
random selection (operator-attested). Two things are not filed: the
40 deg minimum elevation is the contribution's own simulation
assumption, carried as the profile's enforced rule and flagged; and the
payload is nowhere — the profile leaves it at scene defaults and says
so in its name. STEAM-2B's filed pfd mask (ntc_id 317520389) is the
envelope any assumed payload must sit under, and pulling it from the
BR database is the step that turns this case into the first real-filing
"plausible envelope" run. One modelling convention surfaced on the way:
a Case-1 design document always carries the examination's artificial
precession (`ToShell` forces NOrbits >= 1), while the oracle's shell
drifts naturally as the published simulation did — the alpha CDF is a
visibility statistic and does not care, but a truth run built from the
design document is not the oracle's truth run to the metre.

## Beamlab — the filed mask, dissected, 3 September 2026

STEAM-2B's filed pfd mask (ntc_id 317520389, mask_id 150, 17.7-20.2 GHz,
40 kHz) is now in hand and read back into operating rules
(`-- dissect`, docs/mask-dissection-steam-2b.md). It is the
satellite-frame (az/el) form: 179 latitude blocks by 1 deg, each a
119 x 119 grid at 1 deg, 2.53 million cells. Mapped to the ground
through the frame the examination reads it with, the mask is a
**two-level rule envelope**: a main-beam plateau at -130.2 dB
(-131.2 within +/-20 deg) wherever a beam may point, a side-lobe floor
30 dB down (falling with range toward the horizon) everywhere else,
nothing in between, no -1000 hole anywhere — the whole visible Earth is
"reachable" and the rules are written as levels. Read off the plateau
boundaries, per latitude:

- **Minimum elevation 40.0 deg at every latitude** (plateau edge 40.0,
  excluded side 39.9). The 40 deg the contribution called a simulation
  assumption is enforced in the filed payload envelope.
- **Exclusion alpha 22.0 deg, constant** over every alpha-limited
  latitude (excluded side 21.9-22.0, plateau side 22.0): one rule, whose
  hole in (az, el) changes shape with latitude purely through geometry.
  A per-latitude MIN_EXCLUDE for this operator would be flat 22 — the
  filed R set's single row is exactly right.
- The alpha rule is limited over **-53..53 deg = the inclination**;
  beyond the sub-satellite reach the blocks are byte-identical filler
  (plateau minimum alpha 23 at 55, 28 at 60, 38 at 70, 82 at 80).
- **A flat pfd cap, -130.2 dB(W/m2) per 40 kHz, independent of range**:
  the plateau is one integer level at every off-nadir angle out to the
  40 deg-elevation edge. That is constant-boresight-PFD power control
  (the profile's `PowerMode = pfd`), not a constant e.i.r.p. seen
  through spreading — the boresight e.i.r.p. density runs from
  2.0 dBW/40 kHz at nadir to 5.0 at the edge. The 1 dB lower cap within
  +/-20 deg latitude is the one feature the two rules do not explain.

So the operational description is now real on four more counts —
min elevation 40 (filed, in the mask), alpha 22 (filed twice, mask and
R set, and constant), the power-control mode, and the boresight pfd
that pins the power density — leaving only the beam pattern and layout
as assumptions (the case profile carries Gm 35 dBi and
TxEirpDbw -33.0 so the truth's boresight pfd meets the cap exactly). On the per-latitude alpha
question: this case is the clean real example of a *constant* rule
read through a latitude-dependent mask, which is what the examination
must reproduce; a test of a *varying* per-latitude MIN_EXCLUDE needs
an operator whose boundary alpha moves, or a synthetic variant of this
case with alpha rows and the mask regenerated by beamlab. Next: run
the projection on the real declarations — E1 with the filed mask and
the filed R set against a truth whose payload peak is pinned to the
mask's 5.0 dBW/40 kHz — the first plausible-envelope figure on a real
filing.

**A format note for your parser, same day.** The contribution's
operating-parameter XML is an illustration, not a schema-faithful R
set: it writes `<min_exclude orb_id="-1">` for its all-orbits row and
`<min_duration latitude="0">-1</min_duration>` for "unused", while the
Rec's Part B text makes 0 the all-orbits marker and the reference
worked examples write `<min_exclude c="0">`. Beamlab's writer follows
the Rec (0, and rejects negatives); its declared-set reader would treat
an orb_id of -1 as matching no orbit — i.e. silently no exclusion. We
will not build a reader for the contribution's dialect (the operator's
parameters go into the operation profile by hand instead), but the
question travels to radians: what does the examination's R-set parser
do with a negative orb_id, and does the BR's validated STEAM-2B set
carry 0 or -1?

## Beamlab — mask parity: our composition against the filed mask, 3 September 2026

The reverse of the dissection (`-- parity`, docs/mask-parity-steam-2b.md):
beamlab composes the STEAM-2 case from its profile + design document
exactly as a run would, exports its own az/el mask (lat -50..50 by 5,
az/el 1 deg — 0.1 min), and both masks go through the same Analyze().
The composition carried the dissected rules (min elev 40, alpha 22,
constant-PFD mode with Gm 35 / Tx -33 so the boresight pfd meets the
-130.2 cap, pattern floor 5 dBi = 30 dB down) and beamlab's defaults for
everything the filing is silent on (pattern, layout, aggregation).

| rule | ours (composition) | theirs (filed) |
|---|---|---|
| minimum elevation | plateau edge 39.5 | 40.0 (both sides) |
| exclusion alpha | hole visible only at -15..15, boundary 15-20 | 22.0 constant over -53..53 |
| pfd cap | -128.1, range-shaped (3.0 dB spread) | -130.2, flat |
| side-lobe floor | 20 dB below peak (-156..-148) | 30 dB below peak (-181..-160) |
| cells vs theirs | plateau +1.4 dB mean (-5.8..+3.0); floor **+22 dB** mean (+14..+34); rim of 2604 cells we radiate and they do not | — |

Three readings, each a statement about THEIR operation as much as about
our assumptions:

1. **Aggregation.** Our cap sits 2.1 dB above theirs and is range-shaped:
   the default power sum of overlapping beams adds the crossover
   neighbours. A flat single-level cap is what one beam's boresight pfd
   looks like, or a co-channel sum over few beams per colour — the filed
   mask is a per-beam-style envelope, not an all-beams aggregate.
2. **The floor is the loud disagreement: +22 dB.** Our floor is the
   power sum of every beam's side lobes over a lattice that tiles the
   whole 40 deg field of view at 183 km cells — hundreds of beams, so
   ~+20 dB over one beam's floor. Their floor is exactly one beam's
   30 dB-down envelope at every cell. Either the payload radiates few
   co-frequency beams per satellite at a time (reuse colours, or a small
   beam count), or the filed floor under-declares the aggregate side
   lobes of a full lattice by ~20 dB. The SNS beam data of the filing
   would decide which; our profile cannot, and this is where a margin
   figure on this case would be pattern assumption rather than rule.
3. **The exclusion hole is nearly filled in our mask.** Beams pointed at
   allowed cells just outside alpha 22 spill main-lobe energy into the
   excluded sliver, so within 3 dB of the peak the hole only survives
   where it is wide (the equatorial band). Their rule mask draws the hole
   sharply. A reachable-envelope mask of a real lattice therefore
   radiates toward the exclusion zone at near-plateau level — the filed
   mask does not say so. That is a concrete E1-vs-T direction question
   for this filing, contingent on the same beam-count assumption as (2).

Min elevation and the disc rim agree (39.5 vs 40.0; the rim is the
horizon-vs-59-deg grid). The parity tool now exists to re-run as the
profile is tuned by hand in the operation window; the numbers above are
the starting point, not the verdict on STEAM-2B.

**Second pass, same day** — case profile now co-channel aggregation
with N = 4 and 92 km beam cells (half the filed 183 km pitch): the cap
lands (**-129.8 vs -130.2, plateau mean -0.1 dB, 88.9% of plateau
cells within 1 dB**), so reading (1) is confirmed — the filed cap is a
co-channel-style single-beam level. The floor stays **+19.5 dB** over
theirs, and the exclusion zone is now filled by main-lobe roll-off to
within 20 dB of the cap everywhere (no floor-class cell survives above
the elevation edge in our mask). Readings (2) and (3) therefore stand:
either few co-frequency beams radiate per satellite at a time, or the
filed mask under-declares a full lattice's side lobes by ~20 dB and its
sharp exclusion hole by ~15 dB. The SNS beam data of the filing is the
arbiter; until then the residues are assumption-priced, not verdicts.

---

## Critique side — the objective pivot: optimize E1 over a compliant truth, 4 September 2026

First, the oracle round: accepted in full. The geometry-and-selection
half of Q4 is closed — worst CDF deviation 0.007 against a table
digitised to three decimals, the L5 count inside its stated band on
every step, V35 pinning both so a selection regression can never move a
margin figure unseen again. The 10 N / alpha 60 residual cell is rightly
recorded-not-chased. Random-as-policy is the correct promotion (argmax
of iid uniforms is uniform; random-at-setup under a hold is the
operators' stated shape), and its R-set image — nothing, by format —
is now a measured statement, not an assumption. The natural-drift vs
artificial-precession convention note stands as written.

**The operator has reset the objective, and it changes what the loop
is for.** The target is not to fit the limits: T is given and must meet
the limit on its own. The optimization variable is the declaration
derivation D(T) -> (masks, R set); the constraint is that D remains an
honest envelope of every operation the profile's declared commitments
permit; the objective is minimize E1. The anti-fitting guard survives
intact — nothing in D may be conditioned on the verdict or on Class-T
knowledge (below) — but the direction of travel reverses: the old
question was "which system passes", the new one is "how little of a
compliant system's true margin does the declaration burn". By the
power-scaling identity the E1 - T projection is invariant under uniform
power moves, so the frozen 6.5 dB transfers unchanged to the compliant
operating point: at the T-frontier the examination sees a system 6.5 dB
over. **Every dB removed from E1 - T is a dB of licensed operating
power.** That is the campaign's value function, and it is exactly the
quantity the STEAM-2B comparison just priced against a real filing.

**Where E1 can move — the mask, and almost only the mask.** The code
says why E2 - E1 ~ 0 was inevitable: OpParamsDeriver already envelopes
the FLOWN operation (measured floors and maxima per latitude band,
unobserved quantities left undeclared, exclusion declared only where it
bound) — the R set is occurring-side by construction and has no slack
to give. The scheduler reads its gates off the declared set itself
(DeclaredConstraints), so declaration and truth cannot drift. The one
reachable-side object in the chain is the pfd mask: it envelopes the
full lattice composition while the truth schedules a handful of beams
per satellite. And the disconnect is visible inside our own artefacts:
**the derived R set already declares max_co_freq_sat measured from the
flown operation — and the mask ignores it.** The filed-mask parity run
now shows the industry does not: STEAM-2B's cap sits at a co-channel
single-beam level, its floor ~20 dB below our reachable envelope, its
exclusion hole sharp where ours fills by main-lobe roll-off. Filed
masks embody a beam-count commitment; our mask construction refuses
one. That is the residue's home, measured twice — ~6.5 dB on the BL
margin lattice, ~20 dB on the STEAM-2B lattice — same phenomenon,
different beam counts.

**Legitimacy criterion, stated once for the whole campaign:** a
declaration tightening is admissible iff it is enforced by a declared,
binding commitment (Class D below) — the mask may assume the frequency
plan (co-channel colour), the per-satellite co-frequency beam cap, the
declared exclusion and elevation gates, because those bind the operator;
it may never assume the selection policy, the demand, the activity or
the duty cycle, because no format field binds them (and WP 4A rejected
every attempt to add one: selection/alpha table 4A/653, duty cycle
4A/623, likelihood weighting R15/4A/904 — Class T is regulatorily
final, not an implementation gap).

**The operation-profile parameters, classed by declarable image** (the
axis the intent grouping doesn't carry; propose it as one line per card
in ParameterCatalog and parameter-cards.html):

- **Class D — declarable, the whole E1-optimization space:**
  MinElev(+ByLat) -> min_elev; AlphaExcl(+ByLat) -> min_exclude;
  NcoPerCell/NcoByLat -> max_co_freq; MaxCoFreqSat -> max_co_freq_sat
  AND the mask's beam-count assumption; MinAngleAtEs/AtSat;
  MinHoldSec -> min_duration; ServiceLat bounds -> es_lat_min/max;
  EsDishM, PowerDbw, PowerControlRefElevDeg -> the E mask; the whole
  Power/Shape set (TxEirpDbw, GainPeakDbi, Taylor SLR/nbar, floor,
  PowerMode, Aggregation/ReuseClusterIndex, RefBw, cell radius,
  crossover) -> the S/pfd masks; FootprintSource is the projection
  switch itself.
- **Class T — truth-only, effect = measured margin, forever:**
  SelectionPolicy, DemandLinksPerCell, ActivityFactor/PeriodSec,
  IlluminationDutyCycle, OperationalFraction, ServiceLon bounds,
  CellKm pitch. Each reduces T and can never reduce E1.
- **Class G — derivation guards, protect validity not tightness:**
  YawSweepDeg, the mask grid steps (the converged b/c 1 / lat 5 are now
  the defaults), CoverageRadiusKm, the header-vs-array precedence.
  Wrong values make E1 wrong, not loose.

**Modern-definition checks from the WP 4A corpus, actionable now:**
(1) Nco realism — the BL family carries 1-4; real gateway-band filings
carry 20-30 (4A/658): one high-Nco case or sweep belongs in the family.
(2) MAX_CO_FREQ_SAT absent must STAY absent in every export — the RR
default is "the number of earth stations created for the epfd-up run",
i.e. unconstrained; writing 0 or 1 would be a wrong declaration
(DeclaredConstraints already treats absent Nco as no-cap; verify the
XML writer end of it). (3) The profile can currently carry MinHoldSec
and MinAngleAtEsDeg together and the composer forwards both — RR
A.14.d.12 makes them mutually exclusive per band; compose/export should
refuse the pair, not emit an unfilable declaration. (4) min_duration:
omit entirely for the classic algorithm, never write 0 (the 661716993
convention).

**Proposed order, for repricing on your side:**

- **E1-1, the committed-beam-count mask** — the envelope sampler gains
  the beam-count/colour commitment: envelope over "any K co-frequency
  beams of the lattice" (K from the declared MaxCoFreqSat / reuse
  colour), not all of them. Readout first: the truth run's per-satellite
  simultaneous co-frequency beam-count distribution, so K is measured
  before it is declared. Expectation stated before the run: a large
  fraction of the 6.5 dB at the tail; what survives is side-lobe
  geometry proper.
- **E1-2, per-latitude alpha mask inheritance** — the composer's own
  PerLatExclusionSceneGap guard names the other known inflation; under
  the new objective it graduates from warning to queue-top.
- **E1-3, the compose/export validations** of (2)-(4) above — cheap,
  and they make every emitted case filable.
- **E1-4, the catalogue re-tag** with the D/T/G axis — documentation,
  cheap, stops anyone "optimizing" E1 with a Class-T knob.
- **E1-0, the testbed:** set TxEirpDbw from the frontier so T passes
  22-1C on its own, freeze that profile, and track E1's distance to
  limit as the campaign metric. The identity guarantees the frozen
  decomposition carries over, so no re-attribution is needed.

STEAM-2B remains the external anchor: once E1-1 lands, re-run the
filed-mask parity — if the committed-beam-count envelope closes most of
the ~20 dB toward their filed cap and floor, the construction is
vindicated against practice, not just against our own truth.

---

## New input — the S.1325 revision draft: the operation profile's parent taxonomy, 4 September 2026

*Source: docs/REC-S.1325 rev/S.1325-rev Clean.docx — Annex 24 to the
WP 4A Chair's Report (Doc 4A/1064, issued as 4A/TEMP/265, May 2026), a
working document toward a preliminary draft revision of Recommendation
ITU-R S.1325, US-driven, motivated by the aggregate-epfd consultation of
Resolution 76 (Rev. WRC-23). Not agreed — bracket-heavy — and a STUDY
Recommendation, not the examination one: which is exactly why it says
out loud everything S.1503 cannot declare.*

Its §2.3 "Operational assumptions" is the ITU's own operation-profile
shape: five earth-station deployment models, a two-stage selection
structure (filter gates, then strategy), a catalogue of selection
strategies, power control in two formulations, and a two-level traffic
model. The operation profile stops being our invention — nearly every
card gains a Rec anchor, and the gaps become a farm list.

**Already in the profile, now with a citable parent** (propose adding
the section number to each card's "Where" line):

- Deployment: known locations / fixed-separation grid (§2.3.1.1, with a
  hexagonal-grid figure) = ServiceGeography.Grid and the specific-ES
  case; uniform density-and-distance with representative-ES aggregation
  n_es = d_es² · rho_es and e.i.r.p._rep = e.i.r.p._es + 10 log n_es
  (§2.3.1.2) = the declared ES_DENSITY/ES_DISTANCE examination side,
  formula-identical.
- Selection (§2.3.2.2): highest elevation with BOTH handover variants
  named — always-highest (our HighestElevation) and
  acquire-then-hold-to-minimum-elevation (our HoldUntilForced's shape);
  largest separation from the GSO arc (our MaxGsoSeparation); random /
  pseudorandom with the SDN rationale — proprietary, too complex,
  "adequate approximation to actual operation", seeded for
  repeatability — the 4A/653 operator-attested story, now in Rec-draft
  prose (our Random, fresh from the oracle round).
- Arc avoidance by angle to the arc (§2.3.2.1.2) = AlphaExclDeg; the
  two-stage structure = Scheduler's CandidateLinks -> policy, verbatim.
- Power control on range (§2.3.3) in both our formulations: desired
  receive power density (the uplink PowerControlRefElevDeg chain) and
  target PFD at the surface (the downlink constant-boresight-PFD
  PowerMode).
- Basic on/off traffic = ActivityFactor / ActivityPeriodSec.
- The rev's own worked deployment zones are lat 30-60 N, lon +/-10
  (baseline) and +/-20 (extended) — the BL service rectangle IS the
  extended zone; and its Fig. 3 finding (baseline vs extended: no
  observable epfd difference) is external support for the
  bounded-rectangle adequacy this debate once questioned.
- §2.5.2 lists "maximum number of co-frequency and co-polarization
  antenna beams and their spatial orientation" as a REQUIRED antenna
  input — the study Rec treats the beam-count commitment as a
  first-class parameter. E1-1's premise, independently stated by the
  parent document.

**The farm list — missing from the profile** (all Class T, so the E1
campaign is untouched; ordered by value-for-cost):

1. **Latitude-band arc avoidance** (§2.3.2.1.1): exclusion +/-X deg
   about the equatorial plane plus a minimum discrimination angle Y at
   the ES — "often used by MEO systems", i.e. the O3b technique. An
   avoidance MODE alongside the alpha mode, not an alpha value. Directly
   relevant to the equatorial-MEO cases.
2. **Longest-dwell selection** (§2.3.2.2.1): at acquisition, minimise
   dot(r_hat, v_hat) — pick the satellite moving toward the station.
   Completes the canonical strategy set; cheap against CandidateLinks.
3. **Reference-vector selection** (§2.3.2.2.5): nearest candidate to a
   declared (az, el) vector per latitude; the zenith vector reduces it
   to highest-elevation. Note the alpha table's afterlife lives here
   too — rejected from the examination Rec, alive as a SIMULATION
   selection strategy in the study Rec.
4. **Local-time-of-day traffic** (§2.3.4.1, Table 2: hour -> P(active),
   10% at night to 100% midday, keyed to LOCAL time at the earth
   station): ActivityByLocalHour rows; our constant ActivityFactor is
   its degenerate case. This is the item that matters for the
   aggregate era and for T realism.
5. Advanced traffic level (level-vs-trigger; CDMA-style power scaling
   P_t = P_max · C_traffic) — optional power-by-load, beyond on/off.
6. Probabilistic / population-based / typical-demand deployment
   (§2.3.1.3-5) — a deployment-mode enum; the population raster
   deferred until a case needs it.
7. Geographic Nco (§2.3.4.2, and the draft's own Editor's note: "The
   maximum Nco specified in the SRS database does not represent dynamic
   NGSO operations… requires further study"; rural Nco = 1 vs urban
   maximum) — WP 4A itself flags the Nco realism point the corpus gave
   us via 4A/658. NcoByLat is its declarable [lat] shadow; the full
   geographic structure is truth-only.
8. Pointing avoidance between non-GSO systems (§2.3.2.3, theta_T /
   theta_R thresholds) — needed only when multi-system aggregate work
   arrives.

**The aggregate horizon.** recommends 6 + Annex 4 + the Annex 5 work
programme are the Res 76 (Rev. WRC-23) aggregate-epfd methodology —
aggregation by CONVOLVING per-system epfd CDFs, demonstrated on two
"contemporary" hypothetical systems whose tables are numerically the
Starlink Gen1 shells (72x22 at 550/53, 72x22 at 540/53.2, 36x20 at
570/70, 6x58 and 4x43 at 560/97.6) and a OneWeb-shaped system — two
more farmable constellation decks now sitting in our own docs folder.
§2.5.3's input list (min elevation, time step, precession, power
control, traffic model, selection description, avoidance technique,
ES density by region, perturbations) reads as the checklist an
operation-profile export should be able to fill line by line. That, and
the multi-system pointing avoidance, define the tool horizon after the
E1 campaign — not before.

**Actions proposed:** (a) card anchoring — one S.1325-rev section
reference per profile card, so the profile's legitimacy is the Rec's,
not ours; (b) adopt farm items 1-4 as the next Class-T increments, each
with its one-line R-set image (= none) stated on the card; (c) log
items 5-8 on the compliance-loop plan as the aggregate-era backlog;
(d) the D/T/G re-tag from the previous round now has an external
template — §2.3's own grouping (location / tracking / power / traffic)
is the Truth-side sub-structure to mirror. Nothing here touches E1:
every farmed parameter is truth realism, which is precisely why the
study Rec could keep them when the examination Rec could not.

## Beamlab — the new objective accepted, and repriced, 4 September 2026

The objective is adopted as stated: the truth is given and must meet the
limit on its own, the optimisation variable is the declaration
derivation, the objective is to minimise E1 subject to the declaration
remaining an honest envelope of everything the commitments permit, and
the value function is that every dB removed from E1 - T at a compliant
operating point is a dB of licensed operating power. Recorded in
docs/compliance-loop-plan.md as "The loop, v3", together with the
Class D/T/G scope limit and the legitimacy criterion (a tightening is
admissible only where a declared, binding commitment enforces it).

Four things measured since your round change the pricing.

**1. The testbed is not free — our STEAM-2 truth fails.** Screening
sweep (144 x 60 s per latitude, 64 min, docs/compliance-steam-2.md):
PASS at 0/10/20 with +31.4/+25.0/+6.2 dB, FAIL at 30/40/50/60 with
-2.4/-7.5/-11.7/-8.8. Worst -11.7 dB at latitude 50, which by the power
identity is exactly the per-beam power the payload would have to shed.
So E1-0 is blocked on this case: under the triage rule no declaration
work is legitimate while the truth is over. The two candidate causes are
the assumed beam count and the assumed power, and the parity run points
hard at the first. I would rather fix it by modelling the beam-count
commitment (E1-1) than by inventing a lower power number.

**2. E1-1's expected size, priced from a smaller instance of the same
effect.** Your expectation was "a large fraction of the 6.5 dB". Mine,
stated before either run: a couple of dB, because a top-K envelope keeps
the dominant contributors and drops the tail of the sum. First evidence:
on the FILED STEAM-2B mask at latitude 40, declaring the per-cell cap 1
instead of the filed 4 moved E1 by **2.8 dB**, not the 6.0 dB a flat
four-way sum would give — the same decay, one level up. I therefore
expect E1-1 to land in the low single digits on the BL margin lattice.
The ~20 dB on STEAM-2B is not a counter-example: that lattice is far
denser (a 92 km beam cell tiling a 40 deg field), so its all-beams sum
sits much further above its top-K.

**3. Two more commitments the mask ignores, and one mechanism for all of
them.** Beyond your E1-2 (per-latitude alpha), the SERVICE LATITUDE BAND
is declared (`ES_LAT_MIN/MAX`, copied by the composer), enforced (the
scheduler refuses cells outside it) and absent from the mask: the
envelope sampler is built from the scene, the inclination and the yaw
sweep alone. On STEAM-2 the coverage half-angle at 40 deg from 1 150 km
is 9.5 deg, so with a 20-50 N band only sub-satellite latitudes
10.5-59.5 N can serve anyone — yet our exported mask declares a full
plateau at every block from -50 to +10 (5 086 cells at -50, 1 788 at the
equator). Thirteen of twenty-one blocks are pure inflation, and our own
two declarations contradict each other. S.1503-4 Sec. C1's -1000 null is
the sanctioned fix.

The mechanism is one gate, and it already exists: `ApplyAlphaExclusion`
walks the scene's beams, takes each beam's ground footprint, computes
alpha there and zeroes the weight inside the band. Feeding that same
gate the declared commitments instead of one scalar covers all four at
once — service band (footprint inside `ES_LAT`), minimum elevation
(footprint clears it), exclusion (alpha at the footprint's latitude
clears the table, i.e. your E1-2), and beam count (keep the strongest K).
The sampler's envelope stops being "every direction the payload can
reach" and becomes "every configuration the commitments permit", which
is what the parity run found a filed mask to be. Scope caution: only
BORESIGHTS are gated. Side lobes still radiate everywhere, so only a
block with no servable cell at all collapses to the null.

**4. The mask has to move inside the loop.** Today the loop ends at
write-back and the hand-off is manual. Under v1 that was harmless
because the verdict being optimised was the truth's; under v3 the
objective is E1 and E1 is read off the mask, so no candidate can be
scored until its mask exists. It is also the only way the mask, the R
set and the enforced gate can carry the same number at every latitude —
applying a per-latitude exclusion and exporting afterwards produces
precisely the mismatch above.

**Order, from the cost asymmetry.** A candidate for exclusion or
elevation changes the truth, so each one costs a full latitude sweep
(~1 h at screening depth on 1 600 satellites). A cap candidate in its
FREE regime — at or above the concurrency the operation actually uses —
changes no link, so the truth is frozen and the iteration costs a mask
export plus an E1 sweep, minutes. The cheapest legitimate dB is also the
cheapest to search for. So: (i) the cap lever, with its stopping rule
reshaped from "first delta that passes" to "smallest cap service can
absorb" — under v3 the old rule would stop at delta 0 and do nothing;
(ii) the commitment gate in the sampler, which delivers the service band
and your E1-2 together; (iii) the beam count on top of the same gate;
(iv) the testbed then falls out rather than being bought with an
invented power.

**On E1-3.** Checked one of the four: the XML writer does reject the
`min_duration` + `min_angle_at_es` pair, but the composer forwards both
silently, so an unfilable profile only fails at export. Moving the
refusal to compose is the right fix. The other three stand as you wrote
them.

**One naming trap for the campaign.** The S.1325 revision draft uses
"alpha table" for a SELECTION PROBABILITY distribution (its
Sec. 2.3.2.2.5, the 4A/653 proposal's afterlife as a simulation
strategy), while ours means `MIN_EXCLUDE`, a hard per-latitude bound.
Same words, opposite objects. Recorded as a documentation rule with the
broader one behind it: a declared data item is described by the
Recommendation that DEFINES the field, and a study Recommendation may
parent only truth-side cards.

## Beamlab — the mask by measurement, and E1 as the adequacy test, 4 September 2026

Two operator corrections to the reply above, both sharpening how the
commitments are supposed to reach the mask. They change the shape of
E1-1 and E1-2, so they belong on the record before either is built.

**1. The mask takes the commitments by MEASUREMENT, not by assertion.**
My proposal was a gate in the envelope sampler: boresight inside
`ES_LAT`, alpha clearing the table, at most K beams. That is the wrong
side of the doctrine this project has otherwise held. Declarations are
envelopes of what the system does; the R set already gets its numbers by
measuring the flown operation, and the mask should get them the same
way. The asymmetry matters in the direction that counts: a mask that
ASSERTS a restriction can assert one the scheduler does not enforce, and
that error is non-conservative — precisely the defect the acceptance
direction exists to catch. A mask that MEASURES cannot make that error,
because it observes exactly what the enforced system did.

So the derivation is a reachability probe: run the scheduler under the
candidate commitments, record the beam configurations it actually grants
— sub-satellite latitude, boresight direction, and how many co-frequency
beams a satellite carries at once — and envelope the pfd over that set.
Every commitment reaches the mask through the one mechanism that already
enforces it, with no per-commitment code: service latitude band, minimum
elevation, per-latitude exclusion, coverage radius and beam cap all
shape what is visited, so they all shape the mask. This also answers
your E1-1 and E1-2 with a single implementation rather than two.

The one thing the probe must not inherit is TRAFFIC. Demand, activity,
duty and operational fraction are Class T by your own classification: no
field binds them, so a mask tightened by them would be fitted to
behaviour the operator never promised. The probe therefore runs
SATURATED — every cell demanding, activity 1, operational fraction 1,
duty 1 — so what is measured is the reachable set under the
COMMITMENTS, not the occurring set under a traffic model. That is the
brief's reachable/occurring distinction obtained by measurement instead
of by assertion.

**2. E1 is what tests whether the simulation and observation were
granular enough.** Measurement here means simulation and observation,
and that trades a construction guarantee for an empirical one. Today
`mask >= live` holds BY CONSTRUCTION: the sampler envelopes the
configurations analytically and `ScenePointing` flies one of them
(the code says so in as many words). A mask derived from an observed set
holds only if the observation was fine enough and long enough. The test
for that is already in hand — an under-sampled probe yields a mask that
is too tight, and a truth run then exceeds it at some percentile. So
E1 >= T stops being only the acceptance criterion and becomes the
ADEQUACY TEST of the derivation itself.

One condition makes it real: the verifying run must be INDEPENDENT of
the derivation run — different epoch or seed, at least as fine,
verdict-grade. A mask derived from run A envelopes run A trivially, so
checking it against run A tests nothing.

The two granularities then have opposite signatures, which makes a
failure diagnosable rather than merely detectable:

| too coarse | effect on E1 | direction | how it surfaces |
|---|---|---|---|
| mask grid (b/c, latitude step) | inflated | safe, wasteful | the refine-and-watch runs — 2.1 dB measured on the BL case |
| probe (time step, duration, configurations visited) | deflated | UNSAFE | E1 < T at some percentile on an independent run |

Which puts granularity on the same footing as the time-step rule:
measured against a stated criterion, not asserted. It also supersedes
the stopping rule I proposed earlier this day (run the probe until the
visited set stops growing); that survives only as a cheap early
indicator, with the independent verification as the real gate.

**Consequence for the loop.** The mask and the R set become two products
of ONE measurement pass over the same enforced run. That is why the loop
must derive both, and why the three-way invariant — mask, R set and
enforced gate carrying the same number at every latitude — then holds by
construction rather than by inspection.

## Beamlab — the first E1, and where the invariant does not hold, 5 September 2026

v3 is built and a loop run now derives before it measures. Three passes:
the saturated probe (no victim, no limit) produces the R set and the pfd
mask; the truth sweep produces T; the examination sweep reads that mask
and that R set and produces E1. The STEAM-2 case at 0.1 d, seven
latitudes:

| lat | T margin | E1 margin | gap | E1 >= T |
|---|---|---|---|---|
| 0 | +31.4 | -15.3 | 46.7 | yes |
| 10 | +25.0 | -12.6 | 37.6 | yes |
| 20 | +6.2 | -17.0 | 23.2 | yes |
| 30 | -2.4 | -11.6 | 9.2 | yes |
| 40 | -7.5 | -16.1 | 8.6 | yes |
| 50 | -11.7 | -22.9 | 11.2 | yes |
| 60 | -8.8 | -21.1 | 12.3 | yes |

T is identical at every latitude to the record taken before any of this
work, so the truth genuinely did not move; and the adequacy test passes
everywhere, so the derivation envelopes the system it describes. Both
were the point, and both hold.

**The saturation leak, priced.** STEAM-2 asks for one link per cell and
declares a cap of four. The probe measures `max_co_freq` = 4 at every
latitude band. Derived from the traffic sample it would have been 1 —
a declaration the system breaks the moment it is busy. The leak was not
hypothetical.

**Where the previous entry was wrong.** It closed by claiming the
three-way invariant — mask, R set and enforced gate carrying the same
number at every latitude — would then hold BY CONSTRUCTION, because the
loop derives both artefacts from one pass. The first measurement says
otherwise, along one dimension the argument never checked: the SERVICE
LATITUDE SPAN.

The derived R set declares `es_lat` 20..49, measured from the cells the
scheduler actually served. The exported mask spans -50..+50, bounded by
the shell inclination alone. So at latitudes the system never serves,
the examination reads a fully-lit mask against a truth that has no
service there — and that, not granularity, is what the 46.7 dB and
37.6 dB gaps are made of. Deriving both artefacts from one pass makes
them consistent about what was OBSERVED; it does not make the mask
inherit what the R set DECLARES. The invariant holds by construction
only over the quantities both artefacts measure, and the service span is
not one of them.

The consequence for how the number is quoted is immediate. The headline
"widest gap 46.7 dB" is dominated by a modelling gap, not by declaration
granularity. Inside the served band the gap is 8.6 to 12.3 dB, and only
that part is what refining the declaration can recover. Quoting the
larger figure as projection margin would overstate the prize by a factor
of four.

**And it is not a clamp.** The obvious fix — null the mask below the
declared service span — is wrong. A satellite over latitude 0 may
legitimately be serving a cell at latitude 20 with its beams pointed
north, and the mask must still envelope that. What the mask has to
reflect is which beam geometries are reachable FROM a given sub-satellite
latitude GIVEN that only cells inside the declared span are served. That
is a constraint on the sampler, not a bound on the output grid, and whether it is
admissible at all is not ours to decide alone.

**The question this puts to the critique side.** Bounding the sampler by
the declared service span IS a tightening of the mask, and the doctrine
this debate settled says a tightening is admissible only where a declared,
binding commitment enforces it. The commitment does exist — `es_lat_min`
/ `es_lat_max` in the R set, and the scheduler serves no cell outside it.
But a pfd mask is read by the examination as a property of the SPACE
STATION, resolved from geometry alone; nothing in the down algorithm
consults the R set's service span when it reads a pfd cell. So either the
mask may inherit a bound that lives in a different artefact — in which
case the two stop being independently meaningful, and the reader of a mask
alone can no longer trust it — or it may not, and this 23-to-47 dB is
simply the price of declaring a mask that does not know where the system
sells service.

We have not settled which, and the number is four times the in-band
granularity term, so guessing is not acceptable. Note also which way the
error runs: an over-declared mask is CONSERVATIVE. Nothing here is unsafe
— it is only expensive, and expensive in exactly the currency the new
objective is denominated in.

**Still open, and named as such.** The walk still moves alpha and Nco —
profile levers — and re-measures T. That is the previous objective's
shape: vary the system until T passes. The granularity walk added
alongside it moves only levers that leave T fixed (the latitude band of
the derived rows, the mask grid), which is the v3 objective; but until
the service span reaches the mask, it is minimising a quantity whose
largest term it cannot touch.

---

## Critique side — the mask goes dark by measurement: the service-span question answered, 5 September 2026

**The answer to the either/or: the mask may go dark where the committed
system is dark, and doing so is not inheritance from another artefact.**
The false premise in the dilemma is that a pfd mask "resolved from
geometry alone" must be agnostic about operations. The mask's latitude
axis exists precisely because emissions VARY with sub-satellite latitude
— and they vary because of operations; that is the only reason the
format has the axis at all. §C1's -1000 null is the format's own device
for saying "this space station does not radiate here". A mask with dark
rows below the service reach is not borrowing the R set's bound at read
time: its VALUES record the consequence of the commitments, the
examination still reads it from geometry alone, and a reader of the mask
alone learns a true, self-contained, independently enforceable property
— "this satellite is dark south of X". If the operator later serves
outside the span, the emission exceeds the filed mask and the mask
itself is violated; no cross-artefact consultation is ever needed. The
coupling lives in the DERIVATION, where the commitments live; the
artefacts stay independent at the point of reading and at the point of
binding. So: admissible — and more than admissible, required, because a
mask lit where the committed system cannot transmit is not a purer
declaration, it is a wrong one about a different system.

**Your own doctrine already contains the mechanism.** The
measurement-not-assertion correction answers the "it is not a clamp"
worry before it is raised: the saturated probe grants no link when the
sub-satellite point is at -50, so the visited configuration set there is
empty and the envelope is empty — the null falls out of measurement, not
out of a bound on the output grid. And in the transition band the same
probe produces exactly the geometry you defended: a satellite at
sub-satellite 10 N serving a 20 N cell with its boresight pointed north
yields a lit, asymmetric row — neither nulled nor full-plateau. No
per-commitment code, no clamp, no assertion.

**One safety condition, and it is the round's only new rule: unvisited
is not unreachable.** A probe too short to visit a rare-but-reachable
configuration must not be allowed to null its block — that is the
deflated-mask direction, the unsafe one from your own granularity table.
So the null needs a stronger warrant than absence of samples: a block is
written -1000 only under an ANALYTIC unreachability certificate — from
that sub-satellite latitude, no cell inside the declared es_lat span is
within the coverage geometry at the declared minimum elevation. That is
closed-form (the 9.5 deg half-angle arithmetic you already did), it is
computed from declared commitments only, and it makes the split clean:
measured configurations decide the lit rows' values; the certificate
decides which rows may be dark; the independent verdict-grade run
remains the adequacy test for both (and for dark rows it tests trivially,
since the scheduler enforces the very span the certificate used).

**Bookkeeping the prize, so the headline stays honest.** The E1 - T gap
now has two named terms. The dark-row term: dominant at out-of-band
victims (lat 0: 46.7 dB gap, E1 -15.3 failing against T +31.4 passing —
that failure is manufactured entirely by rows the system cannot light),
certain to recover, cheap, admissible per the above. The in-band
granularity term: 8.6-12.3 dB at lats 30-60, the beam-count and
grid work. Report them separately from now on — "projection margin"
means the in-band term; the dark-row term is a modelling defect being
repaired, not a margin.

**Concessions and acceptances, so the record is straight.** (1) Your
E1-1 repricing stands and mine is withdrawn: the cap-4-to-1 measurement
on the filed mask (2.8 dB, not 6.0) plus the top-K decay argument is
evidence against my "large fraction of the 6.5 dB"; the probe decides,
and your low-single-digits prior is now the better-grounded one. (2) The
invariant correction is accepted as stated: one-pass derivation
guarantees consistency over what both artefacts MEASURE, and the service
span was not such a quantity — under the probe-plus-certificate design
it becomes one, and the three-way invariant is restored honestly.
(3) The saturation-leak handling is endorsed: the R set declares the cap
(4), not the traffic accident (1) — declarations envelope commitments,
never workload. (4) E1-3: moving the min_duration/min_angle_at_es
refusal from the writer to the composer is right. (5) The naming rule —
the Recommendation that defines a field owns its card; a study
Recommendation parents truth-side cards only — is adopted; "alpha table"
gets a disambiguation line on both cards it could confuse.

**The v3 boundary, stated once so "still open" can close.** There are
two nested loops and they are both clean. The OUTER loop designs the
system: it may move alpha, Nco, power, anything — T moves, and that is
not verdict-fitting, it is engineering a compliant system; the guard
never applied to the system, only to its description. The INNER loop
derives the declaration of whatever system the outer loop fixed:
measurement-only, saturated, verdict-blind. The walk that moves alpha
and re-measures T is the outer loop and is legitimate there; the
granularity walk is the inner. What the guard forbids is only the
diagonal move — changing the DESCRIPTION because of the VERDICT without
a commitment that enforces it.

**Order, endorsed with one promotion.** Dark rows first — certificate
plus probe: it flips the manufactured failures at lats 0-20, removes the
largest term, and lands the same probe machinery E1-1 needs. Then the
cap lever in its free regime (truth frozen, minutes per candidate). Then
the beam count on the same probe output. STEAM-2's in-band T failure
(-2.4 to -11.7 at 30-60) stays an OUTER-loop fact throughout: the system
as assumed — non-filed 40 deg elevation, scene-default payload — is over
the limit, and no declaration work may hide that; whether the real
STEAM-2B payload clears it is answered by the filed mask's power level,
which is the parity run's next question anyway.

## Beamlab — the dark-row term was ours, not the system's, 5 September 2026

A correction, and it removes the premise the previous two entries were
built on. The certificate is built, it works, and it recovers nothing.

**What was claimed.** My entry of this morning reported the E1 - T gap
splitting into a dark-row term (46.7 dB at victim latitude 0, 37.6 at 10,
23.2 at 20) and an in-band granularity term (8.6 to 12.3 dB at latitudes
30 to 60). The critique side's reply accepted that split, called the
dark-row term "dominant at out-of-band victims, certain to recover,
cheap, admissible", and ordered the work around it: dark rows first.

**What was wrong.** The dark rows were an artifact of OUR service
geography, not a property of the system. The STEAM-2 case carried a
single northern box — latitudes 20 to 50, longitudes -10 to 10 — as a
test geography. Nothing in Doc 4A/653 says the system serves only that
box; we chose it, and then measured the consequences of our own choice.

Widened to a global service area (-50..50 latitude, -180..180
longitude), with the certificate applied exactly as agreed:

| service area | es_lat span | lit rows | dark rows |
|---|---|---|---|
| the northern box | 20..49 | 5 | 6 |
| global | -50..50 | 11 | 0 |

Zero. With a system that serves both hemispheres and all longitudes, no
sub-satellite latitude is certifiably unreachable, so the certificate
darkens nothing and the dominant term of the gap does not exist. The
23-to-47 dB was never margin waiting to be recovered; it was the price
of a mask correctly describing a system we had invented.

**What survives, and it is not nothing.** The certificate is still
required, for exactly the reason the critique side gave: a mask lit where
the committed system cannot transmit is a wrong mask about a different
system. That argument was never contingent on the size of the prize, and
it stands. It will also matter for any genuinely regional filing, where
the dark rows are real. What does not survive is the ORDER built on the
prize, and the endorsement of that order, which rested on our premise
rather than on the critique side's own reasoning.

**A second correction, of the same kind.** I reported that widening the
service area improved T at latitude 40 by 11 dB — from -7.5 dB failing
to +3.6 dB passing — and said it needed explaining before either number
could be trusted. It needed no explaining: I had compared a 7-step run
against a 144-step one. The same-depth control settles it.

| service area | steps | max epfd | T margin |
|---|---|---|---|
| the northern box | 7 | -179.2 | +3.6 |
| global | 7 | -179.1 | +3.6 |

The geography moves the truth at that victim by 0.1 dB. What moved was
depth, and it moves T a great deal:

| steps | percentile floor | T margin at lat 40 |
|---|---|---|
| 7 | 14.3% | +3.6 |
| 29 | 3.4% | -1.9 |
| 144 | 0.69% | -7.5 |

which is the tail resolving, exactly as it should. The rule this leaves
behind is dull and was already printed on every run: a comparison
between runs must hold depth fixed. The loop reports its own resolvable
percentile floor and I did not read it.

**What the geography does move is the DECLARATION, not the truth.** The
same two probes, same depth:

| quantity | northern box | global |
|---|---|---|
| es_lat | 20..49 | -50..50 |
| max_co_freq_sat | 43 | 57 |
| min_angle_at_es | 1.9 deg | 0.1 deg |
| min_angle_at_sat | 4.6 deg | 2.3 deg |
| link samples (7 steps) | 4 950 | 319 635 |

The truth at one victim barely notices; nearly every declared quantity
loosens. That asymmetry is the E1 - T gap in one picture — the
declaration must envelope a much larger operating space while the truth
at any single victim stays where it is. It also means the earlier
narrow-box declaration was tight because the service area was small, not
because the system is disciplined, and any margin attributed to that
tightness was borrowed against a geography we had made up.

**The bookkeeping, restated.** One term, not two. The gap at latitude 40
is 7.9 dB at 7 steps, all of it in-band, and the granularity walk has
already measured what grid and latitude-band refinement can take off it:

| granularity | worst E1 | recovered |
|---|---|---|
| coarse (mask lat 10, az/el 2) | -23.0 dB | — |
| baseline (mask lat 10, az/el 1) | -22.9 dB | 0.1 dB |
| fine (mask lat 5, az/el 1) | -22.9 dB | 0.1 dB |

0.1 dB. The inner loop's current levers are spent. Whatever the in-band
gap is made of, it is not mask-grid granularity, and it is not the
service span. That leaves the beam-count and envelope work — E1-1 — as
the only remaining candidate, and it is now the whole of the inner
loop's agenda rather than the second item on it.

Note also that the 2.1 dB attributed to the b/c grid on the BL case does
not generalise: the same axis is worth 0.1 dB here. It should stop being
quoted as a general figure.

**One implementation note worth recording, because it is a general
trap.** The first certificate tested a row's own latitude. Sec. D5.1.5
step 1 reads the table with the NEAREST latitude, so a row governs the
half-step either side of it; testing the centre darkened rows that also
govern reachable latitudes, under-declaring for every satellite in the
reachable half — the deflated-mask direction, the unsafe one. It passed
its checks, because the checks were written to the same wrong model. Any
per-row rule on a nearest-read table has to clear the whole band the row
governs, and the check has to say so.

**Still outstanding.** The like-for-like global run at 144 steps, against
the -7.5 dB record, is in flight. The STEAM-2 in-band failure remains an
outer-loop fact: the system as assumed — 40 deg elevation not from the
filing, scene-default payload — is over the limit, and no declaration
work may hide that.

---

## Critique side — two corrections owned, and the round's keeper, 5 September 2026

**The referee miss is mine to own, precisely.** I priced and ordered the
work by a term whose provenance I never checked. The case document
flagged the 40 deg elevation as a non-filed assumption; the service box
deserved the same flag, and the question "is the box filed?" was one
sentence away when I wrote "certain to recover, cheap" and put dark rows
first. The admissibility argument survives — you said so, and it does,
because it never depended on the prize — but an argument surviving is
not the same as an order surviving, and the order was built on a number
I took from the entry above it without asking where the geography came
from. Corrected in full: dark rows are a correctness requirement for
regional systems, not a recovery item for this case.

**The certificate trap is also partly mine, and the fix generalises
beyond this repository.** My rule was worded point-wise — "from that
sub-satellite latitude" — and a point-wise warrant on a NEAREST-read
table is exactly the under-declaring model your drift caught: a row
governs the half-step band either side of it, so the warrant must clear
everything the row governs. Adopted as stated. And it exports: every
nearest-read array in the operating-parameter format — min_elev latitude
blocks, max_co_freq, min_duration — has the same band-not-point
semantics, so any per-row rule anywhere (radians' resolution layer, the
dataset validators, the EPS's own wording when it describes row
applicability) must be written band-wise. That one goes on the list of
things this project learned that the examination side needs to hear.

**Bookkeeping accepted: one term.** And with it the discipline update:
the 2.1 dB b/c share was a BL-lattice measurement, not a constant of the
method — on this case the same axis is worth 0.1 dB. Grid shares are
per-case measurements; no grid figure travels without its case attached
(the same rule the margin headline already obeys).

**The round's keeper is the asymmetry table.** Truth at one victim:
moved 0.1 dB by the geography. Declarations: es_lat 20..49 -> -50..50,
max_co_freq_sat 43 -> 57, both min angles collapsing toward zero. That
is the cleanest statement this debate has produced of why E1 - T exists
at all: **E1 answers for the whole committed operating space; T answers
for one victim's history.** Two consequences worth recording. First, the
gap grows with declared generality — an operator serving a region has a
structurally tighter examination position than a global operator at the
same per-victim truth. That is an outer-loop design fact with regulatory
meaning, and it is cheap to demonstrate properly now: one BL-family pair
— same shell, same payload, regional vs global service — same T at the
common victim, different E1. The dataset should carry that pair; it is
the dark-row physics presented honestly, as a property of systems rather
than of our test box. Second, the depth rule stays dull and absolute:
comparisons hold the printed percentile floor fixed; the loop already
says it on every run.

**The inner loop's agenda is E1-1, alone.** Agreed — and the frame for
it should be falsifiable before it runs. The in-band gap at depth is
8.6-12.3 dB; your top-K prior says E1-1 recovers low single digits; the
grid walk says ~0.1 dB here. So the pre-stated expectation: after E1-1,
a residue of roughly 5-9 dB remains at the tail, and that residue is the
envelope price proper — side-lobe geometry enveloped over the committed
space, the quantity Q1 owns. If E1-1 instead recovers most of the gap,
my top-K concession was premature and we will say so; if it recovers
~nothing, the beam-count commitment was already priced into the filed
cap reading and the envelope price is the whole gap. Either way the
number lands in a frame that was written down first.

Outer-loop facts stand as you closed them: the 144-step like-for-like is
awaited, and the STEAM-2 in-band failure belongs to the assumed system —
non-filed elevation, scene-default payload — which no declaration work
may hide. The parity run's next question (the filed mask's power level)
is where that thread resumes.

### Addendum, same day — the control was one latitude, and the wrong one

The like-for-like run finished, and it amends the entry above. At 144
steps, narrow box against global, both with the certificate:

| lat | T narrow | T global | difference |
|---|---|---|---|
| 0 | +31.4 | -9.1 | -40.5 |
| 10 | +25.0 | -4.8 | -29.8 |
| 20 | +6.2 | -7.4 | -13.6 |
| 30 | -2.4 | -5.7 | -3.3 |
| 40 | -7.5 | -6.9 | +0.6 |
| 50 | -11.7 | -11.9 | -0.2 |
| 60 | -8.8 | -11.7 | -2.9 |

The geography is decisive at low latitudes and negligible at high ones.
I controlled at latitude 40 — the one place where it barely registers —
and generalised from it. The obvious mechanism, obvious in hindsight:
the northern box put no service near the equator, so a victim at
latitude 0 saw a nearly empty sky; a global system fills it. Nothing
subtle, and one more latitude in the control would have caught it.

What the entry above got right stands. The dark-row term is zero on a
global geography, and the latitude-0 gap falls from 46.7 dB to 6.2 dB,
which is that term disappearing. The latitude-40 discrepancy was depth:
-7.5 narrow against -6.9 global at the same 144 steps. What does not
stand is the reading the entry invites — that the geography is neutral
for T. It is neutral for T AT LATITUDE 40. Elsewhere it is worth up to
40 dB, and a single-latitude control cannot say otherwise.

The rule this leaves is the same shape as the depth rule and just as
dull: a control must vary over the dimension the claim is about. The
claim was about the sweep, and I controlled at a point in it.

**The run itself.** T now fails at all seven latitudes, worst -11.9 dB.
The gap is 5.9 to 11.0 dB at every latitude, uniformly in-band, with no
outlier — the one-term bookkeeping holding across the whole sweep rather
than only where it was first measured. Adequacy holds everywhere.

**One declared value worth noticing.** Over 6 575 111 link samples the
probe measured min_angle_at_es = 0.0 deg: two satellites served one cell
at essentially zero separation. The system can promise nothing on that
axis, and the derived set correctly declares nothing. That is the
saturated probe doing its job — under the narrow box the same axis
declared 1.9 deg, which was a promise the larger operating space cannot
keep.

## Beamlab — the keeper does not survive its own table, and what does, 5 September 2026

The addendum above was written before your reply and appended past it,
so it does not answer you. This does. The like-for-like run is in, and
it costs the round's keeper its evidence while sharpening what the
keeper was reaching for.

**The asymmetry table was measured at one latitude, and it was the wrong
one.** "Truth at one victim: moved 0.1 dB by the geography" is a
latitude-40 fact. At latitude 0 the truth moved 40.5 dB. The northern
box left the equator unserved, so a victim there saw an almost empty
sky; that is the dark-row artifact appearing a second time, in T instead
of in the mask, and I handed you the table without controlling for it.

**Your first consequence is not supported.** The claim was that the gap
grows with declared generality — a regional operator holding a
structurally tighter examination position than a global one at the same
per-victim truth. Narrow box against global, 144 steps:

| lat | T narrow | T global | gap narrow | gap global | change |
|---|---|---|---|---|---|
| 0 | +31.4 | -9.1 | 46.7 | 6.2 | -40.5 |
| 10 | +25.0 | -4.8 | 37.6 | 7.7 | -29.9 |
| 20 | +6.2 | -7.4 | 23.2 | 9.6 | -13.6 |
| 30 | -2.4 | -5.7 | 9.2 | 5.9 | -3.3 |
| 40 | -7.5 | -6.9 | 8.6 | 9.2 | +0.6 |
| 50 | -11.7 | -11.9 | 11.2 | 11.0 | -0.2 |
| 60 | -8.8 | -11.7 | 12.3 | 9.4 | -2.9 |

The only rows where the comparison is controlled are 40, 50 and 60,
where T moves by at most 2.9 dB. There the gap goes 8.6 -> 9.2,
11.2 -> 11.0, 12.3 -> 9.4. Flat. Where the gap did collapse — latitudes
0 to 20 — it collapsed because the dark-row artifact went away, not
because generality bought anything. At matched truth, going from a
regional box to a global service area did not widen the examination
position at all.

**What survives is stranger and more useful than what was claimed.** At
those same controlled latitudes the DECLARATION loosened hard:
min_angle_at_es 1.9 -> 0.0 deg, min_angle_at_sat 4.6 -> 2.2,
max_co_freq_sat 43 -> 59, es_lat 20..49 -> -50..50. Every one of those
is a promise weakened, and E1 did not move. So the asymmetry is real but
it is not the one you named: declaration GENERALITY moved the declared
numbers a great deal while moving E1 essentially not at all.

Which localises the gap. If loosening the min angles by 1.9 and 2.4 deg
and the per-satellite cap by 16 links changes E1 by under a dB, then E1
is not being set by the R-set gates in this regime — it is being set by
the mask, and by the mask's VALUES rather than its resolution, since the
grid walk already priced resolution at 0.1 dB. The 5.9 to 11.0 dB is
envelope price almost by elimination: not service span, not grid, not
the gates that changed. That is your E1-1 frame arrived at from the
other side, and it tightens your pre-stated expectation rather than
loosening it — there is less room for E1-1 to be recovering gate slack
by accident.

**The BL-family pair is still worth carrying, with one condition.** It
should now be run as a test of a hypothesis this data disfavours, not as
a demonstration of one it supports, and it must control T explicitly:
compare only at victims where the two geographies produce the same truth
to within a stated tolerance, because the uncontrolled rows are where
all the apparent effect lived. If the pair reproduces flat gaps at
matched T, "regional systems hold a tighter examination position" should
be struck rather than filed.

**Both of your ownerships are accepted, and the band rule's export is
the better half of this round.** That every nearest-read array in the
format — min_elev, max_co_freq, min_duration — carries band-not-point
semantics is worth more than any dB figure this round produced, because
it is a statement about the Recommendation's own reading rules rather
than about our case. It also predicts where else this bug lives: any
consumer resolving a per-latitude row to a point rather than to the band
it governs will under-apply the array near its edges, and that is
checkable against radians' resolution layer directly.

**Outer-loop facts, unchanged.** T now fails at all seven latitudes,
worst -11.9 dB, on the assumed system — non-filed 40 deg elevation,
scene-default payload. The declaration work has not moved it and cannot;
the filed mask's power level remains where that thread resumes.

---

## Critique side — the keeper struck, the control rule codified, 5 September 2026

Accepted without reservation. The keeper was built on an uncontrolled
table — the same defect class, third appearance this week: a measured
number accepted without asking what varied underneath it. Depth, then
provenance, now the control latitude. The rule that stops the recurrence
belongs in the record as a standing one, stated once:

**No comparative claim enters this document without naming its
controls.** A table that compares two runs states what was held fixed
(depth via the printed percentile floor, geometry, T at the compared
victim) in the same breath as the numbers. A row where the control does
not hold is marked uncontrolled and used for nothing. That rule would
have caught all three of this week's misses at the moment of writing.

So: "the gap grows with declared generality" is struck, not filed —
subject only to the BL pair run under your inverted framing (a test of a
hypothesis the data disfavours, compared solely at matched-T victims
within a stated tolerance; flat gaps there make the strike final).

**The localisation is the round's real product, and it tightens E1-1's
meaning.** At controlled latitudes the gates loosened hard — min angles
1.9 -> 0.0 and 4.6 -> 2.2 deg, the per-satellite cap 43 -> 59, es_lat to
the full span — and E1 moved under a dB; the grid was already priced at
0.1. In this regime E1 is set by the mask's VALUES and nothing else.
Which means the one lever aimed at the operative term is exactly E1-1
(a mask-values change), and my pre-stated frame gains precision rather
than losing it: whatever E1-1 recovers is genuine envelope-construction
slack, uncontaminated by gate effects, and what it leaves is the
envelope price proper, nearly the whole in-band gap by elimination. The
regime qualifier stays attached: with a small Nco or a binding
exclusion, the gates would reclaim their share; this case's gates are
slack, and the statement travels with the case.

**The band rule's radians export, made concrete.** Your prediction —
any consumer resolving a per-latitude row to a point under-applies the
array near its edges — is directly checkable and belongs in the
validation dataset: one case whose min_elev (or max_co_freq) rows sit at
stated latitudes with victims and cells placed half-a-step either side
of a row boundary, so a point-reading consumer and a band-reading one
produce different verdicts. That is a cheap, discriminating BL-family
addition, and the same probe pattern belongs in the examination-side
test plan when radians' S.1503-4 resolution layer lands. Flagged to the
operator as a brief item rather than smuggled into this loop's scope.

Outer-loop facts as you closed them: T fails at all seven latitudes on
the assumed system; declaration work cannot and must not move it; the
filed mask's power level is where that thread resumes. E1-1 runs next
against a target this round has made unusually clean.
