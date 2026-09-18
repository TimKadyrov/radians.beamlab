# Annex A — Validation material for the Bureau's examination of non-GSO systems under Recommendation ITU-R S.1503-4

**Technical annex to the concept note on S.1503-4 validation material — draft, 14 September 2026**

Technical detail is in the records this note cites: the dataset design brief, the case records under `dataset/`, and the working record `docs/simulation-debate.md`.

---

## 1 Purpose

Recommendation ITU-R S.1503-4 is the method by which the Bureau examines non-GSO FSS systems against the equivalent power flux-density limits of Article 22 (Nos. 22.5C, 22.5D, 22.5F and 22.5L), under Resolution 85 (Rev.WRC-23). The Bureau's software implements the previous version, S.1503-2. The -4 version changes the inputs (a per-latitude operating-parameter set with new constraints), the downlink and uplink selection algorithms, and the worst-case geometry; the Bureau's implementation project has planned the work in nine phases.

An implementation of an examination method needs material it can be checked against. For S.1503-4 no such material exists: there is no reference tool, the filings in hand carry no known answer, and the Recommendation itself leaves several reading rules to the implementer. This note describes what has been built to supply that material, what it has measured on the way, and the decisions that remain.

## 2 The present position

**The method.** S.1503-4 is an envelope method. The pfd mask supplied under Appendix 4, Annex 2, item A.14 upper-bounds what a satellite radiates toward any point; the worst-case geometry upper-bounds the victim's exposure; the operating-parameter set (Part B, § B3.3) bounds how many satellites are counted. The examination result is therefore an upper bound on what the system does, never an estimate of it. Whether an implementation reproduces that bound cannot be told from the result alone.

**The reading rules.** The Recommendation attaches a rule to every per-latitude array — nearest row for the minimum elevation angle, the co-frequency cap and the tracking duration (§ D5.1.5, step 1), linear interpolation for the exclusion angle (Part B) — and does not say what a set filed in both the header-scalar and the array form means. Real filings mostly carry flat arrays, on which every reading gives the same answer; an implementation that reads an array wrongly is therefore invisible until an array varies. Building the producer described below surfaced this class of defect five times in one week, each in code that had passed on flat arrays.

**The consistency of a filing with itself.** A pfd mask and the exclusion angle declared beside it should describe one system. On filed material they do not always: the mask is a per-direction envelope in which the beam that produced each value has been lost, so a declared gate cannot be applied to it after the fact. The only available action is to detect the inconsistency and examine the mask as supplied.

**The consequence.** Without material of known truth, the Bureau's -4 implementation would be verified against itself and against the previous version, neither of which can say whether a varying array is read as the Recommendation intends, whether the envelope direction holds, or how conservative the examination is.

## 3 Scope

This note covers the downlink examination (§ D5.1) in the FSS bands of Article 22, on constructed systems and on the filed masks in the Bureau's reference corpus. The uplink (§ D5.2) and inter-satellite (§ D5.3) examinations are covered on the truth side only: the producer simulates them but does not implement the Recommendation's examination for them. The track-duration variant of the downlink algorithm is likewise simulated but not examined. Aggregate limits (Resolution 76) and the Res 770 post-processing are outside.

## 4 Approach

### 4.1 A producer, not a second implementation

The material comes from a producer that stands on the other side of the format from the Bureau's tool. It simulates a non-GSO system as an operator runs it — orbits, beams, a scheduler that honours the declared constraints — and derives from that system, by measurement, the filing the format can carry: the pfd mask as the envelope of every configuration the system can reach, and the operating-parameter set as the bounds the scheduler was run under. The two are two products of one construction, so they cannot describe different systems.

Three runs then give the numbers. The **truth** is the epfd the simulated system actually produces at a victim, sampled into the examination's own 0.1 dB bins (§ D7.1.2). The **examination-read** curve is the Recommendation's downlink algorithm (§ D5.1.4.1) run over the derived mask and set by the producer's own implementation of it. The difference is the **projection margin**: exactly what the format discards. The acceptance direction for any implementation is fixed by the method:

