using System;
using System.Collections.Generic;
using RMC.TotalRisk.Core.Enums;

namespace RMC.TotalRisk.Systems.Components
{
    /// <summary>
    /// The structural layout of a component's end states for the cascade combination semantics
    /// (arch doc §7.9): which projected failure modes form mutually-exclusive state groups, which
    /// stand alone, which are failure states versus claimed non-failure states, and which
    /// Non-Fail sibling pairs each failure state's excess counterfactual.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Built once per run from the frozen projection snapshot (and on demand for validation) out
    /// of each mode's stamped <see cref="FailureMode.ProjectedResponseOrdinals"/> and stage
    /// polarities — pure structure, no sampling. A <b>leaf signature</b> is the ordered
    /// (response ordinal, polarity) pair sequence along a mode's path. Terminals sharing a first
    /// response element with <i>distinct</i> signatures form one exclusive state group (their
    /// events are disjoint by construction — they diverge at a shared response via opposite
    /// ports); duplicate and prefix-nested signatures leave the partition and combine as
    /// standalone units under the ambient <see cref="Core.Enums.FailureModeMethod"/> (the Q2
    /// ruling, user decision 2026-07-24 — exactly the legacy fan-out semantics). Classification
    /// is final-stage polarity (§7.9.2): Fail-final modes are failure states; Non-Fail-final
    /// modes are claimed non-failure states that ride the complement. Chain-authored modes carry
    /// no ordinals and behave as standalone Fail-final units — every pre-6.7 model produces the
    /// trivial layout, whose combination kernels are byte-for-byte the pre-cascade paths.
    /// </para>
    /// <para>
    /// Public because consuming layers legitimately need the same structure the engine combines
    /// with — group membership for diagram and result displays, the combination dimension for
    /// correlation-matrix editors — and deriving it independently would invite drift.
    /// </para>
    /// </remarks>
    public sealed class EndStateGroupLayout
    {
        #region Construction

        /// <summary>
        /// Initializes the layout from its computed structure (the factory owns all derivation).
        /// </summary>
        /// <param name="stateToCombinationUnit">The per-state combination unit map.</param>
        /// <param name="combinationUnitStates">The per-unit failure-state member lists.</param>
        /// <param name="isFailureState">The per-state final-polarity classification.</param>
        /// <param name="pairingPartnerState">The per-state flipped-final sibling map.</param>
        /// <param name="claimedStateUnit">The per-state owning combination unit of each claimed state.</param>
        /// <param name="claimedStateCount">The number of claimed non-failure states in the layout.</param>
        /// <param name="claimingCascadeCount">The number of cascades carrying claimed states.</param>
        /// <param name="hasNonFailBranchFailureState">Whether any failure state rides a Non-Fail branch.</param>
        /// <param name="isTrivial">Whether the layout is the pre-6.7 shape.</param>
        private EndStateGroupLayout(int[] stateToCombinationUnit, int[][] combinationUnitStates,
            bool[] isFailureState, int[] pairingPartnerState, int[] claimedStateUnit,
            int claimedStateCount, int claimingCascadeCount, bool hasNonFailBranchFailureState, bool isTrivial)
        {
            StateToCombinationUnit = stateToCombinationUnit;
            CombinationUnitStates = combinationUnitStates;
            IsFailureState = isFailureState;
            PairingPartnerState = pairingPartnerState;
            ClaimedStateUnit = claimedStateUnit;
            ClaimedStateCount = claimedStateCount;
            ClaimingCascadeCount = claimingCascadeCount;
            HasNonFailBranchFailureState = hasNonFailBranchFailureState;
            IsTrivial = isTrivial;
        }

