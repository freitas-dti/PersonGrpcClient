using System.Globalization;

namespace PersonGrpcClient.Converters
{
    class NumericValidationConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string str)
            {
                // Verifica se está vazio
                if (string.IsNullOrWhiteSpace(str))
                    return true;

                // Tenta converter para número
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
