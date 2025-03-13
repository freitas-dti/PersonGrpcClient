using System.Globalization;

namespace PersonGrpcClient.Converters
{
    class NumericValidationConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return true;

            if (value is string str)
            {
                if (string.IsNullOrWhiteSpace(str))
                    return true;

                if (double.TryParse(str, out double number))
                {
                    return number <= 0;
                }
                return true;
            }
            return true;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
