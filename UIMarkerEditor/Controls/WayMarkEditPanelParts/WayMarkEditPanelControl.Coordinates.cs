using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.ComponentModel;
using System.Text.Json;
using System.Globalization;
using System.Collections.ObjectModel;
using System.IO;
using FF14ConfigEditor;
using FF14ConfigEditor.UISave;

namespace UIMarkerEditor.Controls
{
    public partial class WayMarkEditPanelControl
    {
        private void Coordinate_TextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is TextBox textBox && TryGetCoordinateEditContext(textBox, out CoordinateEditContext context))
            {
                coordinateEditContexts[textBox] = context;
                coordinateAcceptedTexts[textBox] = textBox.Text;
                textBox.ToolTip ??= CoordinateInputTip;
            }
        }

        private void Coordinate_TextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is not TextBox textBox) return;

            if (!coordinateEditContexts.TryGetValue(textBox, out CoordinateEditContext context) &&
                !TryGetCoordinateEditContext(textBox, out context))
            {
                return;
            }

            coordinateEditContexts.Remove(textBox);
            CommitOrRevertCoordinateText(textBox, context);
            coordinateAcceptedTexts.Remove(textBox);
        }

        private void Coordinate_TextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || sender is not TextBox textBox) return;

            if (!coordinateEditContexts.TryGetValue(textBox, out CoordinateEditContext context) &&
                !TryGetCoordinateEditContext(textBox, out context))
            {
                return;
            }

            e.Handled = true;
            CommitOrRevertCoordinateText(textBox, context);
            textBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        }

        private void Coordinate_TextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                e.Handled = !CanApplyCoordinateText(textBox, e.Text);
                if (e.Handled)
                {
                    ShowInvalidCoordinateFeedback(textBox);
                }
            }
        }

        private void Coordinate_TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox textBox) return;
            if (coordinateTextChangeGuards.Contains(textBox)) return;
            if (!textBox.IsKeyboardFocusWithin) return;

            if (IsCoordinateEditingText(textBox.Text))
            {
                coordinateAcceptedTexts[textBox] = textBox.Text;
                return;
            }

            string fallbackText = coordinateAcceptedTexts.GetValueOrDefault(textBox, string.Empty);
            coordinateTextChangeGuards.Add(textBox);
            textBox.Text = fallbackText;
            textBox.CaretIndex = textBox.Text.Length;
            coordinateTextChangeGuards.Remove(textBox);

            ShowInvalidCoordinateFeedback(textBox);
        }

        private void Coordinate_TextBox_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (sender is not TextBox textBox) return;

            if (!e.DataObject.GetDataPresent(DataFormats.Text))
            {
                e.CancelCommand();
                ShowInvalidCoordinateFeedback(textBox);
                return;
            }

            string pastedText = e.DataObject.GetData(DataFormats.Text) as string ?? string.Empty;
            if (!CanApplyCoordinateText(textBox, pastedText))
            {
                e.CancelCommand();
                ShowInvalidCoordinateFeedback(textBox);
            }
        }

        private void CommitOrRevertCoordinateText(TextBox textBox, CoordinateEditContext context)
        {
            if (TryParseCoordinateText(textBox.Text, out int rawCoordinate))
            {
                SetCoordinateValue(context.Point, context.Axis, rawCoordinate);

                CloseCoordinateInputTipFor(textBox);
            }
            else
            {
                ShowInvalidCoordinateFeedback(textBox);
            }

            textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            textBox.CaretIndex = textBox.Text.Length;
        }

        private void RegisterCoordinateTextBoxPasteHandlers()
        {
            foreach (TextBox textBox in FindVisualChildren<TextBox>(Edit2_Grid))
            {
                DataObject.AddPastingHandler(textBox, Coordinate_TextBox_Pasting);
            }
        }

        private static bool CanApplyCoordinateText(TextBox textBox, string inputText)
        {
            string candidateText = textBox.Text.Remove(textBox.SelectionStart, textBox.SelectionLength)
                .Insert(textBox.SelectionStart, inputText);

            return IsCoordinateEditingText(candidateText);
        }

        private static bool IsCoordinateEditingText(string text)
        {
            if (string.IsNullOrEmpty(text)) return true;
            if (text.Length > MaxCoordinateTextLength) return false;

            bool hasDecimalPoint = false;
            int decimalDigitCount = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char character = text[i];
                if (char.IsDigit(character))
                {
                    if (hasDecimalPoint)
                    {
                        decimalDigitCount++;
                        if (decimalDigitCount > 3) return false;
                    }

                    continue;
                }

                if (character == '.')
                {
                    if (hasDecimalPoint) return false;
                    hasDecimalPoint = true;
                    continue;
                }

                if (character == '-' && i == 0) continue;

                return false;
            }

            return !IsCompleteCoordinateText(text) || IsCoordinateTextInRange(text);
        }

        private bool TryGetCoordinateEditContext(TextBox textBox, out CoordinateEditContext context)
        {
            context = default;
            if (currentWayMark is not WayMark wayMark) return false;

            string[] nameParts = textBox.Name.Split('_');
            if (nameParts.Length < 3) return false;

            WayMarkPoint point = nameParts[0] switch
            {
                "A" => wayMark.A,
                "B" => wayMark.B,
                "C" => wayMark.C,
                "D" => wayMark.D,
                "One" => wayMark.One,
                "Two" => wayMark.Two,
                "Three" => wayMark.Three,
                "Four" => wayMark.Four,
                _ => null!
            };
            if (point == null) return false;

            CoordinateAxis axis = nameParts[1] switch
            {
                "X" => CoordinateAxis.X,
                "Y" => CoordinateAxis.Y,
                "Z" => CoordinateAxis.Z,
                _ => (CoordinateAxis)(-1)
            };
            if (!Enum.IsDefined(axis)) return false;

            context = new CoordinateEditContext(point, axis);
            return true;
        }

        private static bool IsCompleteCoordinateText(string text)
        {
            string trimmedText = text.Trim();
            return trimmedText is not ("" or "-" or "." or "-.");
        }

        private static bool IsCoordinateTextInRange(string text)
        {
            return decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal value) &&
                value >= (decimal)MinRawCoordinate / CoordinateScale &&
                value <= (decimal)MaxRawCoordinate / CoordinateScale;
        }

        private static bool TryParseCoordinateText(string text, out int rawCoordinate)
        {
            rawCoordinate = 0;
            string trimmedText = text.Trim();
            if (!IsCompleteCoordinateText(trimmedText) || !IsCoordinateEditingText(trimmedText))
            {
                return false;
            }

            if (decimal.TryParse(trimmedText, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal value))
            {
                decimal rawValue = value * CoordinateScale;
                if (rawValue < MinRawCoordinate || rawValue > MaxRawCoordinate)
                {
                    return false;
                }

                rawCoordinate = (int)rawValue;
                return true;
            }

            return false;
        }

        private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            CloseRegionSearchIfClickedOutside(e.OriginalSource as DependencyObject);

            if (activeCoordinateInputTipTarget == null) return;

            TextBox? clickedTextBox = FindVisualParent<TextBox>(e.OriginalSource as DependencyObject);
            if (clickedTextBox != activeCoordinateInputTipTarget)
            {
                CloseActiveCoordinateInputTip();
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (activeCoordinateInputTipTarget?.IsKeyboardFocusWithin == false)
            {
                CloseActiveCoordinateInputTip();
            }
        }

        private void ShowInvalidCoordinateFeedback(TextBox textBox)
        {
            FlashInvalidCoordinateInput(textBox);
            ShowCoordinateInputTip(textBox);
        }

        private static void FlashInvalidCoordinateInput(TextBox textBox)
        {
            textBox.Background = new SolidColorBrush(Color.FromRgb(255, 225, 225));
            textBox.BorderBrush = new SolidColorBrush(Color.FromRgb(220, 60, 60));

            System.Windows.Threading.DispatcherTimer timer = new()
            {
                Interval = TimeSpan.FromMilliseconds(220)
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                textBox.ClearValue(Control.BackgroundProperty);
                textBox.ClearValue(Control.BorderBrushProperty);
            };
            timer.Start();
        }

        private void ShowCoordinateInputTip(TextBox textBox)
        {
            CloseActiveCoordinateInputTip();

            ToolTip toolTip = textBox.ToolTip as ToolTip ?? new ToolTip
            {
                Content = CoordinateInputTip
            };
            toolTip.Content = CoordinateInputTip;
            toolTip.PlacementTarget = textBox;
            textBox.ToolTip = toolTip;
            toolTip.IsOpen = true;

            activeCoordinateInputTip = toolTip;
            activeCoordinateInputTipTarget = textBox;
            activeCoordinateInputTipTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            activeCoordinateInputTipTimer.Tick += (_, _) =>
            {
                CloseActiveCoordinateInputTip();
            };
            activeCoordinateInputTipTimer.Start();
        }

        private void CloseActiveCoordinateInputTip()
        {
            activeCoordinateInputTipTimer?.Stop();
            activeCoordinateInputTipTimer = null;

            if (activeCoordinateInputTip != null)
            {
                activeCoordinateInputTip.IsOpen = false;
            }

            activeCoordinateInputTip = null;
            activeCoordinateInputTipTarget = null;
        }

        private void CloseCoordinateInputTipFor(TextBox textBox)
        {
            if (activeCoordinateInputTipTarget == textBox)
            {
                CloseActiveCoordinateInputTip();
            }
        }

        private static void SetCoordinateValue(WayMarkPoint point, CoordinateAxis axis, int rawCoordinate)
        {
            switch (axis)
            {
                case CoordinateAxis.X:
                    point.X = rawCoordinate;
                    break;
                case CoordinateAxis.Y:
                    point.Y = rawCoordinate;
                    break;
                case CoordinateAxis.Z:
                    point.Z = rawCoordinate;
                    break;
            }
        }

    }
}
