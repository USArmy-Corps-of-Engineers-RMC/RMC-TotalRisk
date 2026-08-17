using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace RMC.TotalRisk.RiskFunctions.Responses.Trees
{
    /// <summary>
    /// An immutable in-memory snapshot of one controlled-tree subtree. Source identifiers are
    /// retained only so paste operations can remap references whose targets are inside the
    /// fragment; every pasted node receives a fresh persistent identifier.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// A fragment is an authoring artifact rather than a serializable model definition. External
    /// function references remain live references, while the fragment's owned node content is
    /// snapshotted at copy time.
    /// </para>
    /// </remarks>
    public sealed class TreeFragment
    {
        /// <summary>Initializes an immutable tree fragment.</summary>
        /// <param name="sourceRootId">The source subtree-root identifier.</param>
        /// <param name="sourceNodeIds">The source identifiers retained for internal remapping.</param>
        /// <param name="payload">The tree-kind-specific immutable snapshot payload.</param>
        internal TreeFragment(Guid sourceRootId, IEnumerable<Guid> sourceNodeIds, object payload)
        {
            if (sourceRootId == Guid.Empty)
                throw new ArgumentException("A tree fragment requires a non-empty source root id.", nameof(sourceRootId));
            if (sourceNodeIds == null) throw new ArgumentNullException(nameof(sourceNodeIds));
            _payload = payload ?? throw new ArgumentNullException(nameof(payload));

            Guid[] ids = sourceNodeIds.ToArray();
            if (ids.Length == 0 || ids[0] != sourceRootId)
                throw new ArgumentException("A tree fragment must begin with its source root id.", nameof(sourceNodeIds));
            if (ids.Any(id => id == Guid.Empty) || ids.Distinct().Count() != ids.Length)
                throw new ArgumentException("Tree-fragment source ids must be non-empty and unique.", nameof(sourceNodeIds));

            SourceRootId = sourceRootId;
            _sourceNodeIds = Array.AsReadOnly(ids);
        }

        /// <summary>The tree-kind-specific immutable snapshot payload.</summary>
        private readonly object _payload;

        /// <summary>The immutable source-node identifier view.</summary>
        private readonly ReadOnlyCollection<Guid> _sourceNodeIds;

        /// <summary>The source subtree-root identifier retained for internal reference remapping.</summary>
        public Guid SourceRootId { get; }

        /// <summary>The source identifiers in deterministic subtree pre-order.</summary>
        public IReadOnlyList<Guid> SourceNodeIds => _sourceNodeIds;

        /// <summary>The number of snapshotted nodes.</summary>
        public int NodeCount => _sourceNodeIds.Count;

        /// <summary>Requires the tree-kind-specific payload used by a controlled paste operation.</summary>
        /// <typeparam name="T">The expected internal payload type.</typeparam>
        /// <returns>The typed immutable payload.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the fragment belongs to another tree kind.</exception>
        internal T RequirePayload<T>() where T : class
        {
            return _payload as T
                ?? throw new InvalidOperationException("The tree fragment belongs to a different controlled tree type.");
        }
    }
}
