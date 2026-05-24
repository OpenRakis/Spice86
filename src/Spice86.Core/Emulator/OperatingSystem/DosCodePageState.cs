namespace Spice86.Core.Emulator.OperatingSystem;

using Spice86.Core.Emulator.OperatingSystem.Enums;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

public sealed class DosCodePageState {
    private const ushort DefaultSystemCodePage = 437;
    internal const ushort UseCurrentSystemCodePage = 0xFFFF;
    private static readonly EncodingProvider s_registeredEncodingProvider = RegisterEncodingProvider();

    private static readonly Dictionary<string, DosLocaleDefinition> CultureCodePages = new(StringComparer.OrdinalIgnoreCase) {
        ["ar"] = new(CountryId.Arabic, 864),
        ["ar-AE"] = new(CountryId.Arabic, 864),
        ["ar-BH"] = new(CountryId.Arabic, 864),
        ["ar-DZ"] = new(CountryId.Arabic, 864),
        ["ar-EG"] = new(CountryId.Arabic, 864),
        ["ar-IQ"] = new(CountryId.Arabic, 864),
        ["ar-JO"] = new(CountryId.Arabic, 864),
        ["ar-KW"] = new(CountryId.Arabic, 864),
        ["ar-LB"] = new(CountryId.Arabic, 864),
        ["ar-LY"] = new(CountryId.Arabic, 864),
        ["ar-MA"] = new(CountryId.Arabic, 864),
        ["ar-OM"] = new(CountryId.Arabic, 864),
        ["ar-QA"] = new(CountryId.Arabic, 864),
        ["ar-SA"] = new(CountryId.Arabic, 864),
        ["ar-SY"] = new(CountryId.Arabic, 864),
        ["ar-TN"] = new(CountryId.Arabic, 864),
        ["ar-YE"] = new(CountryId.Arabic, 864),
        ["be"] = new(CountryId.Belarus, 866),
        ["be-BY"] = new(CountryId.Belarus, 866),
        ["bg"] = new(CountryId.Bulgaria, 855),
        ["bg-BG"] = new(CountryId.Bulgaria, 855),
        ["bs"] = new(CountryId.Bosnia, 852),
        ["bs-BA"] = new(CountryId.Bosnia, 852),
        ["bs-Cyrl"] = new(CountryId.Bosnia, 855),
        ["bs-Latn"] = new(CountryId.Bosnia, 852),
        ["ca"] = new(CountryId.Spain, 850),
        ["ca-ES"] = new(CountryId.Spain, 850),
        ["cs"] = new(CountryId.CzechSlovak, 852),
        ["cs-CZ"] = new(CountryId.CzechSlovak, 852),
        ["da"] = new(CountryId.Denmark, 865),
        ["da-DK"] = new(CountryId.Denmark, 865),
        ["de"] = new(CountryId.Germany, 850),
        ["de-AT"] = new(CountryId.Austria, 850),
        ["de-CH"] = new(CountryId.Switzerland, 850),
        ["de-DE"] = new(CountryId.Germany, 850),
        ["el"] = new(CountryId.Greece, 869),
        ["el-GR"] = new(CountryId.Greece, 869),
        ["en"] = new(CountryId.UnitedStates, 437),
        ["en-AU"] = new(CountryId.Australia, 437),
        ["en-CA"] = new(CountryId.CanadaEnglish, 863),
        ["en-GB"] = new(CountryId.UnitedKingdom, 437),
        ["en-US"] = new(CountryId.UnitedStates, 437),
        ["es"] = new(CountryId.LatinAmerica, 850),
        ["es-AR"] = new(CountryId.Argentina, 850),
        ["es-BO"] = new(CountryId.LatinAmerica, 850),
        ["es-CL"] = new(CountryId.LatinAmerica, 850),
        ["es-CO"] = new(CountryId.LatinAmerica, 850),
        ["es-CR"] = new(CountryId.LatinAmerica, 850),
        ["es-DO"] = new(CountryId.LatinAmerica, 850),
        ["es-EC"] = new(CountryId.LatinAmerica, 850),
        ["es-ES"] = new(CountryId.Spain, 850),
        ["es-GT"] = new(CountryId.LatinAmerica, 850),
        ["es-HN"] = new(CountryId.LatinAmerica, 850),
        ["es-MX"] = new(CountryId.LatinAmerica, 850),
        ["es-NI"] = new(CountryId.LatinAmerica, 850),
        ["es-PA"] = new(CountryId.LatinAmerica, 850),
        ["es-PE"] = new(CountryId.LatinAmerica, 850),
        ["es-PR"] = new(CountryId.LatinAmerica, 850),
        ["es-PY"] = new(CountryId.LatinAmerica, 850),
        ["es-SV"] = new(CountryId.LatinAmerica, 850),
        ["es-UY"] = new(CountryId.LatinAmerica, 850),
        ["es-VE"] = new(CountryId.LatinAmerica, 850),
        ["et"] = new(CountryId.Estonia, 775),
        ["et-EE"] = new(CountryId.Estonia, 775),
        ["eu"] = new(CountryId.Spain, 850),
        ["eu-ES"] = new(CountryId.Spain, 850),
        ["fi"] = new(CountryId.Finland, 850),
        ["fi-FI"] = new(CountryId.Finland, 850),
        ["fo"] = new(CountryId.FaeroeIslands, 861),
        ["fo-FO"] = new(CountryId.FaeroeIslands, 861),
        ["fr"] = new(CountryId.France, 850),
        ["fr-BE"] = new(CountryId.Belgium, 850),
        ["fr-CA"] = new(CountryId.CanadianFrench, 863),
        ["fr-CH"] = new(CountryId.Switzerland, 850),
        ["fr-FR"] = new(CountryId.France, 850),
        ["gl"] = new(CountryId.Spain, 850),
        ["gl-ES"] = new(CountryId.Spain, 850),
        ["he"] = new(CountryId.Israel, 862),
        ["he-IL"] = new(CountryId.Israel, 862),
        ["hr"] = new(CountryId.Croatia, 852),
        ["hr-HR"] = new(CountryId.Croatia, 852),
        ["hu"] = new(CountryId.Hungary, 852),
        ["hu-HU"] = new(CountryId.Hungary, 852),
        ["is"] = new(CountryId.Iceland, 861),
        ["is-IS"] = new(CountryId.Iceland, 861),
        ["it"] = new(CountryId.Italy, 850),
        ["it-CH"] = new(CountryId.Switzerland, 850),
        ["it-IT"] = new(CountryId.Italy, 850),
        ["lt"] = new(CountryId.Lithuania, 775),
        ["lt-LT"] = new(CountryId.Lithuania, 775),
        ["lv"] = new(CountryId.Latvia, 775),
        ["lv-LV"] = new(CountryId.Latvia, 775),
        ["mk"] = new(CountryId.Macedonia, 855),
        ["mk-MK"] = new(CountryId.Macedonia, 855),
        ["mt"] = new(CountryId.Malta, 850),
        ["mt-MT"] = new(CountryId.Malta, 850),
        ["nb"] = new(CountryId.Norway, 865),
        ["nb-NO"] = new(CountryId.Norway, 865),
        ["nl"] = new(CountryId.Netherlands, 850),
        ["nl-BE"] = new(CountryId.Belgium, 850),
        ["nl-NL"] = new(CountryId.Netherlands, 850),
        ["nn"] = new(CountryId.Norway, 865),
        ["nn-NO"] = new(CountryId.Norway, 865),
        ["no"] = new(CountryId.Norway, 865),
        ["no-NO"] = new(CountryId.Norway, 865),
        ["pl"] = new(CountryId.Poland, 852),
        ["pl-PL"] = new(CountryId.Poland, 852),
        ["pt"] = new(CountryId.Portugal, 860),
        ["pt-BR"] = new(CountryId.Brazil, 860),
        ["pt-PT"] = new(CountryId.Portugal, 860),
        ["ro"] = new(CountryId.Romania, 852),
        ["ro-RO"] = new(CountryId.Romania, 852),
        ["ru"] = new(CountryId.Russia, 866),
        ["ru-RU"] = new(CountryId.Russia, 866),
        ["sk"] = new(CountryId.CzechSlovak, 852),
        ["sk-SK"] = new(CountryId.CzechSlovak, 852),
        ["sl"] = new(CountryId.Slovenia, 852),
        ["sl-SI"] = new(CountryId.Slovenia, 852),
        ["sq"] = new(CountryId.Albania, 852),
        ["sq-AL"] = new(CountryId.Albania, 852),
        ["sr"] = new(CountryId.Serbia, 855),
        ["sr-Cyrl"] = new(CountryId.Serbia, 855),
        ["sr-Latn"] = new(CountryId.Serbia, 852),
        ["sv"] = new(CountryId.Sweden, 850),
        ["sv-SE"] = new(CountryId.Sweden, 850),
        ["tr"] = new(CountryId.Turkey, 857),
        ["tr-TR"] = new(CountryId.Turkey, 857),
        ["uk"] = new(CountryId.Ukraine, 866),
        ["uk-UA"] = new(CountryId.Ukraine, 866)
    };

