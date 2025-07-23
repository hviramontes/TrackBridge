using System.Collections.Generic;

namespace TrackBridge
{
    /// <summary>
    /// Represents simple filter criteria for entities in the TrackBridge app.
    /// You can extend this class later to support more advanced filtering.
    /// </summary>
    public partial class FilterSettings
    {
        /// <summary>
        /// List of entity domains to allow (e.g., "Land", "Air").
        /// </summary>
        public List<string> AllowedDomains { get; set; } = new List<string>();

        /// <summary>
        /// List of entity kinds to allow (e.g., "Friendly", "Hostile").
        /// </summary>
        public List<string> AllowedKinds { get; set; } = new List<string>();

        /// <summary>
        /// Whether only "Publish=true" tracks should be sent.
        /// </summary>
        public bool PublishOnly { get; set; } = false;
    }
}
