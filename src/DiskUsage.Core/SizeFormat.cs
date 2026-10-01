using System.Globalization;

namespace DiskUsage.Core;

public static class SizeFormat
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly string[] Units = ["Б", "КБ", "МБ", "ГБ", "ТБ", "ПБ"];

    public static string FormatBytes(long bytes)
    {
        if (bytes < 0)
            bytes = 0;

        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        if (unit == 0)
            return bytes.ToString(Russian) + " Б";

        return value.ToString("0.#", Russian) + " " + Units[unit];
    }

    public static double Percent(long size, long totalBytes)
    {
        if (size <= 0 || totalBytes <= 0)
            return 0;

        return size * 100d / totalBytes;
    }

    public static string FormatPercent(long size, long totalBytes)
    {
        if (size <= 0 || totalBytes <= 0)
            return "0 %";

        var percent = Percent(size, totalBytes);
        if (percent < 0.1)
            return "< 0,1 %";

        return percent.ToString("0.0", Russian) + " %";
    }
}