> examination result ≥ truth, at every percentile.

Below the truth is a defect — in the mask, the declaration or the implementation. Far above is valid but of little use.

### 4.2 The dataset

One constructed system — three shells covering the three orbit models of the Recommendation, 76 satellites, deliberately over-featured — emitted as ten cases, each a notice in the SNS format, a masks database with its XML sources, and expectation records:

| Case | What it exercises | What a correct implementation reproduces |
|---|---|---|
| BL-D1, BL-U1, BL-U2, BL-I1, BL-ALL | the five band and mask forms, both downlink algorithms, all three directions | the truth CDFs (48 h at 30 s), each with its 24 h prefix so every level says how far it is from converged; for BL-I1 the examination-read curve beside the truth, direction holding with a smallest gap of 10 dB |
| BL-D2 | a set filed in both the header and the array form | a rejection naming the two quantities — not an examination under any precedence |
| BL-R1 | the nearest-row read: two MIN_ELEV rows, victims half a step either side of their midpoint | FAIL at one victim, PASS at the other; interpolation or a point read fails both |
| BL-R2 | the interpolated read of MIN_EXCLUDE | the resolved angle at each victim, 8 / 10 / 12 degrees (the verdict does not discriminate, see § 4.4) |
| BL-R3 | the worst victim between the standard sweep points | the worst margin quoted with its sweep step: compliant at 10 degrees, exceeded at 5 and finer |
| BL-C1 | a declared exclusion zone beside masks that ignore it | the inconsistency detected and graded before any verdict; the conservative verdict if the examination proceeds |

Every case is a frozen, stamped triple: producer build, time, depth, and the SHA-256 of every file. A companion guide in the implementation project's repository states, per case, what the implementation is expected to report.

### 4.3 The method of work

The producer was built in a running technical debate between two independent sessions — one building, one critiquing from the Recommendation's text and the Bureau's specification — with the author deciding contested points. The record (`docs/simulation-debate.md`) carries every measured claim, its control, and the decisions taken. Two rules from that record govern what the dataset states:

- **Control with every comparative claim.** Nothing is quoted as an effect without the run it is compared with.
- **A figure is quotable only with its convergence pair.** The same statistic at two depths, both named, moving no more than 0.5 dB and never finer than the examination's 0.1 dB bin; the pair names its kind (a run and its prefix, or two independent runs). Convergence is a property of each percentile: on the dataset's 48-hour runs the body of the distribution moves 0.4 dB or less between 24 and 48 hours, the short-term end 2 to 18 dB.

### 4.4 What the work has measured

The findings below are the ones that change how the examination should be read or drafted. Each is in a cited record with its control.

