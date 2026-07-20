using RMC.TotalRisk.Core.Interfaces;

namespace RMC.TotalRisk.Core.Enums
{
    /// <summary>
    /// The role a risk-graph node plays in a component's compute chain, surfaced by
    /// <see cref="IRiskElement.ElementType"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Mirrors the Hydrologics <c>ElementType</c> contract on <c>IBasinElement</c>. Elements carry
    /// no canonical hash of their own — seeding identity rides the projected component/failure-mode
    /// content — so this discriminator is topology metadata and is never serialized.
    /// </para>
    /// <para>
    /// Declared in compute-chain order: a chain begins at a hazard, may pass through transforms,
    /// reaches a response, and terminates in consequences.
    /// </para>
    /// </remarks>
    public enum RiskElementType
    {
        /// <summary>A hazard source: the frequency curve a chain integrates over.</summary>
        Hazard,

        /// <summary>A transform: maps one hazard axis onto another (for example, flow to stage).</summary>
        Transform,

        /// <summary>A response: the conditional failure probability given the incoming hazard.</summary>
        Response,

        /// <summary>A consequence: the outcome measure evaluated at the incoming hazard.</summary>
        Consequence,
    }
}
