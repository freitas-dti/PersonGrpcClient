using System.Globalization;

namespace PersonGrpcClient.Converters
{
    class NullableValueConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value == null)
                return string.Empty;

            return value.ToString();
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value == null)
                return null;

            string? strValue = value as string;

            if (string.IsNullOrWhiteSpace(strValue))
                return null;

            try
            {
                if (targetType == typeof(int?))
                {
                    if (int.TryParse(strValue, out int result))
                        return result;
                    return null;
                }

                if (targetType == typeof(double?))
                {
                    if (double.TryParse(strValue, out double result))
                        return result;
                    return null;
                }
            }
            catch
            {
                return null;
            }

            return null;
        }
    }
}

