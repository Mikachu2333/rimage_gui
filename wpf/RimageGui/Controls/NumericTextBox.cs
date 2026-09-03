using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RimageGui.Controls
{
    /// <summary>
    /// Text box that only ever holds a non-negative integer inside
    /// [<see cref="Minimum"/>, <see cref="Maximum"/>].
    /// </summary>
    /// <remarks>
    /// Rejecting bad keystrokes and pastes up front is what keeps the rest of the
    /// app from having to defend against unparsable numbers; the value is also
    /// clamped on blur so a partially typed number cannot survive as
    /// out-of-range. Arrow keys step the value, which is what users expect from a
    /// numeric field.
    /// </remarks>
    public class NumericTextBox : TextBox
    {
        public static readonly DependencyProperty MinimumProperty =
            DependencyProperty.Register(nameof(Minimum), typeof(int), typeof(NumericTextBox),
                new PropertyMetadata(1));

        public static readonly DependencyProperty MaximumProperty =
            DependencyProperty.Register(nameof(Maximum), typeof(int), typeof(NumericTextBox),
                new PropertyMetadata(100));

        public int Minimum
        {
            get => (int)GetValue(MinimumProperty);
            set => SetValue(MinimumProperty, value);
        }

        public int Maximum
        {
            get => (int)GetValue(MaximumProperty);
            set => SetValue(MaximumProperty, value);
        }

        public NumericTextBox()
        {
            PreviewTextInput += OnPreviewTextInput;
            PreviewKeyDown += OnPreviewKeyDown;
            LostFocus += OnLostFocus;
            DataObject.AddPastingHandler(this, OnPaste);
            TextAlignment = TextAlignment.Right;
        }

        private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !IsDigits(e.Text);
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Space would otherwise reach the text box and break parsing.
            if (e.Key == Key.Space)
            {
                e.Handled = true;
                return;
            }

            var step = e.Key == Key.Up ? 1 : e.Key == Key.Down ? -1 : 0;
            if (step == 0)
            {
                return;
            }

            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                step *= 10;
            }

            SetValueClamped(Current() + step);
            e.Handled = true;
        }

        private void OnPaste(object sender, DataObjectPastingEventArgs e)
        {
            var text = e.DataObject.GetData(DataFormats.UnicodeText) as string;
            if (!IsDigits(text))
            {
                e.CancelCommand();
            }
        }

        private void OnLostFocus(object sender, RoutedEventArgs e)
        {
            SetValueClamped(Current());
        }

        private int Current()
        {
            return int.TryParse(Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : Minimum;
        }

        private void SetValueClamped(int value)
        {
            var clamped = Math.Max(Minimum, Math.Min(Maximum, value));
            var text = clamped.ToString(CultureInfo.InvariantCulture);
            if (Text == text)
            {
                return;
            }

            Text = text;
            CaretIndex = Text.Length;
            // The binding is UpdateSourceTrigger=PropertyChanged, so assigning
            // Text is enough to push the clamped value back to the view model.
        }

        private static bool IsDigits(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            foreach (var character in value)
            {
                if (character < '0' || character > '9')
                {
                    return false;
                }
            }

            return true;
        }
    }
}
