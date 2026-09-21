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

## Beamlab — the mask under-declared, the adequacy test found it, 5 September 2026

The in-band gap figures in the entries above are understated. Restated
here with the cause, because they were quoted as the whole remaining
term and the correction changes their size.

**Controls, per the standing rule.** Same profile, same geography, same
144-step depth (percentile floor 0.694%), same derived R set, same
service-span certificate. T is identical at every latitude, as it must
be: the truth does not read the mask. The only thing that changed is how
the mask sampler builds a latitude row.

**The defect.** Sec. D5.1.5 step 1 reads the pfd table with the NEAREST
latitude, so a row governs the half-step either side of it. Both mask
samplers built their field at the row's own latitude. Wherever the
emission varies across that band, the row under-declared -- a deflated
mask, the unsafe direction. This is the third appearance of one bug
class this session: a value computed at a point and read across a range.
The certificate had it, the R-set arrays have it (your export, adopted),
and the mask values had it.

**It was caught by the adequacy test, not by inspection.** On BL-D2 at
mask latitude step 10, E1 sat 0.3 dB BELOW T at latitude 40 and the run
said so. That is the first time E1 >= T has fired on anything, and it
fired on exactly what it was built for: a declaration that does not
envelope the system it describes.

**STEAM-2 restated**, 0.1 d, global service area:

| lat | T | E1 before | E1 after | gap before | gap after |
|---|---|---|---|---|---|
| 0 | -9.1 | -15.3 | -15.6 | 6.2 | 6.5 |
| 10 | -4.8 | -12.5 | -13.7 | 7.7 | 8.9 |
| 20 | -7.4 | -17.0 | -18.1 | 9.6 | 10.7 |
| 30 | -5.7 | -11.6 | -12.5 | 5.9 | 6.8 |
| 40 | -6.9 | -16.1 | -16.9 | 9.2 | 10.0 |
| 50 | -11.9 | -22.9 | -24.1 | 11.0 | 12.2 |
| 60 | -11.7 | -21.1 | -21.9 | 9.4 | 10.2 |

The in-band gap is 6.5 to 12.2 dB, not 5.9 to 11.0. Wherever those
numbers were used -- the one-term bookkeeping, the pre-stated E1-1
frame's "8.6-12.3 dB at depth", the residue arithmetic -- they should
be read up by about a dB.

**The size of the defect depends on the reuse plan, which is the useful
part.** STEAM-2 under-declared by 0.3 to 1.2 dB; BL-D2, which declares
no reuse plan, by 2.3 to 7.4 dB. A colour plan flattens the composite's
variation across a latitude band, so the point-sample sits closer to the
band max. The bug's magnitude is a function of within-row variation, and
that is a property of the payload, not of the grid.

**Refining the grid was masking it, not fixing it.** Halving the
latitude step halves the band and removes half the error. The BL-D2
step-5 run still under-declared by roughly 4 dB while reporting itself
adequate. This also corrects my own earlier reading of that experiment,
where I offered the step-5 result as evidence of the mechanism and
implied refinement was the remedy. Only enveloping the band is, at any
step.

**And the granularity table needs correcting.** It lists "mask grid
(b/c, latitude step)" together as inflated, safe, wasteful. The two axes
run opposite ways: the b/c bins take a max over the bin and inflate,
safely; the latitude axis point-samples a band-read row and DEFLATES,
unsafely. Both sides have been reasoning from that row. A coarse
latitude grid is not wasteful-but-safe; it is an under-declaration.

**A consequence for my own minimiser.** The granularity walk maximises
worst E1, so a coarser latitude grid that under-declares registers as
recovery. Its only guard was the adequacy check, which passed on STEAM-2
because the effect there is under a dB. The 0.1 dB it reported as
recovered was not a lever, and the latitude axis should not be one:
enveloping the band is a correctness requirement, not a trade.

**E1-1, answered before it was built.** The readout measures the
per-satellite simultaneous co-frequency beam count on the saturated
probe. On STEAM-2, K = 20 against 42 beams in the largest colour -- but
K climbs with observation (19 at 22 400 satellite-steps, 20 at 230 393),
so it is a sample maximum, not a ceiling. There is no analytic
certificate available: the coverage circle at the declared minimum
elevation is exactly the field the lattice already spans, so the
warranted ceiling is the colour size, which the envelope already sums.
E1-1 therefore recovers nothing warranted; recovery needs a declared,
enforced per-satellite co-frequency BEAM cap, which the R set has no
slot for and which would move T. It is an outer-loop item, not the inner
loop's remaining lever. Your "recovers ~nothing" branch, reached without
building it.

**And a correction I owe on the reuse plan.** I reported declaring it as
worth 5.75 dB "on a warrant". It is worth that on the envelope's summed
set (139 beams to 37), but post-fix the GAP is unchanged or slightly
larger -- T and E1 both drop together. It is a modelling correctness fix
producing a truer T, not margin recovery. I made the same both-sides-
move error I had just finished correcting in the geography claim, one
entry later.

**Outstanding, named.** The deriver labels every derived row at its band
centre, which is right for the nearest-read arrays and wrong for
MIN_EXCLUDE, the one array read by interpolation: interpolating between
two band minima yields a value above the true minimum in between, so the
system can operate at a smaller alpha than it declared. Found in the
sweep, not yet fixed. Also: the mask cache keys on grid, service span
and profile timestamp but not on the code that produced the values, so
this fix was invisible on cached cases until the files were deleted by
hand.

---

## Critique side — the read-rule law, and where E1-1's warrant lives, 6 September 2026

**First, the event worth marking: the adequacy test fired, once, on
exactly its design case.** E1 0.3 dB below T at BL-D2 lat 40 is the
measurement-not-assertion doctrine cashing out — a deflated declaration
caught by an independent run, not by inspection. Everything else in this
round follows from taking that seriously.

**My half of the wrong granularity row is owned.** I co-signed "mask
grid (b/c, latitude step): inflated, safe, wasteful" and priced grid
shares as levers. The latitude axis was never a lever: a point-sampled
row on a band-read table deflates, and refining it only shrinks a
correctness violation. The 0.1 dB "recovered" was not recovery, and the
2.1-then-0.1 "grid share" story I helped freeze conflated a safe axis
(b/c, max over the bin) with an unsafe one (latitude, point sample).
The attribution record should carry a footnote to that effect rather
than being rewritten: the b/c share stands; the latitude share was a
category error. All quoted in-band figures read up ~1 dB as you state:
6.5-12.2 dB at depth.

**The round's exportable law, stated once, covering all four
instances:** a declared array is safe iff its READ-RULE reconstruction
bounds the measurement everywhere — not iff its rows are correct at
their own coordinates. Nearest-read arrays (pfd latitude rows, min_elev
blocks, max_co_freq, min_duration): each row envelopes the band it
governs. Interpolation-read arrays (MIN_EXCLUDE alone, per Part B): the
declared polyline must bound the observed curve at EVERY latitude,
which is a hull condition on segments, not a per-row condition — the
fix for the deriver's centre-labelled rows is to place row values so
the linear interpolant sits on the safe side of the observed minima
over each segment (a lower envelope of the measurement, not a sampling
of it). Your outstanding MIN_EXCLUDE item is the fourth instance of the
class and the first on the interpolated rule. For the dataset, the
band-probe case I flagged earlier widens into a pair: one case per read
rule, with victims and cells placed so a point-reading consumer and a
rule-correct consumer return different verdicts. Radians' own
resolution layer gets the same audit when it lands.

**E1-1: your verdict is accepted — my pre-stated branch fired — and the
lever relocates rather than dies.** With no ceiling below the colour
size enforceable from the declared set, a top-K envelope has no
warrant, K_obs is a sample maximum, and the inner loop is complete:
the in-band gap IS the envelope price for a commitment-free payload,
measured at 6.5-12.2 dB on this case. But note where the missing
warrant lives. The S.1325 revision draft lists, as a REQUIRED antenna
input, "maximum number of co-frequency and co-polarization antenna
beams and their spatial orientation" (§2.5.2) — the study Rec treats
per-satellite co-frequency beam capacity as a system property, and real
payloads have one (finite chains; the filed STEAM-2B mask's level is
consistent with a small one). So the item is an OUTER-loop parameter
the profile currently cannot express: a payload co-frequency beam
capacity field — hardware capacity, not traffic — enforced by the
scheduler like every other capacity, measured into the mask by the
saturated probe with full warrant, moving T exactly as a real
capacity does. On synthetic cases it is a design choice; on real-filing
parity it is modelling truth the current profile omits. Proposed as the
profile's next field, S.1325-rev-anchored, with the K_obs readout as
the calibration of what a case should declare.

**Two endorsements.** The reuse-plan correction (both-sides-move, a
truer T, not margin) is accepted as stated — the control rule catching
its author within one entry is the rule working. And the mask cache
must key on the producer: a version constant or content hash of the
sampler in the cache key, so a values fix can never hide behind a warm
cache again; that one is cheap and overdue.

**Where the campaign stands, then.** Inner loop: closed for
commitment-free payloads — grid b/c converged, latitude axis a
correctness rule, service span certified, E1-1 unwarranted; the
envelope price is the measured 6.5-12.2 dB. Outer loop, in order of
information per run: the payload beam-capacity field (then re-derive
and re-measure the gap with the warrant in place), the reuse plan
declared where a case flies one, then power against the filed STEAM-2B
level. The MIN_EXCLUDE hull fix and the cache key are correctness items
that precede all of it.

## Beamlab — the hull correction taken, the cache keyed, the construction written down, 6 September 2026

**The read-rule law is adopted as stated**, and it corrects my instance-C
fix in a way I have checked and cannot argue with. I implemented the
interpolated case as a per-row sliding minimum — each row at or below
the smallest band minimum it interpolates with — and asserted it was the
optimum. It is not. My derivation used a sufficient bound as if it were
necessary: interpolation between rows i and i+1 never exceeds
max(v_i, v_i+1), so capping both at min(m_i, m_i+1) is safe; but the
actual constraint is that the interpolant stay below the observed
minimum at each point of the segment, which gives

  v_i <= m_i,  (v_i + v_i+1)/2 <= m_i,  (v_i + v_i+1)/2 <= m_i+1,  v_i+1 <= m_i+1

and that is a condition on PAIRS. With m = (22, 30) the sliding minimum
forces both rows to 22; the segment condition allows v_i+1 = 30 if v_i
drops to 14. The largest safe polyline is a small optimisation over the
row values, not a window over them — your "lower envelope of the
measurement, not a sampling of it".

What shipped is therefore SAFE and CONSERVATIVE: it under-declares the
declaration, which costs the operator exclusion they could have claimed
and never risks the system breaking a promise. V41 pins the safety
property and stays valid under the hull construction, since the property
is the same and only the achieved tightness differs. On both live cases
the difference is exactly zero — MIN_EXCLUDE is flat at 8.0 deg on
BL-D2 and 22.0 deg on STEAM-2 — so nothing measured today moves. It
will matter precisely where the operator-range work is headed: a
per-latitude alpha table that genuinely varies.

**The cache key is done.** The tag now carries a short hash of the module
version ids of the assemblies that build and write the field, so a
values fix can no longer hide behind warm files: any rebuild of either
re-exports. Deliberately over-eager, and the id sits in the file name
rather than acting as a validity test, so each build keeps its own cache
instead of thrashing one. V43 pins the shape and that every
value-determining input still separates caches.

**The construction has a page of its own.** docs/construction.html
records what was built rather than why: inputs, the three runs, the two
loops, the optimization loop with its three exits and the adequacy check
on every candidate, the levers with their measured worth, and the rules
— the read-rule law among them, drawn for both read rules. It states the
inner loop as spent for a commitment-free payload and the residue as the
envelope price. One honesty note attached to it here: the page draws the
law's interpolated case as the hull condition while the code implements
the conservative window, so the page currently documents a rule the code
satisfies but does not fully exploit. They should converge, in the code's
direction.

**A fifth instance, found while tidying a record and fixed the same
day.** The compliance sweep itself samples victims on a latitude grid and
reported "worst margin" as if it were the system's. On BL-D2 a 5 deg
sweep found latitude 35 to be 10 dB worse than anything the 10 deg sweep
visited: -43.7 dB against the -33.4 dB the record carried. Not the
band-versus-point defect — the sweep honestly evaluates discrete victims
— but the same family: an extremum that depends on the sampling grid,
quoted without it. The summary now travels with its step and span, and
says a finer sweep can find worse between the sampled points, exactly as
depth already travels with the percentile floor. V42 pins it, including
on the COMPLIANT branch, since passing at the sampled latitudes is not
passing everywhere.

That makes five in one day: certificate, mask rows, MIN_EXCLUDE, sweep
grid, and the granularity walk's latitude axis that rewarded the mask
defect as recovery. I would stop counting instances and treat it as a
property of how this codebase has handled every grid it owns. Your
band-probe pair for the dataset is the right export; I would add the
sweep grid to what it probes.

**Your order is taken, and one item on it is now resolved differently
than you framed it.** The MIN_EXCLUDE hull is a refinement of a safe fix,
not a correctness item; it no longer needs to precede the beam-capacity
field. The cache key did, and is done. So the outer loop is next: the
payload co-frequency beam-capacity field, S.1325-rev §2.5.2-anchored,
enforced by the scheduler and measured into the mask by the probe, with
the K readout — 20 on STEAM-2 against 42 in the colour — as the
calibration of what a case should declare. Then the reuse plan where a
case flies one, as a truer T rather than as margin. Then power against
the filed STEAM-2B level.

**The attribution footnote you asked for is still owed.** The
margin-figure records carry "2.1 dB in the b/c grid" without the
qualification that the latitude share was a category error and the b/c
share is per-case. Footnote, not rewrite, as you said; not yet done.

---

## Critique side — hull algebra verified, and the family's reach named, 6 September 2026

**The pair-condition derivation is checked and correct.** Over the
segment between rows i and i+1, the interpolant's maximum on each
band-half is attained at an endpoint or the midpoint, which gives
exactly your four inequalities; with m = (22, 30) the point (14, 30) is
feasible and the sliding minimum's (22, 22) is not maximal. So the
record stands as you wrote it: what shipped is safe and conservative —
it under-declares the declaration, never the emission — V41's safety
property covers both constructions, and the LP refinement is
correctly parked until a case flies a genuinely varying alpha table.
One nuance on the construction page: converge the two by ANNOTATION,
not by weakening — the page should keep the law stated in full (the
hull condition is the rule) and carry one implementation-status line
saying the code currently satisfies it by the conservative window
without exploiting it. Documenting the weaker rule because the code
stops there would invert the relationship between the two.

**The fifth instance is accepted, and the family reaches further than
this codebase.** "An extremum that depends on the sampling grid, quoted
without it" describes the examination itself: the S.1503 worst-case
geometry search is a grid-sampled extremum by construction, and the
Recommendation's remedy is the opposite of ours — it PRESCRIBES the
grid, trading truth for reproducibility. The distinction belongs in the
record because it decides what a dataset expectation means: a consumer
matching the examination must reproduce the extremum of the prescribed
grid, not hunt the continuum's true worst; while OUR truth-side records
must carry their grid precisely because no prescription protects them.
Your addition of the sweep grid to the dataset probe export is taken;
the probe family is now three: nearest-read band, interpolated hull,
and extremum-vs-grid.

**The -43.7 dB resets BL-D2's outer-loop record.** Worst margin on that
case is now a 5-deg-sweep figure with the caveat V42 pins — and the
compliant-branch half of that pin is the important one: a dataset case
marked PASS is only pass-at-the-sampled-victims, which any consumer of
the BL family needs stated on the case sheet, not discovered here.

Order confirmed as you closed it: the payload beam-capacity field next
(S.1325-rev §2.5.2-anchored, K = 20-vs-42 as the calibration), then the
reuse plan as a truer T, then power against the filed STEAM-2B level.
The attribution footnote stays on your ledger, your own listing.

And on stopping the count at five: agreed, with the closing form of the
lesson stated once — every grid this project owns is either a
DECLARATION (then its read-rule reconstruction must bound the
measurement), or a MEASUREMENT (then its extrema travel with the grid),
or a PRESCRIPTION (then reproducing it exactly is the requirement).
Naming which of the three a grid is, at the moment it is introduced, is
the check that would have caught all five before they ran.

## Beamlab — E1-1 built with its warrant, and measured, 6 September 2026

The payload co-frequency beam capacity exists as a profile field, anchored
to S.1325-rev §2.5.2 as you proposed. One value takes one route — profile,
composer, scene, resolved set — and two consumers read it: the scheduler
refuses a satellite a further link in a colour that already has the
capacity lit, and the reachable-envelope mask sums only that many
same-colour contributions at each cell. V44 pins the four halves,
including that enforcement discriminates (a satellite lighting 6 same-
colour beams free, at most 2 under a cap of 3) and that the uncapped path
is byte-identical to before.

**Controls, per the standing rule.** STEAM-2, global service area, 0.1 d
(floor 0.694%), same derived R set (identical output), same certificate
(11 lit, 0 dark). The profile differs in exactly one field:
CoFrequencyBeamCapacity = 20, the readout's calibration. T is identical at
every latitude, because the readout said it would be: K_obs reached 20
three times in 230 393 satellite-steps, so a cap of 20 essentially never
binds. What moved is therefore the envelope alone.

| lat | T | E1 before | E1 with cap 20 | gap before | gap after | recovered |
|---|---|---|---|---|---|---|
| 0 | -9.1 | -15.6 | -15.6 | 6.5 | 6.5 | 0.0 |
| 10 | -4.8 | -13.7 | -13.7 | 8.9 | 8.9 | 0.0 |
| 20 | -7.4 | -18.1 | -17.8 | 10.7 | 10.4 | 0.3 |
| 30 | -5.7 | -12.5 | -12.1 | 6.8 | 6.4 | 0.4 |
| 40 | -6.9 | -16.9 | -15.9 | 10.0 | 9.0 | 1.0 |
| 50 | -11.9 | -24.1 | -22.8 | 12.2 | 10.9 | 1.3 |
| 60 | -11.7 | -21.9 | -20.1 | 10.2 | 8.4 | 1.8 |

**E1-1 recovers 0.0 to 1.8 dB with full warrant.** Widest gap 12.2 to
10.9 dB. Adequate at every latitude, as it must be: the scheduler binds
what the mask assumes. The flat-sum bound of 3.2 dB was loose by about a
factor of two, which is the top-K decay doing what the pricing argument
said it would — the beams beyond the twentieth in a colour carry little at
any test point.

**Your pre-stated frame lands where it was written.** Low single digits
recovered; the residue — 6.4 to 10.9 dB in band — is the envelope price
proper for this payload at this depth: side-lobe geometry enveloped over
the committed operating space, the quantity Q1 owns. Your top-K
concession was not premature, and my "large fraction of 6.5 dB" from two
rounds ago stays withdrawn.

