using System.Globalization;
using System.Windows.Data;
using FtpClient.Core.Models;

namespace FtpClient.App.Converters;

public sealed class FileSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null || value is not IConvertible convertible)
        {
            return "—";
        }

        var bytes = convertible.ToDouble(CultureInfo.InvariantCulture);
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:0.##} {units[unit]}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class EntryKindConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? "文件夹" : "文件";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class TransferStatusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is TransferStatus status
            ? status switch
            {
                TransferStatus.Queued => "排队中",
                TransferStatus.Running => "传输中",
                TransferStatus.Paused => "已暂停",
                TransferStatus.Completed => "已完成",
                TransferStatus.Failed => "失败",
                TransferStatus.Cancelled => "已取消",
                _ => status.ToString()
            }
            : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