1. **The derivation is exactly stable in depth; the truth is not.** The mask and set derived from a system are identical at 0.1 and 1.0 days of probing (123 431 lit cells); the truth CDF moved up to 5 dB between those depths. Artefacts are checked by identity, statistics by pairs.
2. **The declared exclusion angle is nearly inert in the downlink examination.** Pinning it anywhere from 0 to 30 degrees moves the worst margin by at most 0.9 dB, because the algorithm counts in-zone satellites in the victim's main beam regardless (§ D5.1.4.1, the main-beam step) and replaces the rest like for like under the cap. The examination charges an operator for a zone it declares and does not build into the mask through the mask's own values inside the zone — 11 to 24 dB on the constructed case. Consequence: a consistency check must precede the examination, and an implementation must be able to print the gates it resolved, since the verdict cannot show them.
3. **A boresight gate leaves no trace in a wide-beam envelope.** With cells of 450 km served from 1 200 km the mask reaches the arc whatever the declared angle; the constructed family therefore declares no exclusion zone, and a filing of that kind reads as inconsistent by geometry, not by intent. The grade vocabulary of the check — CONSISTENT, MASK TIGHTER, LIT INSIDE, SATURATED, NOT EXERCISED, DARK — is now attested on filed material for three of its grades and on a derived mask for the fourth.
4. **The elevation floor is not verifiable from a range-shaped mask by inspection.** Near-peak power thins with slant range before it reaches low elevations, with or without a floor; the check's elevation grade must not be read as a verified floor.
5. **Coarse time steps understate main-beam transients by about 20 dB** (60 s against 6 s); body percentiles are stable from 60 s. Tail levels are quotable only at the step that resolves them.
6. **On the two filed masks in the corpus** the check performs as its table says: the 1 600-satellite system's mask reads CONSISTENT at its declared 22 degrees in 109 of 179 blocks, and its clearing level on the Article 22 row lies within a few decibels of its filed power; the sun-synchronous system's mask reads MASK TIGHTER THAN DECLARED on both axes, so its declaration is overtaken by its mask and tells the examiner nothing. Neither statement is a finding on the standing of a filing: both rest on a reconstructed set and one victim configuration.
7. **The gap between the examination and the truth is the envelope’s, not the selection rules’.** The margin decomposition runs three curves on one victim grid and one comb — the truth, the examination’s selection rules (§ D5.1.4.1: elevation gate, exclusion zone, the MAX_CO_FREQ pick, MIN_ANGLE_AT_ES pruning, the main-beam always-include) applied to the truth’s own values, and the examination proper over the declared mask. On the constructed 1 600-satellite system the selection component is 0.0 dB at the deciding point of every latitude from 0 to 60 at 0.5 days (0.0 to −0.1 at 0.1 days) and removes 0.2 to 0.7 dB in the body; the envelope component is the whole gap, +7.5 to +11.6 dB by latitude at 0.5 days (+6.5 to +12.1 at 0.1). The selection the examination substitutes for the operator’s scheduler costs nothing where the verdict is decided; the price of the format is the per-direction maximum, and every granularity lever acts on that component. The zero of the decomposition is a checked invariant (with nothing declared the selection curve equals the truth bin for bin), and its first record had to be withdrawn: one live read shared across victims re-entered the scheduler out of order, which the instrument now forbids.
8. **A convergence pair firms the body of a curve and only provisionally its tail.** The truth-only sweep of the constructed system was extended on one comb from 0.5 to 1.0, 2.0 and 4.0 days. At each doubling one or two latitudes moved by 1 to 3 dB at the deciding point while the others stood, and the latitudes that stood over one doubling were not the ones that stood over the next: a maximum can only rise under extension, a resolved percentile can fall as rarer samples dilute a cluster. The examination followed the same geometry through the mask, so the gap between the two moved less than either curve (6.4 to 10.6 dB at 0 to 40 degrees at every depth from 0.5 days) and the examination stayed at or above the truth at every latitude at every depth. Consequence for the dataset: an expectation curve’s body is quotable from its pair; its short-term end is quotable only with the run length that produced it, whatever that run length is.

### 4.5 A first use outside the dataset

The producer's examination was used this week by the two sessions drafting the concept note on No. 11.32A for non-GSO systems. Its case (a) — applying the Article 22 values of an adjacent band to a non-GSO system with a geostationary victim, so that an operator who protects the arc has a route to a favourable finding — rested on an unmeasured claim. Measured on the constructed system under the limit-curve verdict rule (re-measured 18 September; the point-wise figures of 14 September were 2 to 4 dB more lenient): a system whose mask carries its declared zone clears the 22-1C rows at a boresight pfd of about −120 to −126 dB(W/(m² MHz)) depending on the victim dish, a working downlink level; the same payload with the arc lit would have to operate 18 to 32 dB lower. The protector is limited at the body of the row, the non-protector at the short-term end by the single main-beam pass inside the zone; the criterion bites hardest near the interfering shell's own inclination, where its satellites are densest — a property an examiner can anticipate from the filing. The note now carries these as measured statements with their caveats, and the same records serve the Bureau's implementation.

## 5 Relationship to other work