        /// <summary>
        /// Builds the layout from a projected failure-mode snapshot. States index the
        /// non-background modes in projection order — the same order the sampled component's
        /// failure-mode list carries.
        /// </summary>
        /// <param name="projectedModes">The projected modes (the background non-failure mode is skipped).</param>
        /// <returns>The layout.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the mode list is null.</exception>
        public static EndStateGroupLayout Build(IReadOnlyList<FailureMode> projectedModes)
        {
            if (projectedModes == null) throw new ArgumentNullException(nameof(projectedModes));

            // Collect the states (non-background modes) with their signatures. Chain-authored
            // modes carry no ordinals — synthesize unique negatives so they can never share.
            var ordinals = new List<int[]>();
            var polarities = new List<BranchPolarity[]>();
            var isFailure = new List<bool>();
            int synthetic = -1;
            for (int i = 0; i < projectedModes.Count; i++)
            {
                var mode = projectedModes[i];
                if (mode.IsNonFailureMode) continue;
                var stages = mode.ResponseStages;
                var stagePolarities = new BranchPolarity[stages.Count];
                for (int s = 0; s < stages.Count; s++)
                {
                    stagePolarities[s] = stages[s]?.BranchPolarity ?? BranchPolarity.Fail;
                }
                int[] stageOrdinals;
                if (mode.ProjectedResponseOrdinals != null && mode.ProjectedResponseOrdinals.Length == stages.Count)
                {
                    stageOrdinals = mode.ProjectedResponseOrdinals;
                }
                else
                {
                    stageOrdinals = new int[stages.Count];
                    for (int s = 0; s < stages.Count; s++)
                    {
                        stageOrdinals[s] = synthetic--;
                    }
                }
                ordinals.Add(stageOrdinals);
                polarities.Add(stagePolarities);
                isFailure.Add(stagePolarities.Length == 0 || stagePolarities[stagePolarities.Length - 1] == BranchPolarity.Fail);
            }

            int stateCount = ordinals.Count;
            var stateToUnit = new int[stateCount];
            var claimedUnit = new int[stateCount];
            var partner = new int[stateCount];
            var standalone = new bool[stateCount];
            for (int i = 0; i < stateCount; i++)
            {
                stateToUnit[i] = -1;
                claimedUnit[i] = -1;
                partner[i] = -1;
            }

            // Cascades key on the first response ordinal; membership in state order.
            var cascadeStates = new List<List<int>>();
            var cascadeByFirstOrdinal = new Dictionary<int, int>();
            for (int i = 0; i < stateCount; i++)
            {
                int first = ordinals[i].Length > 0 ? ordinals[i][0] : synthetic--;
                if (!cascadeByFirstOrdinal.TryGetValue(first, out int cascade))
                {
                    cascade = cascadeStates.Count;
                    cascadeByFirstOrdinal.Add(first, cascade);
                    cascadeStates.Add(new List<int>());
                }
                cascadeStates[cascade].Add(i);
            }

            // Within each cascade: eject duplicate-signature failure states (all copies) and
            // prefix failure states (the shorter of a nested pair) from the exclusive partition
            // (the Q2 ruling); resolve each failure state's flipped-final sibling.
            bool hasNonFailBranchFailure = false;
            foreach (var members in cascadeStates)
            {
                for (int a = 0; a < members.Count; a++)
                {
                    int i = members[a];
                    if (isFailure[i])
                    {
                        for (int s = 0; s < polarities[i].Length - 1; s++)
                        {
                            if (polarities[i][s] == BranchPolarity.NonFail)
                            {
                                hasNonFailBranchFailure = true;
                                break;
                            }
                        }
                    }
                    for (int b = a + 1; b < members.Count; b++)
                    {
                        int j = members[b];
                        int relation = CompareSignatures(ordinals[i], polarities[i], ordinals[j], polarities[j]);
                        if (relation == 0 && isFailure[i] && isFailure[j])
                        {
                            standalone[i] = true;
                            standalone[j] = true;
                        }
                        else if (relation == 1 && isFailure[i])
                        {
                            standalone[i] = true;
                        }
                        else if (relation == 2 && isFailure[j])
                        {
                            standalone[j] = true;
                        }
                        if (partner[i] < 0 && IsFlippedFinalSibling(ordinals[i], polarities[i], ordinals[j], polarities[j]))
                        {
                            if (isFailure[i] && !isFailure[j]) partner[i] = j;
                        }
                        if (partner[j] < 0 && IsFlippedFinalSibling(ordinals[j], polarities[j], ordinals[i], polarities[i]))
                        {
                            if (isFailure[j] && !isFailure[i]) partner[j] = i;
                        }
                    }
                }
            }

            // Assign combination units in state order: the cascade's exclusive unit on its first
            // exclusive failure state, a fresh standalone unit per ejected failure state. Claimed
            // states attach to their cascade's exclusive unit (for the conditional divisor) but
            // are not combination members.
            var unitStates = new List<List<int>>();
            var cascadeExclusiveUnit = new int[cascadeStates.Count];
            for (int c = 0; c < cascadeExclusiveUnit.Length; c++)
            {
                cascadeExclusiveUnit[c] = -1;
            }
            var stateCascade = new int[stateCount];
            for (int c = 0; c < cascadeStates.Count; c++)
            {
                foreach (int i in cascadeStates[c])
                {
                    stateCascade[i] = c;
                }
            }
            for (int i = 0; i < stateCount; i++)
            {
                if (!isFailure[i]) continue;
                if (standalone[i])
                {
                    stateToUnit[i] = unitStates.Count;
                    unitStates.Add(new List<int> { i });
                }
                else
                {
                    int cascade = stateCascade[i];
                    if (cascadeExclusiveUnit[cascade] < 0)
                    {
                        cascadeExclusiveUnit[cascade] = unitStates.Count;
                        unitStates.Add(new List<int>());
                    }
                    stateToUnit[i] = cascadeExclusiveUnit[cascade];
                    unitStates[cascadeExclusiveUnit[cascade]].Add(i);
                }
            }

            int claimedCount = 0;
            var claimingCascades = new HashSet<int>();
            for (int i = 0; i < stateCount; i++)
            {
                if (isFailure[i]) continue;
                claimedCount++;
                claimingCascades.Add(stateCascade[i]);
                claimedUnit[i] = cascadeExclusiveUnit[stateCascade[i]];
            }

            var units = new int[unitStates.Count][];
            bool trivial = claimedCount == 0 && unitStates.Count == stateCount;
            for (int u = 0; u < unitStates.Count; u++)
            {
                units[u] = unitStates[u].ToArray();
                if (units[u].Length != 1) trivial = false;
            }

            return new EndStateGroupLayout(stateToUnit, units, isFailure.ToArray(), partner, claimedUnit,
                claimedCount, claimingCascades.Count, hasNonFailBranchFailure, trivial);
        }