**The recovery grows with latitude — 0.0 at the equator, 1.8 at 60 — and I
have not established why.** The natural reading is that more same-colour
beams contribute meaningfully at a high-latitude cell, so the tail beyond
the cap carries more there; but that is a reading, not a measurement, and
it goes in the record as such.

**Measured before declared, cashed out.** The K readout gave 20; declaring
20 left T untouched and took the whole envelope effect. Declaring below
K_obs would have moved T — legitimately, as an outer-loop design choice —
and declaring above it would have wasted the lever. That is the doctrine
paying for itself in one run, and it is the reason the readout precedes
the sampler change in the order you set.

**The other half of the field is running now.** A cap of 12, near the
mode of the distribution, binds on roughly a fifth of satellite-steps.
That run answers the operator's question rather than the examiner's: what
committing to a smaller payload buys on E1 and costs on T, both sides
moving, the way a real capacity does. Reported when it lands, with T
named as the thing that moved.

**Records.** The construction page's lever table now states this figure
against the beam-count row and its tech box no longer says the commitment
does not exist; the user guide describes the field. The attribution
footnote on the margin-figure records stays on my ledger.

## Beamlab — a cap that binds at peak and not today: the third regime, 6 September 2026

The second variant is in, and it corrects a prediction of mine while
completing the picture the field was built to draw.

**Controls.** STEAM-2, global service area, 0.1 d (floor 0.694%), same
certificate (11 lit, 0 dark). The profile differs from the record in one
field: CoFrequencyBeamCapacity = 12, near the mode of the SATURATED
distribution. One control does NOT hold and is named: the derived R set
differs. The saturated probe placed 6 560 480 links against 6 575 111
(0.22% fewer) and derived max_co_freq_sat 48 against 59 — the cap bound
at peak. The epfd(down) algorithm does not read MAX_CO_FREQ_SAT, so the
E1 change below is the mask's alone; but the declaration as a whole is
tighter than the record's, and a reader comparing R sets should know why.

| lat | T record | T cap 12 | E1 record | E1 cap 12 | gap record | gap cap 12 | recovered |
|---|---|---|---|---|---|---|---|
| 0 | -9.1 | -9.1 | -15.6 | -14.8 | 6.5 | 5.7 | 0.8 |
| 10 | -4.8 | -4.8 | -13.7 | -12.6 | 8.9 | 7.8 | 1.1 |
| 20 | -7.4 | -7.4 | -18.1 | -16.5 | 10.7 | 9.1 | 1.6 |
| 30 | -5.7 | -5.7 | -12.5 | -10.6 | 6.8 | 4.9 | 1.9 |
| 40 | -6.9 | -6.9 | -16.9 | -14.4 | 10.0 | 7.5 | 2.5 |
| 50 | -11.9 | -11.9 | -24.1 | -21.7 | 12.2 | 9.8 | 2.4 |
| 60 | -11.7 | -11.7 | -21.9 | -18.6 | 10.2 | 6.9 | 3.3 |

**T did not move. To the decimal, at every latitude.** I predicted it
would, and I chose 12 from the wrong distribution. The readout I picked
it from was the SATURATED one — demand at the declared cap of four — and
that is the distribution a declaration must envelope. But the truth sweep
runs the profile's real traffic, one link per cell, roughly a quarter of
the per-colour load. Measured under that traffic the worst colour on any
satellite is 11 (once in 21 840 satellite-steps; mode 3; two thirds at 3
or fewer) and nothing goes unserved. A cap of 12 is never reached today,
so declaring it cannot move today's truth.

**It binds at peak, and that is where its cost is.** Under saturation the
worst colour reaches 12, the scheduler refuses beyond it, and 2.67% of
the offered peak demand goes unserved (17 466 of 655 312 link-steps at
0.01 d). The commitment is real and enforced; its price is blocking at
peak, not epfd today.

**So the field has three regimes, and only one of them was visible
before this run.**

| declared capacity | binds on the truth? | binds at peak? | T | E1 | cost |
|---|---|---|---|---|---|
| >= K_sat (20 here) | no | no | unchanged | -0.0 to -1.8 dB | none |
| between K_truth and K_sat (12 here) | no | yes | unchanged | -0.8 to -3.3 dB | peak blocking (2.67%) and a tighter R set |
| < K_truth (below 11 here) | yes | yes | moves | tighter still | today's service, as unserved demand |

The middle row is the one an operator would want to know about: a
commitment the system already honours at today's traffic, that buys 0.8
to 3.3 dB on the examination, and whose only cost falls on the busiest
hour. It is also the row the previous entry's framing — "moves T exactly
as a real capacity does" — did not anticipate, because that framing
assumed the cap would be set below what the system does today. Whether
it does depends on which side of K_truth the operator declares.

**The readout now measures both distributions.** A `truth` mode reports
the real-traffic per-colour count, and both modes report whether the cap
was reached and how much offered demand went unserved. Choosing a cap
from the saturated distribution says what the declaration may envelope;
choosing from the truth distribution says what declaring it costs today.
They answer different questions, and I conflated them for one run.

**The third regime is running**: capacity 4, which the truth distribution
says binds on about a fifth of satellite-steps. T should move there, and
the record will name it as the thing that moved.

**Adequacy held at every latitude in both variants**, as it must: the
scheduler binds exactly what the mask assumes, so the envelope cannot
fall below the system it describes. That is the warrant working, not a
coincidence.

## Beamlab — the third regime: a cap that binds today moves T both ways, 6 September 2026

Capacity 4, chosen from the TRUTH distribution this time, where it binds
on about a fifth of satellite-steps at the mode and, it turns out, pins
the worst colour at exactly 4 on 45% of them. Controls: STEAM-2, global
service, 0.1 d (floor 0.694%), same certificate. Two controls do not hold
and are named below, because they are part of the result.

| lat | T record | T cap 4 | E1 record | E1 cap 4 | gap record | gap cap 4 |
|---|---|---|---|---|---|---|
| 0 | -9.1 | **-7.6** | -15.6 | -11.7 | 6.5 | 4.1 |
| 10 | -4.8 | -4.8 | -13.7 | -10.2 | 8.9 | 5.4 |
| 20 | -7.4 | -7.4 | -18.1 | -14.2 | 10.7 | 6.8 |
| 30 | -5.7 | -5.7 | -12.5 | -7.6 | 6.8 | 1.9 |
| 40 | -6.9 | -7.0 | -16.9 | -11.7 | 10.0 | 4.7 |
| 50 | -11.9 | **-12.8** | -24.1 | -19.6 | 12.2 | 6.8 |
| 60 | -11.7 | **-12.4** | -21.9 | -15.6 | 10.2 | 3.2 |

**T moved, as predicted — and in both directions, which I did not
predict.** Better by 1.5 dB at the equator, unchanged from 10 to 30,
worse by 0.7 to 0.9 dB at 50 and 60. The truth-mode readout says why.
Under real traffic the cap binds constantly (45% of satellite-steps sit
at 4), yet only 2 of 163 828 link-steps go unserved: a refused link is
not dropped, it is REASSIGNED to the next-best satellite. The
interference is not thinned, it is moved — and at some victims the
satellite it moves to sits in worse geometry. A tighter payload
commitment is not monotone for compliance. That is the operator-facing
result of this run, and it is not one the "moves T as a real capacity
does" framing anticipated in either direction.

**E1 drops 2.4 to 6.9 dB, widest gap 12.2 to 6.8, adequate everywhere.**
But this is NOT the mask's doing alone, and here is the second control
that fails: the saturated probe under cap 4 placed 3 493 938 links
against 6 575 111 — 47% of peak demand unserved, because at saturation
there is no spare satellite to reassign to — and derived a different R
set: max_co_freq_sat 16 against 59, and min_exclude 26.5 deg at the
35-deg band and 33.0 at 45, against 22.0 everywhere on the record. The
examination READS MIN_EXCLUDE, so part of the high-latitude E1 gain is
the tightened exclusion, not the top-4 envelope. I have not separated the
two, and this entry does not claim to. What is safe to say is that the
derived declaration tightened by measurement, the scheduler honours what
it declares, and adequacy held — the warrant intact at the extreme.

**Why the exclusion tightened is not established.** With a Random
selection policy the refused links should be random, so a systematic
shift of the surviving links toward larger alpha at high latitude is
unexplained. It goes in the record as an observation.

**The field, complete, on STEAM-2 at 0.1 d:**

| capacity | binds today | binds at peak | T | widest gap | today's cost | peak cost |
|---|---|---|---|---|---|---|
| 20 = K_sat | no | no | unchanged | 12.2 -> 10.9 | none | none |
| 12 | no (worst 11) | yes | unchanged | 12.2 -> 9.8 | none | 2.67% unserved; max_co_freq_sat 59 -> 48 |
| 4 | yes (45% pinned) | yes | +1.5 to -0.9 dB | 12.2 -> 6.8 | ~0 unserved; interference redistributed | 47% unserved; max_co_freq_sat 59 -> 16; exclusion tightened |

The middle row remains the one an operator would choose. The bottom row
maps the extreme and says what it costs: half of peak demand, and a
compliance position that improves at some victims and worsens at others.

**Measured before declared, twice.** The saturated readout said what may
be enveloped; the truth readout said what a cap binds on today. Picking
12 from the first and 4 from the second is what made the regimes visible
at all. The tool now reports both distributions and both costs.

## Beamlab — the cap-4 gain separated: it is the envelope's, not the exclusion's, 6 September 2026

The previous entry left the cap-4 E1 gain unseparated: the derived R set
had tightened min_exclude to 26.5 and 33.0 deg at the 35 and 45 deg
bands, the examination reads that array, and I said part of the
high-latitude gain might be the declaration's rather than the top-4
envelope's. That is now measured, and the suspicion was wrong in size.

**The instrument.** An examination-only mode: a named R set against a
named mask over the sweep grid, no probe, no truth sweep — what an
administration does with a filing. It lets one artefact change while the
other is held fixed, which the full loop cannot do because it derives
both from one probe. Two reproductions bracket the control:

| run | R set | mask | E1 at 0 / 10 / 20 / 30 / 40 / 50 / 60 |
|---|---|---|---|
| (a) | record | record | -15.6 / -13.7 / -18.1 / -12.5 / -16.9 / -24.1 / -21.9 |
| (b) | **record** | cap 4 | -11.7 / -10.2 / -14.2 / -7.6 / -11.7 / -19.6 / **-15.5** |
| (c) | cap 4 | cap 4 | -11.7 / -10.2 / -14.2 / -7.6 / -11.7 / -19.6 / **-15.6** |

(a) reproduces the committed record bit for bit, so the mode is sound
and the record's mask file is the band-fixed one. (c) reproduces the
cap-4 record bit for bit. Between them, (b) is the control.

**The exclusion's share is 0.1 dB at one latitude and nothing at the
other six.** (b) and (c) differ only at latitude 60, by 0.1 dB, at a
percentile where the bins are 0.1 dB wide — sign not meaningful. The
max-epfd column is identical at all seven. So the tightened min_exclude
did not move the examination, and the whole cap-4 E1 gain is the
envelope's: **3.5 to 6.4 dB** per latitude ((a) to (b)), growing with
latitude as the cap-20 result did. The failed control in the previous
entry is resolved, and the regime table's bottom row may be read as a
mask result without qualification.

**A correction of my own range.** I wrote that E1 "drops 2.4 to 6.9 dB"
at cap 4. That was the change in the GAP, which includes T's movement
(+1.5 at the equator, -0.9 at 50). The change in E1 itself is 3.5 to
6.4 dB. Both are true of different quantities; the entry named the wrong
one.

**Why the exclusion did not bite is not established** — the R set the
examination read moved from 22 to 33 deg at latitude 45 and E1 did not
notice. The likely reading is that satellites inside 22 to 33 deg of the
GSO arc contribute little at these victims once the top-4 envelope has
already thinned the colour, so gating them changes nothing that matters;
but that is a reading. Recorded as observed.

**The instrument's cost is the other finding.** Each examination took
seconds — 0.0 to 0.1 min for 144 steps across seven latitudes and 1600
satellites. The loop's 25 to 29 minutes are the saturated probe over
11 702 cells and the truth sweep's live composition; the examination
itself is essentially free. Examining a filing is cheap. Deriving one is
what costs, and that is where the depth and grid rules earn their keep.

**Bookkeeping.** With the exclusion's share measured at ~0, the three
regimes stand as tabulated, and the middle row — 0.8 to 3.3 dB for 2.67%
of peak demand, T untouched — remains the operator's choice. Examination
records for the three controls sit beside the artefacts under
dataset/margin/examine/ and are cited from here rather than filed as
figures.

## Beamlab — the filed STEAM-2B mask examined: the failure was the assumption's, and the filing has a question of its own, 6 September 2026

The last item on your order. The filed STEAM-2B pfd mask — the operator's
actual declaration, 88 MB, az/el, 179 latitude blocks — examined the
administration's way: the epfd(down) algorithm reading that mask and an
R set over the sweep grid. No probe, no truth sweep. It ran in seconds.

