using System.Windows;
using System.Windows.Controls;

namespace UIMarkerEditor.Controls;

public partial class SegmentedSwitchControl : UserControl
{
    private bool isUpdatingSelection;

    public SegmentedSwitchControl()
    {
        InitializeComponent();
        UpdateSelection();
    }

    public static readonly DependencyProperty IsLeftSelectedProperty =
        DependencyProperty.Register(
            nameof(IsLeftSelected),
            typeof(bool),
            typeof(SegmentedSwitchControl),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnIsLeftSelectedChanged));

    public bool IsLeftSelected
    {
        get => (bool)GetValue(IsLeftSelectedProperty);
        set => SetValue(IsLeftSelectedProperty, value);
    }

    public static readonly DependencyProperty LeftTextProperty =
        DependencyProperty.Register(nameof(LeftText), typeof(string), typeof(SegmentedSwitchControl), new PropertyMetadata(string.Empty));

    public string LeftText
    {
        get => (string)GetValue(LeftTextProperty);
        set => SetValue(LeftTextProperty, value);
    }

    public static readonly DependencyProperty RightTextProperty =
        DependencyProperty.Register(nameof(RightText), typeof(string), typeof(SegmentedSwitchControl), new PropertyMetadata(string.Empty));

    public string RightText
    {
        get => (string)GetValue(RightTextProperty);
        set => SetValue(RightTextProperty, value);
    }

    public static readonly DependencyProperty LeftToolTipProperty =
        DependencyProperty.Register(nameof(LeftToolTip), typeof(object), typeof(SegmentedSwitchControl));

    public object? LeftToolTip
    {
        get => GetValue(LeftToolTipProperty);
        set => SetValue(LeftToolTipProperty, value);
    }

    public static readonly DependencyProperty RightToolTipProperty =
        DependencyProperty.Register(nameof(RightToolTip), typeof(object), typeof(SegmentedSwitchControl));

    public object? RightToolTip
    {
        get => GetValue(RightToolTipProperty);
        set => SetValue(RightToolTipProperty, value);
    }

    private static void OnIsLeftSelectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SegmentedSwitchControl control)
        {
            control.UpdateSelection();
        }
    }

    private void LeftOption_RadioButton_Checked(object sender, RoutedEventArgs e)
    {
        if (isUpdatingSelection) return;
        IsLeftSelected = true;
    }

    private void RightOption_RadioButton_Checked(object sender, RoutedEventArgs e)
    {
        if (isUpdatingSelection) return;
        IsLeftSelected = false;
    }

    private void UpdateSelection()
    {
        isUpdatingSelection = true;
        try
        {
            LeftOption_RadioButton.IsChecked = IsLeftSelected;
            RightOption_RadioButton.IsChecked = !IsLeftSelected;
        }
        finally
        {
            isUpdatingSelection = false;
        }
    }
}
