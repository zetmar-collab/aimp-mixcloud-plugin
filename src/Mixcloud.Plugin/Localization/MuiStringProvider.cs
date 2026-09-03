using System;
using AIMP.SDK.MUIManager;
using Mixcloud.Core.Localization;

namespace Mixcloud.Plugin.Localization
{
    public sealed class MuiStringProvider : IStringProvider
    {
        private readonly IAimpServiceMUI _mui;

        public MuiStringProvider(IAimpServiceMUI mui)
        {
            _mui = mui ?? throw new ArgumentNullException(nameof(mui));
        }

        public string Get(string key)
        {
            try
            {
                var value = _mui.GetValue(key);
                // Brakujacy klucz zwraca swoja nazwe: widac to od razu przy pracy
                // i nie wywala wtyczki u uzytkownika.
                return string.IsNullOrEmpty(value) ? key : value;
            }
            catch (Exception)
            {
                return key;
            }
        }
    }
}
