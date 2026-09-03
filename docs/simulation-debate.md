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