**Controls.** Same profile, geometry and victim configuration as every
STEAM-2 record (earth station at longitude 0, GSO at +10 deg, 1.00 m
dish, the row's own diameter), same limit row (TABLE 22-1B, FSS
17800-18600 MHz, at the profile's 18.15 GHz), same R set as the record —
our derived one, which declares exactly what the mask dissection found
encoded in the filing itself: min_elev 40 deg, alpha 22 deg constant,
Nco 4. Depth 0.1 d for the like-for-like against T and E1, then 1.0 d
(floor 0.069%) for the filing's own verdict.

| lat | T (assumed payload) | E1 (our derived mask) | E_filed (their mask) | E_filed - T |
|---|---|---|---|---|
| 0 | -158.0 | -151.5 | -173.6 | -15.6 dB |
| 10 | -166.0 | -158.9 | -173.3 | -7.3 |
| 20 | -159.7 | -149.1 | -173.2 | -13.5 |
| 30 | -164.5 | -155.3 | -173.6 | -9.1 |
| 40 | -160.2 | -150.2 | -173.7 | -13.5 |
| 50 | -157.5 | -146.2 | -168.9 | -11.4 |
| 60 | -158.3 | -148.1 | -169.7 | -11.4 |

(max epfd, dB(W/m2)/40 kHz, 0.1 d)

**First answer: the in-band T failure belongs to the assumed payload.**
The filed declaration sits 7 to 16 dB BELOW our truth at every latitude.
In this project's own terms that is an adequacy failure — a declaration
that does not envelope the system it is paired with — but the system it
is paired with is OURS, not theirs: scene-default pattern and layout,
Gm 35 assumed, power set to meet the filed cap at boresight, 40 deg
elevation from 4A/653 rather than a filing. Our assumed system radiates
far more than STEAM-2B filed, and our own derived mask is 12 to 18 dB
more conservative than their filing. Both point the same way, and the
parity run had already pointed there: our all-beams side-lobe floor sat
19.5 dB above theirs. The operator's system, as declared, is much quieter
than the one we have been failing. Whether the real payload is quieter
because it lights few co-frequency beams, or because its side lobes fall
faster than a Taylor pattern with a 5 dBi floor, the filing does not say;
it declares the consequence and not the cause.

**Second answer, unasked for: the filing itself exceeds TABLE 22-1B
under this examination.** Not by much at low latitudes, more at high:

| lat | margin at 0.1 d | margin at 1.0 d |
|---|---|---|
| 0 | -1.2 | -1.0 |
| 10 | -0.9 | -0.9 |
| 20 | -0.8 | -1.0 |
| 30 | -0.5 | -0.5 |
| 40 | -1.2 | -1.3 |
| 50 | -6.2 | -6.2 |
| 60 | -4.9 | -4.9 |

Ten times the depth moved no margin by more than 0.2 dB. This is not a
percentile-floor artefact. It is, however, a statement about someone
else's filing, and it carries every one of these caveats in the same
breath: the R set paired with the mask is OUR derivation, not the
operator's filed set — a larger exclusion or a smaller cap on their side
would lower E_filed; the victim configuration is our sweep's single
earth-station longitude, GSO offset and dish, not the examination's
full grid; one limit row was applied, at 18.15 GHz, while the mask's
band reaches 20.2 GHz where TABLE 22-1C governs; and the mask-read
verdict path is our implementation of Sec. D5.1.4.1 and D5.1.5, which
the oracle has validated for the geometry-and-selection chain and not
for the reading of a real filing. Under those conditions the filed mask
fails by 0.5 to 1.3 dB at 0 to 40 deg and 4.9 to 6.2 dB at 50 and 60.
Under the BR's conditions it may not. The number is offered as a
question to put to the filing, not as a verdict on it.

**Third finding, about our own figures: the derived mask does not
converge at 0.1 d; the filed one does.** Our E1 at the two depths:

| lat | E1 at 0.1 d | E1 at 1.0 d | moved |
|---|---|---|---|
| 0 | -15.6 | -18.1 | -2.5 |
| 10 | -13.7 | -17.7 | -4.0 |
| 20 | -18.1 | -18.0 | +0.1 |
| 30 | -12.5 | -18.6 | -6.1 |
| 40 | -16.9 | -18.8 | -1.9 |
| 50 | -24.1 | -20.0 | +4.1 |
| 60 | -21.9 | -21.4 | +0.5 |

The filed mask is a two-level rule — plateau and a floor 30 dB down —
so its epfd distribution is set by how many satellites are in view and
settles at once. Ours carries side-lobe structure whose tail needs
samples, and at 144 steps it had not got them: per-latitude E1 is soft
by 4 to 6 dB between 0.1 d and 1.0 d, in both directions. Every
comparison this week held depth fixed, as the rule requires, so the
recovered-dB figures stand as differences. The ABSOLUTE in-band gap
quoted at 0.1 d — 6.5 to 12.2 dB — does not; it needs T at 1.0 d to
restate, and a truth sweep at that depth is the expensive run this
campaign has not made. That is the next thing to buy, and it is the
only thing on this list that costs hours rather than seconds.

**What this does to the campaign.** The system we have been optimising
declarations for is not the one STEAM-2B filed; it is roughly 10 dB
hotter. The three-regime result, the certificate, the band fixes and
the examination mode are all sound as construction — none of them
depended on which system was in the profile. But the STEAM-2 numbers
should now be read as numbers about the assumed payload, and the
operator-side question becomes the payload itself: beam count, pattern,
floor. The parity run said that a week ago; the filed mask now says it
in the examination's own currency.

**For the critique side.** Five things want your judgement, in the order
the entries above raise them:

1. The co-frequency beam capacity as a declared, binding commitment, and
   the three regimes as tabulated — nothing binds above K_sat; binds at
   peak only between K_truth and K_sat, T untouched; binds today below
   K_truth, T moving both ways. Is the third regime rightly placed on the
   design side of the line, a decision about the system rather than a
   description of it?
2. The exclusion's share at cap 4 measured at ~0 dB while min_exclude
   moved from 22 to 33 deg at latitude 45. Recorded as observed, with a
   reading and no explanation. Is the reading sound, and is there a
   cheap check that would settle it?
3. The derived mask's E1 did not converge at 0.1 d — up to 6 dB per
   latitude by 1.0 d, in both directions — while the filed two-level
   mask converged at once. Does the depth rule need a per-artefact
   convergence test before any absolute figure is quoted, and how would
   you state it?
4. The filed STEAM-2B mask, read by our D5.1.4.1 path with our derived
   R set, exceeds TABLE 22-1B by 0.5 to 6.2 dB, firm across ten times
   the depth. That read path has no oracle behind it for a real filing.
   Can the radians examination read the same file, same R set, same
   victim, so the verdict has an independent reader?
5. Your band-rule probe dataset case still awaits the operator's
   decision; nothing has changed there.

---

## Critique side — the five answers, 7 September 2026

First, the instrument note: the examination-only mode is the missing
referee tool, and entry two is the control rule working exactly as
codified — a suspected confound named in one entry, bracketed with (a),
(b), (c) and measured to ~0 in the next. The cap-4 result now reads
clean: the envelope's 3.5-6.4 dB, T moving both ways, exclusion inert.

**1. Yes — the third regime is design-side, and the run supplies the
criterion that decides it.** The line is not "does it move T" and never
was "which way": it is whether the knob changes what the system DOES.
A cap that binds today changes the granted links — reassignment is a
different interference geometry, which is why T moved in both
directions. So: a commitment is declaration-side iff today's granted
links are unchanged under it — it binds only beyond current operation,
which is precisely what a commitment is, a promise about the extreme
rather than a description of the present. Regime 1 tightens only the
envelope (links identical: declaration). Regime 2 binds at saturation
only (links identical today, probe changes: declaration, and the honest
kind — the promise costs 2.67% of a peak the system may never sell).
Regime 3 changes today's links: a redesign, to be chosen or refused as
engineering. The non-monotonicity is the empirical proof the line
matters: a knob that can worsen compliance cannot be a
declaration-tightening move, because declarations never change
behaviour.

**2. The reading is sound, and the mechanism is one sentence: the mask
already encodes the exclusion.** The derived pfd mask bakes the alpha
notch, so satellites between 22 and 33 deg of the arc carry
mask-suppressed values; widening MIN_EXCLUDE re-labels their selection
status without changing what they contribute, and the top-4 envelope
has already thinned exactly the population the wider zone would gate.
The R-set exclusion's epfd bite is therefore conditional on the mask
NOT carrying the notch — which is why it measured ~0 here and why it
must not be generalised to filings whose masks are flat near the arc.
Cheap check, no new run type: examination-only, record mask held fixed,
R-set min_exclude forced to 0 — if E1 stays put, the array is confirmed
epfd-inert for notched masks; a per-step readout summing the mask reads
of satellites whose status flips between 22 and 33 deg settles the
high-latitude drift question with arithmetic instead of a theory. Why
the SATURATED probe under cap 4 measured larger surviving alpha at high
latitude stays open; it is a derivation observation, not an examination
effect, and it no longer contaminates any number.

**3. Yes, and state it as the depth rule's second half.** The percentile
floor guards a percentile's EXISTENCE; nothing yet guards a value's
STABILITY, and the filed-vs-derived contrast shows stability is a
property of the artefact (a two-level mask settles instantly; a
side-lobe-structured one has tails that need samples). Rule, in the
pre-registered form the control rule demands: an ABSOLUTE figure is
quotable only with a convergence pair — the same statistic at depth d
and at >= 2d, moving no more than a stated tolerance (0.1 dB is the bin
floor; 0.5 dB is the pragmatic default), both depths and the tolerance
named where the figure is quoted. Differences at matched depth remain
quotable as differences, as this week's were. Consequence accepted with
it: the 6.5-12.2 dB in-band gap is a 0.1 d figure awaiting its pair,
and the 1.0 d truth sweep is the purchase that converts the campaign's
absolutes from provisional to firm. Buy it once, on the assumed-payload
profile, and every standing figure either survives or gets its
correction in one run.

**4. Yes — radians can be the independent reader, and the path exists
today.** The configuration the filed mask needs is expressible in
S.1503-2 terms, which radians implements end to end: the az/el pfd mask
format is native, min_elev 40 is grp.elev_min, alpha 22 is
f_x_zone/x_zone, Nco 4 is the sat_oper table, and TABLE 22-1B with the
1 m dish comes from the same WRC-23 limits database radians ships. For
this configuration — no min_duration, constant alpha and Nco — the -2
selection (Step 20: Nco largest eligible plus exclusion-zone
satellites) coincides with the D5.1.4.1 reading your path implements,
so the comparison is semantically fair. Recipe: emit a case package the
BL way — SRS notice carrying the STEAM-2 shell and those group
parameters, Masks.MDB carrying the filed 88 MB mask verbatim — and
radians runs the seven victims as Additional Tests (entered geometry,
WCG search skipped), same GSO at +10, same dish. I take the radians
side of that as an action item when the package lands. Two flags for
the fairness of it: check which angle metric the filed az/el mask keys
its exclusion to before equating x_zone semantics; and note our
examination used min_elev 40, which is GENEROUS to the filing — fewer
eligible satellites, lower E_filed — so the exceedance is unlikely to
be an artefact of the one assumption known to be unfiled. The caveats
you attached stand; an independent reader converts "a question to put
to the filing" into "a question two implementations put to the filing",
which is the strongest statement our tools can make.

**5. Relayed to the operator again, unchanged** — the band-rule probe
pair (nearest-read and interpolated-hull, now plus the sweep-grid
probe) as dataset cases sits on the operator's desk, not ours.

**On the campaign consequence, agreed in full:** the construction —
regimes, certificate, band fixes, examination mode, adequacy test —
stands independent of the profile that exercised it; the STEAM-2
figures are re-labelled as assumed-payload results; and the operator
question is now the payload (beam count, pattern, floor), where the
parity run pointed a week ago and the filing now points in the
examination's own currency. The 1.0 d truth sweep and the radians
cross-read are the two purchases that close this chapter's books.

---

## Operator decision — the read-rule probes go into the dataset, 7 September 2026

Question 5 is decided: the three probe cases — nearest-read, interpolation,
sweep-grid disclosure — are now section 3.9 of the dataset design brief
(radians commit a42ea40), each specified so the read rule is the only thing
that decides the verdict, statuses GAP until the generator emits them. They
double as acceptance tests for any consumer's resolution layer, radians'
included. The BL generator is the intended producer.

## Beamlab — the exclusion measured inert, the convergence pair taken, two purchases in flight, 7 September 2026

Your cheap check, run before anything else. Examination only, the
record mask held fixed, the record R set with every MIN_EXCLUDE row
forced to 0 — no exclusion zone at all — otherwise identical, 0.1 d,
seven latitudes:

| lat | E1, record R set | E1, min_exclude 0 | max epfd moved |
|---|---|---|---|
| 0 | -15.6 | -15.6 | 0.0 |
| 10 | -13.7 | -13.7 | 0.0 |
| 20 | -18.1 | -18.1 | 0.0 |
| 30 | -12.5 | -12.5 | 0.0 |
| 40 | -16.9 | -16.9 | 0.0 |
| 50 | -24.1 | -24.1 | +0.1 |
| 60 | -21.9 | -21.9 | 0.0 |

Removing the zone entirely moved no margin. The array is epfd-inert for
this mask, as you read it: the derived mask already carries the alpha
notch, so the satellites the zone would gate contribute mask-suppressed
values whether the R set calls them operating or not. The +0.1 dB in max
epfd at latitude 50 is consistent with the mechanism — with no zone, the
notch satellites become eligible for the top-Nco pick and one of them
enters with a suppressed value — and it is the same 0.1 dB the cap-4
control showed at latitude 60. Both are the notch admitting a satellite
that has nothing to add. The caveat travels with the result: inert for a
notched mask, and only for one. Against a filing whose mask is flat near
the arc the zone is the only thing suppressing those satellites, and it
bites. The construction page's control box now says so, and the
saturated probe's larger surviving alpha at high latitude under cap 4
stays open as a derivation observation, contaminating nothing.

**The regime criterion, taken verbatim.** Declaration-side if and only
if today's granted links are unchanged under the commitment. That is
sharper than "does T move" and it is mechanically testable: the loop
knows the granted link set of every run, so "links unchanged between the
record and a variant" is a readout, not a judgement. Not built yet;
noted as the check that would make the criterion an instrument.

**The convergence pair, taken as the depth rule's second half.** The
pre-registered form as you gave it: an absolute figure is quotable only
with the same statistic at depth d and at >= 2d, moving no more than a
stated tolerance — 0.5 dB by default, 0.1 dB the bin floor — with both
depths and the tolerance named where the figure is quoted; differences
at matched depth remain quotable as differences. Applied to what stands:

| figure | pair | status |
|---|---|---|
| filed-mask margins, seven latitudes | 0.1 d / 1.0 d, worst move 0.2 dB | firm |
| record mask E1, seven latitudes | 0.1 d / 1.0 d, moves up to 6.1 dB | provisional |
| T, seven latitudes | none yet | provisional; run in flight |
| E1 - T in band, 6.5-12.2 dB | none | provisional |
| cap-20 / cap-12 / cap-4 E1 gains | differences at matched depth | stand as differences |
| exclusion share ~0 | difference at matched depth | stands |

**Purchase one, launched.** The loop at 1.0 d — 1440 steps of 60 s,
floor 0.069% — on a name-only copy of the STEAM-2 profile, so the 0.1 d
record and its artefacts stay where they are. It runs the whole
iteration at depth: saturated probe, truth sweep, examination. Expected
four to five hours from the 0.1 d wall clock. When it lands it yields
T's pair, which is what the standing absolutes wait on; and something
the truth sweep alone would not have: the derivation's own stability —
the 1.0 d mask and R set against the 0.1 d ones, row by row. Since the
0.1 d mask has already been examined at 1.0 d, the two masks at one
depth separate the derivation's depth sensitivity from the
examination's. Every standing figure survives or gets its correction in
the one run, as you said.

**Purchase two, taken.** The package for your independent read. The
pieces exist on this side: the notice builder takes a shell from the
orbit design, the SRS writer and the mask writer already produce the BL
databases through the BR API, and the operating-parameters XML is an
artefact of every run. What is unknown until tried is whether the mask
API accepts the 88 MB az/el file verbatim. Your two flags are on the
list: the dissection read the notch as alpha 22 at the satellite, and
whether it is equally an X-angle notch is checkable from the same cells
before the package goes out; and min_elev 40 being generous to the
filing is recorded with the result, so an exceedance under a generous
assumption is read for what it is.

**Section 3.9, acknowledged.** The three probe cases are ours to emit,
the BL generator is the producer, and they queue behind the package.
Each has a natural ancestor in this week's fixes — the certificate for
the nearest-read probe, V41's raw-versus-safe MIN_EXCLUDE for the
interpolation probe, V42's travelling worst margin for the sweep-grid
probe — so the expectation records can be written from checks that
already pass rather than from theory.

---

## Critique side — masks do not take gates after the fact: the adaptation rule, 7 September 2026

Closing the exclusion-inertness thread with the general answer behind it,
raised by the operator: STEAM-2B's min_exclude was inert because the filed
mask already carries the notch — the mask was the given, and T with it. May
the opposite case exist: a mask supplied as SATURATION (full load, no
victim shaping) that the toolchain adapts to a provided min_exclude,
min_elev and so on? Mostly no, and the no is structural.

**Why gates cannot be applied to a mask after the fact.** A pfd mask is a
per-direction envelope: each cell stores one number, the maximum composite
pfd over the permitted beam configurations. The R-set gates act on
BORESIGHTS — configurations — not on emission directions. Carving the
exclusion notch into a saturated mask means removing main-lobe
contributions at in-zone directions while keeping the side lobes of beams
legitimately serving out-of-zone cells; that needs to know which
configuration produced each cell's max, and the max has already collapsed
exactly that. The envelope does not invert. Boresight-selective gates
(min_exclude, azimuth-dependent min_elev) are therefore unrecoverable from
the artefact alone — not for lack of data quality, but because the
information was destroyed by construction.

**The one warranted mask-level adaptation.** Gates that eliminate entire
configurations by pure geometry survive the collapse: where no servable
cell exists from a sub-satellite latitude (service span, coverage geometry,
elevation floor), the satellite radiates nothing — no beams, hence no side
lobes — and the whole row goes to the C1 null. That is the dark-row
certificate, and it is the complete list. It worked without a payload model
precisely because it zeroes configurations, not contributions.

**What saturation-mask-plus-operational-gates actually is: an inconsistent
filing, to be detected rather than repaired.** Direction of error first: an
unadapted saturated mask is an envelope of a superset, so the examination
stays conservative — it over-charges the operator, never under-protects the
GSO. But it over-charges brutally: in-zone satellites enter as
always-include spillover carrying main-beam-grade values. A declaration
pair of min_exclude 22 deg beside a mask lit at alpha 5 deg is
self-inconsistent, and the response is a consistency verdict, not surgery —
the 4A/937 mask-vs-parameters C/N test is the published shape of that
check, and France's 4A/945 scenario B is the precedent of a deliberately
inconsistent case built to see what implementations do. The dataset should
carry one such mask-shaping-consistency probe alongside the read-rule
probes.

**With an assumed pattern, a modeled reshaping is constructible — where we
own the truth, and only there.** Assume a beam model and lattice and one
can subtract the main-lobe class near the zone and floor at an estimated
side-lobe level. For SYNTHESIZING dataset cases that is legitimate: the
assumption is the truth generator, which is what the saturated probe
already does properly — adaptation done right is re-derivation from the
payload under the commitments, never surgery on the artefact. For judging a
REAL filing it is not: the assumed pattern would be doing the judging, and
this week measured what an assumed payload can sit from a filed one.

**The rule, one line.** Mask given: use as-is (conservative), null the
certifiable dark rows, flag gate/mask inconsistencies. Payload given: never
adapt the mask — re-derive it.

---

## Operator decision — consistency probe adopted; priority returns to the composition model, 7 September 2026

Two rulings.

The mask-shaping-consistency probe goes into the dataset design brief as
its section 3.10 (radians commit 5c21563), beside the read-rule probes:
a saturation-shaped mask paired with operational gates, the inconsistency
named in the expectation record together with the conservative verdict a
consumer that examines it anyway must reproduce.

And a course correction. The campaign has concentrated too heavily on
STEAM-2B under the pfd-mask option. That thread has delivered what it can
deliver — the assumed-payload relabelling, the filed mask's own question
recorded with its caveats, the adaptation rule — and it parks here.
Priority is the model built from the OPERATION PROFILE describing beam
composition, the raw system data: truth computed from the composed beams;
declarations derived from the flown and saturated operation; the payload
co-frequency beam-capacity field; the convergence purchase (the 1.0 d
truth sweep) spent on composition-driven cases; and the BL family emission
the brief now specifies, read-rule and consistency probes included. In
this program masks are DERIVED artefacts, never inputs. The mask-as-input
mode stays in the toolbox for parity runs and for examining a real filing
when asked — it is not the campaign's centre.

## Beamlab — the cross-read package built, and a gap it found on the way, 7 September 2026

Purchase two is on the shelf: `dataset/STEAM-2B-FILED/`, built the BL
way by the dataset tool's new `--package` mode from three files — the
STEAM-2 orbit design, the record's derived R set, and the filed mask.

**What it holds.** `900123480 SRS.MDB`: 32 orbit rows and 1600 phase
rows for the reconstructed constellation; one down scenario over
17700-20200 MHz linking mask 150 to every orbit; sat_oper rows;
mask_info for the pfd mask (P, az/el) and the operating parameters
(R); mask_lnk3 to param 1. `900123480 Masks.MDB`: the filed mask and
the R set. `xml/`: the mask copy and `param1_oper.xml`. `expected/`:
the record you already have, both depths, caveats attached. A README
states what is verbatim, what is reconstructed, and what is ours.

**The BR native store took the 88 MB az/el mask as it is.** Status 0,
f_mask_type written by the store itself, and the extractor returns it
byte-identical — the copy differs from the operator's file in exactly
the seven bytes of the root `ntc_id`. So the container fallback was
not needed, and your reader gets the mask through the same library it
uses for every filing.

**Your -2 fields are there.** Your reader takes min_elev from
`grp.elev_min` through a query that joins `srv_cls`, the exclusion
zone from `non_geo.f_x_zone`/`x_zone`, and Nco from `sat_oper`. The
package writes all of it after the notice: `non_geo.f_x_zone = Y`,
`x_zone = 22`; one emission group (`grp` 901123480, 17700-20200 MHz,
`elev_min` 40) with its `freq` and `srv_cls` (EK) rows; `sat_oper` as
the nearest-row reconstruction of max_co_freq[lat] — midpoints between
rows, the end rows carried to the poles, so latitude 60 reads 4 as it
did in our sweep. Where an array varies the single-valued form would
carry the smallest angle, the victim-conservative reading; here the
arrays are flat and nothing is lost. `Y` is the earth-station alpha per
your Scenario reader, which is the angle our derivation and the mask
dissection use.

**The gap.** Building the notice found that `AddShell` declared the
Walker phasing only: a shell with an exact inter-plane phase — STEAM-2
flies 1.9 deg — was propagated as one system and would have been filed
as another. Now mirrored, and V45 pins the declared phase rows to the
constellation's own initial phases for both phasing forms (the package
shows it: plane 2, satellite 1 at phase 1.9). No BL shell carries the
field, so their packages are unchanged.

**Your two flags, carried.** The alpha-versus-X question is stated in
the README as an open check, with the evidence for alpha (the notch
constant in earth-station alpha across latitudes) and the absence of a
test in X. The min_elev-40-is-generous point sits in the caveats
beside the R-set and constellation caveats.

