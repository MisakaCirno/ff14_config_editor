using System.ComponentModel;

namespace FF14ConfigEditor.UISave
{
    /// <summary>
    /// 每一个标点的坐标数据结构。
    /// </summary>
    public class WayMarkPoint : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        // 游戏内显示时坐标会换算为浮点数，文件中以乘以 1000 后的整数保存。
        private int rawX = 0;
        private int rawY = 0;
        private int rawZ = 0;

        public int X
        {
            get { return rawX; }
            set
            {
                if (rawX != value)
                {
                    rawX = value;
                    OnPropertyChanged(nameof(X));
                    OnPropertyChanged(nameof(FloatX));
                }
            }
        }
        public int Y
        {
            get { return rawY; }
            set
            {
                if (rawY != value)
                {
                    rawY = value;
                    OnPropertyChanged(nameof(Y));
                    OnPropertyChanged(nameof(FloatY));
                }
            }
        }
        public int Z
        {
            get { return rawZ; }
            set
            {
                if (rawZ != value)
                {
                    rawZ = value;
                    OnPropertyChanged(nameof(Z));
                    OnPropertyChanged(nameof(FloatZ));
                }
            }
        }

        public float FloatX
        {
            get { return rawX / 1000f; }
            set { X = ConvertFloatCoordinate(value); }
        }
        public float FloatY
        {
            get { return rawY / 1000f; }
            set { Y = ConvertFloatCoordinate(value); }
        }
        public float FloatZ
        {
            get { return rawZ / 1000f; }
            set { Z = ConvertFloatCoordinate(value); }
        }

        private static int ConvertFloatCoordinate(float value)
        {
            if (!WayMarkCoordinateConverter.TryRoundToRawCoordinate(value, out int rawCoordinate))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "坐标必须是有限数，且转换后的 raw 坐标必须在 Int32 范围内。");
            }

            return rawCoordinate;
        }
    }
}
