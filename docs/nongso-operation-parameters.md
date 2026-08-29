# Non-GSO operation: candidate input parameters beyond the S.1503-4 set

Research note for the producer side. beamlab's system description is
deliberately richer than the S.1503-4 input format (simulation spec sec. 1);
this note collects the parameters that describe how a non-GSO system
actually operates, so the description can grow deliberately rather than by
accretion. Sources: S.1503-4 Part B (the projection target -- its complete
operating-parameter set is the twelve B3.3 identifiers already modelled);
Rec. ITU-R S.1325-3 (simulation-methodology inputs: orbit perturbations,
tracking strategy, power control on range, traffic/duty cycle, polarization
isolation, atmospheric loss); Maral, Satellite Communications Systems 6e
(ch. 5.11 multibeam coverage, ch. 6.2 traffic parameters, ch. 6 access and
assignment); and current NGSO filing practice (dynamic beam pointing and
power management, cease-transmission near the GSO arc, beam hopping).

Per parameter: what it describes, why it moves the interference truth, its
status in beamlab, whether the S.1503-4 format can express it, and which
margin component it feeds (mask envelope / worst-case geometry / selection
rules / unrepresented). "Unrepresented" is the interesting bucket: it is
conservatism the examination cannot see, measurable only by simulation.

## A. Constellation and dynamics

| Parameter | Describes | epfd relevance | beamlab | S.1503-4 | Margin bucket |
|---|---|---|---|---|---|
| Deployment state (operational fraction, spares, orbit-raising cohort) | how many of N_sat actually transmit, and from which altitudes | fewer/lower emitters than declared; transfer-altitude emissions are off-shell | not modelled (all sats active on-shell) | no (N_sat is total) | unrepresented |
| Station-keeping error model | real deadband cycling vs the W_delta sweep | second-order for epfd; affects repeat-track fidelity | W_delta sweep (vendored Case 2) | keep_rnge only | selection rules |
| Attitude / yaw-steering law | body yaw vs ground-track heading (sun-pointing yaw steering rotates the layout) | rotates every footprint; the reachable envelope must cover the yaw range | heading-locked BodyYawDeg only | no | mask envelope |
| Inter-shell phasing lock | whether shells are phase-coordinated | changes aggregate-geometry statistics, not the per-sat envelope | shells independent | no | worst-case geometry |
| Disposal/decay traffic | emissions during deorbit | usually non-transmitting; declaration hygiene only | n/a | no | -- |

## B. Payload and RF

| Parameter | Describes | epfd relevance | beamlab | S.1503-4 | Margin bucket |
|---|---|---|---|---|---|
| Polarization plan and isolation | dual-CP reuse; NGSO-vs-victim mismatch | typically 0-3 dB the examination ignores (it is co-polar worst case) | not modelled | no | unrepresented |
| Channelization / carrier-to-beam plan | which carriers a beam radiates; PSD per RefBW | mask is per-RefBW PSD; carrier packing sets the true PSD ceiling | single-carrier-per-beam abstraction (power per beam) | RefBW only | mask envelope |
| Downlink power control (range / fade) | power varies with slant range and rain state | S.1325 lists "power control on range" explicitly; truth PSD sits below the fixed budget most of the time | fixed per-beam powers within budget | no | unrepresented |
| Uplink ES power control distribution | closed-loop ES eirp vs the declared ceiling | epfd(up) truth: the mask envelopes the control CEILING, operation sits lower by the control margin | ES radiate the mask base level | mask ceiling only | unrepresented |
| Adaptive coding/modulation | occupied bandwidth and PSD track link state | second-order PSD shaping | no | no | unrepresented |
| Beam hopping frame (illumination duty cycle, revisit) | time-division illumination of cells | time-averaged PSD well below peak; 30 s steps see the average only if modelled | scheduler gates beams per step; no sub-step frame | no (masks are peak) | unrepresented |
| Scan-dependent gain droop / grating lobes | phased-array Gmax rolloff and spurious lobes at wide scan | changes the composite toward victims at scan edges | UV array beams model steering; droop partially | mask captures it IF sampled | mask envelope |
| Payload power budget | total simultaneous EIRP bound | already the reason the reachable envelope is meaningful | yes (WP4) | implicit in mask | mask envelope |
| Graceful degradation (element failures) | pattern floor rises with dead elements | envelope commitment question, not typical operation | no | no | mask envelope |

## C. Operations and mitigation