**One labelling choice to know about.** The R set was derived at one
frequency (18150 MHz, the sweep's) and is declared in the package over
the mask's whole band. Its values are geometric and do not depend on
frequency; the band label is the only thing widened, and the README
says so.

The seven victims are in the README as entered geometry: earth station
at longitude 0, latitudes 0-60 by 10, GSO at +10, 1.00 m dish, TABLE
22-1B at 40 kHz. The package is yours to run.

**Operator addendum, same day:** the mask-shaping-consistency check is
wanted as a standing VERIFICATION TOOL against declarations, not only as a
probe case — recorded in the brief's tooling section (radians cd75bcc): the
section 3.10 checks run as a tool, detection only, doubling as the
self-check on every emitted dataset case and as an examination-side
pre-check on incoming filings. The examination-only mode is its natural
host.

## Beamlab — the package, corrected to the operator's direction: raw XML, no Masks database, 7 September 2026

Two notes on the entry above, which landed in commit 85cdc3e under a
message about a consistency addendum — the commit's content is that
entry; the addendum's text is not in the file.

**Operator direction, applied.** "We don't need to package masks into
mdb, we can use raw xml." The package now carries the filed mask and
the R set as raw XML under `xml/` and no Masks database; the SRS notice
stays, with mask_info registering mask 150 and param 1 so the links
resolve to the files. The finding that the BR native store took the
88 MB az/el mask as it is stands as recorded and is no longer used.
`dataset/STEAM-2B-FILED/` is regenerated in that form: `900123480
SRS.MDB`, `xml/mask150_pfd_azel_filed.xml`, `xml/param1_oper.xml`,
`expected/`, README.

**Where this leaves the thread.** Per the operator's ruling above, the
STEAM-2B pfd-mask thread parks here with the package on the shelf for
the cross-read when radians runs it. The 1.0 d run in flight is a
composition-driven case — truth from the composed beams, declaration
from the saturated probe — so it is the convergence purchase spent
where the ruling says to spend it. The harness stands at 137 passed
with V45 pinning the declared phase rows to the flown ones.

---

## Operator decision — BL family re-emission, version stamps, both curves, 7 September 2026

Recorded as the generator work item, from the operator's question "can the
masks themselves move?" — they can and already have: the BL-* cases emitted
on 29 August carry masks from before the band-enveloping fix, i.e.
point-sampled rows under-declared in exactly the way the adequacy test later
caught on BL-D2 (2.3-7.4 dB there). The shipped family is stale against its
own generator. The discipline and the work item:

1. **Masks move freely between revisions, never within one.** A dataset case
   is a frozen triple — masks, R set, expectation records — internally
   consistent at emission. Regeneration produces a new revision, all three
   re-derived in one pass; nothing mutates a shipped case.
2. **Every emission carries its provenance stamp.** The case sheet states
   the producer version (the cache-key hash of the generating assemblies —
   the existing mechanism), the mask grids, and the expectation depths, so
   "the masks moved" between revisions is versioned, never silent.
3. **Each downlink case ships BOTH curves.** The truth CDF (composition,
   stable across derivation changes — it moves only with the system
   definition) anchors the case's physics; the examination-read CDF (the
   examination-only mode over the shipped masks and R set, at stated depth
   and grid, with its convergence pair) is the bit-comparable expectation a
   consumer must reproduce. Consumers are validated against the shipped
   artefacts, not against beamlab's living code.
4. **One re-emission of the whole family, after the beam-capacity field
   lands** — so a single pass picks up the band-enveloping fix, the
   service-span certificate, the converged grids (b/c 1, lat 5), the
   capacity field, and the new probe cases the brief now specifies
   (read-rule 3.9, consistency 3.10). Until then the 29 August artefacts
   stand as structure examples only and their READMEs should say so.

## Beamlab — the consistency check built, and three corrections it forced on the way, 7 September 2026

The operator placed the mask-versus-gates consistency check inside the
compliance loop, for the case where the pfd mask is given and not
composed, and confirmed the line the adaptation rule draws: a declared
mask cannot move; a derived mask moves inside the loop. Built as
directed — it runs on the loop's mask-source path and in the
examination-only mode, and writes a "Mask consistency" section into
both records. Per latitude block of an az/el mask it measures how far
inside the declared MIN_EXCLUDE zone, and how far below the declared
MIN_ELEV floor, power within 3 dB of the block's peak reaches, and
grades the answer: CONSISTENT (the mask's own edge sits at the gate),
MASK TIGHTER (dark beyond the gate: the gate is inert), LIT INSIDE
(near-peak power reaches inside and stops short of the arc or horizon),
SATURATED (it reaches the arc or the horizon: no shaping at all — the
section 3.10 case). The filed STEAM-2B mask reads CONSISTENT against
the record's R set: 109 blocks consistent, 70 not exercised where no
cell reaches the zone, elevation consistent at all 179. V46 pins the
scheme on that filing with gates declared wider, narrower, higher and
lower than its edges.

Building it forced three corrections, each measured.

**1. The inert exclusion had the wrong explanation.** I wrote, and the
construction page said, that the derived mask carries the alpha notch.
The dissection of the record mask says otherwise: near-peak power
reaches alpha 11.7–16.4 deg against the declared 22, and ground
elevation 37.3 deg against the declared 40, at every lit block. That is
the main-lobe edge of beams gated at their boresight reaching the
neighbouring ground at 183 km cells — the parity run saw the same
spillover into the filed mask's hole on 3 September. The mechanism of
the inertness is therefore the algorithm's, not the mask's: Step 22
counts main-beam satellites regardless of the zone, and when the zone
is lifted the same satellites, being the strongest, are the first the
capped pick takes; the weaker in-zone satellites are too small to
register. The filed two-level mask measures inert for the complementary
reason — its in-zone values sit 30 dB down. The measurement (0.0 dB at
seven latitudes) stands; the explanation is replaced on the
construction page, and the check now reports each mask's own reach
instead of assuming a notch. The derived mask reads LIT INSIDE, never
SATURATED, and V46 pins that too.

**2. The examination read no cap and no floor beyond the declared
rows.** The resolver behind D5.1.4.1 clamps MIN_EXCLUDE to its end rows
(interpolation, Part B) but returns the header value — absent, so no
cap and no floor — for MIN_ELEV, MAX_CO_FREQ and MIN_DURATION at an
earth-station latitude outside the array's latitude span. Every derived
R set has rows at ±45 deg, so at victims 50 and 60 the examination
applied no elevation floor and no Nco cap. The sixth instance of the
band-versus-point family, this time on the examination's side, and the
Rec's rule is the one the exclusion resolver already follows: Sec.
D5.1.5 step 1 reads the nearest row, so the end rows govern beyond the
array. Measured by extending the record's rows to ±65 at the same
values, which emulates the nearest-row read:

| lat | derived mask, record rows | derived mask, rows to 65 | filed mask, record rows | filed mask, rows to 65 |
|---|---|---|---|---|
| 50 | -24.1 | -24.0 | -6.2 | -2.0 |
| 60 | -21.9 | -21.9 | -4.9 | -4.0 |

(0.1 d; at 1.0 d the filed mask with rows to 65 reads -1.8 / -4.0.)

The derived masks are unaffected within 0.1 dB — where the cap would
bite, the mask is dark — so every standing figure on a derived mask
survives. The filed mask is not: its high-latitude exceedance was
inflated by the resolver, and under the nearest-row read the filing
exceeds TABLE 22-1B by 0.5–2.0 dB at 0–50 deg and 4.0 dB at 60, firm
across ten times the depth. The record and the construction page carry
the corrected figures with the method named. The fix itself — clamp the
three nearest-read arrays to their end rows, as the exclusion resolver
does, with a check — changes the examination path and waits for the
operator's word; until then the rows-to-65 emulation is the
measurement.

**3. The consistency check's first verdict scheme was built for a rule
mask and misread a physical one.** "Lit inside the zone" and "saturated"
had one name, INCONSISTENT, and the derived mask tripped it at every
block. A physically derived mask always carries main-lobe reach past a
boresight gate; only a mask lit to the arc itself carries no shaping.
The scheme is now graded as above, with the reach in degrees, and the
meaning text gives both readings a LIT INSIDE verdict admits — a gate
declared wider than the mask's edge, or the main-lobe edge of gated
beams — because the mask alone cannot separate them.

Control for all three: same profile, design, limit row, victims and
depth as the record; the record's R set and its rows-to-65 variant
differ in the four added rows only; the dissection numbers are from the
record's own mask file.

## Beamlab — the header is global: the resolver was right, the declaration was short, 7 September 2026

A correction to the previous entry's second point, on the operator's
ruling: "the header is global — it provides the values not covered by
the table." That is EPS Sec. 6.7.2.2 as the resolver already implements
it and as the design brief records it — the array prevails inside the
latitudes it covers, the header applies outside them — and it settles
the defect on the other side from where I placed it. The examination
did not misread the derived R sets at latitudes 50 and 60; it read them
exactly: rows at ±45 and no header, which declares nothing beyond the
rows. The derivation wrote a declaration that was short.

**The fix, declaration-side.** The deriver now carries the two global
headers the enforced set already holds — the elevation floor and the
co-frequency cap — so every latitude the measured tables do not cover
is promised what the system enforces everywhere. MIN_DURATION is
deliberately not carried: declaring it switches the examination to the
track-duration algorithm. V38 pins that a derived set carries the
headers and never the duration; V47 pins the reading — array inside its
span, header outside, nothing when there is neither, and MIN_EXCLUDE on
its own interpolate-and-clamp rule.

**Verified before it was written.** The record's R set with the two
headers added and nothing else changed, examined against the filed mask
under the unchanged resolver, reproduces the rows-to-65 figures to the
tenth: -2.0 at 50, -4.0 at 60, the other five unchanged. The corrected
filed-mask figures stand as recorded — 0.5–2.0 dB at 0–50, 4.0 dB at
60, firm across ten times the depth — and the record now names the
header as the route. The derived masks are unaffected within 0.1 dB, as
measured.

**What this leaves.** Every R set derived so far on disk lacks the
headers; the next derivation writes them. The 1.0 d run in flight was
started from the earlier build and will write a set without them; its
E1 at 50 and 60 is on a derived mask, where the effect measured at most
0.1 dB, so the figures it produces stand and the set can be re-labelled
with headers before it is filed anywhere. The read-rule law gains its
missing clause on the construction page: beyond an array's span the
header governs, and a declaration without one declares nothing there.

**Housekeeping recorded.** The seven pre-fix derived masks under
dataset/margin (31 August to 3 September, point-sampled) are deleted;
the seven of 7 September stand. The operator's decision on the BL
family — frozen versioned triples, provenance stamps, both curves, one
re-emission after the probe cases — is the generator work item, and the
29 August case READMEs now say they are structure examples until then.

---

## Operator decision — header and array forms are mutually exclusive, 7 September 2026

The duality question is ruled. S.1503-4 SB3.3 defines min_elev (header
name elev_angle), max_co_freq and min_duration in both a header-attribute
form and a per-latitude-array form, and never says what a set carrying
both means; its own worked example populates one form per quantity, and
real filings write constants as one-row arrays. The ruling: **the two
forms are mutually exclusive per quantity.** A set carrying both is an
invalid filing, reported rather than resolved. Outside an array's span the
RR absence defaults apply; a non-default value at every latitude is filed
as rows to +/-90. The header remains for the scalar-only quantities.
Recorded in the dataset brief section 3.8 (radians 6ef4c45); the EPS's
6.7.2.2 precedence rule is superseded and will be updated; the WP4A
next-revision watch list gains the exclusivity clarification.

Consequences on this side, folded into the standing re-emission item:
the exporter emits exactly one form per quantity; compose/export refuses
a profile that would produce both (the same refusal family as
min_duration + min_angle_at_es); DeclaredConstraints' in-span/out-of-span
precedence survives only as the ARRAY-plus-defaults read (array row where
covered, RR default outside), never as header-vs-array arbitration; the
set-22 dataset case (both forms, different values) re-purposes into the
invalid-filing probe whose expectation record is the rejection; and the
header-vs-arrays parameter card is rewritten to the ruling.

**Operator correction to the exclusivity ruling, same day:** the
out-of-span fallback stated above (RR absence defaults beyond an array's
rows) was wrong — Appendix 4's "assumed if not provided" applies to a
QUANTITY filed in neither form, and no latitude-coverage concept exists in
it. None is needed: the nearest-row read is total — beyond the outermost
rows the nearest row IS the outermost row, so edge rows govern outward to
the poles, and a single row declares a globally constant value (as real
filings already write constants). MIN_EXCLUDE, the interpolated array,
gains the one explicit edge statement: beyond its end rows the end-row
value applies. Brief updated (radians 3bb6f74). Consequences here: the
"rows to +/-90" advice is withdrawn; DeclaredConstraints drops its
span test entirely for arrays (nearest/interpolated read everywhere,
clamped at the ends); and the band the OUTERMOST row governs runs from
its midpoint-with-neighbour to the pole — the derivation must envelope
edge rows over that full band, which the band-rule fix should be checked
against.

## Beamlab — one form per quantity, the read total: the brief's ruling applied, the afternoon's two readings retired, 7 September 2026

The design brief's section 3.8 and the EPS at V43 now rule the
header-array question, and the operator asked for my view before the
code moved. The view, briefly: it simplifies, and it closes the hole
rather than papering over it. No precedence logic — one place to look
per quantity, and a set carrying both forms becomes a validation
verdict instead of an invented semantics, the same stance as the
adaptation rule. No "nothing" state for an array that exists — the
outermost row governs outward, which is exactly the hole that inflated
the filed mask at 50 and 60. One source of truth for the gates — the
R set, with `grp.elev_min` deprecated and the `epfd_param` scalars left
empty when a set overrides them. The cost is coordination: the ruling
departs from the EPS text as it stood and from what radians' reader
does today, so the resolver change here, the EPS revision (done, V43),
and the reader change on your side land as one move or the cross-read
diverges on precisely the cases the new invalid-filing probe exercises.

**What moved, and what it retires.** Two readings from this afternoon
are withdrawn. My entry before last placed the defect in the resolver
and proposed the total read; the entry after it placed it in the
declaration under "the header is global" and made the deriver write
headers. The ruling settles it in favour of the first placement and
against the second's remedy: the resolver now reads the nearest row
everywhere — the outermost row beyond the table, the header only when
no array is filed — and reports a set that files a quantity in both
forms as invalid, unexamined. The deriver writes array-only sets again,
as it always had; the headers it wrote for an hour are gone. V38 pins
array-only; V47 pins the total read, the header-only read, the
single-row constant, the both-forms report, and MIN_EXCLUDE's own
interpolate-and-clamp.

**The figures do not move.** The rows-to-65 measurement was the total
read by another name: the filed mask exceeds TABLE 22-1B by 0.5–2.0 dB
at 0–50 and 4.0 dB at 60, firm across ten times the depth, and the
record's unchanged R set now gives those figures directly. The derived
masks are unaffected within 0.1 dB, as measured. The record and the
construction page carry the ruling; the spec note that recorded the
withdrawn precedence is corrected.

**The package, corrected once more.** The group scalars I wrote into
the notice two hours ago — `grp.elev_min`, its `freq` and `srv_cls`
rows, `non_geo.x_zone` — are out: the SRS layer carries no copy of the
gates, the R set is the one source, and `sat_oper` stays as the
notice's own table. The parameter XML is array-only; the mask is raw
XML. `dataset/STEAM-2B-FILED/` is regenerated in that form.

**One loose end of my own.** The debate now holds, in order, a wrong
placement corrected by a wrong remedy corrected by the ruling. That is
how the record is meant to work, and it cost the afternoon two rounds;
reading the resolver's own comment before proposing would have saved
one of them.

## Beamlab — the ruling's consequence list, item by item, 7 September 2026

The operator's decision on exclusivity listed five consequences for this
side and one check. Status, in the list's order.

1. **The exporter emits exactly one form per quantity.** Done by
   construction: the deriver never wrote headers for the three quantities
   except for the hour recorded above, now undone; the derived sets on
   disk are array-only for min_elev and max_co_freq and file no
   min_duration.
2. **Compose and export refuse a set that would carry both forms.** Done,
   in the same refusal family as min_duration beside min_angle_at_es: the
   XML writer throws on a both-forms set, naming the quantities. One
   opt-in exists so the dataset can emit the invalid-filing probe
   deliberately; nothing else uses it. The examination-only mode already
   refused such a set on input.
3. **DeclaredConstraints drops the span test.** Done: the array is read at
   the nearest row everywhere, the header only when no array is filed;
   MIN_EXCLUDE interpolates and clamps. V47 pins it.
4. **Set 22 re-purposes into the invalid-filing probe.** Half done: the
   generator writes it through the opt-in, and its expectation record
   still says what the withdrawn precedence said. The record becomes the
   rejection message when the family is re-emitted, with the other
   probes, as the standing item says.
5. **The header-versus-arrays card is rewritten to the ruling.** Done, in
   both locked places, catalogue and card deck, with the figure redrawn:
   the array governs the whole latitude axis, a header beside it makes
   the set invalid.

**The check the correction asked for.** With the read total, the
outermost derived row governs from its midpoint with its neighbour out to
the pole, so the derivation must envelope it over that whole band. The
adequacy test already exercises this: the STEAM-2 sweeps place victims at
50 and 60 deg, beyond the outermost derived rows at ±45, and E1 ≥ T held
there on the record and on every cap variant. Beyond the served span the
probe measures nothing, so the edge row carries the enforced value it
measured in its own band, and the examination's own verdict at the
outer victims is the test that it suffices. The 1.0 d run in flight
repeats that test at depth.

**One more measurement, closing the afternoon's loop.** The record's R
set, unchanged, examined against the filed mask under the total read:
-2.0 at 50 and -4.0 at 60, the tenth of a dB the two emulations gave. A
both-forms variant of the same set is refused with the diagnostic and
exit code 2. Harness 139 passed at the ruling's commit.

## Beamlab — the convergence purchase: the derivation does not move, the truth does, 7 September 2026

The 1.0 d run landed at 20:42, 246 minutes on the name-only copy of the
STEAM-2 profile — probe, truth sweep and examination all at 1440 steps
of 60 s, floor 0.069%. Three results, in the order the rule wants them.

**1. The derivation is depth-stable, exactly.** The R set derived at
1.0 d is identical to the record's, row for row. The mask derived at
1.0 d agrees with the record's at every one of its 123 431 lit cells to
the hundredth of a dB (the files differ only in their name tag). The
saturated probe measures a configuration space, not a sample of one,
and ten times the duration found no configuration the record's probe
had missed. So E1 of the 1.0 d artefacts at 1.0 d equals E1 of the
record's artefacts at 1.0 d — the same seven numbers to the tenth —
and every movement of E1 with depth this week was the examination's
sampling of a fixed declaration, never the declaration moving.

**2. T is not converged at 0.1 d.** The pair, worst margin at each
victim:

| lat | T at 0.1 d | T at 1.0 d | moved |
|---|---|---|---|
| 0 | -9.1 | -9.3 | -0.2 |
| 10 | -4.8 | -10.2 | -5.4 |
| 20 | -7.4 | -9.5 | -2.1 |
| 30 | -5.7 | -10.9 | -5.2 |
| 40 | -6.9 | -10.3 | -3.4 |
| 50 | -11.9 | -9.3 | +2.6 |
| 60 | -11.7 | -10.4 | +1.3 |

At depth the truth flattens: -9.3 to -10.9 across all seven latitudes,
where the 0.1 d record showed -4.8 to -11.9. Most of the record's
latitude structure was sampling. The power headroom on the assumed
payload reads -10.9 dB at 1.0 d against -11.9 at 0.1 d; the assumed
payload is the hot one by ten dB at every victim, not only at some.

**3. The gap, both depths, and what is quotable.**

| lat | E1 - T at 0.1 d | E1 - T at 1.0 d | moved |
|---|---|---|---|
| 0 | 6.5 | 8.8 | +2.3 |
| 10 | 8.9 | 7.5 | -1.4 |
| 20 | 10.7 | 8.5 | -2.2 |
| 30 | 6.8 | 7.7 | +0.9 |
| 40 | 10.0 | 8.5 | -1.5 |
| 50 | 12.2 | 10.7 | -1.5 |
| 60 | 10.2 | 11.0 | +0.8 |

Under the convergence-pair rule the 0.1 d absolutes are retired: no
figure moved less than the tolerance. The 1.0 d figures — T at -9.3 to
-10.9, E1 at -17.7 to -21.4, the in-band gap 7.5 to 10.7 dB (11.0 at
60) — are the better numbers and still provisional, because their pair
does not exist yet. The cheapest legitimate pair is a 0.5 d run, about
two hours: if 0.5 d and 1.0 d agree within 0.5 dB the 0.5 d figures are
quotable with 1.0 d as their pair, and the 1.0 d ones stand with them.
A 2.0 d run, eight hours, would pair 1.0 d directly. Since the
derivation is exactly stable, only the truth sweep and the examination
need the depth; the probe does not, and a truth-only mode would halve
the cost of any further pair.

**What stands unchanged.** Every comparison made at matched depth this
week — the three regimes, the exclusion's inert share, the band fixes,
the filed-mask contrast — stands as a difference. The filed mask's own
pair (0.1 d against 1.0 d, within 0.2 dB) was firm before this run and
is firm after it. What this run adds is the one thing the differences
could not give: the declaration itself does not move with depth, so the
gap between declaration and truth is a property of the envelope, and
the remaining uncertainty is the truth's tail alone.

Controls: same profile in all but name, same design, limit row,
victims, grid and step; depth the only variable; the record's artefacts
and the 1.0 d artefacts compared file to file.

## Beamlab — the pair bought: five latitudes firm, two still moving, and what the second half of a day contains, 7 September 2026

The truth-only rerun exists and was used at once: the record's
declaration read back, the probe skipped, the truth sweep and the
examination run at 0.5 d — 720 steps of 60 s, floor 0.139% — in 85
minutes against the 246 the full loop took at 1.0 d. The pair is
(0.5 d, 1.0 d), tolerance 0.5 dB, all three statistics:

| lat | T 0.5 d | T 1.0 d | moved | E1 0.5 d | E1 1.0 d | moved | gap 0.5 d | gap 1.0 d | moved | status |
|---|---|---|---|---|---|---|---|---|---|---|
| 0 | -9.3 | -9.3 | 0.0 | -18.1 | -18.1 | 0.0 | 8.8 | 8.8 | 0.0 | firm |
| 10 | -10.2 | -10.2 | 0.0 | -17.7 | -17.7 | 0.0 | 7.5 | 7.5 | 0.0 | firm |
| 20 | -9.5 | -9.5 | 0.0 | -18.1 | -18.0 | +0.1 | 8.6 | 8.5 | -0.1 | firm |
| 30 | -10.9 | -10.9 | 0.0 | -18.6 | -18.6 | 0.0 | 7.7 | 7.7 | 0.0 | firm |
| 40 | -7.5 | -10.3 | -2.8 | -18.1 | -18.8 | -0.7 | 10.6 | 8.5 | -2.1 | provisional |
| 50 | -11.9 | -9.3 | +2.6 | -23.5 | -20.0 | +3.5 | 11.6 | 10.7 | -0.9 | provisional |
| 60 | -10.6 | -10.4 | +0.2 | -21.9 | -21.4 | +0.5 | 11.3 | 11.0 | -0.3 | firm |

**What is now quotable as an absolute.** At 0, 10, 20, 30 and 60 deg, T,
E1 and the gap each moved 0.5 dB or less between the two depths, so the
1.0 d figures stand with their pair named: T from -9.3 to -10.9, E1
from -17.7 to -18.6 in band, the in-band gap 7.5 to 8.8 dB, and 11.0 at
latitude 60. The power headroom on the assumed payload, -10.9 dB at
latitude 30, is firm. At 40 and 50 the truth moved 2.8 and 2.6 dB — the
worst event at each arrived in the second half of the day — and E1 at
50 moved 3.5, so those two latitudes keep their 1.0 d figures as
provisional. The widest gap, 10.7 at 50, is therefore still a
provisional figure; the firm range is 7.5 to 8.8.

**One caveat that belongs with the pair.** The runs are deterministic
and share their first 720 steps: the 0.5 d run is the first half of the
1.0 d run. So the pair asks whether the second half of the day changes
a figure, which is the rule's question, but it is not an independent
draw. Where the two agree to the tenth at 0–30, that is the same events
resolving the same percentile; where they differ at 40 and 50, a worse
event arrived later. An independent pair would start the propagation
from another epoch, which the loop does not yet offer.

**What the pair says about the two earlier records.** The 0.1 d record's
T at 50 (-11.9) is the 0.5 d value; the 1.0 d run alone found the
-9.3. And the 0.1 d record's latitude structure, which the 1.0 d entry
called sampling, was: at the five firm latitudes the 0.1 d figures were
off by 0.2 to 5.4 dB from values that a further doubling did not move.

**Cost and the next step.** The truth-only rerun makes any further pair
a truth sweep alone: a 2.0 d run, about four and a half hours, would
pair the 1.0 d figures at 40 and 50 directly. It is the one thing left
that the tools cannot shorten further, and it is the operator's call
whether two latitudes are worth it now or wait for the composition-
driven cases the ruling put first.

Controls: the record's R set and mask read back unchanged (the record
states both files by name); same profile in all but name, same design,
limit row, victims, grid and step; depth the only variable.

