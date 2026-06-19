using System;
using System.Globalization;
using System.Windows.Data;

namespace UIMarkerEditor
{
    public class RegionIdToNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ushort id)
            {
                // Retrieve name using MapData.GetName
                string name = MapData.GetName(id);
                // format: Map Name(Region ID)
                return $"{name}({id})";
            }
            if (value is int intId)
            {
                 // Try to cast to ushort
                 if (intId >= 0 && intId <= ushort.MaxValue)
                 {
                     ushort ushortId = (ushort)intId;
                     string name = MapData.GetName(ushortId);
                     return $"{name}({ushortId})";
                 }
            }
            return value?.ToString() ?? "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
