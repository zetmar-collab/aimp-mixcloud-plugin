using System;
using System.Threading;
using AIMP.SDK.Player.Extensions;
using Mixcloud.Core.Media;
using Mixcloud.Core.Urls;

namespace Mixcloud.Plugin.Extensions
{
    public sealed class MixcloudPlayerHook : IAimpExtensionPlayerHook
    {
        private readonly IMediaSource _source;

        public MixcloudPlayerHook(IMediaSource source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
        }

        public bool OnCheckURL(ref string url)
        {
            // Wywolywane przez AIMP na sciezce odtwarzania dla kazdego utworu,
            // nie tylko z Mixcloud - dlatego straznik musi byc pierwszy i tani,
            // a zaden wyjatek nie moze stad uciec w natywny kod hosta.
            try
            {
                if (MixcloudUrl.Parse(url).Kind != MixcloudUrlKind.Cloudcast) return false;

                var playable = _source.GetPlayableUrl(url, CancellationToken.None);
                if (string.IsNullOrEmpty(playable)) return false;

                url = playable;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