---

## Critique side — the week's structural result, two ownerships, one loose end, 7 September 2026

**The structural result first, because it reorganises the rulebook: the
declaration does not move with depth — exactly.** R set identical row for
row, the mask identical to a hundredth across 123 431 cells, at ten times
the duration. The saturated probe measures a configuration space, not a
sample of one, so every depth movement this week was the examination
sampling a fixed object. The convergence-pair rule therefore splits into
its two natural halves: ARTEFACTS are checked by file identity against a
deeper derivation — free, exact, and now demonstrated — while STATISTICS
(T, and E1 as a read) need pairs. Accepted with the bookkeeping as you
ran it: 0.1 d absolutes retired; five latitudes firm on the (0.5, 1.0)
pair — T at -9.3 to -10.9, in-band gap 7.5 to 8.8 dB, 11.0 at 60 — and
40/50 provisional with the worst events arriving in the second half.

**The nested-pair caveat graduates into the rule.** A pair whose shorter
run is a prefix of the longer answers the EXTENSION question — does more
of the same trajectory change the figure — not the stability-under-
resampling question. Both are legitimate; they are different claims, and
a quoted pair says which kind it is. The independent-epoch pair is the
stronger instrument and goes on the backlog, not the critical path. On
the 2.0 d question: relayed to the operator with a recommendation to
defer — the case is the parked assumed-payload STEAM-2, the two
provisional latitudes are labelled and harm nothing, and the
composition-first ruling stands. If a STEAM-2 figure is ever quoted
outside this document, that is the moment the 4.5 hours are owed.

**Ownership one: my notch mechanism was asserted, your instrument
measured, and the instrument wins.** I wrote "the mask already encodes
the exclusion" as a one-sentence mechanism; the dissection shows the
derived mask LIT INSIDE — near-peak power at alpha 11.7-16.4 deg against
the declared 22, main-lobe edges of boresight-gated beams reaching
neighbouring ground. The inertness is the algorithm's: Step 20 counts
main-beam satellites regardless of the zone, and lifting the zone only
promotes satellites the capped pick already prefers. The filed mask is
inert for the complementary reason (30 dB down in-zone). The measurement
stood; my explanation did not, and the correction is exactly what the
consistency check exists to produce. Its four grades — CONSISTENT, MASK
TIGHTER, LIT INSIDE, SATURATED — are the right vocabulary for the
section 3.10 probe's expectation record too: the probe's expected grade
is SATURATED, and the grade scheme should be named in the brief when the
probe is emitted.

**Ownership two, shared: the sixth instance reached the examination
resolver, and the day held two rulings about it.** The morning ruling —
header global outside the table — and the afternoon ruling — mutual
exclusivity, nearest read total — collided; the afternoon superseded,
your consequence list undid the interim header-writing hour, and V47
pins the final read: array at the nearest row everywhere it is filed,
header only when no array exists, both forms invalid, MIN_EXCLUDE
interpolate-and-clamp. For the uniform sets on disk the two semantics
give identical numbers — which is why the corrected filed-mask figures
(-2.0 at 50, -4.0 at 60, firm across ten times the depth) stand
untouched by the supersession. One loose end remains and it is textual:
the construction page's read-rule-law clause written in the interim —
"beyond an array's span the header governs, and a declaration without
one declares nothing there" — contradicts V47's read and should be
rewritten to it. The cards were fixed; the page clause was not named in
the fix list.

**A depth qualifier owed to the regime table.** The third regime's
"T moves both ways" (+1.5 at the equator, -0.7 to -0.9 at 50 and 60)
is a matched-comb difference at 0.1 d and stands as one — but its two
negative entries live at exactly the latitudes the pair later showed
unconverged at that depth (2.6-2.8 dB of movement). The mechanism
(reassignment relocates interference) is sound; the direction claim at
50/60 should carry its comb until a truth-only rerun of cap 4 at depth
(85 minutes, when the regimes thread resumes) either firms or retires
the negative sign. The equatorial +1.5 sits at a latitude the pair
found firm to 0.0 and needs nothing.

**The rest is acceptance.** The min_exclude-0 check confirms the
inertness measurement with the +0.1 dB trace explained; the regime
criterion as a granted-links readout is the right instrument and waits
its turn; the consequence list is complete against the ruling; the
cross-read package sits on the shelf in the operator-directed form (raw
XML, no Masks database), and the radians run stays parked until the
operator calls it. The probes queue behind nothing now — per the
priority ruling they and the capacity field are the campaign.

## Beamlab — the reply taken: the rule in two halves, the comb carried, one clause already where it should be, 8 September 2026

Taken as written, with three notes.

**The rule in two halves.** Artefacts by identity against a deeper
derivation; statistics by pairs, each quoted pair saying whether it is
an extension pair (the shorter run a prefix of the longer) or a
resampling pair (another epoch). The records already use the first
wording — the 0.5 d record names itself the first half of the 1.0 d run
— and any pair quoted from here on names its kind. The independent-epoch
instrument goes on the backlog as you place it; the loop would need an
epoch argument and nothing else.

**The comb carried.** The regime table's bottom row now carries its
qualifier on the construction page: the +1.5 at the equator sits at a
latitude the pair found firm to 0.0 dB; the negative entries at 50 and
60 sit at the two latitudes the pair found unconverged at 0.1 d, 2.6 to
2.8 dB of movement, so the direction claim there carries its comb until
a truth-only rerun of cap 4 at depth firms or retires the sign. The
mechanism stands either way. The rerun is 85 minutes and waits for the
regimes thread, as you suggest.

**The clause was already rewritten.** The construction page's
read-rule-law clause was changed in the same commit that changed the
cards (5a94801, on azure/main since the evening push); the interim
wording — "beyond an array's span the header governs" — is gone, and the
page reads: header attribute and per-latitude array mutually exclusive
per quantity, the nearest-row read total, the outermost row governing
every latitude beyond the table, a set carrying both forms an invalid
filing reported and not read. The consequence-list entry named the cards
and not the page, which is what misled; the page was done.

**Two small things for the record.** Your Step 20 and my Step 22 are the
same step — the always-include of main-beam satellites — under the two
numberings in play; the implementation's comments carry the -4 count.
And the four grades of the consistency check will be named in the
section 3.10 probe's expectation record when it is emitted, with
SATURATED as the expected grade, as you propose.

**The deferral, accepted.** The 2.0 d run for latitudes 40 and 50 waits
until a STEAM-2 figure is quoted outside this document. The provisional
labels stand where they are, and the campaign is the probes and the
capacity field.

## Beamlab — the re-emission begins: the section 3.8 sets, and where the invalid probe lives, 8 September 2026

The operator started the BL re-emission with the section 3.8 sets. The
three shapes the brief asks for existed already — set 21 arrays-only,
set 23 header-only, set 22 filing max_co_freq and min_elev in both
forms with different values — so the work was to give the third its
new meaning and to keep the family valid around it.

**The design decision, taken with the operator.** Set 22 carried two
cases: BL-D2 and BL-ALL. Re-purposing it into the invalid-filing probe
would have made the everything case an invalid filing too. BL-D2 is
therefore the probe, on set 22 as it stands, and BL-ALL reads the D2
band through a new set 26 — set 22's arrays without its headers, the
same MIN_ELEV rows at 10, the same MAX_CO_FREQ rows at 2, the same
exclusion tables and ES population — so the family keeps a valid
examination of the per-shell and per-satellite masks 2 to 5, and the
epfd(is) expectation of BL-ALL is computed under a valid set. The
alternative, a separate small invalid case with BL-D2 kept valid, was
offered and not taken; the recorded decision named set 22's case.

**What BL-D2 now is.** A well-formed notice with four well-formed masks
and one operating-parameter set that must be rejected. Its expectation
record is `expected/rejection.md`: the two quantities named, the rule
cited, the consumer behaviour stated — a rejection with a diagnostic,
not an examination under any precedence and not a silent choice of one
form — and the reference diagnostic this toolchain prints, the same line
the examination-only mode refuses with. No CDF is expected; a consumer
that produces one has failed the case. The masks stay so the rejection
has to come from the R set and nothing else.

**Emitted and read back.** Both cases generated in quick mode into a
scratch directory, the family on disk untouched until the one
re-emission: BL-D2 carries param22 with `elev_angle` 5 and `max_co_freq`
4 beside its arrays and the rejection record; BL-ALL carries params 21,
23, 24 and 26, param26 with no header for either quantity, and its
down, is and up CDFs. V49 pins the four shapes, the case assignment, and
that the rejection record carries the diagnostic. The writer refuses
set 22 without the opt-in; the generator is the only caller that passes
it, for set 22 alone.

**What the re-emission still needs.** The section 3.9 read-rule probes
(nearest-read, interpolation, sweep-grid disclosure), the section 3.10
consistency probe with its expected grade SATURATED and the grade
vocabulary named in its record, the provenance stamps and the two curves
per downlink case with their convergence pairs, and then the one pass
over the whole family. The 29 August artefacts keep their
structure-example note until that pass.

## Beamlab — the section 3.9 probes built by measurement: two read rules that decide a verdict, one that cannot, 13 September 2026

The three read-rule probes of the brief's section 3.9 now exist as
cases BL-R1, BL-R2 and BL-R3 (ntc_id 900123477-479), each with its own
operating-parameter set (27, 28, 29) and its own pfd mask (11, 12, 13)
in the D1 band, 19.7-20.2 GHz. Their rows, values and power offsets
were not reasoned out; they were chosen from a table. A harness mode
(`probescan`) runs the examination — Sec. D5.1.4.1 over a probe mask
and a one-row set that pins one quantity to a candidate value — at
named victims against the band's Article 22 row read from the BR
database, and reports the worst margin per (quantity, value, victim).
On this family that examination costs about a second per victim at 48
hours of 30 s steps, so the design space was measured, not guessed. Two
structural facts came out before any probe could be built, and one of
them changes what the interpolation probe can be.

**First fact: the verdict was owned by a main-beam pass.** On the
family's own mask 1, at every latitude tried, the worst margin sat at
the short-term point (-154 dB(W/m2) for 0.017% of the time) and came
from a single satellite crossing the line from the earth station to the
wanted GSO satellite: maximum epfd -99, against a CDF body near -142 at
the 28.57% point, while the two limit points are only 28 dB apart. That
pass is counted by Step 22 whatever the operating parameters say (a
satellite in the earth station's main beam contributes regardless of
the gates), so no read rule can move it — and it was lit in the mask
because the family's beams are 450 km cells from 1200 km, some 20
degrees wide, and an 8-degree boresight gate leaves the alpha axis lit
straight through zero. A probe whose verdict is decided there tests
nothing. The probe masks therefore carry a rule notch: mask 1's
reachable-envelope construction with the alpha nodes strictly inside
the declared exclusion angle set to the Sec. C1 -1000 null, alpha nodes
every 2 degrees so the notch edge is sharp, and the payload lowered by
35 to 43 dB so that the BODY of the CDF sits at the limit. With the
notch the maximum drops to about -129 and the body point becomes
binding by 12 to 18 dB; the read rules then have something to decide.

**Second fact: the exclusion read is nearly inert in the down
examination.** With the notch at 6 degrees and the mask lit outside it,
pinning the all-orbits MIN_EXCLUDE to 6, 10, 14, 18, 22, 26 or 30
degrees moved the worst margin at 25, 30 and 35 N by at most 0.9 dB
(cap 1) and 0.3 dB (cap 3); the GSO satellite overhead instead of 10
degrees east made no difference. The mechanism is the Recommendation's
own design: a satellite the gate removes is counted anyway when it
sits in the main beam (Step 22, threshold min(Gmax - 30 dB,
Grx(alpha0)), which widens exactly as the declared zone widens), and
elsewhere the removed satellite is replaced under the MAX_CO_FREQ pick
by another of like receive gain. This is the same result the
min_exclude-0 check gave on STEAM-2's consistent mask (0.0 dB), now
measured on a mask that is lit inside the zone: the exclusion
declaration has no epfd(down) consequence beyond a fraction of a
decibel, consistent mask or not. A probe "with the verdict keyed to the
interpolated value", as the brief's table words it, cannot be built in
this direction on this family — nor, I think, on any family, since the
mechanism is not geometric.

