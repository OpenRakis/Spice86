namespace Spice86.Core.Emulator.OperatingSystem;

using Spice86.Core.Emulator.OperatingSystem.Enums;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

public sealed class DosCodePageState {
    private const ushort DefaultSystemCodePage = 437;
    internal const ushort UseCurrentSystemCodePage = 0xFFFF;

    private static readonly Dictionary<string, DosLocaleDefinition> ExactCultureCodePages = new(StringComparer.OrdinalIgnoreCase) {
        ["en-US"] = new(CountryId.UnitedStates, 437),
        ["en-GB"] = new(CountryId.UnitedKingdom, 437),
        ["fr-FR"] = new(CountryId.France, 850),
        ["de-DE"] = new(CountryId.Germany, 850),
        ["es-ES"] = new(CountryId.Spain, 850),
        ["it-IT"] = new(CountryId.Italy, 850),
        ["pl-PL"] = new(CountryId.Poland, 852),
        ["cs-CZ"] = new(CountryId.CzechSlovak, 852),
        ["hu-HU"] = new(CountryId.Hungary, 852),
        ["he-IL"] = new(CountryId.Israel, 862),
        ["tr-TR"] = new(CountryId.Turkey, 857),
        ["ru-RU"] = new(CountryId.Russia, 866)
    };

    private static readonly Dictionary<string, DosLocaleDefinition> LanguageCodePages = new(StringComparer.OrdinalIgnoreCase) {
        ["en"] = new(CountryId.UnitedStates, 437),
        ["fr"] = new(CountryId.France, 850),
        ["de"] = new(CountryId.Germany, 850),
        ["es"] = new(CountryId.Spain, 850),
        ["it"] = new(CountryId.Italy, 850),
        ["pl"] = new(CountryId.Poland, 852),
        ["cs"] = new(CountryId.CzechSlovak, 852),
        ["hu"] = new(CountryId.Hungary, 852),
        ["he"] = new(CountryId.Israel, 862),
        ["tr"] = new(CountryId.Turkey, 857),
        ["ru"] = new(CountryId.Russia, 866)
    };

    static DosCodePageState() {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public DosCodePageState() : this(ResolveCurrentCulture()) {
    }

    public DosCodePageState(ushort codePage, CountryId country) : this(codePage, DefaultSystemCodePage, country) {
    }

    public DosCodePageState(ushort activeCodePage, ushort systemCodePage, CountryId country) {
        ActiveCodePage = activeCodePage;
        SystemCodePage = systemCodePage;
        Country = country;
        CurrentEncoding = ResolveEncoding(activeCodePage);
    }

    private DosCodePageState(DosLocaleDefinition locale) : this((ushort)locale.CodePage, DefaultSystemCodePage, locale.Country) {
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
        string cultureName = currentCulture.Name;
        if (!string.IsNullOrWhiteSpace(cultureName) && TryResolve(ExactCultureCodePages, cultureName, out DosLocaleDefinition exactCulture)) {
            return exactCulture;
        }

        string twoLetterLanguageName = currentCulture.TwoLetterISOLanguageName;
        if (!string.IsNullOrWhiteSpace(twoLetterLanguageName) && TryResolve(LanguageCodePages, twoLetterLanguageName, out DosLocaleDefinition languageCulture)) {
            return languageCulture;
        }

        return new DosLocaleDefinition(CountryId.UnitedStates, 437);
    }

    private static bool TryResolve(Dictionary<string, DosLocaleDefinition> source, string key, out DosLocaleDefinition definition) {
        if (source.TryGetValue(key, out definition) && DosSupportedCodePages.Contains(definition.CodePage)) {
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