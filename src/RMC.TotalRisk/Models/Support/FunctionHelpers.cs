using Numerics;
using Numerics.Data;

namespace RMC.TotalRisk.Models.Support
{
    /// <summary>
    /// Shared numerical helpers for sampled risk-function curves.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Ported from the v1.0 <c>FunctionHelpers</c> with the BinaryFormatter-based
    /// <c>GenerateSeedFromObject</c> dropped — content-based seeding now derives from
    /// <see cref="CanonicalContentHasher"/> + <see cref="SeedHelpers"/>.
    /// </para>
    /// </remarks>
    public static class FunctionHelpers
    {
        /// <summary>
        /// Forces a sampled curve to be strictly monotonic by nudging each violating ordinate one
        /// machine epsilon past its predecessor, per the curve's declared X and Y sort orders.
        /// </summary>
        /// <param name="opd">The sampled curve, mutated in place.</param>
        /// <remarks>
        /// Exact v1.0 behavior (porting-fidelity rule): the nudge is the absolute
        /// <see cref="Tools.DoubleMachineEpsilon"/>, applied per ordinate in a single forward pass,
        /// written through the <see cref="OrderedPairedData"/> indexer. Because <see cref="Ordinate"/>
        /// equality is tolerance-based (|Δ| ≤ machine epsilon compares equal) and the indexer skips
        /// tolerance-equal assignments, a genuine curve CROSSING is clamped to one epsilon past the
        /// predecessor while an exact TIE survives unrepaired — identical to v1.0, which ran against
        /// the same Numerics semantics.
        /// </remarks>
        public static void ForceMonotonic(OrderedPairedData opd)
        {
            if (opd.OrderX == SortOrder.Descending && opd.OrderY == SortOrder.Ascending)
            {
                for (int i = 1; i <= opd.Count - 1; i++)
                {
                    if (opd[i].X >= opd[i - 1].X || opd[i].Y <= opd[i - 1].Y)
                    {
                        double x = opd[i].X >= opd[i - 1].X ? opd[i - 1].X - Tools.DoubleMachineEpsilon : opd[i].X;
                        double y = opd[i].Y <= opd[i - 1].Y ? opd[i - 1].Y + Tools.DoubleMachineEpsilon : opd[i].Y;
                        opd[i] = new Ordinate(x, y);
                    }
                }
            }
            else if (opd.OrderX == SortOrder.Descending && opd.OrderY == SortOrder.Descending)
            {
                for (int i = 1; i <= opd.Count - 1; i++)
                {
                    if (opd[i].X >= opd[i - 1].X || opd[i].Y >= opd[i - 1].Y)
                    {
                        double x = opd[i].X >= opd[i - 1].X ? opd[i - 1].X - Tools.DoubleMachineEpsilon : opd[i].X;
                        double y = opd[i].Y >= opd[i - 1].Y ? opd[i - 1].Y - Tools.DoubleMachineEpsilon : opd[i].Y;
                        opd[i] = new Ordinate(x, y);
                    }
                }
            }
            else if (opd.OrderX == SortOrder.Ascending && opd.OrderY == SortOrder.Ascending)
            {
                for (int i = 1; i <= opd.Count - 1; i++)
                {
                    if (opd[i].X <= opd[i - 1].X || opd[i].Y <= opd[i - 1].Y)
                    {
                        double x = opd[i].X <= opd[i - 1].X ? opd[i - 1].X + Tools.DoubleMachineEpsilon : opd[i].X;
                        double y = opd[i].Y <= opd[i - 1].Y ? opd[i - 1].Y + Tools.DoubleMachineEpsilon : opd[i].Y;
                        opd[i] = new Ordinate(x, y);
                    }
                }
            }
            else if (opd.OrderX == SortOrder.Ascending && opd.OrderY == SortOrder.Descending)
            {
                for (int i = 1; i <= opd.Count - 1; i++)
                {
                    if (opd[i].X <= opd[i - 1].X || opd[i].Y >= opd[i - 1].Y)
                    {
                        double x = opd[i].X <= opd[i - 1].X ? opd[i - 1].X + Tools.DoubleMachineEpsilon : opd[i].X;
                        double y = opd[i].Y >= opd[i - 1].Y ? opd[i - 1].Y - Tools.DoubleMachineEpsilon : opd[i].Y;
                        opd[i] = new Ordinate(x, y);
                    }
                }
            }
        }
    }
}
