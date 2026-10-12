using System.Globalization;

namespace TaskManager.Domain.Extensions;

public static class DatetimeExtensions
{
    private const string FullThaiDateFormat = "d MMMM yyyy";
    private const string ShortThaiDateFormat = "d MMM yy";

    private static readonly CultureInfo ThaiCulture = CreateThaiCulture();

    private static readonly TimeZoneInfo BangkokTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows()
                ? "SE Asia Standard Time"
                : "Asia/Bangkok");

    /// <summary>
    /// Converts a UTC database value to Bangkok time and formats its date in
    /// Thai using the Buddhist Era, for example "8 ตุลาคม 2569".
    /// </summary>
    public static string ToFullThaiDatetime(this DateTime utcDateTime)
    {
        return ToBangkokTime(utcDateTime)
            .ToString(FullThaiDateFormat, ThaiCulture);
    }

    /// <summary>
    /// Converts a UTC database value to Bangkok time and formats its date in
    /// short Thai form, for example "8 ต.ค. 69".
    /// </summary>
    public static string ToShortThaiDatetime(this DateTime utcDateTime)
    {
        return ToBangkokTime(utcDateTime)
            .ToString(ShortThaiDateFormat, ThaiCulture);
    }

    /// <summary>
    /// Formats a date-only value in Thai using the Buddhist Era.
    /// </summary>
    public static string ToFullThaiDatetime(this DateOnly date)
    {
        return date
            .ToDateTime(TimeOnly.MinValue)
            .ToString(FullThaiDateFormat, ThaiCulture);
    }

    /// <summary>
    /// Formats a date-only value in short Thai form.
    /// </summary>
    public static string ToShortThaiDatetime(this DateOnly date)
    {
        return date
            .ToDateTime(TimeOnly.MinValue)
            .ToString(ShortThaiDateFormat, ThaiCulture);
    }

    /// <summary>Gregorian ISO value for HTML date controls and transport, never Buddhist Era.</summary>
    public static string ToIsoDateString(this DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateTime ToBangkokTime(DateTime utcDateTime)
    {
        // SQL Server datetime2 does not preserve DateTime.Kind. Values read by
        // EF Core can therefore be Unspecified even though the stored value is UTC.
        var normalizedUtc = utcDateTime.Kind switch
        {
            DateTimeKind.Utc => utcDateTime,
            DateTimeKind.Local => utcDateTime.ToUniversalTime(),
            _ => DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc)
        };

        return TimeZoneInfo.ConvertTimeFromUtc(normalizedUtc, BangkokTimeZone);
    }

    private static CultureInfo CreateThaiCulture()
    {
        var culture = new CultureInfo("th-TH");
        culture.DateTimeFormat.Calendar = new ThaiBuddhistCalendar();
        return CultureInfo.ReadOnly(culture);
    }
}
