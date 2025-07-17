namespace Spice86.Core.Emulator.OperatingSystem;

using Serilog.Events;

using Spice86.Shared.Interfaces;

public class Clock(ILoggerService loggerService) {
    private TimeSpan _timeOffset = TimeSpan.Zero;
    private TimeSpan _dateOffset = TimeSpan.Zero;
    private bool _hasTimeOffset;
    private bool _hasDateOffset;

    /// <summary>
    /// Sets the virtual time by computing the offset from the real time.
    /// </summary>
    /// <param name="hours">Hours (0-23)</param>
    /// <param name="minutes">Minutes (0-59)</param>
    /// <param name="seconds">Seconds (0-59)</param>
    /// <param name="hundredths">Hundredths of a second (0-99)</param>
    public bool SetTime(byte hours, byte minutes, byte seconds, byte hundredths)
    {
        if (hours > 23 || minutes > 59 || seconds > 59 || hundredths > 99) {
            return false;
        }
        
        TimeSpan virtualTime;
        try {
            virtualTime = new TimeSpan(0, hours, minutes, seconds, hundredths * 10);
        }
        catch (ArgumentOutOfRangeException)
        {
            loggerService.Warning("Invalid time (hours, minutes, seconds, hundredths): {}, {}, {}, {}", hours, minutes, seconds, hundredths);
            return false;
        }
        
        TimeSpan realTime = DateTime.Now.TimeOfDay;
        _timeOffset = virtualTime - realTime;
        _hasTimeOffset = true;

        if (loggerService.IsEnabled(LogEventLevel.Verbose)) {
            loggerService.Verbose("Time changed to {O}", GetVirtualDateTime());
        }

        return true;
    }

    /// <summary>
    /// Sets the virtual date by computing the offset from the real date.
    /// </summary>
    public bool SetDate(ushort year, byte month, byte day)
    {
        if (month > 12 || day > 31) {
            return false;
        }
        
        DateTime virtualDate;
        try
        {
            virtualDate = new DateTime(year, month, day);
        }
        catch (ArgumentOutOfRangeException)
        {
            loggerService.Warning("Invalid date (y-m-d): {}-{}-{}", year, month, day);
            return false;
        }
        
        DateTime realDate = DateTime.Now.Date;
        _dateOffset = virtualDate - realDate;
        _hasDateOffset = true;
        
        if (loggerService.IsEnabled(LogEventLevel.Verbose)) {
            loggerService.Verbose("Date changed to {O}", GetVirtualDateTime());   
        }

        return true;
    }

    /// <summary>
    /// Gets the current virtual time, applying the offset if one has been set.
    /// </summary>
    /// <returns>A tuple containing (hours, minutes, seconds, hundredths)</returns>
    public (byte hours, byte minutes, byte seconds, byte hundredths) GetTime()
    {
        DateTime currentTime = GetVirtualDateTime();
        TimeSpan timeOfDay = currentTime.TimeOfDay;
        
        return (
            (byte)timeOfDay.Hours,
            (byte)timeOfDay.Minutes,
            (byte)timeOfDay.Seconds,
            (byte)(timeOfDay.Milliseconds / 10)
        );
    }

    /// <summary>
    /// Gets the current virtual date, applying the offset if one has been set.
    /// </summary>
    /// <returns>A tuple containing (year, month, day, dayOfWeek)</returns>
    public (ushort year, byte month, byte day, byte dayOfWeek) GetDate()
    {
        DateTime currentDate = GetVirtualDateTime();
        
        return (
            (ushort)currentDate.Year,
            (byte)currentDate.Month,
            (byte)currentDate.Day,
            (byte)currentDate.DayOfWeek
        );
    }

    /// <summary>
    /// Gets the virtual DateTime by applying both date and time offsets to the current real time.
    /// </summary>
    /// <returns>The virtual DateTime</returns>
    public DateTime GetVirtualDateTime()
    {
        DateTime now = DateTime.Now;
        DateTime virtualDateTime = now;
        
        // Apply date offset if set
        if (_hasDateOffset)
        {
            virtualDateTime = virtualDateTime.Add(_dateOffset);
        }
        
        // Apply time offset if set
        if (_hasTimeOffset)
        {
            virtualDateTime = virtualDateTime.Add(_timeOffset);
        }
        
        return virtualDateTime;
    }

    /// <summary>
    /// Gets a value indicating whether any virtual time or date has been set.
    /// </summary>
    public bool HasOffset => _hasTimeOffset || _hasDateOffset;
}