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
