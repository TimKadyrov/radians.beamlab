# Compliance loop — design plan

The producer workflow that closes beamlab's circle for epfd(down): start
from the real system, iterate against the Article 22 limits, and end with
declarations that pass the examination by construction. Recorded before
implementation; decisions taken are marked, open points listed at the end.

**Purpose, stated precisely** (correcting an earlier drift, twice): the
tool is not for fitting anything — neither an arbitrary parameter set
nor the model itself — to the epfd limits. Its purpose is to find **what
granularity of description is needed for the most realistic simulation
of the system**, and then to express the **minimum set of epfd
declarations** — masks, R set, exclusion commitments — **at exactly that
granularity, demonstrating compliance for a fully specified real
system**. Modelling decisions (e.g. whether a payload power budget must
be modelled) are made by measuring whether their absence distorts the
realistic statistics, never by whether they tighten an envelope or a
verdict. The operational truth side (the operation profile: payload,
beams, coverage, scheduling, activity) is submitted to the loop in full
and is never itself fitted to the verdict.
The loop's degrees of freedom are on the declaration side only, and even
there every quantity stays anchored to the truth: masks are envelopes of
the flown system (mask >= live, checked), the R set is the envelope of
measured operation, and the exclusion angle found by the advisor is an
*operational commitment* written back into the profile — the scheduler
then actually enforces it, so the declared zone and the flown zone are
one number. "Minimum" means minimal conservatism and minimal scope, not
minimal honesty.

## The workflow

1. The system's core operating characteristics are known: minimum
   elevation (global or per latitude), tracking strategy, beam
   composition (spot beams, reuse factor), beam antenna pattern, gain,
   transmission characteristics, Nco (global or per latitude), demand and
   activity, operational fraction, illumination duty — the list is open
   and grows as the model does.
2. These characteristics get their own first-class input element — the
   **Operation profile** — exactly as orbits got the design document.
3. The exclusion zone angle need not be defined yet: it is the loop's
   output, not an input.
4. Simulate epfd(down) at victim earth stations across a latitude grid;
   collect the statistics per grid point.
5. Verify each point against the epfd(down) limit.
6. On exceedance, suggest the adjustment path: the exclusion angle
   (globally, or per latitude where only some bands fail — finer table
   granularity), re-declared minimum elevation rows, and so on.
7. Repeat 4–6 until compliant.
8. Completion: the found exclusion and the measured behaviour become the
   declarations — the operating-parameter set through the deriver, the
   pfd mask through the scene export, the orbits through the design
   document — and the SNS builder assembles the dataset.

## What already exists (research)

- **Verdict machinery is vendored**: `EpfdAccumulator.CompareWithLimits`
  is the examination's own limit comparison (S.1503-4 §D7.1); the
  accumulator bins in the examination's 0.1 dB grid, so verdicts are
  commensurable with the BR software's.
- **Limit tables**: the vendored `radlimits` loader reads Article 22
  limits from the BR limits database. Decision: stage B accepts
  hand-entered limit points (epfd dB, % time) first — transparent and
  dependency-free — with limits-database loading as a follow-up.
- **The truth side is complete**: scene (`PfdMaskViewModel` — gain
  `GmDbi`, beam cell radius, Taylor SLR/n̄, pattern floor, `TxEirpDbw`,
  power mode, aggregation and reuse cluster, `RefBwKHz`), scheduler
  (policy, Nco, min-elevation and exclusion gates, coverage radius),
  geography (per-cell demand, activity factor/period), shells
  (operational fraction), `ScheduledPointing` (illumination duty), and
  `EpfdDown.Run`. The simulation runner already composes them for one
  victim.
- **The deriver exists**: measured link envelopes already become an R
  set (check V21). Step 8's second half is wired.

## Stage A — the Operation profile

`OperationProfile` record + `*.opprofile.json` codec (schema 1), an
editor window, and a composer that turns a profile into the runtime
stack.

Parameters (grouped; payload knobs are nullable — null keeps the scene's
own default, so the schema grows without breaking older files):

- **Payload / beams**: frequency (GHz), peak gain (dBi), beam cell
  radius (km), Taylor SLR (dB) and n̄, pattern floor (dBi), Tx e.i.r.p.
  (dBW), power mode (constant-e.i.r.p. | constant-PFD), aggregation
  (power-sum | co-channel) with reuse cluster, reference bandwidth (kHz).
- **Coverage / service**: minimum elevation (global + per-latitude
  rows), service area (lat/lon bounds), cell pitch (km), optional
  coverage radius.
- **Operation / scheduling**: tracking policy (highest elevation |
  max-GSO-separation), Nco per cell (global + per-latitude), per-satellite
  link cap, min angle at satellite / at ES, demand links per cell,
  activity factor and period, operational fraction, illumination duty.