| Parameter | Describes | epfd relevance | beamlab | S.1503-4 | Margin bucket |
|---|---|---|---|---|---|
| Satellite-selection strategy | highest-elevation vs max-GSO-separation vs longest-dwell | selection determines which geometries occur; arc-avoiding selection cuts epfd well below the exclusion-only bound | highest-elevation policy | only via MIN_EXCLUDE | selection rules |
| Progressive power reduction near the ring | taper vs the hard alpha gate | filing practice (dynamic power management near the arc); truth sits below the hard-gate model | hard gate (scene ring) | hard exclusion only | unrepresented |
| Cease-transmission behaviour | beams that would cross the arc shut off vs steer away | same family as above | gate = off | exclusion only | selection rules |
| Geo-fencing / service-area masks | longitude-dependent service (borders, markets) | removes emitters over regions | lat bounds only (es_lat via WP2) | ES_LAT only | selection rules |
| Gateway diversity / switchover | feeder-link site handover under rain | moves the uplink emitter set | static gateways | e_as_stn static | unrepresented |
| Time-domain coordination with other NGSO | inter-system sharing schedules | out of Article 22 scope; changes occurring set | no | no | unrepresented |

## D. Traffic and demand (Maral ch. 6.2, 6.9)

| Parameter | Describes | epfd relevance | beamlab | S.1503-4 | Margin bucket |
|---|---|---|---|---|---|
| Per-cell traffic intensity (Erlang) | offered load R_call * T_call per cell | sets how many links actually exist vs the Nco bound | DemandLinks static per cell | Nco bound only | selection rules |
| Diurnal / geographic demand profile | load follows local time and population | occurring set varies hour-by-hour; 48 h comb averages it | static | no | unrepresented |
| Burstiness / activity factor | duty cycle of an active link | time-averaged uplink PSD below the per-link level | links always on | no | unrepresented |
| Blocking objective (Erlang B) | capacity dimensioning | fixes fleet utilisation ceiling | implicit in demand | no | selection rules |
| Assignment mode (fixed / on-demand / random) | how capacity maps to users | shapes the emitter population statistics | on-demand-like greedy | no | selection rules |

## E. Earth segment

| Parameter | Describes | epfd relevance | beamlab | S.1503-4 | Margin bucket |
|---|---|---|---|---|---|
| ES spatial distribution | population-weighted, land-only vs uniform density | epfd(up) aggregate follows the real distribution, not ES_DENSITY squares | service-grid cells | ES_DENSITY/DISTANCE | worst-case geometry |
| Terminal class mix | gateway dishes vs phased-array user terminals (skew-dependent sidelobes) | different off-axis eirp toward the arc per class | one antenna family per band | one mask per ES type | mask envelope |
| Pointing error distribution | mispointing widens effective sidelobes | truth eirp toward arc above the ideal pattern | ideal pointing | inside the mask if declared | mask envelope |
| Operational min elevation | used vs declared minimum | higher operational elevation reduces arc-adjacent geometry | declared value used | MIN_ELEV | selection rules |

## F. Comparison assumptions (not system parameters)

- Polarization isolation and atmospheric/rain loss on the interference
  path: the examination is free-space co-polar (S.1503-4 spreading only);
  S.1325 lists both as optional considerations. Quantifying them is a note
  in the margin decomposition, not a model change.
- Deterministic comb vs Monte Carlo start-epoch sampling (S.1325 offers
  both): the examination is deterministic; the simulation could bound
  epoch sensitivity by re-running with dispersed initial phases.

## Priorities for beamlab

1. **Uplink power-control distribution** -- one scalar distribution per ES
   class; directly sharpens the epfd(up) truth CDF against the declared
   ceiling. Small.
2. **Yaw-steering law option** (sun-pointing yaw vs heading lock) -- the
   only listed item that can make the current reachable envelope
   NON-conservative if a real system yaws; the envelope generator must
   sweep the yaw range. Small-medium, and it is a correctness guard.
3. **Per-cell traffic intensity + activity factor** -- turns DemandLinks
   into offered Erlang load with an on/off activity model; makes the
   occurring set honest and the selection-rule margin measurable. Medium.
4. **Beam-hopping frame model** -- sub-step illumination duty cycle folded
   into the time-averaged composite (a weight per beam-step, not a finer
   comb). Medium; only worth it if the described system hops.
5. **Selection-strategy variants** (max-GSO-separation, taper near ring) --
   cheap policy alternatives on the existing scheduler; each one is a
   measurable declaration strategy per the brief's secondary output.
6. **Deployment fraction** -- a per-shell operational fraction; trivial
   model change, honest N_sat.

Items A/B/C marked "unrepresented" are the dataset's long-term value: each
is real conservatism the format cannot see, and the expectation CDFs are
where it becomes a number.
