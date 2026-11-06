// SPDX-FileCopyrightText: 2019-2025 The DOSBox Staging Team
// SPDX-FileCopyrightText: 2002-2018 The DOSBox Team
// SPDX-License-Identifier: GPL-2.0-or-later

namespace Spice86.Core.Emulator.Devices.Sound.Tandy;

using System.Globalization;

using Microsoft.Extensions.Logging;

using Spice86.Audio.Filters;

internal static class TandyAudioSettings {
    internal static bool? ParseBoolean(string preference) => preference switch {
        "enabled" or "true" or "on" or "yes" => true,
        "disabled" or "false" or "off" or "no" or "none" => false,
        _ => null
    };

    internal static void ConfigureFilter(SoundChannel channel, string preference, ILogger logger) {
        bool? enabled = ParseBoolean(preference);
        if (enabled is null && TryConfigureCustomFilter(channel, preference)) {
            return;
        }
        if (enabled is null) {
            logger.LogWarning("{Channel}: Invalid Tandy filter '{Filter}', using on", channel.Name, preference);
        }
        if (enabled == false) {
            channel.HighPassFilter = FilterState.Off;
            channel.LowPassFilter = FilterState.Off;
            return;
        }
        channel.ConfigureHighPassFilter(3, 120);
        channel.HighPassFilter = FilterState.On;
        channel.ConfigureLowPassFilter(2, 4800);
        channel.LowPassFilter = FilterState.On;
    }

    private static bool TryConfigureCustomFilter(SoundChannel channel, string preference) {
        string[] words = preference.Split(' ');
        if (words.Length is not (3 or 6)) {
            return false;
        }
        HashSet<string> types = new(StringComparer.Ordinal);
        for (int index = 0; index < words.Length; index += 3) {
            string type = words[index];
            if (type is not ("hpf" or "lpf") || !types.Add(type) ||
                !int.TryParse(words[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int order) ||
                !int.TryParse(words[index + 2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int cutoff) ||
                order is < 1 or > 16 || cutoff <= 0) {
                return false;
            }
        }
        channel.HighPassFilter = FilterState.Off;
        channel.LowPassFilter = FilterState.Off;
        for (int index = 0; index < words.Length; index += 3) {
            int order = int.Parse(words[index + 1], CultureInfo.InvariantCulture);
            int cutoff = int.Parse(words[index + 2], CultureInfo.InvariantCulture);
            if (words[index] == "hpf") {
                channel.ConfigureHighPassFilter(order, cutoff);
                channel.HighPassFilter = FilterState.On;
            } else {
                channel.ConfigureLowPassFilter(order, cutoff);
                channel.LowPassFilter = FilterState.On;
            }
        }
        return true;
    }
}