    public DosCodePageState(ushort activeCodePage, ushort systemCodePage, CountryId country) {
        EnsureEncodingProviderRegistered();
        ActiveCodePage = activeCodePage;
        SystemCodePage = systemCodePage;
        Country = country;
        CurrentEncoding = ResolveEncoding(activeCodePage);
    }

    public static DosCodePageState CreateForCurrentCulture() {
        DosLocaleDefinition locale = ResolveCurrentCulture();
        return CreateForLocale(locale);
    }

    public static DosCodePageState Create(ushort codePage, CountryId country) {
        return new DosCodePageState(codePage, DefaultSystemCodePage, country);
    }

    internal ushort ActiveCodePage { get; private set; }

    internal ushort SystemCodePage { get; }

    internal CountryId Country { get; }

    internal Encoding CurrentEncoding { get; private set; }

    internal bool TrySetActiveCodePage(ushort requestedActiveCodePage, ushort requestedSystemCodePage, out DosErrorCode errorCode) {
        if (requestedSystemCodePage != UseCurrentSystemCodePage && requestedSystemCodePage != SystemCodePage) {
            errorCode = DosErrorCode.DataInvalid;
            return false;
        }

        if (!DosSupportedCodePages.Contains(requestedActiveCodePage)) {
            errorCode = DosErrorCode.FileNotFound;
            return false;
        }

        try {
            Encoding resolvedEncoding = ResolveEncoding(requestedActiveCodePage);
            ActiveCodePage = requestedActiveCodePage;
            CurrentEncoding = resolvedEncoding;
            errorCode = DosErrorCode.NoError;
            return true;
        } catch (ArgumentException) {
            errorCode = DosErrorCode.FileNotFound;
            return false;
        } catch (NotSupportedException) {
            errorCode = DosErrorCode.FileNotFound;
            return false;
        }
    }