- **The Bureau's S.1503-4 implementation.** Its plan takes the dataset's probe cases as the acceptance tests of its parameter-resolution layer and downlink phase, and the header-versus-array question is closed by the ruling of 7 September (mutually exclusive per quantity; a set carrying both is invalid). Its track-duration engine would produce the first examination-read curves for the two track-duration cases.
- **The concept note on No. 11.32A.** § 4.5 above.
- **Working Party 4A.** The dataset's constructed cases and the read-rule probes are the kind of worked material the Recommendation's revision history lacks; the two 4A documents on mask-versus-parameter consistency (4A/937, 4A/945) are the precedent for the check in § 4.4.

## 6 Questions to be settled

1. **Distribution.** Whether the dataset is internal validation material only, or is also offered to Working Party 4A as worked examples for the next revision of S.1503. The constructed cases carry no filing's data; the two filed-mask readings do, and are stated here without identification and without a finding on standing.
2. **The examination side of the track-duration algorithm.** Two of the three downlink truth curves have no examination-read curve until a track-duration examination exists. Build it in the producer, or wait for the implementation project's phase 3 and compare then.
3. **Access to filed masks.** The SNS distribution on this side has the mask table unpopulated; the masks are held server-side. Confirming the § 4.5 mechanism on a second filed system needs one low-inclination filed mask exported from the SNS.
4. **The consistency check's elevation axis.** Whether to add a second criterion (§ 4.4, item 4), which would re-grade one existing record; and whether the Bureau's own implementation should carry the check at all, which is a question for the -4 plan.
5. **The reconstructed sets.** The corpus filings' operating parameters were reconstructed from the SNS scalars because their XML sets are not compliant. Results on them are stated as such; if the Bureau wants findings on those filings, they belong to the ordinary examination route.

## 7 The weakest leg

The examination-read curves come from the producer's own implementation of one algorithm of the Recommendation, verified against the text and against the critique session, not against the Bureau's tool, which does not yet exist for -4. The truth curves come from one constructed system with an assumed payload; their realism is a modelling choice, stated in the records, not a measurement. And the filed-mask readings rest on reconstructed parameters at one victim configuration. The dataset is strongest where it is a probe — where the answer follows from the Recommendation's text alone and the constructed system is only the vehicle — and that is where its acceptance tests sit.

## 8 Way forward

| Step | Target |
|---|---|
| Implementation project cross-reads the ten cases; discrepancies logged in the debate record | when its phases 1 and 2 land |
| Decision on the track-duration examination side (§ 6, item 2) | October 2026 |
| Second filed system with a declared zone, if a mask can be exported (§ 6, item 3) | when access is granted |
| Note to Working Party 4A on the read-rule probes, if distribution is agreed (§ 6, item 1) | first quarter 2027 |
| Aggregate and uplink examination sides in the producer | not planned; decision with § 6, item 2 |

## 9 Principal instruments referred to

**Radio Regulations:** Article 22, Nos. 22.5C, 22.5C.7, 22.5D, 22.5F, 22.5L, Tables 22-1B and 22-1C; Appendix 4, Annex 2, item A.14; No. 11.32A (§ 4.5 only).

**Resolutions:** 85 (Rev.WRC-23); 76 (Rev.WRC-23) and 770 (Rev.WRC-23) as out of scope.

**ITU-R Recommendations:** S.1503-4 (Part B § B3.3 and § B5; Part C § C1 and § C4.1; Part D §§ D5.1.4.1, D5.1.5, D5.2, D5.3, D6.4.4, D6.5.2, D7.1.2); S.1503-2; S.1428-1; S.672-4; S.1325-3 (convergence of two-body statistics, referred to in § 4.3).

**Working Party 4A documents:** 4A/653 (the case text of the constructed system's model), 4A/937, 4A/945.

**Project records:** the dataset design brief and the consumer guide (implementation project repository, `architecture/`); `dataset/*/expected/` and `dataset/*/README.md`; `docs/simulation-debate.md`; `docs/construction.html`; `docs/arc-shield-case-a.md`; the depth records `docs/compliance-steam-2*.md` (0.1, 0.5, 1.0, 2.0 and 4.0 days, and the cap-4 pair) and the decomposition records `docs/margin-decomposition-steam-2*.md` (0.1 and 0.5 days).