- **Exclusion**: alpha (global + per-latitude rows) — starts absent.

Composer output: the enforced `OperatingParamsSet` (the scheduler's
gates ARE the real behaviour), the scene, the geography (cells carrying
demand/activity), the policy, duty and coverage; shells get the
operational fraction applied. Consumers rewired: the simulation runner
and the R-set deriver accept a profile path (empty keeps their loose
fields, so existing checks and workflows stand).

## Stage B — latitude-sweep verification

A Compliance window: design document + operation profile + victim sweep
(ES latitude from/to/step at a chosen ES longitude, wanted GSO at ES
longitude + offset, S.1428 dish) + duration/step + limit points
(hand-entered "epfd_db percent" lines, Article 22 tables cited in the
tooltip). Per grid point: one `EpfdDown.Run` against the limit points,
`CompareWithLimits` verdict, and a dB margin read off the CDF at each
limit percentage (worst margin reported). Output: a per-latitude table
(max epfd, worst margin, pass/fail), an overall verdict, CSV export.
The summary also prints the **power headroom**: epfd is exactly
dB-for-dB in per-beam TxEirpDbw (the envelope study's linear frontier),
so the worst margin doubles as the per-beam power headroom at the swept
exclusion — live-composition footprint only (a declared mask is fixed;
a power move needs the mask regenerated). The advisor prints it at its
endpoints too.

## Stage C — adjustment advisor and completion

On failure: walk the exclusion angle upward (configurable step and cap),
re-sweeping until compliant; report the smallest compliant alpha and the
failing-latitude pattern. Where only some latitude bands fail, propose
per-latitude alpha rows (finer table granularity) instead of the global
value. "Apply to profile" writes the found exclusion back into the
profile file; from there the existing deriver produces the R set and the
scene exports the mask — step 8.

## Promotion — from tendency to promise

The filing carries no tracking strategy; a scheduling behaviour reaches
the declarations only by being promoted into an enforced gate. The
recipe (the alpha advisor is its automated instance):

1. **Observe** the tendency in the flown truth (measured dwells, the
   measured minimum serving elevation, the measured min |alpha|).
2. **Harden** it into the profile field the scheduler enforces
   (`MinHoldSec`, `MinElevDeg`, `AlphaExclDeg`, `NcoPerCell`).
3. **Verify** the truth cost of enforcing it — re-sweep, and fly the
   truth payload under the declared gates (the runner's R-override
   mode is exactly this before/after instrument): unserved demand,
   margin shift, lattice re-sizing under a raised elevation floor.
4. **Declare** it through the R set, now backed by enforcement.

Two worked parameters:

- **MIN_DURATION**: `HoldUntilForced` produces long dwells by
  preference only. Promotion = set `MinHoldSec`; the scheduler then
  enforces it as dwell AND as admission (a link is only made toward a
  satellite that stays feasible for the whole promised duration — the
  filter bites, V25). Declaring it flips the examination to the
  track-duration algorithm and forbids `min_angle_at_es` (regime
  pair). Gap: the deriver does not measure link lifetimes yet, so the
  declared value is hand-carried into the designer after enforcement
  rather than envelope-fed (small open item).
- **MIN_ELEV**: under `HighestElevation` the derived min_elev rows can
  sit well above the enforced floor (measured 22 over a 10 floor).
  That derived value is the envelope of ONE finite run, not a promise
  — a feasible 12-degree link tomorrow would violate the filing. The
  gap between derived rows and the enforced floor IS the promotable
  amount: raise `MinElevDeg` to what you are willing to declare, then
  re-verify (this is the taxonomy's non-monotone knob — the floor also
  re-sizes the beam lattice and can unserve edge geometry).

Status: automated for alpha only (Suggest exclusion -> Apply,
write-back invariant). MIN_ELEV and the Nco caps are the tier-1 walk
candidates of the taxonomy above; MIN_DURATION additionally wants
dwell measurement in the deriver.

## Triage rule — which projection failed decides the tool (operator, 2026-09-01)

E1 >= T by the envelope direction, so three cases only. Basis: E1 =
T + projection margin, so a crossed limit is crossed by the truth
itself or inside the added margin — fix the component that crossed it. **T fails:**
the system itself violates — no declaration work is legitimate; fix the
system first (power density is the exact dB-for-dB lever the headroom
line quantifies, then the operating rules the loop walks, then the
constellation). **T passes / E1 fails:** the projection-margin regime —
the optimisation moves to the declaration side, in order: honest
examination inputs (mask b/c + latitude grids, resolvable comb),
declarations tightened to the flown truth (deriver envelopes), promoted
rules shrinking the reachable envelope. Fidelity is the objective;
passing is the consequence — declarations are never fitted to the
limits. **Both pass:** file; E1−T is carried robustness. E1 < T at any
percentile is never a win — it is a mis-declaration the direction check
exists to catch. (Mirrored in compliance-loop.html "Which failure is
it?".)

## The loop, v2 — from parameter ranges to array-form declarations (design, operator-directed, 2026-09-01)

**Purpose in one sentence:** given a lever parameter, the profile's
current view of it (operator arrays included) as the baseline, and a
range — walk the range, synthesize the per-latitude values each
latitude's own outcome justifies, verify the composed array jointly over
the grid, and report the increment over the operator's declared
discipline.

**Two scenarios (operator's framing):**

1. **Range of parameters** — min/max/step for the lever. The degenerate
   range (min = max) is pure VALIDATION: sweep the grid at the given
   value(s), verdict table, no walk. A real range with output
   granularity "global" is exactly today's advisor; with granularity
   "per latitude" it synthesizes the array view.
2. **Already-declared array** — the operator's per-latitude rows as
   input (the profile fields AlphaByLat / MinElevByLat / NcoByLat exist
   for exactly this). Two sub-modes: REVALIDATE (degenerate range over
   the array as-is, verified at rows plus the read-rule-sensitive
   points), or REFINE GRANULARITY (re-base a coarse array onto a finer
   latitude grid using the parameter's own read rule — nearest row or
   linear interpolation — then delta-walk the refined rows).

**Mechanics:**

- Baseline b(lat) = operator's row where present, else the global —
  the format's own header/array resolution, reused.
- Walk: a uniform delta over the baseline (all latitudes shifted
  together), per-latitude outcomes recorded at every step — the data
  the walk already produces.
- Synthesis: per latitude, the minimal delta that passes AND stays
  passing for the rest of the walk (monotonicity is not assumed).
- **Joint verification is mandatory**: rows are coupled through the
  scheduler (a gate change at one latitude changes the operation
  everywhere), so the composed array gets a full sweep — at the rows
  plus midpoints for linearly-interpolated parameters (alpha), rows
  plus a just-outside-span point for nearest-row parameters — and a
  fixed-point iteration bumps any regressed row and re-sweeps. The
  iteration's worst case converges to the uniform delta, i.e. exactly
  the old global answer: v2 can never do worse than v1.
- Write-back per parameter, gated by expressiveness:

  | lever | scheduler per-lat | payload/mask expressible | write-back |
  |---|---|---|---|
  | alpha | yes (linear interp) | no — the tracked scene gap | global: yes; array: ADVICE-ONLY until ground-lat gating |
  | min elevation | yes | lattice uses one global value | yes, with a lattice-gap warning (analogue of the alpha guard — to add) |
  | Nco (MAX_CO_FREQ) | yes | clean (pure scheduler) | yes, both forms — **IMPLEMENTED** (first v2 leg, V34) |
  | min duration | yes | clean, regime-exclusive | needs a per-lat profile field first; deriver measures no dwell |

- Reporting: the per-latitude increment over the operator's declared
  discipline (the granularity dividend made visible), plus the existing
  power-headroom line and trend.

**Does v2 replace the loop, and what is lost?** It subsumes it:
scenario 1 degenerate = today's validation sweep; scenario 1 with a
range at global granularity = today's advisor + Apply; scenario 2 is
new capability. Nothing functional is lost provided v2 keeps four
things: (i) the global-granularity output mode — the one-number answer,
and for alpha the only write-back-capable form until scene gating;
(ii) the mandatory joint verification pass, preserving the
jointly-verified guarantee the global walk gave for free; (iii) the
headroom and trend reporting; (iv) the per-parameter write-back gating
with the min-elev lattice warning added beside the alpha guard. The
costs are honest but bounded: more sweeps in array modes, and UI
surface (lever + range + granularity + scenario) — defaults keep it
simple (no inputs = validate current; one range = today's advisor).

**Status:** the Nco leg is implemented (2026-09-01): the sweep core now
takes any profile variant (`RunSweepProfile`), the walk-synthesize-verify
machinery is a pure core over a sweep delegate (`NcoAdviseCore`, pinned
headlessly by V34: monotone synthesis, the stay-passing rejection of a
non-monotone dip, honest non-convergence at the range floor, the
row-preserving merge, and the demand-clamped baseline read), the window
gained the "Cap advisor (loop v2)" group (Advise / Apply Nco rows), and
the walk reports "not the lever" when no margin moves — with demand 1
link/cell the cap barely binds, which the report says outright. Alpha
and min-elevation reuse the same core when their gating clears.

**Defect found under this framing (interim warning shipped):** the
current advisor and Apply run `profile with { AlphaExclDeg = a,
AlphaByLat = null }` — an operator-provided alpha array is WIPED and
replaced by a walked global, which discards declared structure and can
even loosen a latitude below the operator's own enforced rule. v2 fixes
this by construction (deltas over the baseline only tighten); until
then the window warns whenever it walks or applies over a profile that
carries per-latitude alpha rows.

## The loop, v3 — declaration optimization over operator ranges (design, operator-directed, 2026-09-04)

**The objective changed.** v1 and v2 asked "which declaration makes the
system pass". The operator has reset it: the truth is given and must
meet the Article 22 limits on its own (the triage rule above); what the
loop optimizes is the DECLARATION derivation, and the objective is to
minimize E1 while the declaration stays an honest envelope of every
operation the profile's commitments permit. Because the projection is
invariant under uniform power scaling, every dB removed from E1 - T at
a compliant operating point is a dB of licensed operating power. That
is the loop's value function from now on.

**The lever is an operator-supplied acceptable range, not a search for
compliance.** The operator states what it can live with -- for the
exclusion angle, "start at 20, do not go above 40" -- and the loop
builds the per-latitude table inside that range. The binding cost is
SERVICE, not compliance: raising alpha removes serving satellites, so
unserved demand rises (the scheduler already reports it per step).
Per latitude the question is therefore

> the largest alpha inside the range that service can absorb,

because E1 falls weakly monotonically as the declared hole grows while
T falls or stays. This is better posed than the v1 walk and it dissolves
the AlphaByLat-wipe defect: the walk is per latitude by construction.

**One table, two declarations.** The table is declared twice, and the
examination applies both halves:

- as `MIN_EXCLUDE[latitude]` in the operating-parameter set (the R
  mask): the selection rule, which satellites may be counted as
  interferers at all;
- as the exclusion hole baked into the pfd mask: the power envelope,
  what may be radiated toward directions inside the zone.

This is the design brief's own "improving the examination result twice
over" (brief Sec. 2), and it is why both artefacts must be generated
from ONE table in one pass -- a mismatch between them is a
mis-declaration, not a conservatism.

**Where the number originates, and how each artefact gets it.** The
alpha table is authored once, by the loop's per-latitude decision, and
written into the operation profile (`AlphaByLat`), where the scheduler
enforces it. The two declarations then acquire it by different routes,
and that asymmetry is the doctrine, not an accident:

- the **pfd mask** CONSUMES the table directly: the envelope sampler
  gates each direction on the alpha at the ground point that direction
  reaches, so the mask's hole varies with latitude by construction;
- the **operating-parameter set** gets it BACK BY MEASUREMENT: the
  deriver reads the flown operation and declares `MIN_EXCLUDE` rows
  where a rule actually bound. It is not a copy of the input; it is the
  envelope of what the enforced table produced.

**The three-way invariant.** At every latitude the alpha the mask was
built with, the alpha the R set declares, and the alpha the scheduler
enforces must be the same number. A mask built at a smaller alpha than
the R set declares is an inflated E1; a mask built at a larger alpha
than the scheduler enforces is a NON-CONSERVATIVE mask, i.e. a defect
of the kind the acceptance direction exists to catch. Worth a check of
its own once inheritance lands: derive from a profile with per-latitude
rows, export the mask from the same profile, and assert the three agree
row by row (allowing for the interpolation rule between rows).

**The prerequisite: mask inheritance of the per-latitude table.**
Today `OperationComposer.PerLatExclusionSceneGap` warns that per-latitude
rows gate the scheduler while the scene and mask bake only the global
alpha. Under the v1 objective that inflated E1; under v3 it inverts the
lever -- the table lowers T and leaves E1 untouched, moving the value
function the wrong way. So mask inheritance is step one, not a
follow-up.

**How the mask must inherit it.** `MIN_EXCLUDE` is indexed by EARTH
STATION latitude; a pfd mask block is indexed by SUB-SATELLITE latitude.
They are different latitudes, so a block cannot use its own row. For
each direction in a block the sampler must find the ground point that
direction reaches, take the table's alpha at THAT point's latitude, and
gate the beam on the geometry to that point. The mask-dissection mode
already performs exactly this mapping in reverse, so the geometry is
settled; only the sampler's gate is new.

**The service latitude band is a third commitment the mask ignores
(operator, 2026-09-04).** `ES_LAT_MIN/MAX` is declared (the composer
copies the profile's service latitude bounds into the declared set) and
enforced (the scheduler refuses cells outside it), but the envelope
sampler knows nothing about it: it is constructed from the scene, the
inclination and the yaw sweep alone, so every latitude block declares
the payload's full reachable envelope whether or not a served cell is
in reach there. Measured on the STEAM-2 case: at 40 deg minimum
elevation from 1 150 km the coverage half-angle is 9.5 deg, so with a
20-50 N service band only sub-satellite latitudes 10.5-59.5 N can serve
anyone at all -- yet our exported mask declares thousands of plateau
cells at every block from -50 to +10 (5 086 at -50, 1 788 at the
equator). Thirteen of twenty-one blocks are pure inflation, and our own
two declarations disagree: the R set says the system serves 20-50 N
while the mask says it radiates at full cap over the whole reachable
latitude range.

S.1503-4 sanctions the fix explicitly: Sec. C1 provides the -1000 dBW
null "it is feasible for a system not to transmit at certain
latitudes". So the correction is a declaration the format already
expects, not an invention.

**One mechanism serves all of them.** Gating the sampler per direction
by the GROUND POINT the direction reaches -- the mechanism the
per-latitude exclusion needs -- also delivers this one and the beam
cap, because all four commitments are the same kind of statement about
which configurations are permitted:

| commitment | field | gate on the ground point |
|---|---|---|
| service latitude band | `ES_LAT_MIN/MAX` | boresight must fall inside the band |
| minimum elevation | `MIN_ELEV` | boresight must clear it |
| exclusion angle, per latitude | `MIN_EXCLUDE` | alpha at that point must clear the table |
| beam count | `MAX_CO_FREQ_SAT` | at most K boresights at once |

So the sampler's envelope stops being "every direction the payload can
reach" and becomes "every configuration the declared commitments
permit", which is what a filed mask means and what the parity run found
STEAM-2B's mask to be. One caution on scope: only BORESIGHTS are gated.
Side lobes still radiate everywhere, so a block whose servable cells lie
at its edge keeps main-beam levels toward them and side-lobe levels
elsewhere; only blocks with no servable cell at all collapse to the
-1000 null.

**The read rule constrains the rows.** `MIN_EXCLUDE` alone is read by
LINEAR INTERPOLATION between latitude rows (Part B; the other arrays
take the nearest row). A declared table whose interpolant rises above
the enforced value between rows promises more avoidance than the system
delivers. The synthesised rows must therefore be the lower envelope of
the enforced per-latitude values, and the row spacing is part of the
answer -- the "read-rule-aware grids" item of v2, now binding.

**The cap lever is demand-driven, in two regimes.** The scheduler grants
`min(demand, cap, availability)` links per cell, so the truth's own
co-frequency count is already demand-limited and a declared cap above
the demand is headroom the operation can never fill -- while the
examination is entitled to sum that many worst-case interferers
(D5.1.4.1 Step 23 sums the top `cap` entries by epfd). That splits the
lever:

- **Free regime, cap down to the measured usage.** The tight cap is
  MEASURED, not searched: the deriver already reads the maximum
  concurrent co-frequency links per cell per latitude band off the flown
  operation. Declaring that value changes no link, costs no service, and
  removes the unusable headroom from E1. It is the promotion recipe
  applied to Nco -- observe, harden, verify, declare.
- **Paid regime, cap below the demand.** Going further means refusing
  service: unserved demand rises, and the operator's tolerance decides
  how far. This is where a walk is actually needed, and where the loop
  must report dB bought against service lost.

**Why this is legitimate although demand is Class T.** DemandLinksPerCell
carries no format field and can never be declared, so no mask or R-set
value may be conditioned on it directly. What the loop does instead is
promote its OBSERVED CONSEQUENCE into a Class D field: the cap. Once
declared, `MAX_CO_FREQ` binds the operator whatever the demand later
does -- which is exactly why it may tighten E1 and why the demand
itself may not. The loop should therefore report the cap together with
the demand model that produced it, so the operator sees what it is
committing to rather than what it happens to use today.

**Correction (operator, 2026-09-04): the mask takes the commitments by
MEASUREMENT, not by assertion.** The gate proposed above would have the
sampler assert each declared commitment directly — boresight inside
`ES_LAT`, alpha clearing the table, at most K beams. That is the wrong
side of the project's own doctrine. Declarations are envelopes of what
the system does; the R set already gets its numbers by measuring the
flown operation, and the mask should get the same numbers the same way.
A mask that ASSERTS a restriction can assert one the scheduler does not
actually enforce, and that error is in the dangerous direction: a
non-conservative mask, the defect the acceptance direction exists to
catch. A mask that MEASURES cannot: it observes exactly what the
enforced system did.

**So the mask is derived from a reachability probe.** Run the scheduler
under the candidate commitments, record the set of beam configurations
it actually grants — the (sub-satellite latitude, boresight direction)
pairs, and how many co-frequency beams a satellite carries at once —
and envelope the pfd over that set. Every commitment then reaches the
mask through the one mechanism that already enforces it, with no
per-commitment code: the service latitude band, the minimum elevation,
the per-latitude exclusion, the coverage radius and the beam cap all
shape what is visited, so they all shape the mask.

**The one thing the probe must not inherit is traffic.** Demand,
activity, duty and operational fraction are Class T: no format field
binds them, so a mask tightened by them would be fitted to behaviour the
operator never promised. The probe therefore runs SATURATED — every cell
demanding, activity 1, operational fraction 1, duty 1 — so that what is
measured is the reachable set under the COMMITMENTS, not the occurring
set under a traffic model. That is the distinction the design brief
draws between reachable and occurring bases, obtained by measurement
rather than by assertion.

**Three runs, and which one derives (2026-09-04).** "Derivation run" is
not the compliance loop's sweep. Three runs appear in one iteration and
they differ in centre, in traffic and in purpose:

| run | centre | victim | traffic | produces |
|---|---|---|---|---|
| derivation probe | the system | none | SATURATED | the pfd mask and the R set |
| truth sweep | a victim, one run per latitude | yes | the profile's real demand and activity | T, and the Article 22 verdict |
| examination sweep | a victim, one run per latitude | yes | not applicable (mask + declared gates) | E1 |

The derivation run already exists as a distinct object:
`OpParamsDeriver.Derive` builds its own scheduler with its own duration
and step (0.5 d at 60 s in the margin figure) and observes LINKS, with
no victim anywhere. Under the correction above it also records BEAM
CONFIGURATIONS, and the mask falls out of the same pass — which is what
makes the mask and the R set two products of one measurement rather than
two artefacts that have to be kept in step by hand.

The compliance sweep cannot play that role. It is victim-centric and
runs once per latitude, while a mask is not per-victim; and deriving
from the run that later verifies would make the adequacy test vacuous,
since a mask derived from a run envelopes that run trivially.

**The saturation rule propagates backwards to the R set.** Today
`Derive` takes the profile's geography and illumination duty, so it
steps a scheduler that applies demand, activity and duty — the derived
R set is therefore traffic-influenced already. Under the doctrine above
that is the same Class T leak, one artefact earlier: `MAX_CO_FREQ`
measured under a traffic sample is a commitment the operator never made
and may not be able to honour at peak. The probe must saturate for BOTH
artefacts.

Saturation buys two things beyond legitimacy. It makes the probe
necessarily a different run from the truth sweep, so the independence
condition of the adequacy test is satisfied by construction. And it
removes traffic from the adequacy question altogether: traffic can only
thin what is lit, so a saturated envelope dominates any traffic-thinned
composite, and what remains to verify is purely whether the probe
visited every permitted beam position — a geometry-and-time question,
which is the one E1 >= T tests.

**Adequacy is E1’s job (operator, 2026-09-04).** "Measurement" here
means simulation and observation: run the system under the candidate
commitments and record what it does. That trades a construction
guarantee for an empirical one. Today mask >= live holds BY
CONSTRUCTION -- the sampler envelopes the configurations analytically
and ScenePointing flies one of them -- whereas a mask derived from an
observed set holds only if the observation was granular enough. The
test for that is already the acceptance direction: an under-sampled
probe yields a mask that is too tight, and a truth run then exceeds it
at some percentile. E1 >= T stops being only an acceptance criterion
and becomes the ADEQUACY TEST of the derivation.

One condition makes it real: the verifying run must be INDEPENDENT of
the derivation run -- a different epoch or seed, at least as fine, and
verdict-grade. A mask derived from run A envelopes run A trivially, so
checking it against run A tests nothing. Derive from the probe, verify
against a separate truth.

The two granularities then have opposite signatures, which is what
makes them diagnosable:

| too coarse | effect on E1 | direction | how it surfaces |
|---|---|---|---|
| mask grid (b/c, latitude step) | inflated | safe, wasteful | the refine-and-watch attribution runs |
| probe (time step, duration, configurations visited) | deflated | UNSAFE | E1 < T at some percentile on an independent run |

**Consequence for the loop.** The mask and the R set become two products
of ONE measurement pass over the same enforced run, which is why the
loop must derive both (above) and why the three-way invariant — mask,
R set, enforced gate carrying the same number at every latitude — then
holds by construction rather than by inspection.

**The mask moves inside the loop (operator, 2026-09-04).** Today the
loop ends at write-back and the hand-off is manual: the window's own
page says that after Apply "the hand-off continues outside this window:
derive the R set and export the masks from the updated profile". Under
v1's objective that was harmless, because the verdict being optimised
was the truth's. Under v3 it is not: the objective is E1, E1 is computed
from the mask, so a candidate declaration cannot be scored at all until
its mask exists. The mask is not an artefact produced after the loop
finishes; it is part of every iteration.

It is also the only way the three-way invariant can hold. Applying a
per-latitude exclusion today and exporting afterwards produces exactly
the mismatch recorded above -- rows enforced by the scheduler, a global
value baked into the mask.

**One iteration, therefore:**

1. take the candidate value(s) from the operator's acceptable range;
2. enforce them in the profile (the scheduler then obeys them);
3. re-run the truth ONLY IF the lever moves it (see the cost note);
4. export the pfd mask from those same commitments (gated per beam by
   the ground point, per the mechanism above);
5. derive the R set from the flown operation;
6. run E1 against that mask and that R set;
7. score: objective = E1's distance to the limit; cost = unserved
   demand; constraint = the truth still passes.

**Cost asymmetry, and why the cap goes first.** An exclusion or
elevation candidate changes the truth, so step 3 runs and an iteration
costs a full latitude sweep (~1 h on a 1 600-satellite case at
screening depth). A cap candidate in its FREE regime -- at or above the
concurrency the operation actually uses -- changes no link, so the truth
is frozen, step 3 is skipped, and the iteration costs only a mask export
plus an E1 sweep (minutes). The cheapest legitimate dB is therefore also
the cheapest to search for.

**Completion.** At the end the loop should emit all three artefacts
together -- profile, R set, mask -- which is step 8 of the workflow
above, today performed by hand in three windows.

**Scope.** Only Class D parameters (declarable, with a binding format
field) may enter this loop: the exclusion angle first, then minimum
elevation and the per-cell cap, which have the same shape. Class T
parameters -- selection policy, demand, activity, duty cycle -- can
never enter it: no field binds them, so tightening on them would be
fitting the declaration to the verdict rather than to a commitment.

**Order.**

1. Mask inheritance of the per-latitude table in the envelope sampler,
   gated per direction by the ground point's latitude.
2. Range-driven per-latitude synthesis with the service tolerance,
   replacing the global walk for this lever.
3. Read-rule-aware row placement, so the interpolant stays inside the
   enforced values.

## Decisions taken

- Limits are hand-entered points in stage B (BR limits DB later).
- The victim geometry is explicit (ES lon, GSO offset), not a hidden
  worst-case search — the sweep is over latitude only; a longitude sweep
  can be added the same way if it proves necessary.
- The scene's exclusion ring stays global in the composition (the
  per-latitude exclusion is enforced by the scheduler's gates); a
  per-latitude scene ring would need the advanced-rings dialog model and
  is deferred.
- The advisor's loop is a linear alpha walk, not a bisection — because
  **the walk produces an output**: the margin-versus-declared-alpha
  trajectory is part of the loop's deliverable, and bisection would
  sample it sparsely. (Predictable run count is a side benefit, not the
  reason — recorded this way per the debate's closing concession, since
  the cost argument alone would not survive long sweep durations.)
- The advisor's found exclusion is **written back into the profile and
  thence enforced by the scheduler — by design and as an invariant**, not
  a convenience: declared zone and flown zone are one number. If the
  write-back ever became advisory, the two could diverge and the
  projection margin would degenerate into measuring the divergence
  (debate follow-up, Q8 concession — the mechanism is the load-bearing
  part).
- The downlink footprint source is an explicit choice in the profile:
  *beam composition* (the live shaped beams — the loop verifies the real
  system) or *PFD mask* (a declared mask XML — the loop then runs the
  examination's own §D5.1.4.1 down algorithm: nearest-latitude table
  read per §D5.1.5, exclusion-zone/minimum-elevation operating gate with
  the main-beam exception, MAX_CO_FREQ cap, MIN_ANGLE_AT_ES thinning —
  the direct check of what the examination will compute from the
  filing). The advisor's alpha walk under the mask source tightens the
  declared zone while the mask file stays fixed, exactly as the
  examination treats them. No epfd(is) byproduct under the mask source
  (it needs the e.i.r.p. masks); MIN_OPERATING_HEIGHT and the dual time
  step are not modelled.

## Open points

- Worst-case victim geometry: re-scoped per the simulation debate — the
  S.1503 formalism has no absolute longitude (the pfd mask is a function
  of satellite latitude and relative geometry), so an ES-longitude sweep
  would re-measure the same geometry. The parameter that genuinely needs
  exploring is the **GSO offset** from the earth station, already an
  input; a sweep over it (not over ES longitude) is the candidate
  addition.
- epfd(up)/(is) compliance sweeps: same machinery, different victim and
  limits; add after the downlink loop settles.
- ~~Limits-database loading through the vendored `radlimits` reader.~~
  Done (post-debate): `LimitsDbReader` (core) mirrors the radians
  reader's calling pattern over the vendored interop; the compliance
  window loads rows and fills the limit text with the chosen one; check
  V29 is the hand-entry cross-check (loaded row -> rendered text ->
  parsed back, exact). Lat-dependent short-term rows are surfaced for
  hand transcription, not auto-filled — and (closing-notes flag) any
  Article 22 table whose short-term limit is latitude-dependent needs a
  lat-aware limits path before it can be *verified* rather than
  transcribed.
- Payload power budget: **contingent, not closed** — the flown-power
  measurement (compare simultaneous resolved power against payload
  capability) settles whether the *truth* side needs the constraint;
  the *declaration* side (envelope basis under a budget) belongs to the
  filed-mask-basis decision and is decided there (debate Q1/Q2).
- The exported mask does not yet inherit per-latitude exclusion: the R
  set can carry per-latitude alpha rows (scheduler-enforced) while the
  scene ring is global — at tightened latitudes the mask then describes
  radiation the operation never produces. Valid (mask >= truth holds)
  but not minimal, and a margin figure computed from such a profile
  carries this deferred feature as part of its gap — name it explicitly
  until the scene/export gates each sampled ground point by
  alpha0(ground-point latitude) (debate follow-up, new finding).
  **Guarded** (closing notes): `OperationComposer.PerLatExclusionSceneGap`
  fires a warning in the compliance and simulation windows whenever a
  profile carries per-latitude rows the scene cannot express, and V28
  pins it — the misleading-figure state cannot be entered unknowingly.
- **SUPERSEDED by the v2 design below (same day)** — kept for the
  scene-gating coupling, which the v2 inherits unchanged. Original item:
  **OPEN (operator, 2026-09-01): per-latitude alpha table from the
  global walk.** The advisor's linear walk already verdicts every
  latitude at every swept alpha, so a per-latitude minimal-alpha table
  (smallest walked alpha at which each latitude's row passes) falls out
  of the existing run at no extra simulation cost. Not implemented yet;
  kept open together with the scene/export ground-latitude gating item
  above — writing per-latitude rows into the enforced profile before the
  scene can express them would trade the global over-constraint for the
  mask gap. Candidate intermediate: emit the table as *advice only*
  (UI/CSV; Apply keeps writing the global alpha), quantifying how much
  each latitude is over-constrained without changing the enforced truth.
  Two correctness caveats when it lands: per-latitude monotonicity is
  not guaranteed (a larger exclusion can worsen a geometry — the trend
  machinery exists for exactly this), so a row's alpha must stay passing
  for the rest of the walk, not just first-cross; and MIN_EXCLUDE is
  read by linear interpolation between rows, so latitudes between the
  sweep grid carry interpolated declarations the sweep never verdicted —
  a filing-grade table wants midpoint verdict passes too.
- **OPEN (operator, 2026-09-01): advisor-parameter taxonomy.** Which
  other global parameters the sweep/advisor machinery could serve, by
  tier. The test any axis must pass is the alpha test: a single scalar
  the sweep re-runs at, a discipline or capability the real system can
  genuinely operate at, and a found value written back into enforced
  operation — never a paper fit. Tier 1, walkable disciplines (the
  existing walk as-is): MinElevDeg (also re-sizes the beam lattice, so
  possibly non-monotone — the trend machinery covers it), NcoPerCell /
  MaxCoFreqSat (integer walk); the separation angles qualify formally
  but are weak levers for epfd(down). Tier 2, linear capability bounds
  (no walk needed — one sweep's worst margin is the answer):
  TxEirpDbw (**now reported** as the power-headroom line),
  PatternFloorDbi (same dB-for-dB family at the far-off-axis end).
  Tier 3, price-only, never advise (doctrine): ActivityFactor,
  IlluminationDutyCycle, DemandLinks, service cell/coverage — demand
  and realism parameters come from measurement; the loop may A/B their
  dB worth (that IS the granularity study), not tune them to pass.
  Victim-side axes (GSO offset — separately re-scoped — ES longitude,
  dish) are sweep grid dimensions, not advised parameters. The
  multi-knob form is the tracked (power, alpha) frontier, affordable
  precisely because the power axis is linear.

## Checks

- V22: profile codec round-trip; composition mirrors every field
  (gates, scene, cells, policy); a profile-driven derive runs end to end.
- V23 (stage B): sweep table against permissive limits (all pass) and
  impossible limits (all fail, negative margins), verdicts consistent
  with `CompareWithLimits`.
- V24 (stage C): the advisor terminates, returns the first compliant
  alpha under permissive limits, and reports failure when the cap is
  reached.