    private static DosLocaleDefinition ResolveCurrentCulture() {
        CultureInfo currentCulture = CultureInfo.CurrentCulture;
        while (!string.IsNullOrWhiteSpace(currentCulture.Name)) {
            if (TryResolve(currentCulture.Name, out DosLocaleDefinition resolvedCulture)) {
                return resolvedCulture;
            }

            CultureInfo parentCulture = currentCulture.Parent;
            if (string.IsNullOrWhiteSpace(parentCulture.Name) || string.Equals(parentCulture.Name, currentCulture.Name, StringComparison.OrdinalIgnoreCase)) {
                break;
            }

            currentCulture = parentCulture;
        }

        return new DosLocaleDefinition(CountryId.UnitedStates, 437);
    }

    private static DosCodePageState CreateForLocale(DosLocaleDefinition locale) {
        return new DosCodePageState((ushort)locale.CodePage, DefaultSystemCodePage, locale.Country);
    }

    private static void EnsureEncodingProviderRegistered() {
        _ = s_registeredEncodingProvider;
    }

    private static EncodingProvider RegisterEncodingProvider() {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return CodePagesEncodingProvider.Instance;
    }

    private static bool TryResolve(string key, out DosLocaleDefinition definition) {
        if (CultureCodePages.TryGetValue(key, out definition) && DosSupportedCodePages.Contains(definition.CodePage)) {
            return true;
        }

        definition = default;
        return false;
    }

    private static Encoding ResolveEncoding(int codePage) {
        return Encoding.GetEncoding(codePage);
    }

    private readonly struct DosLocaleDefinition {
        internal DosLocaleDefinition(CountryId country, int codePage) {
            Country = country;
            CodePage = codePage;
        }

        internal CountryId Country { get; }

        internal int CodePage { get; }
    }
}