        #endregion

        #region Members

        /// <summary>
        /// The number of end states — the non-background projected modes, in projection order
        /// (the sampled component's failure-mode list order).
        /// </summary>
        public int StateCount => IsFailureState.Length;

        /// <summary>
        /// The number of combination units — the entities the failure-mode combination method
        /// operates over: exclusive state groups plus standalone failure states. This is the
        /// dimension of the combination caches, the multivariate normal, and the correlation
        /// matrix. Equals the failure-path count for every pre-6.7 layout.
        /// </summary>
        public int CombinationUnitCount => CombinationUnitStates.Length;

        /// <summary>
        /// Each failure state's combination unit; −1 for claimed non-failure states (they ride
        /// the complement, never the combination).
        /// </summary>
        public int[] StateToCombinationUnit { get; }

        /// <summary>
        /// Each combination unit's member failure states in state order — a singleton for a
        /// standalone unit, the exclusive-partition members for a state group. The unit's
        /// failure mass at a hazard level is the exact sum of its members' polarity-product
        /// weights (disjoint leaves).
        /// </summary>
        public int[][] CombinationUnitStates { get; }

        /// <summary>
        /// Each state's classification (§7.9.2): true for a Fail-final failure state, false for
        /// a Non-Fail-final claimed non-failure state.
        /// </summary>
        public bool[] IsFailureState { get; }

