using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;
using System;

namespace EZ_HeadTracker.Views
{
    public partial class SliderUserControl : UserControl
    {
        private double _sliderIncrement = .1;
        private double _sliderMinimum = .1f;
        private double _sliderMaximum = 10;

        public double SliderIncrement
        {
            get { return _sliderIncrement; }
            set
            {
                if (value > 0)
                {
                    _sliderIncrement = value;
                    slider.SmallChange = value; // update the slider small change
                }
            }
        }
        public double SliderMinimum
        {
            get { return _sliderMinimum; }
            set
            {
                if (value >= 0)
                {
                    _sliderMinimum = value;
                    slider.Minimum = value; // update the slider minimum
                }
            }
        }
        public double SliderMaximum
        {
            get { return _sliderMaximum; }
            set
            {
                if (value > _sliderMinimum)
                {
                    _sliderMaximum = value;
                    slider.Maximum = value; // update the slider maximum
                }
            }
        }

        public SliderUserControl()
        {
            InitializeComponent();
            DataContext = this;

            slider.SmallChange = _sliderIncrement;
            slider.Minimum = _sliderMinimum;
            slider.Maximum = _sliderMaximum;


            minusBtn.Click += MinusBtn_Click;
            plusBtn.Click += PlusBtn_Click;

            slider.ValueChanged += Slider_ValueChanged;
            txtBox.IsReadOnly = true; // make the text box read only
        }

        private void Slider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            txtBox.Text = slider.Value.ToString("0.0");
        }

        private void PlusBtn_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            double newValue = slider.Value + SliderIncrement;

            if (newValue > slider.Maximum)
            {
                newValue = slider.Maximum;
            }

            slider.Value = newValue;
        }
        private void MinusBtn_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            double newValue = slider.Value - SliderIncrement;

            if (newValue < slider.Minimum)
            {
                newValue = slider.Minimum;
            }

            slider.Value = newValue;
        }

        public void SetText(string text)
        {
            // set the text of the label
            txtLabel.Content = text;
        }
        public void SetSliderValue(double value)
        {
            // set the value of the slider
            slider.Value = value;
        }

        
    }
}

