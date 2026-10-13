using TheHub.Core.Bus;

namespace TheHub.Core.Services;

public record PrayerSchedule(
    DateTime Fajr,
    DateTime Sunrise,
    DateTime Dhuhr,
    DateTime Asr,
    DateTime Maghrib,
    DateTime Isha
);

/// <summary>
/// Offline astronomical prayer times calculation engine.
/// Defaults to Umm al-Qura method (Fajr 18.5 deg, Isha 90 min after Maghrib).
/// Periodically checks the current time and publishes PrayerTimeDue events to IHubBus.
/// </summary>
public class PrayerTimeService : IDisposable
{
    private readonly IHubBus _bus;
    private readonly System.Threading.Timer _timer;
    private string _lastAlertedPrayer = "";

    public double Latitude { get; set; } = 24.7136; // Riyadh default
    public double Longitude { get; set; } = 46.6753;
    public int LeadTimeMinutes { get; set; } = 5;

    public PrayerTimeService(IHubBus? bus = null)
    {
        _bus = bus ?? HubBus.Default;
        _timer = new System.Threading.Timer(CheckPrayers, null, 1000, 30000); // Check every 30 seconds
    }

    public PrayerSchedule CalculatePrayers(DateTime date)
    {
        // Astronomical algorithm for prayer times
        double d = (date.Date - new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc)).TotalDays;
        double g = NormalizeAngle(357.529 + 0.98560028 * d);
        double q = NormalizeAngle(280.459 + 0.98564736 * d);
        double l = NormalizeAngle(q + 1.915 * SinD(g) + 0.020 * SinD(2 * g));
        double e = 23.439 - 0.00000036 * d;

        // Sun declination and Equation of Time (in minutes)
        double dec = AsinD(SinD(e) * SinD(l));
        double ra = Atan2D(CosD(e) * SinD(l), CosD(l)) / 15.0;
        double eqTime = q / 15.0 - ra;

        // Solar noon in hours UTC
        double noonUtc = 12.0 - eqTime - (Longitude / 15.0);
        var timeZoneOffset = TimeZoneInfo.Local.GetUtcOffset(date).TotalHours;
        double noonLocal = noonUtc + timeZoneOffset;

        // Hour angle helper
        double GetHourAngle(double angle)
        {
            double cosH = (SinD(-angle) - SinD(Latitude) * SinD(dec)) / (CosD(Latitude) * CosD(dec));
            if (cosH > 1.0 || cosH < -1.0) return 0;
            return AcosD(cosH) / 15.0;
        }

        // Sunrise & Sunset: Sun is 0.833 degrees below horizon
        double hSunrise = GetHourAngle(0.833);
        double sunriseLocal = noonLocal - hSunrise;
        double sunsetLocal = noonLocal + hSunrise;

        // Fajr (Umm al-Qura: 18.5 degrees)
        double hFajr = GetHourAngle(18.5);
        double fajrLocal = noonLocal - hFajr;

        // Asr (Shafi'i: shadow length factor = 1)
        double asrElev = 90.0 - AtanD(1.0 + TanD(Math.Abs(Latitude - dec)));
        double cosHAsr = (SinD(asrElev) - SinD(Latitude) * SinD(dec)) / (CosD(Latitude) * CosD(dec));
        double hAsr = Math.Abs(cosHAsr) <= 1.0 ? AcosD(cosHAsr) / 15.0 : 3.0;
        double asrLocal = noonLocal + hAsr;

        // Maghrib = Sunset
        double maghribLocal = sunsetLocal;

        // Isha (Umm al-Qura: 90 minutes after Maghrib)
        double ishaLocal = maghribLocal + 1.5;

        DateTime ToDate(double hours)
        {
            var baseDate = date.Date;
            int h = (int)Math.Floor(hours);
            int m = (int)Math.Round((hours - h) * 60.0);
            if (m >= 60) { h++; m -= 60; }
            if (h >= 24) h -= 24;
            if (h < 0) h += 24;
            return baseDate.AddHours(h).AddMinutes(m);
        }

        return new PrayerSchedule(
            Fajr: ToDate(fajrLocal),
            Sunrise: ToDate(sunriseLocal),
            Dhuhr: ToDate(noonLocal),
            Asr: ToDate(asrLocal),
            Maghrib: ToDate(maghribLocal),
            Isha: ToDate(ishaLocal)
        );
    }

    private void CheckPrayers(object? state)
    {
        try
        {
            var now = DateTime.Now;
            var schedule = CalculatePrayers(now);

            var prayers = new (string Name, DateTime Time)[]
            {
                ("Fajr", schedule.Fajr),
                ("Sunrise", schedule.Sunrise),
                ("Dhuhr", schedule.Dhuhr),
                ("Asr", schedule.Asr),
                ("Maghrib", schedule.Maghrib),
                ("Isha", schedule.Isha)
            };

            foreach (var (name, time) in prayers)
            {
                var diff = time - now;
                // Within lead time or just passed
                if (diff.TotalMinutes >= 0 && diff.TotalMinutes <= LeadTimeMinutes)
                {
                    string key = $"{name}_{now:yyyyMMdd}_{time:HHmm}";
                    if (_lastAlertedPrayer != key)
                    {
                        _lastAlertedPrayer = key;
                        string timeString = time.ToString("hh:mm tt");
                        _bus.Publish(new PrayerTimeDue(name, timeString));
                    }
                    break;
                }
            }
        }
        catch { }
    }

    private static double SinD(double deg) => Math.Sin(deg * Math.PI / 180.0);
    private static double CosD(double deg) => Math.Cos(deg * Math.PI / 180.0);
    private static double TanD(double deg) => Math.Tan(deg * Math.PI / 180.0);
    private static double AsinD(double val) => Math.Asin(Math.Clamp(val, -1.0, 1.0)) * 180.0 / Math.PI;
    private static double AcosD(double val) => Math.Acos(Math.Clamp(val, -1.0, 1.0)) * 180.0 / Math.PI;
    private static double AtanD(double val) => Math.Atan(val) * 180.0 / Math.PI;
    private static double Atan2D(double y, double x) => Math.Atan2(y, x) * 180.0 / Math.PI;
    private static double NormalizeAngle(double a) => (a % 360.0 + 360.0) % 360.0;

    public void Dispose()
    {
        _timer.Dispose();
    }
}