        /// <summary>
        /// Each failure state's excess pairing partner (§7.9.4): the state whose signature
        /// matches with the final polarity flipped — the exact "last response held"
        /// counterfactual — or −1 to fall back to the component background mode (v1.0 parity).
        /// </summary>
        public int[] PairingPartnerState { get; }

        /// <summary>
        /// Each claimed non-failure state's owning combination unit (its cascade's exclusive
        /// group, whose failure mass is the conditional divisor 1 − P_g of §7.9.5); −1 when the
        /// cascade has no exclusive failure states (the divisor is then one). −1 for failure
        /// states.
        /// </summary>
        public int[] ClaimedStateUnit { get; }

        /// <summary>
        /// The number of claimed non-failure states (Non-Fail-final terminals).
        /// </summary>
        public int ClaimedStateCount { get; }

        /// <summary>
        /// The number of cascades carrying claimed non-failure states — validation limits this
        /// to one per component (§7.9.5, the single-claiming-group scope).
        /// </summary>
        public int ClaimingCascadeCount { get; }

        /// <summary>
        /// Whether any failure state's path rides a Non-Fail branch before its final stage (an
        /// else-chain) — such a state's weight is not monotone in the hazard, which gates the
        /// competing-failures method (§7.9.6).
        /// </summary>
        public bool HasNonFailBranchFailureState { get; }

        /// <summary>
        /// Whether the layout is the pre-6.7 shape: every state is a standalone Fail-final unit
        /// (all pre-6.7 models, chain-authored modes, and legacy same-branch fan-out). The
        /// combination kernels take the byte-identical pre-cascade paths under a trivial layout.
        /// </summary>
        public bool IsTrivial { get; }

        #endregion

        #region Private Helpers

        /// <summary>
        /// Compares two leaf signatures: 0 when identical, 1 when the first is a strict prefix
        /// of the second, 2 when the second is a strict prefix of the first, −1 when they
        /// diverge (distinct leaves — the exclusive-partition case).
        /// </summary>
        /// <param name="ordinalsA">The first signature's response ordinals.</param>
        /// <param name="polaritiesA">The first signature's polarities.</param>
        /// <param name="ordinalsB">The second signature's response ordinals.</param>
        /// <param name="polaritiesB">The second signature's polarities.</param>
        /// <returns>The relation code.</returns>
        private static int CompareSignatures(int[] ordinalsA, BranchPolarity[] polaritiesA,
            int[] ordinalsB, BranchPolarity[] polaritiesB)
        {
            int shared = Math.Min(ordinalsA.Length, ordinalsB.Length);
            for (int k = 0; k < shared; k++)
            {
                if (ordinalsA[k] != ordinalsB[k] || polaritiesA[k] != polaritiesB[k]) return -1;
            }
            if (ordinalsA.Length == ordinalsB.Length) return 0;
            return ordinalsA.Length < ordinalsB.Length ? 1 : 2;
        }

        /// <summary>
        /// Determines whether the second signature is the first's flipped-final sibling: the
        /// same response ordinals throughout, the same polarities before the final stage, and
        /// the opposite final polarity (§7.9.4 — the exact counterfactual leaf).
        /// </summary>
        /// <param name="ordinalsA">The reference signature's response ordinals.</param>
        /// <param name="polaritiesA">The reference signature's polarities.</param>
        /// <param name="ordinalsB">The candidate signature's response ordinals.</param>
        /// <param name="polaritiesB">The candidate signature's polarities.</param>
        /// <returns>True when the candidate is the flipped-final sibling.</returns>
        private static bool IsFlippedFinalSibling(int[] ordinalsA, BranchPolarity[] polaritiesA,
            int[] ordinalsB, BranchPolarity[] polaritiesB)
        {
            int length = ordinalsA.Length;
            if (length == 0 || ordinalsB.Length != length) return false;
            for (int k = 0; k < length; k++)
            {
                if (ordinalsA[k] != ordinalsB[k]) return false;
                if (k < length - 1 && polaritiesA[k] != polaritiesB[k]) return false;
            }
            return polaritiesA[length - 1] != polaritiesB[length - 1];
        }

        #endregion
    }
}
