using System.Collections.Generic;

namespace Mixcloud.Core.Catalog
{
    public sealed class MixcloudListing
    {
        public string Name { get; set; } = string.Empty;
        public IReadOnlyList<MixcloudTrack> Tracks { get; set; } = new List<MixcloudTrack>();
    }
}