**BL-R1, the nearest-read probe, on MIN_ELEV.** Rows at 20 N (10
degrees) and 40 N (55 degrees), victims at 25 N and 35 N — half a
10-degree sweep step either side of the midpoint, which is itself not a
victim because the nearest-row read is undefined there. Notch 8, cap 3,
exclusion 8, payload 35.5 dB below mask 1's. Measured at 48 hours: the
correct read gives FAIL at 25 N (-5.6 dB) and PASS at 35 N (+1.8 dB);
interpolation gives FAIL at both (-4.2, -1.7); a point read that finds
no row at the victim's latitude gives FAIL at both (-6.1, -6.7); the
other row gives +2.1 and -6.5. So both wrong readers are caught by the
verdict at 35 N, and the record carries all four rows per victim so a
consumer can see which one it reproduced. The 24-hour prefix moved no
margin by more than 0.1 dB. Two choices in this construction deserve a
word. Elevation rather than the cap: the two levers are of a size
(10 -> 40 degrees moves the body 4.3 dB, cap 1 -> 4 moves it 4.5), but
an interpolating consumer of an integer array has to round, and 1.75
truncates to 1 — the correct verdict by accident. And 55 degrees rather
than 40: the interpolated value at 35 N is 43.75, and the margin there
barely moves between 40 and 50 degrees (-37.3, -36.9 at mask 1's power)
before jumping at 55 (-33.7); the row value had to sit across the limit
from the interpolated one, so the row went to 55.

**BL-R2, the interpolation probe, on MIN_EXCLUDE — the resolved value
is the discriminator.** All-orbits rows 6 degrees at 20 N and 14 at
40 N, so the interpolated values at 25, 30 and 35 N are 8, 10 and 12 —
different from both rows, as the brief asks — with the notch at 6 (the
smaller row, so the mask is consistent where the 20 N row governs and
lit inside the zone toward 40 N, deliberately), cap 1, payload 40.6 dB
below mask 1's. Measured: every read at every victim PASSES, margins
+1.8 to +3.3, the spread across reads 0.9 dB. The record therefore says
what it can honestly say: the expected outcome is the resolved
exclusion angle at each victim — 8.0, 10.0, 12.0 — which a consumer is
asked to report from its resolution layer (the brief itself calls the
probes acceptance tests of that layer), beside the nearest-row values
it would have reported instead (6; a tie at 30 N; 14) and the point
read's none; the verdict is stated as PASS under every read, and the
finding above is written into the record with its mechanism. If the
operator wants a verdict-keyed interpolation probe, the direction that
would give one is epfd(up): there the exclusion angle decides which
satellite each earth station points at, and the station's off-axis
angle toward the GSO victim moves with it, about 9 dB of sidelobe gain
between 6 and 14 degrees. This producer has the truth side of that
direction (the scheduler honours the interpolated read) but not the
examination side (Sec. D5.2 over the e.i.r.p. mask), so that probe is a
decision, not a next step.

**BL-R3, the sweep-grid disclosure probe, on MAX_CO_FREQ.** Rows 1 at
62.5 N, 8 at 65 N, 1 at 67.5 N: under the nearest-row read the cap of 8
governs 63.75-66.25 N and 1 everywhere else, the outermost rows
governing outward. Notch 8, elevation 10, exclusion 8, payload 43.2 dB
below mask 1's. The examination was run at every whole degree from 70 S
to 70 N (282 runs with the pair), and the record states the worst
margin per sweep step with its latitude: 10 degrees, +1.7 at 70 N, the
sweep COMPLIANT; 5 degrees, -1.6 at 65 N, EXCEEDED; 2 and 1 degree,
-1.8 at 66 N, EXCEEDED. The disclosure the brief asks for is thereby a
verdict: the same filing passes or fails depending on the grid it was
swept with, and a consumer quoting a gridless worst margin has quoted
nothing. The full table is in the case's `sweep_margins.csv`, so any
grid that is a subset of the 1-degree grid can be looked up; the
southern hemisphere was measured to make sure no grid point there
undercuts the spike (its worst is +2.6 at 65 S).

**What every probe record carries.** The correct read and the wrong
reads side by side; the 24 h / 48 h extension pair (named as such —
the shorter run is the first half of the longer, not an independent
draw; nothing moved more than 0.1 dB, the body points converge fast);
the limit row and its points — TABLE 22-1C, the D1 band's, 70 cm dish,
the same row-choice rule the compliance loop applies; the SHA-256 of
the mask and of the set, the identity a frozen artefact is checked by;
and a provenance line with the producer build id and the emission
time. That is the first appearance of the stamps decided for the
re-emission; the family's other cases receive theirs at the one pass.
The examination CDFs at the victims are written beside the records.

**A defect found on the way and fixed.** The 3.8 step moved BL-ALL's D2
band to set 26 in the case list, but the notice builder still listed
set 22 — the SRS said 22 in mask_info and mask_lnk3 while the Masks
database carried param26. The harness had checked the case list and
the XML files, not the notice. Every notice now takes its set list from
the one list, and V50 pins notice = case list for every case. This was
in abc82a9, unpushed; it is corrected before anything leaves.

**Cost and state.** Quick-mode generation of the whole family, nine
cases, takes 3.3 minutes; the full-depth emission of the three probe
cases about 30 minutes (each `--case` run regenerates all mask
sources); the harness stands at 143 passed, 0 failed (T5 and V50 new).
The family on disk is untouched until the one re-emission. Still
needed there: the section 3.10 consistency probe with its expected
grade SATURATED and the grade vocabulary in its record, the stamps and
the two curves on the downlink cases, and the one pass — plus the
operator's word on what BL-R2 is allowed to be.

## Beamlab — the section 3.10 probe, and what the family's own masks turned out to carry, 13 September 2026

The declaration-consistency probe exists as case BL-C1 (ntc_id
900123480): operating-parameter set 30 in the D2 band declares an
all-orbits exclusion zone of 8 degrees, an elevation floor of 10
degrees, a cap of 2 and the classic algorithm's 2.5-degree separation,
one row per quantity; its three pfd masks (14, 15, 16, one per shell in
the azimuth/elevation form, linked per orbital-plane range like
BL-D2's) are the reachable envelope of the same payload composed with
no boresight gate and no elevation floor — full load, no victim
avoidance — at a payload 45 dB below mask 1's. The grade vocabulary of
the consistency check is written into the record, as agreed: six words,
one per axis per latitude block. The dissection and the grader moved
from the harness into the app assembly on the way, unchanged, so that
the generator grades with the same code the loop and the examine mode
use; the harness keeps a thin console front end.

**The expected grade, measured.** SATURATED on the exclusion axis for
every mask: near-peak power reaches alpha 0.0 against the declared 8 in
five to seven blocks per mask, the arc itself lit at low latitudes, not
exercised at high ones where no cell reaches the zone. On the
elevation axis the grade reads CONSISTENT — and it reads CONSISTENT for
the family's gated masks too, because the grade is keyed to near-peak
power and a range-shaped envelope thins by more than 3 dB before it
reaches low elevations. I added a second reading to the dissection for
this, the lit reach (the lowest elevation still within 20 dB of the
block peak), expecting it to see the missing floor. It does not, on
this family: the saturated masks light the ground down to 2.7, 2.1 and
10.4 degrees of elevation for shells A, B and C, and the gated control
masks light it down to exactly the same values. A beam gated at a
10-degree boresight floor spills to the horizon when the beam is 20
degrees wide. So the record says what is true: the exclusion side of
the inconsistency is detected and graded; the elevation side is real by
construction and invisible to mask inspection on this family, with the
control's difference at the victims as its only trace.

**The conservative verdict.** The examination of the pair at seven
victims, 0 to 60 N: FAIL everywhere, worst -24.1 dB at 40 N at the
short-term end of the row (TABLE 22-1B, 1 m dish): the 10% point
passes by 8 to 13 dB, the 1% point sits between -3 and +7 dB, and the
short-term points fail by 11 to 24 dB. The binding contribution is the mechanism the
brief names — a satellite crossing the earth station's main beam inside
the declared zone, counted by Step 22 and carrying a main-beam-grade
mask value because nothing in the mask is dark there. The control (set
30 against the family's gated masks 2, 3, 4 at the same payload) fails
at every victim too, with maxima 0.5 to 2.4 dB below the saturated
masks': the whole effect of both gates on this examination. The verdict
is firm in the 24 h prefix at every victim; the margins are firm to 0.5
dB at four victims and provisional at three (0 N moved 17.5 dB — the
closest main-beam pass of the run arrived in the second half), which
the record states row by row. Margins at every limit point, the control
and the pair are in the case's `sweep_margins.csv`.

**The finding for the family.** Graded against their own declared set
26, the family's D2 masks 2 to 5 read SATURATED on exclusion — near-peak
power reaches alpha 0.0 to 0.3 against a declared 7.4 to 8.0 at the
low-latitude blocks — and light the horizon under their 10-degree
floor. On this payload, then, the boresight gates shape neither axis of
the envelope: 450 km cells are some 20 degrees wide from 1200 km, and
the derivation, correctly, shows what the payload does rather than what
the set declares. BL-D2 and BL-ALL therefore pair a declared MIN_EXCLUDE
and MIN_ELEV with masks that do not carry them as edges — inconsistent
in the same sense as BL-C1, by geometry rather than by construction.
This is the extreme form of the LIT INSIDE finding on STEAM-2's derived
mask (there the spillover was 6 to 10 degrees at 183 km cells; here it
reaches the arc). It is a decision for the re-emission: a declaration
without the gates, or a payload whose cells are small enough for the
gates to shape the envelope. Writing the gates into the family's masks
as rule notches, as the section 3.9 probes do, is not open to the
family, because their truth curves would then exceed the mask inside
the zone and the acceptance direction would be violated by
construction.

**Two limitations of the grader, recorded.** The elevation axis cannot
see a missing floor on a range-shaped mask, by either reading; and the
exclusion axis at high latitudes reads NOT EXERCISED where the geometry
puts no cell inside the zone, which is correct but means a mask is
graded only where the arc is in view. Neither is changed here: V46 pins
the grader's behaviour on the filed STEAM-2B mask and on the derived
mask, and changing the criterion would move those — a test conflict I
would rather surface than absorb. V51 pins the family finding itself
(mask 2 against set 26: SATURATED on exclusion, CONSISTENT on
elevation).

**State.** Harness 145 passed, 0 failed with T6 and V51 new; the quick
family generation now carries ten cases; the full-depth emission of
BL-C1 takes about five minutes on top of the mask sources. The family on
disk is untouched. What the re-emission still needs: the provenance
stamps and both curves on the downlink cases, then the one pass — and
now three decisions ahead of it: BL-R2's nature, the family's gates,
and whether the grader's elevation axis should gain a lit-reach
criterion (which would re-grade STEAM-2's derived mask).

## Beamlab — stamps, both curves, and what the one case with an examination side shows, 13 September 2026

The last two pieces before the one re-emission are in. Every case
now writes `expected/provenance.md`: the producer build id, the time,
the profile and the depth, and the SHA-256 of every file of the triple
— the notice, the masks with their XML sources, the expectation records
— so an artefact is checked by identity, as the pair rule's first half
asks, and a file whose hash differs is not this emission's whatever its
name says. And every case with a truth curve writes `expected/curves.md`:
per direction, the truth curve at the tabulated percentiles beside its
24 h prefix (the extension pair, named as such), and, where this
producer has the examination side, the examination-read curve beside it
with the gap and the direction check — examination at or above truth at
every resolvable percentile, the brief's acceptance criterion.

**Where the examination side exists, and where it does not.** This
producer implements the classic downlink algorithm: Sec. D5.1.4.1 with
MIN_ANGLE_AT_ES. A set that files MIN_DURATION selects the
track-duration algorithm, which it does not implement; there is no
epfd(is) examination (that reads the satellite e.i.r.p. mask, not the
pfd mask) and no Sec. D5.2 epfd(up) examination. So the examination-read
curve exists for exactly one direction of the family: BL-I1's downlink
under set 25, masks 2, 3 and 4 per shell. BL-D1's and BL-ALL's downlink
under set 21 carry a note instead of a curve, deliberately: a reading
under the classic algorithm of a set that selects the other one would
not be the consumer's reading, and the record says why rather than
substituting it. This is worth a line in the decisions list: the family
has three downlink truth curves and one examination-read curve beside
them; the other two wait for a track-duration examination, which is
radians' side of the house or a new piece here.

**BL-I1 at depth.** The truth pair moved 0.4 dB or less at every
percentile down to 0.1% and 2.6 dB at 0.05%, the maximum 3.6 dB; the
examination pair 0.4 dB or less down to 1%, then 1.3, 0.2, 0.7 and 2.1
dB, the maximum 6.6 dB. The direction check holds at all eleven
resolvable percentiles, smallest gap +8.5 dB at 10%, the gap rising to
12 to 17 dB in the tail. That gap is the projection margin of this case
— larger than STEAM-2's 7.5 to 8.8 dB in band, for a reason the
declaration makes visible: set 25 is the family's minimal set and files
no MAX_CO_FREQ, so the examination sums every operating satellite it
sees, while the truth's scheduler serves cells one satellite each. A
consumer's own examination should land near this producer's curve; the
gap is what a declaration with no cap buys the operator, and it is the
kind of number the margin decomposition of the brief's section 2 is
about. The 48-hour epfd(is) truth at the GSO satellite moved 0.2 dB or
less down to 0.1% and 0.6 dB at the maximum.

**Cost.** The truth pair doubles the truth time of every case: BL-I1's
two truth runs took about eleven minutes, the two examinations and the
records five more. The whole family at full depth, ten cases with all
pairs and probes, should take about an hour and a half after the mask
sources. Quick mode of the family is four minutes; the harness stands
at 147 passed, 0 failed, with T7 (stamps verified by recomputing the
hashes, curves present, BL-I1's examination curve and direction check,
the track-duration cases carrying their note) and V52 (the SHA-256
vector, the producer id, the direction check on synthetic curves).

**What the re-emission needs, and why it waits.** The pipeline is
ready: one run into `dataset/` replaces the 29 August structure
examples with stamped triples. Two things stand in front of it. The
first is mechanical: the old case directories carry files the
regeneration would not remove — BL-D2's former CDF, BL-ALL's former
param22 — so the six directories must be cleared first, which is a
deletion I will not make without the word. The second is the decision
from the section 3.10 entry: BL-D2 and BL-ALL declare a MIN_EXCLUDE and a
MIN_ELEV their masks do not carry at this beam size. Re-emitting them as
they are freezes that pair as a delivered artefact, documented in
BL-C1's record; changing the declaration (no gates) or the payload
(smaller cells) changes what the family is. Either is a legitimate
choice; it is not mine. The third open item, BL-R2's nature, does not
block the run.

## Beamlab — the joint strike: case (a) of the 11.32A note measured, a benign pair built, the consumer guide rewritten, 13 September 2026

While the family re-emitted, the two other sessions on this machine —
the radians session, on a two-body interference trial for the RR
No. 11.32A concept note, and the regulations session, drafting that
note and its Annex A — took the producer's numbers into their texts,
and asked for three things in return. All three are done; the note's
case (a) now rests on a measurement instead of an assertion, and the
measurement changed one row of its table.

**What went into Annex A first.** Four refinements from this week's
records, each with a number: that a system gating its beams at their
boresights reads SATURATED whenever the beams are wider than the
declared angle (450 km cells from 1 200 km with an 8 degree gate: the
arc lit, identical to no gate), so the table's tolerance belongs to
that grade too; that the elevation floor is not verifiable from a
range-shaped mask by inspection, by the near-peak reading or the lit
one (20 to 27 degrees and 2.1 to 2.7 degrees respectively, with or
without a floor), so a consistent elevation grade is not a verified
floor; that detection is the only lever because the declared angle
moves the epfd(down) worst margin by at most 0.9 dB while the mask
values inside the zone charge the operator 11 to 24 dB — the
Annex's "the verification precedes the examination and does not alter
its result" became a measured statement; and the convergence-pair rule
as a stopping rule for "run until the statistic converges", per
percentile, with the body at 0.4 dB and the short-term end at 2 to
18 dB. The regulations session drew a consequence — that a criterion
resting on the body would be the more robust to compute — and I gave it
the counterweight: robust, and blind to the in-line events the zone
exists to prevent. It reversed the sentence and kept the compromise:
decide on the converged body statistic, quote the short-term end with
its depth named and as a bound.

**Case (a), measured.** The note's case (a) proposes that in bands
adjacent to those with Article 22 limits the neighbouring Table 22-1
values be applied to a non-GSO system with a geostationary victim, so
that an operator who protects the arc has a route to a favourable
finding. It rested on the claim that such a system clears the values
with room and one that does not protect the arc fails them; nobody had
measured it, and the same session had already found case (b)
unpassable, so the note had one leg. The measurement (`arcshield`
mode, record `docs/arc-shield-case-a.md`): the family constellation at
one payload — boresight pfd −119.8 dB(W/(m² MHz)), a working Ka-band
downlink — as three systems: no zone declared and the arc lit; a
declared 8 degree zone written into the mask as the −1000 notch; the
same at 22 degrees. Each examined at victims 0 to 60 N against every
plain FSS row of 22-1C for 19.7–20.2 GHz (70 cm, 90 cm, 2.5 m, 5 m; 40
kHz and 1 MHz), 48 hours with the 24-hour prefix as the pair. On the
70 cm row the arc-lit system fails by 20.3 dB at the 0.017% point, the
8 degree protector sits at −2.5 dB and the 22 degree protector at −0.7
dB, both at the 28.57% body point; the boresight pfd at which each just
clears the row is −140.1, −122.3 and −120.5 dB(W/(m² MHz)). So the
protector clears at a level at which such a system operates and the
non-protector would have to run 18 to 20 dB below it, 15 to 25 dB
below any working downlink — the disease case (b) died of, on the
non-protecting system alone. Every row shows the same structure: the
protector limited at the body by the cap and the side-lobe levels, the
non-protector at the short-term end by the single main-beam pass
inside the zone; the 40 kHz and 1 MHz rows identical to 0.1 dB, because
the 1 MHz limits sit 14 dB above the 40 kHz ones and the flat-spectrum
scaling is 13.98 — which the Annex now carries as a condition a Rule
must state rather than an observation, since No. 22.5C.7 makes both
bandwidths binding. The two grades the Rule would refer to came out as
the table predicts, on masks built to test it: the notched masks read
CONSISTENT at exactly their declared angle, the arc-lit mask CONSISTENT
against its own no-zone declaration and SATURATED against a claimed 8
degree zone (the arc lit in 9 of 15 blocks). That grading needed the
alpha form, which the check could not read; the alpha axis needs no
geometry, so the check gained `CheckAlphaForm` for the exclusion axis.
Two caveats travel with the result and the Annex carries both
prominently: the protector protects by declaration and mask together,
so an operator protecting by scheduler alone with beams wider than its
angle reads as the non-protector — a consequence the Rule now owns
rather than discovers; and the protector's clearing level is set by the
cap and the side lobes, not the zone, so "at a working level" is
established for this payload family and is to be confirmed on a filed
system. The regulations session anonymised the filed system I offered
for that (the STEAM-2B mask: a genuine 22 degree zone, its clearing
level on the 22-1B row within a few decibels of its filed power, its
mask graded CONSISTENT at 22.0 degrees in 109 of 179 blocks) and
declined to state an exceedance on a reconstruction at one victim in a
document going to the Board — rightly. The transfer itself is confirmed
on filed material only by that mask against the 22-1C row, which is
queued behind the runs below, with the cap-4 and cap-2 readings side
by side so that a lower level can be attributed to the cap or to the
system.

**The benign pair for case (b).** The radians session's trial needs a
co-frequency pair whose in-line events are rare by geometry, in the
dataset's format: it is built (`--trial two-body`) and on disk. TB-M, a
20-satellite MEO at 8 000 km (2 × 10, 45 degrees, Case 1), against
TB-L, the family's shell A (1 200 km, 55 degrees, 4 × 8, Case 2), in
19.7–20.2 GHz — the band was my error at first (18.8–19.3 GHz is
No. 5.523A with no Article 22 row to test against; the radians session
caught it); and TB-M2 / TB-L2, the same systems serving 30 S–30 N and
40–70 N. Each system has its notice, an az/el pfd mask linked in the
scenario and an alpha-form mask stored beside it so the consumer's two
readers can be compared on one system, an arrays-only set with no zone,
minimum elevation 10 and cap 2, both payloads at one boresight pfd
(−120.6 and −121.8 dB(W/(m² MHz)), the case (a) level, so the three
measurements are comparable), a consistency record (every mask
CONSISTENT on both axes against its set) and a provenance stamp. The
span certificate did what it is for: the LEO serving 40–70 N radiates
from 20 N northward only, 8 of its 13 latitude blocks dark; the MEO
from 8 000 km reaches 30 S–30 N from every block, so its variant-2
masks equal its variant-1 masks and the declaration alone carries the
span. The terminal and the reference link budget are the trial plan's
(radians, `architecture/two-body-trial-plan.md` section 4), cited from
the READMEs rather than repeated.

**The consumer guide.** Rewritten in the radians repository to the ten
cases, what each expects the consumer to report, the read-rule ruling
as the probes test it (including the requirement to print the resolved
gates per victim, without which BL-R2 cannot be passed), the records,
the family's no-zone declaration and the direction rule; committed
there by the radians session unchanged. Its S.1503-4 implementation
plan closes its Phase 1 precedence question by the 7 September ruling
and points at this repository's resolver as the reference; its Phase 2
gains the probe cases as acceptance tests.

**State.** Harness 148 passed, 0 failed (V53 pins the pair). The
re-emission of the family runs meanwhile; its entry follows.

## Beamlab — the one pass: the family re-emitted as stamped triples, declaring no zone, 13 September 2026

The operator's three words settled the three decisions: clear the six,
the gates as declared are made up and can be adjusted, BL-R2 stays.
The re-emission ran from 17:26 to 18:42 local, and `dataset/` now holds
the ten BL cases as frozen, version-stamped triples from one build
(ec6501e6, full profile), beside the four TB systems of the two-body
pair. The six 29 August directories are gone with their
structure-example notes; the top README carries the emission stamp
instead.

**The gates.** The family's masks did not carry the exclusion zone its
sets declared — at 450 km cells a boresight gate leaves no trace in the
envelope — and the operator's word was that the declared values were
made up and could be adjusted. With this payload the only exclusion
value the masks carry is none, so sets 21, 22, 25 and 26 now file one
all-orbits MIN_EXCLUDE row of 0, and masks 1 to 5 are derived with no
gate, so declaration and masks are two products of one construction
again. Minimum elevation stays at 10 (the near-peak grader reads it
consistent, with the limitation recorded under the section 3.10 probe);
the uplink sets keep their 10 degree zone, which governs earth-station
pointing and is meaningful there. A declared, varying zone is exercised
by the probe cases, where it belongs. V51 now asserts the no-zone reads
and a CONSISTENT grading of mask 2 against set 26, a test changed on the
operator's decision. BL-C1's control text was rewritten to what the
control now is: the family's masks with the elevation floor only; the
history is a sentence in its record.

**What the pass produced.** Every case stamped (`expected/provenance.md`,
SHA-256 of every artefact; BL-ALL lists 25 files); every truth case with
its curves record: BL-D1's downlink pair moves 0.6 dB or less down to 1%
of time and 2.0 dB at 0.2%, its maximum 4.0; BL-U1's uplink 0.7 dB or
less; BL-U2's 0.8 dB or less with the maximum 1.6; BL-ALL's three
directions likewise. BL-I1's examination-read curve against its truth:
the direction holds at all eleven resolvable percentiles, the smallest
gap now +10.0 dB at 5% (it was +8.5 at 10% with the zone declared: the
scheduler no longer avoids the arc, the truth rose a little, the
examination rose a little more). The four probes reproduce their
scratch numbers exactly — BL-R1 FAIL −5.6 / PASS +1.8, BL-R2 resolved 8
/ 10 / 12 with a 0.9 dB spread, BL-R3 +1.7 at 10 degrees and −1.6 /
−1.8 / −1.8 at 5 / 2 / 1, BL-C1 SATURATED on exclusion for all three
masks and FAIL at every victim, worst −24.1 dB at 40 N — as the
determinism the family is built on says they should.

**The two-body pair, beside it.** TB-M, TB-L, TB-M2 and TB-L2 sit in the
same directory in the same format, for the radians session's trial
(previous entry). They are not part of the BL family: no truth curves,
no expectation of a verdict; their records are their construction, the
consistency grades and the stamps.

**What stays open.** The examination-read curve exists for one downlink
direction of the family until a track-duration examination exists,
here or in radians. The grader's elevation axis is blind to a missing
floor on range-shaped masks by either reading. BL-R2 stays a
resolved-value probe by the operator's word. And two things belong to
the joint work rather than the family: a second filed system with a
genuine zone and a low inclination, to test the mechanism behind the
case (a) result's latitude dependence (the candidate's mask is not in
this corpus), and the 60 N figure of the filed 53 degree system, kept
anonymised and without an exceedance claim in the Bureau's paper, as the
regulations session drafted it.

Ten commits ahead of azure/main since the last push, none pushed;
harness 148 passed, 0 failed.

## Beamlab — the step made parallel without a trace, and the cap-4 comb read at depth, 14 September 2026

**What was asked.** Threads for the simulation, and a question worth
answering first: parallel over time steps, or chunks of time, rather
than inside a step? For the truth run, no. The scheduler is a state
machine over time — a made link is held from one step to the next, and
the Random policy the STEAM-2 profile declares draws its keys from one
seeded generator in step order, then cell order, then satellite order.
Steps on different threads would hold different links and hand different
keys to different candidates: equivalent statistics, not the same run,
and the same run is what makes a faster simulation a refactor instead of
a new model. A time chunk with a sequential spine — assignment and draws
in order, the heavy geometry pipelined ahead — would keep identity at the
price of splitting the scheduler into stages and buffering every step's
candidate lists, roughly cells times visible satellites each. Inside a
step, everything expensive is per satellite and independent of the other
satellites; and the two runs in progress this afternoon said where the
seconds were: 6.9 per step for the truth over seven victims, 4.2 for one
victim through the live-composition read, figures that fit about 1.8 ms
per beam-lattice rebuild with the truth run rebuilding each satellite
twice — once inside the scheduler, once in the snapshot. The mask
examination is the one place time chunks are right: no scheduler, a
stateless read, every step a function of its own time.

**What was built.** One switch, `SimulationParallel` (default every
processor; `BEAMLAB_THREADS` names a smaller count; 1 is the sequential
code path, kept as it was). A pointing that can be resolved on several
threads declares it (`IConcurrentBeamPointing`) and is asked to prepare
the step once, on the calling thread, before the satellites fan out —
the scheduled pointing computes its schedule there, under a lock, exactly
when the sequential loop would have triggered it; the scene pointing
keeps a pool of scene copies, each a settings copy of one frozen
template, so a mutable generation scene is never shared. Inside the
scheduler's step the two independent phases run in parallel —
propagation, resolution and footprints per satellite; geometry, gates
and covering beam per cell — and the Random policy's keys are then drawn
on one thread, cell by cell and candidate by candidate, in the order the
sequential loop drew them; the greedy assignment stays sequential. The
down run computes each satellite's terms toward every victim in parallel
and sums them in satellite order, so the accumulated values are the same
bits. The mask examination runs its steps over time chunks when the read
declares itself pure (`IPureMaskPfdRead`: the mask-file read and the
dataset tool's wrappers; the live-composition read does not, and stays
step by step), each worker on its own constellation clone because the
vendored propagator keeps per-call scratch state, and the accumulator
then takes the values in step order. V56 compares one thread against
twenty-four as raw bits — 31 549 values: the down CDFs, maxima and quiet
counts over three victims with the epfd(is) byproduct, twelve schedule
steps' links, candidates and handover counts under the Random policy,
epfd(up), the mask examination over time chunks, the live-composition
examination — and finds no difference. Harness 152 passed, 0 failed.

**What it bought.** A `bench` mode times the STEAM-2 step (1600
satellites, 11 702 cells) at the current thread count: the truth step
went from 5.93 s to 0.57 s on 24 cores with another job running, 10.3
times; the examination step from 1.3 ms to 0.2 ms — it was never the
cost. What remains sequential is about 0.34 s per step, the greedy
assignment over the cells and the list of every candidate link the step
exposes for the play session, roughly a million records per step; that
is the next lever if one is wanted. The double rebuild per satellite per
step is a further factor of two on the dominant cost and was left alone:
a separate optimisation, not threading.

**The cap-4 comb, read at depth.** The truth-only rerun of cap 4 at
0.5 d (79.5 minutes, `docs/compliance-steam-2-cap4-05d.md`) against the
baseline at the same depth: T −1.3 dB at the equator, +1.1 at 10, +0.6
at 20, −0.2 at 30, 0.0 at 40, −0.5 at 50, −0.6 at 60. Both directions
survive — a binding cap still moves T both ways — but the 0.1 d figures
did not: the equatorial +1.5 read there is −1.3 here, and the largest
gain moved to 10 N. The cap-4 truth itself moved 0.4 to 5.4 dB by
latitude between the two depths, so it has no depth pair of its own and
these figures carry their comb as the earlier ones did; a 0.5 d against
1.0 d pair for cap 4 would have cost about 160 minutes at the old speed
and costs a quarter of that now. The examination side at 0.5 d: widest
gap 11.6 → 6.5 dB, E1 alone 3.6 to 6.3 dB by latitude — the envelope
gain of the 0.1 d row, unchanged. The construction page's footnote is
rewritten to the measured state; its previous text also mis-named the
baseline pair's unconverged latitudes as 50 and 60, where the pair had
moved at 40 and 50 — corrected in passing.

**The brief's decomposition, measured — twice.** The STEAM-2 margin
decomposition (T, E_sel, E1 on one victim grid, 0.1 d at 60 s, latitudes
0 to 60) finished its first run after 92 minutes and read, at 50 N, a
selection component of +2.4 dB at the 1% point: the examination's
selection over the live values sitting ABOVE the truth. That is
impossible when the two share a schedule — E_sel sums a subset of T's
terms — so the instrument, not the system, was at fault. It was: the
decomposition fed one live-composition read, with its cross-time cache,
to all seven victims in turn. A later pass that met a satellite the
first pass never saw (visible from 50 N, not from the equator) resolved
it through the scheduled pointing at an earlier time from a later state
— dwell memory carried to the end of the previous pass, the Random
policy's seeded sequence already advanced — and got a different
schedule. The equator's row, the first pass, was exact; every later
row mixed schedules. V55 never saw it because it builds one read per
latitude. The fix is the same: a fresh read, and so a fresh scheduler,
per victim; the read's comment now says ONE READ SERVES ONE PASS and
why. The re-run took 10.0 minutes against 92.0 on the same machine, and
its T and E1 columns — every percentile at every latitude, the runs the
threading touched — are the first run's to the last printed digit: the
identity V56 pins on a small fixture holds at 1600 satellites.

**What the corrected record says** (`docs/margin-decomposition-steam-2.md`).
At the deciding point the selection component is 0.0 dB at five
latitudes and −0.1 at 10 and 30 N; the envelope component is +6.5,
+9.0, +10.7, +6.9, +10.0, +12.1 and +10.2 dB at 0 to 60 N — the whole
gap between the examination and the truth, at this depth and on this
system, is the mask envelope's. In the body of the distribution the
selection rules remove a little (−0.7 to −0.2 dB at the 10% point) and
the envelope adds +5.6 to +13.7; at 50 N, where the contaminated run had
read +2.4, the corrected selection component is 0.0 at 1% and at the
maximum. The 0.1 d comb qualifies the absolute levels as before; the
differences at matched depth are the quotable quantity, and the
decomposition's zero — E_sel equals T bin for bin with nothing declared
— is V55's checked invariant.

**A defect of the harness file, found on the way.** The `decompose`
mode's default paths never worked: the dispatch's path literals had been
spliced into the harness through a script that processed escapes, so
`@"C:\Projects\radians.beamlab"` had become `C:Projects`, a bare
carriage return, `adians.beamlab` — the stray CRs that had made the file
read as binary to git since the beamcount and decompose modes were added.
Repaired byte for byte; the harness source is plain text again, and
`decompose` and `beamcount` run with no arguments.

**State.** Uncommitted, awaiting the operator's word: the threading
change with V56 and the `bench` mode, the decomposition fix and the
literal repair, the construction page's cap-4 footnote, the two records
(`docs/compliance-steam-2-cap4-05d.md`, `docs/margin-decomposition-steam-2.md`)
and this entry; one commit (the decomposition instrument) ahead of
azure/main. Harness 152 passed, 0 failed on the final build.

## Beamlab — two depth pairs bought in an hour: cap 4 firm both ways, and the decomposition at 0.5 d, 14 September 2026

**What the speed was for.** The morning's rule was that a figure carries
its comb until a pair firms or retires it; the afternoon's threading made
pairs cheap. Two were bought at once, in sequence on the same machine:
the cap-4 truth at 1.0 d (14.7 minutes; the 0.5 d run of the morning had
taken 79.5 sequentially) and the STEAM-2 margin decomposition at 0.5 d.

**Cap 4, pair-firm both ways.** The 1.0 d cap-4 truth against the 0.5 d
one (`docs/compliance-steam-2-cap4-1d.md`, `-05d.md`): no figure moved at
0, 10, 20 and 30; 2.8 and 2.9 dB at 40 and 50; 0.6 at 60 — the baseline
pair's pattern exactly, the same two latitudes unconverged at this
depth, so what moves there is the comb, not the cap. Read against the
baseline at matched depth, the four firm latitudes give T −1.3 at the
equator, +1.1 at 10, +0.6 at 20 and −0.2 at 30, identical at 0.5 and
1.0 d. The both-directions claim of the regime table is therefore firm,
with its figures changed from the 0.1 d comb: the equator worsens, 10 N
improves. At 40 and 50 the two systems move together and their
difference stays within 0.5 dB at both depths (0.0 and −0.5, then 0.0
and −0.2). On the examination side the top-4 envelope gains 3.6 to 6.4 dB
by latitude at 0.1, 0.5 and 1.0 d alike — the one figure of that row
that never depended on depth — and the widest gap reads 11.0 → 5.9 dB
at 1.0 d. The construction page's footnote is rewritten a second time
today, from "carries its comb" to "pair-firm".

**The decomposition at 0.5 d.** The brief's section 2 deliverable now
has its pair (`docs/margin-decomposition-steam-2-05d.md`, 50.7 minutes
on the parallel build against the 92 the 0.1 d run had taken
sequentially). At the deciding point of every latitude the selection
component is 0.0 dB — at 0.1 d it read 0.0 at five latitudes and −0.1
at two — and the envelope component is the whole gap: +8.8, +7.5, +8.6,
+7.7, +10.6, +11.6 and +11.3 dB at 0 to 60, the baseline record's
E1 − T at this depth to the decimal. In the body the selection rules
remove 0.2 to 0.5 dB at the 10% point (0.2 to 0.7 at 0.1 d) and the
envelope adds +5.5 to +13.7. What moved between the depths is the comb:
the envelope component at the deciding point followed the truth's own
movement (6.5 → 8.8 at the equator, 12.1 → 11.6 at 50) while the
selection component did not move at all. The reading for the brief is
one sentence: on STEAM-2 the format's price is the mask envelope's, and
the selection rules the examination substitutes for the operator's
scheduler cost nothing at the deciding point and a fraction of a decibel
in the body. The construction page carries it as a box, "The gap,
decomposed", with both records named.

**State.** Uncommitted with everything since the morning: the threading
change, the two cap-4 records and the two decomposition records, the
construction page's footnote and box, the debate entries; one commit
ahead of azure/main. Harness 152 passed, 0 failed on the build these
runs used.

## Beamlab — the two-day sweep: what a pair firms, and what it never can, 17 September 2026

**The clean timing.** The bench pair re-run on an otherwise idle
machine: the truth step at 24 threads reads 0.57 s, as it did on Sunday
beside another job; at one thread it reads 10.9 s, against 5.9 s on
Sunday. The box is a hybrid processor, eight performance cores and
sixteen efficiency cores, so a lone thread's speed depends on the core
it lands on, and the sequential baseline is not one number. The
speed-up is quoted from here on as about ten to nineteen times, with
both baselines named; the parallel figure is the stable one.

**The 2.0 d sweep, and the qualifier it was to retire.** The
construction page had said that latitudes 40 and 50 "stay provisional
until a 2.0 d truth sweep pairs them", the two latitudes the 0.5/1.0 d
pair had found unconverged. The sweep is in
(`docs/compliance-steam-2-2d.md`, 2880 steps, 33 minutes on the
parallel build, the record's declaration reused). Against 1.0 d the
truth moved 0.4, 1.0, 2.7, 1.2, 0.1, 0.6 and 0.1 dB at 0 to 60. So 40
and 50 are paired, 0.1 and 0.6 — and 10, 20 and 30 moved instead,
latitudes the first pair had called firm to 0.0. The maximum at 20 N
rose 2.7 dB in the second day. The lesson is the one the family's
curves records already carried for the short-term end: the body of the
distribution converges, the tail keeps finding rarer, stronger geometry
as the run lengthens, and the deciding point sits in the tail. A pair
therefore firms the body and only ever provisionally the tail; "firm"
from one pair is a statement about the run length it covers. The
examination moved with the truth where the truth moved (1.7 at 10, 1.6
at 20, 1.3 at 50), the same geometry seen through the mask, and E1
stayed at or above T at every latitude at every depth run so far —
0.1, 0.5, 1.0 and 2.0 d. The gap in band reads 6.4 to 8.6 dB at 2.0 d
(11.4 at 50). The rule that survives untouched is the one adopted on
7 September: differences measured at fixed depth stand; absolute
short-term levels carry their run length, at any run length. The page's
sentence is rewritten to that.

**State.** Unchanged since Sunday, plus today's record and the
name-only profile copy: everything uncommitted, one commit ahead of
azure/main, no code touched today, so the harness figure stands at
152 passed, 0 failed.

## Beamlab — the ladder's fourth rung, and what it says about the tail, 18 September 2026

**The 4.0 d sweep.** One more doubling of the baseline truth-only
sweep, the record's declaration reused, 5760 steps, 75 minutes on the
parallel build (`docs/compliance-steam-2-4d.md`). Against 2.0 d the
truth moved at three latitudes and stood at four: the equator worsened
by 1.1 dB, 50 and 60 improved by 0.7 each, 10 to 40 did not move.
Yesterday's doubling had moved 10, 20 and 30 and stood the rest; the
one before had moved 40 and 50. So the pattern over three doublings is
one or two latitudes per rung, never the same ones twice, and the sign
goes both ways.

**Two mechanisms, told apart by the maximum.** The records carry the
maximum sample beside the worst margin, and the two do not always move
together. At 20 N the maximum rose 2.7 dB in the second day and the
margin followed: the deciding point there is the maximum against the
0% row, and a maximum can only rise under extension. At 60 N the
maximum has not changed since 0.5 d while the worst margin improved by
1.0 dB over three doublings: the deciding point there is a resolved
percentile, and a percentile falls when the samples that made it turn
out to be a cluster diluted by a longer run. Both are the tail, and the
tail is where the verdict is decided.

**What holds.** E1 follows the same geometry through the mask, so the
gap moves less than either curve: at 0 to 40 N it has read 6.4 to
10.6 dB at every depth from 0.5 d, at 50 and 60 N 10.7 to 11.6, and
E1 ≥ T at every latitude at every one of the five depths. The
construction page now carries the ladder as a table — T and E1 worst
margins at 0.1, 0.5, 1.0, 2.0 and 4.0 d, all seven latitudes — under
the inner-loop box, and Annex A of the concept note gains two findings
in its measured list: the decomposition (item 7, the gap is the
envelope's) and the tail (item 8, a pair firms the body and only
provisionally the tail). Neither changes a rule; both sharpen the
7 September one: differences at fixed depth stand, absolute short-term
levels carry their run length, at any run length.

**State.** Uncommitted since 14 September, now with six records and
three name-only profile copies on top of the threading change; one
commit ahead of azure/main; no code touched since Sunday, harness
152 passed, 0 failed.

## Beamlab — the verdict is not one rule: the limit curve between its points, and four expectations that turn on it, 18 September 2026

**Where it came from.** The drift guard J0 went red this morning: the
radians session had committed to its accumulator a scan of the whole
Article 22 limit curve (`c31f6db`, "Detect epfd excess between
tabulated limit points"), and our vendored copy is the file before it.
Asked whether the scan is diagnostic or verdict, radians answered
verdict: a filing passes only if every tabulated point passes AND the
CDF nowhere crosses the log-linear curve through the points, read with
a horizontal tolerance of half a bin (0.05 dB, their operator's
parameter). Their reason is not a reading they chose: on a filed case
the reference tool fails two examinations at bins where every tabulated
point passes, and radians passed them until this commit. The Bureau's
own implementation, then, applies the curve. Every beamlab verdict, and
every expected verdict in the dataset, is point-wise.

**The measurement.** A harness mode, `curvescan`, lays radians' rule
bin for bin over the dataset's expected examination CDFs, using the
vendored accumulator's own curve construction and the same limit rows
from the Bureau's database, and re-examines the sweep-grid probe's
10-degree grid at the record's depth. Point-wise margin against the
curve scan, tolerance 0.05 dB (zero changes nothing below but one
ratio):

- BL-R1, 25 N: FAIL −5.6 dB; crosses at −177.6 dB, 61.2% against
  10.5% allowed. No flip.
- BL-R1, 35 N: PASS +1.8 dB; crosses at −180.5 dB, 23.0% against 20.5%
  allowed (limit 20.2%), ratio 1.13. **Flips to FAIL.**
- BL-R2, 25 / 30 / 35 N: PASS +3.3 / +3.0 / +2.5 dB; all cross at
  −186.3 to −186.7 dB, ratios 1.00 / 1.12 / 1.19. **All three flip.**
- BL-C1, 40 N: FAIL −24.1 dB; crosses at −164.2 dB, ratio 6.4. No flip.
- BL-R3, 10-degree grid, fifteen victims: +1.7 to +6.7 dB at the
  points, no crossing anywhere. Stable under either rule.

**What it means.** The crossings sit in the body of the CDF. The 22-1C
0.70 m row, read as the percentage of time the epfd may be exceeded,
runs 100% at −187.4 dB, 28.571% at −182, 2.857% at −172, 0.017% and 0%
at −154: BL-R2's crossings (−186.3 to −186.7 dB) lie in the first
segment, 5.4 dB wide, BL-R1's (−177.6 and −180.5) in the second, 10 dB
wide — the very region the probes were tuned to, so that the body and
not a main-beam pass decides. A margin at the two neighbouring points
does not bound the crossing: +1.8 dB at both ends and a 13% excess in
the middle. Radians checked the Radio Regulations table itself (RR
2024, Article 22): the database's 5, 8, 4 and 9 points for the four
22-1C masks are the table's, every value identical, so the reference
tool is not catching these crossings with a denser table; it scans the
curve, and the dense-table alternative is closed. Radians also
recomputed three of the scan's allowed values by hand from the row and
found them to three figures, so the disagreement is about the rule, not
anyone's arithmetic. For scale: the 2.5 m victim's first segment is
34.4 dB wide with nothing tested inside; a probe whose body decides is
exposed in proportion to the segment it sits in. Under the curve rule BL-R1's correct read gives FAIL at 25 and
FAIL at 35, the pair the wrong reads give under the point-wise rule, so
the nearest-row probe stops discriminating at the verdict level until
its power offset is re-tuned against the curve; BL-R2's "every read
PASS" is false under the curve rule, though its resolved-value
discriminator survives. Two of the ten cases would need re-tuning and
re-emission, and every record must name the rule it verdicts under.
Two implementations read D7.1.3 point-wise (ours and a published
Python simulator), one reads the curve (the reference), and the table
holds no more points than the database: the reference reads the curve.

**Who decides.** One named verdict rule in the design brief, both sides
implementing it, is the proposal; it changes a shared artefact and the
dataset's expected verdicts, so it is the two operators' call. The
radians session has put it to theirs; this record puts it to ours.
Nothing in the verdict path was touched; the scan writes nothing. The
accumulator sync (purely additive, 98 lines) waits for the same word.

## Beamlab — the curve rule adopted: what changed in the verdict path, in two probe cases and in the case (a) numbers, 18 September 2026

**The decision.** "Adopt the rule, commit and continue." The radians
operator had already written the rule into the design brief; ours
adopted it for the producer. This entry records what adoption meant.

**The verdict path.** A core `LimitCurveRule` names the rule and carries
it: the verdict is the accumulator's own point-wise comparison AND its
curve scan (the vendored file synced byte for byte to radians'
`c31f6db`, ninety-eight added lines and nothing removed); a crossing is
reported at the bin of largest computed-over-allowed ratio with its
allowed and untolerated values; and a **curve margin** is measured as
the largest whole-bin shift of the whole distribution towards higher
epfd that still leaves the curve clear — negative when it crosses as it
stands — found by bisection, since shifting up can only add crossings.
The point margin every record has quoted is kept and labelled; the
verdict's margin is the smaller of the two. The compliance sweep, the
probe examination, the margin figure, the arc-shield instrument and the
probe records all verdict under the rule and print point margin, curve
margin and crossing, with the rule and its tolerance named in every
record. V57 pins it on a synthetic 22-1B row: a distribution clearing
every tabulated point but bulging between the 1% and 0.286% points
fails, the array scan names the accumulator's own bin, and the curve
margin is exactly the shift at which the curve is reached.

**Two things the adoption exposed.** The app's default limit template —
100% at −300 dB, 0.0001% at 0 dB, the "verdict-permissive" pair — is
under the rule a steep curve that every real distribution crosses; it
was permissive only point-wise, and two long-standing checks (V23, V24)
failed on it. The default is now the flat 100% line, and those checks
pass unchanged: a fixture semantics change driven by the rule, disclosed
here. And the probe scan's mask cache keyed its files on the integer
part of the power offset, so a scan over half-decibel offsets reused
one mask and reported one answer five times; the first re-tuning pass
was worthless and the key now carries a decimal.

**The two probe cases, re-tuned by measurement.** Margins move dB for
dB with the payload offset, so one scan at the old offset and one at a
candidate fix each probe. BL-R1: −35.5 → −37.0 dB; the correct read now
reads FAIL at 25 N (point −4.1, curve −6.2) and PASS at 35 N (point
+3.3, curve +0.9, no crossing), the interpolation read FAIL at both
(−0.8 rule margin at 35 N, the narrow side of the window), the point
read FAIL at both, the other row PASS at 25 N as before. BL-R2: −40.6 →
−42.1 dB; every read PASSES at 25, 30 and 35 N with curve margins +0.6
to +1.5 and point margins +3.3 to +4.8, the spread between reads about
a decibel, so the record's finding — the verdict does not discriminate
this read, the resolved value does — stands under the rule. Both cases
are re-emitted (build 518e50fc, 18 September); the other eight carry
the 13 September emission. The curve scan over the whole dataset now
reports no flip.

**The case (a) numbers.** The arc-shield record regenerated under the
rule: the daylight between protector and non-protector barely moves
(70 cm row +18.4 / +19.0 dB against +17.8 / +19.6 point-wise; 90 cm
+22.9 / +23.5; 2.5 m +26.1 / +26.7; 5 m +31.7 / +32.3), but the absolute
clearing levels drop 2.4 to 3.6 dB: at the payload used the protectors
no longer clear the 70 cm row (rule margins −4.3 and −4.9 at the 28.57%
body point); they clear it at −124.1 / −124.7 dB(W/(m² MHz)) and the
90 cm row at −125.0 / −125.6, the 5 m row at −120.0 / −120.6, the
non-protector at −143.1 to −152.3. Annex A's case (a) sentence now reads
"about −120 to −126 depending on the victim dish" and "18 to 32 dB
lower", and radians was asked to carry the correction to the 11.32A
note. The point-wise record is kept beside the debate for comparison.

**State.** Harness 153 passed, 0 failed. Committed on the operator's word;
radians told which cases changed for the consumer guide.

## Beamlab — after the rule: every record named, the sweep step measured to a tenth of a degree, and the ladder's fifth rung, 18 September 2026

**Every record names its rule.** The fifteen historical records in
`docs/` — thirteen compliance runs and the two decompositions — now open
with the rule they verdicted under (point-wise, the rule in force when
they were produced) and why their verdicts cannot change under the curve
rule: none of them has a PASS row, and the curve test can only add
failures. BL-R3 and BL-C1 were re-emitted so that all four probe records
name the new rule and print the curve margin; their verdicts did not
move. Annex A gained its item 9, the verdict-rule finding; the compliance
window shows the curve margin beside the point margin; the README's
compliance-loop bullet names the rule. The CSN-SSO weak-form check,
re-examined under the rule, passes at every latitude with no crossing
(point margins +9.0 to +22.3 dB, curve +7.1 to +19.3), so of the 11.32A
note's measured statements only the case (a) clearing levels moved.

**The sweep step, measured to a tenth of a degree.** The cover note's
default-step example — one degree against a tenth — had been parked
since the 14th. Picked today, it became two rows of the sweep-grid probe:
BL-R3 is now examined at every tenth of a degree from 70 S to 70 N,
1401 victims, and its record quotes the worst margin at 10, 5, 2, 1, 0.5
and 0.1 degrees (point margin / curve margin under the rule):

| step (deg) | victims | worst | where | verdict |
|---|---|---|---|---|
| 10 | 15 | +2.7 / +0.6 | 70 S | COMPLIANT |
| 5 | 29 | −1.6 / −3.3 | 65 N | EXCEEDED |
| 2 | 71 | −1.8 / −3.5 | 66 N | EXCEEDED |
| 1 | 141 | −1.8 / −3.5 | 66 N | EXCEEDED |
| 0.5 | 281 | −1.8 / −3.5 | 66 N | EXCEEDED |
| 0.1 | 1401 | −1.8 / −3.5 | 65.9 N | EXCEEDED |

The step matters where the record always said it did — between 10 and 5
degrees the verdict flips, between 5 and 2 the margin moves 0.2 dB — and
from one degree down to a tenth the worst margin does not move to the
0.1 dB bin: the plateau at 65.9 to 66.2 N reads −1.80 / −3.50 at every
finer step. On this probe the one-degree default finds the worst victim;
the reason is the probe's own five-degree-wide spike, and a narrower
feature would need the finer grid, so the rule stands as written: the
margin is quoted with its step. The 1401 examinations took six minutes
on the parallel build. Quick generation keeps the 141-victim, one-degree
sweep, so the harness checks that pin it are untouched (153 passed,
0 failed on the fine-sweep build).

**The ladder's fifth rung, and the deciding points read at last.** The
8.0 d baseline sweep (`docs/compliance-steam-2-8d.md`, 11 520 steps,
128 minutes on the parallel build) moved one latitude: 20 N, by 1.1 dB,
its maximum rising again from −151.9 to −150.8 dB; no other latitude
moved by more than 0.2. That is the fifth doubling in a row to move one
or two latitudes and stand the rest, never the same ones twice. The
record is the first written under the rule, so it names what the
earlier entries had to infer: the deciding point is the maximum against
the 0% row at 0 to 40 N and the 1% point at 50 and 60 N — the two
mechanisms of the 17th, read off the record instead of argued from the
movement of the maximum. It also reports the curve: at every latitude
the distribution crosses at −164.1 dB, on the segment between the
0.286% and 0.029% points, by nine to forty-four times the allowed
percentage, because the STEAM-2 tail sits far above the row's short-term
end; the curve margin binds only at 40 N, and there by 0.2 dB (−10.6
against a point margin of −10.4). E1 stayed at or above T at every
latitude, as at the five depths before; the gap at 0 to 40 N reads 6.4
to 8.6 dB, at 50 and 60 N 11.2 and 11.7. The ladder on the construction
page has the sixth column.

**State.** Uncommitted: Annex A item 9, the compliance grid's curve
margin column, the README clause, the fine-sweep probe and its generator
text, the construction page, the 8.0 d record and this entry; 84a241a
unpushed. Harness 153 passed, 0 failed on the fine-sweep build. Radians
has the sweep-step measurement for the cover note and the new BL-R3
stamp; the consumer guide and the 11.32A note remain with their owners.

## Beamlab — the decomposition at a third depth, the ladder's sixth rung, and the lit-reach criterion measured, 21 September 2026

**The decomposition at 1.0 d.** The 0.5 d decomposition extended on its
own comb to 1.0 d (`docs/margin-decomposition-steam-2-1d.md`, 1 440
steps, 98 minutes on the parallel build), so the pair is a run and its
prefix. The selection component is 0.0 dB at the deciding point of every
latitude from 0 to 60 — the third depth to say so — and −0.2 to −0.5 at
the 10% point; the envelope is the whole gap, +7.5 to +11.0 dB by
latitude (+7.5 to +11.6 at 0.5 d, +6.5 to +12.1 at 0.1 d). Where the
deciding-point margins moved between the two depths, the ladder's two
mechanisms are both on show: at 40 N the maximum rose from −156.6 to
−153.9 dB and T's worst margin moved from −7.5 to −10.3; at 50 N the 1%
point fell from −160.6 to −163.2 dB and the worst margin from −11.9 to
−9.3. E1 followed at both (−18.1 to −18.8, −23.5 to −20.0), so the gap
moved 2.1 and 0.9 dB while the other five latitudes stood within 0.3.
Annex A item 7 and the construction page's decomposition box carry the
third depth.

**The ladder's sixth rung.** The 16.0 d baseline sweep
(`docs/compliance-steam-2-16d.md`, 23 040 steps, 211 minutes) moved two
latitudes: the equator by 1.4 dB (its maximum from −153.3 to −151.9) and
10 N by 1.8 (−152.9 to −151.1); 50 N's 1% point moved 0.3, 30 N 0.2,
60 N 0.1, and 20 and 40 N did not move. The movers by doubling now read
40 and 50; 10, 20 and 30; 0, 50 and 60; 20; 0 and 10 — the set changes
at every doubling, but 0 N and 20 N have each moved twice, so the ladder
box's "not the same ones twice" was too strong and now says what the
table shows. The deciding points are the 8 d record's — the maximum at
0 to 40 N, the 1% point at 50 and 60, for T and for E1 alike (E1's at
40 N moved from the 0.286% point to the maximum) — and so is the
crossing bin, −164.1 dB at every latitude, 9.5 to 45 times the allowed
percentage; the curve margin binds at 50 N only, by 0.5 dB (−9.5 against
−9.0), no longer at 40 N. E1 ≥ T at every latitude for the seventh
depth; the gap 6.4 to 8.4 dB at 0 to 40 N and 11.7 at 50 and 60; the
worst rule margin −13.3 dB at 20 N, as at 8 d. The ladder has its
seventh column, the inner-loop passage its sixth doubling, Annex A
item 8 the two depths it lacked.

**The lit-reach criterion, measured before it was built.** The parked
item read: a lit-reach criterion beside the near-peak one on the
grader's elevation axis, which would make a missing floor visible on
range-shaped masks and re-grade the STEAM-2 mask V46 pins. A scratch
probe against the built assemblies graded every mask the harness and
the records pin both ways (near-peak as now; lit power below the
declared floor reading LIT INSIDE, at the horizon SATURATED). The
premise does not hold: V46's derived mask is already LIT INSIDE on
elevation by the near-peak reading (37.3 against the filed 40), and the
criterion changes its reported reach to 11.0 without changing the word;
the filed STEAM-2B mask is unchanged under all five gate pairs (hard
edge: lit reach equals near-peak reach at 40.0). Where the criterion
does change grades it flags floor-gated masks exactly as floor-less
ones: BL-C1's shells A and B go CONSISTENT to LIT INSIDE on elevation
for the saturated masks and for the controls alike (2.7 and 2.1 against
10; shell C at 10.4 stands), the family's own mask 2 against set 26 goes
CONSISTENT to LIT INSIDE at full depth (the quick mask V51 runs on stays
at 53.5), and the two-body LEO masks TB-L and TB-L2 go CONSISTENT to LIT
INSIDE (T8 asserts CONSISTENT); the MEO masks at 8 000 km stand at
17.6. So on every range-shaped LEO mask in the corpus the lit reach sits
at 2 to 3 degrees with or without a floor, which is what the 12
September entry measured, and grading on it would apply a 20 dB
standard on one axis while the exclusion axis grades at 3 dB. Options
put to the operator, in order of my preference: the lit reach printed
beside the near-peak reach in the grader's rows and records with the
verdict still keyed to near-peak; the grade as prototyped, with T8
changed on the operator's decision, BL-C1 and the two-body records
re-emitted, and the family's MIN_ELEV declaration reopened; or nothing
in code. One stale line found on the way: the consistency probe's class
comment says the lit reach "does see the missing floor" while the record
it writes says it does not. Decision below.

**The lit reach reported, not graded.** On the operator's "continue"
(21 September) the first option went in: the grader's row carries the
lit reach on the elevation axis beside the near-peak reach, the summary
sentence says in how many blocks lit power reaches below the declared
floor and how low, or that it stays above the floor in every block, the
record table and the console grade mode print the column, and the caveat
states why it is not graded. Verdicts are untouched: every grade the
harness pins reads as before. On STEAM-2's derived mask against set 30
the sentence reads "stays above the declared elevation floor in every
block" (lit reach 11.0 to 19.8 against 10); on BL-C1's mask 14 it reads
"reaches below the declared elevation floor in 13 of 13 blocks, lowest
2.7 deg against a declared 10.0". Annex A item 4 carries the measurement
and the decision; the open-decision list's item 4 is settled on the
criterion and still open on whether the Bureau's implementation should
carry the check at all; the probe's class comment now says what its
record says.

**State.** Uncommitted: the two records, the construction page (ladder,
passage, decomposition box), Annex A items 4, 7 and 8, the grader (the
row's lit reach, the summary sentence, the record column, the console
grade mode, the caveat), the probe's comment, this entry; 84a241a and
ee1b5e3 unpushed. Harness on the lit-reach build: 153 passed, 0 failed
(21 September, 08:00). The scratch probe lives in the session
scratchpad and will be swept.
